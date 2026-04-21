using System.Collections.Generic;
using System.Linq;
using XFoil.Core.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: GGCALC/PSILIN
// Secondary legacy source: f_xfoil/src/xoper.f :: SPECAL/SPECCL; f_xfoil/src/xfoil.f :: CPCALC/CLCALC
// Role in port: Orchestrates the direct linear-vorticity inviscid solver path used by the Newton-coupled viscous flow and parity diagnostics.
// Differences: The solver lineage is direct, but the managed implementation separates assembly, tracing, factorization, basis solves, alpha/CL specification, and pressure recovery into explicit methods and parity-aware helpers instead of one monolithic workspace routine.
// Decision: Keep the decomposed managed structure and preserve the legacy arithmetic/order inside the parity-sensitive kernel paths.
namespace XFoil.Solver.Services;

/// <summary>
/// Complete linear-vorticity inviscid solver implementing XFoil's streamfunction formulation.
/// Orchestrates panel distribution, geometry computation, influence matrix assembly,
/// LU factoring, basis solution computation, alpha/CL specification, and pressure integration.
///
/// This is a direct port of XFoil's GGCALC, SPECAL, SPECCL, QISET, CLCALC, CPCALC routines
/// in clean idiomatic C# with 0-based indexing.
/// </summary>
public static class LinearVortexInviscidSolver
{
    private const double TwoPi = 2.0 * Math.PI;

    /// <summary>
    /// Assembles the (N+1)x(N+1) streamfunction influence system, LU-factors it,
    /// and solves the two basis RHS vectors for the alpha=0 and alpha=90 unit solutions.
    /// Port of XFoil's GGCALC algorithm.
    /// </summary>
    /// <param name="panel">Panel geometry state with nodes distributed.</param>
    /// <param name="state">Inviscid solver state (matrices, workspace arrays).</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude (QINF).</param>
    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC.
    // Difference from legacy: The method breaks the original routine into explicit assembly, LU factorization, basis backsolve, and trace stages, and it exposes the parity float solve path through state flags.
    // Decision: Keep the structured orchestration because it makes the direct port auditable while retaining the original algorithmic order.
    public static void AssembleAndFactorSystem(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        int n = panel.NodeCount;
        int systemSize = AssembleSystem(panel, state, freestreamSpeed, angleOfAttackRadians);

        // Phase 1 strip: float-only path reproduces XFoil's single-precision LU.
        // The doubled tree (auto-generated *.Double.cs twin via gen-double.py)
        // rewrites `LegacyStreamfunctionInfluenceFactors`/`LegacyPivotIndices`
        // to their non-Legacy double counterparts, plus `CopyMatrixToSingle` to
        // a no-op identity copy.
        CopyMatrixToSingle(state.StreamfunctionInfluence, state.LegacyStreamfunctionInfluenceFactors, systemSize);
        ScaledPivotLuSolver.Decompose(
            state.LegacyStreamfunctionInfluenceFactors,
            state.LegacyPivotIndices,
            systemSize,
            traceContext: "basis_aij_single");

        state.IsInfluenceMatrixFactored = true;

        // Step 6: Solve both basis RHS vectors
        SolveBasisRightHandSides(state, systemSize);

        // Step 7: Copy basis surface speeds from the velocity sensitivities
        // For basis solutions, the surface speed is the tangential velocity component.
        // We need to recompute velocity sensitivities for each basis solution.
        // In XFoil, QINV0[i,0/1] = GAM0[i,0/1] -- the surface speed equals the vortex strength.
        for (int i = 0; i < n; i++)
        {
            state.BasisInviscidSpeed[i, 0] = state.BasisVortexStrength[i, 0];
            state.BasisInviscidSpeed[i, 1] = state.BasisVortexStrength[i, 1];
        }

        // Step 8: Mark as complete
        state.AreBasisSolutionsComputed = true;

        // GDB parity trace: dump GAMU and AIJ at stagnation area

    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC system-assembly block.
    // Difference from legacy: The row construction matches the legacy system, but the managed code delegates panel geometry preparation and PSILIN row evaluation to separate services.
    // Decision: Keep the split because it makes the system-assembly boundary explicit and traceable.
    private static int AssembleSystem(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        int n = panel.NodeCount;
        int systemSize = n + 1;

        // Step 1: Ensure geometry is prepared
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state, state.UseLegacyPanelingPrecision);
        PanelGeometryBuilder.ComputeNormals(panel, state.UseLegacyPanelingPrecision);
        PanelGeometryBuilder.ComputePanelAngles(panel, state, state.UseLegacyPanelingPrecision);
        TracePanelNodes(panel);

        // Zero the workspace
        for (int i = 0; i < systemSize; i++)
        {
            for (int j = 0; j < systemSize; j++)
            {
                state.StreamfunctionInfluence[i, j] = 0.0;
            }

            state.BasisVortexStrength[i, 0] = 0.0;
            state.BasisVortexStrength[i, 1] = 0.0;
        }

        // Step 2: For each airfoil node, compute influence coefficients
        for (int i = 0; i < n; i++)
        {
            StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                i,
                panel.X[i], panel.Y[i],
                panel.NormalX[i], panel.NormalY[i],
                false,
                true,
                panel, state,
                freestreamSpeed,
                angleOfAttackRadians);

            for (int j = 0; j < n; j++)
            {
                state.StreamfunctionInfluence[i, j] = state.StreamfunctionVortexSensitivity[j];
                state.SourceInfluence[i, j] = -state.StreamfunctionSourceSensitivity[j];
            }

            // Debug: dump full DZDG for i=79 (AIJ row 80 in 1-based)
            

            state.StreamfunctionInfluence[i, n] = -1.0;
            state.BasisVortexStrength[i, 0] = -freestreamSpeed * panel.Y[i];
            state.BasisVortexStrength[i, 1] = freestreamSpeed * panel.X[i];
        }

        // Debug: dump BIJ (SourceInfluence) at specific elements
        

        // GDB-parity: dump raw AIJ row 12 and RHS (=Fortran row 13) before LU
        

        // Debug: dump AIJ for comparison with Fortran
        

        // Step 3: Kutta condition (row N): gamma[0] + gamma[N-1] = 0
        for (int j = 0; j < systemSize; j++)
        {
            state.StreamfunctionInfluence[n, j] = 0.0;
        }

        state.StreamfunctionInfluence[n, 0] = 1.0;
        state.StreamfunctionInfluence[n, n - 1] = 1.0;
        state.BasisVortexStrength[n, 0] = 0.0;
        state.BasisVortexStrength[n, 1] = 0.0;

        // Step 4: Sharp TE override -- replace last airfoil-node row with bisector condition
        if (state.IsSharpTrailingEdge)
        {
        ApplySharpTrailingEdgeCondition(panel, state, freestreamSpeed, angleOfAttackRadians, n);
        }

        TraceInfluenceSystem(state, n, systemSize);

        state.IsInfluenceMatrixFactored = false;
        state.AreBasisSolutionsComputed = false;
        return systemSize;
    }

    // Legacy mapping: none; managed-only trace helper family around GGCALC basis and matrix state.
    // Difference from legacy: The original solver only emits equivalent detail in instrumented builds, while the managed port keeps structured trace helpers alongside the kernel.
    // Decision: Keep the trace-helper block because parity debugging depends on it and it does not alter solver behavior.
    private static void TracePanelNodes(LinearVortexPanelState panel)
    {
    }

    private static void TraceInfluenceSystem(InviscidSolverState state, int nodeCount, int systemSize)
    {
    }

    private static void TraceBasisEntries(
        string scope,
        string name,
        double[,] values,
        int column,
        int count,
        string precision)
    {
    }

    private static void TraceBasisEntries(
        string scope,
        string name,
        IReadOnlyList<double> values,
        string precision)
    {
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC basis back-substitution.
    // Difference from legacy: The managed port solves the two basis RHS vectors in a dedicated helper and explicitly mirrors the parity float backsolve path.
    // Decision: Keep the helper because it isolates the basis solve stage cleanly.
    private static void SolveBasisRightHandSides(InviscidSolverState state, int systemSize)
    {
        // Phase 1 strip: float-only path uses the parity float backsolve.
        // The doubled tree (auto-generated *.Double.cs twin via gen-double.py)
        // rewrites BasisRhs0Single → BasisRhs0 and the LegacyXxx fields to
        // their non-Legacy double counterparts.
        var rhs0 = XFoil.Solver.Numerics.SolverBuffers.BasisRhs0(systemSize);
        var rhs1 = XFoil.Solver.Numerics.SolverBuffers.BasisRhs1(systemSize);
        var rhs0Single = XFoil.Solver.Numerics.SolverBuffers.BasisRhs0Single(systemSize);
        var rhs1Single = XFoil.Solver.Numerics.SolverBuffers.BasisRhs1Single(systemSize);
        for (int i = 0; i < systemSize; i++)
        {
            rhs0Single[i] = (float)state.BasisVortexStrength[i, 0];
            rhs1Single[i] = (float)state.BasisVortexStrength[i, 1];
        }

        ScaledPivotLuSolver.BackSubstitute(
            state.LegacyStreamfunctionInfluenceFactors,
            state.LegacyPivotIndices,
            rhs0Single,
            systemSize,
            traceContext: "basis_gamma_alpha0_single");
        ScaledPivotLuSolver.BackSubstitute(
            state.LegacyStreamfunctionInfluenceFactors,
            state.LegacyPivotIndices,
            rhs1Single,
            systemSize,
            traceContext: "basis_gamma_alpha90_single");

        for (int i = 0; i < systemSize; i++)
        {
            rhs0[i] = rhs0Single[i];
            rhs1[i] = rhs1Single[i];
            state.BasisVortexStrength[i, 0] = rhs0Single[i];
            state.BasisVortexStrength[i, 1] = rhs1Single[i];
        }
    }

    // Legacy mapping: managed-only parity storage helper corresponding to legacy REAL matrix state.
    // Difference from legacy: Fortran already stores the matrix in REAL form; the managed port copies the double matrix into a float workspace only when replaying the legacy kernel.
    // Decision: Keep the helper because it localizes the parity-only conversion.
    private static void CopyMatrixToSingle(double[,] source, float[,] destination, int size)
    {
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                destination[row, col] = (float)source[row, col];
            }
        }
    }

    // Trace helper no-ops fully removed as part of the Phase 1 strip.

    /// <summary>
    /// Superimposes basis solutions for a given angle of attack, computes surface speeds,
    /// and integrates pressure forces. Port of XFoil's SPECAL algorithm.
    /// </summary>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <param name="panel">Panel geometry state.</param>
    /// <param name="state">Inviscid solver state with basis solutions computed.</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude.</param>
    /// <param name="machNumber">Freestream Mach number.</param>
    /// <returns>Inviscid analysis result.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL.
    // Difference from legacy: The basis superposition and force recovery follow SPECAL, but the managed code keeps the basis vectors and alpha derivatives in explicit state objects and uses explicit parity math helpers where needed.
    // Decision: Keep the clearer managed solve path while preserving the legacy alpha-superposition semantics.
    public static LinearVortexInviscidResult SolveAtAngleOfAttack(
        double alphaRadians,
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double machNumber)
    {
        int n = panel.NodeCount;

        // Step 1: Ensure basis solutions exist
        if (!state.AreBasisSolutionsComputed)
        {
            AssembleAndFactorSystem(panel, state, freestreamSpeed, alphaRadians);
        }

        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;
        double cosa = LegacyPrecisionMath.Cos(alphaRadians, useLegacyPrecision);
        double sina = LegacyPrecisionMath.Sin(alphaRadians, useLegacyPrecision);

        // Step 2: Superimpose basis solutions
        for (int i = 0; i < n; i++)
        {
            state.VortexStrength[i] = SuperimposeBasisPair(
                cosa,
                state.BasisVortexStrength[i, 0],
                sina,
                state.BasisVortexStrength[i, 1],
                useLegacyPrecision);
        }
        

        // Internal streamfunction is the N+1th entry
        state.InternalStreamfunction = SuperimposeBasisPair(
            cosa,
            state.BasisVortexStrength[n, 0],
            sina,
            state.BasisVortexStrength[n, 1],
            useLegacyPrecision);

        // Step 3: Alpha derivatives
        for (int i = 0; i < n; i++)
        {
            state.VortexStrengthAlphaDerivative[i] = SuperimposeBasisPairAlphaDerivative(
                cosa,
                state.BasisVortexStrength[i, 0],
                sina,
                state.BasisVortexStrength[i, 1],
                useLegacyPrecision);
        }

        // Step 4: Update TE vortex/source strengths
        UpdateTrailingEdgeStrengths(panel, state);

        // Step 5: Compute inviscid speed
        ComputeInviscidSpeed(alphaRadians, state, n);

        // Step 6: Compute alpha derivative of speed
        for (int i = 0; i < n; i++)
        {
            state.InviscidSpeedAlphaDerivative[i] = SuperimposeBasisPairAlphaDerivative(
                cosa,
                state.BasisInviscidSpeed[i, 0],
                sina,
                state.BasisInviscidSpeed[i, 1],
                useLegacyPrecision);
        }

        // Step 7: Integrate pressure forces (at M=0, no Mach iteration needed)
        LinearVortexInviscidResult result = IntegratePressureForces(
            panel, state, alphaRadians, machNumber, freestreamSpeed,
            0.25 * panel.Chord + panel.LeadingEdgeX,
            panel.LeadingEdgeY);


        return result;
    }

    /// <summary>
    /// Newton iteration to find the angle of attack that produces a desired CL.
    /// Port of XFoil's SPECCL algorithm.
    /// </summary>
    /// <param name="targetCl">Target lift coefficient.</param>
    /// <param name="panel">Panel geometry state.</param>
    /// <param name="state">Inviscid solver state.</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude.</param>
    /// <param name="machNumber">Freestream Mach number.</param>
    /// <returns>Inviscid analysis result at the converged alpha.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL.
    // Difference from legacy: The managed implementation uses a compact Newton loop around SolveAtAngleOfAttack instead of embedding the CL specification in the original OPER control flow.
    // Decision: Keep the direct managed root solve because it is faithful to the legacy intent and simpler to reuse.
    public static LinearVortexInviscidResult SolveAtLiftCoefficient(
        double targetCl,
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double machNumber)
    {
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;

        // Phase 2 iter 89: shift initial guess by zero-lift CL so cambered
        // airfoils start near the true root. Call SolveAtAngleOfAttack(0)
        // first to derive CL_0 (zero-lift CL = camber contribution), then
        // estimate α = (targetCl - CL_0) / (2π). This costs one extra inviscid
        // solve but makes the Newton converge reliably for cambered airfoils
        // (NACA 4412 zero-lift α≈-4°, so the previous guess α=targetCl/2π
        // started ~4° off, often outside the Newton's basin of attraction).
        var zeroResult = SolveAtAngleOfAttack(0d, panel, state, freestreamSpeed, machNumber);
        double cl0 = zeroResult.LiftCoefficient;
        double alpha = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.Subtract(targetCl, cl0, useLegacyPrecision),
            TwoPi,
            useLegacyPrecision);

        LinearVortexInviscidResult result = null!;

        for (int iter = 0; iter < 20; iter++)
        {
            result = SolveAtAngleOfAttack(alpha, panel, state, freestreamSpeed, machNumber);

            double clError = LegacyPrecisionMath.Subtract(result.LiftCoefficient, targetCl, useLegacyPrecision);
            if (LegacyPrecisionMath.Abs(clError, useLegacyPrecision) < 1e-6)
            {
                break;
            }

            double clAlpha = result.LiftCoefficientAlphaDerivative;
            if (LegacyPrecisionMath.Abs(clAlpha, useLegacyPrecision) < 1e-10)
            {
                break; // Degenerate derivative
            }

            alpha = LegacyPrecisionMath.Subtract(
                alpha,
                LegacyPrecisionMath.Divide(clError, clAlpha, useLegacyPrecision),
                useLegacyPrecision);
        }

        return result;
    }

    /// <summary>
    /// Computes inviscid surface speed from basis speed vectors via cos/sin superposition.
    /// </summary>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <param name="state">Inviscid solver state with basis speeds.</param>
    /// <param name="nodeCount">Number of panel nodes.</param>
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL basis-speed superposition.
    // Difference from legacy: The managed helper exposes the speed superposition separately so later solver stages can reuse it directly.
    // Decision: Keep the helper because it clarifies the basis-to-surface-speed step.
    public static void ComputeInviscidSpeed(double alphaRadians, InviscidSolverState state, int nodeCount)
    {
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;
        double cosa = LegacyPrecisionMath.Cos(alphaRadians, useLegacyPrecision);
        double sina = LegacyPrecisionMath.Sin(alphaRadians, useLegacyPrecision);

        for (int i = 0; i < nodeCount; i++)
        {
            state.InviscidSpeed[i] = SuperimposeBasisPair(
                cosa,
                state.BasisInviscidSpeed[i, 0],
                sina,
                state.BasisInviscidSpeed[i, 1],
                useLegacyPrecision);

            
        }

    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL/SPECCL and f_xfoil/src/xpanel.f :: QISET basis superposition.
    // Difference from legacy: The parity path keeps these as explicitly staged float products plus a float add/subtract instead of hiding the REAL assignment inside a wider helper.
    // Decision: Keep this helper family local to the inviscid alpha superposition path so the proven product-rounding pattern stays isolated from unrelated parity helpers.
    private static double SuperimposeBasisPair(
        double cosa,
        double basisAlpha0,
        double sina,
        double basisAlpha90,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The doubled tree (auto-generated
        // *.Double.cs twin via gen-double.py) gets the double-precision mirror.
        // Fortran: GAM(I) = COSA*GAMU(I,1) + SINA*GAMU(I,2). With
        // -ffp-contract=off all variables are REAL, so each multiply rounds to
        // float before the add. The wide-accumulation form (double products +
        // single rounding) gave 1 ULP drift at asymmetric alpha, propagating
        // to 128 ULP in PSILIN at wake node 6 and 3125 ULP in final CD.
        float cosaSingle = (float)cosa;
        float basisAlpha0Single = (float)basisAlpha0;
        float sinaSingle = (float)sina;
        float basisAlpha90Single = (float)basisAlpha90;
        float product1 = LegacyPrecisionMath.RoundBarrier(cosaSingle * basisAlpha0Single);
        float product2 = LegacyPrecisionMath.RoundBarrier(sinaSingle * basisAlpha90Single);
        return LegacyPrecisionMath.RoundBarrier(product1 + product2);
    }

    private static double SuperimposeBasisPairAlphaDerivative(
        double cosa,
        double basisAlpha0,
        double sina,
        double basisAlpha90,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The doubled tree (auto-generated
        // *.Double.cs twin via gen-double.py) gets the double-precision mirror.
        // Fortran: GAMU_A(I) = COSA*GAMU(I,2) - SINA*GAMU(I,1)
        float cosaSingle = (float)cosa;
        float basisAlpha0Single = (float)basisAlpha0;
        float sinaSingle = (float)sina;
        float basisAlpha90Single = (float)basisAlpha90;
        float product1 = LegacyPrecisionMath.RoundBarrier(cosaSingle * basisAlpha90Single);
        float product2 = LegacyPrecisionMath.RoundBarrier(sinaSingle * basisAlpha0Single);
        return LegacyPrecisionMath.RoundBarrier(product1 - product2);
    }

    /// <summary>
    /// Integrates surface pressures to compute CL, CM, CDP with the Karman-Tsien correction
    /// and second-order DG*DX/12 moment correction term. Port of XFoil's CLCALC algorithm.
    /// </summary>
    /// <param name="panel">Panel geometry state.</param>
    /// <param name="state">Inviscid solver state with surface speeds set.</param>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <param name="machNumber">Freestream Mach number.</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude.</param>
    /// <param name="momentRefX">Moment reference point X coordinate.</param>
    /// <param name="momentRefY">Moment reference point Y coordinate.</param>
    /// <returns>Inviscid analysis result with CL, CM, CDP, pressure coefficients.</returns>
    // Legacy mapping: f_xfoil/src/xfoil.f :: CLCALC.
    // Difference from legacy: The panelwise pressure integration is materially the same, but the managed code names each accumulation term and reuses LegacyPrecisionMath to make parity-sensitive order explicit.
    // Decision: Keep the decomposed integration because it is easier to audit and still follows CLCALC.
    public static LinearVortexInviscidResult IntegratePressureForces(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double alphaRadians,
        double machNumber,
        double freestreamSpeed,
        double momentRefX,
        double momentRefY)
    {
        int n = panel.NodeCount;
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;
        double cosa = LegacyPrecisionMath.Cos(alphaRadians, useLegacyPrecision);
        double sina = LegacyPrecisionMath.Sin(alphaRadians, useLegacyPrecision);

        // Step 1: Compute compressibility parameters
        var comp = PanelGeometryBuilder.ComputeCompressibilityParameters(machNumber);
        double beta = comp.Beta;
        double bFac = comp.KarmanTsienFactor;

        // Step 2: Compute Karman-Tsien corrected Cp at each node
        double[] cp = XFoil.Solver.Numerics.SolverBuffers.CpInviscid(n);
        ComputePressureCoefficients(state.InviscidSpeed, freestreamSpeed, machNumber, cp, n, useLegacyPrecision);

        // Cp derivatives for CL_alpha and CL_M^2.
        //
        // dCp/dM² comes from dKT/dM² on the KT-corrected pressure formula:
        //   CPG     = CGINC / (BETA + BFAC*CGINC)
        //   CPG_MSQ = -CPG / (BETA + BFAC*CGINC) * (BETA_MSQ + BFAC_MSQ*CGINC)
        // where BETA_MSQ = -0.5/BETA, BFAC_MSQ = 0.5/(1+BETA) - BFAC/(1+BETA)*BETA_MSQ.
        // Previously this block left `cpM2` unpopulated and `clMach2` hardcoded
        // to 0.0 at line 551 — restoring the Fortran CLCALC computation so
        // `LiftCoefficientMachSquaredDerivative` on the result carries a
        // meaningful value at M > 0 (needed by Tier B4 compressibility work).
        // At M=0: BETA=1, BFAC=0, BETA_MSQ=-0.5, BFAC_MSQ=0.25 → CPG_MSQ =
        // 0.5·CGINC − 0.25·CGINC², non-zero but only enters CL_MSQ which is
        // not compared by parity tests (NACA 4455 bit-exact on CL/CD/CM).
        double betaMsq = -0.5 / Math.Max(beta, 1e-12);
        double bFacMsq = 0.5 / (1.0 + beta) - bFac / (1.0 + beta) * betaMsq;

        double[] cpAlpha = XFoil.Solver.Numerics.SolverBuffers.CpAlpha(n);
        double[] cpM2 = XFoil.Solver.Numerics.SolverBuffers.CpM2(n);
        for (int i = 0; i < n; i++)
        {
            double qByQinf = state.InviscidSpeed[i] / freestreamSpeed;
            double cpInc = LegacyPrecisionMath.MultiplySubtract(qByQinf, qByQinf, 1.0, useLegacyPrecision);

            // dCp/dalpha from dQ/dalpha
            double dqda = state.InviscidSpeedAlphaDerivative[i];
            double dcpInc_da = -LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(2.0, qByQinf, dqda, useLegacyPrecision), freestreamSpeed, useLegacyPrecision);

            if (machNumber > 0.0)
            {
                double denom = LegacyPrecisionMath.MultiplyAdd(bFac, cpInc, beta, useLegacyPrecision);
                double denomSq = LegacyPrecisionMath.Square(denom, useLegacyPrecision);
                cpAlpha[i] = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(dcpInc_da, beta, useLegacyPrecision), denomSq, useLegacyPrecision);
                // CPG_MSQ = -CPG/DEN * (BETA_MSQ + BFAC_MSQ*CGINC)
                double cpg = cp[i];
                cpM2[i] = -cpg / denom * (betaMsq + bFacMsq * cpInc);
            }
            else
            {
                cpAlpha[i] = dcpInc_da;
                // At M=0: DEN=1, so CPG_MSQ reduces to -CGINC * (−0.5 + 0.25·CGINC).
                cpM2[i] = -cpInc * (betaMsq + bFacMsq * cpInc);
            }
        }

        // Step 3: Integrate pressure forces over each panel
        double cl = 0.0;
        double cdp = 0.0;
        double cm = 0.0;
        double clAlpha = 0.0;
        double clMach2 = 0.0;

        for (int i = 0; i < n - 1; i++)
        {
            int ip = i + 1;

            // Panel direction in physical coordinates
            double dxPhys = panel.X[ip] - panel.X[i];
            double dyPhys = panel.Y[ip] - panel.Y[i];

            // Project onto wind-axis system
            // DX = projected chord increment (lift direction)
            // DY = projected thickness increment (drag direction)
            double dx = LegacyPrecisionMath.SumOfProducts(dxPhys, cosa, dyPhys, sina, useLegacyPrecision);
            double dy = LegacyPrecisionMath.MultiplyAdd(dyPhys, cosa, -LegacyPrecisionMath.Multiply(dxPhys, sina, useLegacyPrecision), useLegacyPrecision);

            double avgCp = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cp[ip], cp[i], useLegacyPrecision), useLegacyPrecision);
            double deltaCp = LegacyPrecisionMath.Subtract(cp[ip], cp[i], useLegacyPrecision);

            double avgCpAlpha = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cpAlpha[ip], cpAlpha[i], useLegacyPrecision), useLegacyPrecision);
            double deltaCpAlpha = LegacyPrecisionMath.Subtract(cpAlpha[ip], cpAlpha[i], useLegacyPrecision);

            double avgCpMsq = 0.5 * (cpM2[ip] + cpM2[i]);

            // CL accumulation (trapezoidal)
            cl = LegacyPrecisionMath.MultiplyAdd(dx, avgCp, cl, useLegacyPrecision);

            // CDP accumulation (should be zero for inviscid)
            cdp = LegacyPrecisionMath.MultiplySubtract(dy, avgCp, cdp, useLegacyPrecision);

            // CL_alpha
            clAlpha = LegacyPrecisionMath.MultiplyAdd(dx, avgCpAlpha, clAlpha, useLegacyPrecision);

            // CL_MSQ (Fortran CLCALC line CL_MSQ = CL_MSQ + DX*AG_MSQ). Not a
            // parity-sensitive path — the result field is unused by callers
            // today, but populating it correctly unlocks B4 compressibility
            // work. Kept outside the LegacyPrecisionMath staging since this
            // didn't exist in the earlier port (no parity baseline to match).
            clMach2 += dx * avgCpMsq;

            // Moment arm from reference point to panel midpoint
            double xMid = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.X[ip], panel.X[i], useLegacyPrecision), useLegacyPrecision);
            double yMid = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.Y[ip], panel.Y[i], useLegacyPrecision), useLegacyPrecision);
            double armX = LegacyPrecisionMath.Subtract(xMid, momentRefX, useLegacyPrecision);
            double armY = LegacyPrecisionMath.Subtract(yMid, momentRefY, useLegacyPrecision);

            // CM with second-order DG*DX/12 and DG*DY/12 correction terms
            // This is the critical correction for CM accuracy from CLCALC
            double mx = LegacyPrecisionMath.MultiplyAdd(deltaCp, LegacyPrecisionMath.Divide(dxPhys, 12.0, useLegacyPrecision), LegacyPrecisionMath.Multiply(avgCp, armX, useLegacyPrecision), useLegacyPrecision);
            double my = LegacyPrecisionMath.MultiplyAdd(deltaCp, LegacyPrecisionMath.Divide(dyPhys, 12.0, useLegacyPrecision), LegacyPrecisionMath.Multiply(avgCp, armY, useLegacyPrecision), useLegacyPrecision);
            cm = LegacyPrecisionMath.Subtract(cm, LegacyPrecisionMath.SumOfProducts(dx, mx, dy, my, useLegacyPrecision), useLegacyPrecision);
        }

        // Handle the closing TE panel (from last node back to first node)
        {
            int i = n - 1;
            int ip = 0;

            double dxPhys = panel.X[ip] - panel.X[i];
            double dyPhys = panel.Y[ip] - panel.Y[i];

            double dx = LegacyPrecisionMath.SumOfProducts(dxPhys, cosa, dyPhys, sina, useLegacyPrecision);
            double dy = LegacyPrecisionMath.MultiplyAdd(dyPhys, cosa, -LegacyPrecisionMath.Multiply(dxPhys, sina, useLegacyPrecision), useLegacyPrecision);

            double avgCp = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cp[ip], cp[i], useLegacyPrecision), useLegacyPrecision);
            double deltaCp = LegacyPrecisionMath.Subtract(cp[ip], cp[i], useLegacyPrecision);

            double avgCpAlpha = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cpAlpha[ip], cpAlpha[i], useLegacyPrecision), useLegacyPrecision);
            double avgCpMsq = 0.5 * (cpM2[ip] + cpM2[i]);

            cl = LegacyPrecisionMath.MultiplyAdd(dx, avgCp, cl, useLegacyPrecision);
            cdp = LegacyPrecisionMath.MultiplySubtract(dy, avgCp, cdp, useLegacyPrecision);
            clAlpha = LegacyPrecisionMath.MultiplyAdd(dx, avgCpAlpha, clAlpha, useLegacyPrecision);
            clMach2 += dx * avgCpMsq;

            double xMid = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.X[ip], panel.X[i], useLegacyPrecision), useLegacyPrecision);
            double yMid = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.Y[ip], panel.Y[i], useLegacyPrecision), useLegacyPrecision);
            double armX = LegacyPrecisionMath.Subtract(xMid, momentRefX, useLegacyPrecision);
            double armY = LegacyPrecisionMath.Subtract(yMid, momentRefY, useLegacyPrecision);

            double mx = LegacyPrecisionMath.MultiplyAdd(deltaCp, LegacyPrecisionMath.Divide(dxPhys, 12.0, useLegacyPrecision), LegacyPrecisionMath.Multiply(avgCp, armX, useLegacyPrecision), useLegacyPrecision);
            double my = LegacyPrecisionMath.MultiplyAdd(deltaCp, LegacyPrecisionMath.Divide(dyPhys, 12.0, useLegacyPrecision), LegacyPrecisionMath.Multiply(avgCp, armY, useLegacyPrecision), useLegacyPrecision);
            cm = LegacyPrecisionMath.Subtract(cm, LegacyPrecisionMath.SumOfProducts(dx, mx, dy, my, useLegacyPrecision), useLegacyPrecision);
        }

        var result = new LinearVortexInviscidResult(
            cl,
            cm,
            cdp,
            clAlpha,
            clMach2,
            cp,
            alphaRadians);

        return result;
    }

    /// <summary>
    /// Computes Karman-Tsien corrected pressure coefficients from surface speed.
    /// At M=0, degenerates to Cp = 1 - (Q/Qinf)^2.
    /// </summary>
    /// <param name="surfaceSpeed">Surface speed array.</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude.</param>
    /// <param name="machNumber">Freestream Mach number.</param>
    /// <param name="pressureCoefficients">Output pressure coefficient array.</param>
    /// <param name="count">Number of nodes.</param>
    // Legacy mapping: f_xfoil/src/xfoil.f :: CPCALC.
    // Difference from legacy: The same Karman-Tsien Cp relation is exposed as a standalone helper over managed arrays instead of operating on shared XFoil work arrays.
    // Decision: Keep the helper because it makes the Cp update reusable and explicit.
    public static void ComputePressureCoefficients(
        double[] surfaceSpeed,
        double freestreamSpeed,
        double machNumber,
        double[] pressureCoefficients,
        int count,
        bool useLegacyPrecision = false)
    {
        var comp = PanelGeometryBuilder.ComputeCompressibilityParameters(machNumber);
        double beta = comp.Beta;
        double bFac = comp.KarmanTsienFactor;

        for (int i = 0; i < count; i++)
        {
            double qByQinf = LegacyPrecisionMath.Divide(surfaceSpeed[i], freestreamSpeed, useLegacyPrecision);
            double cpInc = LegacyPrecisionMath.MultiplySubtract(qByQinf, qByQinf, 1.0, useLegacyPrecision);

            if (machNumber > 0.0)
            {
                double denom = LegacyPrecisionMath.MultiplyAdd(bFac, cpInc, beta, useLegacyPrecision);
                pressureCoefficients[i] = denom > 1e-12 ? LegacyPrecisionMath.Divide(cpInc, denom, useLegacyPrecision) : cpInc;
            }
            else
            {
                pressureCoefficients[i] = cpInc;
            }
        }

    }

    /// <summary>
    /// High-level convenience method. Takes raw airfoil coordinates, desired panels, alpha, Mach.
    /// Creates states, calls Distribute -> geometry -> AssembleAndFactorSystem -> SolveAtAngleOfAttack.
    /// This is the entry point for testing.
    /// </summary>
    /// <param name="inputX">Raw airfoil X coordinates.</param>
    /// <param name="inputY">Raw airfoil Y coordinates.</param>
    /// <param name="inputCount">Number of raw input points.</param>
    /// <param name="angleOfAttackDegrees">Angle of attack in degrees.</param>
    /// <param name="panelCount">Desired number of panel nodes (default 160).</param>
    /// <param name="machNumber">Freestream Mach number (default 0.0).</param>
    /// <returns>Inviscid analysis result.</returns>
    // Legacy mapping: managed façade over f_xfoil/src/xpanel.f :: GGCALC and f_xfoil/src/xoper.f :: SPECAL.
    // Difference from legacy: The high-level convenience entry point allocates its own managed state objects and optionally rounds the alpha conversion for paneling parity.
    // Decision: Keep the convenience API because it is useful for tests and tooling.
    public static LinearVortexInviscidResult AnalyzeInviscid(
        double[] inputX, double[] inputY, int inputCount,
        double angleOfAttackDegrees,
        int panelCount = 160,
        double machNumber = 0.0,
        bool useLegacyPanelingPrecision = false)
    {
        double freestreamSpeed = 1.0;

        // Allocate panel state and solver state with sufficient capacity
        int maxNodes = panelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var state = new InviscidSolverState(maxNodes);

        // Step 1: Distribute panels using cosine clustering
        CurvatureAdaptivePanelDistributor.Distribute(
            inputX, inputY, inputCount, panel, panelCount, useLegacyPrecision: useLegacyPanelingPrecision);

        // Step 2: Initialize solver state for this node count
        state.InitializeForNodeCount(panel.NodeCount);
        state.UseLegacyPanelingPrecision = useLegacyPanelingPrecision;

        // Step 3: Solve
        double alphaRadians = angleOfAttackDegrees * (Math.PI / 180.0);
        if (useLegacyPanelingPrecision)
        {
            // Keep the parity path on a float-rounded degree->radian conversion so
            // the legacy panel geometry and alpha superposition start from the same input.
            alphaRadians = LegacyPrecisionMath.RoundToSingle(alphaRadians);
        }

        return SolveAtAngleOfAttack(alphaRadians, panel, state, freestreamSpeed, machNumber);
    }

    /// <summary>
    /// Applies the sharp trailing edge bisector condition.
    /// Replaces the last airfoil-node row (N-1) with an internal bisector zero-velocity condition.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC sharp-trailing-edge bisector override.
    // Difference from legacy: The managed version computes the bisector condition through explicit helper calls and trace events instead of inline workspace mutations.
    // Decision: Keep the explicit helper because it isolates one of the main special cases in the inviscid assembly.
    private static void ApplySharpTrailingEdgeCondition(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double angleOfAttackRadians,
        int n)
    {
        int last = n - 1;
        const double bisectorWeight = 0.1;
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;

        // Parity: use LegacyLibm.Atan2 (libm atan2f) instead of MathF.Atan2,
        // which drifts 1-3 ULP from glibc atan2f at certain inputs.
        double ag1 = useLegacyPrecision
            ? LegacyLibm.Atan2((float)(-panel.YDerivative[0]), (float)(-panel.XDerivative[0]))
            : LegacyPrecisionMath.Atan2(-panel.YDerivative[0], -panel.XDerivative[0], useLegacyPrecision);
        double ag2 = PanelGeometryBuilder.ContinuousAtan2(
            panel.YDerivative[last],
            panel.XDerivative[last],
            ag1,
            useLegacyPrecision);
        double abis = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(ag1, ag2, useLegacyPrecision), useLegacyPrecision);
        double cbis = LegacyPrecisionMath.Cos(abis, useLegacyPrecision);
        double sbis = LegacyPrecisionMath.Sin(abis, useLegacyPrecision);

        double ds1 = LegacyPrecisionMath.Sqrt(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Square(LegacyPrecisionMath.Subtract(panel.X[0], panel.X[1], useLegacyPrecision), useLegacyPrecision),
                LegacyPrecisionMath.Square(LegacyPrecisionMath.Subtract(panel.Y[0], panel.Y[1], useLegacyPrecision), useLegacyPrecision),
                useLegacyPrecision),
            useLegacyPrecision);
        double ds2 = LegacyPrecisionMath.Sqrt(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Square(LegacyPrecisionMath.Subtract(panel.X[last], panel.X[last - 1], useLegacyPrecision), useLegacyPrecision),
                LegacyPrecisionMath.Square(LegacyPrecisionMath.Subtract(panel.Y[last], panel.Y[last - 1], useLegacyPrecision), useLegacyPrecision),
                useLegacyPrecision),
            useLegacyPrecision);
        double dsMin = LegacyPrecisionMath.Min(ds1, ds2, useLegacyPrecision);
        if (dsMin <= 1e-12)
        {
            return;
        }

        double xBis = LegacyPrecisionMath.Subtract(
            LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.X[0], panel.X[last], useLegacyPrecision), useLegacyPrecision),
            LegacyPrecisionMath.Multiply(bisectorWeight, dsMin, cbis, useLegacyPrecision),
            useLegacyPrecision);
        double yBis = LegacyPrecisionMath.Subtract(
            LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(panel.Y[0], panel.Y[last], useLegacyPrecision), useLegacyPrecision),
            LegacyPrecisionMath.Multiply(bisectorWeight, dsMin, sbis, useLegacyPrecision),
            useLegacyPrecision);
        double nx = -sbis;
        double ny = cbis;

        // Compute velocity influence at bisector point
        StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            -1,  // off-body point
            xBis, yBis,
            nx, ny,
            false,
            true,
            panel, state,
            freestreamSpeed,
            angleOfAttackRadians);

        // Replace row N-1 with velocity sensitivities
        for (int j = 0; j < n; j++)
        {
            state.StreamfunctionInfluence[last, j] = state.VelocityVortexSensitivity[j];
            state.SourceInfluence[last, j] = -state.VelocitySourceSensitivity[j];
        }

        state.StreamfunctionInfluence[last, n] = 0.0;  // No streamfunction column contribution

        // Fortran sharp-TE row RHS:
        // GAMU(N,1) = -CBIS
        // GAMU(N,2) = -SBIS
        state.BasisVortexStrength[last, 0] = -freestreamSpeed * cbis;
        state.BasisVortexStrength[last, 1] = -freestreamSpeed * sbis;

    }

    /// <summary>
    /// Updates trailing edge vortex and source strengths from TE geometry ratios.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN/TE geometry trailing-edge strength update lineage.
    // Difference from legacy: The same TE source/vortex relations are expressed as a dedicated helper over the managed state object.
    // Decision: Keep the helper because it clarifies the TE update stage and preserves the legacy formula.
    private static void UpdateTrailingEdgeStrengths(
        LinearVortexPanelState panel,
        InviscidSolverState state)
    {
        int n = panel.NodeCount;
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;

        double scs, sds;
        if (state.IsSharpTrailingEdge)
        {
            scs = 1.0;
            sds = 0.0;
        }
        else
        {
            scs = LegacyPrecisionMath.Divide(state.TrailingEdgeAngleNormal, state.TrailingEdgeGap, useLegacyPrecision);
            sds = LegacyPrecisionMath.Divide(state.TrailingEdgeAngleStreamwise, state.TrailingEdgeGap, useLegacyPrecision);
        }

        // SIGTE = 0.5 * SCS * (GAM(1) - GAM(N))
        // GAMTE = -0.5 * SDS * (GAM(1) - GAM(N))
        double gamDiff = LegacyPrecisionMath.Subtract(state.VortexStrength[0], state.VortexStrength[n - 1], useLegacyPrecision);
        state.TrailingEdgeSourceStrength = LegacyPrecisionMath.Multiply(0.5, scs, gamDiff, useLegacyPrecision);
        state.TrailingEdgeVortexStrength = -LegacyPrecisionMath.Multiply(0.5, sds, gamDiff, useLegacyPrecision);

    }
}

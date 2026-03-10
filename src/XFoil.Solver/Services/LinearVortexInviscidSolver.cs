using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

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
    public static void AssembleAndFactorSystem(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed)
    {
        int n = panel.NodeCount;
        int systemSize = n + 1;

        // Step 1: Ensure geometry is prepared
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state);
        PanelGeometryBuilder.ComputeNormals(panel);
        PanelGeometryBuilder.ComputePanelAngles(panel, state);

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
                false,  // no geometric sensitivities
                true,   // include source terms
                panel, state,
                freestreamSpeed,
                0.0);   // alpha=0 for assembly

            // Copy vortex sensitivities into influence matrix
            for (int j = 0; j < n; j++)
            {
                state.StreamfunctionInfluence[i, j] = state.StreamfunctionVortexSensitivity[j];
                state.SourceInfluence[i, j] = -state.StreamfunctionSourceSensitivity[j];
            }

            // Internal streamfunction column
            state.StreamfunctionInfluence[i, n] = -1.0;

            // Basis RHS vectors: alpha=0 basis (cos=1, sin=0) and alpha=90 basis (cos=0, sin=1)
            state.BasisVortexStrength[i, 0] = -freestreamSpeed * panel.Y[i];
            state.BasisVortexStrength[i, 1] = freestreamSpeed * panel.X[i];
        }

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
            ApplySharpTrailingEdgeCondition(panel, state, freestreamSpeed, n);
        }

        // Step 5: LU-factor the influence matrix
        ScaledPivotLuSolver.Decompose(state.StreamfunctionInfluence, state.PivotIndices, systemSize);
        state.IsInfluenceMatrixFactored = true;

        // Step 6: Solve both basis RHS vectors
        // Extract column 0, back-substitute, store back
        var rhs0 = new double[systemSize];
        var rhs1 = new double[systemSize];
        for (int i = 0; i < systemSize; i++)
        {
            rhs0[i] = state.BasisVortexStrength[i, 0];
            rhs1[i] = state.BasisVortexStrength[i, 1];
        }

        ScaledPivotLuSolver.BackSubstitute(state.StreamfunctionInfluence, state.PivotIndices, rhs0, systemSize);
        ScaledPivotLuSolver.BackSubstitute(state.StreamfunctionInfluence, state.PivotIndices, rhs1, systemSize);

        for (int i = 0; i < systemSize; i++)
        {
            state.BasisVortexStrength[i, 0] = rhs0[i];
            state.BasisVortexStrength[i, 1] = rhs1[i];
        }

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
    }

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
            AssembleAndFactorSystem(panel, state, freestreamSpeed);
        }

        double cosa = Math.Cos(alphaRadians);
        double sina = Math.Sin(alphaRadians);

        // Step 2: Superimpose basis solutions
        for (int i = 0; i < n; i++)
        {
            state.VortexStrength[i] = cosa * state.BasisVortexStrength[i, 0]
                                    + sina * state.BasisVortexStrength[i, 1];
        }

        // Internal streamfunction is the N+1th entry
        state.InternalStreamfunction = cosa * state.BasisVortexStrength[n, 0]
                                     + sina * state.BasisVortexStrength[n, 1];

        // Step 3: Alpha derivatives
        for (int i = 0; i < n; i++)
        {
            state.VortexStrengthAlphaDerivative[i] =
                -sina * state.BasisVortexStrength[i, 0]
                + cosa * state.BasisVortexStrength[i, 1];
        }

        // Step 4: Update TE vortex/source strengths
        UpdateTrailingEdgeStrengths(panel, state);

        // Step 5: Compute inviscid speed
        ComputeInviscidSpeed(alphaRadians, state, n);

        // Step 6: Compute alpha derivative of speed
        for (int i = 0; i < n; i++)
        {
            state.InviscidSpeedAlphaDerivative[i] =
                -sina * state.BasisInviscidSpeed[i, 0]
                + cosa * state.BasisInviscidSpeed[i, 1];
        }

        // Step 7: Integrate pressure forces (at M=0, no Mach iteration needed)
        return IntegratePressureForces(
            panel, state, alphaRadians, machNumber, freestreamSpeed,
            0.25 * panel.Chord + panel.LeadingEdgeX,
            panel.LeadingEdgeY);
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
    public static LinearVortexInviscidResult SolveAtLiftCoefficient(
        double targetCl,
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double machNumber)
    {
        // Initial guess: alpha = targetCl / (2*pi)
        double alpha = targetCl / TwoPi;

        LinearVortexInviscidResult result = null!;

        for (int iter = 0; iter < 20; iter++)
        {
            result = SolveAtAngleOfAttack(alpha, panel, state, freestreamSpeed, machNumber);

            double clError = result.LiftCoefficient - targetCl;
            if (Math.Abs(clError) < 1e-6)
            {
                break;
            }

            double clAlpha = result.LiftCoefficientAlphaDerivative;
            if (Math.Abs(clAlpha) < 1e-10)
            {
                break; // Degenerate derivative
            }

            alpha -= clError / clAlpha;
        }

        return result;
    }

    /// <summary>
    /// Computes inviscid surface speed from basis speed vectors via cos/sin superposition.
    /// </summary>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <param name="state">Inviscid solver state with basis speeds.</param>
    /// <param name="nodeCount">Number of panel nodes.</param>
    public static void ComputeInviscidSpeed(double alphaRadians, InviscidSolverState state, int nodeCount)
    {
        double cosa = Math.Cos(alphaRadians);
        double sina = Math.Sin(alphaRadians);

        for (int i = 0; i < nodeCount; i++)
        {
            state.InviscidSpeed[i] = cosa * state.BasisInviscidSpeed[i, 0]
                                   + sina * state.BasisInviscidSpeed[i, 1];
        }
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
        double cosa = Math.Cos(alphaRadians);
        double sina = Math.Sin(alphaRadians);

        // Step 1: Compute compressibility parameters
        var comp = PanelGeometryBuilder.ComputeCompressibilityParameters(machNumber);
        double beta = comp.Beta;
        double bFac = comp.KarmanTsienFactor;

        // Step 2: Compute Karman-Tsien corrected Cp at each node
        double[] cp = new double[n];
        ComputePressureCoefficients(state.InviscidSpeed, freestreamSpeed, machNumber, cp, n);

        // Cp derivatives for CL_alpha and CL_M^2
        double[] cpAlpha = new double[n];
        double[] cpM2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            double qByQinf = state.InviscidSpeed[i] / freestreamSpeed;
            double cpInc = 1.0 - qByQinf * qByQinf;

            // dCp/dalpha from dQ/dalpha
            double dqda = state.InviscidSpeedAlphaDerivative[i];
            double dcpInc_da = -2.0 * qByQinf * dqda / freestreamSpeed;

            if (machNumber > 0.0)
            {
                double denom = beta + bFac * cpInc;
                double denomSq = denom * denom;
                cpAlpha[i] = (dcpInc_da * beta) / denomSq;
            }
            else
            {
                cpAlpha[i] = dcpInc_da;
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
            double dx = dxPhys * cosa + dyPhys * sina;   // Lift projection
            double dy = -dxPhys * sina + dyPhys * cosa;   // Drag projection

            double avgCp = 0.5 * (cp[ip] + cp[i]);
            double deltaCp = cp[ip] - cp[i];

            double avgCpAlpha = 0.5 * (cpAlpha[ip] + cpAlpha[i]);
            double deltaCpAlpha = cpAlpha[ip] - cpAlpha[i];

            // CL accumulation (trapezoidal)
            cl += dx * avgCp;

            // CDP accumulation (should be zero for inviscid)
            cdp -= dy * avgCp;

            // CL_alpha
            clAlpha += dx * avgCpAlpha;

            // Moment arm from reference point to panel midpoint
            double xMid = 0.5 * (panel.X[ip] + panel.X[i]);
            double yMid = 0.5 * (panel.Y[ip] + panel.Y[i]);
            double armX = xMid - momentRefX;
            double armY = yMid - momentRefY;

            // CM with second-order DG*DX/12 and DG*DY/12 correction terms
            // This is the critical correction for CM accuracy from CLCALC
            cm -= dx * (avgCp * armX + deltaCp * dxPhys / 12.0)
                + dy * (avgCp * armY + deltaCp * dyPhys / 12.0);
        }

        // Handle the closing TE panel (from last node back to first node)
        {
            int i = n - 1;
            int ip = 0;

            double dxPhys = panel.X[ip] - panel.X[i];
            double dyPhys = panel.Y[ip] - panel.Y[i];

            double dx = dxPhys * cosa + dyPhys * sina;
            double dy = -dxPhys * sina + dyPhys * cosa;

            double avgCp = 0.5 * (cp[ip] + cp[i]);
            double deltaCp = cp[ip] - cp[i];

            double avgCpAlpha = 0.5 * (cpAlpha[ip] + cpAlpha[i]);

            cl += dx * avgCp;
            cdp -= dy * avgCp;
            clAlpha += dx * avgCpAlpha;

            double xMid = 0.5 * (panel.X[ip] + panel.X[i]);
            double yMid = 0.5 * (panel.Y[ip] + panel.Y[i]);
            double armX = xMid - momentRefX;
            double armY = yMid - momentRefY;

            cm -= dx * (avgCp * armX + deltaCp * dxPhys / 12.0)
                + dy * (avgCp * armY + deltaCp * dyPhys / 12.0);
        }

        return new LinearVortexInviscidResult(
            cl,
            cm,
            cdp,
            clAlpha,
            clMach2,
            cp,
            alphaRadians);
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
    public static void ComputePressureCoefficients(
        double[] surfaceSpeed,
        double freestreamSpeed,
        double machNumber,
        double[] pressureCoefficients,
        int count)
    {
        var comp = PanelGeometryBuilder.ComputeCompressibilityParameters(machNumber);
        double beta = comp.Beta;
        double bFac = comp.KarmanTsienFactor;

        for (int i = 0; i < count; i++)
        {
            double qByQinf = surfaceSpeed[i] / freestreamSpeed;
            double cpInc = 1.0 - qByQinf * qByQinf;

            if (machNumber > 0.0)
            {
                double denom = beta + bFac * cpInc;
                pressureCoefficients[i] = denom > 1e-12 ? cpInc / denom : cpInc;
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
    public static LinearVortexInviscidResult AnalyzeInviscid(
        double[] inputX, double[] inputY, int inputCount,
        double angleOfAttackDegrees,
        int panelCount = 160,
        double machNumber = 0.0)
    {
        double freestreamSpeed = 1.0;

        // Allocate panel state and solver state with sufficient capacity
        int maxNodes = panelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var state = new InviscidSolverState(maxNodes);

        // Step 1: Distribute panels using cosine clustering
        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, panelCount);

        // Step 2: Initialize solver state for this node count
        state.InitializeForNodeCount(panel.NodeCount);

        // Step 3: Solve
        double alphaRadians = angleOfAttackDegrees * Math.PI / 180.0;
        return SolveAtAngleOfAttack(alphaRadians, panel, state, freestreamSpeed, machNumber);
    }

    /// <summary>
    /// Applies the sharp trailing edge bisector condition.
    /// Replaces the last airfoil-node row (N-1) with an internal bisector zero-velocity condition.
    /// </summary>
    private static void ApplySharpTrailingEdgeCondition(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        int n)
    {
        int last = n - 1;
        int systemSize = n + 1;

        // Compute bisector angle from TE node tangent directions
        // The bisector direction is the average of the upper-surface and lower-surface tangent vectors
        // Upper surface tangent at node 0: (XP[0], YP[0])
        // Lower surface tangent at node N-1: (XP[N-1], YP[N-1])
        double dxu = panel.XDerivative[0];
        double dyu = panel.YDerivative[0];
        double dxl = panel.XDerivative[last];
        double dyl = panel.YDerivative[last];

        // Bisector direction: average of negated upper tangent and lower tangent
        // (matching Fortran convention from TECALC)
        double bx = 0.5 * (-dxu + dxl);
        double by = 0.5 * (-dyu + dyl);
        double bMag = Math.Sqrt(bx * bx + by * by);
        if (bMag < 1e-12) return;  // Degenerate, skip

        bx /= bMag;
        by /= bMag;

        // Control point slightly inside the TE along bisector
        double ds = 0.001 * panel.Chord;
        double xBis = 0.5 * (panel.X[0] + panel.X[last]) + ds * bx;
        double yBis = 0.5 * (panel.Y[0] + panel.Y[last]) + ds * by;

        // Normal along bisector perpendicular (for velocity projection)
        double nx = by;
        double ny = -bx;

        // Compute velocity influence at bisector point
        StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            -1,  // off-body point
            xBis, yBis,
            nx, ny,
            false,
            true,
            panel, state,
            freestreamSpeed,
            0.0);

        // Replace row N-1 with velocity sensitivities
        for (int j = 0; j < n; j++)
        {
            state.StreamfunctionInfluence[last, j] = state.VelocityVortexSensitivity[j];
        }

        state.StreamfunctionInfluence[last, n] = 0.0;  // No streamfunction column contribution

        // Basis RHS: velocity from freestream at bisector point
        // alpha=0 basis: dPsi/dn from freestream = Qinf * (cos(0)*ny - sin(0)*nx) = Qinf * ny
        // alpha=90 basis: dPsi/dn from freestream = Qinf * (cos(90)*ny - sin(90)*nx) = -Qinf * nx
        state.BasisVortexStrength[last, 0] = -freestreamSpeed * ny;
        state.BasisVortexStrength[last, 1] = freestreamSpeed * nx;
    }

    /// <summary>
    /// Updates trailing edge vortex and source strengths from TE geometry ratios.
    /// </summary>
    private static void UpdateTrailingEdgeStrengths(
        LinearVortexPanelState panel,
        InviscidSolverState state)
    {
        int n = panel.NodeCount;

        double scs, sds;
        if (state.IsSharpTrailingEdge)
        {
            scs = 1.0;
            sds = 0.0;
        }
        else
        {
            scs = state.TrailingEdgeAngleNormal / state.TrailingEdgeGap;
            sds = state.TrailingEdgeAngleStreamwise / state.TrailingEdgeGap;
        }

        // SIGTE = 0.5 * SCS * (GAM(1) - GAM(N))
        // GAMTE = -0.5 * SDS * (GAM(1) - GAM(N))
        double gamDiff = state.VortexStrength[0] - state.VortexStrength[n - 1];
        state.TrailingEdgeSourceStrength = 0.5 * scs * gamDiff;
        state.TrailingEdgeVortexStrength = -0.5 * sds * gamDiff;
    }
}

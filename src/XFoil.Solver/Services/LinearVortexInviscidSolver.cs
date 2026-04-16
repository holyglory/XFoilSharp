using System.Collections.Generic;
using System.Linq;
using XFoil.Core.Numerics;
using XFoil.Solver.Diagnostics;
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { panel.NodeCount, freestreamSpeed, angleOfAttackRadians });
        int n = panel.NodeCount;
        int systemSize = AssembleSystem(panel, state, freestreamSpeed, angleOfAttackRadians);

        // Step 5: LU-factor the influence matrix. The parity-only legacy path
        // reproduces XFoil's single-precision factorization and backsolve.
        if (state.UseLegacyKernelPrecision)
        {
            CopyMatrixToSingle(state.StreamfunctionInfluence, state.LegacyStreamfunctionInfluenceFactors, systemSize);
            // GDB: dump AIJ row 32 (0-indexed = Fortran row 33) before LU
            if (DebugFlags.SetBlHex)
            {
                var aij = state.LegacyStreamfunctionInfluenceFactors;
                Console.Error.WriteLine(
                    $"C_AIJ33 c1={BitConverter.SingleToInt32Bits(aij[32, 0]):X8}" +
                    $" c2={BitConverter.SingleToInt32Bits(aij[32, 1]):X8}" +
                    $" c3={BitConverter.SingleToInt32Bits(aij[32, 2]):X8}" +
                    $" c80={BitConverter.SingleToInt32Bits(aij[32, 79]):X8}");
                Console.Error.WriteLine(
                    $"C_AIJ80 c1={BitConverter.SingleToInt32Bits(aij[79, 0]):X8}" +
                    $" c2={BitConverter.SingleToInt32Bits(aij[79, 1]):X8}" +
                    $" c3={BitConverter.SingleToInt32Bits(aij[79, 2]):X8}" +
                    $" c80={BitConverter.SingleToInt32Bits(aij[79, 79]):X8}" +
                    $" c81={BitConverter.SingleToInt32Bits(aij[79, 80]):X8}");
                Console.Error.WriteLine(
                    $"C_AIJ81 c1={BitConverter.SingleToInt32Bits(aij[80, 0]):X8}" +
                    $" c2={BitConverter.SingleToInt32Bits(aij[80, 1]):X8}" +
                    $" c3={BitConverter.SingleToInt32Bits(aij[80, 2]):X8}" +
                    $" c80={BitConverter.SingleToInt32Bits(aij[80, 79]):X8}" +
                    $" c81={BitConverter.SingleToInt32Bits(aij[80, 80]):X8}");
            }
            ScaledPivotLuSolver.Decompose(
                state.LegacyStreamfunctionInfluenceFactors,
                state.LegacyPivotIndices,
                systemSize,
                traceContext: "basis_aij_single");
            // GDB: dump FULL LU matrix to binary file
            if (DebugFlags.SetBlHex)
            {
                var lu = state.LegacyStreamfunctionInfluenceFactors;
                using var fs = new System.IO.FileStream("c_lu_matrix.bin", System.IO.FileMode.Create);
                using var bw = new System.IO.BinaryWriter(fs);
                for (int col = 0; col < systemSize; col++)
                    for (int row = 0; row < systemSize; row++)
                        bw.Write(lu[row, col]);
                Console.Error.WriteLine($"C_LU_DUMP {systemSize * systemSize} entries to c_lu_matrix.bin");
            }
            TraceFactoredMatrix("basis_lu_aij", state.LegacyStreamfunctionInfluenceFactors, systemSize, "SingleKernel");
            TracePivotEntries("basis_lu_pivot", state.LegacyPivotIndices, systemSize, "SingleKernel");
            if (DebugFlags.ParityTrace && systemSize == 81)
            {
                var piv = state.LegacyPivotIndices;
                var lu = state.LegacyStreamfunctionInfluenceFactors;
                var sbp = new System.Text.StringBuilder("C_PIVALL");
                for (int pp = 0; pp < 81; pp++) sbp.Append($" {piv[pp]+1,3}");
                Console.Error.WriteLine(sbp.ToString());
                var sbd1 = new System.Text.StringBuilder("C_LUDIAG1_20");
                for (int pp = 0; pp < 20; pp++) sbd1.Append($" {BitConverter.SingleToInt32Bits(lu[pp, pp]):X8}");
                Console.Error.WriteLine(sbd1.ToString());
                var sbd2 = new System.Text.StringBuilder("C_LUDIAG21_40");
                for (int pp = 20; pp < 40; pp++) sbd2.Append($" {BitConverter.SingleToInt32Bits(lu[pp, pp]):X8}");
                Console.Error.WriteLine(sbd2.ToString());
                var sbd3 = new System.Text.StringBuilder("C_LUDIAG41_60");
                for (int pp = 40; pp < 60; pp++) sbd3.Append($" {BitConverter.SingleToInt32Bits(lu[pp, pp]):X8}");
                Console.Error.WriteLine(sbd3.ToString());
                var sbd4 = new System.Text.StringBuilder("C_LUDIAG61_81");
                for (int pp = 60; pp < 81; pp++) sbd4.Append($" {BitConverter.SingleToInt32Bits(lu[pp, pp]):X8}");
                Console.Error.WriteLine(sbd4.ToString());
            }
            if (DebugFlags.BldifDebug)
            {
                var piv = state.LegacyPivotIndices;
                Console.Error.WriteLine($"PIV_CS {piv[0]+1,4} {piv[1]+1,4} {piv[2]+1,4} {piv[3]+1,4} {piv[4]+1,4} {piv[5]+1,4} {piv[6]+1,4} {piv[7]+1,4} {piv[8]+1,4} {piv[9]+1,4}");
                var lu = state.LegacyStreamfunctionInfluenceFactors;
                // float -> double hex for comparison with Fortran (which casts REAL to DBLE)
                long d1 = BitConverter.DoubleToInt64Bits((double)lu[0, 0]);
                long d80 = BitConverter.DoubleToInt64Bits((double)lu[79, 79]);
                long d81 = BitConverter.DoubleToInt64Bits((double)lu[80, 80]);
                Console.Error.WriteLine($"LU_DIAG_CS {d1:X16} {d80:X16} {d81:X16}");
                // Also dump pre-LU matrix at row 80 (before factoring)
                // But the matrix is already factored in-place... we need to dump before.
                // Instead dump a few LU elements around row 80 for detailed comparison
                // Dump entire row 80 of LU in hex to find first divergence
                {
                    var sb = new System.Text.StringBuilder("LU80_CS");
                    for (int cc = 0; cc < Math.Min(systemSize, 161); cc++)
                    {
                        sb.Append($" {BitConverter.SingleToInt32Bits(lu[79, cc]):X8}");
                    }
                    Console.Error.WriteLine(sb.ToString());
                }
            }
        }
        else
        {
            ScaledPivotLuSolver.Decompose(
                state.StreamfunctionInfluence,
                state.PivotIndices,
                systemSize,
                traceContext: "basis_aij_double");
            TraceFactoredMatrix("basis_lu_aij", state.StreamfunctionInfluence, systemSize, "Double");
            TracePivotEntries("basis_lu_pivot", state.PivotIndices, systemSize, "Double");
        }

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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "basis_ready",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new { systemSize, nodeCount = n });
        }
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { panel.NodeCount, freestreamSpeed, angleOfAttackRadians });
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
            if (i == 79 && DebugFlags.BldifDebug)
            {
                var sb = new System.Text.StringBuilder("DZDG80_CS");
                for (int dbgJ = 0; dbgJ < n; dbgJ++)
                {
                    sb.Append($" {BitConverter.SingleToInt32Bits((float)state.StreamfunctionVortexSensitivity[dbgJ]):X8}");
                }
                Console.Error.WriteLine(sb.ToString());
            }

            state.StreamfunctionInfluence[i, n] = -1.0;
            state.BasisVortexStrength[i, 0] = -freestreamSpeed * panel.Y[i];
            state.BasisVortexStrength[i, 1] = freestreamSpeed * panel.X[i];
        }

        // Debug: dump BIJ (SourceInfluence) at specific elements
        if (DebugFlags.BldifDebug)
        {
            // BIJ[30,79] should match Fortran BIJ(31,80) = -DZDM(80) for i=31
            int bij_hex = BitConverter.SingleToInt32Bits((float)state.SourceInfluence[30, 79]);
            int bij2_hex = BitConverter.SingleToInt32Bits((float)state.SourceInfluence[30, 1]);
            Console.Error.WriteLine($"BIJ_CS [30,79]={bij_hex:X8} [30,1]={bij2_hex:X8}");
        }

        // GDB-parity: dump raw AIJ row 12 and RHS (=Fortran row 13) before LU
        if (DebugFlags.ParityTrace && n == 80)
        {
            Console.Error.WriteLine($"C_DIAG12_RHS12 {BitConverter.SingleToInt32Bits((float)state.StreamfunctionInfluence[12, 12]):X8} {BitConverter.SingleToInt32Bits((float)state.BasisVortexStrength[12, 0]):X8}");
        }

        // Debug: dump AIJ for comparison with Fortran
        if (DebugFlags.BldifDebug)
        {
            // Dump full AIJ row 80 BEFORE LU factoring
            {
                var sb = new System.Text.StringBuilder("AIJ80_CS");
                for (int cc = 0; cc < n + 1; cc++)
                {
                    sb.Append($" {BitConverter.SingleToInt32Bits((float)state.StreamfunctionInfluence[79, cc]):X8}");
                }
                Console.Error.WriteLine(sb.ToString());
            }
            long h12 = BitConverter.DoubleToInt64Bits(state.StreamfunctionInfluence[0, 1]);
            long h180 = BitConverter.DoubleToInt64Bits(state.StreamfunctionInfluence[0, 79]);
            long h801 = BitConverter.DoubleToInt64Bits(state.StreamfunctionInfluence[79, 0]);
            long h8080 = BitConverter.DoubleToInt64Bits(state.StreamfunctionInfluence[79, 79]);
            long h8081 = BitConverter.DoubleToInt64Bits(state.StreamfunctionInfluence[79, 80]);
            Console.Error.WriteLine($"AIJ_CS {h12:X16} {h180:X16} {h801:X16} {h8080:X16} {h8081:X16}");
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
        ApplySharpTrailingEdgeCondition(panel, state, freestreamSpeed, angleOfAttackRadians, n);
        }

        TraceInfluenceSystem(state, n, systemSize);

        state.IsInfluenceMatrixFactored = false;
        state.AreBasisSolutionsComputed = false;
        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "system_assembled",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new { systemSize, sharpTrailingEdge = state.IsSharpTrailingEdge });
        }
        return systemSize;
    }

    // Legacy mapping: none; managed-only trace helper family around GGCALC basis and matrix state.
    // Difference from legacy: The original solver only emits equivalent detail in instrumented builds, while the managed port keeps structured trace helpers alongside the kernel.
    // Decision: Keep the trace-helper block because parity debugging depends on it and it does not alter solver behavior.
    private static void TracePanelNodes(LinearVortexPanelState panel)
    {
        string scope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver), nameof(AssembleAndFactorSystem));
        for (int i = 0; i < panel.NodeCount; i++)
        {
            if (SolverTrace.IsActive)
            {
                SolverTrace.Event(
                    "panel_node",
                    scope,
                    new
                    {
                        index = i + 1,
                        x = panel.X[i],
                        y = panel.Y[i],
                        xp = panel.XDerivative[i],
                        yp = panel.YDerivative[i],
                        nx = panel.NormalX[i],
                        ny = panel.NormalY[i],
                        panelAngle = panel.PanelAngle[Math.Min(i, panel.NodeCount - 1)]
                    });
            }
        }
    }

    private static void TraceInfluenceSystem(InviscidSolverState state, int nodeCount, int systemSize)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        string scope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver), nameof(AssembleAndFactorSystem));
        string precision = state.UseLegacyKernelPrecision ? "SingleKernel" : "Double";

        for (int row = 0; row < systemSize; row++)
        {
            for (int col = 0; col < systemSize; col++)
            {
                // The legacy parity path solves a float-cast copy of the influence system.
                // Trace that exact input so bitwise comparisons are made against the real kernel input.
                double value = state.UseLegacyKernelPrecision
                    ? (float)state.StreamfunctionInfluence[row, col]
                    : state.StreamfunctionInfluence[row, col];
                if (SolverTrace.IsActive)
                {
                    SolverTrace.Event(
                        "matrix_entry",
                        scope,
                        new
                        {
                            matrix = "aij",
                            row = row + 1,
                            col = col + 1,
                            value,
                            precision
                        });
                }
            }
        }

        for (int row = 0; row < systemSize; row++)
        {
            for (int col = 0; col < nodeCount; col++)
            {
                double value = state.UseLegacyKernelPrecision
                    ? (float)state.SourceInfluence[row, col]
                    : state.SourceInfluence[row, col];
                if (SolverTrace.IsActive)
                {
                    SolverTrace.Event(
                        "matrix_entry",
                        scope,
                        new
                        {
                            matrix = "bij",
                            row = row + 1,
                            col = col + 1,
                            value,
                            precision
                        });
                }
            }
        }

        TraceBasisEntries(scope, "basis_rhs_alpha0", state.BasisVortexStrength, 0, systemSize, precision);
        TraceBasisEntries(scope, "basis_rhs_alpha90", state.BasisVortexStrength, 1, systemSize, precision);
    }

    private static void TraceBasisEntries(
        string scope,
        string name,
        double[,] values,
        int column,
        int count,
        string precision)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        for (int index = 0; index < count; index++)
        {
            if (SolverTrace.IsActive)
            {
                SolverTrace.Event(
                    "basis_entry",
                    scope,
                    new
                    {
                        index = index + 1,
                        value = values[index, column],
                        precision
                    },
                    name);
            }
        }
    }

    private static void TraceBasisEntries(
        string scope,
        string name,
        IReadOnlyList<double> values,
        string precision)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        for (int index = 0; index < values.Count; index++)
        {
            if (SolverTrace.IsActive)
            {
                SolverTrace.Event(
                    "basis_entry",
                    scope,
                    new
                    {
                        index = index + 1,
                        value = values[index],
                        precision
                    },
                    name);
            }
        }
    }

    private static string FormatSingleHex(float[] values, int index)
    {
        return (uint)index < (uint)values.Length
            ? BitConverter.SingleToInt32Bits(values[index]).ToString("X8")
            : "NA";
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC basis back-substitution.
    // Difference from legacy: The managed port solves the two basis RHS vectors in a dedicated helper and explicitly mirrors the parity float backsolve path.
    // Decision: Keep the helper because it isolates the basis solve stage cleanly.
    private static void SolveBasisRightHandSides(InviscidSolverState state, int systemSize)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { systemSize });
        var rhs0 = new double[systemSize];
        var rhs1 = new double[systemSize];
        if (state.UseLegacyKernelPrecision)
        {
            var rhs0Single = new float[systemSize];
            var rhs1Single = new float[systemSize];
            for (int i = 0; i < systemSize; i++)
            {
                rhs0Single[i] = (float)state.BasisVortexStrength[i, 0];
                rhs1Single[i] = (float)state.BasisVortexStrength[i, 1];
            }

            if (DebugFlags.SetBlHex)
            {
                Console.Error.WriteLine(
                    $"C_RHS0 r0={FormatSingleHex(rhs0Single, 0)}" +
                    $" r1={FormatSingleHex(rhs0Single, 1)}" +
                    $" rLast={FormatSingleHex(rhs0Single, systemSize - 1)}" +
                    $" n={systemSize}");
                Console.Error.Flush();
            }
            ScaledPivotLuSolver.BackSubstitute(
                state.LegacyStreamfunctionInfluenceFactors,
                state.LegacyPivotIndices,
                rhs0Single,
                systemSize,
                traceContext: "basis_gamma_alpha0_single");
            if (DebugFlags.SetBlHex)
            {
                Console.Error.WriteLine(
                    $"C_GAM0 g0={FormatSingleHex(rhs0Single, 0)}" +
                    $" g1={FormatSingleHex(rhs0Single, 1)}" +
                    $" g80={FormatSingleHex(rhs0Single, 80)}" +
                    $" gLast={FormatSingleHex(rhs0Single, systemSize - 1)}" +
                    $" n={systemSize}");
                Console.Error.Flush();
            }
            ScaledPivotLuSolver.BackSubstitute(
                state.LegacyStreamfunctionInfluenceFactors,
                state.LegacyPivotIndices,
                rhs1Single,
                systemSize,
                traceContext: "basis_gamma_alpha90_single");
            if (DebugFlags.SetBlHex)
            {
                Console.Error.WriteLine(
                    $"C_GAM1 g0={FormatSingleHex(rhs1Single, 0)}" +
                    $" g1={FormatSingleHex(rhs1Single, 1)}" +
                    $" g80={FormatSingleHex(rhs1Single, 80)}" +
                    $" gLast={FormatSingleHex(rhs1Single, systemSize - 1)}");
            }

            for (int i = 0; i < systemSize; i++)
            {
                rhs0[i] = rhs0Single[i];
                rhs1[i] = rhs1Single[i];
                state.BasisVortexStrength[i, 0] = rhs0Single[i];
                state.BasisVortexStrength[i, 1] = rhs1Single[i];
            }

            if (DebugFlags.ParityTrace && systemSize == 81)
            {
                var sbg = new System.Text.StringBuilder("C_GAMU9_15");
                for (int gi = 9; gi < 16; gi++)
                    sbg.Append($" {BitConverter.SingleToInt32Bits(rhs0Single[gi]):X8}");
                Console.Error.WriteLine(sbg.ToString());
            }
        }
        else
        {
            for (int i = 0; i < systemSize; i++)
            {
                rhs0[i] = state.BasisVortexStrength[i, 0];
                rhs1[i] = state.BasisVortexStrength[i, 1];
            }

            ScaledPivotLuSolver.BackSubstitute(
                state.StreamfunctionInfluence,
                state.PivotIndices,
                rhs0,
                systemSize,
                traceContext: "basis_gamma_alpha0_double");
            ScaledPivotLuSolver.BackSubstitute(
                state.StreamfunctionInfluence,
                state.PivotIndices,
                rhs1,
                systemSize,
                traceContext: "basis_gamma_alpha90_double");

            for (int i = 0; i < systemSize; i++)
            {
                state.BasisVortexStrength[i, 0] = rhs0[i];
                state.BasisVortexStrength[i, 1] = rhs1[i];
            }
        }

        string traceScope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver));
        string precision = state.UseLegacyKernelPrecision ? "SingleKernel" : "Double";
        TraceBasisEntries(traceScope, "basis_gamma_alpha0", rhs0, precision);
        TraceBasisEntries(traceScope, "basis_gamma_alpha90", rhs1, precision);



        if (DebugFlags.BldifDebug)
        {
            // Dump basis gamma at nodes 79-82 (stagnation area) for parity comparison
            for (int dbg = 78; dbg <= 82 && dbg < systemSize; dbg++)
            {
                long h0 = BitConverter.DoubleToInt64Bits(rhs0[dbg]);
                long h1 = BitConverter.DoubleToInt64Bits(rhs1[dbg]);
                Console.Error.WriteLine($"GAMU_CS [{dbg+1,3}] a0={h0:X16} a90={h1:X16}");
            }
        }

        if (SolverTrace.IsActive)
        {
            SolverTrace.Array(
                traceScope,
                "basis_gamma_alpha0",
                rhs0,
                new { systemSize });
        }
        if (SolverTrace.IsActive)
        {
            SolverTrace.Array(
                traceScope,
                "basis_gamma_alpha90",
                rhs1,
                new { systemSize });
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

    private static void TraceFactoredMatrix(
        string name,
        float[,] values,
        int size,
        string precision)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        string scope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver), nameof(AssembleAndFactorSystem));
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (SolverTrace.IsActive)
                {
                    SolverTrace.Event(
                        "matrix_entry",
                        scope,
                        new
                        {
                            matrix = name,
                            row = row + 1,
                            col = col + 1,
                            value = values[row, col],
                            precision
                        });
                }
            }
        }
    }

    private static void TraceFactoredMatrix(
        string name,
        double[,] values,
        int size,
        string precision)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        string scope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver));
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (SolverTrace.IsActive)
                {
                    SolverTrace.Event(
                        "matrix_entry",
                        scope,
                        new
                        {
                            matrix = name,
                            row = row + 1,
                            col = col + 1,
                            value = values[row, col],
                            precision
                        });
                }
            }
        }
    }

    private static void TracePivotEntries(
        string name,
        IReadOnlyList<int> pivotIndices,
        int size,
        string precision)
    {
        if (SolverTrace.Current is null)
        {
            return;
        }

        string scope = SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver));
        for (int index = 0; index < size; index++)
        {
            if (SolverTrace.IsActive)
            {
                SolverTrace.Event(
                    "pivot_entry",
                    scope,
                    new
                    {
                        vector = name,
                        index = index + 1,
                        value = pivotIndices[index] + 1,
                        precision
                    });
            }
        }
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { alphaRadians, panel.NodeCount, freestreamSpeed, machNumber });
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
        if (DebugFlags.SetBlHex)
        {
            Console.Error.WriteLine(
                $"C_GAMSUP cosa={BitConverter.SingleToInt32Bits((float)cosa):X8}" +
                $" sina={BitConverter.SingleToInt32Bits((float)sina):X8}" +
                $" b00={BitConverter.SingleToInt32Bits((float)state.BasisVortexStrength[0, 0]):X8}" +
                $" b01={BitConverter.SingleToInt32Bits((float)state.BasisVortexStrength[0, 1]):X8}" +
                $" g0={BitConverter.SingleToInt32Bits((float)state.VortexStrength[0]):X8}" +
                $" g1={BitConverter.SingleToInt32Bits((float)state.VortexStrength[1]):X8}" +
                $" g80={BitConverter.SingleToInt32Bits((float)state.VortexStrength[80]):X8}");
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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "inviscid_solution",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new
                {
                    result.LiftCoefficient,
                    result.MomentCoefficient,
                    result.AngleOfAttackRadians,
                    result.LiftCoefficientAlphaDerivative
                });
        }

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

        // Initial guess: alpha = targetCl / (2*pi)
        double alpha = LegacyPrecisionMath.Divide(targetCl, TwoPi, useLegacyPrecision);

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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { alphaRadians, nodeCount });
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

            if (DebugFlags.SetBlHex
                && i >= 79 && i <= 82)
            {
                Console.Error.WriteLine(
                    $"C_QINV I={i + 1,4}" +
                    $" Q={BitConverter.SingleToInt32Bits((float)state.InviscidSpeed[i]):X8}" +
                    $" Q0={BitConverter.SingleToInt32Bits((float)state.BasisInviscidSpeed[i, 0]):X8}" +
                    $" Q90={BitConverter.SingleToInt32Bits((float)state.BasisInviscidSpeed[i, 1]):X8}" +
                    $" COS={BitConverter.SingleToInt32Bits((float)cosa):X8}" +
                    $" SIN={BitConverter.SingleToInt32Bits((float)sina):X8}");
            }
        }

        if (SolverTrace.IsActive)
        {
            SolverTrace.Array(
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                "inviscid_speed",
                state.InviscidSpeed.Take(nodeCount).ToArray(),
                new { nodeCount });
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
        if (!useLegacyPrecision)
        {
            return (cosa * basisAlpha0) + (sina * basisAlpha90);
        }

        float cosaSingle = (float)cosa;
        float basisAlpha0Single = (float)basisAlpha0;
        float sinaSingle = (float)sina;
        float basisAlpha90Single = (float)basisAlpha90;
        // Fortran: GAM(I) = COSA*GAMU(I,1) + SINA*GAMU(I,2)
        // With -ffp-contract=off all variables are REAL, so each multiply
        // rounds to float before the add. The previous wide-accumulation
        // form (double products + single rounding) gave 1 ULP drift at
        // asymmetric alpha, propagating to 128 ULP in PSILIN at wake
        // node 6 and 3125 ULP in final CD.
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
        if (!useLegacyPrecision)
        {
            return (cosa * basisAlpha90) - (sina * basisAlpha0);
        }

        float cosaSingle = (float)cosa;
        float basisAlpha0Single = (float)basisAlpha0;
        float sinaSingle = (float)sina;
        float basisAlpha90Single = (float)basisAlpha90;
        // Fortran: GAMU_A(I) = COSA*GAMU(I,2) - SINA*GAMU(I,1)
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { panel.NodeCount, alphaRadians, machNumber, freestreamSpeed, momentRefX, momentRefY });
        int n = panel.NodeCount;
        bool useLegacyPrecision = state.UseLegacyKernelPrecision || state.UseLegacyPanelingPrecision;
        double cosa = LegacyPrecisionMath.Cos(alphaRadians, useLegacyPrecision);
        double sina = LegacyPrecisionMath.Sin(alphaRadians, useLegacyPrecision);

        // Step 1: Compute compressibility parameters
        var comp = PanelGeometryBuilder.ComputeCompressibilityParameters(machNumber);
        double beta = comp.Beta;
        double bFac = comp.KarmanTsienFactor;

        // Step 2: Compute Karman-Tsien corrected Cp at each node
        double[] cp = new double[n];
        ComputePressureCoefficients(state.InviscidSpeed, freestreamSpeed, machNumber, cp, n, useLegacyPrecision);

        // Cp derivatives for CL_alpha and CL_M^2
        double[] cpAlpha = new double[n];
        double[] cpM2 = new double[n];
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
            double dx = LegacyPrecisionMath.SumOfProducts(dxPhys, cosa, dyPhys, sina, useLegacyPrecision);
            double dy = LegacyPrecisionMath.MultiplyAdd(dyPhys, cosa, -LegacyPrecisionMath.Multiply(dxPhys, sina, useLegacyPrecision), useLegacyPrecision);

            double avgCp = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cp[ip], cp[i], useLegacyPrecision), useLegacyPrecision);
            double deltaCp = LegacyPrecisionMath.Subtract(cp[ip], cp[i], useLegacyPrecision);

            double avgCpAlpha = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(cpAlpha[ip], cpAlpha[i], useLegacyPrecision), useLegacyPrecision);
            double deltaCpAlpha = LegacyPrecisionMath.Subtract(cpAlpha[ip], cpAlpha[i], useLegacyPrecision);

            // CL accumulation (trapezoidal)
            cl = LegacyPrecisionMath.MultiplyAdd(dx, avgCp, cl, useLegacyPrecision);

            // CDP accumulation (should be zero for inviscid)
            cdp = LegacyPrecisionMath.MultiplySubtract(dy, avgCp, cdp, useLegacyPrecision);

            // CL_alpha
            clAlpha = LegacyPrecisionMath.MultiplyAdd(dx, avgCpAlpha, clAlpha, useLegacyPrecision);

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

            cl = LegacyPrecisionMath.MultiplyAdd(dx, avgCp, cl, useLegacyPrecision);
            cdp = LegacyPrecisionMath.MultiplySubtract(dy, avgCp, cdp, useLegacyPrecision);
            clAlpha = LegacyPrecisionMath.MultiplyAdd(dx, avgCpAlpha, clAlpha, useLegacyPrecision);

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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "pressure_forces",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new { cl, cdp, cm, clAlpha, clMach2 });
        }

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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { freestreamSpeed, machNumber, count });
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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Array(
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                "pressure_coefficients",
                pressureCoefficients.Take(count).ToArray(),
                new { count });
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
        CosineClusteringPanelDistributor.Distribute(
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { n, freestreamSpeed, angleOfAttackRadians });
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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "sharp_te_condition",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new { row = last + 1, cbis, sbis });
        }
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
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
            new { panel.NodeCount });
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

        if (SolverTrace.IsActive)
        {
            SolverTrace.Event(
                "trailing_edge_strengths",
                SolverTrace.ScopeName(typeof(LinearVortexInviscidSolver)),
                new
                {
                    gamDiff,
                    state.TrailingEdgeSourceStrength,
                    state.TrailingEdgeVortexStrength
                });
        }
    }
}

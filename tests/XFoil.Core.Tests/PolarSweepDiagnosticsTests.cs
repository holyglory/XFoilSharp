using System;
using System.Globalization;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: SPECAL operating-point progression
// Secondary legacy source: none
// Role in port: Managed-only diagnostic test that exposes per-point alpha-sweep results while debugging polar continuation parity.
// Differences: Legacy XFoil does not provide an equivalent structured unit-test dump; this file exists only to inspect the managed sweep state at each operating point.
// Decision: Keep the diagnostic test because it shortens parity descent on sweep-level failures without changing solver behavior.
namespace XFoil.Core.Tests;

public sealed class PolarSweepDiagnosticsTests
{
    private static readonly object DebugCounterLock = new();
    private readonly ITestOutputHelper _output;

    // Legacy mapping: none; managed-only test constructor for xUnit output capture.
    // Difference from legacy: This is test infrastructure only. Decision: Keep the managed helper.
    public PolarSweepDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL alpha sequence lineage.
    // Difference from legacy: The diagnostic emits structured managed result fields per alpha point instead of inspecting interactive OPER output.
    // Decision: Keep the diagnostic because it localizes sweep-level NaN and convergence failures quickly.
    public void AlphaSweep_Naca0012_DumpPerPointResults()
    {
        var settings = CreateXFoilParitySettings(1_000_000);
        var geometry = BuildNaca0012(settings.PanelCount);
        var results = PolarSweepRunner.SweepAlpha(geometry, settings, -2.0, 10.0, 2.0);

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            _output.WriteLine(
                $"i={i} alpha={result.AngleOfAttackDegrees:F1} converged={result.Converged} iter={result.Iterations} " +
                $"CL={result.LiftCoefficient:G17} CM={result.MomentCoefficient:G17} " +
                $"CD={result.DragDecomposition.CD:G17} CDF={result.DragDecomposition.CDF:G17} CDP={result.DragDecomposition.CDP:G17}");
        }

        Assert.Equal(7, results.Count);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL single operating-point solve lineage.
    // Difference from legacy: This diagnostic runs one managed alpha point directly to distinguish standalone point failure from sweep continuation failure.
    // Decision: Keep the diagnostic because it isolates whether the alpha=10 NaN is local or history-dependent.
    public void Alpha10_Naca0012_DirectPoint_DumpResult()
    {
        var settings = CreateXFoilParitySettings(1_000_000);
        var geometry = BuildNaca0012(settings.PanelCount);
        long debugCounter = AllocateDebugCounter();
        string debugDirectory = GetFortranDebugDirectory();
        Directory.CreateDirectory(debugDirectory);
        string debugPath = Path.Combine(debugDirectory, $"polar_alpha10_debug.{debugCounter}.txt");
        string dijTracePath = Path.Combine(debugDirectory, $"polar_alpha10_dij_trace.{debugCounter}.jsonl");
        string solverTracePath = Path.Combine(debugDirectory, $"polar_alpha10_solver_trace.{debugCounter}.jsonl");

        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CurvatureAdaptivePanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        LinearVortexInviscidSolver.AssembleAndFactorSystem(
            panel,
            inviscidState,
            settings.FreestreamVelocity,
            10.0 * Math.PI / 180.0);

        double alphaDegrees = 10.0;
        double alphaRadians = alphaDegrees * Math.PI / 180.0;
        var inviscid = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians, panel, inviscidState,
            settings.FreestreamVelocity, settings.MachNumber);
        ViscousAnalysisResult result;
        int nWake = Math.Max((panel.NodeCount / 8) + 2, 3);
        using (var traceWriter = new JsonlTraceWriter(dijTracePath, runtime: "csharp", session: new
        {
            caseId = "n0012_re1e6_a10",
            alphaDegrees,
            traceCounter = debugCounter,
            source = "alpha10_dij_build",
            nWake
        }))
        using (var traceScope = SolverTrace.Begin(traceWriter))
        {
            _ = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
                inviscidState,
                panel,
                nWake,
                settings.FreestreamVelocity,
                alphaRadians,
                settings.UseLegacyWakeSourceKernelPrecision);
        }

        using (var debugWriter = new StreamWriter(debugPath, false))
        {
            using var traceWriter = new JsonlTraceWriter(solverTracePath, runtime: "csharp", session: new
            {
                caseId = "n0012_re1e6_a10",
                alphaDegrees,
                traceCounter = debugCounter,
                source = "alpha10_solver",
                nWake
            });
            using var traceScope = SolverTrace.Begin(traceWriter);
            result = ViscousSolverEngine.SolveViscousFromInviscid(
                panel, inviscidState, inviscid, settings, alphaRadians, debugWriter);
        }

        _output.WriteLine(
            $"direct alpha={alphaDegrees:F1} converged={result.Converged} iter={result.Iterations} " +
            $"CL={result.LiftCoefficient:G17} CM={result.MomentCoefficient:G17} " +
            $"CD={result.DragDecomposition.CD:G17} CDF={result.DragDecomposition.CDF:G17} CDP={result.DragDecomposition.CDP:G17}");
        _output.WriteLine($"debug_path={debugPath}");
        _output.WriteLine($"dij_trace_path={dijTracePath}");
        _output.WriteLine($"solver_trace_path={solverTracePath}");

        int historyCount = result.ConvergenceHistory.Count;
        _output.WriteLine($"history_count={historyCount}");
        int firstBad = -1;
        for (int i = 0; i < historyCount; i++)
        {
            var step = result.ConvergenceHistory[i];
            if (!double.IsFinite(step.RmsResidual) ||
                !double.IsFinite(step.MaxResidual) ||
                !double.IsFinite(step.CL) ||
                !double.IsFinite(step.CM) ||
                !double.IsFinite(step.CD))
            {
                firstBad = i;
                break;
            }
        }

        _output.WriteLine($"first_bad_history_index={firstBad}");
        if (firstBad >= 0)
        {
            int start = Math.Max(0, firstBad - 2);
            int end = Math.Min(historyCount - 1, firstBad + 2);
            for (int i = start; i <= end; i++)
            {
                var step = result.ConvergenceHistory[i];
                _output.WriteLine(
                    $"focus iter={step.Iteration} rms={step.RmsResidual:G17} max={step.MaxResidual:G17} " +
                    $"side={step.MaxResidualSide} station={step.MaxResidualStation} " +
                    $"rlx={step.RelaxationFactor:G17} CL={step.CL:G17} CM={step.CM:G17} CD={step.CD:G17}");
            }
        }

        for (int i = Math.Max(0, historyCount - 5); i < historyCount; i++)
        {
            var step = result.ConvergenceHistory[i];
            _output.WriteLine(
                $"hist iter={step.Iteration} rms={step.RmsResidual:G17} max={step.MaxResidual:G17} " +
                $"side={step.MaxResidualSide} station={step.MaxResidualStation} " +
                $"rlx={step.RelaxationFactor:G17} CL={step.CL:G17} CM={step.CM:G17} CD={step.CD:G17}");
        }

        Assert.True(true);
    }

    // Legacy mapping: managed-only shared settings helper aligned with the existing parity sweep tests.
    // Difference from legacy: The helper just avoids repeating the managed analysis settings boilerplate. Decision: Keep it local to the diagnostic test.
    private static AnalysisSettings CreateXFoilParitySettings(double reynoldsNumber)
    {
        return new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: reynoldsNumber,
            machNumber: 0.0,

            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: 9.0,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);
    }

    // Legacy mapping: none directly; managed geometry helper for the test fixture.
    // Difference from legacy: The classic test fixture builds the NACA geometry through the managed generator rather than interactive NACA commands. Decision: Keep the helper because it matches the rest of the test suite.
    private static (double[] x, double[] y) BuildNaca0012(int panelCount)
    {
        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic("0012", 239);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        return (x, y);
    }

    // Legacy mapping: none; managed-only counter helper shared with parity diagnostics.
    // Difference from legacy: The test harness assigns a unique monotonically increasing counter so new debug artifacts never overwrite older ones.
    // Decision: Keep the helper because it prevents stale-trace confusion during iterative debugging.
    private static long AllocateDebugCounter()
    {
        lock (DebugCounterLock)
        {
            string counterPath = FortranParity.FortranReferenceCases.GetTraceCounterPath();
            string? counterDirectory = Path.GetDirectoryName(counterPath);
            if (!string.IsNullOrWhiteSpace(counterDirectory))
            {
                Directory.CreateDirectory(counterDirectory);
            }

            long current = 0;
            if (File.Exists(counterPath))
            {
                string text = File.ReadAllText(counterPath).Trim();
                _ = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out current);
            }

            long next = current + 1;
            File.WriteAllText(counterPath, next.ToString(CultureInfo.InvariantCulture));
            return next;
        }
    }

    // Legacy mapping: none; managed-only repository helper for diagnostic artifacts.
    // Difference from legacy: This exists only so the test can place numbered debug logs under the shared fortran-debug workspace.
    // Decision: Keep the helper because it keeps ad hoc diagnostics in one predictable location.
    private static string GetFortranDebugDirectory()
    {
        string root = FortranParity.FortranReferenceCases.FindRepositoryRoot();
        return Path.Combine(root, "tools", "fortran-debug");
    }
}

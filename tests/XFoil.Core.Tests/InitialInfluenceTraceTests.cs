using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f initial influence assembly trace lineage
// Secondary legacy source: tools/fortran-debug trace instrumentation
// Role in port: Verifies the managed diagnostic trace that captures the initial influence-matrix state for parity debugging.
// Differences: The managed test writes JSONL diagnostics, which is a port-specific observability layer rather than a direct legacy runtime feature.
// Decision: Keep this managed-only tracing harness because it is required to compare the port against legacy internals.
namespace XFoil.Core.Tests;

[Trait("Category", "Diagnostic")]
public class InitialInfluenceTraceTests
{
    private const int ClassicXFoilNacaPointCount = 239;
    private readonly ITestOutputHelper output;

    public InitialInfluenceTraceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    // Legacy mapping: initial xpanel influence assembly state.
    // Difference from legacy: The trace is emitted as managed JSONL diagnostics rather than through ad hoc debug printing in the Fortran runtime.
    // Decision: Keep the managed tracing test because it underpins parity-debug workflows absent from the original program.
    public void Naca0012_Re1e6_Alpha0_WritesInitialInfluenceTrace()
    {
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: 20,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: 9.0,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);

        string traceDir = FindSolutionRoot("tools/fortran-debug");
        Directory.CreateDirectory(traceDir);
        string tracePath = Path.Combine(traceDir, "csharp_initial_influence_trace.jsonl");

        var geometry = BuildNacaSymmetric0012();
        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
        {
            caseName = "Naca0012_Re1e6_Alpha0_InitialInfluence",
            settings.PanelCount,
            settings.ReynoldsNumber,
            alphaRadians = 0.0
        });
        using var traceScope = SolverTrace.Begin(traceWriter);

        CosineClusteringPanelDistributor.Distribute(
            geometry.x,
            geometry.y,
            geometry.x.Length,
            panel,
            settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            inviscidState,
            settings.FreestreamVelocity,
            settings.MachNumber);

        int nWake = Math.Max((panel.NodeCount / 8) + 2, 3);
        double[,] dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState,
            panel,
            nWake,
            settings.FreestreamVelocity,
            angleOfAttackRadians: 0.0,
            useLegacyWakeSourceKernelPrecision: settings.UseLegacyWakeSourceKernelPrecision);

        output.WriteLine($"Initial influence trace written to: {tracePath}");
        output.WriteLine($"DIJ size: {dij.GetLength(0)} x {dij.GetLength(1)}");

        Assert.True(File.Exists(tracePath), $"Trace file should exist at {tracePath}");
        Assert.True(new FileInfo(tracePath).Length > 100, "Trace file should be non-empty.");
        Assert.True(double.IsFinite(dij[0, 0]));
    }

    private static (double[] x, double[] y) BuildNacaSymmetric0012()
    {
        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic("0012", ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        return (x, y);
    }

    private static string FindSolutionRoot(string subpath)
    {
        if (TryFindRepoRoot(Environment.CurrentDirectory, out string? root) && root is not null)
        {
            return Path.Combine(root, subpath);
        }

        string assemblyDir = Path.GetDirectoryName(typeof(InitialInfluenceTraceTests).Assembly.Location) ?? ".";
        if (TryFindRepoRoot(assemblyDir, out root) && root is not null)
        {
            return Path.Combine(root, subpath);
        }

        return Path.GetFullPath(subpath);
    }

    private static bool TryFindRepoRoot(string startDir, out string? root)
    {
        root = null;
        string? dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "tests")))
            {
                root = dir;
                return true;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }
}

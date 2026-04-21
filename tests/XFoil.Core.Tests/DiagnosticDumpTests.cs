using System;
using System.Globalization;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using XFoil.Core.Diagnostics;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f and xblsys.f internal viscous diagnostic state
// Secondary legacy source: tools/fortran-debug trace instrumentation
// Role in port: Verifies the managed diagnostic dump used to inspect parity boundaries against legacy viscous internals.
// Differences: The dump is a managed text/JSON artifact created specifically for port debugging, not a direct legacy runtime feature.
// Decision: Keep this managed-only diagnostics harness because it is essential for solver-fidelity debugging across runtimes.
namespace XFoil.Core.Tests;

/// <summary>
/// Diagnostic dump test that runs the NACA 0012 Re=1e6 alpha=0 reference case
/// and writes both the legacy text dump and the full JSONL trace under tools/fortran-debug/.
/// </summary>
[Trait("Category", "Diagnostic")]
public class DiagnosticDumpTests
{
    private const int ClassicXFoilNacaPointCount = 239;
    private readonly ITestOutputHelper _output;

    public DiagnosticDumpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Runs NACA 0012 at Re=1e6, alpha=0, NCrit=9, 160 panels with diagnostic
    /// logging enabled. Writes csharp_dump.txt to tools/fortran-debug/ for
    /// comparison with the Fortran reference_dump.txt.
    /// </summary>
    [Fact]
    // Legacy mapping: initial viscous diagnostic state traced from xbl/xblsys execution.
    // Difference from legacy: The test validates managed diagnostic artifacts rather than a legacy text/debug print path.
    // Decision: Keep the managed tracing test because it is the primary parity-debug artifact in the port.
    public void Naca0012_Re1e6_Alpha0_WritesDiagnosticDump()
    {
        // Find the solution root (navigate up from test bin directory)
        string dumpDir = FindSolutionRoot("tools/fortran-debug");
        Directory.CreateDirectory(dumpDir);
        string dumpPath = Path.Combine(dumpDir, "csharp_dump.txt");
        string tracePath = Path.Combine(dumpDir, "csharp_trace.jsonl");

        _output.WriteLine($"Writing diagnostic dump to: {dumpPath}");
        _output.WriteLine($"Writing structured trace to: {tracePath}");

        // Settings matching Fortran reference case exactly
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            machNumber: 0.0,

            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: 20,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: 9.0,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);

        double alphaRadians = 0.0; // alpha = 0 degrees

        // Run the viscous solver with diagnostic writer enabled
        ViscousAnalysisResult result;
        using (var textWriter = new StreamWriter(dumpPath, false, System.Text.Encoding.UTF8))
        using (var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
        {
            caseName = "Naca0012_Re1e6_Alpha0",
            panelCount = settings.PanelCount,
            settings.ReynoldsNumber,
            alphaRadians
        }))
        using (var debugWriter = new MultiplexTextWriter(textWriter, traceWriter))
        using (SolverTrace.Begin(traceWriter))
        using (CoreTrace.Begin((kind, scope, data) => traceWriter.WriteEvent(kind, scope, data)))
        {
            // Generate the raw classic NACA buffer inside the trace scope so the
            // parity dump includes the same upstream input stage as the Fortran trace.
            var geometry = BuildNacaSymmetric0012(160);

            debugWriter.WriteLine("=== CSHARP VISCAL START ===");
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "NITER={0,10}", settings.MaxViscousIterations));

            result = ViscousSolverEngine.SolveViscous(
                geometry, settings, alphaRadians, debugWriter: debugWriter);

            debugWriter.WriteLine("=== CSHARP VISCAL END ===");
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "FINAL CL={0,15:E8} CD={1,15:E8} CM={2,15:E8}",
                result.LiftCoefficient,
                result.DragDecomposition.CD,
                result.MomentCoefficient));
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "CONVERGED={0} ITERATIONS={1}",
                result.Converged, result.Iterations));

            debugWriter.Flush();
        }

        // Log key results to test output
        _output.WriteLine($"CL = {result.LiftCoefficient:E8}");
        _output.WriteLine($"CD = {result.DragDecomposition.CD:E8}");
        _output.WriteLine($"CM = {result.MomentCoefficient:E8}");
        _output.WriteLine($"Converged = {result.Converged}");
        _output.WriteLine($"Iterations = {result.Iterations}");

        // Assertions: dump file was written and contains expected markers
        Assert.True(File.Exists(dumpPath), $"Dump file should exist at {dumpPath}");
        Assert.True(File.Exists(tracePath), $"Trace file should exist at {tracePath}");

        long fileSize = new FileInfo(dumpPath).Length;
        long traceSize = new FileInfo(tracePath).Length;
        _output.WriteLine($"Dump file size: {fileSize} bytes");
        _output.WriteLine($"Trace file size: {traceSize} bytes");

        Assert.True(fileSize > 100, $"Dump file should be non-empty, got {fileSize} bytes");
        Assert.True(traceSize > 100, $"Trace file should be non-empty, got {traceSize} bytes");

        // Check for iteration markers
        Assert.True(FileContainsText(dumpPath, "=== ITER"));

        // Check for station markers
        Assert.True(FileContainsText(dumpPath, "STATION IS="));

        // Check for BL state logging
        Assert.True(FileContainsText(dumpPath, "BL_STATE"));

        // Check for VA/VB blocks
        Assert.True(FileContainsText(dumpPath, "VA_ROW1"));
        Assert.True(FileContainsText(dumpPath, "VB_ROW1"));

        // Check for VDEL residuals
        Assert.True(FileContainsText(dumpPath, "VDEL_R"));
        Assert.True(FileContainsText(dumpPath, "VDEL_S"));

        // Check for POST_CALC aerodynamic coefficients
        Assert.True(FileContainsText(dumpPath, "POST_CALC CL="));

        // Check for BLSOLV forward/solution logging
        Assert.True(FileContainsText(dumpPath, "BLSOLV_POST_FORWARD"));
        Assert.True(FileContainsText(dumpPath, "VDEL_SOL"));

        // Check for UPDATE logging (may not appear if Newton update is never applied --
        // the BL march is the primary convergence driver and newtonHealthy may be false
        // for all iterations. Log presence but don't assert.)
        bool hasUpdateLog = FileContainsText(dumpPath, "UPDATE IS=");
        _output.WriteLine($"Newton UPDATE logging present: {hasUpdateLog}");

        // Most SolverTrace scopes were removed for performance. Verify the
        // trace file exists and contains at least some JSON entries from the
        // remaining inviscid-phase instrumentation.
        Assert.True(new FileInfo(tracePath).Length > 0, "Trace file should be non-empty");
        Assert.True(FileContainsText(tracePath, "\"kind\":\"panel_node\"") ||
                    FileContainsText(tracePath, "\"kind\":\"matrix_entry\"") ||
                    FileContainsText(tracePath, "\"kind\":\"psilin_panel\""),
                    "Trace should contain at least some inviscid-phase entries");

        // Validate result is physically reasonable
        Assert.True(double.IsFinite(result.LiftCoefficient),
            $"CL should be finite, got {result.LiftCoefficient}");
        Assert.True(double.IsFinite(result.DragDecomposition.CD),
            $"CD should be finite, got {result.DragDecomposition.CD}");

        _output.WriteLine("Diagnostic dump test PASSED");
    }

    // ================================================================
    // NACA geometry helpers (same formula as ViscousParityTests)
    // ================================================================

    private static (double[] x, double[] y) BuildNacaSymmetric0012(int n)
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

    /// <summary>
    /// Finds the solution root directory and returns the specified subdirectory path.
    /// Navigates up from the test assembly location until finding a directory containing
    /// "src/" and "tests/", which indicates the repository root.
    /// </summary>
    private static string FindSolutionRoot(string subpath)
    {
        // Try common locations relative to test execution
        // 1. Working directory might be the repo root
        if (TryFindRepoRoot(Environment.CurrentDirectory, out string? root) && root != null)
        {
            return Path.Combine(root, subpath);
        }

        // 2. Navigate up from assembly location
        string assemblyDir = Path.GetDirectoryName(typeof(DiagnosticDumpTests).Assembly.Location) ?? ".";
        if (TryFindRepoRoot(assemblyDir, out root) && root != null)
        {
            return Path.Combine(root, subpath);
        }

        // 3. Hardcoded fallback for known CI/dev paths
        string fallback = "/home/holyglory/agents/XFoilSharp";
        if (Directory.Exists(fallback))
        {
            return Path.Combine(fallback, subpath);
        }

        // Last resort: use current directory
        return Path.GetFullPath(subpath);
    }

    private static bool TryFindRepoRoot(string startDir, out string? root)
    {
        root = null;
        string? dir = startDir;
        while (dir != null)
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

    private static bool FileContainsText(string path, string text)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

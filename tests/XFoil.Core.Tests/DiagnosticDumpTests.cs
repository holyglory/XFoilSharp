using System;
using System.Globalization;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Diagnostic dump test that runs the NACA 0012 Re=1e6 alpha=0 reference case
/// and writes structured intermediate values to tools/fortran-debug/csharp_dump.txt.
/// The output format mirrors the Fortran reference_dump.txt so Plan 13 can compare
/// the two files line-by-line to identify divergence points.
/// </summary>
[Trait("Category", "Diagnostic")]
public class DiagnosticDumpTests
{
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
    public void Naca0012_Re1e6_Alpha0_WritesDiagnosticDump()
    {
        // Find the solution root (navigate up from test bin directory)
        string dumpDir = FindSolutionRoot("tools/fortran-debug");
        Directory.CreateDirectory(dumpDir);
        string dumpPath = Path.Combine(dumpDir, "csharp_dump.txt");

        _output.WriteLine($"Writing diagnostic dump to: {dumpPath}");

        // Generate NACA 0012 geometry (same formula as ViscousParityTests)
        var geometry = BuildNacaSymmetric0012(160);

        // Settings matching Fortran reference case exactly
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
            criticalAmplificationFactor: 9.0);

        double alphaRadians = 0.0; // alpha = 0 degrees

        // Run the viscous solver with diagnostic writer enabled
        ViscousAnalysisResult result;
        using (var writer = new StreamWriter(dumpPath, false, System.Text.Encoding.UTF8))
        {
            writer.WriteLine("=== CSHARP VISCAL START ===");
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "NITER={0,10}", settings.MaxViscousIterations));

            result = ViscousSolverEngine.SolveViscous(
                geometry, settings, alphaRadians, debugWriter: writer);

            writer.WriteLine("=== CSHARP VISCAL END ===");
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "FINAL CL={0,15:E8} CD={1,15:E8} CM={2,15:E8}",
                result.LiftCoefficient,
                result.DragDecomposition.CD,
                result.MomentCoefficient));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "CONVERGED={0} ITERATIONS={1}",
                result.Converged, result.Iterations));

            writer.Flush();
        }

        // Log key results to test output
        _output.WriteLine($"CL = {result.LiftCoefficient:E8}");
        _output.WriteLine($"CD = {result.DragDecomposition.CD:E8}");
        _output.WriteLine($"CM = {result.MomentCoefficient:E8}");
        _output.WriteLine($"Converged = {result.Converged}");
        _output.WriteLine($"Iterations = {result.Iterations}");

        // Assertions: dump file was written and contains expected markers
        Assert.True(File.Exists(dumpPath), $"Dump file should exist at {dumpPath}");

        string dumpContent = File.ReadAllText(dumpPath);
        long fileSize = new FileInfo(dumpPath).Length;
        _output.WriteLine($"Dump file size: {fileSize} bytes");

        Assert.True(fileSize > 100, $"Dump file should be non-empty, got {fileSize} bytes");

        // Check for iteration markers
        Assert.Contains("=== ITER", dumpContent);

        // Check for station markers
        Assert.Contains("STATION IS=", dumpContent);

        // Check for BL state logging
        Assert.Contains("BL_STATE", dumpContent);

        // Check for VA/VB blocks
        Assert.Contains("VA_ROW1", dumpContent);
        Assert.Contains("VB_ROW1", dumpContent);

        // Check for VDEL residuals
        Assert.Contains("VDEL_R", dumpContent);

        // Check for POST_CALC aerodynamic coefficients
        Assert.Contains("POST_CALC CL=", dumpContent);

        // Check for BLSOLV forward/solution logging
        Assert.Contains("BLSOLV_POST_FORWARD", dumpContent);
        Assert.Contains("VDEL_SOL", dumpContent);

        // Check for UPDATE logging (may not appear if Newton update is never applied --
        // the BL march is the primary convergence driver and newtonHealthy may be false
        // for all iterations. Log presence but don't assert.)
        bool hasUpdateLog = dumpContent.Contains("UPDATE IS=");
        _output.WriteLine($"Newton UPDATE logging present: {hasUpdateLog}");

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
        return BuildNacaCambered(0.0, 0.0, 0.12, n);
    }

    /// <summary>
    /// Build a general NACA 4-digit airfoil with parameters (m, p, t).
    /// Generates (n+1) points: upper surface TE->LE then lower surface LE->TE.
    /// Uses cosine spacing matching XFoil's NACA paneling.
    /// </summary>
    private static (double[] x, double[] y) BuildNacaCambered(
        double m, double p, double t, int n)
    {
        int half = n / 2;
        double[] xCoords = new double[n + 1];
        double[] yCoords = new double[n + 1];

        for (int i = 0; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 + Math.Cos(theta));
            double yt = 5.0 * t * (0.2969 * Math.Sqrt(xc) - 0.1260 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);

            double yc = 0.0, dyc = 0.0;
            if (m > 0 && p > 0)
            {
                if (xc < p)
                {
                    yc = m / (p * p) * (2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / (p * p) * (p - xc);
                }
                else
                {
                    yc = m / ((1.0 - p) * (1.0 - p)) * (1.0 - 2.0 * p + 2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / ((1.0 - p) * (1.0 - p)) * (p - xc);
                }
            }

            double theta2 = Math.Atan(dyc);
            xCoords[i] = xc - yt * Math.Sin(theta2);
            yCoords[i] = yc + yt * Math.Cos(theta2);
        }

        for (int i = 1; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 - Math.Cos(theta));
            double yt = 5.0 * t * (0.2969 * Math.Sqrt(xc) - 0.1260 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);

            double yc = 0.0, dyc = 0.0;
            if (m > 0 && p > 0)
            {
                if (xc < p)
                {
                    yc = m / (p * p) * (2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / (p * p) * (p - xc);
                }
                else
                {
                    yc = m / ((1.0 - p) * (1.0 - p)) * (1.0 - 2.0 * p + 2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / ((1.0 - p) * (1.0 - p)) * (p - xc);
                }
            }

            double theta2 = Math.Atan(dyc);
            xCoords[half + i] = xc + yt * Math.Sin(theta2);
            yCoords[half + i] = yc - yt * Math.Cos(theta2);
        }

        return (xCoords, yCoords);
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
}

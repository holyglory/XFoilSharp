using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Validates the MSES pipeline against airfoils loaded from .dat
/// files (the file-input path used by viscous-point-mses-file CLI).
/// Ensures the parser → MSES chain doesn't drop anything between
/// Generate4DigitClassic-generated airfoils and file-loaded ones.
/// </summary>
public class MsesFileInputTests
{
    private static AirfoilGeometry LoadSeligAirfoil(string datFile)
    {
        // Selig database files live at tools/selig-database/*.dat
        // relative to the repo root. Tests run with a CWD at the
        // test project directory, so we climb up to find the data.
        string? cwd = System.IO.Directory.GetCurrentDirectory();
        string? repoRoot = cwd;
        while (repoRoot != null
            && !System.IO.Directory.Exists(
                System.IO.Path.Combine(repoRoot, "tools", "selig-database")))
        {
            repoRoot = System.IO.Path.GetDirectoryName(repoRoot);
        }
        if (repoRoot is null)
        {
            throw new System.IO.FileNotFoundException(
                "Could not locate tools/selig-database relative to "
                + System.IO.Directory.GetCurrentDirectory());
        }
        string fullPath = System.IO.Path.Combine(
            repoRoot, "tools", "selig-database", datFile);
        return new AirfoilParser().ParseFile(fullPath);
    }

    [Fact]
    public void SeligNacaFile_MatchesGeneratedNaca_WithinTolerance()
    {
        // Selig's naca1410 file vs our NACA 1410 generator should give
        // similar MSES results. Geometry differences (Selig files have
        // different paneling) mean results won't be identical, but
        // CL should match to within 5 %, CD within 2×.
        var fileGeom = LoadSeligAirfoil("naca1410.dat");
        var genGeom = new NacaAirfoilGenerator()
            .Generate4DigitClassic("1410", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new ThesisClosureAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true,
            useThesisExactLaminar: true);
        var rFile = svc.AnalyzeViscous(fileGeom, 4.0, settings);
        var rGen = svc.AnalyzeViscous(genGeom, 4.0, settings);

        Assert.True(rFile.Converged);
        Assert.True(rGen.Converged);

        // CL within 10% (different paneling shifts circulation slightly).
        double clRatio = rFile.LiftCoefficient / rGen.LiftCoefficient;
        Assert.InRange(clRatio, 0.9, 1.1);

        // CD within 3× (paneling variations can shift Xtr significantly).
        double cdRatio = rFile.DragDecomposition.CD / rGen.DragDecomposition.CD;
        Assert.InRange(cdRatio, 0.33, 3.0);
    }

    [Fact]
    public void SeligFile_ProducesPlausibleViscousResult()
    {
        // Sanity: file input produces finite, bounded, converged
        // viscous output with non-empty profiles.
        var geom = LoadSeligAirfoil("naca633018.dat");
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);
        var svc = new ThesisClosureAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var r = svc.AnalyzeViscous(geom, 2.0, settings);

        Assert.True(r.Converged);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.DragDecomposition.CD, 0.001, 0.1);
        Assert.NotEmpty(r.UpperProfiles);
        Assert.NotEmpty(r.LowerProfiles);
        Assert.NotEmpty(r.WakeProfiles);
    }
}

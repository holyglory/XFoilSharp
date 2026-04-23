using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Compares the uncoupled MSES pipeline (Phase-5 stub: inviscid CL
/// + Squire-Young CD) against the production Modern viscous service
/// on a small benchmark set. These aren't parity tests — the MSES
/// implementation lacks viscous Ue feedback, so CL will exactly
/// match the inviscid value while CD comes from the thesis closure.
///
/// Purpose: (1) confirm MSES viscous runs without NaN/crash on real
/// airfoils, (2) provide a running record of how close Squire-Young
/// CD gets to the Newton-coupled CD before Phase 5 lands.
/// </summary>
public class MsesVsModernBenchmarkTests
{
    private static AnalysisSettings BuildSettings(double re, double mach = 0.0)
    {
        return new AnalysisSettings(
            panelCount: 161,
            freestreamVelocity: 1.0,
            machNumber: mach,
            reynoldsNumber: re);
    }

    [Theory]
    [InlineData("0012", 0.0, 1_000_000)]
    [InlineData("0012", 4.0, 1_000_000)]
    [InlineData("2412", 4.0, 3_000_000)]
    [InlineData("4412", 0.0, 3_000_000)]
    public void MsesVsModern_ProducesSameOrderOfMagnitudeCd(
        string naca, double alphaDeg, double Re)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var settings = BuildSettings(Re);

        var mses = new ThesisClosureAnalysisService();
        var modern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();

        var msesResult = mses.AnalyzeViscous(geom, alphaDeg, settings);
        ViscousAnalysisResult? modernResult = null;
        try { modernResult = modern.AnalyzeViscous(geom, alphaDeg, settings); }
        catch { /* modern may fail on some configs, ignore */ }

        // MSES must not NaN.
        Assert.True(double.IsFinite(msesResult.LiftCoefficient),
            $"MSES CL NaN on NACA {naca} α={alphaDeg} Re={Re}");
        Assert.True(double.IsFinite(msesResult.DragDecomposition.CD),
            $"MSES CD NaN on NACA {naca} α={alphaDeg} Re={Re}");

        // If Modern also ran, their CD should be same order of magnitude.
        if (modernResult is not null && modernResult.Converged
            && double.IsFinite(modernResult.DragDecomposition.CD))
        {
            double ratio = msesResult.DragDecomposition.CD
                           / System.Math.Max(modernResult.DragDecomposition.CD, 1e-8);
            // Accept 0.1× to 10× (order of magnitude). This is a
            // working tolerance for the uncoupled-MSES prototype.
            Assert.InRange(ratio, 0.1, 10.0);
        }
    }
}

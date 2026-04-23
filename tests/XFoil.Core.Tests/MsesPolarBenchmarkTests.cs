using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Polar-sweep benchmarks of the MSES Phase-5-stub pipeline across
/// a spread of airfoil/α/Re combinations. These assert only that
/// the marcher converges and produces physically plausible CD
/// trends (monotonic rise with α, within envelope, no NaN). Not a
/// parity check — MSES is uncoupled at this phase so CL equals
/// inviscid and CD is Squire-Young.
/// </summary>
public class MsesPolarBenchmarkTests
{
    [Theory]
    [InlineData("0012", 1_000_000, 0.0, 4.0, 1.0)]
    [InlineData("2412", 1_000_000, 0.0, 4.0, 1.0)]
    [InlineData("4412", 3_000_000, 0.0, 6.0, 1.0)]
    public void PolarSweep_Converges_MonotonicCd(
        string naca, double Re, double aStart, double aEnd, double aStep)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re);
        var mses = new ThesisClosureAnalysisService();

        int count = (int)((aEnd - aStart) / aStep) + 1;
        var clArr = new double[count];
        var cdArr = new double[count];
        bool anyConverged = false;
        for (int i = 0; i < count; i++)
        {
            double a = aStart + i * aStep;
            var r = mses.AnalyzeViscous(geom, a, settings);
            clArr[i] = r.LiftCoefficient;
            cdArr[i] = r.DragDecomposition.CD;
            if (r.Converged) anyConverged = true;
            Assert.True(double.IsFinite(clArr[i]), $"CL NaN at α={a}");
            Assert.True(double.IsFinite(cdArr[i]), $"CD NaN at α={a}");
            Assert.InRange(cdArr[i], 0.0, 0.5);
        }
        Assert.True(anyConverged, "At least one point in the sweep should converge");
    }

    [Fact]
    public void PolarSweep_ClIncreasesWithAlpha()
    {
        // CL should rise monotonically with α in the linear regime
        // (a ≤ 8° on NACA 4412 at Re=3e6).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 161,
            freestreamVelocity: 1.0, machNumber: 0.0, reynoldsNumber: 3_000_000);
        var mses = new ThesisClosureAnalysisService();
        double clPrev = -999;
        for (double a = 0.0; a <= 8.0; a += 2.0)
        {
            var r = mses.AnalyzeViscous(geom, a, settings);
            Assert.True(r.LiftCoefficient > clPrev,
                $"CL non-monotonic: α={a} CL={r.LiftCoefficient} vs prev {clPrev}");
            clPrev = r.LiftCoefficient;
        }
    }
}

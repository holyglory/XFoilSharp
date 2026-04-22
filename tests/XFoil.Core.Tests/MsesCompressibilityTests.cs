using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Compressibility propagation through the MSES pipeline. Drela's
/// closure relations have Me-dependent corrections:
/// - Hk = (H − 0.290·Me²) / (1 + 0.113·Me²)
/// - Turbulent H* has `(baseValue + 0.028·Me²) / (1 + 0.014·Me²)`
/// - Us has `(1 + 0.014·Me²)` factor
///
/// These corrections are small at M ≤ 0.3 but non-zero. Tests here
/// pin that:
/// 1. M=0 and M=0.3 produce different (Hk, CL, CD) — the correction
///    actually propagates.
/// 2. Results at M=0.3 are still finite/plausible (no blow-up from
///    Karman-Tsien on the inviscid side).
/// </summary>
public class MsesCompressibilityTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    [InlineData(0.3)]
    public void Naca0012_Alpha4_VariousMach_ProducesPlausibleResult(double mach)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: mach,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var r = svc.AnalyzeViscous(geom, 4.0, settings);

        Assert.True(r.Converged);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        // CL rises under compressibility (Karman-Tsien).
        Assert.InRange(r.LiftCoefficient, 0.4, 0.7);
        Assert.InRange(r.DragDecomposition.CD, 0.002, 0.03);
    }

    [Fact]
    public void Compressibility_ActuallyPropagates_M0Vs_M03_AreDifferent()
    {
        // If the Mach number weren't propagating, M=0 and M=0.3 would
        // give identical results. They must differ — in the right
        // direction: CL rises under compressibility (Karman-Tsien).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);

        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);

        var settingsM0 = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);
        var settingsM03 = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.3,
            reynoldsNumber: 3_000_000);

        var rM0 = svc.AnalyzeViscous(geom, 4.0, settingsM0);
        var rM03 = svc.AnalyzeViscous(geom, 4.0, settingsM03);

        // Karman-Tsien: CL(M) = CL(0) / √(1 − M²) in first approx.
        // At M=0.3: 1/√0.91 ≈ 1.049, so CL should rise by ~5%.
        Assert.NotEqual(rM0.LiftCoefficient, rM03.LiftCoefficient);
        Assert.True(rM03.LiftCoefficient > rM0.LiftCoefficient,
            $"Expected CL(M=0.3) > CL(M=0): {rM0.LiftCoefficient} vs {rM03.LiftCoefficient}");
        double liftRatio = rM03.LiftCoefficient / rM0.LiftCoefficient;
        Assert.InRange(liftRatio, 1.01, 1.2);
    }
}

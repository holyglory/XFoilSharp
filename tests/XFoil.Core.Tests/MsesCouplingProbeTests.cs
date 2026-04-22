using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Probe tests for the Phase-5-lite viscous-inviscid coupling
/// (MsesAnalysisService ctor param viscousCouplingIterations).
/// Verifies that enabling iteration doesn't regress the un-coupled
/// baseline — if coupling diverges, the service should fall back
/// to the last valid result, not propagate NaN.
/// </summary>
public class MsesCouplingProbeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Naca4412_Alpha8_Coupling_ConvergesAndFinite(int iterations)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(
            viscousCouplingIterations: iterations,
            viscousCouplingRelaxation: 0.3,
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        var r = svc.AnalyzeViscous(geom, 8.0, settings);

        Assert.True(r.Converged);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.LiftCoefficient, 0.5, 2.0);
        Assert.InRange(r.DragDecomposition.CD, 0.001, 0.1);
    }

    [Fact]
    public void IterationsField_ReflectsActualCouplingIterations()
    {
        // Uncoupled (viscousCouplingIterations=0): Iterations = 1
        // (the single uncoupled pass).
        // Coupled (viscousCouplingIterations=2): Iterations should be
        // between 1 (zero accepted) and 3 (both accepted).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svcBase = new MsesAnalysisService(useThesisExactTurbulent: true);
        var svcCoup = new MsesAnalysisService(
            viscousCouplingIterations: 2,
            viscousCouplingRelaxation: 0.1,
            useThesisExactTurbulent: true);

        var rBase = svcBase.AnalyzeViscous(geom, 2.0, settings);
        var rCoup = svcCoup.AnalyzeViscous(geom, 2.0, settings);

        Assert.Equal(1, rBase.Iterations);
        Assert.InRange(rCoup.Iterations, 1, 3);
    }

    [Fact]
    public void Coupling_DoesntChangeResultOnVeryLowRelaxation()
    {
        // With relaxation = 0 (no geometric offset), coupling should
        // reproduce the uncoupled result exactly.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svcBase = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var svcCoup = new MsesAnalysisService(
            viscousCouplingIterations: 3,
            viscousCouplingRelaxation: 0.0,
            useThesisExactTurbulent: true, useWakeMarcher: true);

        var rBase = svcBase.AnalyzeViscous(geom, 4.0, settings);
        var rCoup = svcCoup.AnalyzeViscous(geom, 4.0, settings);

        // At zero relaxation the thickened geometry is identical to
        // the original, so results must match within numerical noise.
        Assert.Equal(rBase.LiftCoefficient, rCoup.LiftCoefficient, 6);
        Assert.Equal(rBase.DragDecomposition.CD, rCoup.DragDecomposition.CD, 6);
    }
}

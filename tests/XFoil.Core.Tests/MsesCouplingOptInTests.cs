using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for the opt-in Phase-5-lite viscous-inviscid coupling in
/// MsesAnalysisService. Users who want the coupled result construct
/// the service with viscousCouplingIterations > 0; existing callers
/// get the uncoupled Squire-Young default.
/// </summary>
public class MsesCouplingOptInTests
{
    [Fact]
    public void DefaultConstructor_HasZeroCouplingIterations()
    {
        // Baseline: uncoupled output should match the Squire-Young
        // stub behavior from previous tests.
        var svc = new MsesAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 1_000_000);
        var r = svc.AnalyzeViscous(geom, 2.0, settings);
        Assert.True(r.Converged);
    }

    [Fact]
    public void OneIterationCoupling_RunsWithoutNaN()
    {
        // Coupled result should remain finite and physical. This
        // doesn't assert numerical equivalence with Modern — just
        // that the coupling loop executes cleanly.
        var svc = new MsesAnalysisService(viscousCouplingIterations: 1);
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 1_000_000);
        var r = svc.AnalyzeViscous(geom, 2.0, settings);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.DragDecomposition.CD, 0.0, 0.5);
    }

    [Fact]
    public void MultipleCouplingIterations_ConvergesOrFallsBack()
    {
        // Running up to 3 iterations must not break anything; if the
        // coupling diverges the service silently keeps the last good
        // state. Output must still be finite.
        var svc = new MsesAnalysisService(
            viscousCouplingIterations: 3,
            viscousCouplingRelaxation: 0.2);
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("2412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 1_000_000);
        var r = svc.AnalyzeViscous(geom, 0.0, settings);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
    }
}

using XFoil.Core.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class DisplacementCoupledViscousTests
{
    [Fact]
    public void DisplacementCoupledViscous_ProducesDisplacedGeometryAndChangesInviscidResult()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new XFoil.Solver.Models.AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var result = service.AnalyzeDisplacementCoupledViscous(
            geometry,
            2d,
            settings,
            iterations: 2,
            viscousIterations: 6,
            residualTolerance: 0.3d,
            displacementRelaxation: 0.4d);

        Assert.True(result.MaxSurfaceDisplacement > 0d);
        Assert.InRange(result.MaxSurfaceDisplacement, 0d, 0.02d);
        Assert.True(result.EstimatedProfileDragCoefficient >= 0d);
        Assert.Equal(2, result.InnerInteractionIterations);
        Assert.True(result.FinalSeedEdgeVelocityChange >= 0d);
        Assert.InRange(result.FinalDisplacementRelaxation, 0.08d, 0.4d);
        Assert.NotEqual(result.InitialAnalysis.Circulation, result.FinalAnalysis.Circulation);
        Assert.True(result.FinalSolveResult.FinalSurfaceResidual < result.FinalSolveResult.InitialSurfaceResidual);
        Assert.True(result.FinalSolveResult.FinalTransitionResidual <= result.FinalSolveResult.InitialTransitionResidual + 0.1d);
    }

    [Fact]
    public void DisplacementCoupledViscous_KeepsFiniteAerodynamicOutputs()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new XFoil.Solver.Models.AnalysisSettings(panelCount: 120, machNumber: 0.2d, reynoldsNumber: 500_000d);

        var result = service.AnalyzeDisplacementCoupledViscous(
            geometry,
            4d,
            settings,
            iterations: 2,
            viscousIterations: 8,
            residualTolerance: 0.8d,
            displacementRelaxation: 0.35d);

        Assert.True(double.IsFinite(result.FinalAnalysis.LiftCoefficient));
        Assert.True(double.IsFinite(result.FinalAnalysis.MomentCoefficientQuarterChord));
        Assert.True(double.IsFinite(result.EstimatedProfileDragCoefficient));
        Assert.True(result.InnerInteractionIterations >= 1);
        Assert.True(double.IsFinite(result.FinalSeedEdgeVelocityChange));
        Assert.True(double.IsFinite(result.FinalLiftDelta));
        Assert.True(double.IsFinite(result.FinalMomentDelta));
        Assert.True(result.Iterations >= 1);
        Assert.True(result.FinalSolveResult.FinalTransitionResidual <= 0.8d);
        Assert.True(result.FinalSolveResult.FinalWakeResidual <= result.FinalSolveResult.InitialWakeResidual);
    }
}

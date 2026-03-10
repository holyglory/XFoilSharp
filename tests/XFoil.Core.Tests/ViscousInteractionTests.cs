using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousInteractionTests
{
    [Fact]
    public void ViscousInteraction_ChangesSeedEdgeVelocitiesAndKeepsFiniteSolve()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var result = service.AnalyzeViscousInteraction(
            geometry,
            2d,
            settings,
            interactionIterations: 2,
            couplingFactor: 0.12d,
            viscousIterations: 6,
            residualTolerance: 0.3d);

        Assert.True(result.AverageRelativeEdgeVelocityChange > 0d);
        Assert.True(result.FinalIterationRelativeEdgeVelocityChange >= 0d);
        Assert.True(result.SolveResult.FinalSurfaceResidual < result.SolveResult.InitialSurfaceResidual);
        Assert.True(result.SolveResult.FinalTransitionResidual <= result.SolveResult.InitialTransitionResidual + 0.1d);
        Assert.True(result.SolveResult.FinalWakeResidual <= result.SolveResult.InitialWakeResidual);
    }

    [Fact]
    public void ViscousInteraction_CanConvergeUnderLooseTolerance()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(panelCount: 120, machNumber: 0.2d, reynoldsNumber: 500_000d);

        var result = service.AnalyzeViscousInteraction(
            geometry,
            4d,
            settings,
            interactionIterations: 2,
            couplingFactor: 0.10d,
            viscousIterations: 8,
            residualTolerance: 0.8d);

        Assert.True(result.SolveResult.Converged);
        Assert.True(result.Converged);
        Assert.True(result.SolveResult.FinalSurfaceResidual <= 0.8d);
        Assert.True(result.SolveResult.FinalTransitionResidual <= 0.8d);
        Assert.True(result.SolveResult.FinalWakeResidual <= 0.8d);
    }
}

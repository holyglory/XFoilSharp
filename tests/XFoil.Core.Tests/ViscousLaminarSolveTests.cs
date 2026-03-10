using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousLaminarSolveTests
{
    [Fact]
    public void ViscousLaminarSolve_ReducesSurfaceResidualAndReportsFiniteState()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var result = service.AnalyzeViscousLaminarSolve(geometry, 2d, settings, maxIterations: 6, residualTolerance: 0.3d);

        Assert.True(result.FinalSurfaceResidual < result.InitialSurfaceResidual);
        Assert.True(result.FinalTransitionResidual <= result.InitialTransitionResidual);
        Assert.True(result.FinalWakeResidual <= result.InitialWakeResidual);
        Assert.True(result.Iterations > 0);
        Assert.All(result.SolvedSystem.State.UpperSurface.Stations, AssertStation);
        Assert.All(result.SolvedSystem.State.LowerSurface.Stations, AssertStation);
        Assert.All(result.SolvedSystem.State.Wake.Stations, AssertStation);
    }

    [Fact]
    public void ViscousLaminarSolve_CanMeetLooseResidualTolerance()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(panelCount: 120, machNumber: 0.2d, reynoldsNumber: 500_000d);

        var result = service.AnalyzeViscousLaminarSolve(geometry, 4d, settings, maxIterations: 8, residualTolerance: 0.8d);

        Assert.True(result.Converged);
        Assert.True(result.FinalSurfaceResidual <= 0.8d);
        Assert.True(result.FinalTransitionResidual <= 0.8d);
        Assert.True(result.FinalWakeResidual <= 0.8d);
    }

    [Fact]
    public void ViscousLaminarSolve_KeepsAmplificationMonotonicAcrossTransition()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000d,
            transitionReynoldsTheta: 120d,
            criticalAmplificationFactor: 3d);

        var result = service.AnalyzeViscousLaminarSolve(geometry, 4d, settings, maxIterations: 6, residualTolerance: 0.8d);

        AssertBranchTransitionBehavior(result.SolvedSystem.State.UpperSurface.Stations, settings.CriticalAmplificationFactor);
        AssertBranchTransitionBehavior(result.SolvedSystem.State.LowerSurface.Stations, settings.CriticalAmplificationFactor);
    }

    private static void AssertStation(ViscousStationState station)
    {
        Assert.True(station.MomentumThickness > 0d);
        Assert.True(station.DisplacementThickness >= station.MomentumThickness);
        Assert.True(station.ShapeFactor > 1d);
        Assert.True(station.ReynoldsTheta > 0d);
        Assert.True(station.AmplificationFactor >= 0d);
    }

    private static void AssertBranchTransitionBehavior(IReadOnlyList<ViscousStationState> stations, double criticalAmplificationFactor)
    {
        var seenTurbulent = false;
        var previousAmplification = 0d;

        foreach (var station in stations)
        {
            Assert.True(station.AmplificationFactor >= previousAmplification - 1e-9);
            previousAmplification = station.AmplificationFactor;

            if (station.Regime == ViscousFlowRegime.Turbulent)
            {
                Assert.True(station.AmplificationFactor >= criticalAmplificationFactor - 1e-9);
                seenTurbulent = true;
            }

            if (seenTurbulent)
            {
                Assert.Equal(ViscousFlowRegime.Turbulent, station.Regime);
            }
        }

        Assert.True(seenTurbulent);
    }
}

using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class TransitionModelTests
{
    [Fact]
    public void ViscousInitialState_TracksAmplificationGrowthAndStickyTransition()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000d,
            transitionReynoldsTheta: 120d,
            criticalAmplificationFactor: 3d);

        var state = service.AnalyzeViscousInitialState(geometry, 4d, settings);

        AssertBranchTransitionBehavior(state.UpperSurface.Stations, settings.CriticalAmplificationFactor);
        AssertBranchTransitionBehavior(state.LowerSurface.Stations, settings.CriticalAmplificationFactor);
    }

    [Fact]
    public void ViscousInitialState_CanDelayTransitionWithHigherCriticalAmplification()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var earlySettings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000d,
            transitionReynoldsTheta: 120d,
            criticalAmplificationFactor: 2.5d);
        var delayedSettings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000d,
            transitionReynoldsTheta: 120d,
            criticalAmplificationFactor: 8d);

        var earlyState = service.AnalyzeViscousInitialState(geometry, 4d, earlySettings);
        var delayedState = service.AnalyzeViscousInitialState(geometry, 4d, delayedSettings);

        Assert.True(FindTransitionXi(delayedState.UpperSurface.Stations) > FindTransitionXi(earlyState.UpperSurface.Stations));
        Assert.True(FindTransitionXi(delayedState.LowerSurface.Stations) > FindTransitionXi(earlyState.LowerSurface.Stations));
    }

    private static void AssertBranchTransitionBehavior(IReadOnlyList<ViscousStationState> stations, double criticalAmplificationFactor)
    {
        Assert.NotEmpty(stations);

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

    private static double FindTransitionXi(IReadOnlyList<ViscousStationState> stations)
    {
        foreach (var station in stations)
        {
            if (station.Regime == ViscousFlowRegime.Turbulent)
            {
                return station.Xi;
            }
        }

        return double.PositiveInfinity;
    }
}

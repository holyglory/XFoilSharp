using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: DAMPL, DAMPL2, TRCHEK2
// Secondary legacy source: f_xfoil/src/xbl.f initial transition-state setup
// Role in port: Verifies managed transition behavior surfaced through the viscous initial-state analysis.
// Differences: The tests inspect immutable managed station objects instead of legacy amplification and regime arrays.
// Decision: Keep the managed state-based checks because they preserve the same transition progression behavior at the public API boundary.
namespace XFoil.Core.Tests;

public sealed class TransitionModelTests
{
    [Fact]
    // Legacy mapping: xblsys amplification growth and sticky-transition behavior.
    // Difference from legacy: The managed test asserts regime progression on typed station objects instead of internal legacy arrays. Decision: Keep the managed regression because it directly protects the ported transition-state semantics.
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
    // Legacy mapping: xblsys sensitivity of transition location to NCrit.
    // Difference from legacy: The managed test compares two explicit settings objects rather than changing interactive runtime state. Decision: Keep the managed comparison because it clearly documents the preserved delayed-transition behavior.
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

using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f initial boundary-layer state setup
// Secondary legacy source: f_xfoil/src/xblsys.f laminar/wake seed assembly
// Role in port: Verifies the managed viscous initial-state estimator derived from the legacy boundary-layer initialization workflow.
// Differences: The test inspects structured branch/station objects instead of the mutable legacy state arrays.
// Decision: Keep the managed state-object tests because they expose the same initialization invariants more clearly.
namespace XFoil.Core.Tests;

public sealed class ViscousInitialStateTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f initial surface branch setup.
    // Difference from legacy: The managed test asserts physical positivity on explicit station objects rather than on legacy work arrays.
    // Decision: Keep the managed invariant because it is the clearest regression for initial surface-state construction.
    public void ViscousInitialState_ProducesPositiveThicknessOnSurfaceBranches()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var state = service.AnalyzeViscousInitialState(geometry, 2d, settings);

        Assert.All(state.UpperSurface.Stations, station =>
        {
            Assert.True(station.MomentumThickness > 0d);
            Assert.True(station.DisplacementThickness > station.MomentumThickness);
            Assert.True(station.ShapeFactor > 1d);
        });

        Assert.All(state.LowerSurface.Stations, station =>
        {
            Assert.True(station.MomentumThickness > 0d);
            Assert.True(station.DisplacementThickness > station.MomentumThickness);
            Assert.True(station.SkinFrictionCoefficient >= 0d);
        });
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f wake initialization path.
    // Difference from legacy: Wake-gap and thickness continuity are checked through immutable managed results instead of internal legacy buffers.
    // Decision: Keep the managed wake-state regression because it documents the same initialization guarantees at the public API.
    public void ViscousInitialState_WakeStartsFromFiniteThicknessAndKeepsWakeGap()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 500_000d);

        var state = service.AnalyzeViscousInitialState(geometry, 4d, settings);

        Assert.True(state.Wake.Stations[0].MomentumThickness > 0d);
        Assert.True(state.Wake.Stations[0].DisplacementThickness >= state.Wake.Stations[0].MomentumThickness);
        Assert.Equal(ViscousFlowRegime.Wake, state.Wake.Stations[0].Regime);
        Assert.True(state.Wake.Stations[^1].ReynoldsTheta >= state.Wake.Stations[0].ReynoldsTheta);
        Assert.All(state.Wake.Stations, station => Assert.True(station.WakeGap >= 0d));
    }
}

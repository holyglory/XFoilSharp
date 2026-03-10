using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousInitialStateTests
{
    [Fact]
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

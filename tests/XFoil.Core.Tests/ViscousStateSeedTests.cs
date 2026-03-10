using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousStateSeedTests
{
    [Fact]
    public void ViscousStateSeed_ProducesMonotonicXiOnAllBranches()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);

        var seed = service.AnalyzeViscousStateSeed(geometry, 2d, new AnalysisSettings(panelCount: 120));

        AssertMonotonic(seed.UpperSurface.Stations, 0d);
        AssertMonotonic(seed.LowerSurface.Stations, 0d);
        AssertMonotonic(seed.Wake.Stations, seed.LowerSurface.Stations[^1].Xi);
    }

    [Fact]
    public void ViscousStateSeed_ProvidesWakeVelocityAndNonNegativeGap()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var seed = service.AnalyzeViscousStateSeed(geometry, 4d, new AnalysisSettings(panelCount: 120));

        Assert.True(seed.Wake.Stations.Count >= 2);
        Assert.True(seed.Wake.Stations[1].EdgeVelocity > 0d);
        Assert.All(seed.Wake.Stations, station => Assert.True(station.WakeGap >= 0d));
        Assert.True(seed.TrailingEdgeGap >= 0d);
    }

    private static void AssertMonotonic(IReadOnlyList<ViscousStationSeed> stations, double expectedStartXi)
    {
        Assert.Equal(expectedStartXi, stations[0].Xi, 12);

        for (var index = 1; index < stations.Count; index++)
        {
            Assert.True(stations[index].Xi > stations[index - 1].Xi);
        }
    }
}

using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f viscous seed/station coordinate setup
// Secondary legacy source: f_xfoil/src/xblsys.f wake seed continuation
// Role in port: Verifies the managed viscous seed builder that prepares the station coordinates and wake seed state before Newton assembly.
// Differences: The managed test reads immutable seed objects instead of legacy common-block arrays.
// Decision: Keep the managed seed representation because it preserves the same setup behavior with better observability and composition.
namespace XFoil.Core.Tests;

public sealed class ViscousStateSeedTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f branch seed coordinate construction.
    // Difference from legacy: The monotonic xi progression is asserted through the managed seed object instead of inferred from downstream assembly success.
    // Decision: Keep the managed invariant because it is a direct regression for the legacy seed ordering rule.
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
    // Legacy mapping: f_xfoil/src/xbl.f wake seed initialization.
    // Difference from legacy: Wake velocity and gap values are read directly from the managed seed result instead of from transient solver arrays.
    // Decision: Keep the managed test because it protects the public pre-Newton seed contract.
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

using XFoil.Core.Services;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: STFIND, UICALC
// Secondary legacy source: f_xfoil/src/xblsys.f
// Role in port: Verifies the managed boundary-layer topology builder derived from the legacy stagnation-finding and branch-construction logic.
// Differences: The test drives a managed analysis service rather than the legacy interactive viscous setup, but it checks the same topology invariants.
// Decision: Keep the managed topology checks because the port exposes branch construction as an analysis result rather than internal transient state.
namespace XFoil.Core.Tests;

public sealed class BoundaryLayerTopologyTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f :: STFIND.
    // Difference from legacy: The managed test asserts the stagnation location directly from the topology result instead of from internal legacy arrays.
    // Decision: Keep the managed invariant because it validates the same stagnation-finding outcome at the public boundary.
    public void SymmetricAirfoilAtZeroAlpha_PlacesStagnationNearLeadingEdge()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var topology = service.AnalyzeBoundaryLayerTopology(geometry, 0d, new XFoil.Solver.Models.AnalysisSettings(panelCount: 120));

        Assert.InRange(topology.StagnationPoint.X, -0.05d, 0.15d);
        Assert.True(topology.UpperSurfaceStations.Count > 1);
        Assert.True(topology.LowerSurfaceStations.Count > 1);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f surface and wake branch station construction.
    // Difference from legacy: Increasing branch distances are checked through immutable managed station objects instead of mutable legacy work arrays.
    // Decision: Keep the managed representation because it makes the legacy topology rules easier to verify.
    public void BoundaryLayerTopology_ProducesIncreasingBranchDistances()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);

        var topology = service.AnalyzeBoundaryLayerTopology(geometry, 2d, new XFoil.Solver.Models.AnalysisSettings(panelCount: 120));

        Assert.Equal(0d, topology.UpperSurfaceStations[0].DistanceFromStagnation, 12);
        Assert.Equal(0d, topology.LowerSurfaceStations[0].DistanceFromStagnation, 12);

        for (var index = 1; index < topology.UpperSurfaceStations.Count; index++)
        {
            Assert.True(topology.UpperSurfaceStations[index].DistanceFromStagnation > topology.UpperSurfaceStations[index - 1].DistanceFromStagnation);
        }

        for (var index = 1; index < topology.LowerSurfaceStations.Count; index++)
        {
            Assert.True(topology.LowerSurfaceStations[index].DistanceFromStagnation > topology.LowerSurfaceStations[index - 1].DistanceFromStagnation);
        }

        Assert.Equal(
            topology.LowerSurfaceStations[^1].DistanceFromStagnation,
            topology.WakeStations[0].DistanceFromStagnation,
            10);
    }
}

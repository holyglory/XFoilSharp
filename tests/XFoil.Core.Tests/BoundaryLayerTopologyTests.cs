using XFoil.Core.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class BoundaryLayerTopologyTests
{
    [Fact]
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

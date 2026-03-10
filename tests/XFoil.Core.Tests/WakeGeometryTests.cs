using XFoil.Core.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class WakeGeometryTests
{
    [Fact]
    public void InviscidAnalysis_ProducesWakeWithIncreasingDownstreamDistance()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscid(geometry, 4d);

        Assert.True(result.Wake.Points.Count >= 6);
        for (var index = 1; index < result.Wake.Points.Count; index++)
        {
            Assert.True(result.Wake.Points[index].DistanceFromTrailingEdge > result.Wake.Points[index - 1].DistanceFromTrailingEdge);
        }
    }

    [Fact]
    public void WakeStartsAtTrailingEdgeAndPointsDownstream()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);

        var result = service.AnalyzeInviscid(geometry, 2d);
        var wake = result.Wake;

        Assert.InRange(wake.Points[0].DistanceFromTrailingEdge, -1e-9, 1e-9);
        Assert.True(wake.Points[^1].Location.X > wake.Points[0].Location.X);
    }

    [Fact]
    public void PositiveAlphaWake_DeflectsBelowTrailingEdge()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscid(geometry, 4d);

        Assert.True(result.Wake.Points[^1].Location.Y < result.Wake.Points[0].Location.Y);
    }
}

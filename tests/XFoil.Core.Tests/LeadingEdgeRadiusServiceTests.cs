using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class LeadingEdgeRadiusServiceTests
{
    [Fact]
    public void ScaleLeadingEdgeRadius_LargerScale_IncreasesEstimatedRadiusAndPreservesPointCount()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new LeadingEdgeRadiusService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.ScaleLeadingEdgeRadius(geometry, 4d, 0.2d);

        Assert.Equal(geometry.Points.Count, result.Geometry.Points.Count);
        Assert.True(double.IsFinite(result.OriginalRadius));
        Assert.True(double.IsFinite(result.FinalRadius));
        Assert.True(result.FinalRadius > result.OriginalRadius);
        Assert.Equal(4d, result.RadiusScaleFactor, 6);
        Assert.Equal(0.2d, result.BlendDistanceChordFraction, 6);
    }

    [Fact]
    public void ScaleLeadingEdgeRadius_SmallerScale_DecreasesEstimatedRadius()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new LeadingEdgeRadiusService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.ScaleLeadingEdgeRadius(geometry, 0.25d, 0.2d);

        Assert.True(result.FinalRadius < result.OriginalRadius);
    }

    [Fact]
    public void ScaleLeadingEdgeRadius_SmallBlend_LeavesTrailingEdgeNearlyUnchanged()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new LeadingEdgeRadiusService();
        var geometry = generator.Generate4Digit("2412", 161);

        var result = service.ScaleLeadingEdgeRadius(geometry, 2d, 0.05d);

        Assert.Equal(geometry.Points[0].X, result.Geometry.Points[0].X, 4);
        Assert.Equal(geometry.Points[0].Y, result.Geometry.Points[0].Y, 4);
        Assert.Equal(geometry.Points[^1].X, result.Geometry.Points[^1].X, 4);
        Assert.Equal(geometry.Points[^1].Y, result.Geometry.Points[^1].Y, 4);
    }
}

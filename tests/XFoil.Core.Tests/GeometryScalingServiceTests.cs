using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class GeometryScalingServiceTests
{
    [Fact]
    public void Scale_AboutLeadingEdge_PreservesLeadingEdgeAndScalesTrailingEdge()
    {
        var service = new GeometryScalingService();
        var geometry = CreateGeometry();

        var result = service.Scale(geometry, 1.5d, GeometryScaleOrigin.LeadingEdge);

        Assert.Equal(0d, result.OriginPoint.X, 6);
        Assert.Equal(0d, result.OriginPoint.Y, 6);
        Assert.Equal(geometry.Points[2], result.Geometry.Points[2]);
        Assert.Equal(1.5d, result.Geometry.Points[0].X, 6);
        Assert.Equal(1.5d, result.Geometry.Points[^1].X, 6);
    }

    [Fact]
    public void Scale_AboutTrailingEdge_PreservesTrailingEdgeMidpoint()
    {
        var service = new GeometryScalingService();
        var geometry = CreateGeometry();

        var result = service.Scale(geometry, 0.5d, GeometryScaleOrigin.TrailingEdge);

        Assert.Equal(1d, result.OriginPoint.X, 6);
        Assert.Equal(0d, result.OriginPoint.Y, 6);
        Assert.Equal(1d, result.Geometry.Points[0].X, 6);
        Assert.Equal(1d, result.Geometry.Points[^1].X, 6);
        Assert.Equal(0.5d, result.Geometry.Points[2].X, 6);
    }

    [Fact]
    public void Scale_AboutPoint_UsesSuppliedOrigin()
    {
        var service = new GeometryScalingService();
        var geometry = CreateGeometry();
        var origin = new AirfoilPoint(0.25d, 0d);

        var result = service.Scale(geometry, 2d, GeometryScaleOrigin.Point, origin);

        Assert.Equal(origin, result.OriginPoint);
        Assert.Equal(1.75d, result.Geometry.Points[0].X, 6);
        Assert.Equal(-0.25d, result.Geometry.Points[2].X, 6);
    }

    private static AirfoilGeometry CreateGeometry()
    {
        return new AirfoilGeometry(
            "Scale Test",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.5d, 0.1d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.5d, -0.1d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }
}

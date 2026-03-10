using XFoil.Core.Models;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class FlapDeflectionServiceTests
{
    [Fact]
    public void DeflectTrailingEdge_PreservesLeadingEdgeRegionAndMovesTrailingEdgeDownward()
    {
        var service = new FlapDeflectionService();
        var geometry = CreateGeometry();
        var hingePoint = new AirfoilPoint(0.75d, 0d);

        var result = service.DeflectTrailingEdge(geometry, hingePoint, 20d);

        Assert.Contains(result.Geometry.Points, point => ApproximatelyEqual(point, geometry.Points[3]));
        Assert.True(result.Geometry.Points[0].Y < geometry.Points[0].Y);
        Assert.True(result.Geometry.Points[^1].Y < geometry.Points[^1].Y);
        Assert.True(result.AffectedPointCount >= 4);
    }

    [Fact]
    public void DeflectTrailingEdge_PreservesTrailingEdgeDistanceFromHinge()
    {
        var service = new FlapDeflectionService();
        var geometry = CreateGeometry();
        var hingePoint = new AirfoilPoint(0.75d, 0d);

        var result = service.DeflectTrailingEdge(geometry, hingePoint, 15d);
        var rotatedUpperTrailingEdge = RotateAroundHinge(geometry.Points[0], hingePoint, 15d);
        var rotatedLowerTrailingEdge = RotateAroundHinge(geometry.Points[^1], hingePoint, 15d);

        Assert.Contains(result.Geometry.Points, point => Distance(point, rotatedUpperTrailingEdge) < 1e-9d);
        Assert.Contains(result.Geometry.Points, point => Distance(point, rotatedLowerTrailingEdge) < 1e-9d);
    }

    [Fact]
    public void DeflectTrailingEdge_ForInsideHinge_PerformsLocalPointSurgeryNearBreak()
    {
        var service = new FlapDeflectionService();
        var geometry = CreateGeometry();
        var hingePoint = new AirfoilPoint(0.75d, 0d);

        var result = service.DeflectTrailingEdge(geometry, hingePoint, 12d);

        Assert.True(result.Geometry.Points.Count != geometry.Points.Count);
        Assert.True(result.InsertedPointCount > 0 || result.RemovedPointCount > 0);
        Assert.Contains(result.Geometry.Points, point => Math.Abs(point.X - hingePoint.X) < 0.08d && Math.Abs(point.Y - hingePoint.Y) < 0.12d);
    }

    [Fact]
    public void DeflectTrailingEdge_ForOutsideHinge_StillProducesFiniteCleanedGeometry()
    {
        var service = new FlapDeflectionService();
        var geometry = CreateGeometry();
        var hingePoint = new AirfoilPoint(0.75d, 0.15d);

        var result = service.DeflectTrailingEdge(geometry, hingePoint, 12d);

        Assert.All(result.Geometry.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.True(result.AffectedPointCount > 0);
        Assert.True(result.Geometry.Points.Count >= 5);
        Assert.Contains(result.Geometry.Points, point => Math.Abs(point.X - hingePoint.X) < 0.12d && Math.Abs(point.Y - hingePoint.Y) < 0.20d);
    }

    private static AirfoilGeometry CreateGeometry()
    {
        return new AirfoilGeometry(
            "TestFoil",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.9d, 0.05d),
                new AirfoilPoint(0.5d, 0.08d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.5d, -0.08d),
                new AirfoilPoint(0.9d, -0.05d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }

    private static double Distance(AirfoilPoint point, AirfoilPoint hingePoint)
    {
        var deltaX = point.X - hingePoint.X;
        var deltaY = point.Y - hingePoint.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static AirfoilPoint RotateAroundHinge(AirfoilPoint point, AirfoilPoint hingePoint, double deflectionDegrees)
    {
        var rotationRadians = deflectionDegrees * Math.PI / 180d;
        var cosine = Math.Cos(rotationRadians);
        var sine = Math.Sin(rotationRadians);
        var xRelative = point.X - hingePoint.X;
        var yRelative = point.Y - hingePoint.Y;
        return new AirfoilPoint(
            hingePoint.X + (xRelative * cosine) + (yRelative * sine),
            hingePoint.Y - (xRelative * sine) + (yRelative * cosine));
    }

    private static bool ApproximatelyEqual(AirfoilPoint first, AirfoilPoint second)
    {
        return Math.Abs(first.X - second.X) < 1e-12d
            && Math.Abs(first.Y - second.Y) < 1e-12d;
    }
}

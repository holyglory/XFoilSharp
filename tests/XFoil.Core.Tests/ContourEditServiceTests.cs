using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class ContourEditServiceTests
{
    [Fact]
    public void AddPoint_InsertsExactPointAtRequestedIndex()
    {
        var service = new ContourEditService();
        var geometry = CreateEditableGeometry();
        var newPoint = new AirfoilPoint(0.9d, 0.02d);

        var result = service.AddPoint(geometry, 1, newPoint);

        Assert.Equal(geometry.Points.Count + 1, result.Geometry.Points.Count);
        Assert.Equal(newPoint, result.Geometry.Points[1]);
        Assert.Equal(1, result.InsertedPointCount);
        Assert.Equal("ADDP", result.Operation);
    }

    [Fact]
    public void MovePoint_ReplacesOnlySelectedPoint()
    {
        var service = new ContourEditService();
        var geometry = CreateEditableGeometry();
        var movedPoint = new AirfoilPoint(0.5d, 0.12d);

        var result = service.MovePoint(geometry, 2, movedPoint);

        Assert.Equal(geometry.Points.Count, result.Geometry.Points.Count);
        Assert.Equal(movedPoint, result.Geometry.Points[2]);
        Assert.Equal(geometry.Points[1], result.Geometry.Points[1]);
        Assert.Equal(geometry.Points[3], result.Geometry.Points[3]);
        Assert.Equal("MOVP", result.Operation);
    }

    [Fact]
    public void DeletePoint_RemovesRequestedPoint()
    {
        var service = new ContourEditService();
        var geometry = CreateEditableGeometry();

        var result = service.DeletePoint(geometry, 1);

        Assert.Equal(geometry.Points.Count - 1, result.Geometry.Points.Count);
        Assert.DoesNotContain(result.Geometry.Points, point => point == geometry.Points[1]);
        Assert.Equal(1, result.RemovedPointCount);
        Assert.Equal("DELP", result.Operation);
    }

    [Fact]
    public void DoublePoint_DuplicatesInteriorPointAndRejectsTrailingEdge()
    {
        var service = new ContourEditService();
        var geometry = CreateEditableGeometry();

        var result = service.DoublePoint(geometry, 3);

        Assert.Equal(geometry.Points.Count + 1, result.Geometry.Points.Count);
        Assert.Equal(result.Geometry.Points[3], result.Geometry.Points[4]);
        Assert.Equal(1, result.InsertedPointCount);
        Assert.Throws<InvalidOperationException>(() => service.DoublePoint(geometry, 0));
    }

    [Fact]
    public void RefineCorners_AddsPointsAroundSharpCorners()
    {
        var service = new ContourEditService();
        var geometry = CreateCornerGeometry();

        var result = service.RefineCorners(geometry, 20d);

        Assert.True(result.Geometry.Points.Count > geometry.Points.Count);
        Assert.True(result.InsertedPointCount >= 2);
        Assert.True(result.RefinedCornerCount >= 1);
        Assert.Contains(
            FindInsertedPoints(geometry, result.Geometry),
            point => point.X is > 0.2d and < 0.6d && Math.Abs(point.Y) > 0.05d);
    }

    [Fact]
    public void RefineCorners_RespectsXRangeAndParameterMode()
    {
        var service = new ContourEditService();
        var geometry = CreateAsymmetricCornerGeometry();

        var arcLengthResult = service.RefineCorners(
            geometry,
            15d,
            CornerRefinementParameterMode.ArcLength,
            0.15d,
            0.7d);
        var uniformResult = service.RefineCorners(
            geometry,
            15d,
            CornerRefinementParameterMode.Uniform,
            0.15d,
            0.7d);

        Assert.True(arcLengthResult.InsertedPointCount > 0);
        Assert.Equal(arcLengthResult.InsertedPointCount, uniformResult.InsertedPointCount);

        var arcLengthInserted = FindInsertedPoints(geometry, arcLengthResult.Geometry);
        var uniformInserted = FindInsertedPoints(geometry, uniformResult.Geometry);

        Assert.All(arcLengthInserted, point => Assert.InRange(point.X, 0.15d, 0.7d));
        Assert.All(uniformInserted, point => Assert.InRange(point.X, 0.15d, 0.7d));
        Assert.NotEmpty(arcLengthInserted);
        Assert.NotEmpty(uniformInserted);
        Assert.Equal(arcLengthResult.RefinedCornerCount, uniformResult.RefinedCornerCount);
    }

    private static IReadOnlyList<AirfoilPoint> FindInsertedPoints(AirfoilGeometry original, AirfoilGeometry edited)
    {
        return edited.Points
            .Where(editedPoint => !original.Points.Any(originalPoint => ApproximatelyEqual(originalPoint, editedPoint)))
            .ToArray();
    }

    private static bool ApproximatelyEqual(AirfoilPoint first, AirfoilPoint second)
    {
        return Math.Abs(first.X - second.X) < 1e-10d
            && Math.Abs(first.Y - second.Y) < 1e-10d;
    }

    private static AirfoilGeometry CreateEditableGeometry()
    {
        return new AirfoilGeometry(
            "Editable",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.75d, 0.04d),
                new AirfoilPoint(0.45d, 0.03d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.45d, -0.03d),
                new AirfoilPoint(0.75d, -0.04d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }

    private static AirfoilGeometry CreateCornerGeometry()
    {
        return new AirfoilGeometry(
            "Cornered",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.82d, 0.02d),
                new AirfoilPoint(0.58d, 0.01d),
                new AirfoilPoint(0.28d, 0.23d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.58d, -0.22d),
                new AirfoilPoint(0.82d, -0.03d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }

    private static AirfoilGeometry CreateAsymmetricCornerGeometry()
    {
        return new AirfoilGeometry(
            "AsymmetricCornered",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.88d, 0.02d),
                new AirfoilPoint(0.64d, 0.015d),
                new AirfoilPoint(0.24d, 0.24d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.54d, -0.24d),
                new AirfoilPoint(0.84d, -0.04d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }
}

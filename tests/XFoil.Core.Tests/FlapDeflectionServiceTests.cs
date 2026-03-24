using XFoil.Core.Models;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: FLAP
// Secondary legacy source: f_xfoil/src/geom.f cleanup and intersection helpers
// Role in port: Verifies the managed flap-deflection service that preserves the legacy hinge-rotation and cleanup behavior.
// Differences: The managed service returns explicit edit accounting and cleaned geometry instead of mutating the active contour through GDES.
// Decision: Keep the managed service behavior and verify it against the same geometric invariants as the legacy flap edit.
namespace XFoil.Core.Tests;

public sealed class FlapDeflectionServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: FLAP.
    // Difference from legacy: The test asserts preserved upstream geometry and downstream rotation through immutable managed output instead of inspecting the live editor state.
    // Decision: Keep the managed invariant because it is the clearest regression for the same flap-deflection behavior.
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
    // Legacy mapping: f_xfoil/src/xgdes.f :: FLAP rigid hinge rotation.
    // Difference from legacy: The test computes the rotated TE points analytically rather than relying on visual confirmation in the legacy editor.
    // Decision: Keep the managed analytical check because it verifies the same rigid-rotation contract more directly.
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
    // Legacy mapping: f_xfoil/src/xgdes.f flap-break cleanup path.
    // Difference from legacy: Local point surgery is validated through explicit inserted/removed point counts in the managed result.
    // Decision: Keep the managed accounting because it makes the cleanup behavior testable and transparent.
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
    // Legacy mapping: f_xfoil/src/xgdes.f :: FLAP with off-surface hinge handling.
    // Difference from legacy: The managed test asserts finite cleaned output explicitly instead of relying on the legacy editor not crashing.
    // Decision: Keep the managed finite-output regression because it is the strongest guard for this edge case.
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

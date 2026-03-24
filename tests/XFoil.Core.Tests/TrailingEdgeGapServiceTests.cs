using XFoil.Core.Models;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f trailing-edge gap editing workflow
// Secondary legacy source: f_xfoil/src/geom.f endpoint tangent helpers
// Role in port: Verifies the managed trailing-edge gap editor derived from legacy geometry-edit operations.
// Differences: The managed service returns explicit original/final gap metadata and immutable geometry instead of mutating the active contour in GDES.
// Decision: Keep the managed result object because it preserves the same edit behavior with a clearer contract.
namespace XFoil.Core.Tests;

public sealed class TrailingEdgeGapServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f trailing-edge gap set operation with blending.
    // Difference from legacy: The test asserts gap metadata and preserved interior points explicitly instead of relying on interactive geometry inspection.
    // Decision: Keep the managed numerical regression because it directly validates the preserved TE-gap edit behavior.
    public void SetTrailingEdgeGap_WithFiniteBlend_ReachesRequestedGapAndPreservesPointCount()
    {
        var service = new TrailingEdgeGapService();
        var geometry = CreateOpenTrailingEdgeGeometry();

        var result = service.SetTrailingEdgeGap(geometry, 0.10d, 0.75d);

        Assert.Equal(geometry.Points.Count, result.Geometry.Points.Count);
        Assert.Equal(0.04d, result.OriginalGap, 6);
        Assert.Equal(0.10d, result.FinalGap, 6);
        Assert.Equal(0.10d, result.TargetGap, 6);
        Assert.Equal(0.75d, result.BlendDistanceChordFraction, 6);
        Assert.Equal(geometry.Points[2], result.Geometry.Points[2]);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f zero-blend endpoint adjustment path.
    // Difference from legacy: Endpoint-only movement is asserted explicitly through the managed result rather than inferred from the edited contour.
    // Decision: Keep the managed invariant because it is the clearest regression for this edge case.
    public void SetTrailingEdgeGap_WithZeroBlend_MovesOnlyEndpoints()
    {
        var service = new TrailingEdgeGapService();
        var geometry = CreateOpenTrailingEdgeGeometry();

        var result = service.SetTrailingEdgeGap(geometry, 0.08d, 0d);

        Assert.Equal(0.08d, result.FinalGap, 6);
        Assert.NotEqual(geometry.Points[0], result.Geometry.Points[0]);
        Assert.NotEqual(geometry.Points[^1], result.Geometry.Points[^1]);
        Assert.Equal(geometry.Points[1], result.Geometry.Points[1]);
        Assert.Equal(geometry.Points[2], result.Geometry.Points[2]);
        Assert.Equal(geometry.Points[3], result.Geometry.Points[3]);
    }

    [Fact]
    // Legacy mapping: legacy closed trailing-edge gap opening based on endpoint tangents.
    // Difference from legacy: The inferred gap direction is validated numerically instead of through visual inspection of the edited airfoil.
    // Decision: Keep the managed numerical check because it makes the legacy behavior reproducible in tests.
    public void SetTrailingEdgeGap_ForClosedTrailingEdge_InfersGapDirectionFromEndpointTangents()
    {
        var service = new TrailingEdgeGapService();
        var geometry = CreateClosedTrailingEdgeGeometry();

        var result = service.SetTrailingEdgeGap(geometry, 0.04d, 1d);

        Assert.Equal(0d, result.OriginalGap, 6);
        Assert.Equal(0.04d, result.FinalGap, 6);
        Assert.True(result.Geometry.Points[0].Y > geometry.Points[0].Y);
        Assert.True(result.Geometry.Points[^1].Y < geometry.Points[^1].Y);
        Assert.Equal(geometry.Points[2], result.Geometry.Points[2]);
    }

    private static AirfoilGeometry CreateOpenTrailingEdgeGeometry()
    {
        return new AirfoilGeometry(
            "Open TE Test",
            new[]
            {
                new AirfoilPoint(1.0d, 0.02d),
                new AirfoilPoint(0.6d, 0.08d),
                new AirfoilPoint(0.0d, 0.0d),
                new AirfoilPoint(0.6d, -0.08d),
                new AirfoilPoint(1.0d, -0.02d),
            },
            AirfoilFormat.PlainCoordinates);
    }

    private static AirfoilGeometry CreateClosedTrailingEdgeGeometry()
    {
        return new AirfoilGeometry(
            "Closed TE Test",
            new[]
            {
                new AirfoilPoint(1.0d, 0.0d),
                new AirfoilPoint(0.7d, 0.06d),
                new AirfoilPoint(0.0d, 0.0d),
                new AirfoilPoint(0.7d, -0.06d),
                new AirfoilPoint(1.0d, 0.0d),
            },
            AirfoilFormat.PlainCoordinates);
    }
}

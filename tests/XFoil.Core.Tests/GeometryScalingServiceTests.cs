using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: SCAL
// Secondary legacy source: f_xfoil/src/geom.f
// Role in port: Verifies the managed geometry-scaling service derived from the legacy scale command.
// Differences: The managed API makes origin selection explicit and returns structured metadata instead of mutating the active geometry in the editor.
// Decision: Keep the managed service shape because it cleanly expresses the same scaling behavior.
namespace XFoil.Core.Tests;

public sealed class GeometryScalingServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: SCAL about leading edge.
    // Difference from legacy: The origin point is exposed explicitly in the managed result rather than inferred from editor state.
    // Decision: Keep the managed result contract because it makes the preserved scaling reference unambiguous.
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
    // Legacy mapping: f_xfoil/src/xgdes.f :: SCAL about trailing edge.
    // Difference from legacy: The managed test asserts midpoint preservation directly instead of checking the transformed contour interactively.
    // Decision: Keep the managed invariant because it is the clearest regression for this scaling mode.
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
    // Legacy mapping: none.
    // Difference from legacy: Supplying an arbitrary explicit origin is a managed API refinement over the legacy scale command.
    // Decision: Keep this managed improvement because it extends the scaling utility without replacing the preserved legacy modes.
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

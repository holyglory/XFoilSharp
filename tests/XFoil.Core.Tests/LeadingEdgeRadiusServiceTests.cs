using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f leading-edge shaping workflow
// Secondary legacy source: f_xfoil/src/geom.f
// Role in port: Verifies the managed leading-edge radius editor derived from legacy geometry-design operations.
// Differences: The legacy editor exposed LE shaping through command sequences, while the port offers a dedicated service with explicit radius metadata.
// Decision: Keep the managed service abstraction because it preserves the geometric intent while providing a clearer API.
namespace XFoil.Core.Tests;

public sealed class LeadingEdgeRadiusServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f leading-edge radius enlargement workflow.
    // Difference from legacy: The managed test checks structured radius metadata instead of relying on inspection of the edited contour alone.
    // Decision: Keep the managed metadata contract because it makes the legacy-derived edit verifiable.
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
    // Legacy mapping: f_xfoil/src/xgdes.f leading-edge radius reduction workflow.
    // Difference from legacy: The test reduces the legacy edit to a direct monotonic radius invariant through the managed result.
    // Decision: Keep the managed invariant because it is the most stable regression for this edit path.
    public void ScaleLeadingEdgeRadius_SmallerScale_DecreasesEstimatedRadius()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new LeadingEdgeRadiusService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.ScaleLeadingEdgeRadius(geometry, 0.25d, 0.2d);

        Assert.True(result.FinalRadius < result.OriginalRadius);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f localized leading-edge blend behavior.
    // Difference from legacy: The managed test asserts trailing-edge preservation numerically instead of visually inspecting the edited shape.
    // Decision: Keep the managed numerical check because it protects the intended locality of the edit.
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

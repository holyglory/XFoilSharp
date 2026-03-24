using XFoil.Core.Models;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f contour spline modification workflow
// Secondary legacy source: f_xfoil/src/spline.f
// Role in port: Verifies the managed contour-modification service derived from legacy local contour editing operations.
// Differences: The managed port exposes control-point driven edits as a pure function instead of mutating the active airfoil inside the legacy editor.
// Decision: Keep the managed service interface because it preserves the geometric behavior while simplifying composition and testing.
namespace XFoil.Core.Tests;

public sealed class ContourModificationServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f local contour modification path.
    // Difference from legacy: The test checks immutable output geometry and interval bookkeeping rather than legacy in-place edit state.
    // Decision: Keep the managed result-based test because it is the intended surface of the refactored editor.
    public void ModifyContour_ChangesOnlySelectedInterval()
    {
        var service = new ContourModificationService();
        var geometry = CreateGeometry();
        var controlPoints = new[]
        {
            geometry.Points[1],
            new AirfoilPoint(0.55d, 0.14d),
            geometry.Points[4],
        };

        var result = service.ModifyContour(geometry, controlPoints);

        Assert.Equal(1, result.ModifiedStartIndex);
        Assert.Equal(4, result.ModifiedEndIndex);
        Assert.Equal(geometry.Points[0], result.Geometry.Points[0]);
        Assert.Equal(geometry.Points[5], result.Geometry.Points[5]);
        Assert.Equal(geometry.Points[6], result.Geometry.Points[6]);
        Assert.NotEqual(geometry.Points[2], result.Geometry.Points[2]);
        Assert.NotEqual(geometry.Points[3], result.Geometry.Points[3]);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f contour modification interpolation path.
    // Difference from legacy: Reversed control-point equivalence is asserted explicitly in the managed harness instead of being implicit in the legacy interaction.
    // Decision: Keep the managed equivalence test because it documents a stability property expected from the refactored API.
    public void ModifyContour_ReversedControlPointsProduceSameResult()
    {
        var service = new ContourModificationService();
        var geometry = CreateGeometry();
        var forward = new[]
        {
            geometry.Points[1],
            new AirfoilPoint(0.55d, 0.14d),
            geometry.Points[4],
        };
        var reverse = forward.Reverse().ToArray();

        var forwardResult = service.ModifyContour(geometry, forward);
        var reverseResult = service.ModifyContour(geometry, reverse);

        Assert.Equal(forwardResult.ModifiedStartIndex, reverseResult.ModifiedStartIndex);
        Assert.Equal(forwardResult.ModifiedEndIndex, reverseResult.ModifiedEndIndex);
        for (var index = 0; index < geometry.Points.Count; index++)
        {
            Assert.True(AreClose(forwardResult.Geometry.Points[index], reverseResult.Geometry.Points[index]));
        }
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f endpoint slope matching option.
    // Difference from legacy: The slope-match switch is a direct managed parameter instead of a command-state toggle.
    // Decision: Keep the managed parameterized test because it makes the legacy option easier to reason about and preserve.
    public void ModifyContour_DisablingSlopeMatchAllowsLeadingEndpointOverride()
    {
        var service = new ContourModificationService();
        var geometry = CreateGeometry();
        var controlPoints = new[]
        {
            new AirfoilPoint(1.02d, 0.03d),
            new AirfoilPoint(0.82d, 0.09d),
            geometry.Points[3],
        };

        var matched = service.ModifyContour(geometry, controlPoints, true);
        var unmatched = service.ModifyContour(geometry, controlPoints, false);

        Assert.True(AreClose(geometry.Points[0], matched.Geometry.Points[0]));
        Assert.False(AreClose(geometry.Points[0], unmatched.Geometry.Points[0]));
        Assert.True(unmatched.Geometry.Points[0].Y > geometry.Points[0].Y);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f contour-edit precondition checks.
    // Difference from legacy: Invalid control geometry is surfaced as a managed exception instead of an interactive command failure.
    // Decision: Keep the managed exception contract because it is clearer for callers and preserves the same validation rule.
    public void ModifyContour_RequiresDistinctEndpoints()
    {
        var service = new ContourModificationService();
        var geometry = CreateGeometry();
        var controlPoints = new[]
        {
            new AirfoilPoint(0.75d, 0.05d),
            new AirfoilPoint(0.74d, 0.06d),
        };

        Assert.Throws<InvalidOperationException>(() => service.ModifyContour(geometry, controlPoints));
    }

    private static bool AreClose(AirfoilPoint first, AirfoilPoint second)
    {
        return Math.Abs(first.X - second.X) < 1e-9d
            && Math.Abs(first.Y - second.Y) < 1e-9d;
    }

    private static AirfoilGeometry CreateGeometry()
    {
        return new AirfoilGeometry(
            "Modifiable",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.8d, 0.06d),
                new AirfoilPoint(0.45d, 0.05d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(0.45d, -0.05d),
                new AirfoilPoint(0.8d, -0.06d),
                new AirfoilPoint(1d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }
}

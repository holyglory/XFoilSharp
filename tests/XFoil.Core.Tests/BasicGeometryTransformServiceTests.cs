using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: ROTATE, NORM, DERO, TSET
// Secondary legacy source: f_xfoil/src/geom.f
// Role in port: Verifies the managed geometry-transform helpers that preserve the intent of the legacy geometry-edit commands.
// Differences: The tests call explicit .NET service methods instead of driving the legacy GDES command loop, while still checking the same geometric invariants.
// Decision: Keep the managed API and test surface because the port intentionally refactors interactive geometry edits into callable services.
namespace XFoil.Core.Tests;

public sealed class BasicGeometryTransformServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: ROTATE.
    // Difference from legacy: The test checks a direct managed rotation helper instead of issuing the legacy geometry-edit command.
    // Decision: Keep the managed test because it validates the preserved rotation convention at the service boundary.
    public void RotateDegrees_RotatesClockwiseLikeLegacyRotate()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.RotateDegrees(geometry, 90d);

        Assert.Equal(0d, result.Points[0].X, 6);
        Assert.Equal(-1d, result.Points[0].Y, 6);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f geometry translation/edit workflow.
    // Difference from legacy: Translation is exposed as a dedicated managed method rather than an interactive edit sequence.
    // Decision: Keep the managed-only surface because it is clearer and still preserves the intended geometry shift behavior.
    public void Translate_ShiftsAllPoints()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.Translate(geometry, 0.5d, -0.25d);

        Assert.Equal(1.5d, result.Points[0].X, 6);
        Assert.Equal(-0.25d, result.Points[0].Y, 6);
        Assert.Equal(0.5d, result.Points[2].X, 6);
        Assert.Equal(-0.25d, result.Points[2].Y, 6);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: SCAL / orientation-preservation logic.
    // Difference from legacy: The port makes the orientation-preserving reflection behavior explicit in the service result.
    // Decision: Keep the managed formulation because it is easier to verify while honoring the same contour-orientation requirement.
    public void ScaleAboutOrigin_WithNegativeDeterminant_ReversesOrderingToPreserveContourOrientation()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.ScaleAboutOrigin(geometry, -1d, 1d);

        Assert.Equal(-1d, result.Points[0].X, 6);
        Assert.Equal(0d, result.Points[0].Y, 6);
        Assert.Equal(-1d, result.Points[^1].X, 6);
        Assert.Equal(0d, result.Points[^1].Y, 6);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f spanwise/camber scaling edits.
    // Difference from legacy: The managed API offers a dedicated linear-Y scaling helper instead of a sequence of interactive edits.
    // Decision: Keep the managed helper because it captures the same geometric effect with a more explicit contract.
    public void ScaleYLinearly_AppliesRequestedEndpointScalingTrend()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.ScaleYLinearly(geometry, 0d, 2d, 1d, 0.5d);

        Assert.True(Math.Abs(result.Points[1].Y) > Math.Abs(geometry.Points[1].Y));
        Assert.Equal(geometry.Points[0].Y, result.Points[0].Y, 6);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: DERO.
    // Difference from legacy: The managed test asserts the leveled-chord invariant directly instead of checking interactive command output.
    // Decision: Keep the managed invariant because it is the clearest regression for the same derotation behavior.
    public void Derotate_LevelsChordLine()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();
        var rotated = service.RotateDegrees(geometry, 15d);
        var metricsCalculator = new AirfoilMetricsCalculator();

        var derotated = service.Derotate(rotated);
        var metrics = metricsCalculator.Calculate(derotated);

        Assert.Equal(metrics.LeadingEdge.Y, metrics.TrailingEdgeMidpoint.Y, 4);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: NORM.
    // Difference from legacy: The port exposes normalization as a direct service call rather than a geometry-edit command.
    // Decision: Keep the managed test because unit-chord normalization is a stable public behavior in the port.
    public void NormalizeUnitChord_ProducesUnitChord()
    {
        var service = new BasicGeometryTransformService();
        var metricsCalculator = new AirfoilMetricsCalculator();

        var result = service.NormalizeUnitChord(CreateScaledGeometry());
        var metrics = metricsCalculator.Calculate(result);

        Assert.InRange(metrics.Chord, 0.999999d, 1.000001d);
    }

    private static AirfoilGeometry CreateGeometry()
    {
        return new AirfoilGeometry(
            "Transform Test",
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

    private static AirfoilGeometry CreateScaledGeometry()
    {
        return new AirfoilGeometry(
            "Scaled Test",
            new[]
            {
                new AirfoilPoint(2d, 0d),
                new AirfoilPoint(1d, 0.2d),
                new AirfoilPoint(0d, 0d),
                new AirfoilPoint(1d, -0.2d),
                new AirfoilPoint(2d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }
}

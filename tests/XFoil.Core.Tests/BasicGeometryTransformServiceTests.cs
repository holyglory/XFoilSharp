using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class BasicGeometryTransformServiceTests
{
    [Fact]
    public void RotateDegrees_RotatesClockwiseLikeLegacyRotate()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.RotateDegrees(geometry, 90d);

        Assert.Equal(0d, result.Points[0].X, 6);
        Assert.Equal(-1d, result.Points[0].Y, 6);
    }

    [Fact]
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
    public void ScaleYLinearly_AppliesRequestedEndpointScalingTrend()
    {
        var service = new BasicGeometryTransformService();
        var geometry = CreateGeometry();

        var result = service.ScaleYLinearly(geometry, 0d, 2d, 1d, 0.5d);

        Assert.True(Math.Abs(result.Points[1].Y) > Math.Abs(geometry.Points[1].Y));
        Assert.Equal(geometry.Points[0].Y, result.Points[0].Y, 6);
    }

    [Fact]
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

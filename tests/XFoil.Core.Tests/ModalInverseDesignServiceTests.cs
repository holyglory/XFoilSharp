using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;

namespace XFoil.Core.Tests;

public sealed class ModalInverseDesignServiceTests
{
    [Fact]
    public void CreateSpectrum_ProducesRequestedModeCount()
    {
        var service = new ModalInverseDesignService();
        var profile = CreateProfile();

        var spectrum = service.CreateSpectrum("Spec", profile, 5, 0.1d);

        Assert.Equal("Spec", spectrum.Name);
        Assert.Equal(5, spectrum.Coefficients.Count);
        Assert.All(spectrum.Coefficients, coefficient => Assert.InRange(coefficient.ModeIndex, 1, 5));
    }

    [Fact]
    public void Execute_ModifiesGeometryAndPreservesEndpoints()
    {
        var modalService = new ModalInverseDesignService();
        var qspecService = new QSpecDesignService();
        var baseline = CreateProfile();
        var target = qspecService.Modify(
            baseline,
            new[]
            {
                new AirfoilPoint(baseline.Points[1].PlotCoordinate, baseline.Points[1].SpeedRatio),
                new AirfoilPoint(baseline.Points[2].PlotCoordinate, 1.30d),
                new AirfoilPoint(baseline.Points[4].PlotCoordinate, baseline.Points[4].SpeedRatio),
            },
            true).Profile;
        var geometry = CreateGeometry(baseline);

        var result = modalService.Execute(geometry, baseline, target, 6, 0.1d, 0.025d);

        Assert.Equal(baseline.Points.Count, result.Geometry.Points.Count);
        Assert.Equal(geometry.Points[0], result.Geometry.Points[0]);
        Assert.Equal(geometry.Points[^1], result.Geometry.Points[^1]);
        Assert.True(result.MaxNormalDisplacement > 0d);
        Assert.True(result.MaxNormalDisplacement <= 0.025d + 1e-9d);
        Assert.True(result.RmsNormalDisplacement > 0d);
        Assert.Equal(6, result.Spectrum.Coefficients.Count);
    }

    [Fact]
    public void PerturbMode_ChangesGeometryWithSelectedMode()
    {
        var service = new ModalInverseDesignService();
        var profile = CreateProfile();
        var geometry = CreateGeometry(profile);

        var result = service.PerturbMode(geometry, profile, 3, 0.5d, 5, 0.05d, 0.02d);

        Assert.Equal(5, result.Spectrum.Coefficients.Count);
        Assert.Equal(3, result.Spectrum.Coefficients.Single(coefficient => coefficient.Coefficient != 0d).ModeIndex);
        Assert.True(result.MaxNormalDisplacement > 0d);
    }

    private static AirfoilGeometry CreateGeometry(QSpecProfile profile)
    {
        return new AirfoilGeometry(
            "ProfileGeom",
            profile.Points.Select(point => point.Location).ToArray(),
            AirfoilFormat.PlainCoordinates);
    }

    private static QSpecProfile CreateProfile()
    {
        return new QSpecProfile(
            "Profile",
            3d,
            0d,
            new[]
            {
                new QSpecPoint(0, 0d, 1d, new AirfoilPoint(1d, 0d), 0.8d, 0.36d, 0.36d),
                new QSpecPoint(1, 0.2d, 0.8d, new AirfoilPoint(0.8d, 0.04d), 1.0d, 0.0d, 0.0d),
                new QSpecPoint(2, 0.4d, 0.6d, new AirfoilPoint(0.6d, 0.08d), 1.5d, -1.25d, -1.20d),
                new QSpecPoint(3, 0.6d, 0.4d, new AirfoilPoint(0.4d, 0.02d), 1.1d, -0.21d, -0.20d),
                new QSpecPoint(4, 0.8d, 0.2d, new AirfoilPoint(0.2d, -0.03d), 0.7d, 0.51d, 0.50d),
                new QSpecPoint(5, 1d, 0d, new AirfoilPoint(0d, 0d), 0.5d, 0.75d, 0.72d),
            });
    }
}

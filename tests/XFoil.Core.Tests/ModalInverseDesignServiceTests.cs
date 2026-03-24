using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xqdes.f modal inverse-design workflow
// Secondary legacy source: legacy QSPEC inverse-design lineage
// Role in port: Verifies the managed modal inverse-design service layered on top of the legacy-inspired QSPEC workflow.
// Differences: Modal spectrum construction and perturbation are exposed as explicit managed APIs rather than hidden inside a legacy design session.
// Decision: Keep the managed modal API because it is a structured port/refactor of the inverse-design functionality.
namespace XFoil.Core.Tests;

public sealed class ModalInverseDesignServiceTests
{
    [Fact]
    // Legacy mapping: legacy inverse-design modal basis construction.
    // Difference from legacy: The managed test asserts explicit spectrum metadata rather than inspecting internal design-session state.
    // Decision: Keep the managed spectrum contract because it makes the modal workflow reusable and testable.
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
    // Legacy mapping: legacy inverse-design geometry update path.
    // Difference from legacy: The managed result exposes displacement metrics and preserved endpoints directly instead of leaving them implicit.
    // Decision: Keep the managed result structure because it improves observability without changing the design intent.
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
    // Legacy mapping: legacy modal perturbation/design experimentation workflow.
    // Difference from legacy: The port offers a dedicated perturbation API that has no single direct legacy command analogue, even though it uses the same modal lineage.
    // Decision: Keep this managed improvement because it is an intentional extension of the inverse-design toolkit.
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

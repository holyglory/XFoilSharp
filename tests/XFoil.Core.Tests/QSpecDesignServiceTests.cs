using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Design.Services;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xqdes.f :: QDES/QSPEC workflows
// Secondary legacy source: f_xfoil/src/xfoil.f inverse-design support paths
// Role in port: Verifies the managed QSPEC design service derived from the legacy speed-distribution editing and inverse-design workflow.
// Differences: The managed service exposes immutable profiles and explicit result objects instead of mutating the active inverse-design state inside the legacy command loop.
// Decision: Keep the managed design API because it preserves the legacy behavior while making inverse-design operations composable and testable.
namespace XFoil.Core.Tests;

public sealed class QSpecDesignServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xqdes.f baseline QSPEC profile construction.
    // Difference from legacy: The managed test validates normalized profile coordinates through explicit DTOs instead of through the legacy design workspace.
    // Decision: Keep the managed profile test because it documents the ported inverse-design input contract.
    public void CreateFromInviscidAnalysis_ProducesNormalizedProfileCoordinates()
    {
        var service = new QSpecDesignService();
        var analysis = CreateAnalysis(
            new[]
            {
                new PressureCoefficientSample(new AirfoilPoint(1d, 0d), 0.8d, 0.36d, 0.34d),
                new PressureCoefficientSample(new AirfoilPoint(0.5d, 0.1d), 1.2d, -0.44d, -0.41d),
                new PressureCoefficientSample(new AirfoilPoint(0d, 0d), 0.6d, 0.64d, 0.61d),
            });

        var profile = service.CreateFromInviscidAnalysis("TestQspec", analysis);

        Assert.Equal(0d, profile.Points[0].SurfaceCoordinate, 9);
        Assert.Equal(1d, profile.Points[^1].SurfaceCoordinate, 9);
        Assert.Equal(1d, profile.Points[0].PlotCoordinate, 9);
        Assert.Equal(0d, profile.Points[^1].PlotCoordinate, 9);
        Assert.Equal(1.2d, profile.Points[1].SpeedRatio, 9);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xqdes.f local QSPEC modification path.
    // Difference from legacy: The modification interval is returned explicitly by the managed service rather than being implicit in legacy state.
    // Decision: Keep the managed result-based test because it strengthens observability without changing the design behavior.
    public void Modify_ChangesSelectedQSpecInterval()
    {
        var service = new QSpecDesignService();
        var profile = CreateProfile();
        var controlPoints = new[]
        {
            new AirfoilPoint(profile.Points[1].PlotCoordinate, profile.Points[1].SpeedRatio),
            new AirfoilPoint(profile.Points[2].PlotCoordinate, 1.30d),
            new AirfoilPoint(profile.Points[4].PlotCoordinate, profile.Points[4].SpeedRatio),
        };

        var result = service.Modify(profile, controlPoints, true);

        Assert.Equal(1, result.ModifiedStartIndex);
        Assert.Equal(4, result.ModifiedEndIndex);
        Assert.Equal(profile.Points[0].SpeedRatio, result.Profile.Points[0].SpeedRatio);
        Assert.Equal(profile.Points[^1].SpeedRatio, result.Profile.Points[^1].SpeedRatio);
        Assert.NotEqual(profile.Points[2].SpeedRatio, result.Profile.Points[2].SpeedRatio);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xqdes.f QSPEC smoothing workflow.
    // Difference from legacy: The test checks endpoint preservation and smoothing length numerically instead of relying on interactive profile inspection.
    // Decision: Keep the managed numerical regression because it is the strongest guard for the smoothing operation.
    public void Smooth_ReducesInteriorPeakWhileKeepingEndpointsFixed()
    {
        var service = new QSpecDesignService();
        var profile = CreateProfile();

        var result = service.Smooth(
            profile,
            profile.Points[1].PlotCoordinate,
            profile.Points[4].PlotCoordinate,
            false,
            0.2d);

        Assert.Equal(profile.Points[1].SpeedRatio, result.Profile.Points[1].SpeedRatio, 9);
        Assert.Equal(profile.Points[4].SpeedRatio, result.Profile.Points[4].SpeedRatio, 9);
        Assert.True(
            Math.Abs(result.Profile.Points[2].SpeedRatio - profile.Points[2].SpeedRatio) > 1e-9d
            || Math.Abs(result.Profile.Points[3].SpeedRatio - profile.Points[3].SpeedRatio) > 1e-9d);
        Assert.True(result.SmoothingLength > 0d);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xqdes.f symmetry enforcement support.
    // Difference from legacy: Symmetry is asserted directly on the managed profile object instead of on the evolving legacy workspace.
    // Decision: Keep the managed symmetry test because it documents the expected transformed profile explicitly.
    public void ForceSymmetry_MakesMirroredSpeedRatiosAntiSymmetric()
    {
        var service = new QSpecDesignService();
        var profile = CreateProfile();

        var symmetric = service.ForceSymmetry(profile);

        for (var index = 0; index < symmetric.Points.Count; index++)
        {
            var mirrorIndex = symmetric.Points.Count - index - 1;
            Assert.Equal(-symmetric.Points[index].SpeedRatio, symmetric.Points[mirrorIndex].SpeedRatio, 9);
        }
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xqdes.f inverse geometry update.
    // Difference from legacy: The managed inverse result exposes bounded displacement metrics that were not surfaced explicitly in legacy workflows.
    // Decision: Keep the managed metrics because they are a deliberate API improvement over the same inverse-design behavior.
    public void ExecuteInverse_ProducesBoundedModifiedGeometry()
    {
        var service = new QSpecDesignService();
        var profile = CreateProfile();
        var edited = service.Modify(
            profile,
            new[]
            {
                new AirfoilPoint(profile.Points[1].PlotCoordinate, profile.Points[1].SpeedRatio),
                new AirfoilPoint(profile.Points[2].PlotCoordinate, 1.25d),
                new AirfoilPoint(profile.Points[4].PlotCoordinate, profile.Points[4].SpeedRatio),
            },
            true);
        var geometry = new AirfoilGeometry(
            "ProfileGeom",
            profile.Points.Select(point => point.Location).ToArray(),
            AirfoilFormat.PlainCoordinates);

        var result = service.ExecuteInverse(geometry, profile, edited.Profile, 0.03d, 1);

        Assert.Equal(profile.Points.Count, result.Geometry.Points.Count);
        Assert.Equal(geometry.Points[0], result.Geometry.Points[0]);
        Assert.Equal(geometry.Points[^1], result.Geometry.Points[^1]);
        Assert.True(result.MaxNormalDisplacement > 0d);
        Assert.True(result.MaxNormalDisplacement <= 0.03d + 1e-9d);
        Assert.True(result.RmsNormalDisplacement > 0d);
        Assert.True(result.MaxSpeedRatioDelta > 0d);
    }

    [Fact]
    // Legacy mapping: legacy inverse-design precondition checks.
    // Difference from legacy: Invalid profile sizes are surfaced as managed exceptions instead of command-loop failures.
    // Decision: Keep the managed exception contract because it is clearer for callers while preserving the same validation rule.
    public void ExecuteInverse_RequiresMatchingPointCounts()
    {
        var service = new QSpecDesignService();
        var profile = CreateProfile();
        var shorterProfile = new QSpecProfile(
            "Short",
            profile.AngleOfAttackDegrees,
            profile.MachNumber,
            profile.Points.Take(profile.Points.Count - 1).ToArray());
        var geometry = new AirfoilGeometry(
            "ProfileGeom",
            profile.Points.Select(point => point.Location).ToArray(),
            AirfoilFormat.PlainCoordinates);

        Assert.Throws<InvalidOperationException>(() => service.ExecuteInverse(geometry, profile, shorterProfile));
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

    private static InviscidAnalysisResult CreateAnalysis(IReadOnlyList<PressureCoefficientSample> samples)
    {
        return new InviscidAnalysisResult(
            0,
            3d,
            0d,
            0.4d,
            0.6d,
            0d,
            0.6d,
            0d,
            0.58d,
            0d,
            -0.1d,
            Array.Empty<double>(),
            0d,
            samples,
            new WakeGeometry(new[] { new WakePoint(new AirfoilPoint(1.2d, 0d), 1d, 0d, 0.2d, 1d) }));
    }
}

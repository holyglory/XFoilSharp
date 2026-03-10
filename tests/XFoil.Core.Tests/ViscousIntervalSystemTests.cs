using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousIntervalSystemTests
{
    [Fact]
    public void ViscousIntervalSystem_ProducesExpectedIntervalCounts()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var system = service.AnalyzeViscousIntervalSystem(geometry, 2d, settings);

        Assert.Equal(system.State.UpperSurface.Stations.Count - 1, system.UpperSurfaceIntervals.Count);
        Assert.Equal(system.State.LowerSurface.Stations.Count - 1, system.LowerSurfaceIntervals.Count);
        Assert.Equal(system.State.Wake.Stations.Count - 1, system.WakeIntervals.Count);
    }

    [Fact]
    public void ViscousIntervalSystem_ProducesFiniteWeightsAndResiduals()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(panelCount: 120, machNumber: 0.2d, reynoldsNumber: 500_000d);

        var system = service.AnalyzeViscousIntervalSystem(geometry, 4d, settings);

        Assert.All(system.UpperSurfaceIntervals, AssertInterval);
        Assert.All(system.LowerSurfaceIntervals, AssertInterval);
        Assert.All(system.WakeIntervals, interval =>
        {
            AssertInterval(interval);
            Assert.Equal(ViscousIntervalKind.Wake, interval.Kind);
        });
    }

    private static void AssertInterval(ViscousIntervalState interval)
    {
        Assert.InRange(interval.UpwindWeight, 0.5d, 1d);
        Assert.True(double.IsFinite(interval.LogXiChange));
        Assert.True(double.IsFinite(interval.LogEdgeVelocityChange));
        Assert.True(double.IsFinite(interval.LogThetaChange));
        Assert.True(double.IsFinite(interval.LogKinematicShapeFactorChange));
        Assert.True(double.IsFinite(interval.AmplificationGrowthRate));
        Assert.True(double.IsFinite(interval.MomentumResidual));
        Assert.True(double.IsFinite(interval.ShapeResidual));
        Assert.True(double.IsFinite(interval.SkinFrictionResidual));
        Assert.True(double.IsFinite(interval.AmplificationResidual));
        Assert.True(interval.StartDerived.KinematicShapeFactor > 1d);
        Assert.True(interval.EndDerived.DensityRatio > 0d);
        Assert.True(interval.AmplificationGrowthRate >= 0d);
    }
}

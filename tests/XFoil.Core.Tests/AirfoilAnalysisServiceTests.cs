using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class AirfoilAnalysisServiceTests
{
    [Fact]
    public void SweepInviscidAlpha_ReturnsExpectedPointCount()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var sweep = service.SweepInviscidAlpha(
            geometry,
            -2d,
            4d,
            2d,
            new AnalysisSettings(panelCount: 100));

        Assert.Equal(4, sweep.Points.Count);
        Assert.Equal(-2d, sweep.Points[0].AngleOfAttackDegrees);
        Assert.Equal(4d, sweep.Points[^1].AngleOfAttackDegrees);
    }

    [Fact]
    public void SweepInviscidAlpha_ForSymmetricAirfoil_ShowsIncreasingLiftTrend()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var sweep = service.SweepInviscidAlpha(
            geometry,
            0d,
            6d,
            2d,
            new AnalysisSettings(panelCount: 120));

        Assert.True(sweep.Points[1].LiftCoefficient >= sweep.Points[0].LiftCoefficient);
        Assert.True(sweep.Points[2].LiftCoefficient >= sweep.Points[1].LiftCoefficient);
        Assert.True(sweep.Points[3].LiftCoefficient >= sweep.Points[2].LiftCoefficient);
    }

    [Fact]
    public void SweepInviscidAlpha_MatchesSinglePointAnalyses()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, machNumber: 0.2d);

        var sweep = service.SweepInviscidAlpha(
            geometry,
            -1d,
            3d,
            2d,
            settings);

        for (var index = 0; index < sweep.Points.Count; index++)
        {
            var alpha = -1d + (2d * index);
            var singlePoint = service.AnalyzeInviscid(geometry, alpha, settings);
            Assert.Equal(singlePoint.LiftCoefficient, sweep.Points[index].LiftCoefficient, 10);
            Assert.Equal(singlePoint.Circulation, sweep.Points[index].Circulation, 10);
        }
    }

    [Fact]
    public void AnalyzeInviscidForLiftCoefficient_ForSymmetricAirfoil_FindsPositiveAlphaForPositiveLift()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscidForLiftCoefficient(
            geometry,
            0.55d,
            new AnalysisSettings(panelCount: 120));

        Assert.InRange(result.LiftCoefficient, 0.50d, 0.60d);
        Assert.True(result.AngleOfAttackDegrees > 0d);
    }

    [Fact]
    public void AnalyzeInviscidForLiftCoefficient_ForSymmetricAirfoil_FindsNearZeroAlphaForZeroLift()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscidForLiftCoefficient(
            geometry,
            0d,
            new AnalysisSettings(panelCount: 120));

        Assert.InRange(result.LiftCoefficient, -0.05d, 0.05d);
        Assert.InRange(result.AngleOfAttackDegrees, -1d, 1d);
    }

    [Fact]
    public void SweepInviscidLiftCoefficient_ForSymmetricAirfoil_ShowsIncreasingSolvedAlphaTrend()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var sweep = service.SweepInviscidLiftCoefficient(
            geometry,
            0.1d,
            0.7d,
            0.2d,
            new AnalysisSettings(panelCount: 120));

        Assert.Equal(4, sweep.Points.Count);
        Assert.Equal(0.1d, sweep.Points[0].TargetLiftCoefficient);
        Assert.Equal(0.7d, sweep.Points[^1].TargetLiftCoefficient);
        Assert.True(sweep.Points[1].OperatingPoint.AngleOfAttackDegrees >= sweep.Points[0].OperatingPoint.AngleOfAttackDegrees);
        Assert.True(sweep.Points[2].OperatingPoint.AngleOfAttackDegrees >= sweep.Points[1].OperatingPoint.AngleOfAttackDegrees);
        Assert.True(sweep.Points[3].OperatingPoint.AngleOfAttackDegrees >= sweep.Points[2].OperatingPoint.AngleOfAttackDegrees);
    }

    [Fact]
    public void AnalyzeInviscid_WithLinearVortexSolverType_ProducesValidResult()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            inviscidSolverType: InviscidSolverType.LinearVortex);

        var result = service.AnalyzeInviscid(geometry, 3.0, settings);

        Assert.NotNull(result);
        Assert.Equal(3.0, result.AngleOfAttackDegrees);
        Assert.True(result.LiftCoefficient > 0, "CL should be positive for cambered airfoil at positive alpha.");
        Assert.True(double.IsFinite(result.MomentCoefficientQuarterChord), "CM should be finite.");
    }

    [Fact]
    public void AnalyzeInviscid_LinearVortexVsHessSmith_ProduceDifferentResults()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var hessSmithSettings = new AnalysisSettings(
            panelCount: 120,
            inviscidSolverType: InviscidSolverType.HessSmith);
        var linearVortexSettings = new AnalysisSettings(
            panelCount: 120,
            inviscidSolverType: InviscidSolverType.LinearVortex);

        var hessSmithResult = service.AnalyzeInviscid(geometry, 5.0, hessSmithSettings);
        var linearVortexResult = service.AnalyzeInviscid(geometry, 5.0, linearVortexSettings);

        Assert.True(hessSmithResult.LiftCoefficient > 0, "Hess-Smith CL should be positive at alpha=5.");
        Assert.True(linearVortexResult.LiftCoefficient > 0, "Linear-vortex CL should be positive at alpha=5.");
        Assert.NotEqual(
            Math.Round(hessSmithResult.LiftCoefficient, 3),
            Math.Round(linearVortexResult.LiftCoefficient, 3));
    }

    [Fact]
    public void AnalyzeInviscid_DefaultSettings_UsesHessSmith()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var defaultResult = service.AnalyzeInviscid(geometry, 5.0);
        var explicitHessSmithResult = service.AnalyzeInviscid(
            geometry, 5.0,
            new AnalysisSettings(inviscidSolverType: InviscidSolverType.HessSmith));

        Assert.Equal(defaultResult.LiftCoefficient, explicitHessSmithResult.LiftCoefficient, 10);
    }
}

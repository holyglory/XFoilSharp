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
    public void SweepDisplacementCoupledAlpha_ReturnsExpectedPointCountAndFiniteMetrics()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000d,
            transitionReynoldsTheta: 120d,
            criticalAmplificationFactor: 3d);

        var sweep = service.SweepDisplacementCoupledAlpha(
            geometry,
            0d,
            4d,
            2d,
            settings,
            couplingIterations: 2,
            viscousIterations: 6,
            residualTolerance: 0.8d,
            displacementRelaxation: 0.4d);

        Assert.Equal(3, sweep.Points.Count);
        Assert.Equal(0d, sweep.Points[0].AngleOfAttackDegrees);
        Assert.Equal(4d, sweep.Points[^1].AngleOfAttackDegrees);
        Assert.All(sweep.Points, point =>
        {
            Assert.True(double.IsFinite(point.LiftCoefficient));
            Assert.True(double.IsFinite(point.EstimatedProfileDragCoefficient));
            Assert.True(double.IsFinite(point.MomentCoefficientQuarterChord));
            Assert.True(double.IsFinite(point.FinalSurfaceResidual));
            Assert.True(double.IsFinite(point.FinalTransitionResidual));
            Assert.True(double.IsFinite(point.FinalWakeResidual));
            Assert.True(point.FinalDisplacementRelaxation >= 0.08d);
            Assert.True(point.FinalSeedEdgeVelocityChange >= 0d);
        });
    }

    [Fact]
    public void AnalyzeDisplacementCoupledForLiftCoefficient_FindsPositiveAlphaForPositiveLift()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 500_000d,
            transitionReynoldsTheta: 100d,
            criticalAmplificationFactor: 3d);

        var result = service.AnalyzeDisplacementCoupledForLiftCoefficient(
            geometry,
            0.35d,
            settings,
            couplingIterations: 2,
            viscousIterations: 8,
            residualTolerance: 0.8d,
            displacementRelaxation: 0.35d,
            initialAlphaDegrees: 2d,
            liftTolerance: 0.20d,
            maxIterations: 8);

        Assert.True(result.SolvedAngleOfAttackDegrees > -1d);
        Assert.InRange(result.OperatingPoint.FinalAnalysis.LiftCoefficient, 0.15d, 0.55d);
        Assert.True(result.OperatingPoint.EstimatedProfileDragCoefficient >= 0d);
        Assert.True(double.IsFinite(result.OperatingPoint.FinalSolveResult.FinalTransitionResidual));
    }

    [Fact]
    public void SweepDisplacementCoupledLiftCoefficient_ReturnsExpectedPointCountAndIncreasingSolvedAlphaTrend()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 500_000d,
            transitionReynoldsTheta: 100d,
            criticalAmplificationFactor: 3d);

        var sweep = service.SweepDisplacementCoupledLiftCoefficient(
            geometry,
            0.10d,
            0.50d,
            0.20d,
            settings,
            couplingIterations: 2,
            viscousIterations: 8,
            residualTolerance: 0.8d,
            displacementRelaxation: 0.35d,
            initialAlphaDegrees: 2d,
            liftTolerance: 0.20d,
            maxIterations: 8);

        Assert.Equal(3, sweep.Points.Count);
        Assert.All(sweep.Points, point => Assert.True(point.OperatingPoint.EstimatedProfileDragCoefficient >= 0d));
        Assert.True(sweep.Points[1].SolvedAngleOfAttackDegrees >= sweep.Points[0].SolvedAngleOfAttackDegrees - 1e-9);
        Assert.True(sweep.Points[2].SolvedAngleOfAttackDegrees >= sweep.Points[1].SolvedAngleOfAttackDegrees - 1e-9);
    }
}

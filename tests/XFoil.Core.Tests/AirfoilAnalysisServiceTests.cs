using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: OPER, ALFA, CLI
// Secondary legacy source: f_xfoil/src/xpanel.f :: PANGEN
// Role in port: Verifies the managed analysis facade that orchestrates the inviscid sweep and target-lift workflows derived from legacy operating-point routines.
// Differences: The test harness drives .NET service entry points instead of the interactive legacy command loop, while still checking the same lift-trend and solver-selection behavior.
// Decision: Keep the managed test structure because it validates the public orchestration layer rather than replaying the legacy UI flow.
namespace XFoil.Core.Tests;

public sealed class AirfoilAnalysisServiceTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ.
    // Difference from legacy: This test exercises the managed sweep facade rather than the legacy command interpreter, but it checks the same alpha-sequence point generation contract.
    // Decision: Keep this managed coverage because the port exposes sweep execution through AirfoilAnalysisService instead of the legacy REPL.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ.
    // Difference from legacy: The assertion is phrased as a monotonic managed regression check instead of comparing console output from the legacy alpha sweep.
    // Decision: Keep the managed trend check because it guards the same lift-ordering behavior with clearer test diagnostics.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: ALFA / OPER.
    // Difference from legacy: The test cross-checks the managed sweep against repeated managed single-point solves instead of probing the legacy state machine.
    // Decision: Keep this managed equivalence check because it validates the refactored orchestration boundary directly.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: CLI.
    // Difference from legacy: The target-lift solve is asserted through the managed API and bounded tolerances instead of the interactive CLI workflow.
    // Decision: Keep the managed assertion because it verifies the same positive-alpha solution behavior with less harness coupling.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: CLI.
    // Difference from legacy: This managed regression reduces the legacy trim solve to a near-zero target-lift invariant.
    // Decision: Keep the managed invariant because it is the clearest public-surface check for the same legacy behavior.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: CSEQ / CLI.
    // Difference from legacy: The test checks the managed lift-sweep wrapper rather than the legacy command sequence, but it preserves the expected solved-alpha ordering.
    // Decision: Keep the managed wrapper test because the production entry point is intentionally higher level than the legacy UI.
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
    // Legacy mapping: none.
    // Difference from legacy: Linear-vortex solver selection is a managed-only extension beyond the original single inviscid legacy path.
    // Decision: Keep this managed-only test because it validates new solver-selection functionality with no direct Fortran analogue.
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
    // Legacy mapping: none.
    // Difference from legacy: Comparing Hess-Smith and linear-vortex managed backends is unique to the C# port and has no single legacy counterpart.
    // Decision: Keep the managed comparison because it protects the intentional multi-solver design added by the port.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: OPER default inviscid operating point path.
    // Difference from legacy: The managed default selection is asserted explicitly instead of being implied by the legacy command environment.
    // Decision: Keep the managed default-contract test because the public API must document and preserve its default solver choice.
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

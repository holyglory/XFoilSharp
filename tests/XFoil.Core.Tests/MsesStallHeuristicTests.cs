using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Regression tests for <see cref="ThesisClosureStallHeuristic.IsLikelyStalled"/>.
/// Pins the empirical thresholds against NACA 0012 Re=3e6 polar
/// behavior so threshold drift doesn't silently change user-facing
/// stall warnings.
/// </summary>
public class ThesisClosureStallHeuristicTests
{
    private static ViscousAnalysisResult RunPoint(double alphaDeg)
    {
        var geom = new XFoil.Core.Services.NacaAirfoilGenerator()
            .Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);
        var svc = new ThesisClosureAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        return svc.AnalyzeViscous(geom, alphaDeg, settings);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(8.0)]
    [InlineData(10.0)]
    public void AttachedCases_NotFlagged(double alpha)
    {
        var r = RunPoint(alpha);
        Assert.False(
            ThesisClosureStallHeuristic.IsLikelyStalled(r.UpperProfiles),
            $"α={alpha} should not be flagged as stalled. "
            + $"TE: H_u={r.UpperProfiles[r.UpperProfiles.Length - 1].Hk}, "
            + $"δ*_u={r.UpperProfiles[r.UpperProfiles.Length - 1].DStar}");
    }

    [Theory]
    [InlineData(16.0)]
    [InlineData(18.0)]
    [InlineData(20.0)]
    public void StalledCases_Flagged(double alpha)
    {
        var r = RunPoint(alpha);
        Assert.True(
            ThesisClosureStallHeuristic.IsLikelyStalled(r.UpperProfiles),
            $"α={alpha} should be flagged as stalled. "
            + $"TE: H_u={r.UpperProfiles[r.UpperProfiles.Length - 1].Hk}, "
            + $"δ*_u={r.UpperProfiles[r.UpperProfiles.Length - 1].DStar}");
    }

    [Fact]
    public void EmptyProfiles_ReturnsFalse()
    {
        Assert.False(ThesisClosureStallHeuristic.IsLikelyStalled(
            System.Array.Empty<BoundaryLayerProfile>()));
    }

    [Fact]
    public void SingleProfile_ReturnsFalse()
    {
        var one = new[] { new BoundaryLayerProfile { Hk = 10.0, DStar = 1.0 } };
        Assert.False(ThesisClosureStallHeuristic.IsLikelyStalled(one));
    }

    [Fact]
    public void SyntheticHeavilySeparatedTE_ReturnsTrue()
    {
        var profiles = new BoundaryLayerProfile[20];
        for (int i = 0; i < 20; i++)
        {
            profiles[i] = new BoundaryLayerProfile
            {
                Hk = i < 19 ? 1.5 : 3.0,  // TE has H > 2.2
                DStar = 0.001,
            };
        }
        Assert.True(ThesisClosureStallHeuristic.IsLikelyStalled(profiles));
    }

    [Fact]
    public void SyntheticThickTEDStar_ReturnsTrue()
    {
        var profiles = new BoundaryLayerProfile[20];
        for (int i = 0; i < 20; i++)
        {
            profiles[i] = new BoundaryLayerProfile
            {
                Hk = 1.4,
                DStar = i < 19 ? 0.01 : 0.1,  // TE δ* = 10% chord
            };
        }
        Assert.True(ThesisClosureStallHeuristic.IsLikelyStalled(profiles));
    }

    [Fact]
    public void SyntheticBackHalfSeparation_ReturnsTrue()
    {
        // Station 15 of 20 (75 % through) has H > 3.5 but TE is
        // "recovered" — back-half scan should catch it.
        var profiles = new BoundaryLayerProfile[20];
        for (int i = 0; i < 20; i++)
        {
            double hk = i == 15 ? 4.0 : 1.4;
            profiles[i] = new BoundaryLayerProfile
            {
                Hk = hk,
                DStar = 0.005,
            };
        }
        Assert.True(ThesisClosureStallHeuristic.IsLikelyStalled(profiles));
    }

    [Fact]
    public void SyntheticLeNearLaminarBubble_NotFlagged()
    {
        // Station 3 (15 % through) has H > 3.5 — the classic laminar
        // separation bubble near LE. Back-half scan shouldn't see it.
        var profiles = new BoundaryLayerProfile[20];
        for (int i = 0; i < 20; i++)
        {
            double hk = i == 3 ? 4.0 : 1.4;
            profiles[i] = new BoundaryLayerProfile
            {
                Hk = hk,
                DStar = 0.005,
            };
        }
        Assert.False(ThesisClosureStallHeuristic.IsLikelyStalled(profiles));
    }
}

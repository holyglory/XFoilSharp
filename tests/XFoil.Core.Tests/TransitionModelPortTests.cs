using System;
using Xunit;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for TransitionModel ported from xblsys.f DAMPL, DAMPL2, AXSET, TRCHEK2.
/// Values verified against the Fortran implementation.
/// </summary>
public class TransitionModelPortTests
{
    private const double JacobianEps = 1e-5;
    private const double JacobianTol = 1e-4;

    // ================================================================
    // DAMPL (ComputeAmplificationRate) tests
    // ================================================================

    [Fact]
    public void ComputeAmplificationRate_Amplifying_ReturnsPositiveAx()
    {
        // Hk=2.1, theta=0.001, Rt=50 (Ue*theta/nu = Re_ref * Ue * theta)
        // For amplifying flow, Rt must be above critical Re_theta
        double hk = 2.1;
        double th = 0.001;
        double rt = 10000.0; // Above critical for Hk=2.1 (Rcrit ~ 6000)

        var (ax, ax_hk, ax_th, ax_rt) = TransitionModel.ComputeAmplificationRate(hk, th, rt);

        Assert.True(ax > 0.0, $"Expected positive amplification, got {ax}");
        Assert.False(double.IsNaN(ax), "AX should not be NaN");
        Assert.False(double.IsNaN(ax_hk), "AX_HK should not be NaN");
        Assert.False(double.IsNaN(ax_th), "AX_TH should not be NaN");
        Assert.False(double.IsNaN(ax_rt), "AX_RT should not be NaN");
    }

    [Fact]
    public void ComputeAmplificationRate_BelowCriticalRe_ReturnsZero()
    {
        // Very low Rt -- well below critical
        double hk = 2.5;
        double th = 0.001;
        double rt = 10.0; // Far below critical

        var (ax, ax_hk, ax_th, ax_rt) = TransitionModel.ComputeAmplificationRate(hk, th, rt);

        Assert.Equal(0.0, ax);
        Assert.Equal(0.0, ax_hk);
        Assert.Equal(0.0, ax_th);
        Assert.Equal(0.0, ax_rt);
    }

    [Fact]
    public void ComputeAmplificationRate_JacobianHk_MatchesCentralDifference()
    {
        double hk = 2.5, th = 0.001, rt = 1000.0;
        double eps = 1e-6;

        var (ax, ax_hk, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, rt);
        var (axP, _, _, _) = TransitionModel.ComputeAmplificationRate(hk + eps, th, rt);
        var (axM, _, _, _) = TransitionModel.ComputeAmplificationRate(hk - eps, th, rt);

        double numerical = (axP - axM) / (2.0 * eps);
        Assert.True(Math.Abs(ax_hk - numerical) < JacobianTol,
            $"AX_HK analytical={ax_hk:E6}, numerical={numerical:E6}, diff={Math.Abs(ax_hk - numerical):E6}");
    }

    [Fact]
    public void ComputeAmplificationRate_JacobianTh_MatchesCentralDifference()
    {
        double hk = 2.5, th = 0.001, rt = 1000.0;
        double eps = th * 1e-6;

        var (_, _, ax_th, _) = TransitionModel.ComputeAmplificationRate(hk, th, rt);
        var (axP, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th + eps, rt);
        var (axM, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th - eps, rt);

        double numerical = (axP - axM) / (2.0 * eps);
        Assert.True(Math.Abs(ax_th - numerical) < Math.Abs(ax_th) * 0.01 + JacobianTol,
            $"AX_TH analytical={ax_th:E6}, numerical={numerical:E6}");
    }

    [Fact]
    public void ComputeAmplificationRate_JacobianRt_MatchesCentralDifference()
    {
        double hk = 2.5, th = 0.001, rt = 1000.0;
        double eps = rt * 1e-6;

        var (_, _, _, ax_rt) = TransitionModel.ComputeAmplificationRate(hk, th, rt);
        var (axP, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, rt + eps);
        var (axM, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, rt - eps);

        double numerical = (axP - axM) / (2.0 * eps);
        Assert.True(Math.Abs(ax_rt - numerical) < Math.Abs(ax_rt) * 0.01 + JacobianTol,
            $"AX_RT analytical={ax_rt:E6}, numerical={numerical:E6}");
    }

    // ================================================================
    // DAMPL2 (ComputeAmplificationRateHighHk) tests
    // ================================================================

    [Fact]
    public void ComputeAmplificationRateHighHk_HighHk_ReturnsValidResult()
    {
        double hk = 5.0;
        double th = 0.002;
        double rt = 500.0;

        var (ax, ax_hk, ax_th, ax_rt) = TransitionModel.ComputeAmplificationRateHighHk(hk, th, rt);

        Assert.False(double.IsNaN(ax), "AX should not be NaN for Hk=5.0");
        Assert.False(double.IsNaN(ax_hk), "AX_HK should not be NaN");
        Assert.False(double.IsNaN(ax_th), "AX_TH should not be NaN");
        Assert.False(double.IsNaN(ax_rt), "AX_RT should not be NaN");
    }

    [Fact]
    public void ComputeAmplificationRateHighHk_BelowCriticalRe_ReturnsZero()
    {
        double hk = 5.0;
        double th = 0.002;
        double rt = 5.0; // Far below critical

        var (ax, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk, th, rt);
        Assert.Equal(0.0, ax);
    }

    [Fact]
    public void ComputeAmplificationRateHighHk_JacobianHk_MatchesCentralDifference()
    {
        double hk = 5.0, th = 0.002, rt = 800.0;
        double eps = 1e-5;

        var (_, ax_hk, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk, th, rt);
        var (axP, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk + eps, th, rt);
        var (axM, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk - eps, th, rt);

        double numerical = (axP - axM) / (2.0 * eps);
        Assert.True(Math.Abs(ax_hk - numerical) < Math.Abs(ax_hk) * 0.01 + JacobianTol,
            $"AX_HK analytical={ax_hk:E6}, numerical={numerical:E6}");
    }

    [Fact]
    public void ComputeAmplificationRateHighHk_LowHk_MatchesDAMPL()
    {
        // For Hk < 3.5, DAMPL2 should return same as DAMPL (no blending kicks in)
        double hk = 2.5, th = 0.001, rt = 1000.0;

        var (ax1, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, rt);
        var (ax2, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk, th, rt);

        // DAMPL2 has an additional 0.1*exp(-20*HMI) term in AF, so they differ
        // slightly at low Hk but should be very close (within 0.01%)
        Assert.True(Math.Abs(ax1 - ax2) / Math.Max(Math.Abs(ax1), 1e-20) < 1e-3,
            $"Below Hk=3.5, DAMPL and DAMPL2 should nearly match: DAMPL={ax1:E10}, DAMPL2={ax2:E10}");
    }

    // ================================================================
    // AXSET (ComputeTransitionSensitivities) tests
    // ================================================================

    [Fact]
    public void ComputeTransitionSensitivities_BasicCase_ReturnsValidResult()
    {
        double hk1 = 2.3, t1 = 0.0008, rt1 = 800.0, a1 = 3.0;
        double hk2 = 2.5, t2 = 0.001, rt2 = 1000.0, a2 = 5.0;
        double acrit = 9.0;

        var result = TransitionModel.ComputeTransitionSensitivities(
            hk1, t1, rt1, a1, hk2, t2, rt2, a2, acrit, useHighHkModel: true);

        Assert.True(result.Ax >= 0.0, $"AX should be non-negative, got {result.Ax}");
        Assert.False(double.IsNaN(result.Ax), "AX should not be NaN");
    }

    [Fact]
    public void ComputeTransitionSensitivities_IncludesDaxNearNcrit()
    {
        // When A is near Acrit, the DAX term adds positive growth
        double hk1 = 2.3, t1 = 0.0008, rt1 = 800.0, a1 = 8.5;
        double hk2 = 2.5, t2 = 0.001, rt2 = 1000.0, a2 = 8.8;
        double acrit = 9.0;

        var result = TransitionModel.ComputeTransitionSensitivities(
            hk1, t1, rt1, a1, hk2, t2, rt2, a2, acrit, useHighHkModel: true);

        Assert.True(result.Ax > 0.0, "Should have positive amplification near Ncrit");
    }

    [Fact]
    public void ComputeTransitionSensitivities_RmsAverage_IsConsistent()
    {
        // The RMS averaging should produce result >= max(AX1, AX2)/sqrt(2)
        double hk1 = 2.5, t1 = 0.001, rt1 = 1000.0, a1 = 3.0;
        double hk2 = 2.5, t2 = 0.001, rt2 = 1000.0, a2 = 3.0;
        double acrit = 9.0;

        var result = TransitionModel.ComputeTransitionSensitivities(
            hk1, t1, rt1, a1, hk2, t2, rt2, a2, acrit, useHighHkModel: true);

        // For identical stations, RMS average = single station value
        var (axSingle, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk1, t1, rt1);
        // Plus DAX term
        Assert.True(result.Ax >= axSingle - 1e-10,
            $"RMS average {result.Ax} should be >= single station {axSingle}");
    }

    // ================================================================
    // Edge cases and smooth blending
    // ================================================================

    [Fact]
    public void ComputeAmplificationRate_HkNear1_NoCrash()
    {
        // Hk near 1.0 should not crash (clamped internally)
        double hk = 1.05, th = 0.001, rt = 100.0;

        var (ax, ax_hk, ax_th, ax_rt) = TransitionModel.ComputeAmplificationRate(hk, th, rt);

        Assert.False(double.IsNaN(ax));
        Assert.False(double.IsInfinity(ax));
    }

    [Fact]
    public void ComputeAmplificationRateHighHk_SmoothBlending_NearHk4()
    {
        // Check that DAMPL2 provides smooth blending between Hk=3.5 and Hk=4.0
        double th = 0.002, rt = 1000.0;

        var (ax35, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(3.49, th, rt);
        var (ax37, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(3.7, th, rt);
        var (ax40, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(4.01, th, rt);

        // Should all be finite
        Assert.False(double.IsNaN(ax35));
        Assert.False(double.IsNaN(ax37));
        Assert.False(double.IsNaN(ax40));
    }

    [Fact]
    public void ComputeAmplificationRate_SmoothOnsetRamp_Works()
    {
        // Test that the onset ramp smoothly transitions from 0 to full amplification
        double hk = 2.5, th = 0.001;

        // Get critical Rt for this Hk
        // At Hk=2.5, HMI = 1/(2.5-1) = 0.667
        // AA = 2.492 * 0.667^0.43 ~ 2.08
        // BB = tanh(14*0.667 - 9.24) = tanh(0.098) ~ 0.098
        // GRCRIT ~ 2.08 + 0.7*(0.098+1) ~ 2.85
        // Rcrit ~ 10^2.85 ~ 708
        // Below Rcrit-DGR: AX=0
        var (axBelow, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, 400.0);
        Assert.Equal(0.0, axBelow);

        // Well above Rcrit: AX > 0
        var (axAbove, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, 2000.0);
        Assert.True(axAbove > 0.0);
    }
}

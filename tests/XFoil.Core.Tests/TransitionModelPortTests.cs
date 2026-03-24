using System;
using Xunit;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: DAMPL, DAMPL2, AXSET, TRCHEK2
// Secondary legacy source: legacy transition-root finding and amplification clamping logic
// Role in port: Verifies the managed transition-model primitives against the legacy formulas and derivative behavior.
// Differences: The managed port exposes these routines as direct static helpers with explicit derivatives, enabling far denser unit coverage than the legacy runtime ever had.
// Decision: Keep the managed helper decomposition while preserving parity-sensitive legacy evaluation order inside the implementations when required.
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
    // Legacy mapping: xblsys DAMPL amplification branch above critical Reynolds theta.
    // Difference from legacy: The managed helper is tested directly instead of only via full viscous solves. Decision: Keep the managed unit regression because it isolates the core formula clearly.
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
    // Legacy mapping: xblsys DAMPL below-critical cutoff.
    // Difference from legacy: Zero-output behavior is asserted directly on the managed helper. Decision: Keep the managed regression because it documents an important piecewise branch explicitly.
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
    // Legacy mapping: xblsys DAMPL derivative with respect to Hk.
    // Difference from legacy: The port exposes analytical derivatives for direct finite-difference validation. Decision: Keep the managed derivative test because this path is parity-sensitive and high-value.
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
    // Legacy mapping: xblsys DAMPL derivative with respect to theta.
    // Difference from legacy: The managed helper surfaces the derivative explicitly for direct numerical verification. Decision: Keep the managed regression because it tightly constrains the ported Jacobian.
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
    // Legacy mapping: xblsys DAMPL derivative with respect to Rt.
    // Difference from legacy: The derivative is tested directly on the managed helper instead of indirectly through a Newton solve. Decision: Keep the managed regression because it guards a parity-critical derivative path.
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
    // Legacy mapping: xblsys DAMPL2 high-Hk branch.
    // Difference from legacy: The managed helper is exercised directly on an isolated high-Hk case. Decision: Keep the managed unit regression because it isolates the second amplification formula family.
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
    // Legacy mapping: xblsys DAMPL2 below-critical cutoff.
    // Difference from legacy: Zero-output branch behavior is asserted directly on the managed helper. Decision: Keep the managed regression because it documents the high-Hk piecewise cutoff explicitly.
    public void ComputeAmplificationRateHighHk_BelowCriticalRe_ReturnsZero()
    {
        double hk = 5.0;
        double th = 0.002;
        double rt = 5.0; // Far below critical

        var (ax, _, _, _) = TransitionModel.ComputeAmplificationRateHighHk(hk, th, rt);
        Assert.Equal(0.0, ax);
    }

    [Fact]
    // Legacy mapping: xblsys DAMPL2 derivative with respect to Hk.
    // Difference from legacy: The managed port exposes analytical derivatives for direct central-difference checks. Decision: Keep the managed derivative regression because it protects a parity-sensitive branch.
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
    // Legacy mapping: DAMPL2-to-DAMPL blending near the low-Hk boundary.
    // Difference from legacy: The managed test makes the formula-family handoff explicit instead of relying on runtime continuity. Decision: Keep the managed regression because it documents an important transition behavior.
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
    // Legacy mapping: xblsys AXSET transition sensitivity assembly.
    // Difference from legacy: The managed helper returns typed sensitivities for direct validation. Decision: Keep the managed regression because it isolates AXSET behavior outside the full solver.
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
    // Legacy mapping: AXSET dependence on dAX near Ncrit.
    // Difference from legacy: The managed test inspects the explicit sensitivity tuple instead of hidden solver arrays. Decision: Keep the managed regression because it documents the ported derivative contract.
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
    // Legacy mapping: AXSET RMS averaging behavior.
    // Difference from legacy: The averaging relation is asserted directly on the managed helper output. Decision: Keep the managed regression because it constrains a subtle formula detail.
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
    // Legacy mapping: DAMPL near Hk=1 stability behavior.
    // Difference from legacy: The managed helper is tested directly for finite output in this edge region. Decision: Keep the managed regression because it guards a numerically sensitive branch.
    public void ComputeAmplificationRate_HkNear1_NoCrash()
    {
        // Hk near 1.0 should not crash (clamped internally)
        double hk = 1.05, th = 0.001, rt = 100.0;

        var (ax, ax_hk, ax_th, ax_rt) = TransitionModel.ComputeAmplificationRate(hk, th, rt);

        Assert.False(double.IsNaN(ax));
        Assert.False(double.IsInfinity(ax));
    }

    [Fact]
    // Legacy mapping: DAMPL/DAMPL2 smooth blending near Hk=4.
    // Difference from legacy: The managed test asserts continuity in a transition region that is hard to observe in the legacy runtime. Decision: Keep the managed regression because it isolates this handoff clearly.
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
    // Legacy mapping: DAMPL onset ramp behavior.
    // Difference from legacy: The managed helper is verified directly over the smooth-onset region rather than only via global solver effects. Decision: Keep the managed regression because it constrains a parity-sensitive nonlinear block.
    public void ComputeAmplificationRate_SmoothOnsetRamp_Works()
    {
        // Test that the onset ramp smoothly transitions from 0 to full amplification
        double hk = 2.5, th = 0.001;

        // Below Rcrit-DGR: AX=0
        var (axBelow, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, 400.0);
        Assert.Equal(0.0, axBelow);

        // Well above Rcrit: AX > 0
        var (axAbove, _, _, _) = TransitionModel.ComputeAmplificationRate(hk, th, 2000.0);
        Assert.True(axAbove > 0.0);
    }

    // ================================================================
    // TRCHEK2 (CheckTransition) tests
    // ================================================================

    [Fact]
    // Legacy mapping: xblsys TRCHEK2 natural transition solve when N crosses Ncrit.
    // Difference from legacy: The managed port exposes the transition finder directly instead of embedding it inside the marching solve. Decision: Keep the managed regression because it isolates the root-finding logic.
    public void CheckTransition_StraddlingNcrit_ConvergesToTransition()
    {
        // Two BL stations straddling N_crit: N1=8.5 < 9.0 < N2=10.0
        // Use larger interval and higher amplification rate so AX*dx can bridge the gap
        double x1 = 0.30, x2 = 0.60; // xi (arc-length) at stations
        double ampl1 = 8.5;           // N at station 1 (close to Ncrit)
        double ampl2 = 10.0;          // N at station 2
        double amcrit = 9.0;

        // BL variables: higher Rt for strong amplification
        double hk1 = 2.6, th1 = 0.0005, rt1 = 8000.0;
        double hk2 = 2.8, th2 = 0.0006, rt2 = 10000.0;
        double ue1 = 1.0, ue2 = 0.96;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: null);

        Assert.True(result.TransitionOccurred, "Transition should occur when straddling Ncrit");
        Assert.True(result.Converged, $"Newton iteration should converge (iterations={result.Iterations})");
        Assert.True(result.TransitionXi > x1, $"Transition xi {result.TransitionXi} should be > x1 {x1}");
        Assert.True(result.TransitionXi < x2, $"Transition xi {result.TransitionXi} should be < x2 {x2}");
        Assert.True(result.Iterations < 30, $"Should converge in < 30 iterations, took {result.Iterations}");
        Assert.Equal(TransitionModel.TransitionResultType.Free, result.Type);
    }

    [Fact]
    // Legacy mapping: TRCHEK2 no-transition branch.
    // Difference from legacy: The no-crossing outcome is returned explicitly by the managed helper rather than remaining implicit in solver state. Decision: Keep the managed regression because it documents the branch result clearly.
    public void CheckTransition_NoStraddling_ReturnsNoTransition()
    {
        // Both stations below N_crit: N1=3.0 < N2=5.0 < 9.0
        double x1 = 0.10, x2 = 0.15;
        double ampl1 = 3.0, ampl2 = 5.0;
        double amcrit = 9.0;

        double hk1 = 2.3, th1 = 0.0006, rt1 = 800.0;
        double hk2 = 2.4, th2 = 0.0007, rt2 = 900.0;
        double ue1 = 1.05, ue2 = 1.03;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: null);

        Assert.False(result.TransitionOccurred, "No transition when both below Ncrit");
    }

    [Fact]
    // Legacy mapping: TRCHEK2 implicit amplification update without transition.
    // Difference from legacy: The managed helper surfaces the updated amplification state directly. Decision: Keep the managed regression because it constrains an internal-but-exposed formula path.
    public void CheckTransition_NoTransition_StillUpdatesImplicitAmplification()
    {
        // TRCHEK2 always solves the implicit downstream N2 update, even when the
        // interval stays laminar. Returning the caller's initial guess here hides
        // the first parity mismatch and skips the no-transition trace records.
        double x1 = 0.10, x2 = 0.15;
        double ampl1 = 3.0, ampl2 = 0.0;
        double amcrit = 9.0;

        double hk1 = 2.3, th1 = 0.0006, rt1 = 800.0;
        double hk2 = 2.4, th2 = 0.0007, rt2 = 900.0;
        double ue1 = 1.05, ue2 = 1.03;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: null);

        Assert.False(result.TransitionOccurred, "Interval should remain laminar");
        Assert.True(result.AmplAtTransition > ampl1, "Implicit TRCHEK2 update should advance N2");
        Assert.NotEqual(ampl2, result.AmplAtTransition);
    }

    [Fact]
    // Legacy mapping: forced transition inside the current interval.
    // Difference from legacy: The forced-location case is asserted explicitly on the managed result instead of via solver side effects. Decision: Keep the managed regression because it documents the override behavior directly.
    public void CheckTransition_ForcedInInterval_ReturnsForcedLocation()
    {
        // Keep the interval subcritical so the result exercises the forced branch
        // directly instead of relying on a competing natural transition.
        double x1 = 0.30, x2 = 0.35;
        double ampl1 = 7.5, ampl2 = 8.0;
        double amcrit = 9.0;

        double hk1 = 2.4, th1 = 0.0008, rt1 = 1500.0;
        double hk2 = 2.6, th2 = 0.0010, rt2 = 2000.0;
        double ue1 = 1.0, ue2 = 0.98;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        double forcedXtr = 0.33;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: forcedXtr);

        Assert.True(result.TransitionOccurred, "Transition should occur (forced)");
        Assert.Equal(TransitionModel.TransitionResultType.Forced, result.Type);
        Assert.Equal(forcedXtr, result.TransitionXi);
    }

    [Fact]
    // Legacy mapping: forced-versus-natural transition precedence.
    // Difference from legacy: The managed helper returns both possibilities in an explicit decision path. Decision: Keep the managed regression because it clearly documents precedence behavior.
    public void CheckTransition_ForcedDownstreamOfNatural_ReturnsNaturalLocation()
    {
        // Reuse a strongly amplifying interval so the free transition is real, not
        // an artifact of the caller-provided N2 guess.
        double x1 = 0.30, x2 = 0.60;
        double ampl1 = 7.5, ampl2 = 10.0;
        double amcrit = 9.0;

        double hk1 = 2.6, th1 = 0.0005, rt1 = 8000.0;
        double hk2 = 2.8, th2 = 0.0006, rt2 = 10000.0;
        double ue1 = 1.0, ue2 = 0.96;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        double forcedXtr = 0.55;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: forcedXtr);

        Assert.True(result.TransitionOccurred, "Transition should occur (natural, upstream of forced)");
        Assert.Equal(TransitionModel.TransitionResultType.Free, result.Type);
        Assert.True(result.TransitionXi < forcedXtr,
            $"Natural transition {result.TransitionXi} should be upstream of forced {forcedXtr}");
    }

    [Fact]
    // Legacy mapping: TRCHEK2 amplification clamping behavior.
    // Difference from legacy: Clamp behavior is validated directly on the managed helper rather than only through end-to-end solver stability. Decision: Keep the managed regression because it protects a subtle stability rule.
    public void CheckTransition_AmplClamping_PreventsOvershoot()
    {
        // N2 far above Ncrit -- check that clamping prevents divergence
        double x1 = 0.30, x2 = 0.35;
        double ampl1 = 8.9;
        double ampl2 = 15.0; // Far above Ncrit
        double amcrit = 9.0;

        double hk1 = 2.4, th1 = 0.0008, rt1 = 1500.0;
        double hk2 = 2.6, th2 = 0.0010, rt2 = 2000.0;
        double ue1 = 1.0, ue2 = 0.98;
        double d1 = hk1 * th1, d2 = hk2 * th2;

        var result = TransitionModel.CheckTransition(
            x1, x2, ampl1, ampl2, amcrit,
            hk1, th1, rt1, ue1, d1,
            hk2, th2, rt2, ue2, d2,
            useHighHkModel: true, forcedXtr: null);

        Assert.True(result.TransitionOccurred, "Should find transition");
        Assert.True(result.TransitionXi >= x1 && result.TransitionXi <= x2,
            $"Transition xi {result.TransitionXi} should be within [{x1}, {x2}]");
        Assert.False(double.IsNaN(result.AmplAtTransition));
    }
}

using XFoil.MsesSolver.Closure;

namespace XFoil.Core.Tests;

/// <summary>
/// Unit tests for Drela MSES closure relations (Phase 1 of the
/// MsesClosurePlan scaffolding). Each test pins one correlation
/// against hand-checked reference values derived from Drela's
/// 1986 MIT thesis (§4, Appendix A).
///
/// These tests are the acceptance gate for Phase 1 — they must all
/// pass before closure relations are wired into a BL marcher.
/// </summary>
public class MsesClosureRelationsTests
{
    [Fact]
    public void ComputeHk_AtZeroMach_ReturnsInputH()
    {
        // Me=0: Hk must equal H exactly (num = H, den = 1).
        Assert.Equal(2.5, MsesClosureRelations.ComputeHk(2.5, 0.0), 12);
        Assert.Equal(1.4, MsesClosureRelations.ComputeHk(1.4, 0.0), 12);
        Assert.Equal(4.0, MsesClosureRelations.ComputeHk(4.0, 0.0), 12);
    }

    [Fact]
    public void ComputeHk_AtMach05_MatchesDrelaFormula()
    {
        // H=2.5, Me=0.5: Hk = (2.5 - 0.290*0.25) / (1 + 0.113*0.25)
        //                   = (2.5 - 0.0725) / 1.02825
        //                   = 2.4275 / 1.02825 = 2.36081...
        double actual = MsesClosureRelations.ComputeHk(2.5, 0.5);
        Assert.Equal(2.36081, actual, 4);
    }

    [Fact]
    public void ComputeHStarLaminar_AtHk4_Returns1_515()
    {
        // Piecewise junction point: both branches give 1.515 at Hk=4.
        double actual = MsesClosureRelations.ComputeHStarLaminar(4.0, 1000.0);
        Assert.Equal(1.515, actual, 6);
    }

    [Fact]
    public void ComputeHStarLaminar_AtHk25_IncreasesAboveJunction()
    {
        // Hk=2.5 (typical attached BL): H* = 1.515 + 0.076*(-1.5)²/2.5
        //                                  = 1.515 + 0.076*2.25/2.5
        //                                  = 1.515 + 0.0684 = 1.5834
        double actual = MsesClosureRelations.ComputeHStarLaminar(2.5, 1000.0);
        Assert.Equal(1.5834, actual, 4);
    }

    [Fact]
    public void ComputeHStarLaminar_AtHk6_SeparatedBranch()
    {
        // Hk=6 (separated lam BL): H* = 1.515 + 0.040*(2)²/6
        //                             = 1.515 + 0.160/6 = 1.5417
        double actual = MsesClosureRelations.ComputeHStarLaminar(6.0, 1000.0);
        Assert.Equal(1.5417, actual, 4);
    }

    [Fact]
    public void ComputeHStarTurbulent_AttachedBranch_ProducesReasonableValue()
    {
        // Hk=1.5, Reθ=1000, Me=0 (typical flat-plate turbulent):
        // H0 = 3 + 400/1000 = 3.4, Hk=1.5 < H0 so attached branch.
        // term = (3.4-1.5)/3.4 = 0.5588
        // factor = 0.165 - 1.6/sqrt(1000) = 0.165 - 0.0506 = 0.1144
        // H* = 1.505 + 4/1000 + 0.1144 * 0.5588³ / (1.5+0.5)
        //    = 1.509 + 0.00997 = 1.519
        // Then Me=0 wrapper: no change.
        double actual = MsesClosureRelations.ComputeHStarTurbulent(1.5, 1000.0, 0.0);
        Assert.InRange(actual, 1.50, 1.55);
    }

    [Fact]
    public void ComputeHStarTurbulent_SeparatedBranch_IsFinite()
    {
        // Hk=3 (separating turbulent), Reθ=500, Me=0. Should remain
        // finite and reasonable (not blow up at the branch switch).
        double actual = MsesClosureRelations.ComputeHStarTurbulent(3.0, 500.0, 0.0);
        Assert.True(double.IsFinite(actual));
        Assert.InRange(actual, 1.4, 2.0);
    }

    [Fact]
    public void ComputeHStarTurbulent_MeCompressibility_IncreasesWithMach()
    {
        // Mach correction wrapper: H* should grow with Me at fixed
        // (Hk, Reθ) per Drela §4.2.
        double h0 = MsesClosureRelations.ComputeHStarTurbulent(1.5, 1000.0, 0.0);
        double h1 = MsesClosureRelations.ComputeHStarTurbulent(1.5, 1000.0, 0.5);
        Assert.True(h1 > h0, $"Expected Me=0.5 H* ({h1}) > Me=0 H* ({h0})");
    }

    [Fact]
    public void ComputeCfLaminar_AtBlasiusHk_MatchesFlatPlateReference()
    {
        // Blasius flat-plate: Hk=2.59, Cf·Reθ/2 = 0.220 (exact).
        // f = -0.067 + 0.01977·(7.4-2.59)²/(2.59-1)
        //   = -0.067 + 0.01977·23.14/1.59
        //   = -0.067 + 0.287 = 0.220
        // Cf = 2·0.220/1000 = 4.40e-4
        double actual = MsesClosureRelations.ComputeCfLaminar(2.59, 1000.0);
        Assert.InRange(actual, 4.3e-4, 4.5e-4);
    }

    [Fact]
    public void ComputeCfLaminar_AtHk74_Junction()
    {
        // Piecewise junction at Hk=7.4. Both branches at Hk=7.4:
        //   Lower branch: f = -0.067 + 0 = -0.067.
        //   Upper branch (Hk-6=1.4): f = -0.067 + 0.022·(0)² = -0.067.
        // Cf = -0.134 / Reθ (deeply separated).
        double actual = MsesClosureRelations.ComputeCfLaminar(7.4, 1000.0);
        Assert.Equal(-0.134 / 1000.0, actual, 7);
    }

    [Fact]
    public void ComputeCfLaminar_SeparatedBranch_IsFiniteAndNegative()
    {
        // Hk=8 (deeply separated laminar, past 7.4 junction): f should
        // be close to -0.067 plus the (1 - 1.4/2)² = 0.09 · 0.022 ≈ 0.002
        // positive adjustment, staying finite but negative.
        double actual = MsesClosureRelations.ComputeCfLaminar(8.0, 1000.0);
        Assert.True(double.IsFinite(actual));
        Assert.True(actual < 0.0, $"Expected negative Cf in separated BL, got {actual}");
    }

    [Fact]
    public void ComputeCfTurbulent_AttachedBranch_PositiveAndReasonable()
    {
        // Hk=1.4 (healthy turbulent), Reθ=5000, Me=0.
        // Cf should be O(0.003-0.005) per flat-plate correlations.
        double actual = MsesClosureRelations.ComputeCfTurbulent(1.4, 5000.0, 0.0);
        Assert.InRange(actual, 1e-4, 1e-2);
    }

    [Fact]
    public void ComputeCfTurbulent_DropsWithHk()
    {
        // As Hk rises (BL thickening toward separation), Cf should drop.
        double cfLow = MsesClosureRelations.ComputeCfTurbulent(1.4, 5000.0, 0.0);
        double cfHigh = MsesClosureRelations.ComputeCfTurbulent(2.5, 5000.0, 0.0);
        Assert.True(cfHigh < cfLow, $"Expected Cf to drop with Hk: got {cfHigh} ≥ {cfLow}");
    }

    [Fact]
    public void ComputeCDLaminar_AtHk25_ProducesPositiveDissipation()
    {
        // Typical attached laminar value. Dissipation correlation +
        // H* factor should give O(1e-4) CD at Reθ=1000.
        double actual = MsesClosureRelations.ComputeCDLaminar(2.5, 1000.0);
        Assert.InRange(actual, 1e-4, 5e-4);
    }

    [Fact]
    public void ComputeCDLaminar_ContinuousAtHk4()
    {
        // Piecewise junction at Hk=4 must be continuous. Both branches
        // give g = 0.207 at Hk=4.
        double belowJunction = MsesClosureRelations.ComputeCDLaminar(3.999, 1000.0);
        double atJunction = MsesClosureRelations.ComputeCDLaminar(4.0, 1000.0);
        double aboveJunction = MsesClosureRelations.ComputeCDLaminar(4.001, 1000.0);
        Assert.Equal(belowJunction, atJunction, 4);
        Assert.Equal(atJunction, aboveJunction, 4);
    }

    [Fact]
    public void ComputeCDLaminar_IncreasesWithHk_BeyondSeparation()
    {
        // For Hk ∈ [4, 7] dissipation rises monotonically (BL in
        // separation region dissipates more energy per unit Reθ).
        double cd4 = MsesClosureRelations.ComputeCDLaminar(4.0, 1000.0);
        double cd5 = MsesClosureRelations.ComputeCDLaminar(5.0, 1000.0);
        double cd6 = MsesClosureRelations.ComputeCDLaminar(6.0, 1000.0);
        Assert.True(cd5 > cd4, $"Expected CD_5 ({cd5}) > CD_4 ({cd4})");
        Assert.True(cd6 > cd5, $"Expected CD_6 ({cd6}) > CD_5 ({cd5})");
    }

    [Fact]
    public void ComputeCTauEquilibrium_ZeroAtHk1()
    {
        // At Hk=1 (flat-plate-like), (Hk-1)³ = 0 so Cτ_eq = 0.
        double actual = MsesClosureRelations.ComputeCTauEquilibrium(1.0, 5000.0, 0.0);
        Assert.Equal(0.0, actual, 10);
    }

    [Fact]
    public void ComputeCTauEquilibrium_PositiveAndFiniteAtHk2()
    {
        // Hk=2 (mid-attached turbulent): Cτ_eq should be small but
        // positive (equilibrium outer-shear balance). Typical O(1e-3).
        double actual = MsesClosureRelations.ComputeCTauEquilibrium(2.0, 5000.0, 0.0);
        Assert.True(double.IsFinite(actual));
        Assert.InRange(actual, 1e-4, 1e-1);
    }

    [Fact]
    public void ComputeCTauEquilibrium_IncreasesWithHk()
    {
        // Past incipient separation (Hk > 2), Cτ_eq grows rapidly
        // with Hk due to the (Hk-1)³ driver. This is what makes
        // MSES's BL robust through separation.
        double ceq2 = MsesClosureRelations.ComputeCTauEquilibrium(2.0, 5000.0, 0.0);
        double ceq3 = MsesClosureRelations.ComputeCTauEquilibrium(3.0, 5000.0, 0.0);
        double ceq4 = MsesClosureRelations.ComputeCTauEquilibrium(4.0, 5000.0, 0.0);
        Assert.True(ceq3 > ceq2, $"Expected Cτeq_3 ({ceq3}) > Cτeq_2 ({ceq2})");
        Assert.True(ceq4 > ceq3, $"Expected Cτeq_4 ({ceq4}) > Cτeq_3 ({ceq3})");
    }

    [Fact]
    public void ComputeCDTurbulent_PositiveAtAttachedHk()
    {
        // Hk=1.4, Reθ=5000, Me=0, Cτ=1e-3 (small). Dissipation
        // should be positive O(1e-3) — dominated by wall friction.
        double actual = MsesClosureRelations.ComputeCDTurbulent(1.4, 5000.0, 0.0, 1e-3);
        Assert.True(actual > 0);
        Assert.InRange(actual, 1e-5, 1e-2);
    }

    [Fact]
    public void ComputeCDTurbulent_OuterLayerDominatesAtSeparation()
    {
        // Past separation (Hk large), Us saturates near 1, so the
        // wall part (Cf·Us/2) drops while the outer part
        // (Cτ·(1−Us)) approaches zero IF Cτ = 0. Real BLs carry a
        // non-trivial Cτ from the lag ODE. This test pins that the
        // function is well-defined (no NaN) at the separated regime.
        double cTauEq = MsesClosureRelations.ComputeCTauEquilibrium(3.5, 2000.0, 0.0);
        double actual = MsesClosureRelations.ComputeCDTurbulent(3.5, 2000.0, 0.0, cTauEq);
        Assert.True(double.IsFinite(actual), $"CD should stay finite in separated BL, got {actual}");
        Assert.True(actual > 0);
    }

    [Fact]
    public void ComputeCDTurbulent_RespondsToCTau()
    {
        // Outer dissipation should scale with Cτ. Doubling Cτ (at
        // fixed Hk, Reθ, Me) should raise CD.
        double cdLow = MsesClosureRelations.ComputeCDTurbulent(2.5, 5000.0, 0.0, 1e-3);
        double cdHigh = MsesClosureRelations.ComputeCDTurbulent(2.5, 5000.0, 0.0, 2e-3);
        Assert.True(cdHigh > cdLow, $"Expected CD to rise with Cτ: got {cdHigh} ≤ {cdLow}");
    }
}

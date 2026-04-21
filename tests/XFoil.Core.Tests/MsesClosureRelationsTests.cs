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
}

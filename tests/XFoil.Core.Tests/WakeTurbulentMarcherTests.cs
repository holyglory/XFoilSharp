using XFoil.ThesisClosureSolver.BoundaryLayer;
using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2f wake marcher tests. The wake is a free-shear layer with
/// Cf = 0. Its BL should relax toward H ≈ 1 as Ue recovers toward U∞,
/// with θ growing monotonically and Cτ decaying toward its lower
/// equilibrium in the freestream.
/// </summary>
public class WakeTurbulentMarcherTests
{
    [Fact]
    public void ConstantUe_ThetaStaysConstantNoGrowth()
    {
        // With Cf = 0 and dUe/dξ = 0, the momentum eq. gives dθ/dx = 0.
        // θ should be constant along the wake (no wall shear, no
        // pressure gradient).
        const int N = 51;
        var s = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            s[i] = 1.0 + i / (double)(N - 1);
            ue[i] = 1.0;
        }
        var r = WakeTurbulentMarcher.March(s, ue, 1e-6,
            theta0: 0.01, h0: 1.6, cTau0: 0.01);

        for (int i = 0; i < N; i++)
        {
            Assert.Equal(0.01, r.Theta[i], 10);
        }
    }

    [Fact]
    public void UeRecoveryDropsH_TowardOne()
    {
        // Wake with Ue recovering from 0.8·U∞ back toward U∞ should
        // see H relaxing toward 1 (freestream) as the BL equilibrates.
        const int N = 101;
        var s = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            double t = i / (double)(N - 1);
            s[i] = 1.0 + t;                 // from x=1 to x=2.
            ue[i] = 0.8 + 0.2 * t;          // recover 0.8 → 1.0.
        }
        var r = WakeTurbulentMarcher.March(s, ue, 1e-6,
            theta0: 0.01, h0: 2.0, cTau0: 0.01);

        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(r.H[i]), $"NaN H at {i}");
        }
        // H should drop below the IC as recovery proceeds.
        Assert.True(r.H[N - 1] < 2.0,
            $"Expected H to relax below IC under Ue recovery, got {r.H[N - 1]}");
    }

    [Fact]
    public void FavorableWake_ThetaShrinks()
    {
        // Under favorable gradient (Ue rising) with Cf=0, the momentum
        // eq. dθ/dx = -(H+2)·θ·dUe/dx/Ue has dUe/dx > 0 so dθ/dx < 0
        // — θ decreases. Pin that behavior.
        const int N = 51;
        var s = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            double t = i / (double)(N - 1);
            s[i] = 1.0 + t;
            ue[i] = 0.6 + 0.4 * t;
        }
        var r = WakeTurbulentMarcher.March(s, ue, 1e-6,
            theta0: 0.01, h0: 1.5, cTau0: 0.01);

        Assert.True(r.Theta[N - 1] < r.Theta[0],
            $"θ should shrink under favorable wake, got {r.Theta[0]} → {r.Theta[N - 1]}");
    }

    [Fact]
    public void SqurireYoungTE_vs_Wake_CdRatioSensible()
    {
        // The whole point of a wake marcher: Squire-Young applied at
        // the wake far-field should give a CD that's in the same
        // ballpark as Squire-Young at the TE (within 0.5× ... 2×),
        // since the momentum-thickness conservation holds in the wake
        // up to corrections of O((H_wake - 1)·dUe/Ue).
        const int N = 21;
        var s = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            double t = i / (double)(N - 1);
            s[i] = 1.0 + t;
            ue[i] = 0.9 + 0.1 * t;
        }
        double θ_TE = 0.01;
        double H_TE = 1.7;
        double cT_TE = 0.01;
        var r = WakeTurbulentMarcher.March(s, ue, 1e-6, θ_TE, H_TE, cT_TE);

        // Squire-Young at TE vs wake TE.
        double sy_TE = 2.0 * θ_TE * System.Math.Pow(ue[0], (H_TE + 5.0) / 2.0);
        double sy_wake = 2.0 * r.Theta[N - 1]
            * System.Math.Pow(ue[N - 1], (r.H[N - 1] + 5.0) / 2.0);

        Assert.InRange(sy_wake / sy_TE, 0.5, 2.0);
    }
}

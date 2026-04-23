using XFoil.ThesisClosureSolver.BoundaryLayer;
using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2e turbulent marcher tests: implicit-Newton backward-Euler
/// on thesis eq. 6.10, with Cτ carried via the 6.35 lag ODE. These
/// pins confirm the H equation doesn't oscillate on flat-plate and
/// tracks attached-gradient behavior without blowing up.
/// </summary>
public class ThesisExactTurbulentMarcherTests
{
    // Seed a typical just-transitioned turbulent IC: H ≈ 1.4, Cτ = 0.3·Cτ_eq.
    private static (double theta0, double h0, double cTau0) SeedTurbIC(double theta0)
    {
        const double hInit = 1.4;
        // Cτ_eq at the IC Hk (≈ 1.4), Reθ ~ moderate. Using a fixed
        // reference evaluation to avoid coupling the IC to station-1 Ue.
        double reTheta = 1000.0;
        double cTauEq = ThesisClosureRelations.ComputeCTauEquilibrium(hInit, reTheta, 0.0);
        return (theta0, hInit, 0.3 * cTauEq);
    }

    [Fact]
    public void FlatPlate_HStaysNearAttachedValue()
    {
        // Turbulent flat-plate shape parameter should sit near H ≈ 1.4
        // (Coles profile, near-equilibrium). Implicit integrator must
        // NOT oscillate — the whole point of Phase-2e.
        const double U0 = 1.0;
        const double nu = 1e-6;
        const int N = 101;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.01 + 0.99 * (double)i / (N - 1);
            ue[i] = U0;
        }

        // Start from a physically reasonable post-transition θ.
        double thetaIC = 0.037 * stations[0] / System.Math.Pow(U0 * stations[0] / nu, 0.2);
        var (theta0, h0, cTau0) = SeedTurbIC(thetaIC);
        var r = ThesisExactTurbulentMarcher.March(stations, ue, nu, theta0, h0, cTau0);

        for (int i = 10; i < N; i++)
        {
            Assert.True(double.IsFinite(r.H[i]), $"NaN H at {i}");
            // Generous band — as Cτ relaxes toward Cτ_eq the H solve
            // can drift a bit; we're pinning no-oscillation, not
            // precise attached value.
            Assert.InRange(r.H[i], 1.1, 2.0);
        }
    }

    [Fact]
    public void FlatPlate_ThetaGrowsMonotonically()
    {
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.01 + 0.99 * i / (double)(N - 1);
            ue[i] = 1.0;
        }
        double thetaIC = 0.037 * stations[0] / System.Math.Pow(stations[0] / 1e-6, 0.2);
        var (t, h, c) = SeedTurbIC(thetaIC);
        var r = ThesisExactTurbulentMarcher.March(stations, ue, 1e-6, t, h, c);
        for (int i = 1; i < N; i++)
        {
            Assert.True(r.Theta[i] >= r.Theta[i - 1] - 1e-12,
                $"θ non-monotonic at {i}: {r.Theta[i]} < {r.Theta[i - 1]}");
        }
    }

    [Fact]
    public void CTauRelaxesTowardEquilibrium()
    {
        // With Cτ0 < Cτ_eq the lag ODE should drive Cτ upward
        // monotonically on a flat plate until it's close to Cτ_eq.
        const int N = 201;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.005 + 0.995 * i / (double)(N - 1);
            ue[i] = 1.0;
        }
        double thetaIC = 0.01;
        double hIC = 1.4;
        // Start an order of magnitude below equilibrium. At H=1.4
        // Cτ_eq ≈ 0.0014, so 1e-5 leaves plenty of growth room.
        double cTau0 = 1e-5;
        var r = ThesisExactTurbulentMarcher.March(stations, ue, 1e-6, thetaIC, hIC, cTau0);

        // Cτ should grow substantially toward equilibrium. Strict
        // monotonicity isn't meaningful because H (hence Cτ_eq) also
        // drifts as the BL relaxes — the target is moving. Pin growth
        // by 10× and no oscillation (no station-to-station dip > 10%).
        bool grew = r.CTau[N - 1] > r.CTau[0] * 10.0;
        Assert.True(grew,
            $"Cτ failed to grow: start={r.CTau[0]}, end={r.CTau[N - 1]}");
        for (int i = 1; i < N; i++)
        {
            double prev = System.Math.Max(r.CTau[i - 1], 1e-12);
            double drop = (prev - r.CTau[i]) / prev;
            Assert.True(drop < 0.10,
                $"Cτ oscillated at {i}: {r.CTau[i - 1]} → {r.CTau[i]}");
        }
    }

    [Fact]
    public void AdverseGradient_RaisesHWithoutBlowup()
    {
        // Gentle adverse gradient (Ue decreases from 1.0 to 0.7 over the
        // plate). H should rise but stay bounded; θ monotonic.
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            double s = i / (double)(N - 1);
            stations[i] = 0.01 + 0.99 * s;
            ue[i] = 1.0 - 0.3 * s;
        }
        double thetaIC = 0.02;
        double hIC = 1.4;
        double cTau0 = 0.01;
        var r = ThesisExactTurbulentMarcher.March(stations, ue, 1e-6, thetaIC, hIC, cTau0);
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(r.H[i]), $"NaN H at {i}");
            Assert.True(r.H[i] < 6.1, $"H blew up at {i}: {r.H[i]}");
        }
        Assert.True(r.H[N - 1] > hIC * 1.05,
            $"Expected H to rise under adverse gradient, got {r.H[N - 1]}");
    }

    [Fact]
    public void NoOscillationInHOverFlatPlate()
    {
        // The Phase-2e raison d'être: explicit RK2 oscillates (Hk swings
        // 2.59 → 2.51 → 2.76 → 2.31 → 2.95 per the MsesClosurePlan).
        // Implicit Newton must NOT oscillate. Pin a tight band.
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.01 + 0.99 * i / (double)(N - 1);
            ue[i] = 1.0;
        }
        double thetaIC = 0.01;
        double hIC = 1.4;
        double cTau0 = 0.005;
        var r = ThesisExactTurbulentMarcher.March(stations, ue, 1e-6, thetaIC, hIC, cTau0);

        // Past the IC region (first 5 stations) H should be smooth.
        // "Smooth" here = consecutive H values within 0.05 of each other.
        for (int i = 6; i < N; i++)
        {
            double d = System.Math.Abs(r.H[i] - r.H[i - 1]);
            Assert.True(d < 0.05,
                $"H oscillated at {i}: {r.H[i - 1]} → {r.H[i]} (Δ={d:F4})");
        }
    }
}

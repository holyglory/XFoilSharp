using XFoil.MsesSolver.BoundaryLayer;
using XFoil.MsesSolver.Closure;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2d tests for the Cτ-lag turbulent marcher. Validates that
/// Cτ relaxes toward Cτ_eq smoothly and the momentum/H evolution
/// remains consistent with the Phase-2c "no-lag" marcher on flat
/// plate (where lag dynamics are quiescent because Cτ_eq changes
/// slowly).
/// </summary>
public class ClosureBasedTurbulentLagMarcherTests
{
    [Fact]
    public void FlatPlate_CTauStaysNearEquilibrium()
    {
        const double U0 = 1.0;
        const double nu = 1e-6;
        const int N = 91;

        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = U0;
        }

        double Re_x0 = U0 * stations[0] / nu;
        double theta0 = 0.036 * stations[0] / System.Math.Pow(Re_x0, 0.2);
        double Reθ0 = U0 * theta0 / nu;
        const double H0 = 1.4;
        double Hk0 = MsesClosureRelations.ComputeHk(H0, 0.0);
        double cTauEq0 = MsesClosureRelations.ComputeCTauEquilibrium(Hk0, Reθ0, 0.0);
        double cTau0 = cTauEq0; // start in equilibrium

        var result = ClosureBasedTurbulentLagMarcher.March(
            stations, ue, nu, theta0, H0, cTau0);

        // Cτ should stay near Cτ_eq throughout (flat plate is slowly
        // varying). Check at a few stations.
        int[] checkIdx = { 10, 30, 60, N - 1 };
        foreach (int i in checkIdx)
        {
            double Reθi = U0 * result.Theta[i] / nu;
            double Hki = MsesClosureRelations.ComputeHk(result.H[i], 0.0);
            double cTauEqi = MsesClosureRelations.ComputeCTauEquilibrium(Hki, Reθi, 0.0);
            double relErr = System.Math.Abs(result.CTau[i] - cTauEqi) / System.Math.Max(cTauEqi, 1e-8);
            // Tolerance 30 % — Cτ_eq itself changes with Reθ on flat
            // plate, and the lag ODE has finite response; this test
            // just pins that tracking is working without blowup.
            Assert.True(relErr < 0.30,
                $"Cτ({i})={result.CTau[i]} vs Cτ_eq={cTauEqi}, relErr={relErr:F3}");
        }
    }

    [Fact]
    public void ThetaAndHConsistentWithNonLagMarcher_OnFlatPlate()
    {
        // With Cτ tracked but starting at equilibrium and Ue
        // constant, the (θ, H) trajectory should match the no-lag
        // marcher (Phase 2c) closely. Any drift exceeding 5%
        // indicates a coupling bug.
        const double U0 = 1.0;
        const double nu = 1e-6;
        const int N = 41;

        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = U0;
        }

        double Re_x0 = U0 * stations[0] / nu;
        double theta0 = 0.036 * stations[0] / System.Math.Pow(Re_x0, 0.2);
        const double H0 = 1.4;
        double Reθ0 = U0 * theta0 / nu;
        double Hk0 = MsesClosureRelations.ComputeHk(H0, 0.0);
        double cTau0 = MsesClosureRelations.ComputeCTauEquilibrium(Hk0, Reθ0, 0.0);

        var lag = ClosureBasedTurbulentLagMarcher.March(stations, ue, nu, theta0, H0, cTau0);
        var noLag = ClosureBasedTurbulentMarcher.March(stations, ue, nu, theta0, H0);

        for (int i = 0; i < N; i++)
        {
            double dθ = System.Math.Abs(lag.Theta[i] - noLag.Theta[i]) / noLag.Theta[i];
            double dH = System.Math.Abs(lag.H[i] - noLag.H[i]) / noLag.H[i];
            Assert.True(dθ < 0.05, $"θ mismatch at {i}: lag={lag.Theta[i]} noLag={noLag.Theta[i]}");
            Assert.True(dH < 0.05, $"H mismatch at {i}: lag={lag.H[i]} noLag={noLag.H[i]}");
        }
    }

    [Fact]
    public void AdverseGradient_CTauRisesTowardSeparationValue()
    {
        // Decelerating Ue drives Hk up → Cτ_eq rises steeply (the
        // (Hk−1)³ driver in the equilibrium formula). The lag Cτ
        // should rise too, approaching but trailing Cτ_eq.
        const int N = 41;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = 1.0 - 0.3 * stations[i]; // 1.0 → 0.7
        }

        const double theta0 = 1e-3;
        const double H0 = 1.4;
        double Hk0 = MsesClosureRelations.ComputeHk(H0, 0.0);
        double cTau0 = MsesClosureRelations.ComputeCTauEquilibrium(Hk0, 1000.0, 0.0);

        var result = ClosureBasedTurbulentLagMarcher.March(
            stations, ue, 1e-6, theta0, H0, cTau0);

        // Cτ should grow monotonically (H rising, Cτ_eq rising →
        // lag Cτ chases it upward).
        Assert.True(result.CTau[N - 1] > result.CTau[0],
            $"Cτ should rise: start={result.CTau[0]} end={result.CTau[N - 1]}");

        // All outputs finite.
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(result.Theta[i]));
            Assert.True(double.IsFinite(result.H[i]));
            Assert.True(double.IsFinite(result.CTau[i]));
        }
    }

    [Fact]
    public void CTauBelowEquilibrium_RecoversToward()
    {
        // Start Cτ at half of Cτ_eq. With flat-plate slow variation,
        // the lag ODE should pull Cτ up toward Cτ_eq over the run.
        const int N = 21;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = 1.0;
        }

        const double theta0 = 1e-3;
        const double H0 = 1.4;
        const double nu = 1e-6;
        double Hk0 = MsesClosureRelations.ComputeHk(H0, 0.0);
        double cTauEq0 = MsesClosureRelations.ComputeCTauEquilibrium(Hk0, 1000.0, 0.0);
        double cTau0 = 0.5 * cTauEq0;

        var result = ClosureBasedTurbulentLagMarcher.March(
            stations, ue, nu, theta0, H0, cTau0);

        // Cτ at final station should be closer to Cτ_eq than the
        // initial state (relaxation toward equilibrium).
        double HkFinal = MsesClosureRelations.ComputeHk(result.H[N - 1], 0.0);
        double ReθFinal = result.Theta[N - 1] / nu;
        double cTauEqFinal = MsesClosureRelations.ComputeCTauEquilibrium(HkFinal, ReθFinal, 0.0);
        double gapStart = cTauEq0 - cTau0;
        double gapFinal = System.Math.Abs(cTauEqFinal - result.CTau[N - 1]);

        Assert.True(gapFinal < gapStart,
            $"Cτ gap should shrink: start={gapStart}, final={gapFinal}");
    }
}

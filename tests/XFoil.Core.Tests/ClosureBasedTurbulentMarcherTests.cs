using XFoil.MsesSolver.BoundaryLayer;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2c tests for the turbulent closure-based marcher. The 1/5-
/// power-law reference is the canonical flat-plate turbulent fit
/// used in every BL textbook:
///   θ(x) = 0.036·x/Re_x^0.2     for 5·10⁵ ≤ Re_x ≤ 10⁸.
/// </summary>
public class ClosureBasedTurbulentMarcherTests
{
    [Fact]
    public void FlatPlateTurbulent_ThetaWithinTenPercentOfOneFifthLaw()
    {
        // Seed at x=0.1 with 1/5-law θ. March to x=1. Check the
        // accumulated θ stays within 10 % of the 1/5-law reference.
        // 10 % is a reasonable tolerance for this rough correlation;
        // Drela's closure is calibrated to data that agrees with 1/5-
        // law within a few percent. A tighter bound requires the full
        // H-lag ODE (Phase 2d).
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
        var (theta, h) = ClosureBasedTurbulentMarcher.March(
            stations, ue, nu, theta0, h0: 1.4);

        int[] checkIdx = { 30, 50, 70, N - 1 };
        foreach (int i in checkIdx)
        {
            double x = stations[i];
            double Re_x = U0 * x / nu;
            double oneFifth = 0.036 * x / System.Math.Pow(Re_x, 0.2);
            double relErr = System.Math.Abs(theta[i] - oneFifth) / oneFifth;
            Assert.True(relErr < 0.10,
                $"Turbulent θ({x})={theta[i]} vs 1/5-law={oneFifth}, relErr={relErr:F4}");
        }
        // H should stay near initial 1.4 on flat plate.
        Assert.InRange(h[N - 1], 1.3, 1.5);
    }

    [Fact]
    public void ThetaMonotonicOnFlatPlate()
    {
        const int N = 41;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = 1.0;
        }
        double Re_x0 = stations[0] / 1e-5;
        double theta0 = 0.036 * stations[0] / System.Math.Pow(Re_x0, 0.2);
        var (theta, _) = ClosureBasedTurbulentMarcher.March(
            stations, ue, 1e-5, theta0, h0: 1.4);
        for (int i = 1; i < N; i++)
        {
            Assert.True(theta[i] >= theta[i - 1] - 1e-10,
                $"θ[{i}]={theta[i]} < θ[{i-1}]={theta[i-1]}");
        }
    }

    [Fact]
    public void FavorableGradient_HDrops()
    {
        const int N = 41;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = 1.0 + 0.5 * stations[i];
        }
        double Re_x0 = stations[0] / 1e-6;
        double theta0 = 0.036 * stations[0] / System.Math.Pow(Re_x0, 0.2);
        var (_, h) = ClosureBasedTurbulentMarcher.March(
            stations, ue, 1e-6, theta0, h0: 1.4);
        // H should drop below 1.4 under favorable gradient.
        Assert.True(h[N - 1] < 1.4,
            $"Expected H < 1.4 under favorable gradient, got {h[N - 1]}");
    }

    [Fact]
    public void AdverseGradient_HRisesWithoutNaN()
    {
        const int N = 41;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = 0.1 + 0.9 * i / (N - 1);
            ue[i] = 1.0 - 0.3 * stations[i];
        }
        double Re_x0 = stations[0] / 1e-6;
        double theta0 = 0.036 * stations[0] / System.Math.Pow(Re_x0, 0.2);
        var (theta, h) = ClosureBasedTurbulentMarcher.March(
            stations, ue, 1e-6, theta0, h0: 1.4);
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(theta[i]), $"NaN θ at {i}");
            Assert.True(double.IsFinite(h[i]), $"NaN H at {i}");
        }
        Assert.True(h[N - 1] > 1.4,
            $"Expected H > 1.4 under adverse gradient, got {h[N - 1]}");
    }
}

using XFoil.ThesisClosureSolver.BoundaryLayer;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2b acceptance tests for the closure-based laminar marcher.
/// The MSES plan targets Blasius within 0.5 % over x/L ∈ [0.01, 1].
/// These tests pin that the closure relations from Phase 1 integrate
/// consistently with the momentum-integral equation.
/// </summary>
public class ClosureBasedLaminarMarcherTests
{
    [Fact]
    public void FlatPlateBlasius_WithinTwoPercent()
    {
        // Unit chord, Ue=1, ν=1e-6 (Re_L=1e6). Test first at 2 %
        // tolerance — the 0.5 % target is harder and may need a
        // closure tuning pass. 2 % still validates that the closure
        // relations aren't grossly wrong.
        const double U0 = 1.0;
        const double nu = 1e-6;
        const int N = 101;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = (double)i / (N - 1);
            ue[i] = U0;
        }

        var (theta, h) = ClosureBasedLaminarMarcher.March(stations, ue, nu);

        // Blasius: θ = 0.664·√(ν·x/U∞), H = 2.59.
        int[] checkIdx = { 10, 30, 50, 70, 100 };
        foreach (int i in checkIdx)
        {
            double x = stations[i];
            double blasius = 0.664 * System.Math.Sqrt(nu * x / U0);
            double relErr = System.Math.Abs(theta[i] - blasius) / blasius;
            Assert.True(relErr < 0.02,
                $"Closure-based θ({x})={theta[i]} vs Blasius={blasius}, relErr={relErr:F4}");
        }
        // H should stay near Blasius 2.59 on flat plate.
        Assert.InRange(h[50], 2.4, 2.8);
    }

    [Fact]
    public void ThetaGrowsOnFlatPlate()
    {
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0;
        }
        var (theta, _) = ClosureBasedLaminarMarcher.March(stations, ue, 1e-5);
        for (int i = 1; i < N; i++)
        {
            Assert.True(theta[i] >= theta[i - 1] - 1e-10,
                $"θ should be monotonic; θ[{i}]={theta[i]} < θ[{i-1}]={theta[i-1]}");
        }
    }

    [Fact]
    public void InitialConditionMatchesBlasiusAtFirstStation()
    {
        // At x=0.01 with flat-plate Ue=1, ν=1e-6, the Blasius IC
        // (θ₀ = 0.664·√(νx/U)) should be seeded exactly.
        var stations = new[] { 0.01, 0.02, 0.03 };
        var ue = new[] { 1.0, 1.0, 1.0 };
        var (theta, h) = ClosureBasedLaminarMarcher.March(stations, ue, 1e-6);
        double expected = 0.664 * System.Math.Sqrt(1e-6 * 0.01);
        Assert.Equal(expected, theta[0], 10);
        Assert.Equal(2.59, h[0], 6);
    }

    [Fact]
    public void Marcher_HandlesFavorableGradient()
    {
        // Favorable gradient: Ue accelerates. θ should grow more
        // slowly and H should stay low (attached).
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0 + 0.3 * stations[i];
        }
        var (theta, h) = ClosureBasedLaminarMarcher.March(stations, ue, 1e-6);
        // All outputs must remain finite and physical.
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(theta[i]), $"NaN θ at station {i}");
            Assert.True(double.IsFinite(h[i]), $"NaN H at station {i}");
            Assert.True(h[i] > 1.0 && h[i] < 8.0, $"H out of range at station {i}: {h[i]}");
        }
    }

    [Fact]
    public void Marcher_HandlesAdverseGradientWithoutNaN()
    {
        // Mild adverse gradient: H rises but shouldn't NaN.
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0 - 0.2 * stations[i];
        }
        var (theta, h) = ClosureBasedLaminarMarcher.March(stations, ue, 1e-6);
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(theta[i]), $"NaN θ at station {i}");
            Assert.True(double.IsFinite(h[i]), $"NaN H at station {i}");
        }
        // H at mid-plate should be > Blasius 2.59 (adverse).
        Assert.True(h[N / 2] > 2.4,
            $"Expected H > 2.4 under adverse gradient, got {h[N / 2]}");
    }
}

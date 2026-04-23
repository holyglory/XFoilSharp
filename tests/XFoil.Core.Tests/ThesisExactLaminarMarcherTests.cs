using XFoil.ThesisClosureSolver.BoundaryLayer;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-2e tests: implicit-Newton thesis-exact laminar marcher.
/// </summary>
public class ThesisExactLaminarMarcherTests
{
    [Fact]
    public void FlatPlateBlasius_WithinFivePercent()
    {
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
        var result = ThesisExactLaminarMarcher.March(stations, ue, nu);

        int[] checkIdx = { 20, 50, 80, 100 };
        foreach (int i in checkIdx)
        {
            double x = stations[i];
            double blasius = 0.664 * System.Math.Sqrt(nu * x / U0);
            double relErr = System.Math.Abs(result.Theta[i] - blasius) / blasius;
            Assert.True(relErr < 0.05,
                $"θ({x})={result.Theta[i]} vs Blasius={blasius}, relErr={relErr:F4}");
        }
        Assert.InRange(result.H[50], 2.40, 2.80);
    }

    [Fact]
    public void ThetaGrowsMonotonicallyOnFlatPlate()
    {
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0;
        }
        var result = ThesisExactLaminarMarcher.March(stations, ue, 1e-5);
        for (int i = 1; i < N; i++)
        {
            Assert.True(result.Theta[i] >= result.Theta[i - 1] - 1e-12,
                $"θ non-monotonic at {i}");
        }
    }

    [Fact]
    public void FavorableGradient_LowersH()
    {
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0 + 0.3 * stations[i];
        }
        var result = ThesisExactLaminarMarcher.March(stations, ue, 1e-6);
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(result.H[i]));
        }
        Assert.True(result.H[N - 1] < 2.59,
            $"Expected H<2.59 at TE under favorable gradient, got {result.H[N - 1]}");
    }

    [Fact]
    public void AdverseGradient_RaisesHWithoutBlowup()
    {
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0 - 0.2 * stations[i];
        }
        var result = ThesisExactLaminarMarcher.March(stations, ue, 1e-6);
        for (int i = 0; i < N; i++)
        {
            Assert.True(double.IsFinite(result.H[i]), $"NaN H at {i}");
            Assert.True(result.H[i] < 7.5, $"H blew up at {i}: {result.H[i]}");
        }
        Assert.True(result.H[N - 1] > 2.59,
            $"Expected H>2.59 at TE under adverse gradient, got {result.H[N - 1]}");
    }

    [Fact]
    public void NoOscillationInHOverFlatPlate()
    {
        // Flat plate should produce H[i] within ±0.1 of Blasius 2.59
        // at every station past the IC. Implicit integrator must not
        // oscillate.
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = 1.0;
        }
        var result = ThesisExactLaminarMarcher.March(stations, ue, 1e-6);
        for (int i = 2; i < N; i++)
        {
            Assert.InRange(result.H[i], 2.49, 2.70);
        }
    }
}

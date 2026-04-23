using XFoil.ThesisClosureSolver.BoundaryLayer;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-3 tests for the laminar transition marcher. Pins that:
/// - A low-Re flat-plate run stays laminar (Ñ never reaches 9).
/// - A high-Re flat-plate run transitions somewhere downstream.
/// - Ñ grows monotonically downstream once past the neutral Reθ.
/// </summary>
public class LaminarTransitionMarcherTests
{
    [Fact]
    public void LowReFlatPlate_StaysLaminar_NoTransitionReported()
    {
        // Short plate, low freestream → low Re_x. Ñ stays small; the
        // marcher must report transitionIdx = -1.
        const double U0 = 1.0;
        const double nu = 1e-4;  // kinematic ν — large → low Re.
        const int N = 51;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = U0;
        }

        var result = LaminarTransitionMarcher.March(stations, ue, nu);

        Assert.Equal(-1, result.TransitionIndex);
        // Final Ñ should be < 9 (no transition).
        Assert.True(result.N[N - 1] < 9.0,
            $"Expected laminar Ñ < 9, got {result.N[N - 1]}");
    }

    [Fact]
    public void HighReFlatPlate_Transitions_SomewhereDownstream()
    {
        // Long plate at ν = 1e-7 → Re_L = 1e7. Plenty of room for TS
        // amplification to reach 9 before end.
        const double U0 = 1.0;
        const double nu = 1e-7;
        const int N = 201;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = U0;
        }

        var result = LaminarTransitionMarcher.March(stations, ue, nu);

        Assert.True(result.TransitionIndex >= 0,
            $"Expected transition at high-Re flat plate, got idx={result.TransitionIndex}");
        Assert.True(result.TransitionX > 0.0 && result.TransitionX < 1.0,
            $"TransitionX={result.TransitionX} out of plate");
        // At transition station Ñ should be near (but ≥) n_crit.
        Assert.True(result.N[result.TransitionIndex] >= 9.0,
            $"Expected Ñ ≥ 9 at transition, got {result.N[result.TransitionIndex]}");
    }

    [Fact]
    public void AmplificationFactor_GrowsMonotonically()
    {
        const double U0 = 1.0;
        const double nu = 1e-6;
        const int N = 101;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = U0;
        }

        var result = LaminarTransitionMarcher.March(stations, ue, nu);
        for (int i = 1; i < N; i++)
        {
            Assert.True(result.N[i] >= result.N[i - 1] - 1e-12,
                $"Ñ not monotonic at {i}: {result.N[i]} < {result.N[i-1]}");
        }
    }

    [Fact]
    public void CriticalNFactor_ControlsTransitionPosition()
    {
        // Running at n_crit = 11 (quiet tunnel) should move transition
        // downstream relative to n_crit = 9 (standard).
        const double U0 = 1.0;
        const double nu = 1e-7;
        const int N = 201;
        var stations = new double[N];
        var ue = new double[N];
        for (int i = 0; i < N; i++)
        {
            stations[i] = i / (double)(N - 1);
            ue[i] = U0;
        }

        var std = LaminarTransitionMarcher.March(stations, ue, nu, nCrit: 9.0);
        var quiet = LaminarTransitionMarcher.March(stations, ue, nu, nCrit: 11.0);

        if (std.TransitionIndex >= 0 && quiet.TransitionIndex >= 0)
        {
            Assert.True(quiet.TransitionX > std.TransitionX,
                $"Quiet tunnel should delay transition: std={std.TransitionX}, quiet={quiet.TransitionX}");
        }
    }
}

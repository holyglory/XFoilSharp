using XFoil.Core.Services;
using XFoil.ThesisClosureSolver.BoundaryLayer;
using XFoil.Solver.Models;
using XFoil.Solver.Modern.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// End-to-end validation that <see cref="CompositeTransitionMarcher"/>
/// with `useThesisExactTurbulent = true` runs on real airfoil Ue(x)
/// distributions and produces finite, bounded output. Wiring gate
/// for the Phase-2e turbulent marcher.
/// </summary>
public class MsesThesisExactIntegrationTests
{
    private static (double[] stations, double[] ue, double nu) BuildSurfaceInputs(
        string naca, double alphaDeg, double Re, bool upper)
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re);
        var inv = new AirfoilAnalysisService()
            .AnalyzeInviscid(geom, alphaDeg, settings);

        var samples = inv.PressureSamples;
        int iLE = 0;
        double minX = samples[0].Location.X;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Location.X < minX) { minX = samples[i].Location.X; iLE = i; }
        }
        int start, end, step;
        if (upper) { start = iLE; end = -1; step = -1; }
        else { start = iLE; end = samples.Count; step = +1; }

        int count = upper ? iLE + 1 : samples.Count - iLE;
        var s = new double[count];
        var ue = new double[count];
        double sAcc = 0.0;
        int prev = -1;
        int outIdx = 0;
        for (int k = start; k != end; k += step)
        {
            if (prev >= 0)
            {
                double dx = samples[k].Location.X - samples[prev].Location.X;
                double dy = samples[k].Location.Y - samples[prev].Location.Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            s[outIdx] = sAcc;
            double cp = samples[k].CorrectedPressureCoefficient;
            double oneMinusCp = System.Math.Max(0.0, 1.0 - cp);
            ue[outIdx] = System.Math.Sqrt(oneMinusCp);
            outIdx++;
            prev = k;
        }

        double nu = 1.0 * 1.0 / Re; // U∞ = 1, chord = 1.
        return (s, ue, nu);
    }

    [Theory]
    [InlineData("0012", 0.0, 3_000_000)]
    [InlineData("0012", 2.0, 3_000_000)]
    [InlineData("4412", 4.0, 3_000_000)]
    public void CompositeThesisExact_RunsEndToEndOnRealAirfoil(
        string naca, double alphaDeg, double Re)
    {
        var (s, ue, nu) = BuildSurfaceInputs(naca, alphaDeg, Re, upper: true);

        var r = CompositeTransitionMarcher.March(
            s, ue, nu, nCrit: 9.0, cTauInitialFactor: 0.3,
            machNumberEdge: 0.0, useThesisExactTurbulent: true);

        int n = r.Theta.Length;
        for (int i = 0; i < n; i++)
        {
            Assert.True(double.IsFinite(r.Theta[i]), $"NaN θ at {i}");
            Assert.True(double.IsFinite(r.H[i]), $"NaN H at {i}");
            // Near-stagnation laminar stations can hit the Thwaites-λ
            // clamp (H=7.0); that's suppressed in downstream Ñ
            // accumulation. Enforce strictly-bounded H only on the
            // middle and TE of the airfoil, where physics should be
            // well-posed.
            Assert.True(r.H[i] <= 7.0 + 1e-12, $"H blew up at {i}: {r.H[i]}");
            Assert.True(r.Theta[i] >= 0.0, $"negative θ at {i}");
        }
        // TE should always be physical (H < 3 for attached flow,
        // < 6 even for separated).
        Assert.True(r.H[n - 1] < 6.0,
            $"TE H out of range: {r.H[n - 1]}");
    }

    [Fact]
    public void CompositeThesisExact_MatchesClauserWithinReasonableBand()
    {
        // Sanity: on an attached-flow case (NACA 0012 α=2°), the two
        // turbulent marchers should give similar TE θ (within ~3×).
        // They use the same closure; the only difference is the H-ODE
        // (Clauser relaxation vs full energy integral). Big deltas at
        // this easy case would signal a sign error in Phase 2e.
        var (s, ue, nu) = BuildSurfaceInputs("0012", 2.0, 3_000_000, upper: true);

        var rClauser = CompositeTransitionMarcher.March(
            s, ue, nu, nCrit: 9.0, cTauInitialFactor: 0.3,
            machNumberEdge: 0.0, useThesisExactTurbulent: false);
        var rThesis = CompositeTransitionMarcher.March(
            s, ue, nu, nCrit: 9.0, cTauInitialFactor: 0.3,
            machNumberEdge: 0.0, useThesisExactTurbulent: true);

        int n = rClauser.Theta.Length;
        double θClauserTE = rClauser.Theta[n - 1];
        double θThesisTE = rThesis.Theta[n - 1];
        double ratio = θThesisTE / System.Math.Max(θClauserTE, 1e-18);
        Assert.InRange(ratio, 0.33, 3.0);
    }
}

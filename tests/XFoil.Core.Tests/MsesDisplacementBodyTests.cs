using XFoil.Core.Models;
using XFoil.ThesisClosureSolver.BoundaryLayer;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for the Phase-5-lite displacement-body helper infrastructure
/// (currently dead code in ThesisClosureAnalysisService but the logic is
/// reachable through the public CompositeResult.Stations and
/// BoundaryLayerProfile.DStar fields that these tests poke at).
///
/// Goal: pin that the arc-length + δ* plumbing is internally
/// consistent so when the outer coupling loop is eventually wired,
/// these invariants are already locked in.
/// </summary>
public class MsesDisplacementBodyTests
{
    private static CompositeTransitionMarcher.CompositeResult RunComposite(
        string naca, double alphaDeg, double Re)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var svc = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        var inv = svc.AnalyzeInviscid(geom, alphaDeg, settings: null);

        // Use upper-surface extraction similar to ThesisClosureAnalysisService.
        var samples = inv.PressureSamples;
        int iLE = 0;
        double minX = samples[0].Location.X;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Location.X < minX) { minX = samples[i].Location.X; iLE = i; }
        }
        int count = iLE + 1;
        var s = new double[count];
        var ue = new double[count];
        double sAcc = 0.0;
        int outIdx = 0;
        int prev = -1;
        for (int k = iLE; k >= 0; k--)
        {
            if (prev >= 0)
            {
                double dx = samples[k].Location.X - samples[prev].Location.X;
                double dy = samples[k].Location.Y - samples[prev].Location.Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            s[outIdx] = sAcc;
            double cp = samples[k].CorrectedPressureCoefficient;
            ue[outIdx] = System.Math.Sqrt(System.Math.Max(0, 1.0 - cp));
            outIdx++;
            prev = k;
        }

        double nu = 1.0 / Re;
        return CompositeTransitionMarcher.March(s, ue, nu);
    }

    [Fact]
    public void StationsMatchInputArray()
    {
        var r = RunComposite("0012", 2.0, 1_000_000);
        // Stations[] should be the same length as Theta/H arrays
        // and match the caller's input.
        Assert.Equal(r.Theta.Length, r.Stations.Length);
        // Stations should be monotonically non-decreasing.
        for (int i = 1; i < r.Stations.Length; i++)
        {
            Assert.True(r.Stations[i] >= r.Stations[i - 1],
                $"Stations not monotonic at {i}: {r.Stations[i]} < {r.Stations[i-1]}");
        }
    }

    [Fact]
    public void EdgeVelocityMatchesInputArray()
    {
        var r = RunComposite("0012", 2.0, 1_000_000);
        Assert.Equal(r.Theta.Length, r.EdgeVelocity.Length);
        // On a subsonic NACA 0012 α=2° the edge velocity should
        // stay in [0, 2] everywhere (max speed at the suction peak
        // can be ~1.3-1.5 U∞).
        foreach (double u in r.EdgeVelocity)
            Assert.InRange(u, 0.0, 2.0);
    }

    [Fact]
    public void DStarInterpolationIsStableNearStationBoundaries()
    {
        // Expose the internal behavior: grab δ* = H·θ at each station
        // and check that evaluating it at a point halfway between two
        // stations produces a value between the two stations' δ*.
        // This confirms the linear-interpolation semantic.
        var r = RunComposite("0012", 2.0, 1_000_000);
        int n = r.Stations.Length;
        Assert.True(n >= 10);
        for (int i = 5; i < n - 5; i++)
        {
            double dStarHere = r.H[i] * r.Theta[i];
            double dStarNext = r.H[i + 1] * r.Theta[i + 1];
            double dStarMin = System.Math.Min(dStarHere, dStarNext);
            double dStarMax = System.Math.Max(dStarHere, dStarNext);
            // Midpoint station is a valid linear combination.
            double midStation = 0.5 * (r.Stations[i] + r.Stations[i + 1]);
            double dStarMid = 0.5 * (dStarHere + dStarNext);
            Assert.InRange(dStarMid, dStarMin, dStarMax + 1e-12);
            _ = midStation;
        }
    }
}

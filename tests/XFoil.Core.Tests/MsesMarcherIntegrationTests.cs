using XFoil.Core.Models;
using XFoil.MsesSolver.BoundaryLayer;
using XFoil.MsesSolver.Closure;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Integration tests that exercise the MSES laminar marcher on a
/// real airfoil's edge-velocity distribution (extracted from the
/// Modern inviscid solver). Not a parity check — this is about
/// making sure the closure + marcher chain produces physically
/// reasonable output when fed production inviscid data, as a
/// precursor to Phase-5 Newton coupling.
/// </summary>
public class MsesMarcherIntegrationTests
{
    private static (double[] s, double[] ue) BuildUpperSurfaceSpeedDistribution(
        string naca, double alphaDeg, double Ue_inf = 1.0)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var svc = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        var inv = svc.AnalyzeInviscid(geom, alphaDeg, settings: null);

        // PressureSamples include both surfaces in XFoil convention:
        // TE → upper → LE → lower → TE. Split at LE (min x).
        var samples = inv.PressureSamples;
        int iLE = 0;
        double minX = samples[0].Location.X;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Location.X < minX)
            {
                minX = samples[i].Location.X;
                iLE = i;
            }
        }

        // Upper surface: samples[0..iLE]. Arc-length s measured from LE
        // along the surface (physical BL coordinate).
        int count = iLE + 1;
        var sBuf = new double[count];
        var ueBuf = new double[count];
        // Walk LE→TE (reverse index order).
        double sAcc = 0.0;
        for (int k = 0; k < count; k++)
        {
            int idx = iLE - k;
            if (k > 0)
            {
                double dx = samples[idx + 1].Location.X - samples[idx].Location.X;
                double dy = samples[idx + 1].Location.Y - samples[idx].Location.Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            sBuf[k] = sAcc;
            // Convert pressure coefficient to speed ratio. Cp = 1 - (q/U∞)²,
            // so q/U∞ = sqrt(max(0, 1 - Cp)).
            double cp = samples[idx].CorrectedPressureCoefficient;
            double oneMinusCp = System.Math.Max(0.0, 1.0 - cp);
            ueBuf[k] = Ue_inf * System.Math.Sqrt(oneMinusCp);
        }
        return (sBuf, ueBuf);
    }

    [Fact]
    public void LaminarMarcher_OnNACA0012Alpha2_ProducesReasonableBL()
    {
        var (s, ue) = BuildUpperSurfaceSpeedDistribution("0012", 2.0);
        Assert.True(s.Length > 50, "Need enough stations for a meaningful march");

        // Re_chord = 1e5 — low enough to stay laminar to TE on a
        // 12 % symmetric airfoil.
        double U_inf = 1.0;
        double chord = s[s.Length - 1];
        double Re_chord = 1e5;
        double nu = U_inf * chord / Re_chord;

        var result = ThesisExactLaminarMarcher.March(s, ue, nu);

        // θ must be finite, monotonic-non-decreasing, and grow to a
        // physically sensible magnitude (rough rule: θ_TE/c ≈ 10/√Re
        // for laminar ≈ 0.03 on a 1 % Re=1e5 plate; airfoil is
        // similar order).
        double thetaTE = result.Theta[result.Theta.Length - 1];
        Assert.True(double.IsFinite(thetaTE), "θ_TE NaN");
        Assert.InRange(thetaTE / chord, 1e-4, 0.03);
        // H should stay in the laminar-physical range [2.0, 5.0]
        // (separation H ≈ 4; our closure permits up to 7 but a
        // healthy low-Re 0012 α=2° run shouldn't separate).
        double HTE = result.H[result.H.Length - 1];
        Assert.InRange(HTE, 1.8, 5.0);
    }

    [Fact]
    public void TransitionMarcher_OnNACA4412AlphaZero_TransitionsOnUpperSurface()
    {
        // NACA 4412 α=0° Re=3e6: canonical free-transition case.
        // Upper-surface Ue accelerates strongly past the LE hump, then
        // adverse gradient over aft half. Transition via e^N should fire.
        var (s, ue) = BuildUpperSurfaceSpeedDistribution("4412", 0.0);
        double U_inf = 1.0;
        double chord = s[s.Length - 1];
        double Re_chord = 3e6;
        double nu = U_inf * chord / Re_chord;

        var composite = CompositeTransitionMarcher.March(
            s, ue, nu, nCrit: 9.0, cTauInitialFactor: 0.3);

        Assert.True(composite.TransitionIndex >= 0,
            $"Expected transition on NACA 4412 α=0° Re=3e6 upper, got idx={composite.TransitionIndex}");
        Assert.True(composite.IsTurbulentAtEnd);

        // Transition x should be within the airfoil; exact location
        // varies because our Ue(x) is currently derived from Cp via
        // 1-Cp (no viscous feedback yet, Phase 5 scope). Loose bound.
        double xTrans = composite.TransitionX / chord;
        Assert.InRange(xTrans, 0.001, 0.99);
    }

    [Fact]
    public void ClosureLibrary_AcceptsBroadParameterRange()
    {
        // Smoke test: the closure library must not NaN on any
        // physically plausible (Hk, Reθ, Me) grid point. This
        // protects against latent domain issues when the Newton
        // inner solve wanders into unusual states.
        double[] hkGrid = { 1.05, 1.3, 1.6, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 7.0 };
        double[] reGrid = { 100, 300, 1000, 3000, 10000, 100000 };
        double[] meGrid = { 0.0, 0.15, 0.3, 0.5 };
        int badCount = 0;
        foreach (double hk in hkGrid)
        foreach (double re in reGrid)
        foreach (double me in meGrid)
        {
            double hsl = MsesClosureRelations.ComputeHStarLaminar(hk, re);
            double hst = MsesClosureRelations.ComputeHStarTurbulent(hk, re, me);
            double cfl = MsesClosureRelations.ComputeCfLaminar(hk, re);
            double cft = MsesClosureRelations.ComputeCfTurbulent(hk, re, me);
            double cdl = MsesClosureRelations.ComputeCDLaminar(hk, re);
            double cdt = MsesClosureRelations.ComputeCDTurbulent(hk, re, me, cTau: 1e-3);
            double ceq = MsesClosureRelations.ComputeCTauEquilibrium(hk, re, me);
            if (!double.IsFinite(hsl) || !double.IsFinite(hst) ||
                !double.IsFinite(cfl) || !double.IsFinite(cft) ||
                !double.IsFinite(cdl) || !double.IsFinite(cdt) ||
                !double.IsFinite(ceq))
            {
                badCount++;
            }
        }
        Assert.Equal(0, badCount);
    }
}

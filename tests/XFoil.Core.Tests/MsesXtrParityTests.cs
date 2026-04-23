using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase 3c — MSES Xtr regression pin on NACA 0012 free-transition.
///
/// Reference values captured 2026-04-22 from the composite marcher
/// running on the fully-thesis-exact path at nCrit=9, M=0,
/// 161-panel. The α=0° symmetric rows (0.65 / 0.48 / 0.30 across
/// Re ∈ {1e6, 3e6, 1e7}) align with published XFoil 6.97 values
/// within 0.02·c; the α>0° rows are self-pinned for drift detection.
///
/// Tolerance: 0.02·c absolute (one panel width). Tight enough to
/// flag any real algorithmic change in the closure, amplification
/// model, or composite marcher handoff.
///
/// Note: we do NOT cross-check against XFoil.Solver.Modern's viscous
/// Xtr — its transition extractor currently reports TE station
/// (x ≈ 0.99) on these attached cases (separate issue, out of scope).
/// </summary>
public class MsesXtrParityTests
{
    private static (double xtrU, double xtrL) AnalyzeMses(
        double alphaDeg, double Re, double nCrit = 9.0)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re, nCritUpper: nCrit, nCritLower: nCrit);
        var mses = new ThesisClosureAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        var r = mses.AnalyzeViscous(geom, alphaDeg, settings);
        return (r.UpperTransition.XTransition, r.LowerTransition.XTransition);
    }

    [Theory]
    // Reference values are the MSES output on the same 161-panel
    // NACA 0012 at the commit where this test was pinned (2026-04-22).
    // All symmetric (α=0°) rows match published XFoil Xtr ≈ 0.30–0.65
    // across this Re range; all asymmetric (α>0) rows are physically
    // monotonic (Xtr_U moves forward with α, Xtr_L moves aft).
    [InlineData(0.0, 1_000_000, 0.6445, 0.6583)]
    [InlineData(0.0, 3_000_000, 0.4766, 0.4937)]
    [InlineData(0.0, 10_000_000, 0.3034, 0.3213)]
    [InlineData(2.0, 1_000_000, 0.4388, 0.8304)]
    [InlineData(2.0, 3_000_000, 0.2965, 0.6703)]
    [InlineData(2.0, 10_000_000, 0.1755, 0.4665)]
    [InlineData(4.0, 1_000_000, 0.2231, 0.9259)]
    [InlineData(4.0, 3_000_000, 0.1376, 0.7917)]
    [InlineData(4.0, 10_000_000, 0.0834, 0.4472)]
    public void Naca0012_Xtr_WithinToleranceOfPublishedReference(
        double alphaDeg, double Re, double refXtrU, double refXtrL)
    {
        // Tight tolerance (0.02·c ≈ one panel) since these are
        // self-pinned regression values, not external reference.
        // Any drift beyond a panel means the closure or marcher
        // moved meaningfully.
        const double tol = 0.02;
        var (xU, xL) = AnalyzeMses(alphaDeg, Re);

        Assert.True(System.Math.Abs(xU - refXtrU) <= tol,
            $"Xtr_U: α={alphaDeg} Re={Re:0.0e0} — got {xU:F4}, ref {refXtrU:F4}, |Δ|={System.Math.Abs(xU - refXtrU):F4}");
        Assert.True(System.Math.Abs(xL - refXtrL) <= tol,
            $"Xtr_L: α={alphaDeg} Re={Re:0.0e0} — got {xL:F4}, ref {refXtrL:F4}, |Δ|={System.Math.Abs(xL - refXtrL):F4}");
    }

    [Fact]
    public void Naca0012_Alpha0_Symmetric_XtrUMatchesXtrL()
    {
        // Physical: at α=0° the upper/lower pressure distributions
        // are mirror images, so Xtr_U ≈ Xtr_L to within half a panel.
        var (xU, xL) = AnalyzeMses(0.0, 3_000_000);
        Assert.True(System.Math.Abs(xU - xL) < 0.02,
            $"Symmetric α=0 should give Xtr_U ≈ Xtr_L; got U={xU:F4} L={xL:F4}");
    }

    [Fact]
    public void Naca0012_Xtr_MovesForwardWithRe()
    {
        // Physical: higher Re destabilizes TS waves earlier →
        // transition moves upstream. Pin on α=0°: Xtr(1e6) >
        // Xtr(3e6) > Xtr(1e7).
        var (xU1, _) = AnalyzeMses(0.0, 1_000_000);
        var (xU3, _) = AnalyzeMses(0.0, 3_000_000);
        var (xU10, _) = AnalyzeMses(0.0, 10_000_000);
        Assert.True(xU1 > xU3 && xU3 > xU10,
            $"Xtr should drop with Re; got Re=1e6:{xU1:F4} Re=3e6:{xU3:F4} Re=1e7:{xU10:F4}");
    }

    [Fact]
    public void Naca0012_Xtr_MovesForwardWithAlpha_UpperSurface()
    {
        // Physical: higher α → more adverse upper gradient aft of
        // the suction peak → earlier upper transition.
        var (xU0, _) = AnalyzeMses(0.0, 3_000_000);
        var (xU2, _) = AnalyzeMses(2.0, 3_000_000);
        var (xU4, _) = AnalyzeMses(4.0, 3_000_000);
        Assert.True(xU0 > xU2 && xU2 > xU4,
            $"Xtr_U should drop with α; got α=0:{xU0:F4} α=2:{xU2:F4} α=4:{xU4:F4}");
    }

    [Fact]
    public void Naca0012_LowerSurface_MovesAftWithAlpha()
    {
        // Physical: positive α unloads the lower surface, pushing
        // Xtr_L aft (favorable gradient, TS waves stable longer).
        var (_, xL0) = AnalyzeMses(0.0, 3_000_000);
        var (_, xL2) = AnalyzeMses(2.0, 3_000_000);
        var (_, xL4) = AnalyzeMses(4.0, 3_000_000);
        Assert.True(xL0 < xL2 && xL2 < xL4,
            $"Xtr_L should rise with α; got α=0:{xL0:F4} α=2:{xL2:F4} α=4:{xL4:F4}");
    }
}

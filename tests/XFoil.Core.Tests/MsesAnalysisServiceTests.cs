using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-0/5-stub tests for the MSES analyzer wrapper. Verifies
/// the assembly wiring (IAirfoilAnalysisService ← MsesSolver ←
/// XFoil.Solver) and the inviscid delegation works end-to-end.
/// Viscous throws NotImplementedException by design until Phase 5.
/// </summary>
public class ThesisClosureAnalysisServiceTests
{
    [Fact]
    public void InviscidDelegatesToInnerProvider()
    {
        IAirfoilAnalysisService svc = new ThesisClosureAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var inv = svc.AnalyzeInviscid(naca, angleOfAttackDegrees: 4.0, settings: null);
        Assert.True(double.IsFinite(inv.LiftCoefficient));
        // NACA 0012 α=4° inviscid CL ≈ 0.43.
        Assert.InRange(inv.LiftCoefficient, 0.35, 0.55);
    }

    [Fact]
    public void Viscous_ProducesFiniteCdAndCl()
    {
        // First-iteration (uncoupled) viscous: CL from inviscid,
        // CD from Squire-Young far-field formula over the composite
        // laminar→turbulent march. Phase 5 will add Newton coupling
        // that shifts CL too.
        IAirfoilAnalysisService svc = new ThesisClosureAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var r = svc.AnalyzeViscous(naca, angleOfAttackDegrees: 2.0, settings: null);
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        // CD should be small but positive (NACA 0012 α=2° Re=1e6 WT
        // ≈ 0.006). Our Squire-Young stub is approximate so accept
        // any reasonable magnitude.
        Assert.InRange(r.DragDecomposition.CD, 1e-5, 0.1);
    }

    [Fact]
    public void ConstructorAcceptsAlternativeInviscidProvider()
    {
        // Inject the Doubled tree explicitly; verify the wrapper
        // forwards to it without pinning a particular CL value
        // (Doubled vs Modern may differ by panel-bias effects).
        IAirfoilAnalysisService inner = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        IAirfoilAnalysisService svc = new ThesisClosureAnalysisService(inner);
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var inv = svc.AnalyzeInviscid(naca, angleOfAttackDegrees: 4.0, settings: null);
        Assert.True(double.IsFinite(inv.LiftCoefficient));
        Assert.InRange(inv.LiftCoefficient, 0.35, 0.55);
    }
}

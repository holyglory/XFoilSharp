using XFoil.MsesSolver.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-0/5-stub tests for the MSES analyzer wrapper. Verifies
/// the assembly wiring (IAirfoilAnalysisService ← MsesSolver ←
/// XFoil.Solver) and the inviscid delegation works end-to-end.
/// Viscous throws NotImplementedException by design until Phase 5.
/// </summary>
public class MsesAnalysisServiceTests
{
    [Fact]
    public void InviscidDelegatesToInnerProvider()
    {
        IAirfoilAnalysisService svc = new MsesAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var inv = svc.AnalyzeInviscid(naca, angleOfAttackDegrees: 4.0, settings: null);
        Assert.True(double.IsFinite(inv.LiftCoefficient));
        // NACA 0012 α=4° inviscid CL ≈ 0.43.
        Assert.InRange(inv.LiftCoefficient, 0.35, 0.55);
    }

    [Fact]
    public void ViscousStub_ThrowsNotImplemented()
    {
        IAirfoilAnalysisService svc = new MsesAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        Assert.Throws<System.NotImplementedException>(() =>
            svc.AnalyzeViscous(naca, angleOfAttackDegrees: 0.0, settings: null));
    }

    [Fact]
    public void ConstructorAcceptsAlternativeInviscidProvider()
    {
        // Inject the Doubled tree explicitly; verify the wrapper
        // forwards to it without pinning a particular CL value
        // (Doubled vs Modern may differ by panel-bias effects).
        IAirfoilAnalysisService inner = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        IAirfoilAnalysisService svc = new MsesAnalysisService(inner);
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var inv = svc.AnalyzeInviscid(naca, angleOfAttackDegrees: 4.0, settings: null);
        Assert.True(double.IsFinite(inv.LiftCoefficient));
        Assert.InRange(inv.LiftCoefficient, 0.35, 0.55);
    }
}

using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-0 scaffolding tests: confirm the IAirfoilAnalysisService
/// interface exists and all three analysis trees (Float/Doubled/
/// Modern) implement it. Used by callers that want to swap the
/// active solver at runtime (e.g. for MSES alternative).
/// </summary>
public class IAirfoilAnalysisServiceTests
{
    [Fact]
    public void BaseFloatTree_ImplementsInterface()
    {
        IAirfoilAnalysisService svc = new AirfoilAnalysisService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void DoubledTree_ImplementsInterface()
    {
        IAirfoilAnalysisService svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void ModernTree_ImplementsInterface()
    {
        IAirfoilAnalysisService svc = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void InterfaceMethods_RoundTripOnFloatTree()
    {
        // Sanity: exercise the interface-typed entry points end to
        // end to ensure virtual dispatch works (catches regressions
        // if a derived tree accidentally hides a method).
        IAirfoilAnalysisService svc = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var naca = gen.Generate4DigitClassic("0012", pointCount: 161);
        var inv = svc.AnalyzeInviscid(naca, angleOfAttackDegrees: 2.0, settings: null);
        Assert.True(double.IsFinite(inv.LiftCoefficient));
        Assert.InRange(inv.LiftCoefficient, 0.05, 0.5);
    }
}

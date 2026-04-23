using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// F2.2 smoke test: verifies the source-distribution coupling loop
/// runs end-to-end and produces finite, physically-plausible
/// results. Sign validation vs the uncoupled baseline is in F2.3
/// (MsesSourceCouplingSignTests).
/// </summary>
public class SourceCouplingSmokeTests
{
    [Fact]
    public void Naca4412_Alpha4_SourceCoupling_RunsAndStaysFinite()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000, nCritUpper: 9.0, nCritLower: 9.0);
        var svc = new ThesisClosureAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true,
            useSourceDistributionCoupling: true,
            sourceCouplingIterations: 10,
            sourceCouplingRelaxation: 0.4);
        var r = svc.AnalyzeViscous(geom, 4.0, settings);
        Assert.True(r.Converged, "Source-coupled run should converge on attached case");
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.LiftCoefficient, 0.3, 1.5);
        Assert.InRange(r.DragDecomposition.CD, 0.001, 0.05);
    }

    [Fact]
    public void Naca0012_Alpha0_SourceCoupling_RunsAndStaysFinite()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000, nCritUpper: 9.0, nCritLower: 9.0);
        var svc = new ThesisClosureAnalysisService(
            useSourceDistributionCoupling: true,
            sourceCouplingIterations: 10);
        var r = svc.AnalyzeViscous(geom, 0.0, settings);
        Assert.True(r.Converged);
        // Symmetric airfoil at α=0 should give CL ≈ 0 regardless of coupling.
        Assert.InRange(r.LiftCoefficient, -0.05, 0.05);
    }
}

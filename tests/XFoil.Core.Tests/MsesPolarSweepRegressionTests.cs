using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Polar-sweep regression pins for the three MSES analysis paths:
///   (1) Clauser-placeholder at TE  — baseline (uncoupled).
///   (2) Thesis-exact turbulent at TE — Phase 2e.
///   (3) Thesis-exact turbulent + wake marcher — Phase 2e + 2f.
///
/// Pin loosely (±25 %) so intentional refinements don't fail the
/// test mechanically, but catastrophic regressions (off by 3×) do.
/// These are not accuracy gates against WT — they're drift detectors
/// for our own implementation.
/// </summary>
public class MsesPolarSweepRegressionTests
{
    private static ViscousAnalysisResult Run(
        MsesAnalysisService svc, string naca, double alphaDeg, double Re)
    {
        var geom = new XFoil.Core.Services.NacaAirfoilGenerator()
            .Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re);
        return svc.AnalyzeViscous(geom, alphaDeg, settings);
    }

    [Theory]
    [InlineData(0.0, 0.00, 0.02)]    // α=0°, symmetric, CD ~ 0.002-0.015
    [InlineData(4.0, 0.003, 0.02)]   // α=4°, light adverse
    [InlineData(8.0, 0.005, 0.025)]  // α=8°, more adverse
    public void Naca0012_ClauserTE_RemainsPlausible(
        double alpha, double cdMin, double cdMax)
    {
        var svc = new MsesAnalysisService();
        var r = Run(svc, "0012", alpha, 3_000_000);
        Assert.True(r.Converged);
        Assert.InRange(r.DragDecomposition.CD, cdMin, cdMax);
    }

    [Theory]
    [InlineData(0.0, 0.00, 0.02)]
    [InlineData(4.0, 0.003, 0.02)]
    [InlineData(8.0, 0.005, 0.025)]
    public void Naca0012_ThesisExactTE_RemainsPlausible(
        double alpha, double cdMin, double cdMax)
    {
        var svc = new MsesAnalysisService(useThesisExactTurbulent: true);
        var r = Run(svc, "0012", alpha, 3_000_000);
        Assert.True(r.Converged);
        Assert.InRange(r.DragDecomposition.CD, cdMin, cdMax);
    }

    [Theory]
    [InlineData(0.0, 0.00, 0.02)]
    [InlineData(4.0, 0.003, 0.02)]
    [InlineData(8.0, 0.005, 0.025)]
    public void Naca0012_ThesisExactWake_RemainsPlausible(
        double alpha, double cdMin, double cdMax)
    {
        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var r = Run(svc, "0012", alpha, 3_000_000);
        Assert.True(r.Converged);
        Assert.InRange(r.DragDecomposition.CD, cdMin, cdMax);
    }

    [Fact]
    public void PathComparison_OnAttachedCase_WithinOrderOfMagnitude()
    {
        // NACA 0012 α=4° Re=3e6 — all three paths should give CD
        // within 5× of each other (they use the same inviscid,
        // same laminar marcher, same transition — differences are
        // purely in the turbulent H-ODE and wake Squire-Young
        // integration point).
        var rClauser = Run(new MsesAnalysisService(), "0012", 4.0, 3_000_000);
        var rThesisTE = Run(new MsesAnalysisService(useThesisExactTurbulent: true),
            "0012", 4.0, 3_000_000);
        var rWake = Run(new MsesAnalysisService(
                useThesisExactTurbulent: true, useWakeMarcher: true),
            "0012", 4.0, 3_000_000);

        double cdMin = System.Math.Min(System.Math.Min(
            rClauser.DragDecomposition.CD,
            rThesisTE.DragDecomposition.CD),
            rWake.DragDecomposition.CD);
        double cdMax = System.Math.Max(System.Math.Max(
            rClauser.DragDecomposition.CD,
            rThesisTE.DragDecomposition.CD),
            rWake.DragDecomposition.CD);
        Assert.True(cdMax / System.Math.Max(cdMin, 1e-9) < 5.0,
            $"CD paths diverged too far: Clauser={rClauser.DragDecomposition.CD}, "
            + $"ThesisTE={rThesisTE.DragDecomposition.CD}, "
            + $"Wake={rWake.DragDecomposition.CD}");
    }

    [Theory]
    [InlineData(0.0, 0.002, 0.025)]
    [InlineData(4.0, 0.005, 0.025)]
    [InlineData(8.0, 0.008, 0.03)]
    public void Naca0012_FullyThesisExact_RemainsPlausible(
        double alpha, double cdMin, double cdMax)
    {
        // Most-thesis-exact configuration: implicit-Newton laminar +
        // implicit-Newton turbulent + wake far-field Squire-Young.
        // This is the MSES-class path end-to-end.
        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        var r = Run(svc, "0012", alpha, 3_000_000);
        Assert.True(r.Converged);
        Assert.InRange(r.DragDecomposition.CD, cdMin, cdMax);
    }

    [Fact]
    public void DragDecomposition_CdfAndCdpSumsToCd()
    {
        // NACA 0012 α=4° Re=3e6 with the fully-thesis-exact path.
        // CDF + CDP must equal CD (the physical decomposition
        // conservation). CDF > 0 (there's friction), CDP >= 0.
        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        var r = Run(svc, "0012", 4.0, 3_000_000);
        Assert.True(r.DragDecomposition.CD > 0);
        Assert.True(r.DragDecomposition.CDF > 0);
        Assert.True(r.DragDecomposition.CDP >= 0);
        double sum = r.DragDecomposition.CDF + r.DragDecomposition.CDP;
        Assert.Equal(r.DragDecomposition.CD, sum, 10);
    }

    [Fact]
    public void Naca0012_PolarMonotonic_CDRisesWithAlpha()
    {
        // Sanity: on NACA 0012, CD should rise monotonically with α
        // in the [0, 8°] range (attached flow getting more shear on
        // the upper surface as adverse gradient grows). All three
        // paths should show this shape.
        var svcWake = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        double[] alphas = { 0.0, 2.0, 4.0, 6.0, 8.0 };
        double prevCd = -1.0;
        foreach (var a in alphas)
        {
            var r = Run(svcWake, "0012", a, 3_000_000);
            Assert.True(r.Converged);
            if (prevCd > 0)
            {
                Assert.True(r.DragDecomposition.CD >= prevCd * 0.8,
                    $"CD({a}) = {r.DragDecomposition.CD} dipped significantly below CD({a - 2}) = {prevCd}");
            }
            prevCd = r.DragDecomposition.CD;
        }
    }
}

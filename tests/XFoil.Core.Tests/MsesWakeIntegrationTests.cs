using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Validates the Phase-2f wake-marcher opt-in through
/// <see cref="MsesAnalysisService"/>. With <c>useWakeMarcher=true</c>
/// Squire-Young is applied at the wake far-field (half-chord
/// downstream of the TE) rather than at the TE itself, which tightens
/// the CD estimate because H_wake has partially relaxed.
/// </summary>
public class MsesWakeIntegrationTests
{
    [Fact]
    public void Naca0012_Alpha0_Re3M_WakePath_ProducesFiniteCd()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true);
        var r = svc.AnalyzeViscous(geom, 0.0, settings);
        Assert.True(r.Converged);
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.DragDecomposition.CD, 0.001, 0.03);
    }

    [Fact]
    public void WakePath_And_TEPath_SameOrderOfMagnitude()
    {
        // Sanity: wake-far-field Squire-Young and TE Squire-Young
        // should land in the same order of magnitude (within 3×).
        // Huge deltas signal a wiring bug.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svcTE = new MsesAnalysisService(useThesisExactTurbulent: true);
        var svcWake = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);

        var rTE = svcTE.AnalyzeViscous(geom, 2.0, settings);
        var rWake = svcWake.AnalyzeViscous(geom, 2.0, settings);

        double ratio = rWake.DragDecomposition.CD / rTE.DragDecomposition.CD;
        Assert.InRange(ratio, 0.33, 3.0);
    }

    [Fact]
    public void WakePath_PopulatesWakeProfilesWithFiniteValues()
    {
        // Opt-in wake should populate ViscousAnalysisResult.WakeProfiles
        // with per-station (θ, H, Cτ, Ue) — non-empty, all finite.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var r = svc.AnalyzeViscous(geom, 2.0, settings);

        Assert.NotEmpty(r.WakeProfiles);
        foreach (var p in r.WakeProfiles)
        {
            Assert.True(double.IsFinite(p.Theta));
            Assert.True(double.IsFinite(p.Hk));
            Assert.True(double.IsFinite(p.EdgeVelocity));
            Assert.Equal(0.0, p.Cf); // wake = free-shear layer
            Assert.True(p.Theta > 0);
        }
    }

    [Fact]
    public void Naca4412_StallCase_WakePathStaysFinite()
    {
        // NACA 4412 α=14° is past the stall shoulder — the upper
        // surface is heavily separated and the TE state is borderline.
        // Wake marcher must not blow up from a marginal TE IC.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true, useWakeMarcher: true);
        var r = svc.AnalyzeViscous(geom, 14.0, settings);
        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.InRange(r.DragDecomposition.CD, 0.0, 0.5);
    }
}

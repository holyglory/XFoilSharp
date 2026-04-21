using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Validates the Phase-2e opt-in through <see cref="MsesAnalysisService"/>.
/// The constructor flag <c>useThesisExactTurbulent</c> is surfaced
/// to CLI and downstream consumers; these tests confirm it produces
/// a finite, plausible viscous result end-to-end.
/// </summary>
public class MsesServiceThesisExactTests
{
    [Fact]
    public void Naca0012_Alpha2_Re3M_ThesisExact_ProducesPlausibleCd()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svcClauser = new MsesAnalysisService(
            useThesisExactTurbulent: false);
        var svcThesis = new MsesAnalysisService(
            useThesisExactTurbulent: true);

        var rClauser = svcClauser.AnalyzeViscous(geom, 2.0, settings);
        var rThesis = svcThesis.AnalyzeViscous(geom, 2.0, settings);

        Assert.True(rThesis.Converged, "thesis path should converge on attached case");
        Assert.True(rThesis.DragDecomposition.CD > 0);
        Assert.True(rThesis.DragDecomposition.CD < 0.1);

        // Both CL values should match (inviscid solve is shared).
        Assert.Equal(rClauser.LiftCoefficient, rThesis.LiftCoefficient, 6);

        // CDs should be in the same ballpark on attached flow.
        double ratio = rThesis.DragDecomposition.CD / rClauser.DragDecomposition.CD;
        Assert.InRange(ratio, 0.3, 3.0);
    }

    [Fact]
    public void Naca4412_Alpha12_Re3M_ThesisExact_StaysFinite()
    {
        // NACA 4412 α=12° is near-stall — the turbulent BL is heavily
        // separated on the upper surface. XFoil's Newton iteration
        // classically diverges here; the thesis-exact marcher should
        // produce a finite (even if approximate) result.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: 3_000_000);

        var svc = new MsesAnalysisService(useThesisExactTurbulent: true);
        var r = svc.AnalyzeViscous(geom, 12.0, settings);

        Assert.True(double.IsFinite(r.DragDecomposition.CD));
        Assert.True(double.IsFinite(r.LiftCoefficient));
        Assert.InRange(r.DragDecomposition.CD, 0.0, 0.5);
    }
}

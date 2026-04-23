using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P3.2 — circulation verification. The Kutta-enforced γ system
/// with σ=0 must produce bit-identical Γ to the baseline P1 solve
/// at several cases. Confirms that the ApplyKuttaRow refactor
/// (P3.1) and the combined-system wiring (P2.2) don't silently
/// perturb the pure-vortex result.
/// </summary>
public class MsesInviscidKuttaCirculationTests
{
    [Theory]
    [InlineData("0012", 0.0, 0.0)]
    [InlineData("0012", 4.0, 0.0)]
    [InlineData("0012", 8.0, 0.2)]
    [InlineData("2412", 4.0, 0.0)]
    [InlineData("4412", 0.0, 0.0)]
    [InlineData("4412", 6.0, 0.3)]
    public void Circulation_WithAndWithoutSourcesArg_BitIdentical(
        string naca, double alphaDeg, double mach)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double alpha = alphaDeg * System.Math.PI / 180.0;

        // Baseline: no sources arg (uses the pure-vortex path).
        var r0 = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, alpha, 1.0, machNumber: mach);
        // Same, but with an explicit zero-sources array (uses the
        // combined-system path with zero σ).
        var zero = new double[pg.PanelCount + 1];
        var rZ = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, alpha, 1.0, machNumber: mach, sources: zero);

        Assert.Equal(r0.Circulation, rZ.Circulation, 14);
        Assert.Equal(r0.LiftCoefficient, rZ.LiftCoefficient, 14);
    }

    [Fact]
    public void GammaAtTrailingEdge_SatisfiesKuttaCondition()
    {
        // After solving, γ_0 + γ_N must equal 0 (the Kutta row).
        // Verify on a few cases.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        foreach (var (naca, aDeg) in new[] { ("0012", 0.0), ("0012", 4.0), ("4412", 6.0) })
        {
            var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
            var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
            var r = ThesisClosurePanelSolver.SolveInviscid(
                pg, 1.0, aDeg * System.Math.PI / 180.0, 1.0);
            double sum = r.Gamma[0] + r.Gamma[r.Gamma.Length - 1];
            Assert.True(System.Math.Abs(sum) < 1e-12,
                $"{naca} α={aDeg}: γ_0+γ_N={sum} should be ~0");
        }
    }

    [Fact]
    public void Circulation_MatchesKuttaJoukowski_Identity()
    {
        // CL = 2·Γ/(V∞·c) must hold by construction. Verify numerically.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var r = ThesisClosurePanelSolver.SolveInviscid(
            pg, freestreamSpeed: 2.0, alphaRadians: 5.0 * System.Math.PI / 180.0,
            chord: 3.0);
        double expected = 2.0 * r.Circulation / (2.0 * 3.0);
        Assert.Equal(expected, r.LiftCoefficient, 12);
    }
}

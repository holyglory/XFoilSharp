using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P1.4 — Karman-Tsien compressibility tests.
/// </summary>
public class MsesInviscidKarmanTsienTests
{
    [Fact]
    public void SolveInviscid_M0_Identical_ToNoMachArg()
    {
        // Passing M=0 must match the default (no compressibility)
        // result identically — the M=0 branch should short-circuit.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var r0 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        var r0m = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, machNumber: 0.0);
        Assert.Equal(r0.LiftCoefficient, r0m.LiftCoefficient, 14);
        for (int i = 0; i < r0.CpMidpoint.Length; i++)
        {
            Assert.Equal(r0.CpMidpoint[i], r0m.CpMidpoint[i], 14);
        }
    }

    [Fact]
    public void SolveInviscid_CL_RisesWithMach_PrandtlGlauert()
    {
        // CL(M) = CL(0) / sqrt(1-M²). At M=0.3, β=0.954, so
        // CL ratio ≈ 1.049.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var r0 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, machNumber: 0.0);
        var r3 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, machNumber: 0.3);
        double ratio = r3.LiftCoefficient / r0.LiftCoefficient;
        double beta = System.Math.Sqrt(1.0 - 0.3 * 0.3);
        double expected = 1.0 / beta;
        Assert.InRange(ratio, 0.95 * expected, 1.05 * expected);
    }

    [Fact]
    public void SolveInviscid_Cp_KarmanTsienAmplifiesNegativeCp()
    {
        // K-T correction amplifies suction (Cp < 0) more than it
        // amplifies pressure (Cp > 0) — this is the physical
        // signature of compressibility on the suction peak.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var r0 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, machNumber: 0.0);
        var r3 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, machNumber: 0.3);

        double minCp0 = double.PositiveInfinity;
        double minCp3 = double.PositiveInfinity;
        foreach (var c in r0.CpMidpoint) if (c < minCp0) minCp0 = c;
        foreach (var c in r3.CpMidpoint) if (c < minCp3) minCp3 = c;
        // Suction peak must be more negative under K-T.
        Assert.True(minCp3 < minCp0,
            $"Expected minCp3 ({minCp3}) < minCp0 ({minCp0})");
    }

    [Fact]
    public void SolveInviscid_SymmetricAtAlpha0_StaysZeroAtAnyMach()
    {
        // Symmetric airfoil at α=0° has CL=0 regardless of Mach.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        foreach (var m in new[] { 0.0, 0.1, 0.2, 0.3, 0.5 })
        {
            var r = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, 0.0, 1.0, m);
            Assert.True(System.Math.Abs(r.LiftCoefficient) < 1e-6,
                $"M={m}: CL should be 0 for symmetric α=0; got {r.LiftCoefficient}");
        }
    }
}

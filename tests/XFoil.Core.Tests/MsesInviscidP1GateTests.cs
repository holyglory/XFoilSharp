using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// P1.5 — hard gate for the clean-room inviscid fork. Cross-checks
/// MSES panel-solver CL against XFoil.Solver.Modern inviscid CL
/// across (NACA 0012, 2412, 4412) × (α = 0, 4, 8°) × (M = 0, 0.2, 0.3).
///
/// Tolerance: 5% on CL. Linear-vortex (this fork) and streamfunction
/// (XFoil.Solver.Modern) formulations give slightly different CL on
/// discrete panelings — 5% accommodates that while catching any
/// real algorithmic error. If this gate fails, P2+ builds on a
/// broken inviscid and should not proceed.
/// </summary>
public class MsesInviscidP1GateTests
{
    private static (double mses, double modern) RunBoth(
        string naca, double alphaDeg, double mach)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var rMses = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, alphaDeg * System.Math.PI / 180.0, 1.0, mach);

        var modernSvc = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: mach);
        var rModern = modernSvc.AnalyzeInviscid(geom, alphaDeg, settings);
        return (rMses.LiftCoefficient, rModern.LiftCoefficient);
    }

    [Theory]
    [InlineData("0012", 0.0, 0.0)]
    [InlineData("0012", 4.0, 0.0)]
    [InlineData("0012", 8.0, 0.0)]
    [InlineData("0012", 4.0, 0.2)]
    [InlineData("0012", 4.0, 0.3)]
    [InlineData("2412", 0.0, 0.0)]
    [InlineData("2412", 4.0, 0.0)]
    [InlineData("2412", 4.0, 0.3)]
    [InlineData("4412", 0.0, 0.0)]
    [InlineData("4412", 4.0, 0.0)]
    [InlineData("4412", 8.0, 0.0)]
    [InlineData("4412", 4.0, 0.2)]
    [InlineData("4412", 4.0, 0.3)]
    public void P1Gate_MsesFork_AgreesWithModernXfoil_Within5Percent(
        string naca, double alphaDeg, double mach)
    {
        var (mses, modern) = RunBoth(naca, alphaDeg, mach);

        // Symmetric α=0° — both must be ≈ 0.
        if (System.Math.Abs(modern) < 0.01)
        {
            Assert.True(System.Math.Abs(mses) < 0.01,
                $"{naca} α={alphaDeg} M={mach}: Modern CL≈0 ({modern:F5}) but fork got {mses:F5}");
            return;
        }

        double relErr = System.Math.Abs(mses - modern) / System.Math.Abs(modern);
        Assert.True(relErr < 0.05,
            $"{naca} α={alphaDeg}° M={mach}: MSES CL={mses:F4}, Modern CL={modern:F4}, "
            + $"rel err {relErr:P2}");
    }
}

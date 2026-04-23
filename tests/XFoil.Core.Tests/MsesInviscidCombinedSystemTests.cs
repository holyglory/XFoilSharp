using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P2.2 — combined γ+σ inviscid system. Verifies that passing
/// σ=null or σ=0 recovers the pure-vortex result, and that a
/// non-zero σ produces the expected perturbation.
/// </summary>
public class MsesInviscidCombinedSystemTests
{
    [Fact]
    public void SolveInviscid_ZeroSource_IsBitIdenticalToNullSource()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var rNull = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        var zero = new double[pg.PanelCount + 1];
        var rZero = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0, sources: zero);
        Assert.Equal(rNull.LiftCoefficient, rZero.LiftCoefficient, 12);
        for (int i = 0; i < rNull.CpMidpoint.Length; i++)
        {
            Assert.Equal(rNull.CpMidpoint[i], rZero.CpMidpoint[i], 12);
        }
    }

    [Fact]
    public void SolveInviscid_PositiveUniformSource_ChangesSolution()
    {
        // A positive uniform σ represents outflow from the body.
        // The inviscid γ must adjust to keep flow tangent, and CL
        // will shift (typically down on an airfoil at +α because
        // source on upper de-accelerates the local flow).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var rBaseline = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        var sources = new double[pg.PanelCount + 1];
        for (int i = 0; i < sources.Length; i++) sources[i] = 0.01;
        var rWithSource = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, a, 1.0, sources: sources);
        // Uniform σ on a closed body doesn't change CL much
        // (net mass flow is bounded), but it MUST produce some
        // detectable Ue perturbation.
        double dUeMax = 0;
        for (int i = 0; i < rBaseline.CpMidpoint.Length; i++)
        {
            double dCp = System.Math.Abs(rBaseline.CpMidpoint[i] - rWithSource.CpMidpoint[i]);
            if (dCp > dUeMax) dUeMax = dCp;
        }
        Assert.True(dUeMax > 1e-4,
            $"Expected observable Cp change under uniform σ=0.01; got dCp_max={dUeMax}");
    }

    [Fact]
    public void SolveInviscid_SourceLengthMismatch_Throws()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var badSources = new double[42];
        Assert.Throws<System.ArgumentException>(
            () => ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, 0.0, 1.0, sources: badSources));
    }

    [Fact]
    public void SolveInviscid_AntisymmetricSource_OnSymmetricAirfoil_ProducesLift()
    {
        // Put a positive σ on the upper surface and negative on
        // the lower surface of a symmetric airfoil at α=0. This
        // breaks symmetry and should produce non-zero CL.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        var sources = new double[n + 1];
        // Panels 0..n/2-1 are upper (walk from TE to LE).
        // Panels n/2..n-1 are lower.
        for (int i = 0; i < n + 1; i++)
        {
            sources[i] = (i < n / 2) ? 0.01 : -0.01;
        }
        var r0 = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, 0.0, 1.0);
        var rS = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, 0.0, 1.0, sources: sources);
        // Uncoupled: CL ≈ 0. Coupled: CL ≠ 0.
        Assert.True(System.Math.Abs(r0.LiftCoefficient) < 1e-6);
        Assert.True(System.Math.Abs(rS.LiftCoefficient) > 1e-4,
            $"Antisymmetric σ on symmetric airfoil should produce non-zero CL; got {rS.LiftCoefficient}");
    }
}

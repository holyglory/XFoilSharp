using XFoil.MsesSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P2.1 — linear-source panel influence tests.
/// </summary>
public class MsesInviscidSourcePanelTests
{
    [Fact]
    public void LinearSourcePanelContribution_SelfPanel_UniformSourceGivesHalfNormal()
    {
        // For a flat source sheet at a point just above, the normal
        // velocity from a uniform σ equals +σ/2 (half the source
        // goes up). The closed-form self-panel case must produce
        // this.
        double L = 2.0;
        double ax = 0, ay = 0, bx = L, by = 0;
        double tx = 1, ty = 0;
        double midX = 0.5 * L, midY = 0.0;
        var (uA, vA, uB, vB) = MsesInviscidPanelSolver
            .LinearSourcePanelContribution(midX, midY, ax, ay, bx, by, tx, ty, L,
                selfPanel: true);
        // Uniform σ=1 ⇒ sum of shape coefficients on the normal = 1/2.
        // In panel-local, v is normal. The rotated global vA + vB
        // equals the rotation of (1/4 + 1/4) normal = 1/2.
        // With tangent = (1, 0), global v = local v. So vA + vB = 0.5.
        double vNormal = vA + vB;
        Assert.Equal(0.5, vNormal, 12);
    }

    [Fact]
    public void LinearSourcePanelContribution_FarField_DecaysLike1OverR()
    {
        // A panel with uniform σ=1 produces velocity at far field
        // with magnitude ∼ (σ·L) / (2π·r).
        double ax = 0, ay = 0, bx = 1, by = 0;
        double tx = 1, ty = 0, L = 1.0;
        double px = 10.0, py = 0.1;
        var (uA, vA, uB, vB) = MsesInviscidPanelSolver
            .LinearSourcePanelContribution(px, py, ax, ay, bx, by, tx, ty, L,
                selfPanel: false);
        double u = uA + uB;
        double v = vA + vB;
        double mag = System.Math.Sqrt(u * u + v * v);
        double expected = 1.0 / (2.0 * System.Math.PI * System.Math.Sqrt(px * px + py * py));
        Assert.InRange(mag, 0.9 * expected, 1.1 * expected);
    }

    [Fact]
    public void LinearSourcePanelContribution_VsVortex_RotatedBy90Degrees()
    {
        // Source induced velocity is the 90° rotation of vortex
        // induced velocity (specifically, (u_σ, v_σ) = (v_γ, -u_γ)
        // in panel-local coords). Verify on a generic point.
        double L = 1.0;
        double ax = 0, ay = 0, bx = L, by = 0;
        double tx = 1, ty = 0;
        double px = 0.3, py = 0.5;
        var vtx = MsesInviscidPanelSolver.LinearVortexPanelContribution(
            px, py, ax, ay, bx, by, tx, ty, L, selfPanel: false);
        var src = MsesInviscidPanelSolver.LinearSourcePanelContribution(
            px, py, ax, ay, bx, by, tx, ty, L, selfPanel: false);
        // Since tx=1, ty=0, local == global, so compare directly:
        //   src.u == vtx.v
        //   src.v == -vtx.u
        Assert.Equal(vtx.vA, src.uA, 12);
        Assert.Equal(vtx.vB, src.uB, 12);
        Assert.Equal(-vtx.uA, src.vA, 12);
        Assert.Equal(-vtx.uB, src.vB, 12);
    }

    [Fact]
    public void BuildSourceTangentInfluenceMatrix_Naca0012_ShapeIsNxNp1()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var a = MsesInviscidPanelSolver.BuildSourceTangentInfluenceMatrix(pg);
        Assert.Equal(pg.PanelCount, a.GetLength(0));
        Assert.Equal(pg.PanelCount + 1, a.GetLength(1));
    }

    [Fact]
    public void BuildSourceNormalInfluenceMatrix_SymmetricAirfoil_Antisymmetric()
    {
        // On a symmetric airfoil, σ on a top panel induces opposite
        // normal velocity at the mirror bottom collocation (compared
        // to the direct). Verified by row-sum antisymmetry.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var a = MsesInviscidPanelSolver.BuildSourceNormalInfluenceMatrix(pg);
        int n = pg.PanelCount;
        // Row i vs row (n-1-i): the magnitudes of the row sums
        // should match. Sign may flip because the mirror flips the
        // inward-normal direction.
        int i = 5;
        int iMirror = n - 1 - i;
        double si = 0, sm = 0;
        for (int k = 0; k < n + 1; k++) { si += a[i, k]; sm += a[iMirror, k]; }
        Assert.True(System.Math.Abs(System.Math.Abs(si) - System.Math.Abs(sm)) < 1e-8,
            $"row-sum magnitudes differ: |sum_i|={System.Math.Abs(si)}, "
            + $"|sum_mirror|={System.Math.Abs(sm)}");
    }
}

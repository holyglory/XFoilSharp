using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P1.2 — linear-vortex influence matrix tests. Validate the
/// Katz &amp; Plotkin §11.4 analytical integrals on known cases
/// (uniform γ, self-panel, far-field decay).
/// </summary>
public class MsesInviscidInfluenceMatrixTests
{
    [Fact]
    public void InfluenceMatrix_Naca0012_ShapeIsNxNp1()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var a = ThesisClosurePanelSolver.BuildVortexInfluenceMatrix(pg);
        Assert.Equal(pg.PanelCount, a.GetLength(0));
        Assert.Equal(pg.PanelCount + 1, a.GetLength(1));
    }

    [Fact]
    public void LinearVortexPanelContribution_UnitConstantGamma_FarField_DecaysLike1OverR()
    {
        // For a single panel [0,1] with γ=1 everywhere, the induced
        // velocity magnitude at a far point (r ≫ L) should decay
        // like Γ/(2π·r) where Γ = total panel circulation = γ·L = 1.
        double ax = 0, ay = 0, bx = 1, by = 0;
        double tx = 1, ty = 0, L = 1.0;
        // Point at (10, 0.1) — roughly 10 chords away, slightly above.
        double px = 10.0, py = 0.1;
        var (uA, vA, uB, vB) = ThesisClosurePanelSolver
            .LinearVortexPanelContribution(px, py, ax, ay, bx, by, tx, ty, L,
                selfPanel: false);

        // Constant-γ contribution on this panel = γ·(shapeA + shapeB).
        // shapeA + shapeB = (1 - ξ/L) + (ξ/L) = 1 for all ξ. So a
        // uniform γ=1 gives total influence (uA+uB, vA+vB).
        double u = uA + uB;
        double v = vA + vB;
        double mag = System.Math.Sqrt(u * u + v * v);

        // Expected magnitude: Γ/(2π·r) = 1/(2π·sqrt(100.01)) ≈
        // 1/62.83 ≈ 0.01592. Allow ±10 % (panel has finite length).
        double expected = 1.0 / (2.0 * System.Math.PI * System.Math.Sqrt(px * px + py * py));
        Assert.InRange(mag, 0.9 * expected, 1.1 * expected);
    }

    [Fact]
    public void LinearVortexPanelContribution_SelfPanel_UniformGammaGivesHalf()
    {
        // For a flat vortex sheet at a point just above, the tangent
        // velocity from a uniform γ equals +γ/2. The closed-form
        // self-panel case must produce this.
        double L = 2.0;
        double ax = 0, ay = 0, bx = L, by = 0;
        double tx = 1, ty = 0;
        double midX = 0.5 * L, midY = 0.0;
        var (uA, _, uB, _) = ThesisClosurePanelSolver
            .LinearVortexPanelContribution(midX, midY, ax, ay, bx, by, tx, ty, L,
                selfPanel: true);
        // Uniform γ=1 ⇒ coefficients sum to 0.5 on the tangent (u).
        Assert.Equal(0.5, uA + uB, 12);
    }

    [Fact]
    public void InfluenceMatrix_UniformGammaOnSymmetricAirfoil_ProducesSymmetricA()
    {
        // On a symmetric airfoil, the influence of panel k and its
        // mirror panel (N-1-k) at the corresponding mirrored
        // collocation point should reflect through y → -y. Test by
        // building the matrix and checking reflective antisymmetry
        // on a representative pair.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var a = ThesisClosurePanelSolver.BuildVortexInfluenceMatrix(pg);
        int n = pg.PanelCount;
        // Compare row i with row (n-1-i). They should have the same
        // column-sum magnitude (within numerical tolerance): a uniform
        // γ=1 produces equal-magnitude tangential velocity at mirror
        // collocation points (sign flipped by tangent orientation).
        int i = 5;
        int iMirror = n - 1 - i;
        double sumI = 0, sumM = 0;
        for (int k = 0; k < n + 1; k++)
        {
            sumI += a[i, k];
            sumM += a[iMirror, k];
        }
        Assert.True(System.Math.Abs(System.Math.Abs(sumI) - System.Math.Abs(sumM)) < 1e-8,
            $"row sums mismatch: |sum_i|={System.Math.Abs(sumI)}, "
            + $"|sum_mirror|={System.Math.Abs(sumM)}");
    }

    [Fact]
    public void LinearVortexPanelContribution_AntisymmetryInEta()
    {
        // Swapping η → -η on the collocation point should flip
        // the sign of the tangential (u_local) contribution (vortex
        // sheet antisymmetry). We test this by placing a panel along
        // the x-axis and querying at (0.5, +h) vs (0.5, -h).
        double L = 1.0;
        double ax = 0, ay = 0, bx = L, by = 0;
        double tx = 1, ty = 0;
        double xi = 0.5, eta = 0.3;
        var plus = ThesisClosurePanelSolver
            .LinearVortexPanelContribution(xi, eta, ax, ay, bx, by, tx, ty, L,
                selfPanel: false);
        var minus = ThesisClosurePanelSolver
            .LinearVortexPanelContribution(xi, -eta, ax, ay, bx, by, tx, ty, L,
                selfPanel: false);
        // In this coordinate setup, tangent=(1,0) so u_global is the
        // tangential component. It should flip sign under η → -η.
        Assert.True(System.Math.Abs(plus.uA + minus.uA) < 1e-12,
            $"uA should flip: +η={plus.uA}, -η={minus.uA}");
        Assert.True(System.Math.Abs(plus.uB + minus.uB) < 1e-12,
            $"uB should flip: +η={plus.uB}, -η={minus.uB}");
    }
}

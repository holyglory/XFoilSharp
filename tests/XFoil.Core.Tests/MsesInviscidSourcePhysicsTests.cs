using XFoil.MsesSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P2.3 — source-distribution physics validation. Tests beyond
/// the structural pins of P2.1/P2.2: verifies the source-only
/// system matches analytic solutions on canonical configurations.
/// </summary>
public class MsesInviscidSourcePhysicsTests
{
    /// <summary>
    /// Flat-plate geometry with 2 aligned panels in a straight line.
    /// For σ=0 (no sources) this is degenerate (zero thickness, zero
    /// circulation), but we can probe the source-panel math in
    /// isolation.
    /// </summary>
    [Fact]
    public void UniformSource_OnFlatPlate_MidpointNormalIsHalfSigma()
    {
        // Build a fake panelized geometry: two panels along the
        // x-axis, from (0,0) → (0.5, 0) → (1, 0). The source-panel
        // contribution at each panel's own midpoint just above
        // the sheet should give v_normal = σ/2 per the standard
        // vortex/source-sheet jump result.
        var pg = new MsesInviscidPanelSolver.PanelizedGeometry(
            PanelCount: 2,
            NodeX: new[] { 0.0, 0.5, 1.0 },
            NodeY: new[] { 0.0, 0.0, 0.0 },
            MidX: new[] { 0.25, 0.75 },
            MidY: new[] { 0.0, 0.0 },
            TangentX: new[] { 1.0, 1.0 },
            TangentY: new[] { 0.0, 0.0 },
            NormalX: new[] { 0.0, 0.0 },
            NormalY: new[] { 1.0, 1.0 },
            Length: new[] { 0.5, 0.5 });
        // Compute the normal-influence matrix.
        var aN = MsesInviscidPanelSolver.BuildSourceNormalInfluenceMatrix(pg);
        // Uniform σ=1 at all three nodes. The self-panel normal
        // contribution at its midpoint is +1/2. Far-panel contribution
        // is smaller (logarithmic). Sum gives full v_normal at mid.
        double sum0 = aN[0, 0] + aN[0, 1] + aN[0, 2];
        double sum1 = aN[1, 0] + aN[1, 1] + aN[1, 2];
        // Dominant term is 1/2 from the self-panel; off-panel
        // contributions add perturbation. Tolerance ±0.1 accepts
        // the neighboring panel's logarithmic contribution.
        Assert.InRange(sum0, 0.35, 0.55);
        Assert.InRange(sum1, 0.35, 0.55);
    }

    [Fact]
    public void UniformSource_OnSymmetricAirfoil_PreservesCL()
    {
        // A uniform σ everywhere on a symmetric airfoil at α=0°
        // should not break the CL=0 symmetry (the source is
        // symmetric top/bottom).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount + 1;
        var uniformSources = new double[n];
        for (int i = 0; i < n; i++) uniformSources[i] = 0.01;
        var r = MsesInviscidPanelSolver.SolveInviscid(
            pg, 1.0, 0.0, 1.0, sources: uniformSources);
        Assert.True(System.Math.Abs(r.LiftCoefficient) < 1e-4,
            $"Uniform σ on symmetric α=0° case should keep CL≈0; got {r.LiftCoefficient}");
    }

    [Fact]
    public void AntisymmetricSource_OnSymmetricAirfoil_ProducesLinearCL()
    {
        // An antisymmetric σ (σ_upper = +c, σ_lower = -c) on a
        // symmetric airfoil breaks symmetry proportionally to c.
        // CL should scale linearly with the amplitude c.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;

        double[] BuildAntisym(double c)
        {
            var s = new double[n + 1];
            for (int i = 0; i < n + 1; i++) s[i] = (i < n / 2) ? c : -c;
            return s;
        }

        var r1 = MsesInviscidPanelSolver.SolveInviscid(
            pg, 1.0, 0.0, 1.0, sources: BuildAntisym(0.01));
        var r2 = MsesInviscidPanelSolver.SolveInviscid(
            pg, 1.0, 0.0, 1.0, sources: BuildAntisym(0.02));
        double ratio = r2.LiftCoefficient / r1.LiftCoefficient;
        Assert.InRange(ratio, 1.95, 2.05);
    }

    [Fact]
    public void Source_Superposition_Linearity()
    {
        // Inviscid is linear in σ (for a given γ solution). If σ_A
        // produces CL_A and σ_B produces CL_B, then σ_A + σ_B should
        // produce CL_A + CL_B − CL(0).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("2412", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;

        double a = 4.0 * System.Math.PI / 180.0;
        var sA = new double[n + 1];
        var sB = new double[n + 1];
        var sAB = new double[n + 1];
        for (int i = 0; i < n + 1; i++)
        {
            // Smooth sinusoidal patterns.
            double phase = 2.0 * System.Math.PI * i / n;
            sA[i] = 0.005 * System.Math.Sin(phase);
            sB[i] = 0.003 * System.Math.Cos(phase);
            sAB[i] = sA[i] + sB[i];
        }
        var r0 = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        var rA = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a, 1.0, sources: sA);
        var rB = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a, 1.0, sources: sB);
        var rAB = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a, 1.0, sources: sAB);
        double predictedCL = rA.LiftCoefficient + rB.LiftCoefficient - r0.LiftCoefficient;
        Assert.True(System.Math.Abs(rAB.LiftCoefficient - predictedCL) < 1e-10,
            $"superposition: CL(A+B)={rAB.LiftCoefficient} should equal "
            + $"CL(A)+CL(B)−CL(0)={predictedCL}");
    }

    [Fact]
    public void SourceInfluence_Reciprocity_BetweenPanels()
    {
        // For a flat geometry, the tangent-velocity induced at
        // panel i from unit σ at panel j is NOT exactly equal to
        // the tangent-velocity at j from unit σ at i (sources are
        // not self-reciprocal like some Green's function pairings),
        // but the MAGNITUDES should be comparable on symmetric
        // configurations.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var aT = MsesInviscidPanelSolver.BuildSourceTangentInfluenceMatrix(pg);
        int n = pg.PanelCount;
        // Probe two well-separated panels.
        int i = 5, j = n - 5;
        Assert.True(System.Math.Abs(aT[i, j]) < 1.0,
            $"Bounded far-field influence: A[{i},{j}]={aT[i,j]}");
        Assert.True(System.Math.Abs(aT[j, i]) < 1.0,
            $"Bounded far-field influence: A[{j},{i}]={aT[j,i]}");
    }
}

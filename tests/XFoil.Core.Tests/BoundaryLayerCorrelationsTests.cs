using System;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for BoundaryLayerCorrelations: BL correlation functions ported from xblsys.f
/// with full Jacobian sensitivity verification via central-difference cross-check.
/// </summary>
public class BoundaryLayerCorrelationsTests
{
    // Tolerance for value comparisons against hand-computed Fortran reference
    private const double ValueTol = 1e-10;
    // Tolerance for numerical Jacobian verification (central difference vs analytical)
    private const double JacobianTol = 1e-6;
    // Perturbation for central-difference Jacobian check
    private const double Eps = 1e-7;

    // =====================================================================
    // HKIN: Kinematic shape parameter
    // =====================================================================

    [Fact]
    public void KinematicShapeParameter_IncompressibleCase_ReturnsCorrectValue()
    {
        // H=2.5, Msq=0 -> Hk = (2.5 - 0) / (1.0) = 2.5
        var (hk, hk_h, hk_msq) = BoundaryLayerCorrelations.KinematicShapeParameter(2.5, 0.0);

        Assert.Equal(2.5, hk, 10);
        Assert.Equal(1.0, hk_h, 10);
        Assert.Equal(-0.29, hk_msq, 10);
    }

    [Fact]
    public void KinematicShapeParameter_CompressibleCase_MatchesFortran()
    {
        // H=2.5, Msq=0.04
        // HK = (2.5 - 0.29*0.04) / (1.0 + 0.113*0.04)
        //    = (2.5 - 0.0116) / (1.0 + 0.00452)
        //    = 2.4884 / 1.00452
        double expectedHk = (2.5 - 0.29 * 0.04) / (1.0 + 0.113 * 0.04);

        var (hk, hk_h, hk_msq) = BoundaryLayerCorrelations.KinematicShapeParameter(2.5, 0.04);

        Assert.Equal(expectedHk, hk, 10);
    }

    [Fact]
    public void KinematicShapeParameter_JacobianMatchesCentralDifference()
    {
        double h = 2.5, msq = 0.04;
        var (_, hk_h, hk_msq) = BoundaryLayerCorrelations.KinematicShapeParameter(h, msq);

        // d/dH via central difference
        var (hkPlus, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h + Eps, msq);
        var (hkMinus, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h - Eps, msq);
        double numHk_h = (hkPlus - hkMinus) / (2.0 * Eps);

        // d/dMsq via central difference
        (hkPlus, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h, msq + Eps);
        (hkMinus, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h, msq - Eps);
        double numHk_msq = (hkPlus - hkMinus) / (2.0 * Eps);

        Assert.True(Math.Abs(hk_h - numHk_h) < JacobianTol,
            $"Hk_H: analytical={hk_h}, numerical={numHk_h}, diff={Math.Abs(hk_h - numHk_h)}");
        Assert.True(Math.Abs(hk_msq - numHk_msq) < JacobianTol,
            $"Hk_Msq: analytical={hk_msq}, numerical={numHk_msq}, diff={Math.Abs(hk_msq - numHk_msq)}");
    }

    // =====================================================================
    // HSL: Laminar shape parameter
    // =====================================================================

    [Fact]
    public void LaminarShapeParameter_BelowThreshold_ReturnsCorrectValue()
    {
        // HK=2.5 < 4.35
        var (hs, hs_hk, hs_rt, hs_msq) = BoundaryLayerCorrelations.LaminarShapeParameter(2.5);

        // Fortran: TMP = 2.5 - 4.35 = -1.85
        // HS = 0.0111*(-1.85)^2/(2.5+1.0) - 0.0278*(-1.85)^3/(2.5+1.0) + 1.528 - 0.0002*(-1.85*2.5)^2
        double tmp = 2.5 - 4.35;
        double expectedHs = 0.0111 * tmp * tmp / (2.5 + 1.0)
                          - 0.0278 * tmp * tmp * tmp / (2.5 + 1.0)
                          + 1.528
                          - 0.0002 * (tmp * 2.5) * (tmp * 2.5);

        Assert.Equal(expectedHs, hs, 10);
        Assert.Equal(0.0, hs_rt, 10);
        Assert.Equal(0.0, hs_msq, 10);
    }

    [Fact]
    public void LaminarShapeParameter_AboveThreshold_ReturnsCorrectValue()
    {
        // HK=5.0 > 4.35
        var (hs, _, _, _) = BoundaryLayerCorrelations.LaminarShapeParameter(5.0);

        double expectedHs = 0.015 * (5.0 - 4.35) * (5.0 - 4.35) / 5.0 + 1.528;

        Assert.Equal(expectedHs, hs, 10);
    }

    [Fact]
    public void LaminarShapeParameter_JacobianMatchesCentralDifference()
    {
        double hk = 2.5;
        var (_, hs_hk, _, _) = BoundaryLayerCorrelations.LaminarShapeParameter(hk);

        var (hsPlus, _, _, _) = BoundaryLayerCorrelations.LaminarShapeParameter(hk + Eps);
        var (hsMinus, _, _, _) = BoundaryLayerCorrelations.LaminarShapeParameter(hk - Eps);
        double numHs_hk = (hsPlus - hsMinus) / (2.0 * Eps);

        Assert.True(Math.Abs(hs_hk - numHs_hk) < JacobianTol,
            $"Hs_Hk: analytical={hs_hk}, numerical={numHs_hk}, diff={Math.Abs(hs_hk - numHs_hk)}");
    }

    // =====================================================================
    // HST: Turbulent shape parameter
    // =====================================================================

    [Fact]
    public void TurbulentShapeParameter_AttachedBranch_ReturnsCorrectValue()
    {
        // HK=1.4, RT=5000, MSQ=0.0 -> attached (HK < HO)
        var (hs, hs_hk, hs_rt, hs_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(1.4, 5000.0, 0.0);

        // Must return a value > HSMIN=1.5
        Assert.True(hs >= 1.5, $"HS should be >= 1.5, got {hs}");
    }

    [Fact]
    public void TurbulentShapeParameter_SeparatedBranch_ReturnsCorrectValue()
    {
        // HK=5.0, RT=5000, MSQ=0.0 -> separated (HK > HO ≈ 3.08)
        var (hs, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(5.0, 5000.0, 0.0);

        Assert.True(hs > 1.0, $"HS should be > 1.0 for turbulent, got {hs}");
    }

    [Fact]
    public void TurbulentShapeParameter_JacobianMatchesCentralDifference()
    {
        double hk = 1.4, rt = 5000.0, msq = 0.0;
        var (_, hs_hk, hs_rt, hs_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt, msq);

        // d/dHk
        var (hsP, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk + Eps, rt, msq);
        var (hsM, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk - Eps, rt, msq);
        double numHs_hk = (hsP - hsM) / (2.0 * Eps);

        // d/dRt
        (hsP, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt + Eps, msq);
        (hsM, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt - Eps, msq);
        double numHs_rt = (hsP - hsM) / (2.0 * Eps);

        // d/dMsq
        (hsP, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt, msq + Eps);
        (hsM, _, _, _) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt, msq - Eps);
        double numHs_msq = (hsP - hsM) / (2.0 * Eps);

        Assert.True(Math.Abs(hs_hk - numHs_hk) < JacobianTol,
            $"Hs_Hk: analytical={hs_hk}, numerical={numHs_hk}, diff={Math.Abs(hs_hk - numHs_hk)}");
        Assert.True(Math.Abs(hs_rt - numHs_rt) < JacobianTol,
            $"Hs_Rt: analytical={hs_rt}, numerical={numHs_rt}, diff={Math.Abs(hs_rt - numHs_rt)}");
        Assert.True(Math.Abs(hs_msq - numHs_msq) < JacobianTol,
            $"Hs_Msq: analytical={hs_msq}, numerical={numHs_msq}, diff={Math.Abs(hs_msq - numHs_msq)}");
    }

    // =====================================================================
    // CFL: Laminar skin friction
    // =====================================================================

    [Fact]
    public void LaminarSkinFriction_NominalCase_ReturnsCorrectValue()
    {
        // HK=2.5, RT=500, MSQ=0.0
        var (cf, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.LaminarSkinFriction(2.5, 500.0, 0.0);

        // Fortran: TMP = (5.5-2.5)^3/(2.5+1.0) = 27.0/3.5 = 7.714285...
        // CF = (0.0727*TMP - 0.07) / 500
        double tmp = Math.Pow(5.5 - 2.5, 3) / (2.5 + 1.0);
        double expectedCf = (0.0727 * tmp - 0.07) / 500.0;

        Assert.Equal(expectedCf, cf, 10);
        Assert.Equal(0.0, cf_msq, 10);
    }

    [Fact]
    public void LaminarSkinFriction_HighHk_ReturnsCorrectValue()
    {
        // HK=6.0 > 5.5, uses alternate branch
        var (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(6.0, 500.0, 0.0);

        double tmp = 1.0 - 1.0 / (6.0 - 4.5);
        double expectedCf = (0.015 * tmp * tmp - 0.07) / 500.0;

        Assert.Equal(expectedCf, cf, 10);
    }

    [Fact]
    public void LaminarSkinFriction_ClampedHk_NoNaNOrInf()
    {
        // HK=1.0 -- dangerous for some correlations, should clamp safely
        var (cf, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.LaminarSkinFriction(1.0, 500.0, 0.0);

        Assert.False(double.IsNaN(cf), "CF should not be NaN");
        Assert.False(double.IsInfinity(cf), "CF should not be Inf");
        Assert.False(double.IsNaN(cf_hk), "CF_HK should not be NaN");
        Assert.False(double.IsInfinity(cf_hk), "CF_HK should not be Inf");
    }

    [Fact]
    public void LaminarSkinFriction_JacobianMatchesCentralDifference()
    {
        double hk = 2.5, rt = 500.0, msq = 0.0;
        var (_, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt, msq);

        // d/dHk
        var (cfP, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk + Eps, rt, msq);
        var (cfM, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk - Eps, rt, msq);
        double numCf_hk = (cfP - cfM) / (2.0 * Eps);

        // d/dRt
        (cfP, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt + Eps, msq);
        (cfM, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt - Eps, msq);
        double numCf_rt = (cfP - cfM) / (2.0 * Eps);

        Assert.True(Math.Abs(cf_hk - numCf_hk) < JacobianTol,
            $"Cf_Hk: analytical={cf_hk}, numerical={numCf_hk}, diff={Math.Abs(cf_hk - numCf_hk)}");
        Assert.True(Math.Abs(cf_rt - numCf_rt) < JacobianTol,
            $"Cf_Rt: analytical={cf_rt}, numerical={numCf_rt}, diff={Math.Abs(cf_rt - numCf_rt)}");
    }

    // =====================================================================
    // CFT: Turbulent skin friction
    // =====================================================================

    [Fact]
    public void TurbulentSkinFriction_NominalCase_ReturnsPositiveValue()
    {
        // HK=1.4, RT=5000, MSQ=0.0
        var (cf, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.TurbulentSkinFriction(1.4, 5000.0, 0.0);

        // Turbulent Cf should be positive for attached flow
        Assert.True(cf > 0.0, $"Turbulent Cf should be positive, got {cf}");
    }

    [Fact]
    public void TurbulentSkinFriction_JacobianMatchesCentralDifference()
    {
        double hk = 1.4, rt = 5000.0, msq = 0.0;
        var (_, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msq);

        // d/dHk
        var (cfP, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk + Eps, rt, msq);
        var (cfM, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk - Eps, rt, msq);
        double numCf_hk = (cfP - cfM) / (2.0 * Eps);

        // d/dRt
        (cfP, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt + Eps, msq);
        (cfM, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt - Eps, msq);
        double numCf_rt = (cfP - cfM) / (2.0 * Eps);

        // d/dMsq (at nonzero Msq to make the derivative meaningful)
        double msqTest = 0.1;
        var (_, cf_hk2, cf_rt2, cf_msq2) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msqTest);
        (cfP, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msqTest + Eps);
        (cfM, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msqTest - Eps);
        double numCf_msq = (cfP - cfM) / (2.0 * Eps);

        Assert.True(Math.Abs(cf_hk - numCf_hk) < JacobianTol,
            $"Cf_Hk: analytical={cf_hk}, numerical={numCf_hk}, diff={Math.Abs(cf_hk - numCf_hk)}");
        Assert.True(Math.Abs(cf_rt - numCf_rt) < JacobianTol,
            $"Cf_Rt: analytical={cf_rt}, numerical={numCf_rt}, diff={Math.Abs(cf_rt - numCf_rt)}");
        Assert.True(Math.Abs(cf_msq2 - numCf_msq) < JacobianTol,
            $"Cf_Msq: analytical={cf_msq2}, numerical={numCf_msq}, diff={Math.Abs(cf_msq2 - numCf_msq)}");
    }

    // =====================================================================
    // DIL: Laminar dissipation
    // =====================================================================

    [Fact]
    public void LaminarDissipation_BelowThreshold_ReturnsCorrectValue()
    {
        // HK=2.5, RT=500
        var (di, di_hk, di_rt) = BoundaryLayerCorrelations.LaminarDissipation(2.5, 500.0);

        double expectedDi = (0.00205 * Math.Pow(4.0 - 2.5, 5.5) + 0.207) / 500.0;

        Assert.Equal(expectedDi, di, 10);
    }

    [Fact]
    public void LaminarDissipation_AboveThreshold_ReturnsCorrectValue()
    {
        // HK=5.0 > 4.0
        var (di, _, _) = BoundaryLayerCorrelations.LaminarDissipation(5.0, 500.0);

        double hkb = 5.0 - 4.0;
        double den = 1.0 + 0.02 * hkb * hkb;
        double expectedDi = (-0.0016 * hkb * hkb / den + 0.207) / 500.0;

        Assert.Equal(expectedDi, di, 10);
    }

    [Fact]
    public void LaminarDissipation_JacobianMatchesCentralDifference()
    {
        double hk = 2.5, rt = 500.0;
        var (_, di_hk, di_rt) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);

        // d/dHk
        var (diP, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk + Eps, rt);
        var (diM, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk - Eps, rt);
        double numDi_hk = (diP - diM) / (2.0 * Eps);

        // d/dRt
        (diP, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt + Eps);
        (diM, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt - Eps);
        double numDi_rt = (diP - diM) / (2.0 * Eps);

        Assert.True(Math.Abs(di_hk - numDi_hk) < JacobianTol,
            $"Di_Hk: analytical={di_hk}, numerical={numDi_hk}, diff={Math.Abs(di_hk - numDi_hk)}");
        Assert.True(Math.Abs(di_rt - numDi_rt) < JacobianTol,
            $"Di_Rt: analytical={di_rt}, numerical={numDi_rt}, diff={Math.Abs(di_rt - numDi_rt)}");
    }

    // =====================================================================
    // DILW: Wake dissipation
    // =====================================================================

    [Fact]
    public void WakeDissipation_NominalCase_ReturnsPositiveValue()
    {
        var (di, di_hk, di_rt) = BoundaryLayerCorrelations.WakeDissipation(2.0, 10000.0);

        Assert.True(di > 0.0, $"Wake dissipation should be positive, got {di}");
    }

    [Fact]
    public void WakeDissipation_JacobianMatchesCentralDifference()
    {
        double hk = 2.0, rt = 10000.0;
        var (_, di_hk, di_rt) = BoundaryLayerCorrelations.WakeDissipation(hk, rt);

        // d/dHk
        var (diP, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk + Eps, rt);
        var (diM, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk - Eps, rt);
        double numDi_hk = (diP - diM) / (2.0 * Eps);

        // d/dRt
        (diP, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk, rt + Eps);
        (diM, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk, rt - Eps);
        double numDi_rt = (diP - diM) / (2.0 * Eps);

        Assert.True(Math.Abs(di_hk - numDi_hk) < JacobianTol,
            $"Di_Hk: analytical={di_hk}, numerical={numDi_hk}, diff={Math.Abs(di_hk - numDi_hk)}");
        Assert.True(Math.Abs(di_rt - numDi_rt) < JacobianTol,
            $"Di_Rt: analytical={di_rt}, numerical={numDi_rt}, diff={Math.Abs(di_rt - numDi_rt)}");
    }

    // =====================================================================
    // HCT: Density thickness shape parameter
    // =====================================================================

    [Fact]
    public void DensityThicknessShapeParameter_NominalCase_ReturnsCorrectValue()
    {
        // HK=1.4, MSQ=0.04
        var (hc, hc_hk, hc_msq) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(1.4, 0.04);

        double expectedHc = 0.04 * (0.064 / (1.4 - 0.8) + 0.251);

        Assert.Equal(expectedHc, hc, 10);
    }

    [Fact]
    public void DensityThicknessShapeParameter_ZeroMsq_ReturnsZero()
    {
        var (hc, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(1.4, 0.0);
        Assert.Equal(0.0, hc, 10);
    }

    [Fact]
    public void DensityThicknessShapeParameter_JacobianMatchesCentralDifference()
    {
        double hk = 1.4, msq = 0.04;
        var (_, hc_hk, hc_msq) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk, msq);

        // d/dHk
        var (hcP, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk + Eps, msq);
        var (hcM, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk - Eps, msq);
        double numHc_hk = (hcP - hcM) / (2.0 * Eps);

        // d/dMsq
        (hcP, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk, msq + Eps);
        (hcM, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk, msq - Eps);
        double numHc_msq = (hcP - hcM) / (2.0 * Eps);

        Assert.True(Math.Abs(hc_hk - numHc_hk) < JacobianTol,
            $"Hc_Hk: analytical={hc_hk}, numerical={numHc_hk}, diff={Math.Abs(hc_hk - numHc_hk)}");
        Assert.True(Math.Abs(hc_msq - numHc_msq) < JacobianTol,
            $"Hc_Msq: analytical={hc_msq}, numerical={numHc_msq}, diff={Math.Abs(hc_msq - numHc_msq)}");
    }

    // =====================================================================
    // EquilibriumShearCoefficient
    // =====================================================================

    [Fact]
    public void EquilibriumShearCoefficient_NominalCase_ReturnsPositiveValue()
    {
        // Typical turbulent BL conditions
        var (cteq, cteq_hk, cteq_us, cteq_hs, cteq_h) =
            BoundaryLayerCorrelations.EquilibriumShearCoefficient(1.4, 1.8, 0.5, 2.5);

        Assert.True(cteq > 0.0, $"Equilibrium Ctau should be positive, got {cteq}");
    }

    [Fact]
    public void EquilibriumShearCoefficient_JacobianMatchesCentralDifference()
    {
        double hk = 1.4, hs = 1.8, us = 0.5, h = 2.5;
        var (_, cteq_hk, cteq_us, cteq_hs, cteq_h) =
            BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs, us, h);

        // d/dHk
        var (ctP, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk + Eps, hs, us, h);
        var (ctM, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk - Eps, hs, us, h);
        double numCteq_hk = (ctP - ctM) / (2.0 * Eps);

        // d/dHs
        (ctP, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs + Eps, us, h);
        (ctM, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs - Eps, us, h);
        double numCteq_hs = (ctP - ctM) / (2.0 * Eps);

        // d/dUs
        (ctP, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs, us + Eps, h);
        (ctM, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs, us - Eps, h);
        double numCteq_us = (ctP - ctM) / (2.0 * Eps);

        // d/dH
        (ctP, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs, us, h + Eps);
        (ctM, _, _, _, _) = BoundaryLayerCorrelations.EquilibriumShearCoefficient(hk, hs, us, h - Eps);
        double numCteq_h = (ctP - ctM) / (2.0 * Eps);

        Assert.True(Math.Abs(cteq_hk - numCteq_hk) < JacobianTol,
            $"Cteq_Hk: analytical={cteq_hk}, numerical={numCteq_hk}, diff={Math.Abs(cteq_hk - numCteq_hk)}");
        Assert.True(Math.Abs(cteq_hs - numCteq_hs) < JacobianTol,
            $"Cteq_Hs: analytical={cteq_hs}, numerical={numCteq_hs}, diff={Math.Abs(cteq_hs - numCteq_hs)}");
        Assert.True(Math.Abs(cteq_us - numCteq_us) < JacobianTol,
            $"Cteq_Us: analytical={cteq_us}, numerical={numCteq_us}, diff={Math.Abs(cteq_us - numCteq_us)}");
        Assert.True(Math.Abs(cteq_h - numCteq_h) < JacobianTol,
            $"Cteq_H: analytical={cteq_h}, numerical={numCteq_h}, diff={Math.Abs(cteq_h - numCteq_h)}");
    }

    // =====================================================================
    // TurbulentDissipation (DIT)
    // =====================================================================

    [Fact]
    public void TurbulentDissipation_NominalCase_ReturnsPositiveValue()
    {
        var (di, di_hs, di_us, di_cf, di_st) =
            BoundaryLayerCorrelations.TurbulentDissipation(1.8, 0.5, 0.003, 0.1);

        Assert.True(di > 0.0, $"Turbulent dissipation should be positive, got {di}");
    }

    [Fact]
    public void TurbulentDissipation_MatchesFortranFormula()
    {
        double hs = 1.8, us = 0.5, cf = 0.003, st = 0.1;

        var (di, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf, st);

        double expectedDi = (0.5 * cf * us + st * st * (1.0 - us)) * 2.0 / hs;

        Assert.Equal(expectedDi, di, 10);
    }

    [Fact]
    public void TurbulentDissipation_JacobianMatchesCentralDifference()
    {
        double hs = 1.8, us = 0.5, cf = 0.003, st = 0.1;
        var (_, di_hs, di_us, di_cf, di_st) =
            BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf, st);

        // d/dHs
        var (diP, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs + Eps, us, cf, st);
        var (diM, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs - Eps, us, cf, st);
        double numDi_hs = (diP - diM) / (2.0 * Eps);

        // d/dUs
        (diP, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us + Eps, cf, st);
        (diM, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us - Eps, cf, st);
        double numDi_us = (diP - diM) / (2.0 * Eps);

        // d/dCf
        (diP, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf + Eps, st);
        (diM, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf - Eps, st);
        double numDi_cf = (diP - diM) / (2.0 * Eps);

        // d/dSt
        (diP, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf, st + Eps);
        (diM, _, _, _, _) = BoundaryLayerCorrelations.TurbulentDissipation(hs, us, cf, st - Eps);
        double numDi_st = (diP - diM) / (2.0 * Eps);

        Assert.True(Math.Abs(di_hs - numDi_hs) < JacobianTol,
            $"Di_Hs: analytical={di_hs}, numerical={numDi_hs}");
        Assert.True(Math.Abs(di_us - numDi_us) < JacobianTol,
            $"Di_Us: analytical={di_us}, numerical={numDi_us}");
        Assert.True(Math.Abs(di_cf - numDi_cf) < JacobianTol,
            $"Di_Cf: analytical={di_cf}, numerical={numDi_cf}");
        Assert.True(Math.Abs(di_st - numDi_st) < JacobianTol,
            $"Di_St: analytical={di_st}, numerical={numDi_st}");
    }
}

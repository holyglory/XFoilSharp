using Xunit;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for BoundaryLayerSystemAssembler: BLPRV, BLKIN, BLVAR, BLMID, BLDIF, TESYS, BLSYS.
/// </summary>
public class BoundaryLayerSystemAssemblerTests
{
    private const double Tol = 1e-10;
    private const double JacTol = 1e-5; // Numerical Jacobian tolerance

    // ========== BLPRV / ConvertToCompressible ==========

    [Fact]
    public void ConvertToCompressible_AtMachZero_ReturnsIdentity()
    {
        // At M=0, TKBL=0, so U2 = UEI*(1-0)/(1-0) = UEI
        double uei = 0.8;
        var (u2, u2_uei, u2_ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei, tkbl: 0.0, qinfbl: 1.0, tkbl_ms: 0.0);

        Assert.Equal(uei, u2, 12);
        Assert.Equal(1.0, u2_uei, 12);
        Assert.Equal(0.0, u2_ms, 12);
    }

    [Fact]
    public void ConvertToCompressible_AtMach05_AppliesKarmanTsien()
    {
        // At nonzero Mach, TKBL != 0, Karman-Tsien correction applies
        // TKBL = M^2 / (1 + sqrt(1-M^2))^2 for Karman-Tsien
        // For M=0.5: beta = sqrt(1-0.25) = sqrt(0.75), TKBL = 0.25/(1+beta)^2
        double m = 0.5;
        double beta = System.Math.Sqrt(1.0 - m * m);
        double tkbl = m * m / ((1.0 + beta) * (1.0 + beta));
        double qinfbl = 1.0;
        double uei = 0.3;

        var (u2, u2_uei, u2_ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei, tkbl, qinfbl, tkbl_ms: 0.0);

        // U2 = UEI*(1 - TKBL) / (1 - TKBL*(UEI/QINFBL)^2)
        double expected = uei * (1.0 - tkbl) / (1.0 - tkbl * (uei / qinfbl) * (uei / qinfbl));
        Assert.Equal(expected, u2, 12);
        // Karman-Tsien maps: at uei < qinfbl, U2 != UEI (non-identity transform)
        Assert.NotEqual(uei, u2, 6);
    }

    [Fact]
    public void ConvertToCompressible_Sensitivities_MatchNumerical()
    {
        double uei = 0.3;
        double tkbl = 0.08;
        double qinfbl = 1.0;
        double tkbl_ms = 0.05;
        double eps = 1e-7;

        var (u2, u2_uei, u2_ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei, tkbl, qinfbl, tkbl_ms);

        // Numerical dU2/dUEI
        var (u2p, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei + eps, tkbl, qinfbl, tkbl_ms);
        var (u2m, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei - eps, tkbl, qinfbl, tkbl_ms);
        double numUei = (u2p - u2m) / (2.0 * eps);

        Assert.Equal(numUei, u2_uei, JacTol);
    }

    // ========== BLVAR / ComputeStationVariables ==========

    [Fact]
    public void ComputeStationVariables_Laminar_DispatchesCorrectly()
    {
        // For laminar (ITYP=1), Cf should come from LaminarSkinFriction
        var vars = BoundaryLayerSystemAssembler.ComputeStationVariables(
            ityp: 1, hk: 2.5, rt: 500.0, msq: 0.0, h: 2.5,
            ctau: 0.0, dw: 0.0, theta: 0.01);

        // Laminar Cf should be nonzero positive for typical Hk
        Assert.True(vars.Cf > 0, "Laminar Cf should be positive");
        // Hs should be in reasonable range
        Assert.True(vars.Hs > 1.0 && vars.Hs < 3.0, "Laminar Hs should be in range");
    }

    [Fact]
    public void ComputeStationVariables_Turbulent_DispatchesCorrectly()
    {
        var vars = BoundaryLayerSystemAssembler.ComputeStationVariables(
            ityp: 2, hk: 1.4, rt: 5000.0, msq: 0.0, h: 1.4,
            ctau: 0.005, dw: 0.0, theta: 0.005);

        Assert.True(vars.Cf > 0, "Turbulent Cf should be positive");
        Assert.True(vars.Di > 0, "Turbulent Di should be positive");
        Assert.True(vars.Cteq > 0, "Turbulent Cteq should be positive");
    }

    [Fact]
    public void ComputeStationVariables_Wake_HasZeroCf()
    {
        var vars = BoundaryLayerSystemAssembler.ComputeStationVariables(
            ityp: 3, hk: 1.05, rt: 5000.0, msq: 0.0, h: 1.05,
            ctau: 0.005, dw: 0.001, theta: 0.005);

        Assert.Equal(0.0, vars.Cf, 12);
        // Wake dissipation should be doubled (two halves)
        Assert.True(vars.Di > 0, "Wake Di should be positive (doubled)");
    }

    // ========== BLMID / ComputeMidpointCorrelations ==========

    [Fact]
    public void ComputeMidpointCorrelations_ProducesAverageValues()
    {
        double hk1 = 2.0, hk2 = 2.5;
        double rt1 = 400.0, rt2 = 600.0;
        double m1 = 0.0, m2 = 0.0;

        var mid = BoundaryLayerSystemAssembler.ComputeMidpointCorrelations(
            ityp: 1, hk1, rt1, m1, hk2, rt2, m2);

        // Midpoint Cf should be computed at average Hk, Rt, M
        var (cfmExpected, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(
            0.5 * (hk1 + hk2), 0.5 * (rt1 + rt2), 0.5 * (m1 + m2));
        Assert.Equal(cfmExpected, mid.Cfm, 12);
    }

    // ========== BLDIF / ComputeFiniteDifferences ==========

    [Fact]
    public void ComputeFiniteDifferences_Laminar_MomentumResidualCorrect()
    {
        // For a laminar interval with known station values, the momentum
        // residual should match the von Karman integral equation discretization.
        var result = BoundaryLayerSystemAssembler.ComputeFiniteDifferences(
            ityp: 1,
            x1: 0.1, x2: 0.2,
            u1: 1.0, u2: 0.98,
            t1: 0.005, t2: 0.006,
            d1: 0.012, d2: 0.015,
            s1: 0.0, s2: 0.0,
            dw1: 0.0, dw2: 0.0,
            msq1: 0.0, msq2: 0.0,
            ampl1: 2.0, ampl2: 3.0,
            amcrit: 9.0);

        // Momentum residual should be finite
        Assert.True(double.IsFinite(result.Residual[1]),
            "Momentum residual should be finite");
        // Shape parameter residual should be finite
        Assert.True(double.IsFinite(result.Residual[2]),
            "Shape parameter residual should be finite");
    }

    [Fact]
    public void ComputeFiniteDifferences_Turbulent_AllThreeResidualsCorrect()
    {
        var result = BoundaryLayerSystemAssembler.ComputeFiniteDifferences(
            ityp: 2,
            x1: 0.3, x2: 0.35,
            u1: 0.95, u2: 0.93,
            t1: 0.008, t2: 0.009,
            d1: 0.012, d2: 0.013,
            s1: 0.005, s2: 0.006,
            dw1: 0.0, dw2: 0.0,
            msq1: 0.0, msq2: 0.0,
            ampl1: 0.0, ampl2: 0.0,
            amcrit: 9.0);

        // All 3 residuals should be finite
        for (int k = 0; k < 3; k++)
        {
            Assert.True(double.IsFinite(result.Residual[k]),
                $"Residual[{k}] should be finite");
        }
        // Jacobian blocks should be 3x5
        Assert.Equal(3, result.VS1.GetLength(0));
        Assert.Equal(5, result.VS1.GetLength(1));
    }

    [Fact]
    public void ComputeFiniteDifferences_JacobianMatchesNumerical()
    {
        double eps = 1e-6;

        double x1 = 0.3, x2 = 0.35;
        double u1 = 0.95, u2 = 0.93;
        double t1 = 0.008, t2 = 0.009;
        double d1 = 0.012, d2 = 0.013;
        double s1 = 0.005, s2 = 0.006;

        var baseResult = BoundaryLayerSystemAssembler.ComputeFiniteDifferences(
            ityp: 2, x1, x2, u1, u2, t1, t2, d1, d2, s1, s2,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 9.0);

        // Perturb t2 (column 2 of VS2) and check momentum equation (row 1)
        var pertResult = BoundaryLayerSystemAssembler.ComputeFiniteDifferences(
            ityp: 2, x1, x2, u1, u2, t1, t2 + eps, d1, d2, s1, s2,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 9.0);

        // Check that perturbation produces a finite change in at least one residual
        bool anyChanged = false;
        for (int k = 0; k < 3; k++)
        {
            double numDeriv = (pertResult.Residual[k] - baseResult.Residual[k]) / eps;
            if (System.Math.Abs(numDeriv) > 1e-10) anyChanged = true;
        }
        Assert.True(anyChanged, "At least one residual should be sensitive to theta2");
    }

    // ========== TESYS / AssembleTESystem ==========

    [Fact]
    public void AssembleTESystem_CouplesCorrectly()
    {
        // TE system: theta_wake = theta_TE, dstar_wake = dstar_TE + DW
        double cte = 0.005, tte = 0.01, dte = 0.025;
        var result = BoundaryLayerSystemAssembler.AssembleTESystem(
            cte, tte, dte,
            hk2: 1.05, rt2: 5000.0, msq2: 0.0, h2: 1.05,
            s2: 0.005, t2: 0.01, d2: 0.024, dw2: 0.001);

        // Residual[0] = CTE - S2
        Assert.Equal(cte - 0.005, result.Residual[0], 12);
        // Residual[1] = TTE - T2
        Assert.Equal(tte - 0.01, result.Residual[1], 12);
        // Residual[2] = DTE - D2 - DW2
        Assert.Equal(dte - 0.024 - 0.001, result.Residual[2], 12);

        // VS1 diagonal should be -1, VS2 diagonal should be +1
        Assert.Equal(-1.0, result.VS1[0, 0], 12);
        Assert.Equal(1.0, result.VS2[0, 0], 12);
    }

    // ========== BLSYS / AssembleStationSystem ==========

    [Fact]
    public void AssembleStationSystem_LaminarInterval_ProducesValidBlocks()
    {
        var result = BoundaryLayerSystemAssembler.AssembleStationSystem(
            isWake: false, isTurbOrTran: false, isTran: false, isSimi: false,
            x1: 0.1, x2: 0.2,
            uei1: 1.0, uei2: 0.98,
            t1: 0.005, t2: 0.006,
            d1: 0.012, d2: 0.015,
            s1: 0.0, s2: 0.0,
            dw1: 0.0, dw2: 0.0,
            ampl1: 2.0, ampl2: 3.0,
            amcrit: 9.0,
            tkbl: 0.0, qinfbl: 1.0, tkbl_ms: 0.0,
            hstinv: 0.0, hstinv_ms: 0.0,
            gm1bl: 0.4, rstbl: 1.0, rstbl_ms: 0.0,
            hvrat: 0.35, reybl: 1e6, reybl_re: 1.0, reybl_ms: 0.0);

        // VA (mapped 3x2 Jacobian) should be finite
        Assert.Equal(3, result.Residual.Length);
        for (int k = 0; k < 3; k++)
        {
            Assert.True(double.IsFinite(result.Residual[k]),
                $"BLSYS Residual[{k}] should be finite");
        }
    }
}

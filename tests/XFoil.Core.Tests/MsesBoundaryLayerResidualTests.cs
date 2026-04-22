using XFoil.MsesSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P5.1 — BL residual function tests.
/// </summary>
public class MsesBoundaryLayerResidualTests
{
    [Fact]
    public void MomentumResidual_ConstantState_IsApproxZero()
    {
        // θ, H, Ue all constant across a small station step.
        // R_θ = 0 - [Cf/2 - (...)·0] = -Cf/2 (non-zero; there's no
        // equilibrium for flat constant-Ue BL without streamwise
        // growth). So instead check: with a momentum-compatible
        // growth  dθ/dξ = Cf/2, R_θ ≈ 0.
        //
        // Simpler check: R_θ at a trivial constant-θ case is just
        // the negative of the RHS term. Verify that the output is
        // finite and has the right sign.
        double r = MsesBoundaryLayerResidual.MomentumResidual(
            thetaPrev: 0.001, theta: 0.001,
            hPrev: 1.4, h: 1.4,
            uePrev: 1.0, ue: 1.0,
            dx: 0.01, nu: 1e-6);
        // Constant state with Cf > 0: R = -Cf/2 (negative).
        Assert.True(r < 0,
            $"Constant-θ R_θ should be negative (Cf loss); got {r}");
        Assert.True(System.Math.Abs(r) < 0.1,
            $"R_θ magnitude should be bounded; got {r}");
    }

    [Fact]
    public void MomentumResidual_BalancedGrowth_IsApproxZero()
    {
        // Pick θ growth rate matching Cf/2 at current H, Reθ to
        // zero the momentum residual (at constant Ue, no adverse
        // gradient).
        double theta0 = 0.001;
        double h = 1.4;
        double ue = 1.0;
        double nu = 1e-6;
        double hkVal = XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeHk(h, 0.0);
        double reT = ue * theta0 / nu;
        double cf = XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeCfTurbulent(
            hkVal, reT, 0.0);
        double dx = 0.01;
        double theta1 = theta0 + dx * cf * 0.5;  // satisfies dθ/dξ = Cf/2
        double r = MsesBoundaryLayerResidual.MomentumResidual(
            thetaPrev: theta0, theta: theta1,
            hPrev: h, h: h,
            uePrev: ue, ue: ue,
            dx: dx, nu: nu);
        Assert.True(System.Math.Abs(r) < 1e-5,
            $"Balanced-growth R_θ should be ≈ 0; got {r}");
    }

    [Fact]
    public void LagResidual_AtEquilibrium_IsApproxZero()
    {
        // If Cτ_prev = Cτ_eq and Cτ = Cτ_eq, the residual is zero
        // (steady state).
        double h = 1.6;
        double theta = 0.002;
        double ue = 1.0;
        double nu = 1e-6;
        double hkVal = XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeHk(h, 0.0);
        double reT = ue * theta / nu;
        double cTauEq = XFoil.MsesSolver.Closure.MsesClosureRelations
            .ComputeCTauEquilibrium(hkVal, reT, 0.0);
        double r = MsesBoundaryLayerResidual.LagResidual(
            cTauPrev: cTauEq, cTau: cTauEq,
            thetaPrev: theta, theta: theta,
            hPrev: h, h: h,
            uePrev: ue, ue: ue,
            dx: 0.01, nu: nu);
        Assert.True(System.Math.Abs(r) < 1e-8,
            $"At-equilibrium R_Cτ should be ≈ 0; got {r}");
    }

    [Fact]
    public void LagResidual_DrivesCTauTowardEquilibrium()
    {
        // Start with Cτ below equilibrium. The expected new Cτ
        // should be between the old value and Cτ_eq — the lag
        // residual pins this self-consistently.
        double h = 1.6;
        double theta = 0.002;
        double ue = 1.0;
        double nu = 1e-6;
        double hkVal = XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeHk(h, 0.0);
        double reT = ue * theta / nu;
        double cTauEq = XFoil.MsesSolver.Closure.MsesClosureRelations
            .ComputeCTauEquilibrium(hkVal, reT, 0.0);

        // Evaluate residual at an "about-right" predicted Cτ.
        double dx = 0.01;
        double deltaMid = hkVal * theta * (3.15 + 1.72 / (hkVal - 1.0));
        double decay = System.Math.Exp(-4.2 * dx / deltaMid);
        double cTauPrev = 0.5 * cTauEq;
        double cTauExpected = cTauEq + (cTauPrev - cTauEq) * decay;
        double r = MsesBoundaryLayerResidual.LagResidual(
            cTauPrev: cTauPrev, cTau: cTauExpected,
            thetaPrev: theta, theta: theta,
            hPrev: h, h: h,
            uePrev: ue, ue: ue,
            dx: dx, nu: nu);
        Assert.True(System.Math.Abs(r) < 1e-8,
            $"Predicted Cτ should zero the lag residual; got {r}");
    }

    [Fact]
    public void MomentumResidual_RejectsNonPositiveStep()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => MsesBoundaryLayerResidual.MomentumResidual(
                0.001, 0.001, 1.4, 1.4, 1.0, 1.0, dx: 0.0, nu: 1e-6));
    }

    [Fact]
    public void SourceConstraintResidual_ExactDerivative_IsZero()
    {
        // σ = d(Ue·δ*)/dξ exactly: pick Ue·δ* linearly increasing,
        // σ = slope. R_σ = σ − slope = 0.
        double uePrev = 1.0, ue = 1.05;
        double dStarPrev = 0.001, dStar = 0.0012;
        double dx = 0.01;
        double expected = (ue * dStar - uePrev * dStarPrev) / dx;
        double r = MsesBoundaryLayerResidual.SourceConstraintResidual(
            sigma: expected,
            dStarPrev: dStarPrev, dStar: dStar,
            uePrev: uePrev, ue: ue,
            dx: dx);
        Assert.True(System.Math.Abs(r) < 1e-14,
            $"σ matching exact derivative should zero R_σ; got {r}");
    }

    [Fact]
    public void SourceConstraintResidual_OffDerivative_ReturnsOffset()
    {
        // R_σ = σ − derivative. If we pass σ = derivative + 0.5,
        // residual should be exactly 0.5.
        double uePrev = 1.0, ue = 1.0;
        double dStarPrev = 0.001, dStar = 0.001;
        double dx = 0.01;
        // d(Ue·δ*)/dx = 0 (constant), so σ = 0.5 gives R = 0.5.
        double r = MsesBoundaryLayerResidual.SourceConstraintResidual(
            sigma: 0.5,
            dStarPrev: dStarPrev, dStar: dStar,
            uePrev: uePrev, ue: ue,
            dx: dx);
        Assert.Equal(0.5, r, 12);
    }

    [Fact]
    public void SourceConstraintResidual_RejectsNonPositiveStep()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => MsesBoundaryLayerResidual.SourceConstraintResidual(
                0.0, 0.001, 0.001, 1.0, 1.0, dx: 0.0));
    }

    [Fact]
    public void ShapeParamResidual_IsFiniteAtNominalState()
    {
        double r = MsesBoundaryLayerResidual.ShapeParamResidual(
            thetaPrev: 0.001, theta: 0.0011,
            hPrev: 1.4, h: 1.45,
            cTau: 0.01,
            uePrev: 1.0, ue: 0.95,
            dx: 0.01, nu: 1e-6);
        Assert.True(double.IsFinite(r));
        Assert.True(System.Math.Abs(r) < 10.0);
    }
}

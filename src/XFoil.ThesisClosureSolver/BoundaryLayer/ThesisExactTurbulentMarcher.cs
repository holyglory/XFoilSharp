using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.ThesisClosureSolver.BoundaryLayer;

/// <summary>
/// Phase-2e turbulent marcher using Drela thesis eqs. 6.9 + 6.10
/// with Cτ carried via the 6.35 lag ODE (Phase-2d-style analytical
/// per-step decay). Equivalent to <see cref="ThesisExactLaminarMarcher"/>
/// but driving the full turbulent closure (Cf_turbulent, CD_turbulent,
/// H*_turbulent with Cτ coupling) and Cτ as a third state.
///
/// Replaces the Clauser-relaxation H-ODE placeholder used by
/// <see cref="ClosureBasedTurbulentLagMarcher"/>. The thesis
/// shape-parameter equation
///   θ·dH*/dξ = 2·CD − H*·Cf/2 − bracket(H)·θ·(dUe/dξ)/Ue,
///   bracket(H) = 2·H**/H* + (1 − H),
/// is near-equilibrium on flat plates (2·CD balances H*·Cf/2), so
/// explicit RK2 oscillates. Backward-Euler absorbs the stiffness:
///   θ·(H*(H,Cτ) − H*ⁿ)/dx = rhs(H, Cτ, θ, Ue, dUe/dξ)
/// solved by Newton on H with the previous-station H as guess.
///
/// Momentum (eq. 6.9) integrated explicitly via midpoint (non-stiff).
/// Cτ relaxed per-step by closed-form exponential decay using K2=4.2
/// and δ = H·θ·(3.15 + 1.72/(Hk−1)) (thesis eq. 6.36).
/// </summary>
public static class ThesisExactTurbulentMarcher
{
    public readonly record struct MarchResult(
        double[] Theta,
        double[] H,
        double[] HStar,
        double[] CTau);

    /// <summary>
    /// Marches (θ, H, Cτ) with the implicit shape-parameter equation.
    /// </summary>
    /// <param name="stations">Stations (ascending).</param>
    /// <param name="edgeVelocity">Ue at each station.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="theta0">Initial θ at stations[0].</param>
    /// <param name="h0">Initial H at stations[0].</param>
    /// <param name="cTau0">Initial Cτ at stations[0]. Pass Cτ_eq to
    /// start in equilibrium; pass 0.3·Cτ_eq for a just-transitioned
    /// BL per Drela's MSES practice.</param>
    /// <param name="machNumberEdge">Optional edge Mach.</param>
    public static MarchResult March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
        double theta0,
        double h0,
        double cTau0,
        double machNumberEdge = 0.0)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (edgeVelocity is null) throw new System.ArgumentNullException(nameof(edgeVelocity));
        if (stations.Length != edgeVelocity.Length)
            throw new System.ArgumentException("length mismatch");
        if (stations.Length < 2)
            throw new System.ArgumentException("need ≥2 stations");

        int n = stations.Length;
        var theta = new double[n];
        var H = new double[n];
        var HStar = new double[n];
        var cTau = new double[n];

        theta[0] = theta0;
        H[0] = h0;
        cTau[0] = cTau0;
        double nuSafe = System.Math.Max(kinematicViscosity, 1e-18);
        double reT00 = edgeVelocity[0] * theta0 / nuSafe;
        double Hk0 = ThesisClosureRelations.ComputeHk(h0, machNumberEdge);
        HStar[0] = ThesisClosureRelations.ComputeHStarTurbulent(Hk0, reT00, machNumberEdge);

        for (int i = 1; i < n; i++)
        {
            double dx = stations[i] - stations[i - 1];
            if (dx <= 0.0)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                HStar[i] = HStar[i - 1];
                cTau[i] = cTau[i - 1];
                continue;
            }

            double ue0 = edgeVelocity[i - 1];
            double ue1 = edgeVelocity[i];
            double ueMid = 0.5 * (ue0 + ue1);
            double dUeDx = (ue1 - ue0) / dx;
            if (ue1 < 1e-12)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                HStar[i] = HStar[i - 1];
                cTau[i] = cTau[i - 1];
                continue;
            }

            // Momentum (explicit midpoint). With H and Cτ treated as
            // known at i-1 this stays non-stiff.
            double HkPrev = ThesisClosureRelations.ComputeHk(H[i - 1], machNumberEdge);
            double ReTPrev = ue0 * theta[i - 1] / nuSafe;
            double CfPrev = ThesisClosureRelations.ComputeCfTurbulent(
                HkPrev, ReTPrev, machNumberEdge);
            double uDuDx_over_Ue = dUeDx / System.Math.Max(ue0, 1e-12);
            double dθ0 = 0.5 * CfPrev
                - (H[i - 1] + 2.0 - machNumberEdge * machNumberEdge) * theta[i - 1] * uDuDx_over_Ue;
            double thetaNew = System.Math.Max(theta[i - 1] + dx * dθ0, 1e-18);

            // Runaway guard: θ shouldn't grow more than 3× in one step.
            if (thetaNew > theta[i - 1] * 3.0)
            {
                thetaNew = theta[i - 1] * 3.0;
            }

            // Cτ lag: closed-form exponential decay over dx using
            // midpoint Cτ_eq/δ (thesis eq. 6.35 + 6.36, K2 = 4.2).
            // Using the average of "start" and "tentative-new" state.
            double thetaMidCτ = 0.5 * (theta[i - 1] + thetaNew);
            double HkMidCτ = ThesisClosureRelations.ComputeHk(H[i - 1], machNumberEdge);
            double ReTMidCτ = ueMid * thetaMidCτ / nuSafe;
            double cTauEqMid = ThesisClosureRelations.ComputeCTauEquilibrium(
                HkMidCτ, ReTMidCτ, machNumberEdge);
            double cTauNew = cTau[i - 1];
            if (HkMidCτ > 1.0)
            {
                double deltaMid = HkMidCτ * thetaMidCτ * (3.15 + 1.72 / (HkMidCτ - 1.0));
                const double K2 = 4.2;
                double decay = System.Math.Exp(-K2 * dx / System.Math.Max(deltaMid, 1e-18));
                cTauNew = System.Math.Max(cTauEqMid + (cTau[i - 1] - cTauEqMid) * decay, 0.0);
            }

            // Implicit Newton solve for H such that
            //   θ·(H*(H,Cτ) − H*ⁿ)/dx = 2·CD − H*·Cf/2 − bracket·θ·(dUe/dξ)/Ue.
            double Hsolve = NewtonSolveH(
                H[i - 1], HStar[i - 1], cTauNew,
                thetaNew, ue1, dUeDx, dx,
                kinematicViscosity, machNumberEdge);
            H[i] = Hsolve;

            double HkSolve = ThesisClosureRelations.ComputeHk(Hsolve, machNumberEdge);
            double ReTSolve = ue1 * thetaNew / nuSafe;
            HStar[i] = ThesisClosureRelations.ComputeHStarTurbulent(
                HkSolve, ReTSolve, machNumberEdge);
            theta[i] = thetaNew;
            cTau[i] = cTauNew;
        }

        return new MarchResult(theta, H, HStar, cTau);
    }

    // Newton iteration on H at station i. Residual zeros when the
    // discrete backward-Euler form of the shape-parameter equation
    // (thesis eq. 6.10) is satisfied:
    //   θ·(H*(H,Cτ) − H*ⁿ)/dx = 2·CD(H,Cτ) − H*(H,Cτ)·Cf(H)/2
    //                           − [2·H**/H* + (1 − H)]·θ·(dUe/dξ)/Ue.
    // Finite-difference Jacobian because the turbulent closures are
    // piecewise (H* has the Hk=H0 junction) and analytic dR/dH would
    // need per-branch logic. Converges in 4-7 Newton iters with the
    // previous-station H as starting guess.
    private static double NewtonSolveH(
        double HInit, double HStarPrev, double cTau,
        double theta, double Ue, double dUeDx, double dx,
        double nu, double Me)
    {
        double Me2 = Me * Me;
        double uDuDx_over_Ue = dUeDx / System.Math.Max(Ue, 1e-12);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);

        double Residual(double h)
        {
            double Hk = ThesisClosureRelations.ComputeHk(h, Me);
            double HStar = ThesisClosureRelations.ComputeHStarTurbulent(Hk, ReTheta, Me);
            double Cf = ThesisClosureRelations.ComputeCfTurbulent(Hk, ReTheta, Me);
            double CD = ThesisClosureRelations.ComputeCDTurbulent(Hk, ReTheta, Me, cTau);
            // H** compressibility term (thesis §6.1.3). At Me=0 the
            // H** contribution vanishes — the turbulent BL's density
            // term is the compressibility-corrected one only.
            double HDoubleStar = Me > 0.0
                ? (0.064 / System.Math.Max(Hk - 0.8, 0.1) + 0.251) * Me2
                : 0.0;
            double bracket = 2.0 * HDoubleStar / System.Math.Max(HStar, 1e-9) + (1.0 - h);
            double lhs = theta * (HStar - HStarPrev) / dx;
            double rhs = 2.0 * CD - 0.5 * HStar * Cf - bracket * theta * uDuDx_over_Ue;
            return lhs - rhs;
        }

        // Turbulent H valid range ≈ [1.05, 4.0] attached + mildly
        // separated. Above Hk≈4 the BL is deep-separated and the
        // H* correlation enters the separated branch; above 6 the
        // physical meaning degrades. Clamp to [1.05, 6.0] to stay
        // in closure-valid territory.
        double H = System.Math.Clamp(HInit, 1.05, 6.0);
        const double eps = 1e-4;
        const double tol = 1e-8;
        for (int it = 0; it < 25; it++)
        {
            double r = Residual(H);
            if (System.Math.Abs(r) < tol) return H;
            double rPlus = Residual(System.Math.Min(H + eps, 6.0 - eps));
            double rMinus = Residual(System.Math.Max(H - eps, 1.05 + eps));
            double dR = (rPlus - rMinus) / (2.0 * eps);
            if (System.Math.Abs(dR) < 1e-14) break;
            double step = -r / dR;
            // Damp to avoid overshoot on the stiff side (H*·Cf/2 balances
            // 2·CD near equilibrium so dR/dH can be small).
            const double maxStep = 0.3;
            if (step > maxStep) step = maxStep;
            if (step < -maxStep) step = -maxStep;
            H = System.Math.Clamp(H + step, 1.05, 6.0);
        }
        return H;
    }
}

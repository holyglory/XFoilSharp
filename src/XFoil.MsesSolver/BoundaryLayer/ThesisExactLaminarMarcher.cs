using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// Phase-2e laminar marcher using Drela thesis eqs. 6.9 + 6.10.
/// Unlike an earlier probe (reverted) which integrated the
/// shape-parameter equation explicitly via RK2 and oscillated, this
/// implementation solves the shape-parameter equation implicitly at
/// each station via Newton iteration on H.
///
/// The shape-parameter equation (thesis eq. 6.10):
///   θ·dH*/dξ = 2·CD − H*·Cf/2
///              − [2·H**/H* + (1 − H)]·θ·(dUe/dξ)/Ue
/// is stiff near flat-plate equilibrium (2·CD ≈ H*·Cf/2), so
/// explicit integration amplifies small H perturbations. Backward-
/// Euler linearization absorbs the stiffness:
///   θ·(H*(H) − H*ⁿ)/dx
///     = 2·CD(H) − H*(H)·Cf(H)/2 − bracket(H)·θ·(dUe/dξ)/Ue
/// One equation in one unknown (H), solved by Newton with the
/// previous-station H as the initial guess. Momentum (eq. 6.9)
/// integrated explicitly via trapezoid (non-stiff).
/// </summary>
public static class ThesisExactLaminarMarcher
{
    public readonly record struct MarchResult(double[] Theta, double[] H, double[] HStar);

    public static MarchResult March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
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

        theta[0] = 0.0;
        H[0] = 2.59;
        HStar[0] = MsesClosureRelations.ComputeHStarLaminar(2.59, 1.0);

        int startIdx = 1;
        if (stations[0] > 0.0 && edgeVelocity[0] > 1e-12)
        {
            theta[0] = 0.664 * System.Math.Sqrt(kinematicViscosity * stations[0] / edgeVelocity[0]);
        }
        else if (n >= 2 && stations[1] > 0.0 && edgeVelocity[1] > 1e-12)
        {
            theta[1] = 0.664 * System.Math.Sqrt(kinematicViscosity * stations[1] / edgeVelocity[1]);
            H[1] = 2.59;
            HStar[1] = MsesClosureRelations.ComputeHStarLaminar(2.59, 1.0);
            startIdx = 2;
        }

        for (int i = startIdx; i < n; i++)
        {
            double dx = stations[i] - stations[i - 1];
            if (dx <= 0.0)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                HStar[i] = HStar[i - 1];
                continue;
            }

            double ue0 = edgeVelocity[i - 1];
            double ue1 = edgeVelocity[i];
            double dUeDx = (ue1 - ue0) / dx;
            if (ue1 < 1e-12)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                HStar[i] = HStar[i - 1];
                continue;
            }

            // Momentum (explicit trapezoid). With H treated as known
            // at station i-1, this is decoupled from the implicit H
            // solve and stays non-stiff.
            double Hk0 = MsesClosureRelations.ComputeHk(H[i - 1], machNumberEdge);
            double ReT0 = ue0 * theta[i - 1] / System.Math.Max(kinematicViscosity, 1e-18);
            double Cf0 = MsesClosureRelations.ComputeCfLaminar(Hk0, ReT0);
            double Me2 = machNumberEdge * machNumberEdge;
            double dθ0 = 0.5 * Cf0 - (H[i - 1] + 2.0 - Me2) * theta[i - 1] * dUeDx / ue0;
            double thetaNew = System.Math.Max(theta[i - 1] + dx * dθ0, 1e-18);

            // Implicit Newton solve: find H such that
            //   θ·(H*(H) − H*ⁿ)/dx = rhs(H, thetaNew, ue1, dUeDx).
            double H_guess = H[i - 1];
            double Hsolve = NewtonSolveH(
                H_guess, H[i - 1], HStar[i - 1],
                thetaNew, ue1, dUeDx, dx,
                kinematicViscosity, machNumberEdge);

            H[i] = Hsolve;
            double HkSolve = MsesClosureRelations.ComputeHk(Hsolve, machNumberEdge);
            double ReTSolve = ue1 * thetaNew / System.Math.Max(kinematicViscosity, 1e-18);
            HStar[i] = MsesClosureRelations.ComputeHStarLaminar(HkSolve, ReTSolve);
            theta[i] = thetaNew;
        }

        return new MarchResult(theta, H, HStar);
    }

    /// <summary>
    /// Newton iteration for the implicit shape-parameter equation at
    /// station i, returning H[i]. Residual zeros when
    ///   θ·(H*(H) − H*ⁿ)/dx = 2·CD(H) − H*(H)·Cf(H)/2
    ///                        − [2·H**/H* + (1 − H)]·θ·(dUe/dξ)/Ue.
    /// Uses finite-difference Jacobian (dR/dH) because the closures
    /// are piecewise with derivative jumps at Hk=4; analytic dR/dH
    /// would need per-branch logic. Newton converges in 3-5 iters
    /// with prev-station H as starting guess.
    /// </summary>
    private static double NewtonSolveH(
        double HInit, double Hprev, double HStarPrev,
        double theta, double Ue, double dUeDx, double dx,
        double nu, double Me)
    {
        double Me2 = Me * Me;
        double uDuDx_over_Ue = dUeDx / System.Math.Max(Ue, 1e-12);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);

        double Residual(double H)
        {
            double Hk = MsesClosureRelations.ComputeHk(H, Me);
            double HStar = MsesClosureRelations.ComputeHStarLaminar(Hk, ReTheta);
            double Cf = MsesClosureRelations.ComputeCfLaminar(Hk, ReTheta);
            double CD = MsesClosureRelations.ComputeCDLaminar(Hk, ReTheta);
            double HDoubleStar = Me > 0.0
                ? (0.064 / System.Math.Max(Hk - 0.8, 0.1) + 0.251) * Me2
                : 0.0;
            double bracket = 2.0 * HDoubleStar / System.Math.Max(HStar, 1e-9) + (1.0 - H);
            // LHS − RHS.
            double lhs = theta * (HStar - HStarPrev) / dx;
            double rhs = 2.0 * CD - 0.5 * HStar * Cf - bracket * theta * uDuDx_over_Ue;
            return lhs - rhs;
        }

        double H = System.Math.Clamp(HInit, 1.05, 7.5);
        const double eps = 1e-4;
        const double tol = 1e-8;
        for (int it = 0; it < 25; it++)
        {
            double r = Residual(H);
            if (System.Math.Abs(r) < tol) return H;
            double rPlus = Residual(System.Math.Min(H + eps, 7.5 - eps));
            double rMinus = Residual(System.Math.Max(H - eps, 1.05 + eps));
            double dR = (rPlus - rMinus) / (2.0 * eps);
            if (System.Math.Abs(dR) < 1e-14) break; // singular Jacobian
            double step = -r / dR;
            // Line-search damping to avoid overshoot.
            double maxStep = 0.3;
            if (step > maxStep) step = maxStep;
            if (step < -maxStep) step = -maxStep;
            H = System.Math.Clamp(H + step, 1.05, 7.5);
        }
        return H;
    }
}

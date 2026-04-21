using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// Wake marcher for the turbulent viscous tail behind the airfoil TE.
/// Drela thesis §6.5: the wake is a free-shear layer with no wall, so
/// Cf = 0 in both the momentum and energy integrals; everything else
/// (CD, H*, Cτ-lag, H* closure) stays the same as the attached
/// turbulent marcher.
///
/// Equations:
///   dθ/dξ  = −(H + 2 − Me²)·θ·(dUe/dξ)/Ue           (eq. 6.9 with Cf=0)
///   θ·dH*/dξ = 2·CD − bracket·θ·(dUe/dξ)/Ue          (eq. 6.10 with Cf=0)
///   dCτ/dξ = (K2/δ)·(Cτ_eq − Cτ)                     (eq. 6.35, unchanged)
///
/// Implicit Newton on H per station (same backward-Euler treatment as
/// <see cref="ThesisExactTurbulentMarcher"/>), Cτ per-step exponential.
///
/// Caller seeds the IC from a merged airfoil-TE state: θ = θ_upper_TE
/// + θ_lower_TE, H blended by Drela's eq. 6.63 (typically a weighted
/// average), Cτ = max(Cτ_upper_TE, Cτ_lower_TE) as the dominant-stress
/// side.
/// </summary>
public static class WakeTurbulentMarcher
{
    public readonly record struct MarchResult(
        double[] Theta,
        double[] H,
        double[] HStar,
        double[] CTau);

    /// <summary>
    /// Marches (θ, H, Cτ) along a downstream wake, no wall shear.
    /// </summary>
    /// <param name="stations">Wake station positions (ascending,
    /// typically starting at x = chord and extending to 2·chord).</param>
    /// <param name="edgeVelocity">Ue(x) along the wake. Typically
    /// interpolated from the inviscid pressure field; in uncoupled
    /// mode a linear recovery toward U∞ is a reasonable placeholder.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="theta0">Initial θ (merged airfoil-TE θ_u + θ_l).</param>
    /// <param name="h0">Initial H (blended airfoil-TE shape).</param>
    /// <param name="cTau0">Initial Cτ (dominant-side airfoil-TE Cτ).</param>
    /// <param name="machNumberEdge">Edge Mach. Default 0.</param>
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
        double Hk0 = MsesClosureRelations.ComputeHk(h0, machNumberEdge);
        HStar[0] = MsesClosureRelations.ComputeHStarTurbulent(Hk0, reT00, machNumberEdge);

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

            // Momentum (explicit, Cf=0 for wake).
            double Me2 = machNumberEdge * machNumberEdge;
            double uDuDx_over_Ue = dUeDx / System.Math.Max(ue0, 1e-12);
            double dθ0 = -(H[i - 1] + 2.0 - Me2) * theta[i - 1] * uDuDx_over_Ue;
            double thetaNew = System.Math.Max(theta[i - 1] + dx * dθ0, 1e-18);

            // Cτ lag: closed-form decay using midpoint state.
            double thetaMidCτ = 0.5 * (theta[i - 1] + thetaNew);
            double HkMidCτ = MsesClosureRelations.ComputeHk(H[i - 1], machNumberEdge);
            double ReTMidCτ = ueMid * thetaMidCτ / nuSafe;
            double cTauEqMid = MsesClosureRelations.ComputeCTauEquilibrium(
                HkMidCτ, ReTMidCτ, machNumberEdge);
            double cTauNew = cTau[i - 1];
            if (HkMidCτ > 1.0)
            {
                double deltaMid = HkMidCτ * thetaMidCτ * (3.15 + 1.72 / (HkMidCτ - 1.0));
                const double K2 = 4.2;
                double decay = System.Math.Exp(-K2 * dx / System.Math.Max(deltaMid, 1e-18));
                cTauNew = System.Math.Max(cTauEqMid + (cTau[i - 1] - cTauEqMid) * decay, 0.0);
            }

            // Implicit Newton for H via backward-Euler shape-parameter
            // eq. (with Cf=0, the Cf-dependent term drops out).
            double Hsolve = NewtonSolveH(
                H[i - 1], HStar[i - 1], cTauNew,
                thetaNew, ue1, dUeDx, dx,
                kinematicViscosity, machNumberEdge);
            H[i] = Hsolve;

            double HkSolve = MsesClosureRelations.ComputeHk(Hsolve, machNumberEdge);
            double ReTSolve = ue1 * thetaNew / nuSafe;
            HStar[i] = MsesClosureRelations.ComputeHStarTurbulent(
                HkSolve, ReTSolve, machNumberEdge);
            theta[i] = thetaNew;
            cTau[i] = cTauNew;
        }

        return new MarchResult(theta, H, HStar, cTau);
    }

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
            double Hk = MsesClosureRelations.ComputeHk(h, Me);
            double HStar = MsesClosureRelations.ComputeHStarTurbulent(Hk, ReTheta, Me);
            // Cf = 0 in wake.
            double CD = MsesClosureRelations.ComputeCDTurbulent(Hk, ReTheta, Me, cTau);
            double HDoubleStar = Me > 0.0
                ? (0.064 / System.Math.Max(Hk - 0.8, 0.1) + 0.251) * Me2
                : 0.0;
            double bracket = 2.0 * HDoubleStar / System.Math.Max(HStar, 1e-9) + (1.0 - h);
            double lhs = theta * (HStar - HStarPrev) / dx;
            double rhs = 2.0 * CD - bracket * theta * uDuDx_over_Ue;
            return lhs - rhs;
        }

        // Wake H typically relaxes toward H ≈ 1 (recovering freestream),
        // but can stay elevated near a separated TE. Clamp [1.0, 5.0].
        double H = System.Math.Clamp(HInit, 1.0, 5.0);
        const double eps = 1e-4;
        const double tol = 1e-8;
        for (int it = 0; it < 25; it++)
        {
            double r = Residual(H);
            if (System.Math.Abs(r) < tol) return H;
            double rPlus = Residual(System.Math.Min(H + eps, 5.0 - eps));
            double rMinus = Residual(System.Math.Max(H - eps, 1.0 + eps));
            double dR = (rPlus - rMinus) / (2.0 * eps);
            if (System.Math.Abs(dR) < 1e-14) break;
            double step = -r / dR;
            const double maxStep = 0.3;
            if (step > maxStep) step = maxStep;
            if (step < -maxStep) step = -maxStep;
            H = System.Math.Clamp(H + step, 1.0, 5.0);
        }
        return H;
    }
}

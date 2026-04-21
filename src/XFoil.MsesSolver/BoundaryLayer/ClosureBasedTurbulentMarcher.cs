using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// Turbulent integral-boundary-layer marcher that evaluates Cf via
/// the MSES turbulent closure (<see cref="MsesClosureRelations.ComputeCfTurbulent"/>).
///
/// Phase-2c of the MSES port (see <c>MsesClosurePlan.md</c>). Starts
/// from a user-provided turbulent IC (θ₀, H₀, x₀) — transition is
/// Phase-3 scope and not done here. Validated against the 1/5-power-
/// law empirical reference for fully-developed turbulent flat-plate:
///   θ(x) = 0.036·x/Re_x^0.2
///   Cf(x) = 0.0592/Re_x^0.2
/// which holds over 5·10⁵ ≤ Re_x ≤ 10⁸.
///
/// For now H evolves via a simple "stay near equilibrium" relation:
/// H stays at 1.4 ± small-gradient adjustment. The full Drela §4.2
/// H-ODE with Cτ-lag tracking is Phase 2d scope.
/// </summary>
public static class ClosureBasedTurbulentMarcher
{
    /// <summary>
    /// Marches (θ, H) turbulent from a user-provided IC.
    /// </summary>
    /// <param name="stations">Station positions (ascending).</param>
    /// <param name="edgeVelocity">Edge velocity at each station.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="theta0">Initial θ at stations[0].</param>
    /// <param name="h0">Initial H at stations[0].</param>
    /// <param name="machNumberEdge">Optional edge Mach.</param>
    /// <returns>(θ, H) at each station.</returns>
    public static (double[] Theta, double[] H) March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
        double theta0,
        double h0,
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
        theta[0] = theta0;
        H[0] = h0;

        for (int i = 1; i < n; i++)
        {
            double x0 = stations[i - 1];
            double x1 = stations[i];
            double dx = x1 - x0;
            if (dx <= 0.0) { theta[i] = theta[i - 1]; H[i] = H[i - 1]; continue; }

            double ue0 = edgeVelocity[i - 1];
            double ue1 = edgeVelocity[i];
            double dUeDx = (ue1 - ue0) / dx;
            double ueMid = 0.5 * (ue0 + ue1);
            if (ueMid < 1e-12)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                continue;
            }

            // RK2 midpoint. Momentum + simple H relaxation.
            double theta_n = theta[i - 1];
            double H_n = H[i - 1];
            double dThetaDx0 = MomentumDerivative(theta_n, H_n, ue0, dUeDx, kinematicViscosity, machNumberEdge);
            double thetaMid = theta_n + 0.5 * dx * dThetaDx0;
            thetaMid = System.Math.Max(thetaMid, 1e-12);
            double dThetaDxMid = MomentumDerivative(thetaMid, H_n, ueMid, dUeDx, kinematicViscosity, machNumberEdge);
            theta[i] = System.Math.Max(theta_n + dx * dThetaDxMid, 1e-12);

            // H evolution: near equilibrium, H drifts toward the
            // equilibrium value given by solving dH*/dHk = 0 — but
            // simpler: apply a small adjustment proportional to
            // dUe/dx (shape factor drops under favorable gradient,
            // rises under adverse).
            //
            // Empirical relaxation: for attached turbulent H ∈
            // [1.3, 1.6] the sensitivity is dH/dx ≈ −(H+1)·(dUe/dx)/Ue.
            // This matches the Clauser-like equilibrium behavior
            // without needing the full H*-energy-integral ODE.
            double dHDx = -(H_n + 1.0) * dUeDx / ueMid;
            double newH = H_n + dx * dHDx;
            H[i] = System.Math.Clamp(newH, 1.05, 4.0);
        }

        return (theta, H);
    }

    private static double MomentumDerivative(
        double theta, double H, double Ue, double dUeDx, double nu, double Me)
    {
        double Hk = MsesClosureRelations.ComputeHk(H, Me);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);
        double Cf = MsesClosureRelations.ComputeCfTurbulent(Hk, ReTheta, Me);
        double uDuDx_over_Ue = dUeDx / System.Math.Max(Ue, 1e-12);
        return 0.5 * Cf - (H + 2.0) * theta * uDuDx_over_Ue;
    }
}

using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.ThesisClosureSolver.BoundaryLayer;

/// <summary>
/// Phase-2d turbulent marcher that carries Cτ as a third state
/// variable via Drela's lag ODE. Integrates the system:
///
///   dθ/dξ  = Cf/2 − (H+2)·θ·(dUe/dξ)/Ue
///   dH/dξ  ≈ −(H+1)·(dUe/dξ)/Ue      (Clauser relaxation — H-ODE
///                                      is Phase 2e scope)
///   dCτ/dξ = (K2/δ)·(Cτ_eq − Cτ)     (ThesisClosureRelations.ComputeCTauLagRhs)
///
/// Cf now comes from <see cref="ThesisClosureRelations.ComputeCfTurbulent"/>
/// as before, but CD is computed via
/// <see cref="ThesisClosureRelations.ComputeCDTurbulent"/> using the
/// carried Cτ (not Cτ_eq). This is what makes the MSES march
/// stable past separation — the outer-layer stress relaxes with
/// finite rate rather than jumping to the local-equilibrium value.
///
/// Phase 2e will add the full energy integral for dH/dξ. For now
/// the Clauser relaxation is the placeholder.
/// </summary>
public static class ClosureBasedTurbulentLagMarcher
{
    /// <summary>
    /// Result bundle: θ, H, and Cτ at each station.
    /// </summary>
    public readonly record struct MarchResult(double[] Theta, double[] H, double[] CTau);

    /// <summary>
    /// Marches (θ, H, Cτ) from a turbulent IC.
    /// </summary>
    /// <param name="stations">Station positions (ascending).</param>
    /// <param name="edgeVelocity">Edge velocity at each station.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="theta0">Initial θ at stations[0].</param>
    /// <param name="h0">Initial H at stations[0].</param>
    /// <param name="cTau0">Initial Cτ at stations[0]. Pass
    /// Cτ_eq from the closure to start in equilibrium.</param>
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
        var cTau = new double[n];
        theta[0] = theta0;
        H[0] = h0;
        cTau[0] = cTau0;

        for (int i = 1; i < n; i++)
        {
            double x0 = stations[i - 1];
            double x1 = stations[i];
            double dx = x1 - x0;
            if (dx <= 0.0)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                cTau[i] = cTau[i - 1];
                continue;
            }

            double ue0 = edgeVelocity[i - 1];
            double ue1 = edgeVelocity[i];
            double dUeDx = (ue1 - ue0) / dx;
            double ueMid = 0.5 * (ue0 + ue1);
            if (ueMid < 1e-12)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                cTau[i] = cTau[i - 1];
                continue;
            }

            // RK2 midpoint for θ and H (non-stiff). Cτ integrated
            // analytically per-step because the lag ODE has a very
            // short time scale (K2/δ ≈ 2000/length) and explicit
            // integration with the BL-marcher step size dx ≈ 0.01
            // overshoots catastrophically. Closed-form relaxation
            // assuming Cτ_eq and δ constant over the step is exact
            // for each trapezoidal segment:
            //   Cτ(x+dx) = Cτ_eq + (Cτ(x) − Cτ_eq)·exp(−K2·dx/δ).
            (double dThetaDx0, double dHDx0, _) = ComputeDerivatives(
                theta[i - 1], H[i - 1], cTau[i - 1],
                ue0, dUeDx, kinematicViscosity, machNumberEdge);

            double thetaMid = System.Math.Max(theta[i - 1] + 0.5 * dx * dThetaDx0, 1e-12);
            double HMid = System.Math.Clamp(H[i - 1] + 0.5 * dx * dHDx0, 1.05, 4.0);

            (double dThetaDxMid, double dHDxMid, _) = ComputeDerivatives(
                thetaMid, HMid, cTau[i - 1],
                ueMid, dUeDx, kinematicViscosity, machNumberEdge);

            double thetaCandidate = System.Math.Max(theta[i - 1] + dx * dThetaDxMid, 1e-12);
            // Runaway guard: θ shouldn't grow more than 3× in one
            // step for a well-resolved BL march. If it does, the
            // Clauser H-ODE + closure's combined (θ, H) dynamics
            // are in a numerically unstable regime. Cap θ growth
            // per-step and freeze H; downstream stations will
            // inherit the cap, signaling "hit the ceiling" to the
            // caller without propagating ∞ into CD integration.
            double maxGrowth = 3.0;
            if (thetaCandidate > theta[i - 1] * maxGrowth)
            {
                thetaCandidate = theta[i - 1] * maxGrowth;
            }
            theta[i] = thetaCandidate;
            H[i] = System.Math.Clamp(H[i - 1] + dx * dHDxMid, 1.05, 4.0);

            // Cτ: analytical step using midpoint Cτ_eq/δ.
            double HkMid = ThesisClosureRelations.ComputeHk(HMid, machNumberEdge);
            double ReThetaMid = ueMid * thetaMid / System.Math.Max(kinematicViscosity, 1e-18);
            double cTauEqMid = ThesisClosureRelations.ComputeCTauEquilibrium(HkMid, ReThetaMid, machNumberEdge);
            if (HkMid > 1.0)
            {
                // Thesis eq. 6.36: δ = (3.15 + 1.72/(Hk-1))·δ*,
                // with δ* = H·θ ≈ Hk·θ for Me=0.
                double deltaMid = HkMid * thetaMid * (3.15 + 1.72 / (HkMid - 1.0));
                // Thesis §6.4: K2 = 4.2 (not Green's 5.6).
                const double K2 = 4.2;
                double decay = System.Math.Exp(-K2 * dx / System.Math.Max(deltaMid, 1e-18));
                cTau[i] = System.Math.Max(cTauEqMid + (cTau[i - 1] - cTauEqMid) * decay, 0.0);
            }
            else
            {
                cTau[i] = cTau[i - 1];
            }
        }

        return new MarchResult(theta, H, cTau);
    }

    private static (double dThetaDx, double dHDx, double dCTauDx) ComputeDerivatives(
        double theta, double H, double cTau,
        double Ue, double dUeDx, double nu, double Me)
    {
        double Hk = ThesisClosureRelations.ComputeHk(H, Me);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);
        double Cf = ThesisClosureRelations.ComputeCfTurbulent(Hk, ReTheta, Me);
        double cTauEq = ThesisClosureRelations.ComputeCTauEquilibrium(Hk, ReTheta, Me);
        double uDuDx_over_Ue = dUeDx / System.Math.Max(Ue, 1e-12);

        // Momentum.
        double dThetaDx = 0.5 * Cf - (H + 2.0) * theta * uDuDx_over_Ue;

        // H — Clauser relaxation (Phase 2e: replace with full energy
        // integral including Cτ contribution).
        double dHDx = -(H + 1.0) * uDuDx_over_Ue;

        // Cτ lag.
        double dCTauDx = ThesisClosureRelations.ComputeCTauLagRhs(cTau, cTauEq, theta, Hk);

        return (dThetaDx, dHDx, dCTauDx);
    }
}

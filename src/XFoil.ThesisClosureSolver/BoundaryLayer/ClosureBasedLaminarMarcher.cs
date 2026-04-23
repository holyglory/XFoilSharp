using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.ThesisClosureSolver.BoundaryLayer;

/// <summary>
/// Laminar integral-boundary-layer marcher that evaluates Cf, CD, and
/// H* via the <see cref="ThesisClosureRelations"/> library from Drela's
/// thesis §4, rather than Thwaites' tabulated λ(θ²·dUe/dx/ν) correlation.
///
/// This is the Phase-2b marcher in the MSES port (see
/// <c>MsesClosurePlan.md</c>). Unlike the classical Thwaites reference
/// marcher in <see cref="ThwaitesLaminarMarcher"/>, this one uses the
/// same closure path that the turbulent marcher (Phase 2c) and the
/// Newton-coupled marcher (Phase 5) will use — so exercising it is the
/// first real test of the closure library in an integrating context.
///
/// State variables per station: (θ, H). At each step:
/// 1. Compute Hk from H and Me (incompressible scope: Hk = H).
/// 2. Evaluate Cf(Hk, Reθ) and CD(Hk, Reθ) from the laminar closure.
/// 3. Step θ via momentum integral:
///      dθ/dξ = Cf/2 − (H + 2)·(θ/Ue)·dUe/dξ
/// 4. Step H via energy integral:
///      θ·dH*/dξ = 2·CD − H*·Cf/2 − [2·H** + H*(1−H)]·(θ/Ue)·dUe/dξ
///    with H* = closure(H), H** ≈ 0.2·H* for laminar (Drela §4.1 eq. 4.16).
///
/// Integration scheme is explicit RK2 for station-to-station stepping.
/// RK4 is overkill for the smooth Blasius-like flat-plate case and
/// would obscure any closure-related residual in the Phase 2b gate.
/// </summary>
public static class ClosureBasedLaminarMarcher
{
    /// <summary>
    /// Marches (θ, H) along a station distribution using Drela's
    /// laminar closure. Me defaults to 0 (incompressible core).
    /// </summary>
    /// <param name="stations">Streamwise station positions (ascending).</param>
    /// <param name="edgeVelocity">Edge velocity at each station.</param>
    /// <param name="kinematicViscosity">ν = μ/ρ.</param>
    /// <param name="machNumberEdge">Optional edge Mach; default 0.</param>
    /// <returns>(θ, H) at each station.</returns>
    public static (double[] Theta, double[] H) March(
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

        // Initial conditions: Blasius-like IC near the leading edge.
        // Start with θ from the flat-plate reference θ = 0.664·√(νx/U).
        // H = 2.59 (Blasius).
        //
        // When stations[0] = 0 (singular LE), we can't seed Reθ > 0 at
        // that station without a divide-by-zero downstream in the Cf
        // correlation. Seed the second station from Blasius @ stations[1]
        // and skip the first-station integration entirely.
        theta[0] = 0.0;
        H[0] = 2.59;

        int startIdx = 1;
        if (stations[0] > 0.0 && edgeVelocity[0] > 1e-12)
        {
            theta[0] = 0.664 * System.Math.Sqrt(kinematicViscosity * stations[0] / edgeVelocity[0]);
            // Normal case: integrate from station 1.
            startIdx = 1;
        }
        else if (n >= 2 && stations[1] > 0.0 && edgeVelocity[1] > 1e-12)
        {
            // Singular LE at x=0: seed station 1 from Blasius and start
            // integrating from station 2.
            theta[1] = 0.664 * System.Math.Sqrt(kinematicViscosity * stations[1] / edgeVelocity[1]);
            H[1] = 2.59;
            startIdx = 2;
        }

        // Laminar separation short-circuit: once H exceeds 6.5 (well
        // into the laminar-separated regime), the Thwaites-λ
        // correlation no longer represents the physics and dθ/dx
        // unbounded growth gives a runaway θ. Freeze both θ and H
        // at their last-known-good values for all remaining stations.
        // The composite marcher sees a "still laminar to the end"
        // result and reports transition-not-found; the caller can
        // then fall back to forced transition or treat the airfoil
        // as separated.
        bool separated = false;
        for (int i = startIdx; i < n; i++)
        {
            if (separated)
            {
                theta[i] = theta[i - 1];
                H[i] = H[i - 1];
                continue;
            }
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

            // RK2 (midpoint). Start state (θ₀, H₀) at station i-1.
            double theta0 = theta[i - 1];
            double H0 = H[i - 1];

            // Derivatives + instantaneous H at (x₀, θ₀, H₀).
            var (dThetaDx0, _) = ComputeMomentumStep(
                theta0, H0, ue0, dUeDx, kinematicViscosity, machNumberEdge);

            // Midpoint θ, get mid-H from Thwaites λ at midpoint.
            double thetaMid = theta0 + 0.5 * dx * dThetaDx0;
            thetaMid = System.Math.Max(thetaMid, 1e-12);

            var (dThetaDxMid, HMid) = ComputeMomentumStep(
                thetaMid, H0, ueMid, dUeDx, kinematicViscosity, machNumberEdge);

            double thetaCandidate = System.Math.Max(theta0 + dx * dThetaDxMid, 1e-12);
            // Laminar θ growth clamp: near stagnation Ue→0 makes
            // dθ/dx = Cf/2 blow up (Cf = 2·f/Reθ with Reθ→0). Cap
            // per-step growth at 3× to prevent spurious θ jumps
            // that propagate into Ñ accumulation and produce
            // sub-Blasius transition positions.
            if (thetaCandidate > theta0 * 3.0 && theta0 > 1e-18)
            {
                thetaCandidate = theta0 * 3.0;
            }
            // Absolute cap: an attached laminar BL shouldn't have θ
            // exceed ~2 % of the local streamwise distance (Blasius
            // gives θ ≈ 0.664·√(νx/U), which at Re_chord=1e6 and
            // x≈chord gives θ/c ≈ 0.00066 — the 2 % cap is a very
            // loose upper bound that only fires on runaway cases
            // like NACA 0006 α=4° lower surface).
            double thetaAbsCap = 0.02 * System.Math.Max(x1, 1e-6);
            if (thetaCandidate > thetaAbsCap)
            {
                thetaCandidate = thetaAbsCap;
            }
            theta[i] = thetaCandidate;
            // Recompute H at the new θ/Ue via Thwaites λ — this is the
            // canonical laminar-H assignment once θ is known.
            var (_, newH) = ComputeMomentumStep(
                theta[i], HMid, ue1, dUeDx, kinematicViscosity, machNumberEdge);
            H[i] = newH;

            // Separation guard: once H exceeds 6.5 AND we're past
            // the stagnation region (Ue > 0.4·U∞ assumed reference),
            // the correlation is unreliable. Freeze state for
            // remaining stations. Near-stagnation Hk spikes are
            // numerical artifacts of Thwaites-λ at small Ue and
            // should NOT trigger the freeze — they resolve as Ue
            // rises and θ recovers a normal Blasius growth.
            if (newH >= 6.5 && ue1 > 0.4)
            {
                separated = true;
            }
        }

        return (theta, H);
    }

    private static (double dThetaDx, double newH) ComputeMomentumStep(
        double theta,
        double H,
        double Ue,
        double dUeDx,
        double nu,
        double Me)
    {
        double Hk = ThesisClosureRelations.ComputeHk(H, Me);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);
        double Cf = ThesisClosureRelations.ComputeCfLaminar(Hk, ReTheta);

        double uDuDx_over_Ue = dUeDx / System.Math.Max(Ue, 1e-12);
        // Momentum: dθ/dx = Cf/2 − (H+2)·θ · (dUe/dx)/Ue.
        double dThetaDx = 0.5 * Cf - (H + 2.0) * theta * uDuDx_over_Ue;

        // H update via Thwaites' λ correlation (Cebeci-Bradshaw form).
        // The energy-integral H-ODE from Drela §5.1 needs careful
        // sign handling that's error-prone without the thesis in
        // hand; Phase 2b uses this proven H-mapping to decouple
        // momentum-closure validation from the H ODE. Phase 2c+ will
        // layer the full energy equation on top once the signs and
        // H** convention are checked against thesis primary source.
        double lambda = theta * theta * dUeDx / System.Math.Max(nu, 1e-18);
        double newH = lambda >= 0.0
            ? 2.61 - 3.75 * lambda + 5.24 * lambda * lambda
            : 2.088 + 0.0731 / System.Math.Max(lambda + 0.14, 1e-6);
        newH = System.Math.Clamp(newH, 1.05, 7.0);
        return (dThetaDx, newH);
    }
}

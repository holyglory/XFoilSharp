using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// Laminar boundary-layer marcher that also tracks Drela's e^N
/// amplification factor Ñ(x), and reports the first station where
/// Ñ reaches the critical n-factor (transition onset).
///
/// This is the Phase-3 piece of the MSES port: it sits between the
/// laminar marcher (Phase 2b) and the turbulent marcher (Phase 2c/2d).
/// When transition is detected the caller should hand off to the
/// turbulent marcher, seeding θ, H from the final laminar state and
/// Cτ from a small fraction of the local Cτ_eq.
/// </summary>
public static class LaminarTransitionMarcher
{
    /// <summary>
    /// Per-station state produced by the marcher.
    /// </summary>
    public readonly record struct TransitionMarchResult(
        double[] Theta,
        double[] H,
        double[] N,
        int TransitionIndex,
        double TransitionX);

    /// <summary>
    /// Marches the laminar BL accumulating the Ñ amplification factor
    /// along the streamwise direction. TransitionIndex is set to the
    /// first station index where Ñ ≥ <paramref name="nCrit"/>; −1 if
    /// the BL stays laminar to the last station.
    /// </summary>
    /// <param name="stations">Station positions (ascending).</param>
    /// <param name="edgeVelocity">Edge velocity at each station.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="nCrit">Critical n-factor. Default 9.0 (standard tunnel).</param>
    /// <param name="machNumberEdge">Edge Mach. Default 0.</param>
    /// <param name="useThesisExactLaminar">If true, drive (θ, H) from
    /// <see cref="ThesisExactLaminarMarcher"/> (implicit-Newton on
    /// eq. 6.10 laminar closure) instead of the Phase-2b Thwaites-λ
    /// marcher. Ñ accumulation uses the resulting (θ, H) exactly the
    /// same way regardless. Default false (preserves existing
    /// behavior).</param>
    public static TransitionMarchResult March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
        double nCrit = 9.0,
        double machNumberEdge = 0.0,
        bool useThesisExactLaminar = false)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (edgeVelocity is null) throw new System.ArgumentNullException(nameof(edgeVelocity));
        if (stations.Length != edgeVelocity.Length)
            throw new System.ArgumentException("length mismatch");
        if (stations.Length < 2)
            throw new System.ArgumentException("need ≥2 stations");

        // Run the Phase-2b (default) or Phase-2e (implicit-Newton)
        // laminar marcher for θ/H.
        double[] theta, h;
        if (useThesisExactLaminar)
        {
            var mr = ThesisExactLaminarMarcher.March(
                stations, edgeVelocity, kinematicViscosity, machNumberEdge);
            theta = mr.Theta;
            h = mr.H;
        }
        else
        {
            var (t, hh) = ClosureBasedLaminarMarcher.March(
                stations, edgeVelocity, kinematicViscosity, machNumberEdge);
            theta = t;
            h = hh;
        }

        int n = stations.Length;
        var NAmp = new double[n];
        int transitionIdx = -1;
        double transitionX = double.NaN;

        for (int i = 1; i < n; i++)
        {
            double dx = stations[i] - stations[i - 1];
            if (dx <= 0.0)
            {
                NAmp[i] = NAmp[i - 1];
                continue;
            }

            // Envelope e^N Ñ accumulation: Ñ grows as
            //   dÑ = dÑ/dReθ(Hk) · ΔReθ
            // with Reθ = Ue·θ/ν. Evaluated at the midpoint between
            // stations.
            // Stagnation-region guard: Ue near zero produces
            // spurious Hk and Reθ values that trigger early
            // "transition" numerically. Skip Ñ accumulation until
            // Ue rises above 0.3·U∞ (assumed reference = 1).
            double reT0 = ReTheta(theta[i - 1], edgeVelocity[i - 1], kinematicViscosity);
            double reT1 = ReTheta(theta[i], edgeVelocity[i], kinematicViscosity);
            double dReTheta = reT1 - reT0;
            double ueMin = System.Math.Min(edgeVelocity[i - 1], edgeVelocity[i]);
            double hkMid = 0.5 * (
                XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeHk(h[i - 1], machNumberEdge)
                + XFoil.MsesSolver.Closure.MsesClosureRelations.ComputeHk(h[i], machNumberEdge));
            double hkClamped = System.Math.Min(hkMid, 4.0);
            double reT0c = AmplificationRateModel.ComputeReThetaCritical(hkClamped);
            // Only accumulate above the neutral-stability boundary
            // AND outside the stagnation region AND when the laminar
            // state isn't in the spurious separation clamp. Hk=7
            // (the upper bound of the closure laminar marcher) on
            // any station indicates the Thwaites-λ correlation hit
            // its domain edge — usually near stagnation where the
            // physics is being mis-represented by the correlation.
            bool spuriousSeparation = hkMid >= 6.5;
            if (dReTheta > 0 && 0.5 * (reT0 + reT1) > reT0c
                && ueMin > 0.3 && !spuriousSeparation)
            {
                double slope = AmplificationRateModel.ComputeDAmplificationDReTheta(hkClamped);
                NAmp[i] = NAmp[i - 1] + slope * dReTheta;
            }
            else
            {
                NAmp[i] = NAmp[i - 1];
            }

            if (transitionIdx < 0 && NAmp[i] >= nCrit)
            {
                transitionIdx = i;
                // Linear interpolate the exact x where Ñ crossed nCrit.
                if (NAmp[i - 1] < nCrit)
                {
                    double t = (nCrit - NAmp[i - 1]) / (NAmp[i] - NAmp[i - 1]);
                    transitionX = stations[i - 1] + t * dx;
                }
                else
                {
                    transitionX = stations[i];
                }
            }
        }

        return new TransitionMarchResult(theta, h, NAmp, transitionIdx, transitionX);
    }

    private static double ReTheta(double theta, double Ue, double nu)
    {
        if (theta < 1e-18 || Ue < 1e-12) return 0.0;
        return Ue * theta / System.Math.Max(nu, 1e-18);
    }
}

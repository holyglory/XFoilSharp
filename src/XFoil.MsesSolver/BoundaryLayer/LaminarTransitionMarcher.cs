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
    public static TransitionMarchResult March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
        double nCrit = 9.0,
        double machNumberEdge = 0.0)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (edgeVelocity is null) throw new System.ArgumentNullException(nameof(edgeVelocity));
        if (stations.Length != edgeVelocity.Length)
            throw new System.ArgumentException("length mismatch");
        if (stations.Length < 2)
            throw new System.ArgumentException("need ≥2 stations");

        // Run the Phase-2b closure-based laminar marcher for θ/H.
        var (theta, h) = ClosureBasedLaminarMarcher.March(
            stations, edgeVelocity, kinematicViscosity, machNumberEdge);

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

            // Trapezoidal Ñ accumulation.
            double rate0 = ComputeRate(h[i - 1], theta[i - 1], edgeVelocity[i - 1],
                kinematicViscosity, machNumberEdge);
            double rate1 = ComputeRate(h[i], theta[i], edgeVelocity[i],
                kinematicViscosity, machNumberEdge);
            NAmp[i] = NAmp[i - 1] + 0.5 * (rate0 + rate1) * dx;

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

    private static double ComputeRate(double H, double theta, double Ue, double nu, double Me)
    {
        if (theta < 1e-18 || Ue < 1e-12) return 0.0;
        double Hk = MsesClosureRelations.ComputeHk(H, Me);
        double ReTheta = Ue * theta / System.Math.Max(nu, 1e-18);
        return AmplificationRateModel.ComputeAmplificationRate(Hk, ReTheta, theta);
    }
}

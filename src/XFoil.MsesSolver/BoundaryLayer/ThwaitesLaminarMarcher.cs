namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// Laminar boundary-layer marcher via Thwaites' method.
///
/// Thwaites' correlation (1949) is the classical explicit integration
/// of the 2D momentum-integral equation for laminar BLs:
///   θ²(x)·Ue⁶(x) = 0.45·ν·∫₀ˣ Ue⁵(ξ) dξ
/// It's the simplest BL march that works, and serves as the Phase-2
/// acceptance baseline in the MSES port — the Thwaites marcher must
/// reproduce Blasius flat-plate growth (θ = 0.664·√(ν·x/U∞)) within
/// 0.5% before the MSES-closure marcher is layered on top.
///
/// Not part of the final MSES solver — Drela's §5.1 integral march
/// uses the closure relations (Cf, CD, H*) directly. Thwaites stays
/// as an independent reference to catch regressions in the harder
/// marcher.
/// </summary>
public static class ThwaitesLaminarMarcher
{
    /// <summary>
    /// Marches momentum thickness θ(x) and shape factor H(x) along
    /// a smooth station distribution via Thwaites. Handles both
    /// favorable (Ue accelerating) and mildly adverse pressure
    /// gradients.
    /// </summary>
    /// <param name="stations">Streamwise station positions x_i (ascending).
    /// Must start at 0 or a small positive offset; the Blasius flat-plate
    /// singularity is handled analytically at x=0.</param>
    /// <param name="edgeVelocity">Edge velocity Ue at each station.</param>
    /// <param name="kinematicViscosity">ν = μ/ρ (freestream).</param>
    /// <returns>(θ, H) at each station.</returns>
    public static (double[] Theta, double[] H) March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (edgeVelocity is null) throw new System.ArgumentNullException(nameof(edgeVelocity));
        if (stations.Length != edgeVelocity.Length)
            throw new System.ArgumentException("stations and edgeVelocity must be same length");
        if (stations.Length < 2)
            throw new System.ArgumentException("Need at least 2 stations to march");

        int n = stations.Length;
        var theta = new double[n];
        var h = new double[n];

        // Accumulator for ∫₀ˣ Ue⁵ dξ via trapezoid rule.
        double integralUe5 = 0.0;
        double ue5Prev = System.Math.Pow(edgeVelocity[0], 5.0);

        for (int i = 0; i < n; i++)
        {
            double x = stations[i];
            double ue = edgeVelocity[i];
            double ue5 = System.Math.Pow(ue, 5.0);

            if (i > 0)
            {
                double dx = x - stations[i - 1];
                integralUe5 += 0.5 * (ue5 + ue5Prev) * dx;
            }
            ue5Prev = ue5;

            // Thwaites: θ² = 0.45·ν·∫Ue⁵ / Ue⁶.
            if (ue < 1e-12 || x <= 0.0)
            {
                theta[i] = 0.0;
                h[i] = 2.61; // Blasius shape factor at stagnation.
                continue;
            }

            double ue6 = ue5 * ue;
            theta[i] = System.Math.Sqrt(0.45 * kinematicViscosity * integralUe5 / ue6);

            // Thwaites' λ parameter:
            //   λ = θ² · dUe/dx / ν
            // evaluated via forward/backward/centered finite diff.
            double dUedx = EstimateDerivative(stations, edgeVelocity, i);
            double lambda = theta[i] * theta[i] * dUedx / kinematicViscosity;

            // Cebeci-Bradshaw form for H(λ) from Thwaites' tables.
            // Piecewise:
            //   λ ∈ [0, 0.1]:   H = 2.61 - 3.75·λ + 5.24·λ²
            //   λ ∈ [-0.1, 0]:  H = 2.088 + 0.0731 / (λ + 0.14)
            if (lambda >= 0.0)
            {
                h[i] = 2.61 - 3.75 * lambda + 5.24 * lambda * lambda;
            }
            else
            {
                double denom = lambda + 0.14;
                h[i] = denom > 1e-6 ? 2.088 + 0.0731 / denom : 4.0;
            }
        }

        return (theta, h);
    }

    private static double EstimateDerivative(double[] x, double[] y, int i)
    {
        int n = x.Length;
        if (n < 2) return 0.0;
        if (i == 0) return (y[1] - y[0]) / (x[1] - x[0]);
        if (i == n - 1) return (y[n - 1] - y[n - 2]) / (x[n - 1] - x[n - 2]);
        return (y[i + 1] - y[i - 1]) / (x[i + 1] - x[i - 1]);
    }
}

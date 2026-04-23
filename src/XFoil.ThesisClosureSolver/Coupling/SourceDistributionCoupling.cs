namespace XFoil.ThesisClosureSolver.Coupling;

/// <summary>
/// Thin-airfoil source-distribution model for viscous-inviscid
/// coupling. The displacement-thickness effect of the BL is
/// represented as a surface source distribution; the tangential
/// velocity perturbation ΔUe at each station comes from a discrete
/// Hilbert-like integral of the source strength.
///
/// This is the physically-correct alternative to Phase-5-lite's
/// geometric displacement-body offset, which injected spurious
/// camber on cambered airfoils (see agents/architecture/
/// MsesClosurePlan.md Phase-5-lite known limitation).
///
/// References:
///   Drela 1986 thesis §6 (source distribution in the Euler couple).
///   Katz & Plotkin "Low-Speed Aerodynamics" §11.3 (thin-airfoil
///     source-distribution theory).
///
/// Sign convention:
///   σ(s) = d(Ue·δ*)/ds. A region of growing δ* (attached BL under
///   adverse gradient) gives σ > 0 locally, which in turn slows the
///   flow on the surface downstream (ΔUe &lt; 0). This is the
///   correct physical direction — the displaced streamlines see an
///   effectively fatter body and cannot re-accelerate as strongly.
/// </summary>
public static class SourceDistributionCoupling
{
    /// <summary>
    /// Computes σ(s) = d(Ue·δ*)/ds on the station grid by finite
    /// difference. Station 0 uses forward difference; station N-1
    /// uses backward difference; interior uses centered difference.
    /// </summary>
    /// <param name="stations">Arc-length stations (ascending).</param>
    /// <param name="edgeVelocity">Ue at each station.</param>
    /// <param name="displacementThickness">δ* at each station.</param>
    public static double[] ComputeDisplacementSource(
        double[] stations, double[] edgeVelocity, double[] displacementThickness)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (edgeVelocity is null) throw new System.ArgumentNullException(nameof(edgeVelocity));
        if (displacementThickness is null) throw new System.ArgumentNullException(nameof(displacementThickness));
        int n = stations.Length;
        if (edgeVelocity.Length != n || displacementThickness.Length != n)
            throw new System.ArgumentException("length mismatch");
        if (n < 2) throw new System.ArgumentException("need ≥2 stations");

        var product = new double[n];
        for (int i = 0; i < n; i++) product[i] = edgeVelocity[i] * displacementThickness[i];

        var sigma = new double[n];
        sigma[0] = (product[1] - product[0]) / System.Math.Max(stations[1] - stations[0], 1e-18);
        sigma[n - 1] = (product[n - 1] - product[n - 2])
                      / System.Math.Max(stations[n - 1] - stations[n - 2], 1e-18);
        for (int i = 1; i < n - 1; i++)
        {
            double ds = stations[i + 1] - stations[i - 1];
            sigma[i] = (product[i + 1] - product[i - 1]) / System.Math.Max(ds, 1e-18);
        }
        return sigma;
    }

    /// <summary>
    /// Tangential velocity perturbation at each station from the
    /// source distribution. Discrete approximation of the
    /// Cauchy principal-value integral
    ///   ΔUe(s) = (1/π) PV ∫ σ(ξ) / (s − ξ) dξ
    /// Self-terms are skipped; the sum is over trapezoidal segments.
    ///
    /// Returns ΔUe[i] for i ∈ [0..n).
    /// </summary>
    /// <param name="stations">Arc-length stations (ascending).</param>
    /// <param name="sigma">Source strength σ(s) from
    /// <see cref="ComputeDisplacementSource"/>.</param>
    /// <param name="selfSkipFraction">Fraction of the local panel
    /// spacing to treat as the self-interaction exclusion radius.
    /// Default 0.5 (skip when |s−ξ| &lt; 0.5·Δs_local). Smaller values
    /// increase numerical noise from the integrable singularity;
    /// larger values over-smooth.</param>
    public static double[] IntegrateSourceUeDelta(
        double[] stations, double[] sigma, double selfSkipFraction = 0.5)
    {
        if (stations is null) throw new System.ArgumentNullException(nameof(stations));
        if (sigma is null) throw new System.ArgumentNullException(nameof(sigma));
        int n = stations.Length;
        if (sigma.Length != n) throw new System.ArgumentException("length mismatch");
        if (n < 2) throw new System.ArgumentException("need ≥2 stations");

        var dUe = new double[n];
        // Local panel widths for self-exclusion. For trapezoidal
        // quadrature on non-uniform grids each σ[j] contributes
        // with weight = 0.5·(stations[j+1] - stations[j-1]); edges
        // use forward/backward half-widths.
        var weight = new double[n];
        weight[0] = 0.5 * (stations[1] - stations[0]);
        weight[n - 1] = 0.5 * (stations[n - 1] - stations[n - 2]);
        for (int j = 1; j < n - 1; j++) weight[j] = 0.5 * (stations[j + 1] - stations[j - 1]);

        const double oneOverPi = 1.0 / System.Math.PI;
        for (int i = 0; i < n; i++)
        {
            double accum = 0.0;
            double localDs = weight[i];
            double exclusion = System.Math.Max(selfSkipFraction * localDs, 1e-18);
            for (int j = 0; j < n; j++)
            {
                double denom = stations[i] - stations[j];
                if (System.Math.Abs(denom) < exclusion) continue;
                accum += sigma[j] * weight[j] / denom;
            }
            dUe[i] = oneOverPi * accum;
        }
        return dUe;
    }
}

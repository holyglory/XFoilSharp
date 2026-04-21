namespace XFoil.MsesSolver.Closure;

/// <summary>
/// TS-wave (Tollmien-Schlichting) amplification-rate model used by
/// Drela's e^N transition criterion in MSES.
///
/// Tracks an amplification factor Ñ(x) along the laminar boundary
/// layer; transition is declared at the streamwise station where
/// Ñ(x) reaches the critical n-factor (typically n_crit = 9 for
/// tunnel flows, lower for dirty environments).
///
/// Model: dÑ/dξ = (dÑ/dReθ)·(dReθ/dξ). Drela gives (dÑ/dReθ) as a
/// correlation in Hk from curve fits of parallel-flow linear-
/// stability calculations (Orr-Sommerfeld at low Re). Ref:
/// Drela/Giles "Viscous-Inviscid Analysis of Transonic and Low-
/// Reynolds-Number Airfoils" AIAA Journal 25(10), 1987.
///
/// The sub-critical-Reθ branch (below the neutral-stability point
/// Reθ₀(Hk)) returns 0 — no amplification. Above the neutral point
/// the rate is a quadratic in Reθ scaled by a Hk-dependent factor.
/// </summary>
public static class AmplificationRateModel
{
    /// <summary>
    /// Critical Reθ₀(Hk) — the neutral-stability Reynolds number.
    /// Below this Reθ the flow is stable, above it TS waves grow.
    /// Drela eq. 5 of the 1987 paper.
    /// </summary>
    public static double ComputeReThetaCritical(double Hk)
    {
        // Drela's fit:
        //   log10(Reθ₀) = (1.415 / (Hk − 1) − 0.489)·tanh(20/(Hk−1) − 12.9)
        //               + 3.295/(Hk − 1) + 0.44
        // Valid for 1.05 ≤ Hk ≤ 10.
        double HkM1 = System.Math.Max(Hk - 1.0, 0.05);
        double arg = 20.0 / HkM1 - 12.9;
        double log10Re = (1.415 / HkM1 - 0.489) * System.Math.Tanh(arg)
                         + 3.295 / HkM1 + 0.44;
        return System.Math.Pow(10.0, log10Re);
    }

    /// <summary>
    /// Amplification-rate slope dÑ/dReθ as a function of the shape
    /// parameter Hk. Drela 1987 eq. 6 (linear growth in Reθ from the
    /// neutral point, peaked in Hk near laminar-separation criteria).
    /// </summary>
    public static double ComputeDAmplificationDReTheta(double Hk)
    {
        // Drela's fit:
        //   dÑ/dReθ = 0.01·√[(2.4·Hk − 3.7 + 2.5·tanh(1.5·(Hk − 3.1)))²
        //                     + 0.25]
        double HkM3 = 1.5 * (Hk - 3.1);
        double tanhTerm = System.Math.Tanh(HkM3);
        double inner = 2.4 * Hk - 3.7 + 2.5 * tanhTerm;
        double raw = inner * inner + 0.25;
        return 0.01 * System.Math.Sqrt(raw);
    }

    /// <summary>
    /// Full dÑ/dξ streamwise amplification rate. Returns 0 if the
    /// local Reθ is below the neutral-stability threshold.
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Local Reθ.</param>
    /// <param name="theta">Local momentum thickness (needed because
    /// the chain-rule factor dReθ/dξ = (dReθ/dθ + …)/θ scales as 1/θ
    /// in Drela's compact form).</param>
    /// <returns>dÑ/dξ.</returns>
    public static double ComputeAmplificationRate(double Hk, double ReTheta, double theta)
    {
        if (theta < 1e-18) return 0.0;

        double ReθCrit = ComputeReThetaCritical(Hk);
        if (ReTheta <= ReθCrit) return 0.0;

        double dNdReθ = ComputeDAmplificationDReTheta(Hk);
        // Drela's compact-form dÑ/dξ = dÑ/dReθ · dReθ/dθ · dθ/dξ,
        // approximated as dÑ/dξ ≈ dÑ/dReθ · (2·Reθ/θ)·K(Hk) with
        // K(Hk) a Hk-dependent scale factor. For the Phase-3 scope
        // we use the simpler form dÑ/dξ = dÑ/dReθ · (Reθ − Reθ₀)/θ
        // which reproduces the Drela-AIAA-1987 linear-growth curve.
        return dNdReθ * (ReTheta - ReθCrit) / theta;
    }

    /// <summary>
    /// Standard critical n-factor value (tunnel flow, moderate
    /// turbulence intensity). Drela's reference default.
    /// </summary>
    public const double NCritStandard = 9.0;

    /// <summary>
    /// Low-turbulence tunnel or smooth-wing flight default.
    /// Higher than standard because there are fewer disturbances
    /// to seed amplification; transition Ñ must reach a larger
    /// value before the flow destabilizes.
    /// </summary>
    public const double NCritQuietTunnel = 11.0;
}

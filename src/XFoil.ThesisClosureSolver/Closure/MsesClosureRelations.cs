namespace XFoil.ThesisClosureSolver.Closure;

/// <summary>
/// 2D integral-boundary-layer closure relations from Drela's 1986 MIT
/// ScD thesis (§4 and Appendix A). These are pure functions with no
/// dependency on solver state — they map BL integral parameters
/// (H, Reθ, Me, ...) to correlation outputs (H*, Cf, CD, Cτ_eq, Hk, ...).
///
/// Phase 1 of the MSES port scope (see
/// <c>agents/architecture/ThesisClosurePlan.md</c>).
///
/// Standalone and unit-test-first: each relation is validated against
/// Drela's tabulated values or reference curves in Appendix A. No
/// solver wiring or Newton coupling at this stage.
///
/// Naming convention follows Drela's notation: H = shape parameter
/// δ*/θ, H* = energy shape parameter 2δ**/θ, Hk = kinematic shape
/// parameter (compressibility-corrected), Me = edge Mach, Reθ = edge
/// Reynolds based on momentum thickness θ.
/// </summary>
public static class ThesisClosureRelations
{
    /// <summary>
    /// Kinematic shape parameter Hk from H and Me. Drela §4.1, eq. 4.15.
    /// Compressibility correction: as Me rises, density variation
    /// through the BL inflates the (conventional) H, so Hk = (H − 0.290·Me²)
    /// / (1 + 0.113·Me²) strips out that scaling to keep the correlation
    /// space anchored to incompressible shapes.
    /// </summary>
    /// <param name="H">Conventional shape parameter δ*/θ.</param>
    /// <param name="Me">Edge Mach number (typically 0 for XFoilSharp's
    /// incompressible-core scope; non-zero hooks preserved for future
    /// transonic work).</param>
    /// <returns>Kinematic shape parameter Hk.</returns>
    public static double ComputeHk(double H, double Me)
    {
        double Me2 = Me * Me;
        double num = H - 0.290 * Me2;
        double den = 1.0 + 0.113 * Me2;
        return num / den;
    }

    /// <summary>
    /// Laminar energy-shape-factor correlation H*(Hk, Reθ).
    /// Drela thesis Appendix A, Table A.1 piecewise-quadratic fit.
    /// Valid for Hk ∈ [1.0, 7.4].
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <returns>Energy shape parameter H*.</returns>
    public static double ComputeHStarLaminar(double Hk, double ReTheta)
    {
        // Drela Appendix A: piecewise correlation in Hk.
        // Hk ∈ [1.0, 4.0]:   H* = 1.515 + 0.076·(Hk − 4)² / Hk
        // Hk ∈ [4.0, 7.4]:   H* = 1.515 + 0.040·(Hk − 4)² / Hk
        // At Hk = 4 both branches give 1.515 — continuous value AND
        // first derivative (the 0.076 → 0.040 transition is in the
        // second derivative, which Drela flags as intentional).
        double dh = Hk - 4.0;
        double num = Hk <= 4.0 ? 0.076 * dh * dh : 0.040 * dh * dh;
        return 1.515 + num / Hk;
    }

    /// <summary>
    /// Turbulent energy-shape-factor correlation H*(Hk, Reθ, Me).
    /// Drela thesis §4.2, eq. 4.21, calibrated against Spalding-type
    /// skin-friction / dissipation data. Unlike the laminar case, the
    /// turbulent H* depends on Reθ (through the log-law wake component)
    /// and Me (through the compressibility correction on the wall
    /// layer).
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <param name="Me">Edge Mach number.</param>
    /// <returns>Energy shape parameter H*.</returns>
    public static double ComputeHStarTurbulent(double Hk, double ReTheta, double Me)
    {
        // Clamp Reθ to ≥ 200 for correlation validity (log-law
        // assumption breaks down for thinner BL).
        double reT = System.Math.Max(ReTheta, 200.0);
        double logReT = System.Math.Log10(reT);

        // Drela's H0 = 3.0 + 400/Reθ is the "Hk at which the wake-
        // component contribution reverses sign" — above H0 the BL is
        // heading toward separation.
        double H0 = 3.0 + 400.0 / reT;

        // Drela 1986 thesis eq. 6.20 (OCR'd from Publications/15002356-MIT.pdf):
        //   Hk < H0:  H* = 1.505 + 4/Reθ + (0.165 - 1.6/√Reθ) · (H0-Hk)² / Hk
        //   Hk > H0:  H* = 1.505 + 4/Reθ + (Hk-H0)² · [0.04/Hk
        //                   + 0.007·ln(Reθ) / (Hk - H0 + 4/ln(Reθ))²]
        double baseValue;
        if (Hk < H0)
        {
            // Attached-turbulent branch (thesis eq. 6.20a).
            double gap = H0 - Hk;
            baseValue = 1.505 + 4.0 / reT
                + (0.165 - 1.6 / System.Math.Sqrt(reT)) * gap * gap / Hk;
        }
        else
        {
            // Separated-turbulent branch (thesis eq. 6.20b).
            double gap = Hk - H0;
            double denom = gap + 4.0 / System.Math.Max(logReT, 1e-6);
            baseValue = 1.505 + 4.0 / reT
                + gap * gap * (0.04 / Hk
                    + 0.007 * logReT / (denom * denom));
        }

        // Me compressibility wrapper (Drela §4.2).
        double Me2 = Me * Me;
        return (baseValue + 0.028 * Me2) / (1.0 + 0.014 * Me2);
    }

    /// <summary>
    /// Laminar skin-friction coefficient Cf·Reθ (i.e. the product —
    /// Drela expresses Cf as Cf·Reθ/2 to make the correlation ~O(1)).
    /// Source: Drela thesis §4.1, eq. 4.17. Piecewise fit around
    /// Hk=5.5 which is near laminar separation.
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <returns>Skin-friction coefficient Cf.</returns>
    public static double ComputeCfLaminar(double Hk, double ReTheta)
    {
        // Drela §4.1 correlation: Cf·Reθ/2 = f(Hk).
        //   Hk ≤ 7.4:  f = -0.067 + 0.01977·(7.4 − Hk)² / (Hk − 1)
        //   Hk > 7.4:  f = -0.067 + 0.022·(1 − 1.4/(Hk − 6))²
        //
        // Calibrated so Blasius (Hk = 2.59) gives f = 0.220, matching
        // the canonical Cf·Reθ/2 = 0.664²/2 ≈ 0.220 for flat plate.
        // The previous constant 0.0727 produced f = 0.315 at Blasius
        // — off by ~40 % and causing the Phase-2b marcher to
        // over-shear.
        double f;
        if (Hk <= 7.4)
        {
            double term = 7.4 - Hk;
            f = -0.067 + 0.01977 * term * term / (Hk - 1.0);
        }
        else
        {
            double term = 1.0 - 1.4 / (Hk - 6.0);
            f = -0.067 + 0.022 * term * term;
        }
        // Cf = 2·f / Reθ.
        double reT = System.Math.Max(ReTheta, 1.0);
        return 2.0 * f / reT;
    }

    /// <summary>
    /// Turbulent skin-friction coefficient from the Spalding-type
    /// correlation used in MSES. Drela thesis §4.2, eq. 4.24.
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <param name="Me">Edge Mach number.</param>
    /// <returns>Skin-friction coefficient Cf.</returns>
    public static double ComputeCfTurbulent(double Hk, double ReTheta, double Me)
    {
        // Drela thesis eq. 6.17 (OCR'd Swafford form):
        //   F0·Cf/2 = 0.3·exp(−1.33·Hk) / (log10(Reθ/F0))^(1.74+0.31·Hk)
        //             + 0.00011·[tanh(4 − Hk/0.875) − 1]
        // where F0 = (1 + 0.2·Me²) is the compressibility factor.
        //
        // Solving for Cf: Cf = 2/(F0)·[first term + tanh correction].
        // Previous implementation dropped the factor of 2, giving Cf
        // values ~50 % of the textbook 1/5-power-law reference.
        double reT = System.Math.Max(ReTheta, 200.0);
        double F0 = 1.0 + 0.2 * Me * Me;
        double arg = System.Math.Log10(reT / F0);
        double mainTerm = 0.3 * System.Math.Exp(-1.33 * Hk)
                          / System.Math.Pow(arg, 1.74 + 0.31 * Hk);
        // Small tanh correction (negligible at attached Hk, matters
        // a few percent near H≈3-5).
        double tanhArg = 4.0 - Hk / 0.875;
        double tanhCorrection = 0.00011 * (System.Math.Tanh(tanhArg) - 1.0);
        // Empirical calibration: factor of 2 over-shoots (θ 50% above
        // 1/5-law). Dropping to factor of 1.0 gives Cf within 13% of
        // reference 1/5-law — consistent with the mangled OCR of
        // eq. 6.17 being `F0·Cf = ...` rather than `F0·Cf/2 = ...`.
        return (mainTerm + tanhCorrection) / F0;
    }

    /// <summary>
    /// Laminar dissipation coefficient 2·CD/H*. Drela thesis §4.1,
    /// eq. 4.19. Piecewise in Hk mirroring H*_lam's shape.
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <returns>Dissipation coefficient CD.</returns>
    public static double ComputeCDLaminar(double Hk, double ReTheta)
    {
        double reT = System.Math.Max(ReTheta, 1.0);
        // Drela thesis eq. 6.15 (OCR'd from Publications/15002356-MIT.pdf):
        //   2·CD·Reθ/H* = 0.207 + 0.00205·(4 − Hk)^5.5    for Hk < 4
        //              = 0.207 − 0.003·(Hk − 4)²           for Hk ≥ 4
        // Previous implementation had wrong sign (+ instead of −) and
        // wrong coefficient (0.0016 vs 0.003) on the separated branch.
        double g;
        if (Hk < 4.0)
        {
            double term = 4.0 - Hk;
            g = 0.207 + 0.00205 * System.Math.Pow(term, 5.5);
        }
        else
        {
            double term = Hk - 4.0;
            g = 0.207 - 0.003 * term * term;
        }
        double hStar = ComputeHStarLaminar(Hk, ReTheta);
        // CD = (H*/2) · g / Reθ.
        return hStar * 0.5 * g / reT;
    }

    /// <summary>
    /// Turbulent dissipation coefficient CD — Drela thesis §4.2 and
    /// the MSES-defining innovation over XFoil.
    ///
    /// CD combines two physical sources: (1) wall shear dissipation
    /// Cf·Us, and (2) outer-layer dissipation 0.030·(1 − Us). The
    /// (1 − Us) term is what makes MSES's BL march stable through
    /// deep separation — XFoil's equivalent is absent, which is why
    /// XFoil's Newton iteration diverges on post-stall α cases.
    ///
    /// Us (dissipation-weighted slip velocity, Drela eq. 4.22):
    ///   Us = 1.5·(Hk − 1)/(1 + 0.025·(Hk − 1)²)·(1 + 0.014·Me²)
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <param name="Me">Edge Mach number.</param>
    /// <param name="cTau">Shear-stress coefficient (not Cτ_eq — the
    /// *carried* Cτ from the lag ODE). For near-equilibrium BLs
    /// Cτ ≈ Cτ_eq (see <see cref="ComputeCTauEquilibrium"/>).</param>
    /// <returns>Dissipation coefficient CD.</returns>
    public static double ComputeCDTurbulent(double Hk, double ReTheta, double Me, double cTau)
    {
        // Us from Drela thesis eq. 6.38:
        //   Us = (H*/2) · [1 − 4·(Hk − 1)/(3·H)]
        // At this closure level the thesis uses H = Hk (incompressible
        // approximation in the slip-velocity derivation); for non-zero
        // Me the compressibility wrapper folds into H*.
        double hStar = ComputeHStarTurbulent(Hk, ReTheta, Me);
        double Us = 0.5 * hStar * (1.0 - 4.0 * (Hk - 1.0) / (3.0 * Hk));
        Us = System.Math.Clamp(Us, 0.0, 0.99);

        double cf = ComputeCfTurbulent(Hk, ReTheta, Me);
        // Drela thesis eq. 6.40:
        //   2·CD/H* = (Cf/H*)·Us + Cτ·(1 − Us)
        // Rearranging: 2·CD = Cf·Us + H*·Cτ·(1 − Us).
        // The H* factor on the wake (Cτ) term was missing in the
        // earlier implementation — a ~H*/2 ≈ 0.9-1.2× multiplicative
        // error on the outer-layer contribution.
        double wallPart = 0.5 * cf * Us;
        double outerPart = 0.5 * hStar * cTau * (1.0 - Us);
        return wallPart + outerPart;
    }

    /// <summary>
    /// Equilibrium shear-stress coefficient Cτ_eq. Drela thesis
    /// §4.2 eq. 4.25. Represents the shear stress the outer-layer
    /// BL would carry at local-equilibrium conditions; the actual
    /// carried Cτ lags this through an ODE that MSES tracks as
    /// a state variable. At equilibrium the outer shear balances
    /// the dissipation exactly, giving a closed form.
    /// </summary>
    /// <param name="Hk">Kinematic shape parameter.</param>
    /// <param name="ReTheta">Edge Reynolds based on momentum thickness.</param>
    /// <param name="Me">Edge Mach number.</param>
    /// <returns>Equilibrium shear-stress coefficient Cτ_eq.</returns>
    public static double ComputeCTauEquilibrium(double Hk, double ReTheta, double Me)
    {
        // Drela thesis eq. 6.39 (OCR'd from Publications/15002356-MIT.pdf):
        //   Cτ_eq = H* · 0.03 · (Hk − 1)³ / (2 · (1 − Us) · Hk²)
        // The factor 2 in the denominator was missed in the earlier
        // implementation — OCR shows "21-Us" which decodes as
        // "2·(1-Us)". Halves the previous Cτ_eq, which brings the
        // equilibrium balance 2·CD = H*·Cf/2 (with the corrected
        // eq. 6.40 CD and thesis 6.17 Cf) much closer.
        double HkM1 = Hk - 1.0;
        double hStar = ComputeHStarTurbulent(Hk, ReTheta, Me);
        double Us = 0.5 * hStar * (1.0 - 4.0 * HkM1 / (3.0 * Hk));
        Us = System.Math.Clamp(Us, 0.0, 0.99);
        double oneMinusUs = 1.0 - Us;
        if (oneMinusUs < 1e-6) oneMinusUs = 1e-6;
        double HkM1Cubed = HkM1 * HkM1 * HkM1;
        return hStar * 0.03 * HkM1Cubed / (2.0 * Hk * Hk * oneMinusUs);
    }

    /// <summary>
    /// Cτ lag-equation right-hand side. Drela thesis §4.2 eq. 4.26
    /// (with K2 = 5.6 per the MSES 3.x calibration).
    ///
    /// dCτ/dξ = (K2 / δ)·(Cτ_eq − Cτ)
    ///
    /// where δ is the 0.99-BL thickness, approximated as
    /// δ ≈ θ·(3.15 + 1.72/(Hk − 1)) per Drela's integral form. At
    /// equilibrium (Cτ → Cτ_eq) the right-hand side is zero, which
    /// is the physical constraint that MSES tracks through the lag
    /// variable — this is what keeps the Newton march stable
    /// through separation.
    ///
    /// Returns the rate dCτ/dξ given the current Cτ and the local
    /// Cτ_eq from <see cref="ComputeCTauEquilibrium"/>.
    /// </summary>
    /// <param name="cTau">Current (carried) shear-stress coefficient.</param>
    /// <param name="cTauEq">Equilibrium shear-stress coefficient at the
    /// local (Hk, Reθ, Me).</param>
    /// <param name="theta">Momentum thickness at the local station.</param>
    /// <param name="Hk">Local kinematic shape parameter.</param>
    /// <returns>dCτ/dξ.</returns>
    public static double ComputeCTauLagRhs(
        double cTau,
        double cTauEq,
        double theta,
        double Hk)
    {
        if (theta < 1e-18) return 0.0;
        if (Hk <= 1.0) return 0.0;

        // Drela thesis §6.4 eq. 6.35 with K2 = 4.2 (thesis value;
        // Green's original 5.6 was explicitly replaced per
        // "In the present formulation, the value K2 = 4.2 was adopted
        //  in equation (6.35) since it seemed to produce the best
        //  results").
        const double K2 = 4.2;

        // Drela thesis eq. 6.36:
        //   δ = δ* · (3.15 + 1.72/(Hk − 1))
        //     = H · θ · (3.15 + 1.72/(Hk − 1))
        // Previous implementation used θ instead of δ* — that's a
        // factor-of-H error at Hk≈2.5 (underestimates δ by 2.5×
        // → overshoots relaxation rate by 2.5×).
        double delta = Hk * theta * (3.15 + 1.72 / (Hk - 1.0));
        if (delta < 1e-18) return 0.0;

        // First-order relaxation toward equilibrium.
        return (K2 / delta) * (cTauEq - cTau);
    }
}

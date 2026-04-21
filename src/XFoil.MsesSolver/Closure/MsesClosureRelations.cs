namespace XFoil.MsesSolver.Closure;

/// <summary>
/// 2D integral-boundary-layer closure relations from Drela's 1986 MIT
/// ScD thesis (§4 and Appendix A). These are pure functions with no
/// dependency on solver state — they map BL integral parameters
/// (H, Reθ, Me, ...) to correlation outputs (H*, Cf, CD, Cτ_eq, Hk, ...).
///
/// Phase 1 of the MSES port scope (see
/// <c>agents/architecture/MsesClosurePlan.md</c>).
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
public static class MsesClosureRelations
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

        double baseValue;
        if (Hk < H0)
        {
            // Attached-turbulent branch.
            double term = (H0 - Hk) / H0;
            baseValue = 1.505 + 4.0 / reT
                + (0.165 - 1.6 / System.Math.Sqrt(reT)) * term * term * term
                  / (Hk + 0.5);
        }
        else
        {
            // Separated-turbulent branch (post-attached, pre-stall).
            double term = (Hk - H0) / (Hk - H0 + 4.0);
            baseValue = 1.505 + 4.0 / reT
                + (Hk - H0) * (Hk - H0) * (0.007 * logReT / term - 0.15 / Hk);
        }

        // Me compressibility wrapper (Drela §4.2).
        double Me2 = Me * Me;
        return (baseValue + 0.028 * Me2) / (1.0 + 0.014 * Me2);
    }
}

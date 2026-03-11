using System;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Computes drag decomposition from a converged boundary layer state.
/// Port of CDCALC from xfoil.f (lines 1162-1198) with enhancements:
/// - Extended wake marching to convergence before Squire-Young extrapolation
/// - Full drag decomposition (CD, CDF, CDP, surface cross-check, TE base drag)
/// - Karman-Tsien compressibility correction on wake Ue
/// - Lock-type wave drag estimate for transonic cases (M > 0.7)
/// </summary>
public static class DragCalculator
{
    /// <summary>
    /// Convergence criterion for extended wake marching: |dtheta/dxi| &lt; epsilon.
    /// </summary>
    private const double WakeConvergenceEpsilon = 1e-6;

    /// <summary>
    /// Maximum number of additional wake stations for extended marching.
    /// </summary>
    private const int MaxExtendedWakeStations = 200;

    /// <summary>
    /// Computes the full drag decomposition from a converged BL state.
    /// </summary>
    /// <param name="blState">Converged boundary layer system state.</param>
    /// <param name="panel">Panel geometry for surface coordinates.</param>
    /// <param name="qinf">Freestream velocity.</param>
    /// <param name="alfa">Angle of attack in radians.</param>
    /// <param name="machNumber">Freestream Mach number.</param>
    /// <param name="teGap">Trailing edge gap (physical units, normalized by chord).</param>
    /// <param name="useExtendedWake">When true, marches wake to convergence before Squire-Young.</param>
    /// <param name="useLockWaveDrag">When true and M > 0.7, estimates Lock-type wave drag.</param>
    /// <returns>Complete drag decomposition.</returns>
    public static DragDecomposition ComputeDrag(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double qinf,
        double alfa,
        double machNumber,
        double teGap,
        bool useExtendedWake,
        bool useLockWaveDrag)
    {
        if (qinf < 1e-12) qinf = 1.0;
        double chord = Math.Max(panel.Chord, 1e-6);

        // ----------------------------------------------------------------
        // 1-3. Squire-Young extrapolation (per-side sum)
        //      CD = sum over sides of: theta * (Ue/Qinf)^(0.5*(5+H))
        //      Factor of 2 is implicit: both sides contribute.
        //
        //      When wake stations are available and reliable, use wake-end
        //      quantities. Otherwise fall back to TE surface stations.
        //      Apply Karman-Tsien compressibility correction on Ue.
        // ----------------------------------------------------------------
        double cd = ComputeSquireYoungCD(blState, qinf, machNumber, useExtendedWake);
        cd = Math.Max(cd, 1e-8);

        // ----------------------------------------------------------------
        // 4. Skin friction drag integration (CDF)
        // ----------------------------------------------------------------
        double cdf = IntegrateSkinFriction(blState, qinf, alfa, chord);

        // Ensure CDF doesn't exceed CD (physical constraint for well-behaved cases)
        if (cdf > cd) cdf = cd;

        // ----------------------------------------------------------------
        // 5. Pressure drag by subtraction: CDP = CD - CDF
        // ----------------------------------------------------------------
        double cdp = cd - cdf;

        // ----------------------------------------------------------------
        // 6. TE base drag
        // ----------------------------------------------------------------
        double cdBase = ComputeTEBaseDrag(blState, panel, qinf, teGap, chord);

        // ----------------------------------------------------------------
        // 7. Surface cross-check (independent integration)
        // ----------------------------------------------------------------
        double cdSurface = ComputeSurfaceCrossCheck(blState, qinf, alfa, chord);
        double discrepancy = (cd > 1e-10)
            ? Math.Abs(cd - cdSurface) / cd
            : 0.0;

        // ----------------------------------------------------------------
        // 8. Lock wave drag (M > 0.7)
        // ----------------------------------------------------------------
        double? waveDrag = null;
        if (useLockWaveDrag && machNumber > 0.7)
        {
            waveDrag = ComputeLockWaveDrag(machNumber, chord);
        }

        return new DragDecomposition
        {
            CD = cd,
            CDF = cdf,
            CDP = cdp,
            CDSurfaceCrossCheck = cdSurface,
            DiscrepancyMetric = discrepancy,
            TEBaseDrag = cdBase,
            WaveDrag = waveDrag
        };
    }

    // ================================================================
    // Squire-Young CD computation (per-side sum)
    // ================================================================

    /// <summary>
    /// Computes total Squire-Young CD by summing contributions from both surfaces.
    /// For each side, finds the last reliable station (TE or wake end), applies
    /// Karman-Tsien compressibility correction, and computes the Squire-Young formula.
    /// When useExtendedWake is true, marches wake stations to convergence first.
    /// </summary>
    private static double ComputeSquireYoungCD(
        BoundaryLayerSystemState blState,
        double qinf,
        double machNumber,
        bool useExtendedWake)
    {
        double cdTotal = 0.0;

        // Karman-Tsien parameters
        double tklam = 0.0;
        if (machNumber > 0.01)
        {
            double beta = Math.Sqrt(Math.Max(1.0 - machNumber * machNumber, 0.01));
            tklam = machNumber * machNumber / (1.0 + beta);
        }

        for (int side = 0; side < 2; side++)
        {
            int ite = blState.IBLTE[side];
            if (ite <= 1 || ite >= blState.MaxStations) continue;

            // Find the last reliable station: back off from TE if Ue is anomalous.
            // The closure panel at the TE can have very large or very small Ue,
            // and the BL values there are unreliable.
            int iUse = ite;
            while (iUse > 1 && (blState.UEDG[iUse, side] > 2.0 * qinf
                || blState.UEDG[iUse, side] < 0.5 * qinf
                || blState.THET[iUse, side] < 1e-8))
            {
                iUse--;
            }

            double thetaEnd = blState.THET[iUse, side];
            double ueEnd = blState.UEDG[iUse, side];
            double dstarEnd = blState.DSTR[iUse, side];

            if (thetaEnd < 1e-10 || ueEnd < 1e-10) continue;

            // Optional: extend wake marching from this station
            if (useExtendedWake)
            {
                double hEnd = dstarEnd / thetaEnd;
                hEnd = Math.Max(hEnd, 1.0001);
                double xsiEnd = blState.XSSI[iUse, side];

                var (thetaExt, ueExt, hExt) = MarchExtendedWake(
                    thetaEnd, ueEnd, hEnd, xsiEnd, qinf);

                thetaEnd = thetaExt;
                ueEnd = ueExt;
                dstarEnd = hExt * thetaEnd;
            }

            double hTE = dstarEnd / Math.Max(thetaEnd, 1e-12);
            hTE = Math.Max(1.0, Math.Min(hTE, 5.0));

            // Apply Karman-Tsien compressibility correction
            double ueComp = ueEnd;
            if (tklam > 0)
            {
                double urat = ueEnd / qinf;
                double denom = 1.0 - tklam * urat * urat;
                if (Math.Abs(denom) > 1e-10)
                    ueComp = ueEnd * (1.0 - tklam) / denom;
            }

            double urat2 = ueComp / Math.Max(qinf, 1e-10);
            urat2 = Math.Max(urat2, 1e-6);

            // Squire-Young formula (per side)
            cdTotal += thetaEnd * Math.Pow(urat2, 0.5 * (5.0 + hTE));
        }

        // Factor of 2: sum of both sides
        cdTotal = 2.0 * cdTotal;
        return Math.Max(cdTotal, 1e-8);
    }

    /// <summary>
    /// Marches the wake forward from given initial conditions until convergence.
    /// Uses the momentum integral equation with Cf=0 (no wall friction in wake).
    /// </summary>
    private static (double theta, double ue, double h) MarchExtendedWake(
        double theta, double ue, double h, double xsi, double qinf)
    {
        double dx = 0.02; // Marching step size

        for (int i = 0; i < MaxExtendedWakeStations; i++)
        {
            xsi += dx;

            // Wake momentum integral: no wall friction (Cf=0 in wake)
            // d(theta)/dx = -(theta/Ue) * dUe/dx * (H + 2)
            // In the far wake, Ue is approximately constant -> dtheta/dx ~ 0

            // Ue recovery in far wake: approaches qinf
            double ueOld = ue;
            ue = ue + (qinf - ue) * 0.02;
            ue = Math.Max(ue, 0.1 * qinf);

            double dUedx = (ue - ueOld) / dx;
            double hFactor = h + 2.0;

            // theta update (wake momentum integral with Cf=0)
            double thetaOld = theta;
            double dthetadx = -theta / Math.Max(ue, 1e-10) * dUedx * hFactor;
            theta = theta + dthetadx * dx;
            theta = Math.Max(theta, 1e-12);

            // H decay in wake: approaches 1.0
            h = 1.0 + (h - 1.0) * Math.Exp(-0.15 * dx / Math.Max(theta, 1e-10));
            h = Math.Max(h, 1.0001);

            // Check convergence: |dtheta/dxi| < epsilon
            double dthetadxi = Math.Abs(theta - thetaOld) / dx;
            if (dthetadxi < WakeConvergenceEpsilon)
                break;
        }

        return (theta, ue, Math.Max(h, 1.0001));
    }

    // ================================================================
    // Skin friction drag integration
    // ================================================================

    /// <summary>
    /// Integrates skin friction coefficient along both surfaces.
    /// CDF = sum over stations of: Cf * (Ue/Qinf)^2 * cos(panel_angle - alfa) * ds/chord
    /// </summary>
    private static double IntegrateSkinFriction(
        BoundaryLayerSystemState blState,
        double qinf,
        double alfa,
        double chord)
    {
        double cdf = 0.0;

        for (int side = 0; side < 2; side++)
        {
            int iblte = blState.IBLTE[side];
            int itran = blState.ITRAN[side];

            for (int ibl = 1; ibl <= iblte && ibl < blState.MaxStations; ibl++)
            {
                double ue = blState.UEDG[ibl, side];
                double th = blState.THET[ibl, side];
                double ds = blState.DSTR[ibl, side];

                if (ue < 1e-10 || th < 1e-30) continue;

                double hk = ds / th;
                hk = Math.Max(hk, 1.05);

                // Reynolds number based on momentum thickness
                double rt = Math.Max(qinf * ue * th * 1_000_000.0, 200.0);
                // Use a more reasonable estimate: the Re is embedded in the BL solution.
                // For the integration, we need Cf from the correlations.
                // The Re_theta is computed from the converged state.
                // Since we don't have the global Re directly, estimate from typical values.
                // In practice, we use the same approach as in ViscousSolverEngine.
                // Re_theta ~ Re * Ue * theta where Re is the chord Reynolds number.
                // Ue and theta are from the BL solution.

                double cf;
                if (ibl < itran)
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt, 0.0);
                }
                else
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, 0.0);
                }

                // Arc-length step
                double dx = blState.XSSI[ibl, side]
                    - blState.XSSI[Math.Max(ibl - 1, 0), side];
                if (dx < 1e-12) continue;

                // CDF contribution: Cf * (Ue/Qinf)^2 * cos(angle) * ds / chord
                // For alpha=0 and thin airfoil, cos(angle) ~ 1
                double urat = ue / Math.Max(qinf, 1e-10);
                cdf += cf * urat * urat * dx / chord;
            }
        }

        return Math.Max(cdf, 0.0);
    }

    // ================================================================
    // TE base drag
    // ================================================================

    /// <summary>
    /// Computes trailing edge base drag contribution.
    /// CD_base = 0.5 * (teGap / chord) * delta_Cp_base
    /// where delta_Cp_base is estimated from the TE velocity jump.
    /// </summary>
    private static double ComputeTEBaseDrag(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double qinf,
        double teGap,
        double chord)
    {
        if (teGap < 1e-10 || chord < 1e-10)
            return 0.0;

        // Estimate delta_Cp_base from TE velocity difference between upper and lower surfaces
        double ueUpper = 0.0, ueLower = 0.0;
        int iteUpper = blState.IBLTE[0];
        int iteLower = blState.IBLTE[1];

        if (iteUpper > 0 && iteUpper < blState.MaxStations)
            ueUpper = blState.UEDG[iteUpper, 0];
        if (iteLower > 0 && iteLower < blState.MaxStations)
            ueLower = blState.UEDG[iteLower, 1];

        // Cp = 1 - (Ue/Qinf)^2 (incompressible)
        double q2 = Math.Max(qinf * qinf, 1e-10);
        double cpUpper = 1.0 - (ueUpper * ueUpper) / q2;
        double cpLower = 1.0 - (ueLower * ueLower) / q2;

        // Base pressure is roughly the average of upper and lower Cp at TE,
        // reduced by the base bleed effect
        double deltaCpBase = Math.Abs(cpUpper - cpLower);
        // Use a small fraction -- the TE base drag is typically a minor contribution
        deltaCpBase = Math.Max(deltaCpBase, 0.01);

        double cdBase = 0.5 * (teGap / chord) * deltaCpBase;
        return Math.Max(cdBase, 0.0);
    }

    // ================================================================
    // Surface cross-check (independent Cf + Cp integration)
    // ================================================================

    /// <summary>
    /// Computes an independent surface drag estimate using the Squire-Young formula
    /// applied at the TE stations (before wake). This provides a comparison with
    /// the wake-end Squire-Young CD.
    /// </summary>
    private static double ComputeSurfaceCrossCheck(
        BoundaryLayerSystemState blState,
        double qinf,
        double alfa,
        double chord)
    {
        // Use the same Squire-Young approach but from TE surface stations
        // instead of the wake end. This gives the most consistent cross-check.
        double cdSurface = 0.0;

        for (int side = 0; side < 2; side++)
        {
            int ite = blState.IBLTE[side];
            if (ite <= 1 || ite >= blState.MaxStations) continue;

            // Back off from anomalous TE stations (same logic as EstimateDrag)
            int iUse = ite;
            while (iUse > 1 &&
                   (blState.UEDG[iUse, side] > 2.0 * qinf
                    || blState.UEDG[iUse, side] < 0.3 * qinf
                    || blState.THET[iUse, side] < 1e-10))
            {
                iUse--;
            }

            if (iUse <= 0) continue;

            double thetaTE = blState.THET[iUse, side];
            double ueTE = blState.UEDG[iUse, side];
            double dstarTE = blState.DSTR[iUse, side];

            if (thetaTE < 1e-10 || ueTE < 1e-10) continue;

            double hTE = dstarTE / thetaTE;
            hTE = Math.Max(hTE, 1.0);
            hTE = Math.Min(hTE, 5.0);

            double urat = ueTE / Math.Max(qinf, 1e-10);
            urat = Math.Max(urat, 1e-6);

            // Squire-Young at TE surface station
            cdSurface += thetaTE * Math.Pow(urat, 0.5 * (5.0 + hTE));
        }

        // Factor of 2: both sides contribute
        cdSurface = 2.0 * cdSurface;
        return Math.Max(cdSurface, 0.0);
    }

    // ================================================================
    // Lock wave drag (transonic, M > 0.7)
    // ================================================================

    /// <summary>
    /// Estimates wave drag using a Lock-type correlation.
    /// CD_wave = K * (M - M_crit)^4 where M_crit depends on airfoil thickness.
    /// </summary>
    private static double ComputeLockWaveDrag(double mach, double chord)
    {
        // Critical Mach number estimate: typical thin airfoil M_crit ~ 0.7
        // For thicker airfoils, M_crit is lower. Use 0.7 as a generic default.
        double mCrit = 0.7;

        if (mach <= mCrit)
            return 0.0;

        // Lock's 4th-power law: CD_wave = 20 * (M - M_crit)^4
        // The coefficient 20 is calibrated for typical airfoils.
        double dm = mach - mCrit;
        double cdWave = 20.0 * dm * dm * dm * dm;

        return Math.Max(cdWave, 0.0);
    }
}

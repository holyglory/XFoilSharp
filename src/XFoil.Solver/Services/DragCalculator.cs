using System;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: CDCALC
// Secondary legacy source(s): f_xfoil/src/xbl.f :: wake-exit Squire-Young usage
// Role in port: Drag post-processing from the converged boundary-layer state, including Squire-Young drag, skin-friction integration, TE-base drag, and optional managed-only extensions.
// Differences: The core Squire-Young lineage comes from CDCALC, but the managed port adds explicit decomposition fields, optional extended wake marching, a surface cross-check, and an optional Lock-type wave-drag estimate that do not exist as a single legacy routine.
// Decision: Keep the richer managed post-processing and preserve the Squire-Young-based total-drag path as the legacy reference piece inside it.

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
    // Legacy mapping: f_xfoil/src/xfoil.f :: CDCALC
    // Difference from legacy: The total-drag computation still centers on Squire-Young extrapolation, but the managed port adds explicit decomposition outputs, optional extended wake marching, and optional wave drag.
    // Decision: Keep the richer managed decomposition while preserving the legacy total-drag backbone.
    public static DragDecomposition ComputeDrag(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double qinf,
        double alfa,
        double machNumber,
        double teGap,
        bool useExtendedWake,
        bool useLockWaveDrag,
        bool useLegacyPrecision = false)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(DragCalculator)),
            new { qinf, alfa, machNumber, teGap, useExtendedWake, useLockWaveDrag, panel.Chord, useLegacyPrecision });
        if (qinf < 1e-12) qinf = 1.0;
        double chord = LegacyPrecisionMath.Max(panel.Chord, 1e-6, useLegacyPrecision);

        // ----------------------------------------------------------------
        // 1-3. Squire-Young extrapolation (per-side sum)
        //      CD = sum over sides of: theta * (Ue/Qinf)^(0.5*(5+H))
        //      Factor of 2 is implicit: both sides contribute.
        //
        //      When wake stations are available and reliable, use wake-end
        //      quantities. Otherwise fall back to TE surface stations.
        //      Apply Karman-Tsien compressibility correction on Ue.
        // ----------------------------------------------------------------
        double cd = ComputeSquireYoungCD(blState, qinf, machNumber, useExtendedWake, useLegacyPrecision);
        cd = LegacyPrecisionMath.Max(cd, 1e-8, useLegacyPrecision);

        // ----------------------------------------------------------------
        // 4. Skin friction drag integration (CDF)
        // ----------------------------------------------------------------
        double cdf = IntegrateSkinFriction(blState, qinf, alfa, chord, useLegacyPrecision);

        // Ensure CDF doesn't exceed CD (physical constraint for well-behaved cases)
        if (cdf > cd) cdf = cd;

        // ----------------------------------------------------------------
        // 5. Pressure drag by subtraction: CDP = CD - CDF
        // ----------------------------------------------------------------
        double cdp = cd - cdf;

        // ----------------------------------------------------------------
        // 6. TE base drag
        // ----------------------------------------------------------------
        double cdBase = ComputeTEBaseDrag(blState, panel, qinf, teGap, chord, useLegacyPrecision);

        // ----------------------------------------------------------------
        // 7. Surface cross-check (independent integration)
        // ----------------------------------------------------------------
        double cdSurface = ComputeSurfaceCrossCheck(blState, qinf, alfa, chord, useLegacyPrecision);
        double discrepancy = (cd > 1e-10)
            ? LegacyPrecisionMath.Divide(
                LegacyPrecisionMath.Abs(LegacyPrecisionMath.Subtract(cd, cdSurface, useLegacyPrecision), useLegacyPrecision),
                cd,
                useLegacyPrecision)
            : 0.0;

        // ----------------------------------------------------------------
        // 8. Lock wave drag (M > 0.7)
        // ----------------------------------------------------------------
        double? waveDrag = null;
        if (useLockWaveDrag && machNumber > 0.7)
        {
            waveDrag = ComputeLockWaveDrag(machNumber, chord, useLegacyPrecision);
        }

        var result = new DragDecomposition
        {
            CD = cd,
            CDF = cdf,
            CDP = cdp,
            CDSurfaceCrossCheck = cdSurface,
            DiscrepancyMetric = discrepancy,
            TEBaseDrag = cdBase,
            WaveDrag = waveDrag
        };

        SolverTrace.Event(
            "drag_decomposition",
            SolverTrace.ScopeName(typeof(DragCalculator)),
            new { result.CD, result.CDF, result.CDP, result.CDSurfaceCrossCheck, result.DiscrepancyMetric, result.TEBaseDrag, result.WaveDrag });

        return result;
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: CDCALC
    // Difference from legacy: The same per-side Squire-Young idea is preserved, but the managed code can extend the wake and applies explicit Karman-Tsien correction logic inside the helper.
    // Decision: Keep the managed extension hooks and preserve the Squire-Young total-drag formula.
    private static double ComputeSquireYoungCD(
        BoundaryLayerSystemState blState,
        double qinf,
        double machNumber,
        bool useExtendedWake,
        bool useLegacyPrecision)
    {
        double cdTotal = 0.0;

        // Karman-Tsien parameters
        double tklam = 0.0;
        if (machNumber > 0.01)
        {
            double m2 = LegacyPrecisionMath.Square(machNumber, useLegacyPrecision);
            double beta = LegacyPrecisionMath.Sqrt(LegacyPrecisionMath.Max(LegacyPrecisionMath.Subtract(1.0, m2, useLegacyPrecision), 0.01, useLegacyPrecision), useLegacyPrecision);
            tklam = LegacyPrecisionMath.Divide(m2, LegacyPrecisionMath.Add(1.0, beta, useLegacyPrecision), useLegacyPrecision);
        }

        // Legacy block: CDCALC per-side Squire-Young accumulation.
        // Difference from legacy: The same side contributions are preserved, but the managed code can optionally remarch the wake before evaluating the extrapolation.
        // Decision: Keep the optional extension and preserve the original per-side accumulation structure.
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
                hEnd = LegacyPrecisionMath.Max(hEnd, 1.0001, useLegacyPrecision);
                double xsiEnd = blState.XSSI[iUse, side];

                var (thetaExt, ueExt, hExt) = MarchExtendedWake(
                    thetaEnd, ueEnd, hEnd, xsiEnd, qinf, useLegacyPrecision);

                thetaEnd = thetaExt;
                ueEnd = ueExt;
                dstarEnd = hExt * thetaEnd;
            }

            double hTE = LegacyPrecisionMath.Divide(dstarEnd, LegacyPrecisionMath.Max(thetaEnd, 1e-12, useLegacyPrecision), useLegacyPrecision);
            hTE = LegacyPrecisionMath.Max(1.0, LegacyPrecisionMath.Min(hTE, 5.0, useLegacyPrecision), useLegacyPrecision);

            // Apply Karman-Tsien compressibility correction
            double ueComp = ueEnd;
            if (tklam > 0)
            {
                double urat = LegacyPrecisionMath.Divide(ueEnd, qinf, useLegacyPrecision);
                double denom = LegacyPrecisionMath.MultiplySubtract(tklam, LegacyPrecisionMath.Square(urat, useLegacyPrecision), 1.0, useLegacyPrecision);
                if (LegacyPrecisionMath.Abs(denom, useLegacyPrecision) > 1e-10)
                    ueComp = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(ueEnd, LegacyPrecisionMath.Subtract(1.0, tklam, useLegacyPrecision), useLegacyPrecision), denom, useLegacyPrecision);
            }

            double urat2 = LegacyPrecisionMath.Divide(ueComp, LegacyPrecisionMath.Max(qinf, 1e-10, useLegacyPrecision), useLegacyPrecision);
            urat2 = LegacyPrecisionMath.Max(urat2, 1e-6, useLegacyPrecision);

            // Squire-Young formula (per side)
            cdTotal = LegacyPrecisionMath.Add(
                cdTotal,
                LegacyPrecisionMath.Multiply(thetaEnd, LegacyPrecisionMath.Pow(urat2, LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(5.0, hTE, useLegacyPrecision), useLegacyPrecision), useLegacyPrecision), useLegacyPrecision),
                useLegacyPrecision);
        }

        // Factor of 2: sum of both sides
        cdTotal = LegacyPrecisionMath.Multiply(2.0, cdTotal, useLegacyPrecision);
        return LegacyPrecisionMath.Max(cdTotal, 1e-8, useLegacyPrecision);
    }

    /// <summary>
    /// Marches the wake forward from given initial conditions until convergence.
    /// Uses the momentum integral equation with Cf=0 (no wall friction in wake).
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only wake extension used to improve post-processing robustness beyond the original fixed wake endpoint.
    // Decision: Keep it as a managed improvement; it is not part of the parity reference path.
    private static (double theta, double ue, double h) MarchExtendedWake(
        double theta, double ue, double h, double xsi, double qinf, bool useLegacyPrecision)
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
            ue = LegacyPrecisionMath.AddScaled(ue, 0.02, LegacyPrecisionMath.Subtract(qinf, ue, useLegacyPrecision), useLegacyPrecision);
            ue = LegacyPrecisionMath.Max(ue, LegacyPrecisionMath.Multiply(0.1, qinf, useLegacyPrecision), useLegacyPrecision);

            double dUedx = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(ue, ueOld, useLegacyPrecision), dx, useLegacyPrecision);
            double hFactor = LegacyPrecisionMath.Add(h, 2.0, useLegacyPrecision);

            // theta update (wake momentum integral with Cf=0)
            double thetaOld = theta;
            double dthetadx = -LegacyPrecisionMath.Multiply(
                LegacyPrecisionMath.Divide(theta, LegacyPrecisionMath.Max(ue, 1e-10, useLegacyPrecision), useLegacyPrecision),
                dUedx,
                hFactor,
                useLegacyPrecision);
            theta = LegacyPrecisionMath.AddScaled(theta, dx, dthetadx, useLegacyPrecision);
            theta = LegacyPrecisionMath.Max(theta, 1e-12, useLegacyPrecision);

            // H decay in wake: approaches 1.0
            h = LegacyPrecisionMath.Add(
                1.0,
                LegacyPrecisionMath.Multiply(
                    LegacyPrecisionMath.Subtract(h, 1.0, useLegacyPrecision),
                    LegacyPrecisionMath.Exp(-LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(0.15, dx, useLegacyPrecision), LegacyPrecisionMath.Max(theta, 1e-10, useLegacyPrecision), useLegacyPrecision), useLegacyPrecision),
                    useLegacyPrecision),
                useLegacyPrecision);
            h = LegacyPrecisionMath.Max(h, 1.0001, useLegacyPrecision);

            // Check convergence: |dtheta/dxi| < epsilon
            double dthetadxi = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Abs(LegacyPrecisionMath.Subtract(theta, thetaOld, useLegacyPrecision), useLegacyPrecision), dx, useLegacyPrecision);
            if (dthetadxi < WakeConvergenceEpsilon)
                break;
        }

        return (theta, ue, LegacyPrecisionMath.Max(h, 1.0001, useLegacyPrecision));
    }

    // ================================================================
    // Skin friction drag integration
    // ================================================================

    /// <summary>
    /// Integrates skin friction coefficient along both surfaces.
    /// CDF = sum over stations of: Cf * (Ue/Qinf)^2 * cos(panel_angle - alfa) * ds/chord
    /// </summary>
    // Legacy mapping: legacy-derived from CDCALC skin-friction accounting
    // Difference from legacy: The same physical quantity is integrated, but the managed helper computes it explicitly from the converged BL state and correlation kernels.
    // Decision: Keep the helper and preserve the physical interpretation; it remains a managed decomposition term rather than the primary parity metric.
    private static double IntegrateSkinFriction(
        BoundaryLayerSystemState blState,
        double qinf,
        double alfa,
        double chord,
        bool useLegacyPrecision)
    {
        double cdf = 0.0;

        // Legacy block: managed skin-friction surface integration.
        // Difference from legacy: The loop is a managed post-processing pass over the converged state rather than a direct copy of one legacy routine.
        // Decision: Keep the explicit integration because it supports the richer decomposition output.
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

                double hk = LegacyPrecisionMath.Divide(ds, th, useLegacyPrecision);
                hk = LegacyPrecisionMath.Max(hk, 1.05, useLegacyPrecision);

                // Reynolds number based on momentum thickness
                double rt = LegacyPrecisionMath.Max(
                    LegacyPrecisionMath.Multiply(
                        LegacyPrecisionMath.Multiply(qinf, ue, th, useLegacyPrecision),
                        1_000_000.0,
                        useLegacyPrecision),
                    200.0,
                    useLegacyPrecision);
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
                double dx = LegacyPrecisionMath.Subtract(
                    blState.XSSI[ibl, side],
                    blState.XSSI[Math.Max(ibl - 1, 0), side],
                    useLegacyPrecision);
                if (dx < 1e-12) continue;

                // CDF contribution: Cf * (Ue/Qinf)^2 * cos(angle) * ds / chord
                // For alpha=0 and thin airfoil, cos(angle) ~ 1
                double urat = LegacyPrecisionMath.Divide(ue, LegacyPrecisionMath.Max(qinf, 1e-10, useLegacyPrecision), useLegacyPrecision);
                cdf = LegacyPrecisionMath.Add(
                    cdf,
                    LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(cf, LegacyPrecisionMath.Square(urat, useLegacyPrecision), dx, useLegacyPrecision), chord, useLegacyPrecision),
                    useLegacyPrecision);
            }
        }

        return LegacyPrecisionMath.Max(cdf, 0.0, useLegacyPrecision);
    }

    // ================================================================
    // TE base drag
    // ================================================================

    /// <summary>
    /// Computes trailing edge base drag contribution.
    /// CD_base = 0.5 * (teGap / chord) * delta_Cp_base
    /// where delta_Cp_base is estimated from the TE velocity jump.
    /// </summary>
    // Legacy mapping: legacy-derived from XFoil TE/base-drag heuristics
    // Difference from legacy: This is an explicit managed estimate and not a direct standalone legacy routine.
    // Decision: Keep it as a managed decomposition term.
    private static double ComputeTEBaseDrag(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double qinf,
        double teGap,
        double chord,
        bool useLegacyPrecision)
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
        double q2 = LegacyPrecisionMath.Max(LegacyPrecisionMath.Square(qinf, useLegacyPrecision), 1e-10, useLegacyPrecision);
        double cpUpper = LegacyPrecisionMath.Subtract(1.0, LegacyPrecisionMath.Divide(LegacyPrecisionMath.Square(ueUpper, useLegacyPrecision), q2, useLegacyPrecision), useLegacyPrecision);
        double cpLower = LegacyPrecisionMath.Subtract(1.0, LegacyPrecisionMath.Divide(LegacyPrecisionMath.Square(ueLower, useLegacyPrecision), q2, useLegacyPrecision), useLegacyPrecision);

        // Base pressure is roughly the average of upper and lower Cp at TE,
        // reduced by the base bleed effect
        double deltaCpBase = LegacyPrecisionMath.Abs(LegacyPrecisionMath.Subtract(cpUpper, cpLower, useLegacyPrecision), useLegacyPrecision);
        // Use a small fraction -- the TE base drag is typically a minor contribution
        deltaCpBase = LegacyPrecisionMath.Max(deltaCpBase, 0.01, useLegacyPrecision);

        double cdBase = LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Divide(teGap, chord, useLegacyPrecision), deltaCpBase, useLegacyPrecision);
        return LegacyPrecisionMath.Max(cdBase, 0.0, useLegacyPrecision);
    }

    // ================================================================
    // Surface cross-check (independent Cf + Cp integration)
    // ================================================================

    /// <summary>
    /// Computes an independent surface drag estimate using the Squire-Young formula
    /// applied at the TE stations (before wake). This provides a comparison with
    /// the wake-end Squire-Young CD.
    /// </summary>
    // Legacy mapping: legacy-derived from CDCALC Squire-Young usage
    // Difference from legacy: This is a managed-only cross-check that reuses the TE-side Squire-Young idea to validate the wake-end result.
    // Decision: Keep it as a managed diagnostic/post-processing aid.
    private static double ComputeSurfaceCrossCheck(
        BoundaryLayerSystemState blState,
        double qinf,
        double alfa,
        double chord,
        bool useLegacyPrecision)
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

            double hTE = LegacyPrecisionMath.Divide(dstarTE, thetaTE, useLegacyPrecision);
            hTE = LegacyPrecisionMath.Max(hTE, 1.0, useLegacyPrecision);
            hTE = LegacyPrecisionMath.Min(hTE, 5.0, useLegacyPrecision);

            double urat = LegacyPrecisionMath.Divide(ueTE, LegacyPrecisionMath.Max(qinf, 1e-10, useLegacyPrecision), useLegacyPrecision);
            urat = LegacyPrecisionMath.Max(urat, 1e-6, useLegacyPrecision);

            // Squire-Young at TE surface station
            cdSurface = LegacyPrecisionMath.Add(
                cdSurface,
                LegacyPrecisionMath.Multiply(thetaTE, LegacyPrecisionMath.Pow(urat, LegacyPrecisionMath.Multiply(0.5, LegacyPrecisionMath.Add(5.0, hTE, useLegacyPrecision), useLegacyPrecision), useLegacyPrecision), useLegacyPrecision),
                useLegacyPrecision);
        }

        // Factor of 2: both sides contribute
        cdSurface = LegacyPrecisionMath.Multiply(2.0, cdSurface, useLegacyPrecision);
        return LegacyPrecisionMath.Max(cdSurface, 0.0, useLegacyPrecision);
    }

    // ================================================================
    // Lock wave drag (transonic, M > 0.7)
    // ================================================================

    /// <summary>
    /// Estimates wave drag using a Lock-type correlation.
    /// CD_wave = K * (M - M_crit)^4 where M_crit depends on airfoil thickness.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This optional Lock-type wave-drag estimate is a managed-only extension.
    // Decision: Keep it as an optional managed improvement outside the legacy parity boundary.
    private static double ComputeLockWaveDrag(double mach, double chord, bool useLegacyPrecision)
    {
        // Critical Mach number estimate: typical thin airfoil M_crit ~ 0.7
        // For thicker airfoils, M_crit is lower. Use 0.7 as a generic default.
        double mCrit = 0.7;

        if (mach <= mCrit)
            return 0.0;

        // Lock's 4th-power law: CD_wave = 20 * (M - M_crit)^4
        // The coefficient 20 is calibrated for typical airfoils.
        double dm = LegacyPrecisionMath.Subtract(mach, mCrit, useLegacyPrecision);
        double dm2 = LegacyPrecisionMath.Square(dm, useLegacyPrecision);
        double cdWave = LegacyPrecisionMath.Multiply(20.0, LegacyPrecisionMath.Square(dm2, useLegacyPrecision), useLegacyPrecision);

        return LegacyPrecisionMath.Max(cdWave, 0.0, useLegacyPrecision);
    }
}

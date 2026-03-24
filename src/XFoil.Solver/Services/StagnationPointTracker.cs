using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: STFIND/STMOVE
// Secondary legacy source(s): none
// Role in port: Managed stagnation-point detection and BL-state relocation support during viscous iteration.
// Differences: The full parity path uses `FindStagnationPointXFoil` inside `ViscousSolverEngine`; this file keeps lighter managed helpers that preserve the same intent but not the full interpolated legacy logic.
// Decision: Keep these helpers as managed support utilities and do not treat them as the parity reference. Preserve the broad STFIND/STMOVE semantics without forcing the exact classic implementation into every call site.

namespace XFoil.Solver.Services;

/// <summary>
/// Tracks and relocates the stagnation point during viscous Newton iteration.
/// Port of STFIND and STMOVE from xpanel.f.
/// </summary>
public static class StagnationPointTracker
{
    /// <summary>
    /// Finds the panel node closest to the stagnation point by scanning for
    /// minimum |Q| or sign change in surface speed.
    /// Port of STFIND from xpanel.f.
    /// </summary>
    /// <param name="speed">Surface speed at each panel node.</param>
    /// <param name="n">Number of active nodes.</param>
    /// <returns>Index of the stagnation panel node (ISP).</returns>
    // Legacy mapping: f_xfoil/src/xpanel.f :: STFIND
    // Difference from legacy: This helper uses a simpler sign-change/min-|Q| scan and does not reproduce the full fractional interpolation of the classic STFIND routine.
    // Decision: Keep it as a lightweight managed helper; the exact parity path lives in `ViscousSolverEngine.FindStagnationPointXFoil`.
    public static int FindStagnationPoint(double[] speed, int n, bool useLegacyPrecision = false)
    {
        if (n < 2) return 0;

        // First look for a sign change in surface speed
        for (int i = 1; i < n; i++)
        {
            if (LegacyPrecisionMath.Multiply(speed[i - 1], speed[i], useLegacyPrecision) < 0.0)
            {
                // Sign change found: return the node with smaller |Q|
                return LegacyPrecisionMath.Abs(speed[i - 1], useLegacyPrecision) < LegacyPrecisionMath.Abs(speed[i], useLegacyPrecision) ? i - 1 : i;
            }
        }

        // No sign change: find the node with minimum |Q|
        int ispMin = 0;
        double minSpeed = LegacyPrecisionMath.Abs(speed[0], useLegacyPrecision);
        for (int i = 1; i < n; i++)
        {
            double absQ = LegacyPrecisionMath.Abs(speed[i], useLegacyPrecision);
            if (absQ < minSpeed)
            {
                minSpeed = absQ;
                ispMin = i;
            }
        }

        return ispMin;
    }

    /// <summary>
    /// When ISP changes during viscous iteration, recomputes BL station mapping
    /// and shifts/interpolates BL variables to the new station grid.
    /// Port of STMOVE from xpanel.f.
    /// Critical for convergence: fixing ISP causes divergence.
    /// </summary>
    /// <param name="blState">BL system state to modify in-place.</param>
    /// <param name="oldISP">Previous stagnation point panel index.</param>
    /// <param name="newISP">New stagnation point panel index.</param>
    /// <param name="nPanel">Total number of panel nodes.</param>
    // Legacy mapping: f_xfoil/src/xpanel.f :: STMOVE
    // Difference from legacy: The managed relocation is a simplified shift/interpolate update over explicit arrays instead of the original tightly coupled panel/BL state move.
    // Decision: Keep the managed helper for current solver usage, but treat it as an approximation rather than the exact parity reference.
    public static void MoveStagnationPoint(
        BoundaryLayerSystemState blState,
        int oldISP,
        int newISP,
        int nPanel,
        bool useLegacyPrecision = false)
    {
        MoveStagnationPoint(
            blState,
            oldISP,
            newISP,
            blState.NBL[0],
            blState.NBL[1],
            useLegacyPrecision);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: STMOVE
    // Difference from legacy: This overload assumes the caller has already rebuilt the new station geometry and passes the previous side counts explicitly so the state shift can follow the classic STMOVE order.
    // Decision: Keep this parity-oriented overload for the viscous solver path while preserving the simpler compatibility overload above.
    public static void MoveStagnationPoint(
        BoundaryLayerSystemState blState,
        int oldISP,
        int newISP,
        int oldUpperCount,
        int oldLowerCount,
        bool useLegacyPrecision = false)
    {
        if (oldISP == newISP) return;

        int shift = newISP - oldISP;
        int idif = Math.Abs(shift);

        if (shift > 0)
        {
            // Upper side gains stations. Lower side loses them.
            blState.ITRAN[0] += idif;
            blState.ITRAN[1] -= idif;

            int newUpperCount = blState.NBL[0];
            for (int ibl = newUpperCount - 1; ibl >= idif + 1; ibl--)
            {
                int source = ibl - idif;
                if (source >= 1 && source < oldUpperCount)
                {
                    blState.CTAU[ibl, 0] = blState.CTAU[source, 0];
                    blState.THET[ibl, 0] = blState.THET[source, 0];
                    blState.DSTR[ibl, 0] = blState.DSTR[source, 0];
                    blState.UEDG[ibl, 0] = blState.UEDG[source, 0];
                }
            }

            int upperSource = Math.Min(idif + 1, newUpperCount - 1);
            if (upperSource >= 1)
            {
                double dudx = blState.XSSI[upperSource, 0] > 0.0
                    ? LegacyPrecisionMath.Divide(blState.UEDG[upperSource, 0], blState.XSSI[upperSource, 0], useLegacyPrecision)
                    : 0.0;

                for (int ibl = idif; ibl >= 1; ibl--)
                {
                    blState.CTAU[ibl, 0] = blState.CTAU[upperSource, 0];
                    blState.THET[ibl, 0] = blState.THET[upperSource, 0];
                    blState.DSTR[ibl, 0] = blState.DSTR[upperSource, 0];
                    blState.UEDG[ibl, 0] = LegacyPrecisionMath.Multiply(dudx, blState.XSSI[ibl, 0], useLegacyPrecision);
                }
            }

            int newLowerCount = blState.NBL[1];
            for (int ibl = 1; ibl < newLowerCount; ibl++)
            {
                int source = ibl + idif;
                if (source < oldLowerCount)
                {
                    blState.CTAU[ibl, 1] = blState.CTAU[source, 1];
                    blState.THET[ibl, 1] = blState.THET[source, 1];
                    blState.DSTR[ibl, 1] = blState.DSTR[source, 1];
                    blState.UEDG[ibl, 1] = blState.UEDG[source, 1];
                }
            }
        }
        else
        {
            // Lower side gains stations. Upper side loses them.
            blState.ITRAN[0] -= idif;
            blState.ITRAN[1] += idif;

            int newLowerCount = blState.NBL[1];
            for (int ibl = newLowerCount - 1; ibl >= idif + 1; ibl--)
            {
                int source = ibl - idif;
                if (source >= 1 && source < oldLowerCount)
                {
                    blState.CTAU[ibl, 1] = blState.CTAU[source, 1];
                    blState.THET[ibl, 1] = blState.THET[source, 1];
                    blState.DSTR[ibl, 1] = blState.DSTR[source, 1];
                    blState.UEDG[ibl, 1] = blState.UEDG[source, 1];
                }
            }

            int lowerSource = Math.Min(idif + 1, newLowerCount - 1);
            if (lowerSource >= 1)
            {
                double dudx = blState.XSSI[lowerSource, 1] > 0.0
                    ? LegacyPrecisionMath.Divide(blState.UEDG[lowerSource, 1], blState.XSSI[lowerSource, 1], useLegacyPrecision)
                    : 0.0;

                for (int ibl = idif; ibl >= 1; ibl--)
                {
                    blState.CTAU[ibl, 1] = blState.CTAU[lowerSource, 1];
                    blState.THET[ibl, 1] = blState.THET[lowerSource, 1];
                    blState.DSTR[ibl, 1] = blState.DSTR[lowerSource, 1];
                    blState.UEDG[ibl, 1] = LegacyPrecisionMath.Multiply(dudx, blState.XSSI[ibl, 1], useLegacyPrecision);
                }
            }

            int newUpperCount = blState.NBL[0];
            for (int ibl = 1; ibl < newUpperCount; ibl++)
            {
                int source = ibl + idif;
                if (source < oldUpperCount)
                {
                    blState.CTAU[ibl, 0] = blState.CTAU[source, 0];
                    blState.THET[ibl, 0] = blState.THET[source, 0];
                    blState.DSTR[ibl, 0] = blState.DSTR[source, 0];
                    blState.UEDG[ibl, 0] = blState.UEDG[source, 0];
                }
            }
        }

        for (int side = 0; side < 2; side++)
        {
            if (blState.ITRAN[side] < 1)
            {
                blState.ITRAN[side] = 1;
            }

            if (blState.ITRAN[side] > blState.IBLTE[side])
            {
                blState.ITRAN[side] = blState.IBLTE[side];
            }

            for (int ibl = 1; ibl < blState.NBL[side]; ibl++)
            {
                if (blState.UEDG[ibl, side] <= 1.0e-7)
                {
                    blState.UEDG[ibl, side] = 1.0e-7;
                }

                blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(
                    blState.DSTR[ibl, side],
                    blState.UEDG[ibl, side],
                    useLegacyPrecision);
            }
        }
    }
}

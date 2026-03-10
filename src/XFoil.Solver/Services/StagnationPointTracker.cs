using XFoil.Solver.Models;

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
    public static int FindStagnationPoint(double[] speed, int n)
    {
        if (n < 2) return 0;

        // First look for a sign change in surface speed
        for (int i = 1; i < n; i++)
        {
            if (speed[i - 1] * speed[i] < 0.0)
            {
                // Sign change found: return the node with smaller |Q|
                return Math.Abs(speed[i - 1]) < Math.Abs(speed[i]) ? i - 1 : i;
            }
        }

        // No sign change: find the node with minimum |Q|
        int ispMin = 0;
        double minSpeed = Math.Abs(speed[0]);
        for (int i = 1; i < n; i++)
        {
            double absQ = Math.Abs(speed[i]);
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
    public static void MoveStagnationPoint(
        BoundaryLayerSystemState blState,
        int oldISP,
        int newISP,
        int nPanel)
    {
        if (oldISP == newISP) return;

        int shift = newISP - oldISP;

        // Shift the BL variables on both surfaces.
        // When ISP moves forward (shift > 0), side 1 gains stations and side 2 loses.
        // When ISP moves backward (shift < 0), side 1 loses and side 2 gains.

        for (int side = 0; side < 2; side++)
        {
            int nblOld = blState.NBL[side];
            int iblteOld = blState.IBLTE[side];

            // Compute new station counts
            int nblNew;
            int iblteNew;

            if (side == 0)
            {
                // Side 1 (upper): ISP to TE going forward
                iblteNew = iblteOld + shift;
                nblNew = nblOld + shift;
            }
            else
            {
                // Side 2 (lower): ISP to TE going backward
                iblteNew = iblteOld - shift;
                nblNew = nblOld - shift;
            }

            // Clamp to valid range
            if (iblteNew < 1) iblteNew = 1;
            if (nblNew < 2) nblNew = 2;
            if (iblteNew >= blState.MaxStations) iblteNew = blState.MaxStations - 1;
            if (nblNew > blState.MaxStations) nblNew = blState.MaxStations;

            // Shift BL variables if needed
            if (shift > 0 && side == 0 || shift < 0 && side == 1)
            {
                // Side gains stations: shift existing data to higher indices
                int absShift = Math.Abs(shift);
                for (int i = nblNew - 1; i >= absShift; i--)
                {
                    int iOld = i - absShift;
                    if (iOld >= 0 && iOld < nblOld)
                    {
                        blState.THET[i, side] = blState.THET[iOld, side];
                        blState.DSTR[i, side] = blState.DSTR[iOld, side];
                        blState.CTAU[i, side] = blState.CTAU[iOld, side];
                        blState.UEDG[i, side] = blState.UEDG[iOld, side];
                        blState.MASS[i, side] = blState.MASS[iOld, side];
                        blState.XSSI[i, side] = blState.XSSI[iOld, side];
                    }
                }
                // Interpolate new stations at the beginning (near stagnation point)
                for (int i = 0; i < absShift && i < nblNew; i++)
                {
                    // Use first available valid data as estimate
                    int iRef = absShift < nblNew ? absShift : nblNew - 1;
                    double frac = (double)(i + 1) / (absShift + 1);
                    blState.THET[i, side] = blState.THET[iRef, side] * frac;
                    blState.DSTR[i, side] = blState.DSTR[iRef, side] * frac;
                    blState.CTAU[i, side] = blState.CTAU[iRef, side] * frac;
                    blState.UEDG[i, side] = blState.UEDG[iRef, side];
                    blState.MASS[i, side] = blState.MASS[iRef, side] * frac;
                    blState.XSSI[i, side] = blState.XSSI[iRef, side] * frac;
                }
            }
            else
            {
                // Side loses stations: shift data to lower indices
                int absShift = Math.Abs(shift);
                for (int i = 0; i < nblNew; i++)
                {
                    int iOld = i + absShift;
                    if (iOld < nblOld)
                    {
                        blState.THET[i, side] = blState.THET[iOld, side];
                        blState.DSTR[i, side] = blState.DSTR[iOld, side];
                        blState.CTAU[i, side] = blState.CTAU[iOld, side];
                        blState.UEDG[i, side] = blState.UEDG[iOld, side];
                        blState.MASS[i, side] = blState.MASS[iOld, side];
                        blState.XSSI[i, side] = blState.XSSI[iOld, side];
                    }
                }
            }

            // Update station counts
            blState.IBLTE[side] = iblteNew;
            blState.NBL[side] = nblNew;

            // Adjust transition location if it moved
            if (blState.ITRAN[side] > 0)
            {
                int newTran = blState.ITRAN[side];
                if (side == 0)
                    newTran += shift;
                else
                    newTran -= shift;

                // Clamp transition to valid range
                if (newTran < 1) newTran = 1;
                if (newTran > iblteNew) newTran = iblteNew;
                blState.ITRAN[side] = newTran;
            }
        }
    }
}

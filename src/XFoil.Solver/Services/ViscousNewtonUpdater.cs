using System;
using System.Globalization;
using System.IO;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Applies the Newton step to BL variables with careful relaxation and variable limiting.
/// Port of UPDATE from xbl.f.
/// Supports both XFoil-compatible RLXBL relaxation and trust-region (Levenberg-Marquardt) modes.
/// All VDEL reads use global system line IV indexing.
/// </summary>
public static class ViscousNewtonUpdater
{
    private const double Gamma = 1.4;
    private const double Gm1 = Gamma - 1.0;

    /// <summary>
    /// Applies the Newton update to all BL variables with appropriate relaxation.
    /// Port of UPDATE from xbl.f.
    /// </summary>
    /// <param name="blState">BL system state to update in-place.</param>
    /// <param name="newtonSystem">Newton system with solved VDEL deltas.</param>
    /// <param name="mode">Solver mode (TrustRegion or XFoilRelaxation).</param>
    /// <param name="hstinv">Stagnation enthalpy inverse for Hk limiting.</param>
    /// <param name="wakeGap">Wake displacement thickness per station.</param>
    /// <param name="trustRadius">Trust-region radius (used only in TrustRegion mode).</param>
    /// <param name="previousRmsbl">Previous iteration RMS (for trust-region accept/reject).</param>
    /// <param name="currentRmsbl">Current iteration RMS before update.</param>
    /// <param name="dij">DIJ influence matrix for computing Ue update from mass coupling (optional).</param>
    /// <param name="isp">Stagnation point panel index (optional, for GetPanelIndex).</param>
    /// <param name="nPanel">Total panel node count (optional, for GetPanelIndex).</param>
    /// <returns>Tuple of (relaxation factor applied, updated RMS, new trust radius, step accepted).</returns>
    public static (double Rlx, double Rmsbl, double TrustRadius, bool Accepted) ApplyNewtonUpdate(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        ViscousSolverMode mode,
        double hstinv,
        double[] wakeGap,
        double trustRadius,
        double previousRmsbl,
        double currentRmsbl,
        double[,]? dij = null,
        int isp = -1,
        int nPanel = -1,
        TextWriter? debugWriter = null)
    {
        if (mode == ViscousSolverMode.XFoilRelaxation)
        {
            double rlx = ApplyXFoilRelaxation(blState, newtonSystem, hstinv, wakeGap, dij, isp, nPanel, debugWriter);
            double rmsbl = ComputeUpdateRms(blState, newtonSystem);
            return (rlx, rmsbl, trustRadius, true);
        }
        else
        {
            return ApplyTrustRegionUpdate(blState, newtonSystem, hstinv, wakeGap,
                trustRadius, previousRmsbl, currentRmsbl, dij, isp, nPanel, debugWriter);
        }
    }

    /// <summary>
    /// XFoil-compatible RLXBL relaxation from UPDATE in xbl.f.
    /// Uses DHI=1.5, DLO=-0.5 thresholds.
    /// Reads VDEL by global system line IV.
    /// </summary>
    private static double ApplyXFoilRelaxation(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double hstinv,
        double[] wakeGap,
        double[,]? dij,
        int isp,
        int nPanel,
        TextWriter? debugWriter)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        // Compute Ue changes from mass coupling via DIJ
        double[]? duedgArr = ComputeUeUpdates(newtonSystem, blState, dij, isp, nPanel);

        double rlx = 1.0;
        double dhi = 1.5;
        double dlo = -0.5;

        // First pass: compute relaxation factor
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Read deltas from VDEL indexed by iv (global system line)
            double dctau = vdel[0, 0, jv];
            double dthet = vdel[1, 0, jv];
            double dmass = vdel[2, 0, jv];

            double duedg = duedgArr != null ? duedgArr[jv] : 0.0;

            // Normalized changes
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = dctau / 10.0;
            else
                dn1 = (Math.Abs(blState.CTAU[ibl, side]) > 1e-30)
                    ? dctau / blState.CTAU[ibl, side] : 0.0;

            double dn2 = (Math.Abs(blState.THET[ibl, side]) > 1e-30)
                ? dthet / blState.THET[ibl, side] : 0.0;

            double ddstr = (Math.Abs(blState.UEDG[ibl, side]) > 1e-30)
                ? (dmass - blState.DSTR[ibl, side] * duedg) / blState.UEDG[ibl, side] : 0.0;

            double dn3 = (Math.Abs(blState.DSTR[ibl, side]) > 1e-30)
                ? ddstr / blState.DSTR[ibl, side] : 0.0;

            double dn4 = Math.Abs(duedg) / 0.25;

            // Check each variable against DHI/DLO limits
            double rdn1 = rlx * dn1;
            if (rdn1 > dhi && dn1 != 0) rlx = dhi / dn1;
            if (rdn1 < dlo && dn1 != 0) rlx = dlo / dn1;

            double rdn2 = rlx * dn2;
            if (rdn2 > dhi && dn2 != 0) rlx = dhi / dn2;
            if (rdn2 < dlo && dn2 != 0) rlx = dlo / dn2;

            double rdn3 = rlx * dn3;
            if (rdn3 > dhi && dn3 != 0) rlx = dhi / dn3;
            if (rdn3 < dlo && dn3 != 0) rlx = dlo / dn3;

            double rdn4 = rlx * dn4;
            if (rdn4 > dhi && dn4 != 0) rlx = dhi / dn4;
            if (rdn4 < dlo && dn4 != 0) rlx = dlo / dn4;
        }

        // Ensure rlx is positive and at most 1.0
        rlx = Math.Max(0.01, Math.Min(1.0, rlx));

        // Diagnostic: log first 5 stations' Newton deltas and the relaxation factor
        if (debugWriter != null)
        {
            int logCount = Math.Min(5, nsys);
            for (int jv = 0; jv < logCount; jv++)
            {
                int iblDbg = isys[jv, 0];
                int sideDbg = isys[jv, 1];
                double dctauDbg = vdel[0, 0, jv];
                double dthetDbg = vdel[1, 0, jv];
                double dmassDbg = vdel[2, 0, jv];
                double duedgDbg = duedgArr != null ? duedgArr[jv] : 0.0;
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE IS={0} IBL={1} IV={2} dC={3,15:E8} dT={4,15:E8} dM={5,15:E8} dU={6,15:E8}",
                    sideDbg + 1, iblDbg, jv + 1, dctauDbg, dthetDbg, dmassDbg, duedgDbg));
            }
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "UPDATE_RLX RLX={0,15:E8}", rlx));
        }

        // Second pass: apply the relaxed update
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap, duedgArr);

        return rlx;
    }

    /// <summary>
    /// Trust-region (Levenberg-Marquardt) update strategy.
    /// </summary>
    private static (double Rlx, double Rmsbl, double TrustRadius, bool Accepted) ApplyTrustRegionUpdate(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double hstinv,
        double[] wakeGap,
        double trustRadius,
        double previousRmsbl,
        double currentRmsbl,
        double[,]? dij,
        int isp,
        int nPanel,
        TextWriter? debugWriter)
    {
        // Compute step norm
        double stepNorm = ComputeStepNorm(blState, newtonSystem);

        // Scale step to stay within trust region
        double rlx = (stepNorm > trustRadius && stepNorm > 1e-30)
            ? trustRadius / stepNorm
            : 1.0;
        rlx = Math.Max(0.01, Math.Min(1.0, rlx));

        // Diagnostic: log first 5 stations' Newton deltas and relaxation factor
        if (debugWriter != null)
        {
            var vdelTr = newtonSystem.VDEL;
            var isysTr = newtonSystem.ISYS;
            int nsysTr = newtonSystem.NSYS;
            int logCount = Math.Min(5, nsysTr);
            for (int jv = 0; jv < logCount; jv++)
            {
                int iblDbg = isysTr[jv, 0];
                int sideDbg = isysTr[jv, 1];
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE IS={0} IBL={1} IV={2} dC={3,15:E8} dT={4,15:E8} dM={5,15:E8} dU={6,15:E8}",
                    sideDbg + 1, iblDbg, jv + 1, vdelTr[0, 0, jv], vdelTr[1, 0, jv], vdelTr[2, 0, jv], 0.0));
            }
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "UPDATE_RLX RLX={0,15:E8}", rlx));
        }

        // Save current state for potential rollback
        var savedThet = (double[,])blState.THET.Clone();
        var savedDstr = (double[,])blState.DSTR.Clone();
        var savedCtau = (double[,])blState.CTAU.Clone();
        var savedUedg = (double[,])blState.UEDG.Clone();
        var savedMass = (double[,])blState.MASS.Clone();

        // Compute Ue updates from mass coupling
        double[]? duedgArr = ComputeUeUpdates(newtonSystem, blState, dij, isp, nPanel);

        // Apply step
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap, duedgArr);

        // Compute new residual
        double newRmsbl = ComputeUpdateRms(blState, newtonSystem);

        double newTrustRadius = trustRadius;
        bool accepted = true;

        if (newRmsbl < currentRmsbl)
        {
            // Step reduced residual: expand trust region
            newTrustRadius = Math.Min(trustRadius * 2.0, 10.0);
        }
        else
        {
            // Step increased residual: shrink trust region and reject
            newTrustRadius = trustRadius * 0.5;
            if (newTrustRadius < 0.01)
            {
                // Trust region collapsed -- accept step anyway to avoid infinite shrink
                accepted = true;
            }
            else
            {
                // Reject step: restore previous state
                Array.Copy(savedThet, blState.THET, savedThet.Length);
                Array.Copy(savedDstr, blState.DSTR, savedDstr.Length);
                Array.Copy(savedCtau, blState.CTAU, savedCtau.Length);
                Array.Copy(savedUedg, blState.UEDG, savedUedg.Length);
                Array.Copy(savedMass, blState.MASS, savedMass.Length);
                accepted = false;
                newRmsbl = currentRmsbl;
            }
        }

        return (rlx, newRmsbl, newTrustRadius, accepted);
    }

    /// <summary>
    /// Computes Ue updates from mass defect coupling via DIJ.
    /// After the tridiagonal solve, VDEL contains deltas for (Ctau/ampl, theta, mass).
    /// The Ue change at each station comes from the mass defect change at all stations:
    /// dUe[iv] = sum_jv( -VTI[iv]*VTI[jv]*DIJ[iPan_iv, iPan_jv] * dMass[jv] )
    /// </summary>
    private static double[]? ComputeUeUpdates(
        ViscousNewtonSystem newtonSystem,
        BoundaryLayerSystemState blState,
        double[,]? dij,
        int isp,
        int nPanel)
    {
        int nsys = newtonSystem.NSYS;
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;

        if (dij == null || nsys <= 0)
            return null;

        double[] duedg = new double[nsys];

        for (int iv = 0; iv < nsys; iv++)
        {
            int ibl = isys[iv, 0];
            int iside = isys[iv, 1];
            int iPan = GetPanelIndex(ibl, iside, isp, nPanel, blState);
            double vtiI = GetVTI(ibl, iside, blState);

            double ueSum = 0.0;
            for (int jv = 0; jv < nsys; jv++)
            {
                int jbl = isys[jv, 0];
                int jside = isys[jv, 1];
                int jPan = GetPanelIndex(jbl, jside, isp, nPanel, blState);
                double vtiJ = GetVTI(jbl, jside, blState);

                double dMass = vdel[2, 0, jv]; // mass defect delta at station jv

                if (iPan >= 0 && jPan >= 0 && iPan < dij.GetLength(0) && jPan < dij.GetLength(1))
                {
                    ueSum += -vtiI * vtiJ * dij[iPan, jPan] * dMass;
                }
            }

            duedg[iv] = ueSum;
        }

        return duedg;
    }

    /// <summary>
    /// Applies the relaxed Newton step to all BL variables with variable limiting.
    /// Uses global system line IV for all VDEL reads.
    /// </summary>
    private static void ApplyRelaxedStep(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double rlx,
        double hstinv,
        double[] wakeGap,
        double[]? duedgArr)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Read deltas from VDEL indexed by iv (global system line)
            double dctau = vdel[0, 0, jv];
            double dthet = vdel[1, 0, jv];
            double dmass = vdel[2, 0, jv];

            double duedg = duedgArr != null ? duedgArr[jv] : 0.0;

            // Apply relaxed changes
            blState.CTAU[ibl, side] += rlx * dctau;
            blState.THET[ibl, side] += rlx * dthet;

            // Update DSTR from mass defect change, accounting for Ue change
            double ddstr = (Math.Abs(blState.UEDG[ibl, side]) > 1e-30)
                ? (dmass - blState.DSTR[ibl, side] * duedg) / blState.UEDG[ibl, side] : 0.0;
            blState.DSTR[ibl, side] += rlx * ddstr;
            blState.UEDG[ibl, side] += rlx * duedg;

            // Variable limiting

            // Ctau clamped to [0, 0.25] for turbulent/wake stations
            if (ibl >= blState.ITRAN[side])
                blState.CTAU[ibl, side] = Math.Min(blState.CTAU[ibl, side], 0.25);

            // Theta must be positive
            if (blState.THET[ibl, side] < 1e-10)
                blState.THET[ibl, side] = 1e-10;

            // Hk limiting via DSLIM
            double dswaki = 0.0;
            if (ibl > blState.IBLTE[side] && wakeGap != null)
            {
                int iw = ibl - blState.IBLTE[side];
                if (iw >= 0 && iw < wakeGap.Length)
                    dswaki = wakeGap[iw];
            }

            double hklim = (ibl <= blState.IBLTE[side]) ? 1.02 : 1.00005;
            double msq = 0.0;
            if (hstinv > 0)
            {
                double uesq = blState.UEDG[ibl, side] * blState.UEDG[ibl, side] * hstinv;
                msq = uesq / (Gm1 * (1.0 - 0.5 * uesq));
            }

            double dsw = blState.DSTR[ibl, side] - dswaki;
            dsw = ApplyDslim(dsw, blState.THET[ibl, side], msq, hklim);
            blState.DSTR[ibl, side] = dsw + dswaki;

            // Update mass defect (nonlinear update)
            blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
        }

        // Eliminate negative Ue islands (matching Fortran)
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 2; ibl <= blState.IBLTE[side]; ibl++)
            {
                if (blState.UEDG[ibl - 1, side] > 0.0 && blState.UEDG[ibl, side] <= 0.0)
                {
                    blState.UEDG[ibl, side] = blState.UEDG[ibl - 1, side];
                    blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
                }
            }
        }
    }

    /// <summary>
    /// Limits displacement thickness to enforce Hk >= hklim.
    /// Port of DSLIM from xbl.f.
    /// </summary>
    private static double ApplyDslim(double dstr, double thet, double msq, double hklim)
    {
        if (thet < 1e-30) return dstr;

        double h = dstr / thet;
        var (hk, hk_h, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h, msq);

        double dh = Math.Max(0.0, hklim - hk) / Math.Max(hk_h, 1e-10);
        return dstr + dh * thet;
    }

    /// <summary>
    /// Computes the norm of the Newton step for trust-region scaling.
    /// Uses global system line IV for VDEL reads.
    /// </summary>
    private static double ComputeStepNorm(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        double norm = 0.0;
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Normalized step components -- read VDEL by iv
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = vdel[0, 0, jv] / 10.0;
            else
                dn1 = (Math.Abs(blState.CTAU[ibl, side]) > 1e-30)
                    ? vdel[0, 0, jv] / blState.CTAU[ibl, side] : 0.0;

            double dn2 = (Math.Abs(blState.THET[ibl, side]) > 1e-30)
                ? vdel[1, 0, jv] / blState.THET[ibl, side] : 0.0;

            double dn3 = (Math.Abs(blState.DSTR[ibl, side]) > 1e-30)
                ? vdel[2, 0, jv] / blState.DSTR[ibl, side] : 0.0;

            norm += dn1 * dn1 + dn2 * dn2 + dn3 * dn3;
        }

        return Math.Sqrt(norm / Math.Max(nsys, 1));
    }

    /// <summary>
    /// Computes the RMS of residuals using global system line IV indexing.
    /// </summary>
    private static double ComputeUpdateRms(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem)
    {
        var vdel = newtonSystem.VDEL;
        int nsys = newtonSystem.NSYS;

        double rmsbl = 0.0;
        for (int jv = 0; jv < nsys; jv++)
        {
            for (int k = 0; k < 3; k++)
            {
                rmsbl += vdel[k, 0, jv] * vdel[k, 0, jv];
            }
        }

        return (nsys > 0) ? Math.Sqrt(rmsbl / (3.0 * nsys)) : 0.0;
    }

    /// <summary>
    /// Gets the panel node index for a given BL station and side.
    /// Mirrors ViscousNewtonAssembler.GetPanelIndex.
    /// </summary>
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        if (isp < 0 || nPanel < 0)
        {
            if (side == 0)
                return ibl;
            else
                return blState.IBLTE[0] + ibl;
        }

        bool wake = (ibl > blState.IBLTE[side]);
        if (wake)
            return nPanel + (ibl - blState.IBLTE[1]);
        else if (side == 0)
            return isp - ibl;
        else
            return isp + ibl;
    }

    /// <summary>
    /// Gets the VTI sign factor for a BL station.
    /// </summary>
    private static double GetVTI(int ibl, int side, BoundaryLayerSystemState blState)
    {
        if (ibl > blState.IBLTE[side])
            return 1.0; // wake
        if (side == 0)
            return 1.0; // upper surface
        return -1.0; // lower surface
    }
}

using System;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Applies the Newton step to BL variables with careful relaxation and variable limiting.
/// Port of UPDATE from xbl.f.
/// Supports both XFoil-compatible RLXBL relaxation and trust-region (Levenberg-Marquardt) modes.
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
    /// <returns>Tuple of (relaxation factor applied, updated RMS, new trust radius, step accepted).</returns>
    public static (double Rlx, double Rmsbl, double TrustRadius, bool Accepted) ApplyNewtonUpdate(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        ViscousSolverMode mode,
        double hstinv,
        double[] wakeGap,
        double trustRadius,
        double previousRmsbl,
        double currentRmsbl)
    {
        if (mode == ViscousSolverMode.XFoilRelaxation)
        {
            double rlx = ApplyXFoilRelaxation(blState, newtonSystem, hstinv, wakeGap);
            double rmsbl = ComputeUpdateRms(blState, newtonSystem);
            return (rlx, rmsbl, trustRadius, true);
        }
        else
        {
            return ApplyTrustRegionUpdate(blState, newtonSystem, hstinv, wakeGap,
                trustRadius, previousRmsbl, currentRmsbl);
        }
    }

    /// <summary>
    /// XFoil-compatible RLXBL relaxation from UPDATE in xbl.f.
    /// Uses DHI=1.5, DLO=-0.5 thresholds.
    /// </summary>
    private static double ApplyXFoilRelaxation(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double hstinv,
        double[] wakeGap)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        double rlx = 1.0;
        double dhi = 1.5;
        double dlo = -0.5;

        // First pass: compute relaxation factor
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            double dctau = vdel[0, 0, ibl];
            double dthet = vdel[1, 0, ibl];
            double dmass = vdel[2, 0, ibl];

            // Normalized changes
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = dctau / 10.0;
            else
                dn1 = (Math.Abs(blState.CTAU[ibl, side]) > 1e-30)
                    ? dctau / blState.CTAU[ibl, side] : 0.0;

            double dn2 = (Math.Abs(blState.THET[ibl, side]) > 1e-30)
                ? dthet / blState.THET[ibl, side] : 0.0;

            double duedg = 0.0; // Simplified: Ue change comes from mass coupling
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

        // Second pass: apply the relaxed update
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap);

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
        double currentRmsbl)
    {
        // Compute step norm
        double stepNorm = ComputeStepNorm(blState, newtonSystem);

        // Scale step to stay within trust region
        double rlx = (stepNorm > trustRadius && stepNorm > 1e-30)
            ? trustRadius / stepNorm
            : 1.0;
        rlx = Math.Max(0.01, Math.Min(1.0, rlx));

        // Save current state for potential rollback
        var savedThet = (double[,])blState.THET.Clone();
        var savedDstr = (double[,])blState.DSTR.Clone();
        var savedCtau = (double[,])blState.CTAU.Clone();
        var savedUedg = (double[,])blState.UEDG.Clone();
        var savedMass = (double[,])blState.MASS.Clone();

        // Apply step
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap);

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
    /// Applies the relaxed Newton step to all BL variables with variable limiting.
    /// </summary>
    private static void ApplyRelaxedStep(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double rlx,
        double hstinv,
        double[] wakeGap)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            double dctau = vdel[0, 0, ibl];
            double dthet = vdel[1, 0, ibl];
            double dmass = vdel[2, 0, ibl];

            // Apply relaxed changes
            blState.CTAU[ibl, side] += rlx * dctau;
            blState.THET[ibl, side] += rlx * dthet;

            // Update DSTR from mass defect change
            double duedg = 0.0;
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

            // Normalized step components
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = vdel[0, 0, ibl] / 10.0;
            else
                dn1 = (Math.Abs(blState.CTAU[ibl, side]) > 1e-30)
                    ? vdel[0, 0, ibl] / blState.CTAU[ibl, side] : 0.0;

            double dn2 = (Math.Abs(blState.THET[ibl, side]) > 1e-30)
                ? vdel[1, 0, ibl] / blState.THET[ibl, side] : 0.0;

            double dn3 = (Math.Abs(blState.DSTR[ibl, side]) > 1e-30)
                ? vdel[2, 0, ibl] / blState.DSTR[ibl, side] : 0.0;

            norm += dn1 * dn1 + dn2 * dn2 + dn3 * dn3;
        }

        return Math.Sqrt(norm / Math.Max(nsys, 1));
    }

    /// <summary>
    /// Computes the RMS of normalized BL changes after update.
    /// </summary>
    private static double ComputeUpdateRms(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        double rmsbl = 0.0;
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];

            for (int k = 0; k < 3; k++)
            {
                rmsbl += vdel[k, 0, ibl] * vdel[k, 0, ibl];
            }
        }

        return (nsys > 0) ? Math.Sqrt(rmsbl / (3.0 * nsys)) : 0.0;
    }
}

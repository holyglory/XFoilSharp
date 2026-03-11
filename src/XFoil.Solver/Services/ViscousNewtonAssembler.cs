using System;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Builds the global Newton system for the viscous BL solver by marching through
/// all BL stations on both surfaces and the wake.
/// Port of SETBL from xbl.f.
/// All array indexing uses global system line IV (0..NSYS-1).
/// </summary>
public static class ViscousNewtonAssembler
{
    private const double Gamma = 1.4;
    private const double Gm1 = Gamma - 1.0;

    // BLPAR defaults matching XFoil
    private const double GACON = 6.70;
    private const double GBCON = 0.75;
    private const double GCCON = 18.0;
    private const double DLCON = 0.9;
    private const double CTCON = 0.5 / (GACON * GACON * GBCON);
    private const double SCCON = 5.6;
    private const double DUXCON = 1.0;

    /// <summary>
    /// Builds the Newton system by marching through all BL stations in order:
    /// side 1 (IBL=2..IBLTE[0]), side 2 (IBL=2..IBLTE[1]),
    /// wake (IBL=IBLTE[1]+1..NBL[1]).
    /// Port of SETBL from xbl.f.
    /// All array writes use global system line IV from the ISYS mapping.
    /// </summary>
    /// <param name="blState">Current BL variable state.</param>
    /// <param name="newtonSystem">Newton system workspace to fill.</param>
    /// <param name="dij">DIJ influence matrix (dUe/dSigma).</param>
    /// <param name="settings">Analysis settings (NCrit, solver mode, etc.).</param>
    /// <param name="isAlphaPrescribed">True for Type 1 (alpha prescribed); false for Type 2 (CL prescribed).</param>
    /// <param name="wakeGap">TE gap (wake displacement thickness) per wake station.</param>
    /// <param name="tkbl">Karman-Tsien compressibility parameter.</param>
    /// <param name="qinfbl">Freestream speed.</param>
    /// <param name="tkbl_ms">Sensitivity of TKBL to Mach squared.</param>
    /// <param name="hstinv">Stagnation enthalpy inverse parameter.</param>
    /// <param name="hstinv_ms">Sensitivity of HSTINV to Mach squared.</param>
    /// <param name="rstbl">Stagnation density ratio.</param>
    /// <param name="rstbl_ms">Sensitivity of RSTBL to Mach squared.</param>
    /// <param name="reybl">Reynolds number for BL.</param>
    /// <param name="reybl_re">Sensitivity of REYBL to Re.</param>
    /// <param name="reybl_ms">Sensitivity of REYBL to Mach squared.</param>
    /// <param name="hvrat">Sutherland constant ratio.</param>
    /// <param name="isp">Stagnation point panel index (for GetPanelIndex mapping).</param>
    /// <param name="nPanel">Total panel node count (for GetPanelIndex mapping).</param>
    /// <returns>RMS of all residuals (RMSBL before solving).</returns>
    public static double BuildNewtonSystem(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double[,] dij,
        AnalysisSettings settings,
        bool isAlphaPrescribed,
        double[] wakeGap,
        double tkbl, double qinfbl, double tkbl_ms,
        double hstinv, double hstinv_ms,
        double rstbl, double rstbl_ms,
        double reybl, double reybl_re, double reybl_ms,
        double hvrat,
        int isp = -1,
        int nPanel = -1)
    {
        int nsys = newtonSystem.NSYS;
        var va = newtonSystem.VA;
        var vb = newtonSystem.VB;
        var vm = newtonSystem.VM;
        var vdel = newtonSystem.VDEL;
        var vz = newtonSystem.VZ;
        var isys = newtonSystem.ISYS;

        // Zero the Newton system arrays
        Array.Clear(va);
        Array.Clear(vb);
        Array.Clear(vm);
        Array.Clear(vdel);
        Array.Clear(vz);

        double rmsbl = 0.0;
        int nResiduals = 0;

        double amcrit = settings.CriticalAmplificationFactor;
        double gm1bl = Gm1;

        // March through both sides and wake (matching Fortran order: IS=1,2)
        for (int side = 0; side < 2; side++)
        {
            double ncrit = settings.GetEffectiveNCrit(side);

            // Previous station variables -- zeroed at start of each side (similarity)
            double x1 = 0, u1 = 0, t1 = 0, d1 = 0, s1 = 0, dw1 = 0;
            double ampl1 = 0;
            double hk1 = 2.1, rt1 = 200.0; // Previous station Hk and Rt for transition check

            for (int ibl = 1; ibl < blState.NBL[side]; ibl++)
            {
                int iv = -1;
                // Find IV from ISYS mapping
                for (int j = 0; j < nsys; j++)
                {
                    if (isys[j, 0] == ibl && isys[j, 1] == side)
                    {
                        iv = j;
                        break;
                    }
                }
                if (iv < 0) continue;

                bool simi = (ibl == 1);
                bool wake = (ibl > blState.IBLTE[side]);
                bool tran = (ibl == blState.ITRAN[side]);
                bool turb = (ibl >= blState.ITRAN[side]);

                // Set primary variables for current station
                double xsi = blState.XSSI[ibl, side];
                double uei = blState.UEDG[ibl, side];
                double thi = blState.THET[ibl, side];
                double mdi = blState.MASS[ibl, side];
                double dsi = (Math.Abs(uei) > 1e-30) ? mdi / uei : 0.0;
                double cti = blState.CTAU[ibl, side];
                double ami = (ibl < blState.ITRAN[side]) ? cti : 0.0;
                if (ibl >= blState.ITRAN[side]) ami = 0.0;
                else cti = 0.0;

                double dswaki = 0.0;
                if (wake)
                {
                    int iw = ibl - blState.IBLTE[side];
                    if (wakeGap != null && iw >= 0 && iw < wakeGap.Length)
                        dswaki = wakeGap[iw];
                }

                // Current station compressible velocity
                var (u2, u2_uei, u2_ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei, tkbl, qinfbl, tkbl_ms);

                double msq2 = 0.0;
                if (hstinv > 0)
                {
                    double u2sq = u2 * u2 * hstinv;
                    msq2 = u2sq / (gm1bl * (1.0 - 0.5 * u2sq));
                }

                // Compute Hk and Rt for current station (for transition check)
                double h2v = (thi > 1e-30) ? dsi / thi : 2.1;
                var (hk2, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h2v, msq2);
                hk2 = Math.Max(hk2, 1.05);
                double rt2 = Math.Max(Math.Abs(uei) * thi * reybl, 200.0);

                // Transition check: call TransitionModel.CheckTransition during BL march
                if (!wake && !turb && !tran && ibl > 1 && x1 > 0 && xsi > x1)
                {
                    var trResult = TransitionModel.CheckTransition(
                        x1, xsi, ampl1, ami, ncrit,
                        hk1, t1 > 0 ? t1 : thi, rt1, u1 > 0 ? u1 : uei, d1 > 0 ? d1 : dsi,
                        hk2, thi, rt2, uei, dsi,
                        settings.UseModernTransitionCorrections, null);

                    if (trResult.TransitionOccurred)
                    {
                        blState.ITRAN[side] = ibl;
                        tran = true;
                        turb = true;
                    }
                }

                // Assemble local BL system
                BoundaryLayerSystemAssembler.BlsysResult localResult;

                if (wake && ibl == blState.IBLTE[side] + 1)
                {
                    // First wake point: use TESYS for TE-to-wake transition
                    double tte = blState.THET[blState.IBLTE[0], 0] + blState.THET[blState.IBLTE[1], 1];
                    double dte = blState.DSTR[blState.IBLTE[0], 0] + blState.DSTR[blState.IBLTE[1], 1];
                    double ante = (wakeGap != null && wakeGap.Length > 0) ? wakeGap[0] : 0.0;
                    dte += ante;

                    double ctteWeight = 0.0;
                    if (tte > 1e-30)
                    {
                        ctteWeight = (blState.CTAU[blState.IBLTE[0], 0] * blState.THET[blState.IBLTE[0], 0]
                                    + blState.CTAU[blState.IBLTE[1], 1] * blState.THET[blState.IBLTE[1], 1]) / tte;
                    }

                    var teResult = BoundaryLayerSystemAssembler.AssembleTESystem(
                        ctteWeight, tte, dte,
                        0, 0, msq2, 0,
                        cti, thi, dsi, dswaki);

                    localResult = new BoundaryLayerSystemAssembler.BlsysResult
                    {
                        Residual = teResult.Residual,
                        VS1 = teResult.VS1,
                        VS2 = teResult.VS2
                    };

                    // Set VZ coupling block for TE junction -- use iv for VB indexing
                    double cte_cte1 = (tte > 1e-30) ? blState.THET[blState.IBLTE[0], 0] / tte : 0.5;
                    double cte_cte2 = (tte > 1e-30) ? blState.THET[blState.IBLTE[1], 1] / tte : 0.5;
                    double cte_tte1 = (tte > 1e-30) ? (blState.CTAU[blState.IBLTE[0], 0] - ctteWeight) / tte : 0;
                    double cte_tte2 = (tte > 1e-30) ? (blState.CTAU[blState.IBLTE[1], 1] - ctteWeight) / tte : 0;

                    for (int k = 0; k < 3; k++)
                    {
                        vz[k, 0] = localResult.VS1[k, 0] * cte_cte1;
                        vz[k, 1] = localResult.VS1[k, 0] * cte_tte1 + localResult.VS1[k, 1];
                        vb[k, 0, iv] = localResult.VS1[k, 0] * cte_cte2;
                        vb[k, 1, iv] = localResult.VS1[k, 0] * cte_tte2 + localResult.VS1[k, 1];
                    }
                }
                else
                {
                    // Regular station: use BLSYS
                    localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                        wake, turb || tran, tran, simi,
                        x1, xsi,
                        u1 > 0 ? u1 : uei, uei,
                        t1 > 0 ? t1 : thi, thi,
                        d1 > 0 ? d1 : dsi, dsi,
                        s1, cti,
                        dw1, dswaki,
                        ampl1, ami,
                        ncrit,
                        tkbl, qinfbl, tkbl_ms,
                        hstinv, hstinv_ms,
                        gm1bl, rstbl, rstbl_ms,
                        hvrat, reybl, reybl_re, reybl_ms);
                }

                // Store into Newton system -- use iv (global system line) for all array writes
                if (!(wake && ibl == blState.IBLTE[side] + 1))
                {
                    for (int k = 0; k < 3; k++)
                    {
                        va[k, 0, iv] = localResult.VS2[k, 0]; // Ctau/ampl at station 2
                        va[k, 1, iv] = localResult.VS2[k, 1]; // Theta at station 2
                        vb[k, 0, iv] = localResult.VS1[k, 0]; // Ctau/ampl at station 1
                        vb[k, 1, iv] = localResult.VS1[k, 1]; // Theta at station 1
                    }
                }

                // Residuals go into VDEL -- indexed by iv
                for (int k = 0; k < 3; k++)
                {
                    vdel[k, 0, iv] = localResult.Residual[k];
                    vdel[k, 1, iv] = 0.0;
                }

                // Add DIJ coupling into VM (mass influence column)
                // VM[k, jv, iv] = VS2[k,2]*D2_M[jv] + VS2[k,3]*UE_M[jv]*u2_uei
                double d2_u2 = (Math.Abs(uei) > 1e-30) ? -dsi / uei : 0.0;
                double d2_m2 = (Math.Abs(uei) > 1e-30) ? 1.0 / uei : 0.0;

                // Populate VM with DIJ coupling for all system lines
                for (int jv = 0; jv < nsys; jv++)
                {
                    int jbl = isys[jv, 0];
                    int jside = isys[jv, 1];

                    // Panel index mapping for DIJ lookup
                    int iPan = GetPanelIndex(ibl, side, isp, nPanel, blState);
                    int jPan = GetPanelIndex(jbl, jside, isp, nPanel, blState);

                    if (iPan >= 0 && jPan >= 0 && iPan < dij.GetLength(0) && jPan < dij.GetLength(1))
                    {
                        double vtiI = GetVTI(ibl, side, blState);
                        double vtiJ = GetVTI(jbl, jside, blState);

                        // DIJ gives dUe_incompressible/dSigma. Must chain through
                        // u2_uei to get compressible velocity sensitivity.
                        double ue_mj = -vtiI * vtiJ * dij[iPan, jPan];

                        // d(delta*)/d(mass) at station jv via Ue change
                        double d2_mj = d2_u2 * ue_mj * u2_uei;
                        if (jv == iv) d2_mj += d2_m2;

                        for (int k = 0; k < 3; k++)
                        {
                            // Mass coupling: d/dMass(j) contribution to equations at station i
                            if (jv < newtonSystem.MaxWake)
                            {
                                vm[k, jv, iv] = localResult.VS2[k, 2] * d2_mj
                                              + localResult.VS2[k, 3] * ue_mj * u2_uei;
                            }
                        }
                    }
                }

                // Accumulate RMS of residuals
                for (int k = 0; k < 3; k++)
                {
                    rmsbl += localResult.Residual[k] * localResult.Residual[k];
                    nResiduals++;
                }

                // Save as previous station for next iteration
                x1 = xsi;
                u1 = uei;
                t1 = thi;
                d1 = dsi;
                s1 = cti;
                dw1 = dswaki;
                ampl1 = (ibl < blState.ITRAN[side]) ? blState.CTAU[ibl, side] : 0.0;
                hk1 = hk2;
                rt1 = rt2;
            }
        }

        // Compute RMS
        if (nResiduals > 0)
            rmsbl = Math.Sqrt(rmsbl / nResiduals);

        return rmsbl;
    }

    /// <summary>
    /// Gets the panel node index for a given BL station and side.
    /// For side 0 (upper): IPAN[ibl] = ISP - ibl (going backward from stag point)
    /// For side 1 (lower): IPAN[ibl] = ISP + ibl (going forward from stag point)
    /// For wake: IPAN[ibl] = N + (ibl - IBLTE[1]) (after last airfoil node)
    /// Falls back to simplified linear offset if isp/nPanel not provided.
    /// </summary>
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        // If isp/nPanel not provided, use simplified linear offset (backward compatibility)
        if (isp < 0 || nPanel < 0)
        {
            if (side == 0)
                return ibl;
            else
                return blState.IBLTE[0] + ibl;
        }

        bool wake = (ibl > blState.IBLTE[side]);
        if (wake)
        {
            // Wake panel indices: after all airfoil panels
            return nPanel + (ibl - blState.IBLTE[1]);
        }
        else if (side == 0)
        {
            // Upper surface: ISP backward to node 0
            return isp - ibl;
        }
        else
        {
            // Lower surface: ISP forward to node N-1
            return isp + ibl;
        }
    }

    /// <summary>
    /// Gets the VTI sign factor for a BL station.
    /// VTI = +1 on side 0 (upper, speed positive going from stag to TE)
    /// VTI = -1 on side 1 (lower, speed is in opposite direction)
    /// VTI = +1 for wake.
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

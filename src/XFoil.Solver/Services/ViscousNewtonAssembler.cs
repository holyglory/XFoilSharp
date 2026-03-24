using System;
using System.Globalization;
using System.IO;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: SETBL
// Secondary legacy source(s): f_xfoil/src/xbl.f :: UESET
// Role in port: Global viscous Newton-system assembly that marches the airfoil and wake stations and couples them back to the inviscid influence system.
// Differences: The managed port splits SETBL into smaller helpers, keeps explicit state snapshots for parity debugging, and exposes the DIJ/leading-edge forcing terms with trace output instead of leaving them implicit in one monolithic routine.
// Decision: Keep the decomposed managed structure for default execution, but preserve the legacy station order, coupling order, and parity-specific stale-state behavior where binary replay depends on it.

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
    /// <param name="ueInv">Current inviscid edge-velocity baseline UINV.</param>
    /// <param name="isp">Stagnation point panel index (for GetPanelIndex mapping).</param>
    /// <param name="nPanel">Total panel node count (for GetPanelIndex mapping).</param>
    /// <returns>RMS of all residuals (RMSBL before solving).</returns>
    // Legacy mapping: f_xfoil/src/xbl.f :: SETBL
    // Difference from legacy: The managed code preserves the same march order and local-equation assembly, but it factors predicted-Ue reconstruction, stagnation forcing, and trace logging into explicit helpers and threads parity snapshots through the call chain.
    // Decision: Keep the split structure because it makes the solver debuggable; preserve the legacy control-flow order and parity-only stale-state semantics inside the march.
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
        double[,] ueInv,
        int isp = -1,
        int nPanel = -1,
        TextWriter? debugWriter = null)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
            new
            {
                nsys = newtonSystem.NSYS,
                upperTe = blState.IBLTE[0],
                lowerTe = blState.IBLTE[1],
                isp,
                nPanel
            });
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

        bool useLegacyPrecision = settings.UseLegacyBoundaryLayerInitialization;
        double amcrit = settings.CriticalAmplificationFactor;
        // Parity mode needs the REAL-staged GAMM1 value that classic XFoil feeds
        // into COMSET/BLKIN, not the double literal 0.4.
        double gm1bl = LegacyPrecisionMath.GammaMinusOne(useLegacyPrecision);
        double[,] usav = ComputePredictedEdgeVelocities(blState, dij, ueInv, isp, nPanel, debugWriter, useLegacyPrecision);
        ComputeLeadingEdgeSensitivities(
            blState,
            out double sstGo,
            out double sstGp,
            out double dule1,
            out double dule2,
            usav,
            useLegacyPrecision);

        // Legacy block: xbl.f SETBL surface-by-surface march.
        // Difference from legacy: The loop order is unchanged, but the managed code names the side-local state instead of reusing shared COMMON-style temporaries.
        // Decision: Keep the named locals; preserve the original side order because the residual assembly depends on it.
        // March through both sides and wake (matching Fortran order: IS=1,2)
        double legacySetblLaminarShearCarry = blState.LegacySetblLaminarShearCarry;
        for (int side = 0; side < 2; side++)
        {
            double ncrit = settings.GetEffectiveNCrit(side);

            // Previous station variables -- initialized from station 0 (virtual stagnation).
            // Station 0 has XSSI=0, UEDG=0, THET=0, DSTR=0 (Fortran IBL=1).
            // Leave at zero so the ternary fallbacks (u1 > 0 ? u1 : uei) in the
            // AssembleStationSystem call correctly substitute current-station values
            // for the similarity station. SIMI zeros VS1 so prev-station doesn't matter.
            double x1 = blState.XSSI[0, side];
            double u1 = blState.UEDG[0, side];
            double t1 = blState.THET[0, side];
            double d1 = blState.DSTR[0, side];
            double s1 = blState.CTAU[0, side];
            double dw1 = 0;
            double ampl1 = 0;
            double hk1 = 2.1, rt1 = 200.0; // Previous station Hk and Rt for transition check

            // DUE/DDS track the mismatch between the current UEDG state and the
            // UESET reconstruction USAV = UINV + DIJ*MASS (Fortran SETBL).
            double due1 = 0.0, dds1 = 0.0;

            // Legacy block: xbl.f SETBL station march.
            // Difference from legacy: The managed loop looks up `IV` through `ISYS` and carries explicit parity snapshots, but it still walks the same similarity, airfoil, and wake interval sequence as SETBL.
            // Decision: Keep the managed lookup and snapshot plumbing; preserve the original station ordering and interval classification.
            // March from station 1 (similarity) to NBL-1.
            // Fortran: DO IBL=2,NBL(IS) — station 2 is similarity in 1-based.
            // C# 0-based: station 1 = Fortran IBL=2 (similarity).
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

                bool simi = (ibl == 1); // C# station 1 = Fortran IBL=2 (similarity)
                bool wake = (ibl > blState.IBLTE[side]);
                bool tran = (ibl == blState.ITRAN[side]);
                bool turb = (ibl >= blState.ITRAN[side]);

                // Set primary variables for current station
                double xsi = blState.XSSI[ibl, side];
                double uei = blState.UEDG[ibl, side];
                double thi = blState.THET[ibl, side];
                double mdi = blState.MASS[ibl, side];
                double dsi = blState.DSTR[ibl, side];
                if (dsi <= 0.0 && LegacyPrecisionMath.Abs(uei, useLegacyPrecision) > 1e-30)
                {
                    dsi = LegacyPrecisionMath.Divide(mdi, uei, useLegacyPrecision);
                }
                double storedCtau = blState.CTAU[ibl, side];
                double cti = storedCtau;
                double ami;
                if (ibl < blState.ITRAN[side])
                {
                    // Classic SETBL only reloads AMI before transition and leaves
                    // the local CTI scratch live across later Newton assemblies.
                    // The default managed branch keeps the explicit 0.03 seed,
                    // while parity mode must replay the carried legacy scratch.
                    ami = storedCtau;
                    cti = useLegacyPrecision
                        ? legacySetblLaminarShearCarry
                        : ViscousSolverEngine.LegacyLaminarShearSeedValue;
                }
                else
                {
                    ami = 0.0;
                }
                legacySetblLaminarShearCarry = cti;

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
                double rt2 = Math.Max(LegacyPrecisionMath.Multiply(LegacyPrecisionMath.Abs(uei, useLegacyPrecision), thi, reybl, useLegacyPrecision), 200.0);

                // Transition check: call TransitionModel.CheckTransition during BL march
                if (!wake && !turb && !tran && ibl > 1 && x1 > 0 && xsi > x1)
                {
                    var trResult = TransitionModel.CheckTransition(
                        x1, xsi, ampl1, ami, ncrit,
                        hk1, t1 > 0 ? t1 : thi, rt1, u1 > 0 ? u1 : uei, d1 > 0 ? d1 : dsi,
                        hk2, thi, rt2, uei, dsi,
                        settings.UseModernTransitionCorrections, null,
                        settings.UseLegacyBoundaryLayerInitialization);

                    if (trResult.TransitionOccurred)
                    {
                        blState.ITRAN[side] = ibl;
                        tran = true;
                        turb = true;
                        if (debugWriter != null)
                        {
                            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "TRANSITION IS={0} IBL={1} XT={2,15:E8}", side + 1, ibl, xsi));
                        }
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
                        cti, thi, dsi - dswaki, dswaki,
                        useLegacyPrecision);

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
                        hvrat, reybl, reybl_re, reybl_ms,
                        useLegacyPrecision: useLegacyPrecision,
                        // Parity mode must reuse the previous station's pre-accept
                        // BLKIN snapshot instead of recomputing it from the later
                        // accepted primary state, which is what classic XFoil feeds
                        // into the next BLSYS call. SETBL still rebuilds the
                        // downstream BLVAR/BLMID secondary packet from that
                        // accepted station state, so only the kinematic owner is
                        // replayed here.
                        station1KinematicOverride: useLegacyPrecision && ibl > 0
                            ? blState.LegacyKinematic[ibl - 1, side]
                            : null,
                        station1SecondaryOverride: null,
                        traceSide: side + 1,
                        traceStation: ibl + 1);
                }

                // Store the local station Jacobian blocks first, then override the
                // TE-coupling rows for the first wake point as Fortran SETBL does.
                for (int k = 0; k < 3; k++)
                {
                    va[k, 0, iv] = localResult.VS2[k, 0]; // Ctau/ampl at station 2
                    va[k, 1, iv] = localResult.VS2[k, 1]; // Theta at station 2
                    vb[k, 0, iv] = localResult.VS1[k, 0]; // Ctau/ampl at station 1
                    vb[k, 1, iv] = localResult.VS1[k, 1]; // Theta at station 1
                }

                double d2_u2 = (LegacyPrecisionMath.Abs(uei, useLegacyPrecision) > 1e-30)
                    ? -LegacyPrecisionMath.Divide(dsi, uei, useLegacyPrecision)
                    : 0.0;
                double d2_m2 = (LegacyPrecisionMath.Abs(uei, useLegacyPrecision) > 1e-30)
                    ? LegacyPrecisionMath.Divide(1.0, uei, useLegacyPrecision)
                    : 0.0;
                double due2 = uei - usav[ibl, side];
                double dds2 = d2_u2 * due2;

                if (wake && ibl == blState.IBLTE[side] + 1)
                {
                    double uete1 = blState.UEDG[blState.IBLTE[0], 0];
                    double uete2 = blState.UEDG[blState.IBLTE[1], 1];
                    double usavTe1 = usav[blState.IBLTE[0], 0];
                    double usavTe2 = usav[blState.IBLTE[1], 1];
                    double dteUte1 = (LegacyPrecisionMath.Abs(uete1, useLegacyPrecision) > 1e-30)
                        ? -LegacyPrecisionMath.Divide(blState.DSTR[blState.IBLTE[0], 0], uete1, useLegacyPrecision)
                        : 0.0;
                    double dteUte2 = (LegacyPrecisionMath.Abs(uete2, useLegacyPrecision) > 1e-30)
                        ? -LegacyPrecisionMath.Divide(blState.DSTR[blState.IBLTE[1], 1], uete2, useLegacyPrecision)
                        : 0.0;
                    due1 = 0.0;
                    dds1 = dteUte1 * (uete1 - usavTe1) + dteUte2 * (uete2 - usavTe2);
                }

                double xiUle1 = (side == 0) ? sstGo : -sstGo;
                double xiUle2 = (side == 0) ? -sstGp : sstGp;
                double xiForcing = xiUle1 * dule1 + xiUle2 * dule2;

                SolverTrace.Event(
                    "setbl_forcing_inputs",
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    new
                    {
                        side = side + 1,
                        station = ibl + 1,
                        iv = iv + 1,
                        uedgStation = blState.UEDG[ibl, side],
                        usavStation = usav[ibl, side],
                        d2_u2,
                        due2,
                        dds2,
                        uedgLe1 = blState.UEDG[1, 0],
                        usavLe1 = usav[1, 0],
                        dule1,
                        uedgLe2 = blState.UEDG[1, 1],
                        usavLe2 = usav[1, 1],
                        dule2,
                        sstGo,
                        sstGp,
                        xiUle1,
                        xiUle2,
                        xiForcing
                    });

                // Legacy block: xbl.f SETBL residual load into VDEL.
                // Difference from legacy: The residual forcing terms are written with named `due/dds/xi` components rather than inline array algebra, but the resulting row construction follows the same VS1/VS2 coupling as SETBL.
                // Decision: Keep the explicit decomposition because it makes the coupling auditable while preserving the legacy row assembly order.
                // Residuals go into VDEL -- indexed by iv
                // Include VS1/VS2 DUE/DDS forced-change terms matching Fortran SETBL
                // C# VS column layout: 0=Ctau/Ampl, 1=Theta, 2=D*, 3=Ue, 4=x
                // Fortran 1-based: col 3=D*, col 4=Ue => C# col 2=D*, col 3=Ue
                for (int k = 0; k < 3; k++)
                {
                    double vs1DueTerm = localResult.VS1[k, 3] * due1;
                    double vs1DdsTerm = localResult.VS1[k, 2] * dds1;
                    double vs2DueTerm = localResult.VS2[k, 3] * due2;
                    double vs2DdsTerm = localResult.VS2[k, 2] * dds2;
                    double xiCoeffBase = localResult.VS1[k, 4] + localResult.VS2[k, 4];
                    double xiBaseTerm = xiCoeffBase * xiForcing;
                    double xiVsxTerm = 0.0;
                    vdel[k, 0, iv] = localResult.Residual[k]
                                   + vs1DueTerm
                                   + vs1DdsTerm
                                   + vs2DueTerm
                                   + vs2DdsTerm
                                   + xiBaseTerm;
                    vdel[k, 1, iv] = 0.0;

                    SolverTrace.Event(
                        "setbl_vdel_terms",
                        SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                        new
                        {
                            side = side + 1,
                            station = ibl + 1,
                            iv = iv + 1,
                            row = k + 1,
                            residual = localResult.Residual[k],
                            vs1U = localResult.VS1[k, 3],
                            due1,
                            vs1DueTerm,
                            vs1D = localResult.VS1[k, 2],
                            dds1,
                            vs1DdsTerm,
                            vs2U = localResult.VS2[k, 3],
                            due2,
                            vs2DueTerm,
                            vs2D = localResult.VS2[k, 2],
                            dds2,
                            vs2DdsTerm,
                            vs1X = localResult.VS1[k, 4],
                            vs2X = localResult.VS2[k, 4],
                            vsx = 0.0,
                            xiForcing,
                            xiBaseTerm,
                            xiVsxTerm,
                            final = vdel[k, 0, iv]
                        });
                }

                // Diagnostic dump: station BL state, VA/VB blocks, VDEL residuals
                SolverTrace.Event(
                    "station_state",
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    new
                    {
                        side = side + 1,
                        station = ibl + 1,
                        iv = iv + 1,
                        xsi,
                        uei,
                        thi,
                        dsi,
                        mdi,
                        due2,
                        dds2,
                        xiForcing
                    });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VA_ROW1",
                    new[] { va[0, 0, iv], va[0, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VA_ROW2",
                    new[] { va[1, 0, iv], va[1, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VA_ROW3",
                    new[] { va[2, 0, iv], va[2, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VB_ROW1",
                    new[] { vb[0, 0, iv], vb[0, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VB_ROW2",
                    new[] { vb[1, 0, iv], vb[1, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VB_ROW3",
                    new[] { vb[2, 0, iv], vb[2, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VDEL_R",
                    new[] { vdel[0, 0, iv], vdel[1, 0, iv], vdel[2, 0, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VDEL_S",
                    new[] { vdel[0, 1, iv], vdel[1, 1, iv], vdel[2, 1, iv] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                SolverTrace.Array(
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    "VSREZ",
                    new[] { localResult.Residual[0], localResult.Residual[1], localResult.Residual[2] },
                    new { side = side + 1, station = ibl + 1, iv = iv + 1 });
                if (debugWriter != null)
                {
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "DUE2={0,15:E8} DDS2={1,15:E8} XIF={2,15:E8}",
                        due2, dds2, xiForcing));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "STATION IS={0,2} IBL={1,4} IV={2,4}", side + 1, ibl + 1, iv + 1));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "BL_STATE x={0} Ue={1} th={2} ds={3} m={4}",
                        FormatDebugSingle(xsi),
                        FormatDebugSingle(uei),
                        FormatDebugSingle(thi),
                        FormatDebugSingle(dsi),
                        FormatDebugSingle(mdi)));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VA_ROW1 {0,15:E8} {1,15:E8}", va[0, 0, iv], va[0, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VA_ROW2 {0,15:E8} {1,15:E8}", va[1, 0, iv], va[1, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VA_ROW3 {0,15:E8} {1,15:E8}", va[2, 0, iv], va[2, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VB_ROW1 {0,15:E8} {1,15:E8}", vb[0, 0, iv], vb[0, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VB_ROW2 {0,15:E8} {1,15:E8}", vb[1, 0, iv], vb[1, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VB_ROW3 {0,15:E8} {1,15:E8}", vb[2, 0, iv], vb[2, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VDEL_R {0,15:E8} {1,15:E8} {2,15:E8}",
                        vdel[0, 0, iv], vdel[1, 0, iv], vdel[2, 0, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VDEL_S {0,15:E8} {1,15:E8} {2,15:E8}",
                        vdel[0, 1, iv], vdel[1, 1, iv], vdel[2, 1, iv]));
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VSREZ {0,15:E8} {1,15:E8} {2,15:E8}",
                        localResult.Residual[0], localResult.Residual[1], localResult.Residual[2]));
                }

                // Legacy block: xbl.f SETBL mass-coupling fill for VM.
                // Difference from legacy: The managed code computes panel-index and sign lookups through helpers and traces each term, but it keeps the same DIJ-driven coupling order into `VM(:,JV,IV)`.
                // Decision: Keep the helper-based structure and tracing; preserve the original coupling formula and accumulation order.
                // Add DIJ coupling into VM (mass influence column)
                // VM[k, jv, iv] = VS2[k,2]*D2_M[jv] + VS2[k,3]*UE_M[jv]*u2_uei
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
                            vm[k, jv, iv] = localResult.VS2[k, 2] * d2_mj
                                          + localResult.VS2[k, 3] * ue_mj * u2_uei;
                        }
                    }
                }

                // Legacy block: xbl.f SETBL RMS accumulation.
                // Difference from legacy: The managed code funnels the sum through `LegacyPrecisionMath` so parity mode can replay REAL accumulation exactly.
                // Decision: Keep the shared helper path and preserve the legacy accumulation order.
                // Accumulate RMS of residuals
                for (int k = 0; k < 3; k++)
                {
                    rmsbl = LegacyPrecisionMath.Add(rmsbl, LegacyPrecisionMath.Square(localResult.Residual[k], useLegacyPrecision), useLegacyPrecision);
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
                due1 = due2;
                dds1 = dds2;
            }
        }

        if (useLegacyPrecision)
        {
            blState.LegacySetblLaminarShearCarry = legacySetblLaminarShearCarry;
        }

        // Compute RMS
        if (nResiduals > 0)
            rmsbl = LegacyPrecisionMath.Sqrt(LegacyPrecisionMath.Divide(rmsbl, nResiduals, useLegacyPrecision), useLegacyPrecision);

        SolverTrace.Event(
            "newton_system_ready",
            SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
            new { rmsbl, residualCount = nResiduals });

        return rmsbl;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: UESET
    // Difference from legacy: The same `USAV = UINV + DIJ*MASS` reconstruction is preserved, but the managed port splits airfoil and wake contributions explicitly and emits per-term traces for parity debugging.
    // Decision: Keep the decomposition and traces; preserve the legacy contribution order and parity arithmetic through `LegacyPrecisionMath`.
    private static double[,] ComputePredictedEdgeVelocities(
        BoundaryLayerSystemState blState,
        double[,] dij,
        double[,] ueInv,
        int isp,
        int nPanel,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
            new
            {
                upperStations = blState.NBL[0],
                lowerStations = blState.NBL[1],
                isp,
                nPanel
            });
        double[,] usav = new double[blState.MaxStations, 2];

        // Legacy block: xbl.f UESET side/station reconstruction loops.
        // Difference from legacy: The nested loops are explicit about airfoil versus wake contributions, but they still preserve the original DUI accumulator shape and march order.
        // Decision: Keep the explicit contribution split while matching the classic `DUI = DUI + UE_M*MASS` then `UEDG = UINV + DUI` update exactly.
        for (int side = 0; side < 2; side++)
        {
            usav[0, side] = 0.0;
            for (int ibl = 1; ibl < blState.NBL[side]; ibl++)
            {
                double ueInvLocal = ueInv[ibl, side];
                double airfoilContribution = 0.0;
                double wakeContribution = 0.0;
                double dui = 0.0;
                int iPan = GetPanelIndex(ibl, side, isp, nPanel, blState);
                if (iPan >= 0 && iPan < dij.GetLength(0))
                {
                    double vtiI = GetVTI(ibl, side, blState);
                    for (int jSide = 0; jSide < 2; jSide++)
                    {
                        for (int jbl = 1; jbl < blState.NBL[jSide]; jbl++)
                        {
                            int jPan = GetPanelIndex(jbl, jSide, isp, nPanel, blState);
                            if (jPan < 0 || jPan >= dij.GetLength(1))
                            {
                                continue;
                            }

                            double vtiJ = GetVTI(jbl, jSide, blState);
                            double dijValue = dij[iPan, jPan];
                            double massValue = blState.MASS[jbl, jSide];
                            double ueM = -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dijValue, useLegacyPrecision);
                            double contribution = LegacyPrecisionMath.Multiply(ueM, massValue, useLegacyPrecision);
                            dui = LegacyPrecisionMath.Add(dui, contribution, useLegacyPrecision);

                            bool isWakeSource = jbl > blState.IBLTE[jSide];
                            if (isWakeSource)
                            {
                                wakeContribution = LegacyPrecisionMath.Add(wakeContribution, contribution, useLegacyPrecision);
                            }
                            else
                            {
                                airfoilContribution = LegacyPrecisionMath.Add(airfoilContribution, contribution, useLegacyPrecision);
                            }

                            SolverTrace.Event(
                                "predicted_edge_velocity_term",
                                SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                                new
                                {
                                    side = side + 1,
                                    station = ibl + 1,
                                    sourceSide = jSide + 1,
                                    sourceStation = jbl + 1,
                                    iPan = iPan + 1,
                                    jPan = jPan + 1,
                                    vtiI,
                                    vtiJ,
                                    dij = dijValue,
                                    mass = massValue,
                                    ueM,
                                    contribution,
                                    isWakeSource
                                });
                        }
                    }
                }

                double predicted = LegacyPrecisionMath.Add(ueInvLocal, dui, useLegacyPrecision);
                usav[ibl, side] = predicted;
                SolverTrace.Event(
                    "predicted_edge_velocity",
                    SolverTrace.ScopeName(typeof(ViscousNewtonAssembler)),
                    new
                    {
                        side = side + 1,
                        station = ibl + 1,
                        ueInv = ueInvLocal,
                        airfoilContribution,
                        wakeContribution,
                        predicted
                    });

                if (debugWriter != null)
                {
                    if (side == 0 && ibl == 1)
                    {
                        double vtiI = GetVTI(ibl, side, blState);
                        for (int sourceSide = 0; sourceSide < 2; sourceSide++)
                        {
                            int maxAirfoilStation = Math.Min(blState.NBL[sourceSide], blState.IBLTE[sourceSide] + 1);
                            for (int jbl = 1; jbl < maxAirfoilStation; jbl++)
                            {
                                int jPan = GetPanelIndex(jbl, sourceSide, isp, nPanel, blState);
                                if (jPan < 0 || jPan >= dij.GetLength(1))
                                {
                                    continue;
                                }

                                double vtiJ = GetVTI(jbl, sourceSide, blState);
                                double ueM = -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dij[iPan, jPan], useLegacyPrecision);
                                double mass = blState.MASS[jbl, sourceSide];
                                double contribution = LegacyPrecisionMath.Multiply(ueM, mass, useLegacyPrecision);

                                debugWriter.WriteLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "USAV_AIR_TERM IS={0,2} IBL={1,4} JS={2,2} JBL={3,4} JPAN={4,4} UE_M={5,15:E8} MASS={6,15:E8} CONTR={7,15:E8}",
                                    side + 1,
                                    ibl + 1,
                                    sourceSide + 1,
                                    jbl + 1,
                                    jPan + 1,
                                    ueM,
                                    mass,
                                    contribution));
                            }
                        }

                        for (int jbl = blState.IBLTE[1] + 1;
                             jbl < Math.Min(blState.NBL[1], blState.IBLTE[1] + 6);
                             jbl++)
                        {
                            int jPan = GetPanelIndex(jbl, 1, isp, nPanel, blState);
                            if (jPan < 0 || jPan >= dij.GetLength(1))
                            {
                                continue;
                            }

                            double vtiJ = GetVTI(jbl, 1, blState);
                            double ueM = -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dij[iPan, jPan], useLegacyPrecision);
                            double mass = blState.MASS[jbl, 1];
                            double contribution = LegacyPrecisionMath.Multiply(ueM, mass, useLegacyPrecision);

                            debugWriter.WriteLine(string.Format(
                                CultureInfo.InvariantCulture,
                                "USAV_WAKE_TERM IS={0,2} IBL={1,4} JS={2,2} JBL={3,4} JPAN={4,4} UE_M={5} MASS={6} CONTR={7}",
                                side + 1,
                                ibl + 1,
                                2,
                                jbl + 1,
                                jPan + 1,
                                FormatDebugSingle(ueM),
                                FormatDebugSingle(mass),
                                FormatDebugSingle(contribution)));
                        }
                    }

                    debugWriter.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "USAV_SPLIT IS={0,2} IBL={1,4} UINV={2} AIR={3} WAKE={4} USAV={5}",
                        side + 1,
                        ibl + 1,
                        FormatDebugSingle(ueInvLocal),
                        FormatDebugSingle(airfoilContribution),
                        FormatDebugSingle(wakeContribution),
                        FormatDebugSingle(predicted)));
                }
            }
        }

        return usav;
    }

    private static string FormatDebugSingle(double value)
    {
        float single = (float)value;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,15:E8} [{1:X8}]",
            single,
            BitConverter.SingleToInt32Bits(single));
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: SETBL stagnation forcing terms
    // Difference from legacy: The algebra is unchanged, but the managed port isolates the leading-edge sensitivity calculation into a helper instead of recomputing it inline inside the station march.
    // Decision: Keep the helper extraction and preserve the original forcing formulas because the Newton residual rows depend on them.
    private static void ComputeLeadingEdgeSensitivities(
        BoundaryLayerSystemState blState,
        out double sstGo,
        out double sstGp,
        out double dule1,
        out double dule2,
        double[,] usav,
        bool useLegacyPrecision)
    {
        double upperUe = blState.UEDG[1, 0];
        double lowerUe = blState.UEDG[1, 1];
        double dgam = -LegacyPrecisionMath.Add(lowerUe, upperUe, useLegacyPrecision);

        if (LegacyPrecisionMath.Abs(dgam, useLegacyPrecision) > 1e-30)
        {
            sstGo = -LegacyPrecisionMath.Divide(blState.XSSI[1, 1], dgam, useLegacyPrecision);
            sstGp = -LegacyPrecisionMath.Divide(blState.XSSI[1, 0], dgam, useLegacyPrecision);
        }
        else
        {
            sstGo = 0.0;
            sstGp = 0.0;
        }

        dule1 = LegacyPrecisionMath.Subtract(blState.UEDG[1, 0], usav[1, 0], useLegacyPrecision);
        dule2 = LegacyPrecisionMath.Subtract(blState.UEDG[1, 1], usav[1, 1], useLegacyPrecision);
    }

    /// <summary>
    /// Gets the panel node index for a given BL station and side from the IPAN array.
    /// Returns -1 for virtual stagnation (station 0) or out-of-range stations.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only array accessor around state that lived in shared arrays in the Fortran code.
    // Decision: Keep the helper because it removes repeated bounds checks; no parity-specific branch is needed.
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        if (ibl < 0 || ibl >= blState.MaxStations) return -1;
        return blState.IPAN[ibl, side];
    }

    /// <summary>
    /// Gets the VTI sign factor for a BL station from the VTI array.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only accessor over the stored sign-factor array rather than a distinct Fortran subroutine.
    // Decision: Keep the helper for readability; no legacy-specialized behavior is required beyond returning the stored value.
    private static double GetVTI(int ibl, int side, BoundaryLayerSystemState blState)
    {
        if (ibl < 0 || ibl >= blState.MaxStations) return 1.0;
        return blState.VTI[ibl, side];
    }
}

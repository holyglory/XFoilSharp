#pragma warning disable CS8602 // Dereference of a possibly null reference - trace code
using System.Globalization;
using System.IO;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: SETBL
// Secondary legacy source(s): f_xfoil/src/xbl.f :: UESET
// Role in port: Global viscous Newton-system assembly that marches the airfoil and wake stations and couples them back to the inviscid influence system.
// Differences: The managed port splits SETBL into smaller helpers, keeps explicit state snapshots for parity debugging, and exposes the DIJ/leading-edge forcing terms with trace output instead of leaving them implicit in one monolithic routine.
// Decision: Keep the decomposed managed structure for default execution, but preserve the legacy station order, coupling order, and parity-specific stale-state behavior where binary replay depends on it.
using XFoil.Solver.Services;

namespace XFoil.Solver.Double.Services;

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
        int wakeGapCount,
        double tkbl, double qinfbl, double tkbl_ms,
        double hstinv, double hstinv_ms,
        double rstbl, double rstbl_ms,
        double reybl, double reybl_re, double reybl_ms,
        double hvrat,
        double[,] ueInv,
        int isp = -1,
        int nPanel = -1,
        double[,]? cachedUsav = null,
        double? cachedSstGo = null,
        double? cachedSstGp = null,
        // Raw trailing edge normal gap (Fortran ANTE). Used for the first wake
        // station's DTE merge: DTE = DSTR_TE1 + DSTR_TE2 + ANTE. Differs from
        // wakeGap[0] = WGAP(1) by ~2 ULP due to cubic (AA+BB) rounding.
        double anteRaw = 0.0)
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

        bool useLegacyPrecision = settings.UseLegacyBoundaryLayerInitialization;
        double amcrit = settings.CriticalAmplificationFactor;
        // Parity mode needs the REAL-staged GAMM1 value that classic XFoil feeds
        // into COMSET/BLKIN, not the double literal 0.4.
        double gm1bl = LegacyPrecisionMath.GammaMinusOne(useLegacyPrecision);
        double[,] usav = cachedUsav ?? ComputePredictedEdgeVelocities(blState, dij, ueInv, isp, nPanel, null, useLegacyPrecision);
        ComputeLeadingEdgeSensitivities(
            blState,
            out double sstGoComputed,
            out double sstGpComputed,
            out double dule1,
            out double dule2,
            usav,
            useLegacyPrecision);
        // Fortran SST_GO/SST_GP are computed ONCE in XICALC (xpanel.f) and
        // stored in COMMON. Use cached inviscid values when available;
        // otherwise use the computed values (which match at iteration 0).
        double sstGo = (useLegacyPrecision && cachedSstGo.HasValue)
            ? cachedSstGo.Value : sstGoComputed;
        double sstGp = (useLegacyPrecision && cachedSstGp.HasValue)
            ? cachedSstGp.Value : sstGpComputed;

        // GDB: dump TE + first wake station state for parity comparison
        
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
            // Fortran AMI is a local variable in SETBL that carries across loop
            // iterations.  At laminar stations AMI = CTAU(IBL,IS); at IBL >= ITRAN
            // AMI retains its previous value.  The C# must reproduce this carry so
            // the transition station receives the correct upstream amplification.
            double legacySetblAmplCarry = 0.0;
            double hk1 = 2.1, rt1 = 200.0; // Previous station Hk and Rt for transition check

            // DUE/DDS track the mismatch between the current UEDG state and the
            // UESET reconstruction USAV = UINV + DIJ*MASS (Fortran SETBL).
            double due1 = 0.0, dds1 = 0.0;

            // Previous station panel index and D/U sensitivities for VM assembly (VS1 terms)
            int iPanPrev = -1;
            int ivPrev = -1; // Previous station's system line index (for D1_M2 diagonal)
            double d1_u1 = 0.0;
            double d1_m2Prev = 0.0; // D1_M2 = 1/UEI_prev for diagonal
            // Track whether the previous station was processed as laminar
            // (BLDIF(1) branch in Fortran). At the next station, this tells
            // AssembleStationSystem to use laminar correlations for COM1.
            bool prevStationLaminar = false;

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
                bool turb = (ibl > blState.ITRAN[side]); // Fix #51: Fortran uses .GT. not .GE.
                // (trace moved below setblFullPoint)
                

                // Set primary variables for current station
                double xsi = blState.XSSI[ibl, side];
                double uei = blState.UEDG[ibl, side];
                double thi = blState.THET[ibl, side];
                double mdi = blState.MASS[ibl, side];
                TransitionModel.TransitionPointResult? setblFullPoint = null;
                // Fortran SETBL: DSI = MDI/UEI — always recomputes DSTR from MASS/UEDG.
                // This ensures double-precision parity with the Fortran single-precision division.
                
                
                double dsi;
                if (useLegacyPrecision && LegacyPrecisionMath.Abs(uei, false) > 1e-30)
                {
                    dsi = LegacyPrecisionMath.Divide(mdi, uei, false);
                }
                else
                {
                    dsi = blState.DSTR[ibl, side];
                    if (dsi <= 0.0 && Math.Abs(uei) > 1e-30)
                        dsi = mdi / uei;
                }
                double storedCtau = blState.CTAU[ibl, side];
                double cti = storedCtau;
                double ami;
                if (ibl < blState.ITRAN[side])
                {
                    // Fortran: IF(IBL.LT.ITRAN(IS)) AMI = CTAU(IBL,IS)
                    // AMI is updated from CTAU at laminar stations and carries forward.
                    ami = storedCtau;
                    legacySetblAmplCarry = ami;
                    // Classic SETBL only reloads AMI before transition and leaves
                    // the local CTI scratch live across later Newton assemblies.
                    // The default managed branch keeps the explicit 0.03 seed,
                    // while parity mode must replay the carried legacy scratch.
                    cti = useLegacyPrecision
                        ? legacySetblLaminarShearCarry
                        : ViscousSolverEngine.LegacyLaminarShearSeedValue;
                }
                else
                {
                    // Fortran: AMI not touched at IBL >= ITRAN — retains value
                    // from the previous (laminar) iteration.  At the transition
                    // station this provides the upstream amplification that
                    // BLPRV feeds to TRCHEK via AMPL2 = AMI.
                    ami = legacySetblAmplCarry;
                }
                legacySetblLaminarShearCarry = cti;

                double dswaki = 0.0;
                if (wake)
                {
                    int iw = ibl - blState.IBLTE[side];
                    if (wakeGap != null && iw >= 0 && iw < wakeGapCount)
                        dswaki = wakeGap[iw];
                    
                }

                // Note: Fortran SETBL does NOT apply HKLIM to DSI.
                // HKLIM (DSI = MAX(DSI-DSWAKI, 1.02*THI) + DSWAKI) is ONLY
                // in MRCHDU (xbl.f lines 1622-1623), not in SETBL.
                // Fix #31 incorrectly added this here. Removed.

                // Current station compressible velocity
                var (u2, u2_uei, u2_ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei, tkbl, qinfbl, tkbl_ms, useLegacyPrecision);

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

                // Transition check: Fortran SETBL calls TRCHEK when IBL==ITRAN (TRAN=true)
                // This updates XT (transition xi) and AMPL2 at the transition station.
                if (tran && !wake && ibl > 1 && x1 > 0 && xsi > x1)
                {
                    // Use CheckTransitionExact which recomputes HKT/RTT from
                    // BLKIN on interpolated (U,T,D), matching Fortran's TRCHEK.
                    // CheckTransition linearly interpolates HK/RT which is structurally
                    // wrong because HK is a nonlinear function of (T,D).
                    // Add trace of SETBL TRCHEK2 inputs at transition station
                    
                    var trResult = useLegacyPrecision
                        ? TransitionModel.CheckTransitionExact(
                            x1, xsi, ampl1, ami, ncrit,
                            u1 > 0 ? u1 : uei, uei,
                            t1 > 0 ? t1 : thi, thi,
                            d1 > 0 ? d1 : dsi, dsi,
                            hstinv, hstinv_ms,
                            gm1bl, rstbl, rstbl_ms, hvrat,
                            reybl, reybl_re, reybl_ms,
                            settings.UseModernTransitionCorrections,
                            blState.XSSI[blState.IBLTE[side], side],
                            settings.UseLegacyBoundaryLayerInitialization,
                            out setblFullPoint,
                            traceSide: side + 1,
                            traceStation: ibl + 1,
                            tracePhase: "setbl_trchek")
                        : TransitionModel.CheckTransition(
                            x1, xsi, ampl1, ami, ncrit,
                            hk1, t1 > 0 ? t1 : thi, rt1, u1 > 0 ? u1 : uei, d1 > 0 ? d1 : dsi,
                            hk2, thi, rt2, uei, dsi,
                            settings.UseModernTransitionCorrections, null,
                            settings.UseLegacyBoundaryLayerInitialization);

                    // Fortran SETBL line 262: AMI = AMPL2 after TRCHEK.
                    // AMPL2 = amplification at station 2 (downstream), not amcrit.
                    ami = trResult.TransitionOccurred
                        ? trResult.DownstreamAmplification
                        : trResult.AmplAtTransition;
                    legacySetblAmplCarry = ami;
                    // Fortran TRCHEK2 updates the TRAN flag: TRAN = TRFORC .OR. TRFREE.
                    // If TRCHEK rejects transition (ampl2 < amcrit and no forced),
                    // TRAN becomes false and the subsequent BLSYS branch takes
                    // `ELSE IF(.NOT.TURB) CALL BLDIF(1)` (pure laminar). Without
                    // this update, C# keeps `tran=true` from the initial
                    // `ibl == ITRAN[side]` check and calls the transition assembly
                    // with TRDIF-style output, producing a completely different
                    // Row 0 (shear-lag) instead of the laminar ampl equation.
                    if (tran && !trResult.TransitionOccurred)
                    {
                        tran = false;
                    }
                    // Trace transition result at side 2 ITRAN station
                    
                    // Trace XT at transition station 5 side 2 call 3
                    
                    
                    // Store transition arc-length position (Fortran: XSSITR(IS) = XT)
                    if (blState.TINDEX != null)
                        blState.TINDEX[side] = trResult.TransitionXi;
                }

                // Assemble local BL system
                BlsysResult localResult;

                if (wake && ibl == blState.IBLTE[side] + 1)
                {
                    // First wake point: use TESYS for TE-to-wake transition
                    // Fortran: TTE = THET(IBLTE(1),1) + THET(IBLTE(2),2) — REAL addition
                    double tte = LegacyPrecisionMath.Add(
                        blState.THET[blState.IBLTE[0], 0],
                        blState.THET[blState.IBLTE[1], 1],
                        useLegacyPrecision);
                    // Fortran line 354: DTE = DSTR_TE1 + DSTR_TE2 + ANTE — REAL 3-term
                    // Uses raw ANTE (TE normal gap), NOT WGAP(1). The cubic WGAP(1)
                    // differs from ANTE by ~2 ULP due to (AA+BB) = 1.0000001 rounding.
                    // `wakeGap[0]` holds WGAP(1) (cubic-evaluated) so we must NOT use
                    // it for the DTE merge. Use anteRaw (from wake seed) instead.
                    double ante = anteRaw;
                    double dte = LegacyPrecisionMath.Add(
                        LegacyPrecisionMath.Add(
                            blState.DSTR[blState.IBLTE[0], 0],
                            blState.DSTR[blState.IBLTE[1], 1],
                            useLegacyPrecision),
                        ante,
                        useLegacyPrecision);

                    // Fortran: CTE = (CTAU*THET + CTAU*THET) / TTE — REAL arithmetic
                    double ctteWeight = 0.0;
                    if (tte > 1e-30)
                    {
                        double ctProduct1 = LegacyPrecisionMath.Multiply(
                            blState.CTAU[blState.IBLTE[0], 0],
                            blState.THET[blState.IBLTE[0], 0],
                            useLegacyPrecision);
                        double ctProduct2 = LegacyPrecisionMath.Multiply(
                            blState.CTAU[blState.IBLTE[1], 1],
                            blState.THET[blState.IBLTE[1], 1],
                            useLegacyPrecision);
                        ctteWeight = LegacyPrecisionMath.Divide(
                            LegacyPrecisionMath.Add(ctProduct1, ctProduct2, useLegacyPrecision),
                            tte,
                            useLegacyPrecision);
                    }

                    double d2ForTesys = LegacyPrecisionMath.Subtract(dsi, dswaki, useLegacyPrecision);
                    var teResult = BoundaryLayerSystemAssembler.AssembleTESystem(
                        ctteWeight, tte, dte,
                        0, 0, msq2, 0,
                        cti, thi,
                        d2ForTesys,
                        dswaki,
                        useLegacyPrecision);

                    localResult = new BlsysResult
                    {
                        Residual = teResult.Residual,
                        VS1 = teResult.VS1,
                        VS2 = teResult.VS2
                    };

                    // Set VZ coupling block for TE junction -- use iv for VB indexing
                    // Fortran: CTE_CTE1 = THET(IBLTE(1),1)/TTE etc. — all REAL
                    double cte_cte1 = (tte > 1e-30) ? LegacyPrecisionMath.Divide(blState.THET[blState.IBLTE[0], 0], tte, useLegacyPrecision) : 0.5;
                    double cte_cte2 = (tte > 1e-30) ? LegacyPrecisionMath.Divide(blState.THET[blState.IBLTE[1], 1], tte, useLegacyPrecision) : 0.5;
                    double cte_tte1 = (tte > 1e-30) ? LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(blState.CTAU[blState.IBLTE[0], 0], ctteWeight, useLegacyPrecision), tte, useLegacyPrecision) : 0;
                    double cte_tte2 = (tte > 1e-30) ? LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(blState.CTAU[blState.IBLTE[1], 1], ctteWeight, useLegacyPrecision), tte, useLegacyPrecision) : 0;

                    
                    for (int k = 0; k < 3; k++)
                    {
                        vz[k, 0] = LegacyPrecisionMath.Multiply(localResult.VS1[k, 0], cte_cte1, useLegacyPrecision);
                        vz[k, 1] = LegacyPrecisionMath.Add(LegacyPrecisionMath.Multiply(localResult.VS1[k, 0], cte_tte1, useLegacyPrecision), localResult.VS1[k, 1], useLegacyPrecision);
                        vb[k, 0, iv] = LegacyPrecisionMath.Multiply(localResult.VS1[k, 0], cte_cte2, useLegacyPrecision);
                        vb[k, 1, iv] = LegacyPrecisionMath.Add(LegacyPrecisionMath.Multiply(localResult.VS1[k, 0], cte_tte2, useLegacyPrecision), localResult.VS1[k, 1], useLegacyPrecision);
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
                        // Route through ThreadStatic pooled buffers so the
                        // per-station × per-iter call never hits the GC.
                        destinationResult: BoundaryLayerSystemAssembler.GetPooledBlsysResult(),
                        bldifBuffer: BoundaryLayerSystemAssembler.GetPooledBldifPrimary(),
                        useLegacyPrecision: useLegacyPrecision,
                        // Parity mode must reuse the previous station's pre-accept
                        // BLKIN snapshot instead of recomputing it from the later
                        // accepted primary state, which is what classic XFoil feeds
                        // into the next BLSYS call. SETBL still rebuilds the
                        // downstream BLVAR/BLMID secondary packet from that
                        // accepted station state, so only the kinematic owner is
                        // replayed here.
                        // Fortran SETBL carries COM1 from the PREVIOUS station's
                        // fresh BLKIN call within the same SETBL sweep. The stored
                        // LegacyKinematic comes from MRCHDU which ran BEFORE UPDATE,
                        // so its HK/RT are stale. Passing null forces recomputation
                        // from the current primary state, matching Fortran's behavior.
                        station1KinematicOverride: null,
                        station1SecondaryOverride: null,
                        traceSide: side + 1,
                        traceStation: ibl + 1,
                        traceIteration: 0,
                        staleVs121: 0.0,
                        // Fortran SETBL: TRCHEK computes XT once, then BLSYS/TRDIF
                        // reads XT from COMMON. Passing the SETBL TRCHEK result as
                        // transitionPointOverride prevents a second TRCHEK2 Newton
                        // solve that may converge 7 ULP differently.
                        transitionPointOverride: (tran && useLegacyPrecision) ? setblFullPoint : null,
                        // Fortran XIFSET: XIFORC = XSSI(IBLTE(IS),IS) for default XSTRIP>=1
                        forcedXtr: (tran && useLegacyPrecision)
                            ? (double?)blState.XSSI[blState.IBLTE[side], side]
                            : null,
                        // Fix #86: COM1 carries previous station's BLVAR output.
                        // When prev station was processed as laminar (BLDIF(1)
                        // branch in Fortran), COM1 has laminar secondary.
                        station1IsLaminar: prevStationLaminar && useLegacyPrecision);
                    
                }

                // Moved per-IV hash to after full assembly
                // Per-station residual trace at buildCall=8 for divergence localization

                // Similarity residual trace for iter-0 debugging

                // Trace VS2(3,2) at station 8 side 2 for VA parity

                // Trace BLVAR output at first Newton iter, station 2 side 1

                // Dump TRDIF VS2 row 1 at transition station, SETBL call 14

                // Per-station hash at SETBL call 2-18 to find divergent station

                // NACA 0012 2M a=3: dump TRAN flag at call 2 side 2 station 65
                
                // ah79k135: dump TE station (side 2 IBL 67) at every iter for parity
                
                // NACA 0012 2M a=3: per-cell at call 2 side 2 station 66 (ibl=65 0-idx, post-trans)
                
                // NACA 0012 2M a=3: per-cell at call 2 side 2 station 65 (ibl=64 0-idx)
                
                // Full VS2/VS1 per-cell trace at call 3 for side 2 stations 70-75 (NACA 0009 5M debug)
                

                // Trace VS1/VS2 at transition and wake stations
                
                // Hex trace for parity debugging (VSREZ comparison with Fortran F_VSREZ)
                

                // Trace VS1 at transition station 5 side 2 call 3
                

                // Store the local station Jacobian blocks first, then override the
                // TE-coupling rows for the first wake point as Fortran SETBL does.
                for (int k = 0; k < 3; k++)
                {
                    va[k, 0, iv] = localResult.VS2[k, 0]; // Ctau/ampl at station 2
                    va[k, 1, iv] = localResult.VS2[k, 1]; // Theta at station 2
                    // Don't overwrite VB at the TE junction — it was already set
                    // with CTE/TTE factors at lines 420-421.
                    if (!(wake && ibl == blState.IBLTE[side] + 1))
                    {
                        vb[k, 0, iv] = localResult.VS1[k, 0]; // Ctau/ampl at station 1
                        vb[k, 1, iv] = localResult.VS1[k, 1]; // Theta at station 1
                    }
                }

                double d2_u2 = (LegacyPrecisionMath.Abs(uei, useLegacyPrecision) > 1e-30)
                    ? -LegacyPrecisionMath.Divide(dsi, uei, useLegacyPrecision)
                    : 0.0;
                double d2_m2 = (LegacyPrecisionMath.Abs(uei, useLegacyPrecision) > 1e-30)
                    ? LegacyPrecisionMath.Divide(1.0, uei, useLegacyPrecision)
                    : 0.0;
                // Fortran SETBL: DUE2 = UEDG(IBL,IS) - USAV(IBL,IS); DDS2 = D2_U2*DUE2
                // ALL REAL per-operation.
                double due2 = LegacyPrecisionMath.Subtract(uei, usav[ibl, side], useLegacyPrecision);

                
                // Trace USAV/DUE2 at station 23 side 2 call 4
                
                // Trace USAV/DUE2 at station 16 side 2
                
                double dds2 = LegacyPrecisionMath.Multiply(d2_u2, due2, useLegacyPrecision);

                // GDB parity hex dump of station state
                

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
                    // Phase 1 strip: double-only path. Fortran SETBL: DDS1 =
                    // DTE_UTE1*(UEDG(IBLTE1)-USAV(IBLTE1)) + DTE_UTE2*...
                    // ALL REAL per-operation arithmetic.
                    double term1 = (double)dteUte1 * ((double)uete1 - (double)usavTe1);
                    double term2 = (double)dteUte2 * ((double)uete2 - (double)usavTe2);
                    dds1 = term1 + term2;
                }

                double xiUle1 = (side == 0) ? sstGo : -sstGo;
                double xiUle2 = (side == 0) ? -sstGp : sstGp;
                // Fortran: XIFORC = XIFORC1*DULE1 + XIFORC2*DULE2 — REAL per-op
                double xiForcing = LegacyPrecisionMath.Add(
                    LegacyPrecisionMath.Multiply(xiUle1, dule1, useLegacyPrecision),
                    LegacyPrecisionMath.Multiply(xiUle2, dule2, useLegacyPrecision),
                    useLegacyPrecision);


                // Legacy block: xbl.f SETBL residual load into VDEL.
                // Difference from legacy: The residual forcing terms are written with named `due/dds/xi` components rather than inline array algebra, but the resulting row construction follows the same VS1/VS2 coupling as SETBL.
                // Decision: Keep the explicit decomposition because it makes the coupling auditable while preserving the legacy row assembly order.
                // Residuals go into VDEL -- indexed by iv
                // Include VS1/VS2 DUE/DDS forced-change terms matching Fortran SETBL
                // C# VS column layout: 0=Ctau/Ampl, 1=Theta, 2=D*, 3=Ue, 4=x
                // Fortran 1-based: col 3=D*, col 4=Ue => C# col 2=D*, col 3=Ue
                for (int k = 0; k < 3; k++)
                {
                    // Phase 1 strip: double-only path. Fortran SETBL:
                    //   VSREZ(K) = VSREZ(K) + (VS1(K,4)*DUE1 + VS1(K,3)*DDS1
                    //                        + VS2(K,4)*DUE2 + VS2(K,3)*DDS2)
                    // ALL REAL per-operation arithmetic. Paired grouping
                    // matters because double is not associative.
                    double res = (double)localResult.Residual[k];
                    double vs1Due = (double)localResult.VS1[k, 3] * (double)due1;
                    double vs1Dds = (double)localResult.VS1[k, 2] * (double)dds1;
                    double vs2Due = (double)localResult.VS2[k, 3] * (double)due2;
                    double vs2Dds = (double)localResult.VS2[k, 2] * (double)dds2;
                    double xiCoeff = (double)localResult.VS1[k, 4] + (double)localResult.VS2[k, 4] + (double)localResult.VSX[k];
                    double xiTerm = xiCoeff * (double)xiForcing;
                    double duePair1 = vs1Due + vs1Dds;
                    double duePair2 = vs2Due + vs2Dds;
                    double acc = res + duePair1;
                    acc += duePair2;
                    acc += xiTerm;
                    vdel[k, 0, iv] = acc;
                    vdel[k, 1, iv] = 0.0;
                }

                // Diagnostic dump: station BL state, VA/VB blocks, VDEL residuals
                // GDB parity hex dump of raw VSREZ and VDEL
                

                // Legacy block: xbl.f SETBL mass-coupling fill for VM.
                // VM(k,JV,IV) = VS1(k,3)*D1_M(JV) + VS1(k,4)*U1_M(JV)
                //             + VS2(k,3)*D2_M(JV) + VS2(k,4)*U2_M(JV)
                //             + (VS1(k,5) + VS2(k,5) + VSX(k))
                //              *(XI_ULE1*ULE1_M(JV) + XI_ULE2*ULE2_M(JV))
                int iPanCur = GetPanelIndex(ibl, side, isp, nPanel, blState);
                for (int jv = 0; jv < nsys; jv++)
                {
                    int jbl = isys[jv, 0];
                    int jside = isys[jv, 1];
                    int jPan = GetPanelIndex(jbl, jside, isp, nPanel, blState);

                    if (jPan < 0 || jPan >= dij.GetLength(1))
                        continue;

                    double vtiJ = GetVTI(jbl, jside, blState);

                    // U2_M(JV) = -VTI(IBL,IS)*VTI(JBL,JS)*DIJ(I,J)
                    double u2_mj = 0.0;
                    double d2_mj_local = 0.0;
                    if (iPanCur >= 0 && iPanCur < dij.GetLength(0))
                    {
                        double vtiI = GetVTI(ibl, side, blState);
                        // Phase 1 strip: Fortran computes U2_M and D2_M in REAL (double)
                        double u2f = (double)(-(double)vtiI * (double)vtiJ * (double)dij[iPanCur, jPan]);
                        u2_mj = u2f;
                        double d2f = (double)((double)d2_u2 * u2f);
                        if (jv == iv) d2f = (double)(d2f + (double)d2_m2);
                        d2_mj_local = d2f;
                    }

                    // U1_M(JV) = -VTI(IBM,IS)*VTI(JBL,JS)*DIJ(I_prev,J)
                    double u1_mj = 0.0;
                    double d1_mj = 0.0;
                    if (wake && ibl == blState.IBLTE[side] + 1)
                    {
                        // First wake station: D1_M comes from TE merge coupling
                        // D1_M(JV) = DTE_UTE1*UTE1_M(JV) + DTE_UTE2*UTE2_M(JV)
                        int ite1Pan = GetPanelIndex(blState.IBLTE[0], 0, isp, nPanel, blState);
                        int ite2Pan = GetPanelIndex(blState.IBLTE[1], 1, isp, nPanel, blState);
                        double uete1 = blState.UEDG[blState.IBLTE[0], 0];
                        double uete2 = blState.UEDG[blState.IBLTE[1], 1];
                        // Fortran: DTE_UTE1 = -DSTR(IBLTE(1),1)/UEDG(IBLTE(1),1) etc. — REAL
                        double dteUte1 = LegacyPrecisionMath.Abs(uete1, useLegacyPrecision) > 1e-30
                            ? -LegacyPrecisionMath.Divide(blState.DSTR[blState.IBLTE[0], 0], uete1, useLegacyPrecision) : 0.0;
                        double dteUte2 = LegacyPrecisionMath.Abs(uete2, useLegacyPrecision) > 1e-30
                            ? -LegacyPrecisionMath.Divide(blState.DSTR[blState.IBLTE[1], 1], uete2, useLegacyPrecision) : 0.0;
                        double dteMte1 = LegacyPrecisionMath.Abs(uete1, useLegacyPrecision) > 1e-30
                            ? LegacyPrecisionMath.Divide(1.0, uete1, useLegacyPrecision) : 0.0;
                        double dteMte2 = LegacyPrecisionMath.Abs(uete2, useLegacyPrecision) > 1e-30
                            ? LegacyPrecisionMath.Divide(1.0, uete2, useLegacyPrecision) : 0.0;

                        double ute1_mj = 0.0, ute2_mj = 0.0;
                        if (ite1Pan >= 0 && ite1Pan < dij.GetLength(0))
                        {
                            double vtiTe1 = GetVTI(blState.IBLTE[0], 0, blState);
                            ute1_mj = useLegacyPrecision
                                ? (double)(-(double)vtiTe1 * (double)vtiJ * (double)dij[ite1Pan, jPan])
                                : -vtiTe1 * vtiJ * dij[ite1Pan, jPan];
                        }
                        if (ite2Pan >= 0 && ite2Pan < dij.GetLength(0))
                        {
                            double vtiTe2 = GetVTI(blState.IBLTE[1], 1, blState);
                            ute2_mj = useLegacyPrecision
                                ? (double)(-(double)vtiTe2 * (double)vtiJ * (double)dij[ite2Pan, jPan])
                                : -vtiTe2 * vtiJ * dij[ite2Pan, jPan];
                        }

                        // Fortran: D1_M = DTE_UTE1*UTE1_M + DTE_UTE2*UTE2_M — REAL sum-of-products
                        d1_mj = LegacyPrecisionMath.Add(
                            LegacyPrecisionMath.Multiply(dteUte1, ute1_mj, useLegacyPrecision),
                            LegacyPrecisionMath.Multiply(dteUte2, ute2_mj, useLegacyPrecision),
                            useLegacyPrecision);
                        // Add diagonal mass sensitivities at TE stations
                        int jvTe1 = -1, jvTe2 = -1;
                        for (int jvt = 0; jvt < nsys; jvt++)
                        {
                            if (isys[jvt, 0] == blState.IBLTE[0] && isys[jvt, 1] == 0) jvTe1 = jvt;
                            if (isys[jvt, 0] == blState.IBLTE[1] && isys[jvt, 1] == 1) jvTe2 = jvt;
                        }
                        if (jv == jvTe1) d1_mj = LegacyPrecisionMath.Add(d1_mj, dteMte1, useLegacyPrecision);
                        if (jv == jvTe2) d1_mj = LegacyPrecisionMath.Add(d1_mj, dteMte2, useLegacyPrecision);

                        // U1_M for first wake station is not used in Fortran's VM formula
                        // (only D1_M matters since it uses DTE coupling)
                        u1_mj = 0.0;
                    }
                    else if (iPanPrev >= 0 && iPanPrev < dij.GetLength(0))
                    {
                        double vtiPrev = GetVTI(ibl > 0 ? ibl - 1 : 0, side, blState);
                        // Phase 1 strip: double-only path.
                        double u1f = (double)(-(double)vtiPrev * (double)vtiJ * (double)dij[iPanPrev, jPan]);
                        u1_mj = u1f;
                        double d1f = (double)((double)d1_u1 * u1f);
                        if (jv == ivPrev) d1f = (double)(d1f + (double)d1_m2Prev);
                        d1_mj = d1f;
                    }

                    // ULE1_M(JV) = -VTI(2,1)*VTI(JBL,JS)*DIJ(ILE1,J)
                    // ULE2_M(JV) = -VTI(2,2)*VTI(JBL,JS)*DIJ(ILE2,J)
                    double ule1_mj = 0.0, ule2_mj = 0.0;
                    int ile1 = GetPanelIndex(1, 0, isp, nPanel, blState);
                    int ile2 = GetPanelIndex(1, 1, isp, nPanel, blState);
                    if (ile1 >= 0 && ile1 < dij.GetLength(0))
                    {
                        double vtiLe1 = GetVTI(1, 0, blState);
                        ule1_mj = useLegacyPrecision
                            ? (double)(-(double)vtiLe1 * (double)vtiJ * (double)dij[ile1, jPan])
                            : -vtiLe1 * vtiJ * dij[ile1, jPan];
                    }
                    if (ile2 >= 0 && ile2 < dij.GetLength(0))
                    {
                        double vtiLe2 = GetVTI(1, 1, blState);
                        ule2_mj = useLegacyPrecision
                            ? (double)(-(double)vtiLe2 * (double)vtiJ * (double)dij[ile2, jPan])
                            : -vtiLe2 * vtiJ * dij[ile2, jPan];
                    }

                    // Trace VM_40 diagonal for debugging
                    

                    for (int k = 0; k < 3; k++)
                    {
                        // Stagnation coupling (XI_ULE terms)
                        double xiUle1Vm = (side == 0) ? sstGo : -sstGo;
                        double xiUle2Vm = (side == 0) ? -sstGp : sstGp;

                        // Phase 1 strip: double-only path. Fortran:
                        //   VM(k,JV,IV) = VS1*D1_M + VS1*U1_M + VS2*D2_M + VS2*U2_M
                        //     + (VS1(k,5)+VS2(k,5)+VSX(k)) * (XI_ULE1*ULE1_M + XI_ULE2*ULE2_M)
                        // ALL REAL arithmetic — compute xiTerm in double; sequential
                        // accumulation with RoundBarrier matches Fortran left-to-right
                        // REAL*4 evaluation; (VS1+VS2)+VSX paren order matters.
                        double vs1d = (double)localResult.VS1[k, 2];
                        double vs1u = (double)localResult.VS1[k, 3];
                        double vs2d = (double)localResult.VS2[k, 2];
                        double vs2u = (double)localResult.VS2[k, 3];
                        double xiCoeffF = 0.0d;
                        if (localResult.VS1.GetLength(1) > 4)
                            xiCoeffF = (double)localResult.VS1[k, 4];
                        if (localResult.VS2.GetLength(1) > 4)
                            xiCoeffF = LegacyPrecisionMath.RoundBarrier(
                                LegacyPrecisionMath.RoundBarrier(xiCoeffF)
                                + (double)localResult.VS2[k, 4]);
                        xiCoeffF = LegacyPrecisionMath.RoundBarrier(
                            LegacyPrecisionMath.RoundBarrier(xiCoeffF)
                            + (double)localResult.VSX[k]);
                        double ule1Prod = LegacyPrecisionMath.RoundBarrier(
                            (double)xiUle1Vm * (double)ule1_mj);
                        double ule2Prod = LegacyPrecisionMath.RoundBarrier(
                            (double)xiUle2Vm * (double)ule2_mj);
                        double uleSum = LegacyPrecisionMath.RoundBarrier(ule1Prod + ule2Prod);
                        double xiTermF = LegacyPrecisionMath.RoundBarrier(xiCoeffF * uleSum);
                        double p1 = LegacyPrecisionMath.RoundBarrier(vs1d * (double)d1_mj);
                        double p2 = LegacyPrecisionMath.RoundBarrier(vs1u * (double)u1_mj);
                        double p3 = LegacyPrecisionMath.RoundBarrier(vs2d * (double)d2_mj_local);
                        double p4 = LegacyPrecisionMath.RoundBarrier(vs2u * (double)u2_mj);
                        double vmAcc = LegacyPrecisionMath.RoundBarrier(p1 + p2);
                        vmAcc = LegacyPrecisionMath.RoundBarrier(vmAcc + p3);
                        vmAcc = LegacyPrecisionMath.RoundBarrier(vmAcc + p4);
                        double vmVal = LegacyPrecisionMath.RoundBarrier(vmAcc + xiTermF);
                        vm[k, jv, iv] = vmVal;
                    }
                }

                // GDB: dump VM and panel indices at IV=0
                

                // Trace VM band entries at station 160 (iv=159), iteration 14
                

                // Legacy block: xbl.f SETBL RMS accumulation.
                // Difference from legacy: The managed code funnels the sum through `LegacyPrecisionMath` so parity mode can replay REAL accumulation exactly.
                // Decision: Keep the shared helper path and preserve the legacy accumulation order.
                // Accumulate RMS of residuals
                for (int k = 0; k < 3; k++)
                {
                    rmsbl = LegacyPrecisionMath.Add(rmsbl, LegacyPrecisionMath.Square(localResult.Residual[k], useLegacyPrecision), useLegacyPrecision);
                    nResiduals++;
                }

                // Store TAU for CDF integration (Fortran: TAU = 0.5*R2*U2^2*CF2)
                // For incompressible (M=0): R2=1, U2≈UEI, CF2 from secondary
                if (localResult.Secondary2Snapshot != null)
                {
                    double cf2 = localResult.Secondary2Snapshot.Cf;
                    double u2Local = localResult.U2;
                    // R2 is density ratio; for M=0 it's 1.0
                    // For compressible: R2 = (1 + 0.5*GM1*M_local^2)^(1/GM1) / (1 + 0.5*GM1*M_inf^2)^(1/GM1)
                    // Simplify: at M=0, R2=1
                    double r2 = 1.0; // TODO: compute properly for compressible
                    blState.TAU[ibl, side] = 0.5 * r2 * u2Local * u2Local * cf2;
                }

                // Save as previous station for next iteration
                iPanPrev = iPanCur;
                ivPrev = iv; // Track previous station's system line for D1_M2 diagonal
                d1_u1 = d2_u2; // Previous station's D_U becomes next station's D1_U1
                d1_m2Prev = d2_m2; // Previous station's D_M2 becomes next station's D1_M2
                x1 = xsi;
                u1 = uei;
                t1 = thi;
                d1 = dsi;
                s1 = cti;
                dw1 = dswaki;
                // Fortran: COM2→COM1 copy (xbl.f line 548) makes AMPL1 = AMPL2.
                // AMPL2 was set by BLPRV to AMI (possibly modified by TRCHEK).
                // The carried ami matches this: at laminar stations it's CTAU[ibl],
                // at the transition station it's post-TRCHEK, at turbulent it carries.
                ampl1 = ami;
                hk1 = hk2;
                rt1 = rt2;
                due1 = due2;
                dds1 = dds2;
                // Fix #86: track whether this station was processed as
                // laminar (BLDIF(1)). This determines whether the NEXT
                // station's COM1 uses laminar or turbulent correlations.
                // Matches Fortran's COM1<-COM2 rotation where the secondary
                // values from BLVAR(1) carry forward implicitly.
                prevStationLaminar = !simi && !wake && !turb && !tran;
            }
        }

        // Phase 1 strip: always-legacy path persists laminar-shear carry.
        blState.LegacySetblLaminarShearCarry = legacySetblLaminarShearCarry;

        // Compute RMS
        if (nResiduals > 0)
            rmsbl = LegacyPrecisionMath.Sqrt(LegacyPrecisionMath.Divide(rmsbl, nResiduals, useLegacyPrecision), useLegacyPrecision);

        // Matrix hash for parity debugging (VA/VB/VM/VDEL)
        


        // Per-IV VM row hash AFTER full assembly at buildCall=2
        
        return rmsbl;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: UESET
    // Difference from legacy: The same `USAV = UINV + DIJ*MASS` reconstruction is preserved, but the managed port splits airfoil and wake contributions explicitly and emits per-term traces for parity debugging.
    // Decision: Keep the decomposition and traces; preserve the legacy contribution order and parity arithmetic through `LegacyPrecisionMath`.
    public static double[,] ComputePredictedEdgeVelocitiesPublic(
        BoundaryLayerSystemState blState,
        double[,] dij,
        double[,] ueInv,
        int isp,
        int nPanel,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
        => ComputePredictedEdgeVelocities(blState, dij, ueInv, isp, nPanel, debugWriter, useLegacyPrecision);

    private static double[,] ComputePredictedEdgeVelocities(
        BoundaryLayerSystemState blState,
        double[,] dij,
        double[,] ueInv,
        int isp,
        int nPanel,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
    {
        double[,] usav = SolverBuffers.UsavScratch(blState.MaxStations);

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
                // Removed: C_IPAN_DBG trace (no longer needed)
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

                            // DUI detail at station 29 side 2, JBL=104-105
                            
                            // DUI at station 53: every term from JBL 95+ on side 2
                            
                            // Per-term DUI at station 2 (ibl=1 side=0) for 1st SETBL call
                            
                            // Per-term DUI for station 9 (C# side=0 ibl=8, Fortran IS=1 IBL=9)
                            
                            // DEBUG: Find NaN-producing MASS at iter 1 / build call 2
                            
                    // Per-term DUI for station 2 (C# side=0 ibl=1, Fortran IS=1 IBL=2) — similarity station parity
                            
                            // Per-term DUI for station 88 (C# side=1 ibl=87, Fortran IS=2 IBL=88)
                            
                            // Trace term 3 (JBL=5) for parity debugging
                            
                            // Trace MASS at key stations during station 6 DUI sum
                            
                            // GDB per-term DUI trace for station 6 (C# ibl=5)
                            
                            
                            

                            bool isWakeSource = jbl > blState.IBLTE[jSide];
                            if (isWakeSource)
                            {
                                wakeContribution = LegacyPrecisionMath.Add(wakeContribution, contribution, useLegacyPrecision);
                            }
                            else
                            {
                                airfoilContribution = LegacyPrecisionMath.Add(airfoilContribution, contribution, useLegacyPrecision);
                            }

                        }
                    }
                }

                double predicted = LegacyPrecisionMath.Add(ueInvLocal, dui, useLegacyPrecision);
                usav[ibl, side] = predicted;
                // GDB parity: dump UESET accumulators
                

            }
        }

        return usav;
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

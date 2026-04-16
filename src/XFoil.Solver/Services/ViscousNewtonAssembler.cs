#pragma warning disable CS8602 // Dereference of a possibly null reference - trace code
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
    [ThreadStatic]
    private static int s_buildCallCount;

    internal static int BuildCallCount => s_buildCallCount;

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
        TextWriter? debugWriter = null,
        double[,]? cachedUsav = null,
        double? cachedSstGo = null,
        double? cachedSstGp = null,
        // Raw trailing edge normal gap (Fortran ANTE). Used for the first wake
        // station's DTE merge: DTE = DSTR_TE1 + DSTR_TE2 + ANTE. Differs from
        // wakeGap[0] = WGAP(1) by ~2 ULP due to cubic (AA+BB) rounding.
        double anteRaw = 0.0)
    {
        s_buildCallCount++;
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
        double[,] usav = cachedUsav ?? ComputePredictedEdgeVelocities(blState, dij, ueInv, isp, nPanel, debugWriter, useLegacyPrecision);
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
        if (DebugFlags.SetBlHex)
        {
            for (int ts = 0; ts < 2; ts++)
            {
                int teIbl = blState.IBLTE[ts];
                Console.Error.WriteLine(
                    $"C_SETBL_TE s={ts + 1} ibl={teIbl + 1}" +
                    $" T={BitConverter.SingleToInt32Bits((float)blState.THET[teIbl, ts]):X8}" +
                    $" D={BitConverter.SingleToInt32Bits((float)blState.DSTR[teIbl, ts]):X8}" +
                    $" U={BitConverter.SingleToInt32Bits((float)blState.UEDG[teIbl, ts]):X8}" +
                    $" C={BitConverter.SingleToInt32Bits((float)blState.CTAU[teIbl, ts]):X8}" +
                    $" M={BitConverter.SingleToInt32Bits((float)blState.MASS[teIbl, ts]):X8}" +
                    $" ITRAN={blState.ITRAN[ts] + 1}");
            }
            // First wake station (on lower surface = side 1)
            int wkIbl = blState.IBLTE[1] + 1;
            if (wkIbl < blState.NBL[1])
            {
                Console.Error.WriteLine(
                    $"C_SETBL_WK1 ibl={wkIbl + 1}" +
                    $" T={BitConverter.SingleToInt32Bits((float)blState.THET[wkIbl, 1]):X8}" +
                    $" D={BitConverter.SingleToInt32Bits((float)blState.DSTR[wkIbl, 1]):X8}" +
                    $" U={BitConverter.SingleToInt32Bits((float)blState.UEDG[wkIbl, 1]):X8}" +
                    $" C={BitConverter.SingleToInt32Bits((float)blState.CTAU[wkIbl, 1]):X8}");
            }
        }
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
                if (DebugFlags.SetBlHex
                    && side == 0 && ibl == blState.IBLTE[0])
                {
                    Console.Error.WriteLine(
                        $"C_SETBL_TRAN ibl={ibl} ITRAN0={blState.ITRAN[0]} IBLTE0={blState.IBLTE[0]}" +
                        $" tran={tran} turb={turb}");
                }

                // Set primary variables for current station
                double xsi = blState.XSSI[ibl, side];
                double uei = blState.UEDG[ibl, side];
                double thi = blState.THET[ibl, side];
                double mdi = blState.MASS[ibl, side];
                TransitionModel.TransitionPointResult? setblFullPoint = null;
                // Fortran SETBL: DSI = MDI/UEI — always recomputes DSTR from MASS/UEDG.
                // This ensures float-precision parity with the Fortran single-precision division.
                if (DebugFlags.SetBlHex
                    && side == 0 && ibl == 2)
                {
                    Console.Error.WriteLine(
                        $"C_SETBL3_IN" +
                        $" MASS={BitConverter.SingleToInt32Bits((float)mdi):X8}" +
                        $" UEDG={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" THET={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                        $" d1={BitConverter.SingleToInt32Bits((float)d1):X8}" +
                        $" t1={BitConverter.SingleToInt32Bits((float)t1):X8}" +
                        $" u1={BitConverter.SingleToInt32Bits((float)u1):X8}");
                }
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 1 && side == 0 && ibl <= 5)
                {
                    Console.Error.WriteLine(
                        $"C_STN_IN s={side + 1} i={ibl + 1,3}" +
                        $" MASS={BitConverter.SingleToInt32Bits((float)mdi):X8}" +
                        $" UEDG={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" THET={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                        $" DSTR={BitConverter.SingleToInt32Bits((float)blState.DSTR[ibl, side]):X8}" +
                        $" CTAU={BitConverter.SingleToInt32Bits((float)blState.CTAU[ibl, side]):X8}");
                }
                double dsi;
                if (useLegacyPrecision && LegacyPrecisionMath.Abs(uei, true) > 1e-30)
                {
                    dsi = LegacyPrecisionMath.Divide(mdi, uei, true);
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
                    if (wakeGap != null && iw >= 0 && iw < wakeGap.Length)
                        dswaki = wakeGap[iw];
                    if (DebugFlags.SetBlHex
                        && side == 1 && ibl == 89)
                    {
                        Console.Error.WriteLine(
                            $"C_VNA_DSWAKI ibl={ibl} iw={iw}" +
                            $" wakeGap[{iw}]={BitConverter.SingleToInt32Bits((float)dswaki):X8}" +
                            $" wakeGap.Length={wakeGap?.Length ?? -1}");
                    }
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
                    if (DebugFlags.SetBlHex
                        && s_buildCallCount >= 8 && side == 1)
                    {
                        Console.Error.WriteLine(
                            $"C_SETBL_TR bc={s_buildCallCount} s={side+1} i={ibl+1}" +
                            $" A1={BitConverter.SingleToInt32Bits((float)ampl1):X8}" +
                            $" AMI={BitConverter.SingleToInt32Bits((float)ami):X8}" +
                            $" X1={BitConverter.SingleToInt32Bits((float)x1):X8}" +
                            $" X2={BitConverter.SingleToInt32Bits((float)xsi):X8}" +
                            $" T1={BitConverter.SingleToInt32Bits((float)(t1>0?t1:thi)):X8}" +
                            $" T2={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                            $" D1={BitConverter.SingleToInt32Bits((float)(d1>0?d1:dsi)):X8}" +
                            $" D2={BitConverter.SingleToInt32Bits((float)dsi):X8}");
                    }
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
                    if (DebugFlags.SetBlHex
                        && side == 1 && ibl == blState.ITRAN[1])
                    {
                        double fXtr = blState.XSSI[blState.IBLTE[side], side];
                        Console.Error.WriteLine(
                            $"C_TRCHK call={s_buildCallCount}" +
                            $" x1={BitConverter.SingleToInt32Bits((float)x1):X8}" +
                            $" xsi={BitConverter.SingleToInt32Bits((float)xsi):X8}" +
                            $" fXtr={BitConverter.SingleToInt32Bits((float)fXtr):X8}" +
                            $" type={setblFullPoint?.Type}" +
                            $" wf2xf={BitConverter.SingleToInt32Bits((float)(setblFullPoint?.Wf2XF ?? 0)):X8}" +
                            $" trOcc={trResult.TransitionOccurred}" +
                            $" a2={BitConverter.SingleToInt32Bits((float)ami):X8}");
                    }
                    // Trace XT at transition station 5 side 2 call 3
                    if (DebugFlags.SetBlHex
                        && side == 1 && ibl == 4 && s_buildCallCount == 3)
                    {
                        Console.Error.WriteLine(
                            $"C_XT5_C3" +
                            $" XT={BitConverter.SingleToInt32Bits((float)trResult.TransitionXi):X8}" +
                            $" A1={BitConverter.SingleToInt32Bits((float)ampl1):X8}" +
                            $" A2={BitConverter.SingleToInt32Bits((float)ami):X8}" +
                            $" X1={BitConverter.SingleToInt32Bits((float)x1):X8}" +
                            $" X2={BitConverter.SingleToInt32Bits((float)xsi):X8}");
                    }
                    if (DebugFlags.SetBlHex
                        && side + 1 == 1 && ibl + 1 == 58)
                    {
                        Console.Error.WriteLine(
                            $"C_TRCHEK58 tran={trResult.TransitionOccurred}" +
                            $" ami={BitConverter.SingleToInt32Bits((float)ami):X8}" +
                            $" ampl1={BitConverter.SingleToInt32Bits((float)ampl1):X8}" +
                            $" AmplAt={BitConverter.SingleToInt32Bits((float)trResult.AmplAtTransition):X8}" +
                            $" DnAmpl={BitConverter.SingleToInt32Bits((float)trResult.DownstreamAmplification):X8}");
                    }
                    // Store transition arc-length position (Fortran: XSSITR(IS) = XT)
                    if (blState.TINDEX != null)
                        blState.TINDEX[side] = trResult.TransitionXi;
                    if (debugWriter != null)
                    {
                        debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "TRANSITION IS={0} IBL={1} XT={2,15:E8}", side + 1, ibl, trResult.TransitionXi));
                    }
                }

                // Assemble local BL system
                BoundaryLayerSystemAssembler.BlsysResult localResult;

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

                    localResult = new BoundaryLayerSystemAssembler.BlsysResult
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

                    if (DebugFlags.SetBlHex && s_buildCallCount == 1)
                        Console.Error.WriteLine($"C_TE_VB side={side} ibl={ibl} iv={iv} ISYS_iv={isys[ibl, side]}");
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
                    if (Environment.GetEnvironmentVariable("XFOIL_COM2_IT1") == "1"
                        && side == 0 && ibl >= 1 && ibl <= 3)
                    {
                        Console.Error.WriteLine($"C_PRE_ASSEMBLE s=1 ibl={ibl} (=F IBL={ibl+1}) t1={BitConverter.SingleToInt32Bits((float)t1):X8} d1={BitConverter.SingleToInt32Bits((float)d1):X8} u1={BitConverter.SingleToInt32Bits((float)u1):X8} t2={BitConverter.SingleToInt32Bits((float)thi):X8} d2={BitConverter.SingleToInt32Bits((float)dsi):X8} u2={BitConverter.SingleToInt32Bits((float)uei):X8}");
                    }
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
                        // Fortran SETBL carries COM1 from the PREVIOUS station's
                        // fresh BLKIN call within the same SETBL sweep. The stored
                        // LegacyKinematic comes from MRCHDU which ran BEFORE UPDATE,
                        // so its HK/RT are stale. Passing null forces recomputation
                        // from the current primary state, matching Fortran's behavior.
                        station1KinematicOverride: null,
                        station1SecondaryOverride: null,
                        traceSide: side + 1,
                        traceStation: ibl + 1,
                        traceIteration: s_buildCallCount,
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
                    if (DebugFlags.SetBlHex
                        && side == 0 && ibl == blState.IBLTE[0])
                    {
                        Console.Error.WriteLine(
                            $"C_FXTR_PASS tran={tran} legacy={useLegacyPrecision}" +
                            $" val={(tran && useLegacyPrecision ? blState.XSSI[blState.IBLTE[side], side].ToString() : "null")}");
                    }
                }

                // Moved per-IV hash to after full assembly
                // Per-station residual trace at buildCall=8 for divergence localization
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 8)
                {
                    Console.Error.WriteLine(
                        $"C_VDEL8 s={side+1} i={ibl+1,4}" +
                        $" R0={BitConverter.SingleToInt32Bits((float)localResult.Residual[0]):X8}" +
                        $" R1={BitConverter.SingleToInt32Bits((float)localResult.Residual[1]):X8}" +
                        $" R2={BitConverter.SingleToInt32Bits((float)localResult.Residual[2]):X8}");
                    if (side == 0 && (ibl == 25 || ibl == 26))
                        Console.Error.WriteLine(
                            $"C_STN8 s=1 i={ibl+1}" +
                            $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                            $" THI={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                            $" MDI={BitConverter.SingleToInt32Bits((float)mdi):X8}" +
                            $" DSI={BitConverter.SingleToInt32Bits((float)dsi):X8}" +
                            $" AMI={BitConverter.SingleToInt32Bits((float)ami):X8}" +
                            $" CTI={BitConverter.SingleToInt32Bits((float)cti):X8}");
                }
                // Similarity residual trace for iter-0 debugging
                if (Environment.GetEnvironmentVariable("XFOIL_DUMP_RESID_IT0") == "1"
                    && s_buildCallCount == 1 && ibl <= 2)
                {
                    Console.Error.WriteLine(
                        $"C_RES_IT0 s={side+1} i={ibl+1,4}" +
                        $" R0={BitConverter.SingleToInt32Bits((float)localResult.Residual[0]):X8}" +
                        $" R1={BitConverter.SingleToInt32Bits((float)localResult.Residual[1]):X8}" +
                        $" R2={BitConverter.SingleToInt32Bits((float)localResult.Residual[2]):X8}");
                }
                // Trace VS2(3,2) at station 8 side 2 for VA parity
                if (DebugFlags.SetBlHex
                    && side == 1 && ibl == 8 && s_buildCallCount == 1)
                {
                    float vs2f = (float)localResult.VS2[2, 1];
                    Console.Error.WriteLine(
                        $"C_VS232_FINAL={BitConverter.SingleToInt32Bits(vs2f):X8}");
                    // Also dump the row 3 components for comparison
                    var kin = localResult.Kinematic2Snapshot;
                    var sec = localResult.Secondary2Snapshot;
                    if (kin != null && sec != null)
                    {
                        Console.Error.WriteLine(
                            $"C_VS232_KIN" +
                            $" HK2={BitConverter.SingleToInt32Bits((float)kin.HK2):X8}" +
                            $" RT2={BitConverter.SingleToInt32Bits((float)kin.RT2):X8}" +
                            $" CF2={BitConverter.SingleToInt32Bits((float)sec.Cf):X8}" +
                            $" DI2={BitConverter.SingleToInt32Bits((float)sec.Di):X8}");
                    }
                }
                // Trace BLVAR output at first Newton iter, station 2 side 1
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 1)
                {
                    Console.Error.WriteLine(
                        $"C_N{side+1}{ibl+1,3}" +
                        $" {BitConverter.SingleToInt32Bits((float)localResult.HK2):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)localResult.Residual[1]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)localResult.Residual[2]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)thi):X8}");
                }
                // Dump TRDIF VS2 row 1 at transition station, SETBL call 14
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 14 && tran)
                {
                    Console.Error.WriteLine(
                        $"C_TRDIF14 s={side+1} i={ibl+1}" +
                        $" V10={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 0]):X8}" +
                        $" V11={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 1]):X8}" +
                        $" V12={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 2]):X8}" +
                        $" V13={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 3]):X8}" +
                        $" V14={BitConverter.SingleToInt32Bits((float)(localResult.VS2.GetLength(1) > 4 ? localResult.VS2[0, 4] : 0)):X8}" +
                        $" R0={BitConverter.SingleToInt32Bits((float)localResult.Residual[0]):X8}");
                }
                // Per-station hash at SETBL call 2-18 to find divergent station
                if (DebugFlags.SetBlHex
                    && s_buildCallCount >= 1 && s_buildCallCount <= 18)
                {
                    unchecked {
                    int vsX = 0;
                    for (int rr = 0; rr < localResult.VS2.GetLength(0); rr++)
                        for (int cc = 0; cc < localResult.VS2.GetLength(1); cc++)
                            vsX ^= BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, cc]);
                    int vs1X = 0;
                    for (int rr = 0; rr < localResult.VS1.GetLength(0); rr++)
                        for (int cc = 0; cc < localResult.VS1.GetLength(1); cc++)
                            vs1X ^= BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, cc]);
                    int resX = 0;
                    for (int kk = 0; kk < 3; kk++)
                        resX ^= BitConverter.SingleToInt32Bits((float)localResult.Residual[kk]);
                    int vsXh = 0;
                    for (int kk = 0; kk < 3; kk++)
                        vsXh ^= BitConverter.SingleToInt32Bits((float)localResult.VSX[kk]);
                    Console.Error.WriteLine(
                        $"C_STN{s_buildCallCount:D2} s={side+1} i={ibl+1,3} VS2X={vsX:X8} VS1X={vs1X:X8} RESX={resX:X8} VSXX={vsXh:X8}");
                    }
                }
                if (Environment.GetEnvironmentVariable("XFOIL_S1210_VS") == "1"
                    && s_buildCallCount == 1 && side == 0 && ibl == 1)
                {
                    for (int rr = 0; rr < 3; rr++)
                    {
                        Console.Error.WriteLine(
                            $"C_S1210_VS2 r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 4]):X8}");
                        Console.Error.WriteLine(
                            $"C_S1210_VS1 r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 4]):X8}");
                    }
                    Console.Error.WriteLine(
                        $"C_S1210_VSX x0={BitConverter.SingleToInt32Bits((float)localResult.VSX[0]):X8}" +
                        $" x1={BitConverter.SingleToInt32Bits((float)localResult.VSX[1]):X8}" +
                        $" x2={BitConverter.SingleToInt32Bits((float)localResult.VSX[2]):X8}");
                    Console.Error.WriteLine(
                        $"C_S1210_VSREZ r0={BitConverter.SingleToInt32Bits((float)localResult.Residual[0]):X8}" +
                        $" r1={BitConverter.SingleToInt32Bits((float)localResult.Residual[1]):X8}" +
                        $" r2={BitConverter.SingleToInt32Bits((float)localResult.Residual[2]):X8}");
                }
                // NACA 0012 2M a=3: dump TRAN flag at call 2 side 2 station 65
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 2 && side == 1 && ibl == 64)
                {
                    Console.Error.WriteLine(
                        $"C_TRAN_STN65 tran={tran} turb={turb} ITRAN1={blState.ITRAN[1]}" +
                        $" IBLTE1={blState.IBLTE[1]}");
                }
                // ah79k135: dump TE station (side 2 IBL 67) at every iter for parity
                if (DebugFlags.SetBlHex && side == 1 && ibl == 66)
                {
                    for (int rr = 0; rr < 3; rr++)
                    {
                        Console.Error.WriteLine(
                            $"C_VSTE2 call={s_buildCallCount} r{rr}" +
                            $" VS1c0={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 0]):X8}" +
                            $" VS1c1={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 1]):X8}" +
                            $" VS1c2={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 2]):X8}" +
                            $" VS1c3={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 3]):X8}" +
                            $" VS1c4={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 4]):X8}" +
                            $" VS2c0={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 0]):X8}" +
                            $" VS2c1={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 1]):X8}" +
                            $" VS2c2={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 2]):X8}" +
                            $" VS2c3={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 3]):X8}" +
                            $" VS2c4={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 4]):X8}" +
                            $" VSX={BitConverter.SingleToInt32Bits((float)localResult.VSX[rr]):X8}" +
                            $" R={BitConverter.SingleToInt32Bits((float)localResult.Residual[rr]):X8}");
                    }
                }
                // NACA 0012 2M a=3: per-cell at call 2 side 2 station 66 (ibl=65 0-idx, post-trans)
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 2
                    && side == 1 && ibl == 65)
                {
                    for (int rr = 0; rr < 3; rr++)
                    {
                        Console.Error.WriteLine(
                            $"C_VS2_0212066 r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 4]):X8}" +
                            $" R={BitConverter.SingleToInt32Bits((float)localResult.Residual[rr]):X8}");
                    }
                }
                // NACA 0012 2M a=3: per-cell at call 2 side 2 station 65 (ibl=64 0-idx)
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 2
                    && side == 1 && ibl == 64)
                {
                    for (int rr = 0; rr < 3; rr++)
                    {
                        Console.Error.WriteLine(
                            $"C_VS2_0212065 r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 4]):X8}" +
                            $" R={BitConverter.SingleToInt32Bits((float)localResult.Residual[rr]):X8}");
                    }
                }
                // Full VS2/VS1 per-cell trace at call 3 for side 2 stations 70-75 (NACA 0009 5M debug)
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 3
                    && side == 1 && ibl >= 69 && ibl <= 74)
                {
                    for (int rr = 0; rr < 3; rr++)
                    {
                        Console.Error.WriteLine(
                            $"C_VS2_{s_buildCallCount:D2} s{side+1}i{ibl+1,3}r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS2[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)(localResult.VS2.GetLength(1) > 4 ? localResult.VS2[rr, 4] : 0)):X8}");
                        Console.Error.WriteLine(
                            $"C_VS1_{s_buildCallCount:D2} s{side+1}i{ibl+1,3}r{rr}" +
                            $" c0={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 0]):X8}" +
                            $" c1={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 1]):X8}" +
                            $" c2={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 2]):X8}" +
                            $" c3={BitConverter.SingleToInt32Bits((float)localResult.VS1[rr, 3]):X8}" +
                            $" c4={BitConverter.SingleToInt32Bits((float)(localResult.VS1.GetLength(1) > 4 ? localResult.VS1[rr, 4] : 0)):X8}");
                    }
                }

                // Trace VS1/VS2 at transition and wake stations
                if (DebugFlags.SetBlHex
                    && side == 1 && (ibl == 49 || ibl == 89))
                {
                    Console.Error.WriteLine(
                        $"C_VS s={side+1} i={ibl+1}" +
                        $" VS1_13={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 2]):X8}" +
                        $" VS1_14={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 3]):X8}" +
                        $" VS1_23={BitConverter.SingleToInt32Bits((float)localResult.VS1[1, 2]):X8}" +
                        $" VS1_24={BitConverter.SingleToInt32Bits((float)localResult.VS1[1, 3]):X8}" +
                        $" VS2_13={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 2]):X8}" +
                        $" VS2_14={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 3]):X8}");
                    if (setblFullPoint != null)
                    {
                        Console.Error.WriteLine(
                            $"C_TP50" +
                            $" XT_D1={BitConverter.SingleToInt32Bits((float)setblFullPoint.Xt1[2]):X8}");
                    }
                }
                // Hex trace for parity debugging (VSREZ comparison with Fortran F_VSREZ)
                if (DebugFlags.SetBlHex)
                {
                    float r1 = (float)localResult.Residual[0];
                    float r2 = (float)localResult.Residual[1];
                    float r3 = (float)localResult.Residual[2];
                    float fUei = (float)uei, fThi = (float)thi, fMdi = (float)mdi, fDsi = (float)dsi;
                    float fCti = (float)cti, fAmi = (float)ami;
                    Console.Error.WriteLine($"C_STN_c{s_buildCallCount:D2} IS={side + 1,2} IBL={ibl + 1,3} UEI={BitConverter.SingleToInt32Bits(fUei):X8} THI={BitConverter.SingleToInt32Bits(fThi):X8} MDI={BitConverter.SingleToInt32Bits(fMdi):X8} DSI={BitConverter.SingleToInt32Bits(fDsi):X8} CTI={BitConverter.SingleToInt32Bits(fCti):X8} AMI={BitConverter.SingleToInt32Bits(fAmi):X8}");
                    Console.Error.WriteLine($"C_VSREZ IS={side + 1,2} IBL={ibl + 1,4} R1={BitConverter.SingleToInt32Bits(r1):X8} R2={BitConverter.SingleToInt32Bits(r2):X8} R3={BitConverter.SingleToInt32Bits(r3):X8}");
                    if (side == 0 && ibl + 1 == 79)
                    {
                        float due2Trace = (float)LegacyPrecisionMath.Subtract(uei, usav[ibl, side], true);
                        Console.Error.WriteLine(
                            $"C_VS79R1" +
                            $" V114={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 3]):X8}" +
                            $" V113={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 2]):X8}" +
                            $" V214={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 3]):X8}" +
                            $" V213={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 2]):X8}" +
                            $" DUE2={BitConverter.SingleToInt32Bits(due2Trace):X8}" +
                            $" VSREZ1={BitConverter.SingleToInt32Bits(r1):X8}");
                    }
                }

                // Trace VS1 at transition station 5 side 2 call 3
                if (DebugFlags.SetBlHex
                    && side == 1 && ibl == 4 && s_buildCallCount == 3 && localResult?.VS1 != null)
                {
                    var vs1t = localResult.VS1;
                    for (int r = 0; r < 3; r++)
                        Console.Error.WriteLine(
                            $"C_VS1_5C3 r{r}:" +
                            $" {BitConverter.SingleToInt32Bits((float)vs1t[r,0]):X8}" +
                            $" {BitConverter.SingleToInt32Bits((float)vs1t[r,1]):X8}" +
                            $" {BitConverter.SingleToInt32Bits((float)vs1t[r,2]):X8}" +
                            $" {BitConverter.SingleToInt32Bits((float)vs1t[r,3]):X8}");
                }

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
                if (Environment.GetEnvironmentVariable("XFOIL_USAV_IT1") == "1"
                    && side == 0 && ibl >= 1 && ibl <= 4 && s_buildCallCount == 1)
                {
                    Console.Error.WriteLine(
                        $"C_USAV_SETBL s=1 ibl={ibl}" +
                        $" UINV={BitConverter.SingleToInt32Bits((float)ueInv[ibl, side]):X8}" +
                        $" USAV={BitConverter.SingleToInt32Bits((float)usav[ibl, side]):X8}" +
                        $" UEDG={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" MASS={BitConverter.SingleToInt32Bits((float)blState.MASS[ibl, side]):X8}" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}");
                }
                if (DebugFlags.SetBlHex
                    && side == 1 && s_buildCallCount == 2
                    && (ibl == 28 || ibl == 29))
                {
                    Console.Error.WriteLine(
                        $"C_DUE29 s=2 i={ibl + 1}" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                        $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" USAV={BitConverter.SingleToInt32Bits((float)usav[ibl, side]):X8}" +
                        $" MASS={BitConverter.SingleToInt32Bits((float)blState.MASS[ibl, side]):X8}");
                }
                if (DebugFlags.SetBlHex
                    && side == 0 && ibl == 1 && s_buildCallCount == 1)
                {
                    Console.Error.WriteLine(
                        $"C_DUE2_S1I2" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                        $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" USAV={BitConverter.SingleToInt32Bits((float)usav[ibl, side]):X8}" +
                        $" D2U2={BitConverter.SingleToInt32Bits((float)d2_u2):X8}" +
                        $" DDS2={BitConverter.SingleToInt32Bits((float)LegacyPrecisionMath.Multiply(d2_u2, due2, useLegacyPrecision)):X8}");
                }
                // Trace USAV/DUE2 at station 23 side 2 call 4
                if (DebugFlags.SetBlHex
                    && side == 1 && ibl == 22 && s_buildCallCount == 4)
                {
                    Console.Error.WriteLine(
                        $"C_SETBL23" +
                        $" THI={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                        $" DSI={BitConverter.SingleToInt32Bits((float)dsi):X8}" +
                        $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" D1={BitConverter.SingleToInt32Bits((float)d1):X8}" +
                        $" MDI_d={BitConverter.DoubleToInt64Bits(mdi):X16}" +
                        $" UEI_d={BitConverter.DoubleToInt64Bits(uei):X16}");
                }
                // Trace USAV/DUE2 at station 16 side 2
                if (DebugFlags.SetBlHex
                    && side == 1 && ibl == 15 && s_buildCallCount >= 3 && s_buildCallCount <= 5)
                {
                    Console.Error.WriteLine(
                        $"C_USAV16 call={s_buildCallCount}" +
                        $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" USAV={BitConverter.SingleToInt32Bits((float)usav[ibl, side]):X8}" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                        $" DSI={BitConverter.SingleToInt32Bits((float)dsi):X8}");
                }
                double dds2 = LegacyPrecisionMath.Multiply(d2_u2, due2, useLegacyPrecision);

                // GDB parity hex dump of station state
                if (DebugFlags.SetBlHex)
                {
                    Console.Error.WriteLine(
                        $"C_STN IS={side + 1,2} IBL={ibl + 1,3}" +
                        $" UEI={BitConverter.SingleToInt32Bits((float)uei):X8}" +
                        $" THI={BitConverter.SingleToInt32Bits((float)thi):X8}" +
                        $" MDI={BitConverter.SingleToInt32Bits((float)mdi):X8}" +
                        $" DSI={BitConverter.SingleToInt32Bits((float)dsi):X8}" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                        $" DDS2={BitConverter.SingleToInt32Bits((float)dds2):X8}");
                }

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
                    if (useLegacyPrecision)
                    {
                        // Fortran SETBL: DDS1 = DTE_UTE1*(UEDG(IBLTE1)-USAV(IBLTE1))
                        //                     + DTE_UTE2*(UEDG(IBLTE2)-USAV(IBLTE2))
                        // ALL REAL per-operation arithmetic.
                        float term1 = (float)dteUte1 * ((float)uete1 - (float)usavTe1);
                        float term2 = (float)dteUte2 * ((float)uete2 - (float)usavTe2);
                        dds1 = term1 + term2;
                    }
                    else
                    {
                        dds1 = dteUte1 * (uete1 - usavTe1) + dteUte2 * (uete2 - usavTe2);
                    }
                }

                double xiUle1 = (side == 0) ? sstGo : -sstGo;
                double xiUle2 = (side == 0) ? -sstGp : sstGp;
                // Fortran: XIFORC = XIFORC1*DULE1 + XIFORC2*DULE2 — REAL per-op
                double xiForcing = LegacyPrecisionMath.Add(
                    LegacyPrecisionMath.Multiply(xiUle1, dule1, useLegacyPrecision),
                    LegacyPrecisionMath.Multiply(xiUle2, dule2, useLegacyPrecision),
                    useLegacyPrecision);

                if (Environment.GetEnvironmentVariable("XFOIL_DUMP_RESID_IT0") == "1"
                    && s_buildCallCount == 1 && ibl <= 2)
                {
                    Console.Error.WriteLine(
                        $"C_FORCE_IT0 s={side+1} i={ibl+1,4}" +
                        $" DUE1={BitConverter.SingleToInt32Bits((float)due1):X8}" +
                        $" DUE2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                        $" DDS1={BitConverter.SingleToInt32Bits((float)dds1):X8}" +
                        $" DDS2={BitConverter.SingleToInt32Bits((float)dds2):X8}" +
                        $" XIFORC={BitConverter.SingleToInt32Bits((float)xiForcing):X8}" +
                        $" DULE1={BitConverter.SingleToInt32Bits((float)dule1):X8}" +
                        $" DULE2={BitConverter.SingleToInt32Bits((float)dule2):X8}" +
                        $" xiULE1={BitConverter.SingleToInt32Bits((float)xiUle1):X8}" +
                        $" xiULE2={BitConverter.SingleToInt32Bits((float)xiUle2):X8}");
                }


                // Legacy block: xbl.f SETBL residual load into VDEL.
                // Difference from legacy: The residual forcing terms are written with named `due/dds/xi` components rather than inline array algebra, but the resulting row construction follows the same VS1/VS2 coupling as SETBL.
                // Decision: Keep the explicit decomposition because it makes the coupling auditable while preserving the legacy row assembly order.
                // Residuals go into VDEL -- indexed by iv
                // Include VS1/VS2 DUE/DDS forced-change terms matching Fortran SETBL
                // C# VS column layout: 0=Ctau/Ampl, 1=Theta, 2=D*, 3=Ue, 4=x
                // Fortran 1-based: col 3=D*, col 4=Ue => C# col 2=D*, col 3=Ue
                for (int k = 0; k < 3; k++)
                {
                    if (useLegacyPrecision)
                    {
                        // Fortran SETBL: VSREZ(K) = VSREZ(K) + (VS1(K,4)*DUE1 + VS1(K,3)*DDS1
                        //                                      + VS2(K,4)*DUE2 + VS2(K,3)*DDS2)
                        // ALL REAL per-operation arithmetic.
                        float res = (float)localResult.Residual[k];
                        float vs1Due = (float)localResult.VS1[k, 3] * (float)due1;
                        float vs1Dds = (float)localResult.VS1[k, 2] * (float)dds1;
                        float vs2Due = (float)localResult.VS2[k, 3] * (float)due2;
                        float vs2Dds = (float)localResult.VS2[k, 2] * (float)dds2;
                        // Fortran: (VS1(K,5) + VS2(K,5) + VSX(K)) * XI_FORCING
                        float xiCoeff = (float)localResult.VS1[k, 4] + (float)localResult.VS2[k, 4] + (float)localResult.VSX[k];
                        float xiTerm = xiCoeff * (float)xiForcing;
                        // Fortran parenthesization:
                        //   VSREZ + (VS1_DUE + VS1_DDS) + (VS2_DUE + VS2_DDS) + xiTerm
                        // The paired grouping matters because float is not associative.
                        float duePair1 = vs1Due + vs1Dds; // (VS1(K,4)*DUE1 + VS1(K,3)*DDS1)
                        float duePair2 = vs2Due + vs2Dds; // (VS2(K,4)*DUE2 + VS2(K,3)*DDS2)
                        float acc = res + duePair1;
                        acc += duePair2;
                        acc += xiTerm;
                        vdel[k, 0, iv] = acc;
                        if (Environment.GetEnvironmentVariable("XFOIL_VDASM_IT1") == "1"
                            && side == 0 && ibl == 1)
                        {
                            Console.Error.WriteLine(
                                $"C_VDASM_IT1 ibl={ibl} k={k} RES={BitConverter.SingleToInt32Bits(res):X8} V1D={BitConverter.SingleToInt32Bits(vs1Due):X8} V1S={BitConverter.SingleToInt32Bits(vs1Dds):X8} V2D={BitConverter.SingleToInt32Bits(vs2Due):X8} V2S={BitConverter.SingleToInt32Bits(vs2Dds):X8} XI={BitConverter.SingleToInt32Bits(xiTerm):X8} DU1={BitConverter.SingleToInt32Bits((float)due1):X8} DU2={BitConverter.SingleToInt32Bits((float)due2):X8} DD1={BitConverter.SingleToInt32Bits((float)dds1):X8} DD2={BitConverter.SingleToInt32Bits((float)dds2):X8}");
                        }
                        // Per-term VDEL assembly trace at station 5 side 2 (k=0 only)
                        if (DebugFlags.SetBlHex
                            && side == 1 && ibl == 4 && k == 0)
                        {
                            Console.Error.WriteLine(
                                $"C_VDASM call={s_buildCallCount}" +
                                $" RES={BitConverter.SingleToInt32Bits(res):X8}" +
                                $" V1D={BitConverter.SingleToInt32Bits(vs1Due):X8}" +
                                $" V1S={BitConverter.SingleToInt32Bits(vs1Dds):X8}" +
                                $" V2D={BitConverter.SingleToInt32Bits(vs2Due):X8}" +
                                $" V2S={BitConverter.SingleToInt32Bits(vs2Dds):X8}" +
                                $" XI={BitConverter.SingleToInt32Bits(xiTerm):X8}" +
                                $" P1={BitConverter.SingleToInt32Bits(duePair1):X8}" +
                                $" S13={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 2]):X8}" +
                                $" S14={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 3]):X8}" +
                                $" S23={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 2]):X8}" +
                                $" S24={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 3]):X8}" +
                                $" DD1={BitConverter.SingleToInt32Bits((float)dds1):X8}" +
                                $" DD2={BitConverter.SingleToInt32Bits((float)dds2):X8}" +
                                $" DU1={BitConverter.SingleToInt32Bits((float)due1):X8}" +
                                $" DU2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                                $" P2={BitConverter.SingleToInt32Bits(duePair2):X8}" +
                                $" ACC={BitConverter.SingleToInt32Bits(acc):X8}");
                        }
                        if (DebugFlags.SetBlHex
                            && side == 0 && ibl == 1 && k == 1)
                        {
                            Console.Error.WriteLine(
                                $"C_VDASM2 call={s_buildCallCount}" +
                                $" RES={BitConverter.SingleToInt32Bits(res):X8}" +
                                $" V1D={BitConverter.SingleToInt32Bits(vs1Due):X8}" +
                                $" V1S={BitConverter.SingleToInt32Bits(vs1Dds):X8}" +
                                $" V2D={BitConverter.SingleToInt32Bits(vs2Due):X8}" +
                                $" V2S={BitConverter.SingleToInt32Bits(vs2Dds):X8}" +
                                $" XI={BitConverter.SingleToInt32Bits(xiTerm):X8}" +
                                $" P1={BitConverter.SingleToInt32Bits(duePair1):X8}" +
                                $" P2={BitConverter.SingleToInt32Bits(duePair2):X8}" +
                                $" ACC={BitConverter.SingleToInt32Bits(acc):X8}" +
                                $" S13={BitConverter.SingleToInt32Bits((float)localResult.VS1[1, 2]):X8}" +
                                $" S14={BitConverter.SingleToInt32Bits((float)localResult.VS1[1, 3]):X8}" +
                                $" S15={BitConverter.SingleToInt32Bits((float)localResult.VS1[1, 4]):X8}" +
                                $" S23={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 2]):X8}" +
                                $" S24={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 3]):X8}" +
                                $" S25={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 4]):X8}" +
                                $" VSX={BitConverter.SingleToInt32Bits((float)localResult.VSX[1]):X8}" +
                                $" DD1={BitConverter.SingleToInt32Bits((float)dds1):X8}" +
                                $" DD2={BitConverter.SingleToInt32Bits((float)dds2):X8}" +
                                $" DU1={BitConverter.SingleToInt32Bits((float)due1):X8}" +
                                $" DU2={BitConverter.SingleToInt32Bits((float)due2):X8}" +
                                $" XIF={BitConverter.SingleToInt32Bits((float)xiForcing):X8}");
                        }
                    }
                    else
                    {
                    double vs1DueTerm = localResult.VS1[k, 3] * due1;
                    double vs1DdsTerm = localResult.VS1[k, 2] * dds1;
                    double vs2DueTerm = localResult.VS2[k, 3] * due2;
                    double vs2DdsTerm = localResult.VS2[k, 2] * dds2;
                    double xiCoeffBase = localResult.VS1[k, 4] + localResult.VS2[k, 4] + localResult.VSX[k];
                    double xiBaseTerm = xiCoeffBase * xiForcing;
                    vdel[k, 0, iv] = localResult.Residual[k]
                                   + vs1DueTerm
                                   + vs1DdsTerm
                                   + vs2DueTerm
                                   + vs2DdsTerm
                                   + xiBaseTerm;
                    }
                    vdel[k, 1, iv] = 0.0;

                }

                // Diagnostic dump: station BL state, VA/VB blocks, VDEL residuals
                // GDB parity hex dump of raw VSREZ and VDEL
                if (DebugFlags.SetBlHex)
                {
                    // Trace VS1+VS2 at station 160 (iv=159) at iteration 14
                    if (iv == 159 && s_buildCallCount == 14 && localResult.VS1 != null)
                    {
                        Console.Error.WriteLine(
                            $"C_VS1_160" +
                            $" v13={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 2]):X8}" +
                            $" v14={BitConverter.SingleToInt32Bits((float)localResult.VS1[0, 3]):X8}" +
                            $" v15={BitConverter.SingleToInt32Bits((float)(localResult.VS1.GetLength(1) > 4 ? localResult.VS1[0, 4] : 0)):X8}" +
                            $" s25={BitConverter.SingleToInt32Bits((float)(localResult.VS2.GetLength(1) > 4 ? localResult.VS2[0, 4] : 0)):X8}" +
                            $" vsx={BitConverter.SingleToInt32Bits((float)localResult.VSX[0]):X8}");
                        Console.Error.WriteLine(
                            $"C_VS2_160" +
                            $" v21={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 0]):X8}" +
                            $" v22={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 1]):X8}" +
                            $" v23={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 2]):X8}" +
                            $" v24={BitConverter.SingleToInt32Bits((float)localResult.VS2[0, 3]):X8}" +
                            $" v31={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 0]):X8}" +
                            $" v32={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 1]):X8}" +
                            $" v33={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 2]):X8}" +
                            $" v34={BitConverter.SingleToInt32Bits((float)localResult.VS2[1, 3]):X8}" +
                            $" v41={BitConverter.SingleToInt32Bits((float)localResult.VS2[2, 0]):X8}" +
                            $" v42={BitConverter.SingleToInt32Bits((float)localResult.VS2[2, 1]):X8}" +
                            $" v43={BitConverter.SingleToInt32Bits((float)localResult.VS2[2, 2]):X8}" +
                            $" v44={BitConverter.SingleToInt32Bits((float)localResult.VS2[2, 3]):X8}");
                    }
                    Console.Error.WriteLine(
                        $"C_VSREZ IS={side + 1,2} IBL={ibl + 1,3}" +
                        $" R1={BitConverter.SingleToInt32Bits((float)localResult.Residual[0]):X8}" +
                        $" R2={BitConverter.SingleToInt32Bits((float)localResult.Residual[1]):X8}" +
                        $" R3={BitConverter.SingleToInt32Bits((float)localResult.Residual[2]):X8}");
                    Console.Error.WriteLine(
                        $"C_VDEL  IS={side + 1,2} IBL={ibl + 1,3}" +
                        $" V1={BitConverter.SingleToInt32Bits((float)vdel[0, 0, iv]):X8}" +
                        $" V2={BitConverter.SingleToInt32Bits((float)vdel[1, 0, iv]):X8}" +
                        $" V3={BitConverter.SingleToInt32Bits((float)vdel[2, 0, iv]):X8}");
                }
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
                        if (useLegacyPrecision)
                        {
                            // Fortran computes U2_M and D2_M in REAL (float)
                            float u2f = (float)(-(float)vtiI * (float)vtiJ * (float)dij[iPanCur, jPan]);
                            u2_mj = u2f;
                            float d2f = (float)((float)d2_u2 * u2f);
                            if (jv == iv) d2f = (float)(d2f + (float)d2_m2);
                            d2_mj_local = d2f;
                            // removed debug trace
                        }
                        else
                        {
                            u2_mj = -vtiI * vtiJ * dij[iPanCur, jPan];
                            d2_mj_local = d2_u2 * u2_mj;
                            if (jv == iv) d2_mj_local += d2_m2;
                        }
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
                                ? (float)(-(float)vtiTe1 * (float)vtiJ * (float)dij[ite1Pan, jPan])
                                : -vtiTe1 * vtiJ * dij[ite1Pan, jPan];
                        }
                        if (ite2Pan >= 0 && ite2Pan < dij.GetLength(0))
                        {
                            double vtiTe2 = GetVTI(blState.IBLTE[1], 1, blState);
                            ute2_mj = useLegacyPrecision
                                ? (float)(-(float)vtiTe2 * (float)vtiJ * (float)dij[ite2Pan, jPan])
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
                        if (useLegacyPrecision)
                        {
                            float u1f = (float)(-(float)vtiPrev * (float)vtiJ * (float)dij[iPanPrev, jPan]);
                            u1_mj = u1f;
                            float d1f = (float)((float)d1_u1 * u1f);
                            if (jv == ivPrev) d1f = (float)(d1f + (float)d1_m2Prev);
                            d1_mj = d1f;
                        }
                        else
                        {
                            u1_mj = -vtiPrev * vtiJ * dij[iPanPrev, jPan];
                            d1_mj = d1_u1 * u1_mj;
                            if (jv == ivPrev) d1_mj += d1_m2Prev;
                        }
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
                            ? (float)(-(float)vtiLe1 * (float)vtiJ * (float)dij[ile1, jPan])
                            : -vtiLe1 * vtiJ * dij[ile1, jPan];
                    }
                    if (ile2 >= 0 && ile2 < dij.GetLength(0))
                    {
                        double vtiLe2 = GetVTI(1, 1, blState);
                        ule2_mj = useLegacyPrecision
                            ? (float)(-(float)vtiLe2 * (float)vtiJ * (float)dij[ile2, jPan])
                            : -vtiLe2 * vtiJ * dij[ile2, jPan];
                    }

                    // Trace VM_40 diagonal for debugging
                    if (DebugFlags.ParityTrace
                        && s_buildCallCount == 1 && iv == 39 && jv == 39)
                    {
                        Console.Error.WriteLine(
                            $"CS_VM40_DETAIL d1_mj={d1_mj:E6} u1_mj={u1_mj:E6}" +
                            $" d2_mj={d2_mj_local:E6} u2_mj={u2_mj:E6}" +
                            $" d2_u2={d2_u2:E6} d2_m2={d2_m2:E6}" +
                            $" d1_u1={d1_u1:E6} d1_m2Prev={d1_m2Prev:E6}" +
                            $" iPanPrev={iPanPrev} iPanCur={iPanCur} wake={wake}" +
                            $" VS1_32={localResult.VS1[2, 2]:E6} VS1_33={localResult.VS1[2, 3]:E6}" +
                            $" VS2_32={localResult.VS2[2, 2]:E6} VS2_33={localResult.VS2[2, 3]:E6}");
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        // Stagnation coupling (XI_ULE terms)
                        double xiUle1Vm = (side == 0) ? sstGo : -sstGo;
                        double xiUle2Vm = (side == 0) ? -sstGp : sstGp;

                        if (useLegacyPrecision)
                        {
                            // Fortran: VM(k,JV,IV) = VS1*D1_M + VS1*U1_M + VS2*D2_M + VS2*U2_M
                            //   + (VS1(k,5)+VS2(k,5)+VSX(k)) * (XI_ULE1*ULE1_M + XI_ULE2*ULE2_M)
                            // ALL REAL arithmetic — compute xiTerm in float
                            float vs1d = (float)localResult.VS1[k, 2];
                            float vs1u = (float)localResult.VS1[k, 3];
                            float vs2d = (float)localResult.VS2[k, 2];
                            float vs2u = (float)localResult.VS2[k, 3];
                            // Trace VM inputs at iv=76 k=2 jv=0 iter 5 (case 188 NACA 0009 side 1 station 78 row 3 jv=1)
                            if (DebugFlags.SetBlHex
                                && iv == 76 && k == 2 && jv == 0 && s_buildCallCount == 5)
                            {
                                Console.Error.WriteLine(
                                    $"C_VMIN77 iv={iv + 1} k={k + 1} jv={jv + 1}" +
                                    $" vs1d={BitConverter.SingleToInt32Bits(vs1d):X8}" +
                                    $" vs1u={BitConverter.SingleToInt32Bits(vs1u):X8}" +
                                    $" vs2d={BitConverter.SingleToInt32Bits(vs2d):X8}" +
                                    $" vs2u={BitConverter.SingleToInt32Bits(vs2u):X8}" +
                                    $" vs15={BitConverter.SingleToInt32Bits((float)localResult.VS1[k, 4]):X8}" +
                                    $" vs25={BitConverter.SingleToInt32Bits((float)localResult.VS2[k, 4]):X8}" +
                                    $" vsx={BitConverter.SingleToInt32Bits((float)localResult.VSX[k]):X8}" +
                                    $" d1mj={BitConverter.SingleToInt32Bits((float)d1_mj):X8}" +
                                    $" u1mj={BitConverter.SingleToInt32Bits((float)u1_mj):X8}" +
                                    $" d2mj={BitConverter.SingleToInt32Bits((float)d2_mj_local):X8}" +
                                    $" u2mj={BitConverter.SingleToInt32Bits((float)u2_mj):X8}" +
                                    $" ule1={BitConverter.SingleToInt32Bits((float)ule1_mj):X8}" +
                                    $" ule2={BitConverter.SingleToInt32Bits((float)ule2_mj):X8}" +
                                    $" xu1={BitConverter.SingleToInt32Bits((float)xiUle1Vm):X8}" +
                                    $" xu2={BitConverter.SingleToInt32Bits((float)xiUle2Vm):X8}");
                            }
                            // Fortran: (VS1(K,5) + VS2(K,5) + VSX(K))
                            // Must match Fortran evaluation order: (VS1+VS2)+VSX
                            // Use RoundBarrier guards to prevent JIT FMA fusion
                            float xiCoeffF = 0.0f;
                            if (localResult.VS1.GetLength(1) > 4)
                                xiCoeffF = (float)localResult.VS1[k, 4];
                            if (localResult.VS2.GetLength(1) > 4)
                                xiCoeffF = LegacyPrecisionMath.RoundBarrier(
                                    LegacyPrecisionMath.RoundBarrier(xiCoeffF)
                                    + (float)localResult.VS2[k, 4]);
                            xiCoeffF = LegacyPrecisionMath.RoundBarrier(
                                LegacyPrecisionMath.RoundBarrier(xiCoeffF)
                                + (float)localResult.VSX[k]);
                            // xiTermF = xiCoeffF * (xiUle1Vm*ule1_mj + xiUle2Vm*ule2_mj)
                            // Fortran evaluates the inner sum left-to-right: (a*b) + (c*d)
                            float ule1Prod = LegacyPrecisionMath.RoundBarrier(
                                (float)xiUle1Vm * (float)ule1_mj);
                            float ule2Prod = LegacyPrecisionMath.RoundBarrier(
                                (float)xiUle2Vm * (float)ule2_mj);
                            float uleSum = LegacyPrecisionMath.RoundBarrier(ule1Prod + ule2Prod);
                            float xiTermF = LegacyPrecisionMath.RoundBarrier(xiCoeffF * uleSum);
                            // vmVal = vs1d*d1_mj + vs1u*u1_mj + vs2d*d2_mj + vs2u*u2_mj + xiTermF
                            // Fortran evaluates left-to-right with each product+add in REAL*4.
                            // Use sequential accumulation with RoundBarrier to match.
                            float p1 = LegacyPrecisionMath.RoundBarrier(vs1d * (float)d1_mj);
                            float p2 = LegacyPrecisionMath.RoundBarrier(vs1u * (float)u1_mj);
                            float p3 = LegacyPrecisionMath.RoundBarrier(vs2d * (float)d2_mj_local);
                            float p4 = LegacyPrecisionMath.RoundBarrier(vs2u * (float)u2_mj);
                            float acc = LegacyPrecisionMath.RoundBarrier(p1 + p2);
                            acc = LegacyPrecisionMath.RoundBarrier(acc + p3);
                            acc = LegacyPrecisionMath.RoundBarrier(acc + p4);
                            float vmVal = LegacyPrecisionMath.RoundBarrier(acc + xiTermF);
                            vm[k, jv, iv] = vmVal;
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount == 14 && iv == 159 && k == 0
                                && (jv == 157 || jv == 158 || jv == 159))
                            {
                                Console.Error.WriteLine(
                                    $"C_VMA jv={jv + 1,4}" +
                                    $" vm={BitConverter.SingleToInt32Bits(vmVal):X8}" +
                                    $" s1d={BitConverter.SingleToInt32Bits(vs1d):X8}" +
                                    $" s2d={BitConverter.SingleToInt32Bits(vs2d):X8}" +
                                    $" s2u={BitConverter.SingleToInt32Bits(vs2u):X8}" +
                                    $" d2m={BitConverter.SingleToInt32Bits((float)d2_mj_local):X8}" +
                                    $" u2m={BitConverter.SingleToInt32Bits((float)u2_mj):X8}" +
                                    $" xi={BitConverter.SingleToInt32Bits(xiTermF):X8}" +
                                    $" xic={BitConverter.SingleToInt32Bits(xiCoeffF):X8}");
                            }
                        }
                        else
                        {
                            double xiCoeff = localResult.VSX[k];
                            if (localResult.VS1.GetLength(1) > 4)
                                xiCoeff += localResult.VS1[k, 4];
                            if (localResult.VS2.GetLength(1) > 4)
                                xiCoeff += localResult.VS2[k, 4];
                            double xiTerm = xiCoeff * (xiUle1Vm * ule1_mj + xiUle2Vm * ule2_mj);
                            vm[k, jv, iv] = localResult.VS1[k, 2] * d1_mj
                                          + localResult.VS1[k, 3] * u1_mj
                                          + localResult.VS2[k, 2] * d2_mj_local
                                          + localResult.VS2[k, 3] * u2_mj
                                          + xiTerm;
                        }
                    }
                }

                // GDB: dump VM and panel indices at IV=0
                if (DebugFlags.SetBlHex && iv == 0 && ibl == 1 && side == 0)
                {
                    // Dump first 3 VM elements and ISYS mapping
                    int iPanHere = blState.IPAN[ibl, side];
                    int jv0_ibl = newtonSystem.ISYS[0, 0];
                    int jv0_side = newtonSystem.ISYS[0, 1];
                    int jv0_pan = blState.IPAN[jv0_ibl, jv0_side];
                    Console.Error.WriteLine(
                        $"C_VM_DBG IV=0 iPan={iPanHere}" +
                        $" JV0_ibl={jv0_ibl} JV0_side={jv0_side} JV0_pan={jv0_pan}" +
                        $" VM10={BitConverter.SingleToInt32Bits((float)vm[1, 0, 0]):X8}" +
                        $" nsys={newtonSystem.NSYS}");
                }

                // Trace VM band entries at station 160 (iv=159), iteration 14
                if (DebugFlags.SetBlHex
                    && s_buildCallCount == 14 && iv == 159)
                {
                    // Dump VM[0, jv, 159] for jv around the band
                    for (int jvDbg = Math.Max(0, iv - 3); jvDbg <= Math.Min(newtonSystem.NSYS - 1, iv + 3); jvDbg++)
                    {
                        Console.Error.WriteLine(
                            $"C_VM160 jv={jvDbg + 1,4}" +
                            $" r0={BitConverter.SingleToInt32Bits((float)vm[0, jvDbg, iv]):X8}" +
                            $" r1={BitConverter.SingleToInt32Bits((float)vm[1, jvDbg, iv]):X8}" +
                            $" r2={BitConverter.SingleToInt32Bits((float)vm[2, jvDbg, iv]):X8}");
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

        if (useLegacyPrecision)
        {
            blState.LegacySetblLaminarShearCarry = legacySetblLaminarShearCarry;
        }

        // Compute RMS
        if (nResiduals > 0)
            rmsbl = LegacyPrecisionMath.Sqrt(LegacyPrecisionMath.Divide(rmsbl, nResiduals, useLegacyPrecision), useLegacyPrecision);

        // Matrix hash for parity debugging (VA/VB/VM/VDEL)
        if (DebugFlags.SetBlHex)
        {
            uint vmHash = 0, vaHash = 0, vbHash = 0, vdelHash = 0;
            for (int iv = 0; iv < nsys; iv++)
            {
                for (int k = 0; k < 3; k++)
                {
                    for (int j = 0; j < nsys; j++)
                        vmHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)vm[k, j, iv]));
                    vaHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)va[k, 0, iv]));
                    vaHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)va[k, 1, iv]));
                    vbHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)vb[k, 0, iv]));
                    vbHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)vb[k, 1, iv]));
                    vdelHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)vdel[k, 0, iv]));
                    vdelHash ^= unchecked((uint)BitConverter.SingleToInt32Bits((float)vdel[k, 1, iv]));
                }
            }
            Console.Error.WriteLine($"C_MATRIX_HASH call={s_buildCallCount} VM={vmHash:X8} VA={vaHash:X8} VB={vbHash:X8} VDEL={vdelHash:X8}");
            // Per-station VB dump at call 3 to find divergent station
            if (s_buildCallCount == 3)
            {
                for (int iv = 0; iv < nsys; iv++)
                {
                    Console.Error.WriteLine(
                        $"C_VB3 iv={iv,3}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[0,0,iv]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[0,1,iv]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[1,0,iv]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[1,1,iv]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[2,0,iv]):X8}" +
                        $" {BitConverter.SingleToInt32Bits((float)vb[2,1,iv]):X8}");
                }
            }
        }


        // Per-IV VM row hash AFTER full assembly at buildCall=2
        if (DebugFlags.SetBlHex
            && s_buildCallCount == 2)
        {
            for (int ivH = 0; ivH < nsys; ivH++)
            {
                unchecked {
                uint rowHash = 0;
                for (int k3 = 0; k3 < 3; k3++)
                    for (int jv3 = 0; jv3 < nsys; jv3++)
                        rowHash += (uint)(BitConverter.SingleToInt32Bits((float)vm[k3, jv3, ivH]) & 0x7FFFFFFF);
                uint vdHash = 0;
                for (int k3 = 0; k3 < 3; k3++)
                    vdHash += (uint)(BitConverter.SingleToInt32Bits((float)vdel[k3, 0, ivH]) & 0x7FFFFFFF);
                Console.Error.WriteLine($"C_VMROW2 iv={ivH + 1,4} VM={rowHash:X8} VD={vdHash:X8}");
                }
            }
        }
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
                            if (Environment.GetEnvironmentVariable("XFOIL_DUI2_IT1") == "1"
                                && side == 0 && ibl == 1 && s_buildCallCount == 1)
                            {
                                Console.Error.WriteLine($"C_DUI2 JS={jSide+1} JBL={jbl+1,3} I={iPan+1,3} J={jPan+1,3} MASS={BitConverter.SingleToInt32Bits((float)massValue):X8} UEM={BitConverter.SingleToInt32Bits((float)ueM):X8} DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }

                            // DUI detail at station 29 side 2, JBL=104-105
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount == 2
                                && side == 1 && ibl == 28
                                && jSide == 1
                                && (jbl == 103 || jbl == 104))
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI29D JBL={jbl + 1,3}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}" +
                                    $" M={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" C={BitConverter.SingleToInt32Bits((float)contribution):X8}");
                            }
                            // DUI at station 53: every term from JBL 95+ on side 2
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount == 2
                                && side == 0 && ibl == 52
                                && jSide == 1 && jbl >= 80 && jbl <= 86)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI53 JS={jSide + 1} JBL={jbl + 1,3}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}");
                            }
                            // Per-term DUI at station 2 (ibl=1 side=0) for 1st SETBL call
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount == 1
                                && side == 0 && ibl == 1)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI2A JS={jSide + 1} JBL={jbl + 1,3}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            // Per-term DUI for station 9 (C# side=0 ibl=8, Fortran IS=1 IBL=9)
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount <= 1
                                && side == 0 && ibl == 8)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI2 JS={jSide + 1,2} JBL={jbl + 1,3}" +
                                    $" I={iPan + 1,4} J={jPan + 1,4}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            // DEBUG: Find NaN-producing MASS at iter 1 / build call 2
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount == 2
                                && side == 0 && ibl == 1
                                && !double.IsFinite(massValue))
                            {
                                Console.Error.WriteLine(
                                    $"C_MASS_NAN JS={jSide + 1} JBL={jbl + 1,3}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" DSTR={BitConverter.SingleToInt32Bits((float)blState.DSTR[jbl, jSide]):X8}" +
                                    $" UEDG={BitConverter.SingleToInt32Bits((float)blState.UEDG[jbl, jSide]):X8}");
                            }
                    // Per-term DUI for station 2 (C# side=0 ibl=1, Fortran IS=1 IBL=2) — similarity station parity
                            if (DebugFlags.SetBlHex
                                && s_buildCallCount <= 1
                                && side == 0 && ibl == 1)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI2ACC JS={jSide + 1} JBL={jbl + 1,3}" +
                                    $" I={iPan + 1,4} J={jPan + 1,4}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            // Per-term DUI for station 88 (C# side=1 ibl=87, Fortran IS=2 IBL=88)
                            if (DebugFlags.SetBlHex
                                && side == 1 && ibl == 87)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI88 JS={jSide + 1,2} JBL={jbl + 1,3}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            // Trace term 3 (JBL=5) for parity debugging
                            if (DebugFlags.SetBlHex
                                && side == 0 && ibl == 2 && jSide == 0 && jbl == 4)
                            {
                                Console.Error.WriteLine(
                                    $"C_TERM3" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" DUI_BEFORE={BitConverter.SingleToInt32Bits((float)(dui - contribution)):X8}" +
                                    $" DIJ={BitConverter.SingleToInt32Bits((float)dijValue):X8}" +
                                    $" PROD={BitConverter.SingleToInt32Bits((float)contribution):X8}" +
                                    $" DUI_AFTER={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            // Trace MASS at key stations during station 6 DUI sum
                            if (DebugFlags.SetBlHex
                                && side == 0 && ibl == 5 && jSide == 1 && jbl >= 98 && jbl <= 102)
                            {
                                Console.Error.WriteLine(
                                    $"C_M81_1" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEDG={BitConverter.SingleToInt32Bits((float)blState.UEDG[jbl, jSide]):X8}" +
                                    $" DSTR={BitConverter.SingleToInt32Bits((float)blState.DSTR[jbl, jSide]):X8}");
                            }
                            // GDB per-term DUI trace for station 6 (C# ibl=5)
                            if (DebugFlags.SetBlHex
                                && side == 0 && ibl == 5)
                            {
                                Console.Error.WriteLine(
                                    $"C_DUI_T JS={jSide + 1,2} JBL={jbl + 1,3}" +
                                    $" I={iPan + 1,4} J={jPan + 1,4}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                                if (jSide == 1 && (jbl + 1 == 81 || (jbl + 1 >= 43 && jbl + 1 <= 46)))
                                {
                                    Console.Error.WriteLine(
                                        $"C_DUI_D JBL={jbl + 1,3}" +
                                        $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                        $" DIJ={BitConverter.SingleToInt32Bits((float)dijValue):X8}" +
                                        $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                        $" CONT={BitConverter.SingleToInt32Bits((float)contribution):X8}");
                                    if (jbl + 1 == 81)
                                    {
                                        Console.Error.WriteLine(
                                            $"C_WK81 DSTR={BitConverter.SingleToInt32Bits((float)blState.DSTR[jbl, jSide]):X8}" +
                                            $" UEI={BitConverter.SingleToInt32Bits((float)blState.UEDG[jbl, jSide]):X8}");
                                        }
                                }
                            }
                            if (DebugFlags.SetBlHex
                                && side == 1 && ibl == 1)
                            {
                                float contributionBefore = (float)((float)dui - (float)contribution);
                                Console.Error.WriteLine(
                                    $"C_DUI22 JS={jSide + 1,2} JBL={jbl + 1,3}" +
                                    $" I={iPan + 1,4} J={jPan + 1,4}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" CONT={BitConverter.SingleToInt32Bits((float)contribution):X8}" +
                                    $" DUI_B={BitConverter.SingleToInt32Bits(contributionBefore):X8}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }
                            if (DebugFlags.SetBlHex
                                && side == 1 && ibl == 2)
                            {
                                float contributionBefore = (float)((float)dui - (float)contribution);
                                Console.Error.WriteLine(
                                    $"C_DUI23 JS={jSide + 1,2} JBL={jbl + 1,3}" +
                                    $" I={iPan + 1,4} J={jPan + 1,4}" +
                                    $" MASS={BitConverter.SingleToInt32Bits((float)massValue):X8}" +
                                    $" UEM={BitConverter.SingleToInt32Bits((float)ueM):X8}" +
                                    $" CONT={BitConverter.SingleToInt32Bits((float)contribution):X8}" +
                                    $" DUI_B={BitConverter.SingleToInt32Bits(contributionBefore):X8}" +
                                    $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}");
                            }

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
                if (DebugFlags.SetBlHex)
                {
                    Console.Error.WriteLine(
                        $"C_UESET IS={side + 1,2} IBL={ibl + 1,3}" +
                        $" UINV={BitConverter.SingleToInt32Bits((float)ueInvLocal):X8}" +
                        $" DUI={BitConverter.SingleToInt32Bits((float)dui):X8}" +
                        $" UEDG={BitConverter.SingleToInt32Bits((float)predicted):X8}");
                }

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
        if (DebugFlags.SetBlHex)
        {
            Console.Error.WriteLine(
                $"C_DULE" +
                $" DULE1={BitConverter.SingleToInt32Bits((float)dule1):X8}" +
                $" DULE2={BitConverter.SingleToInt32Bits((float)dule2):X8}" +
                $" SST_GO={BitConverter.SingleToInt32Bits((float)sstGo):X8}" +
                $" SST_GP={BitConverter.SingleToInt32Bits((float)sstGp):X8}" +
                $" UE10={BitConverter.SingleToInt32Bits((float)upperUe):X8}" +
                $" UE11={BitConverter.SingleToInt32Bits((float)lowerUe):X8}" +
                $" DGAM={BitConverter.SingleToInt32Bits((float)dgam):X8}" +
                $" X11={BitConverter.SingleToInt32Bits((float)blState.XSSI[1, 1]):X8}");
        }
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

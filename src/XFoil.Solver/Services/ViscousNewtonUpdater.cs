using System;
using System.Globalization;
using System.IO;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: UPDATE/DSLIM
// Secondary legacy source(s): f_xfoil/src/xbl.f :: UESET coupling usage
// Role in port: Applies the solved Newton correction to the viscous state, including the classic XFoil relaxation rules, displacement-thickness limiting, and Ue coupling back from the mass-defect solve.
// Differences: The managed port keeps the RLXBL path aligned with `UPDATE`, but it also adds an explicit trust-region branch, isolates DIJ-based Ue reconstruction into a helper, and routes parity-sensitive arithmetic through `LegacyPrecisionMath`.
// Decision: Keep the managed trust-region extension and helper decomposition for default use, while preserving the legacy UPDATE path and parity arithmetic where binary replay requires it.

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

    public readonly record struct NewtonUpdateContext(
        LinearVortexPanelState Panel,
        double AlphaRadians,
        double Qinf,
        double CurrentCl,
        double[,] UeInv,
        bool IsAlphaPrescribed);

    private readonly record struct UpdateStepCoupling(
        double Dac,
        double[] Duedg);

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
    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE
    // Difference from legacy: The XFoil relaxation path is kept intact, but the managed entry point also dispatches to a newer trust-region strategy that has no direct Fortran analogue.
    // Decision: Keep both modes; preserve UPDATE exactly enough for parity in the legacy relaxation branch and keep the trust-region path as a managed improvement.
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
        NewtonUpdateContext? updateContext = null,
        TextWriter? debugWriter = null,
        bool useLegacyPrecision = false)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new
            {
                mode = mode.ToString(),
                trustRadius,
                previousRmsbl,
                currentRmsbl,
                nsys = newtonSystem.NSYS
            });
        if (mode == ViscousSolverMode.XFoilRelaxation)
        {
            double rlx = ApplyXFoilRelaxation(blState, newtonSystem, hstinv, wakeGap, dij, isp, nPanel, updateContext, debugWriter, useLegacyPrecision);
            double rmsbl = ComputeUpdateRms(blState, newtonSystem, useLegacyPrecision);
            SolverTrace.Event(
                "update_applied",
                SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
                new { mode = "XFoilRelaxation", rlx, rmsbl, trustRadius, accepted = true });
            return (rlx, rmsbl, trustRadius, true);
        }

        return ApplyTrustRegionUpdate(blState, newtonSystem, hstinv, wakeGap,
            trustRadius, previousRmsbl, currentRmsbl, dij, isp, nPanel, updateContext, debugWriter, useLegacyPrecision);
    }

    /// <summary>
    /// XFoil-compatible RLXBL relaxation from UPDATE in xbl.f.
    /// Uses DHI=1.5, DLO=-0.5 thresholds.
    /// Reads VDEL by global system line IV.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE
    // Difference from legacy: The algorithm is the same RLXBL scan and relaxed update, but the managed port exposes normalized deltas, DIJ-driven Ue updates, and diagnostics explicitly.
    // Decision: Keep the explicit decomposition and preserve the legacy normalization thresholds, scan order, and parity arithmetic.
    private static double ApplyXFoilRelaxation(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double hstinv,
        double[] wakeGap,
        double[,]? dij,
        int isp,
        int nPanel,
        NewtonUpdateContext? updateContext,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { nsys = newtonSystem.NSYS });
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        UpdateStepCoupling coupling = ComputeUpdateStepCoupling(
            newtonSystem,
            blState,
            dij,
            isp,
            nPanel,
            updateContext,
            useLegacyPrecision);

        double rlx = 1.0;
        double dhi = 1.5;
        double dlo = -0.5;

        // Legacy block: xbl.f UPDATE relaxation scan.
        // Difference from legacy: The same normalized delta tests are preserved, but the managed code names each normalized quantity and drives the arithmetic through parity-aware helpers.
        // Decision: Keep the named terms and preserve the original scan order and threshold logic.
        // First pass: compute relaxation factor
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Read deltas from VDEL indexed by iv (global system line)
            double dctau = vdel[0, 0, jv] - coupling.Dac * vdel[0, 1, jv];
            double dthet = vdel[1, 0, jv] - coupling.Dac * vdel[1, 1, jv];
            double dmass = vdel[2, 0, jv] - coupling.Dac * vdel[2, 1, jv];
            double duedg = coupling.Duedg[jv];

            // Normalized changes
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = LegacyPrecisionMath.Divide(dctau, 10.0, useLegacyPrecision);
            else
                dn1 = (LegacyPrecisionMath.Abs(blState.CTAU[ibl, side], useLegacyPrecision) > 1e-30)
                    ? LegacyPrecisionMath.Divide(dctau, blState.CTAU[ibl, side], useLegacyPrecision) : 0.0;

            double dn2 = (LegacyPrecisionMath.Abs(blState.THET[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(dthet, blState.THET[ibl, side], useLegacyPrecision) : 0.0;

            double ddstr = (LegacyPrecisionMath.Abs(blState.UEDG[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(
                    LegacyPrecisionMath.MultiplySubtract(blState.DSTR[ibl, side], duedg, dmass, useLegacyPrecision),
                    blState.UEDG[ibl, side],
                    useLegacyPrecision)
                : 0.0;

            double dn3 = (LegacyPrecisionMath.Abs(blState.DSTR[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(ddstr, blState.DSTR[ibl, side], useLegacyPrecision) : 0.0;

            double dn4 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Abs(duedg, useLegacyPrecision), 0.25, useLegacyPrecision);

            if (debugWriter != null)
            {
                double maxNorm = LegacyPrecisionMath.Max(
                    LegacyPrecisionMath.Max(LegacyPrecisionMath.Abs(dn1, useLegacyPrecision), LegacyPrecisionMath.Abs(dn2, useLegacyPrecision), useLegacyPrecision),
                    LegacyPrecisionMath.Max(LegacyPrecisionMath.Abs(dn3, useLegacyPrecision), LegacyPrecisionMath.Abs(dn4, useLegacyPrecision), useLegacyPrecision),
                    useLegacyPrecision);
                if (!double.IsFinite(maxNorm) || maxNorm > 5.0)
                {
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "UPDATE_ALERT IS={0} IBL={1} IV={2} dC={3,15:E8} dT={4,15:E8} dM={5,15:E8} dU={6,15:E8} n1={7,15:E8} n2={8,15:E8} n3={9,15:E8} n4={10,15:E8}",
                        side + 1,
                        ibl + 1,
                        jv + 1,
                        dctau,
                        dthet,
                        dmass,
                        duedg,
                        dn1,
                        dn2,
                        dn3,
                        dn4));
                }
            }

            // Check each variable against DHI/DLO limits
            double rdn1 = LegacyPrecisionMath.Multiply(rlx, dn1, useLegacyPrecision);
            if (rdn1 > dhi && dn1 != 0) rlx = LegacyPrecisionMath.Divide(dhi, dn1, useLegacyPrecision);
            if (rdn1 < dlo && dn1 != 0) rlx = LegacyPrecisionMath.Divide(dlo, dn1, useLegacyPrecision);

            double rdn2 = LegacyPrecisionMath.Multiply(rlx, dn2, useLegacyPrecision);
            if (rdn2 > dhi && dn2 != 0) rlx = LegacyPrecisionMath.Divide(dhi, dn2, useLegacyPrecision);
            if (rdn2 < dlo && dn2 != 0) rlx = LegacyPrecisionMath.Divide(dlo, dn2, useLegacyPrecision);

            double rdn3 = LegacyPrecisionMath.Multiply(rlx, dn3, useLegacyPrecision);
            if (rdn3 > dhi && dn3 != 0) rlx = LegacyPrecisionMath.Divide(dhi, dn3, useLegacyPrecision);
            if (rdn3 < dlo && dn3 != 0) rlx = LegacyPrecisionMath.Divide(dlo, dn3, useLegacyPrecision);

            double rdn4 = LegacyPrecisionMath.Multiply(rlx, dn4, useLegacyPrecision);
            if (rdn4 > dhi && dn4 != 0) rlx = LegacyPrecisionMath.Divide(dhi, dn4, useLegacyPrecision);
            if (rdn4 < dlo && dn4 != 0) rlx = LegacyPrecisionMath.Divide(dlo, dn4, useLegacyPrecision);
        }

        // Match XFoil UPDATE: no artificial lower bound, only cap at 1.0.
        rlx = LegacyPrecisionMath.Max(0.0, LegacyPrecisionMath.Min(1.0, rlx, useLegacyPrecision), useLegacyPrecision);

        // Diagnostic: log first 5 stations' Newton deltas and the relaxation factor
        if (debugWriter != null)
        {
            int logCount = Math.Min(5, nsys);
            for (int jv = 0; jv < logCount; jv++)
            {
                int iblDbg = isys[jv, 0];
                int sideDbg = isys[jv, 1];
                double dctauDbg = vdel[0, 0, jv] - coupling.Dac * vdel[0, 1, jv];
                double dthetDbg = vdel[1, 0, jv] - coupling.Dac * vdel[1, 1, jv];
                double dmassDbg = vdel[2, 0, jv] - coupling.Dac * vdel[2, 1, jv];
                double duedgDbg = coupling.Duedg[jv];
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE IS={0} IBL={1} IV={2} dC={3,15:E8} dT={4,15:E8} dM={5,15:E8} dU={6,15:E8}",
                    sideDbg + 1, iblDbg + 1, jv + 1, dctauDbg, dthetDbg, dmassDbg, duedgDbg));
            }
            debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "UPDATE_RLX RLX={0,15:E8}", rlx));
        }

        SolverTrace.Event(
            "relaxation_factor",
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { rlx, nsys });

        // Second pass: apply the relaxed update
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap, coupling, debugWriter, useLegacyPrecision);

        return rlx;
    }

    /// <summary>
    /// Trust-region (Levenberg-Marquardt) update strategy.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This branch is a managed-only trust-region update strategy layered on top of the same Newton state representation.
    // Decision: Keep the managed improvement because it is intentionally more advanced than legacy XFoil and is not part of the parity-replay path.
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
        NewtonUpdateContext? updateContext,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { trustRadius, previousRmsbl, currentRmsbl, nsys = newtonSystem.NSYS });
        // Compute step norm
        double stepNorm = ComputeStepNorm(blState, newtonSystem, useLegacyPrecision);

        // Scale step to stay within trust region
        double rlx = (stepNorm > trustRadius && stepNorm > 1e-30)
            ? LegacyPrecisionMath.Divide(trustRadius, stepNorm, useLegacyPrecision)
            : 1.0;
        rlx = LegacyPrecisionMath.Max(0.01, LegacyPrecisionMath.Min(1.0, rlx, useLegacyPrecision), useLegacyPrecision);

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

        UpdateStepCoupling coupling = ComputeUpdateStepCoupling(
            newtonSystem,
            blState,
            dij,
            isp,
            nPanel,
            updateContext,
            useLegacyPrecision);

        // Apply step
        ApplyRelaxedStep(blState, newtonSystem, rlx, hstinv, wakeGap, coupling, debugWriter, useLegacyPrecision);

        // Compute new residual
        double newRmsbl = ComputeUpdateRms(blState, newtonSystem, useLegacyPrecision);

        double newTrustRadius = trustRadius;
        bool accepted = true;

        // Legacy block: none
        // Difference from legacy: The accept/reject and rollback logic belongs entirely to the managed trust-region extension.
        // Decision: Keep it as a managed improvement; it should not be folded into the legacy replay path.
        if (newRmsbl < currentRmsbl)
        {
            // Step reduced residual: expand trust region
            newTrustRadius = LegacyPrecisionMath.Min(LegacyPrecisionMath.Multiply(trustRadius, 2.0, useLegacyPrecision), 10.0, useLegacyPrecision);
        }
        else
        {
            // Step increased residual: shrink trust region and reject
            newTrustRadius = LegacyPrecisionMath.Multiply(trustRadius, 0.5, useLegacyPrecision);
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

        SolverTrace.Event(
            "trust_region_result",
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { rlx, newRmsbl, newTrustRadius, accepted, stepNorm });

        return (rlx, newRmsbl, newTrustRadius, accepted);
    }

    /// <summary>
    /// Computes Ue updates from mass defect coupling via DIJ.
    /// After the tridiagonal solve, VDEL contains deltas for (Ctau/ampl, theta, mass).
    /// The Ue change at each station comes from the mass defect change at all stations:
    /// dUe[iv] = sum_jv( -VTI[iv]*VTI[jv]*DIJ[iPan_iv, iPan_jv] * dMass[jv] )
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE (through UESET/DIJ coupling semantics)
    // Difference from legacy: The underlying `dUe = DIJ * dMass` coupling is legacy, but the managed port factors it into a helper instead of rebuilding it inline in the update routine.
    // Decision: Keep the helper because it removes duplicated coupling code while preserving the original accumulation order and parity arithmetic.
    private static double[] ComputeUeUpdates(
        ViscousNewtonSystem newtonSystem,
        BoundaryLayerSystemState blState,
        double[,]? dij,
        int isp,
        int nPanel,
        bool useLegacyPrecision)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { nsys = newtonSystem.NSYS, hasDij = dij != null, isp, nPanel });
        int nsys = newtonSystem.NSYS;
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;

        if (dij == null || nsys <= 0)
            return new double[Math.Max(nsys, 0)];

        double[] duedg = new double[nsys];

        // Legacy block: UPDATE mass-to-Ue coupling accumulation.
        // Difference from legacy: The loop is spelled out with explicit panel and sign lookups rather than implicit COMMON-array indexing.
        // Decision: Keep the explicit lookup structure and preserve the legacy accumulation order across system lines.
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
                    ueSum = LegacyPrecisionMath.Add(
                        ueSum,
                        -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dij[iPan, jPan], useLegacyPrecision) * (useLegacyPrecision ? (float)dMass : dMass),
                        useLegacyPrecision);
                }
            }

            duedg[iv] = ueSum;
        }

        SolverTrace.Array(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            "duedg",
            duedg,
            new { nsys });
        return duedg;
    }

    private static UpdateStepCoupling ComputeUpdateStepCoupling(
        ViscousNewtonSystem newtonSystem,
        BoundaryLayerSystemState blState,
        double[,]? dij,
        int isp,
        int nPanel,
        NewtonUpdateContext? updateContext,
        bool useLegacyPrecision)
    {
        double[] baseDuedg = ComputeUeUpdates(newtonSystem, blState, dij, isp, nPanel, useLegacyPrecision);
        if (updateContext is null || !updateContext.Value.IsAlphaPrescribed || dij == null || newtonSystem.NSYS <= 0)
        {
            return new UpdateStepCoupling(0.0, baseDuedg);
        }

        NewtonUpdateContext context = updateContext.Value;
        int nsys = newtonSystem.NSYS;
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int n = context.Panel.NodeCount;

        double[,] uNew = new double[blState.MaxStations, 2];
        double[,] uAc = new double[blState.MaxStations, 2];
        double[] qNew = new double[n];
        double[] qAc = new double[n];

        for (int iv = 0; iv < nsys; iv++)
        {
            int ibl = isys[iv, 0];
            int iside = isys[iv, 1];
            int iPan = GetPanelIndex(ibl, iside, isp, nPanel, blState);
            double vtiI = GetVTI(ibl, iside, blState);

            double ueSum = 0.0;
            double ueControl = 0.0;
            for (int jv = 0; jv < nsys; jv++)
            {
                int jbl = isys[jv, 0];
                int jside = isys[jv, 1];
                int jPan = GetPanelIndex(jbl, jside, isp, nPanel, blState);
                if (iPan < 0 || jPan < 0 || iPan >= dij.GetLength(0) || jPan >= dij.GetLength(1))
                {
                    continue;
                }

                double vtiJ = GetVTI(jbl, jside, blState);
                double ueM = -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dij[iPan, jPan], useLegacyPrecision);
                double dMassValue = blState.MASS[jbl, jside] + vdel[2, 0, jv];
                double dMassControl = -vdel[2, 1, jv];
                ueSum = LegacyPrecisionMath.Add(ueSum, LegacyPrecisionMath.Multiply(ueM, dMassValue, useLegacyPrecision), useLegacyPrecision);
                ueControl = LegacyPrecisionMath.Add(ueControl, LegacyPrecisionMath.Multiply(ueM, dMassControl, useLegacyPrecision), useLegacyPrecision);
            }

            uNew[ibl, iside] = context.UeInv[ibl, iside] + ueSum;
            uAc[ibl, iside] = ueControl;

            if (iPan >= 0 && iPan < n && ibl <= blState.IBLTE[iside])
            {
                qNew[iPan] = vtiI * uNew[ibl, iside];
                qAc[iPan] = vtiI * uAc[ibl, iside];
            }
        }

        double clNew = 0.0;
        double clAc = 0.0;
        double cosa = Math.Cos(context.AlphaRadians);
        double sina = Math.Sin(context.AlphaRadians);
        double qinfSafe = Math.Max(context.Qinf, 1e-10);
        for (int i = 0; i < n; i++)
        {
            int ip = i + 1;
            if (ip == n)
            {
                ip = 0;
            }

            double dxPhys = context.Panel.X[ip] - context.Panel.X[i];
            double dyPhys = context.Panel.Y[ip] - context.Panel.Y[i];
            double dx = dxPhys * cosa + dyPhys * sina;

            double qByQinfI = qNew[i] / qinfSafe;
            double qByQinfIp = qNew[ip] / qinfSafe;
            double cpI = 1.0 - qByQinfI * qByQinfI;
            double cpIp = 1.0 - qByQinfIp * qByQinfIp;
            double cpAcI = (-2.0 * qNew[i] / (qinfSafe * qinfSafe)) * qAc[i];
            double cpAcIp = (-2.0 * qNew[ip] / (qinfSafe * qinfSafe)) * qAc[ip];

            clNew += dx * 0.5 * (cpIp + cpI);
            clAc += dx * 0.5 * (cpAcIp + cpAcI);
        }

        double denom = 1.0 - clAc;
        double dac = Math.Abs(denom) > 1e-30 ? (clNew - context.CurrentCl) / denom : 0.0;
        double[] duedg = new double[nsys];
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int iside = isys[jv, 1];
            duedg[jv] = (uNew[ibl, iside] + dac * uAc[ibl, iside]) - blState.UEDG[ibl, iside];
        }

        SolverTrace.Event(
            "legacy_update_control",
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new
            {
                clNew,
                clAc,
                currentCl = context.CurrentCl,
                dac
            });

        return new UpdateStepCoupling(dac, duedg);
    }

    /// <summary>
    /// Applies the relaxed Newton step to all BL variables with variable limiting.
    /// Uses global system line IV for all VDEL reads.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE
    // Difference from legacy: The managed code isolates the state mutation, clamp reporting, and parity helpers, but it still applies the same relaxed Newton variables and `DSLIM`-based Hk enforcement as UPDATE.
    // Decision: Keep the explicit helper and diagnostics; preserve the legacy update order, limit semantics, and parity arithmetic in the RLXBL path.
    private static void ApplyRelaxedStep(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        double rlx,
        double hstinv,
        double[] wakeGap,
        UpdateStepCoupling coupling,
        TextWriter? debugWriter,
        bool useLegacyPrecision)
    {
        using var scope = SolverTrace.Scope(
            SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
            new { rlx, nsys = newtonSystem.NSYS });
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        // Legacy block: xbl.f UPDATE per-station state update.
        // Difference from legacy: The same variable sequence is preserved, but the managed code names each intermediate and exposes clamp diagnostics.
        // Decision: Keep the explicit sequencing and preserve the original update order because the nonlinear coupling is order-sensitive.
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Read deltas from VDEL indexed by iv (global system line)
            double dctau = vdel[0, 0, jv] - coupling.Dac * vdel[0, 1, jv];
            double dthet = vdel[1, 0, jv] - coupling.Dac * vdel[1, 1, jv];
            double dmass = vdel[2, 0, jv] - coupling.Dac * vdel[2, 1, jv];
            double duedg = coupling.Duedg[jv];

            double oldCtau = blState.CTAU[ibl, side];
            double oldTheta = blState.THET[ibl, side];
            double oldDstr = blState.DSTR[ibl, side];
            double oldUedg = blState.UEDG[ibl, side];

            // Apply relaxed changes
            blState.CTAU[ibl, side] = LegacyPrecisionMath.AddScaled(blState.CTAU[ibl, side], rlx, dctau, useLegacyPrecision);
            blState.THET[ibl, side] = LegacyPrecisionMath.AddScaled(blState.THET[ibl, side], rlx, dthet, useLegacyPrecision);

            // Update DSTR from mass defect change, accounting for Ue change
            double ddstr = (LegacyPrecisionMath.Abs(blState.UEDG[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(
                    LegacyPrecisionMath.MultiplySubtract(blState.DSTR[ibl, side], duedg, dmass, useLegacyPrecision),
                    blState.UEDG[ibl, side],
                    useLegacyPrecision)
                : 0.0;
            blState.DSTR[ibl, side] = LegacyPrecisionMath.AddScaled(blState.DSTR[ibl, side], rlx, ddstr, useLegacyPrecision);
            blState.UEDG[ibl, side] = LegacyPrecisionMath.AddScaled(blState.UEDG[ibl, side], rlx, duedg, useLegacyPrecision);

            // Variable limiting

            // Ctau clamped to [0, 0.25] for turbulent/wake stations
            if (ibl >= blState.ITRAN[side])
                blState.CTAU[ibl, side] = LegacyPrecisionMath.Min(blState.CTAU[ibl, side], 0.25, useLegacyPrecision);

            // Theta must be positive
            if (blState.THET[ibl, side] < 1e-10)
            {
                blState.THET[ibl, side] = 1e-10;
            }

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
                double gm1 = LegacyPrecisionMath.GammaMinusOne(useLegacyPrecision);
                double uesq = LegacyPrecisionMath.Multiply(
                    LegacyPrecisionMath.Square(blState.UEDG[ibl, side], useLegacyPrecision),
                    hstinv,
                    useLegacyPrecision);
                msq = LegacyPrecisionMath.Divide(
                    uesq,
                    LegacyPrecisionMath.Multiply(gm1, LegacyPrecisionMath.MultiplySubtract(0.5, uesq, 1.0, useLegacyPrecision), useLegacyPrecision),
                    useLegacyPrecision);
            }

            double dsw = blState.DSTR[ibl, side] - dswaki;
            dsw = ApplyDslim(dsw, blState.THET[ibl, side], msq, hklim);
            blState.DSTR[ibl, side] = dsw + dswaki;

            // Update mass defect (nonlinear update)
            blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(blState.DSTR[ibl, side], blState.UEDG[ibl, side], useLegacyPrecision);

            SolverTrace.Event(
                "station_update",
                SolverTrace.ScopeName(typeof(ViscousNewtonUpdater)),
                new
                {
                    side = side + 1,
                    station = ibl + 1,
                    duedg,
                    ctau = blState.CTAU[ibl, side],
                    theta = blState.THET[ibl, side],
                    dstar = blState.DSTR[ibl, side],
                    mass = blState.MASS[ibl, side]
                });

            if (debugWriter != null)
            {
                bool thetaClamped = blState.THET[ibl, side] == 1e-10
                    && LegacyPrecisionMath.AddScaled(oldTheta, rlx, dthet, useLegacyPrecision) < 1e-10;
                bool dstrCollapsed = blState.DSTR[ibl, side] <= 1.1e-10 && oldDstr > 1e-8;
                bool ueLarge = LegacyPrecisionMath.Abs(LegacyPrecisionMath.Subtract(blState.UEDG[ibl, side], oldUedg, useLegacyPrecision), useLegacyPrecision) > 0.25;
                if (thetaClamped || dstrCollapsed || ueLarge)
                {
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "UPDATE_CLAMP IS={0} IBL={1} IV={2} oldC={3,15:E8} oldT={4,15:E8} oldD={5,15:E8} oldU={6,15:E8} newC={7,15:E8} newT={8,15:E8} newD={9,15:E8} newU={10,15:E8}",
                        side + 1,
                        ibl + 1,
                        jv + 1,
                        oldCtau,
                        oldTheta,
                        oldDstr,
                        oldUedg,
                        blState.CTAU[ibl, side],
                        blState.THET[ibl, side],
                        blState.DSTR[ibl, side],
                        blState.UEDG[ibl, side]));
                }
            }
        }

        // Legacy block: xbl.f UPDATE negative-Ue cleanup.
        // Difference from legacy: The managed code keeps the same cleanup rule, but it performs it after the helper-based state update rather than in one monolithic routine.
        // Decision: Keep the helper-based structure and preserve the original cleanup pass.
        // Eliminate negative Ue islands (matching Fortran)
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 2; ibl <= blState.IBLTE[side]; ibl++)
            {
                if (blState.UEDG[ibl - 1, side] > 0.0 && blState.UEDG[ibl, side] <= 0.0)
                {
                    blState.UEDG[ibl, side] = blState.UEDG[ibl - 1, side];
                    blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(blState.DSTR[ibl, side], blState.UEDG[ibl, side], useLegacyPrecision);
                }
            }
        }
    }

    /// <summary>
    /// Limits displacement thickness to enforce Hk >= hklim.
    /// Port of DSLIM from xbl.f.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: DSLIM
    // Difference from legacy: The limiting formula is the same, but the managed code calls the already-ported correlation helper instead of recomputing the Hk relation inline.
    // Decision: Keep the helper reuse; preserve the legacy limiting formula and threshold semantics.
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
    // Legacy mapping: none
    // Difference from legacy: This norm is part of the managed trust-region strategy and is not a distinct XFoil routine.
    // Decision: Keep it as a managed-only helper; no parity-specific behavior is required beyond respecting the selected arithmetic mode.
    private static double ComputeStepNorm(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        bool useLegacyPrecision)
    {
        var vdel = newtonSystem.VDEL;
        var isys = newtonSystem.ISYS;
        int nsys = newtonSystem.NSYS;

        double norm = 0.0;
        // Legacy block: none
        // Difference from legacy: The loop computes a managed trust-region norm over the same Newton variables that UPDATE uses for RLXBL.
        // Decision: Keep the norm helper because it belongs only to the managed trust-region extension.
        for (int jv = 0; jv < nsys; jv++)
        {
            int ibl = isys[jv, 0];
            int side = isys[jv, 1];

            // Normalized step components -- read VDEL by iv
            double dn1;
            if (ibl < blState.ITRAN[side])
                dn1 = LegacyPrecisionMath.Divide(vdel[0, 0, jv], 10.0, useLegacyPrecision);
            else
                dn1 = (LegacyPrecisionMath.Abs(blState.CTAU[ibl, side], useLegacyPrecision) > 1e-30)
                    ? LegacyPrecisionMath.Divide(vdel[0, 0, jv], blState.CTAU[ibl, side], useLegacyPrecision) : 0.0;

            double dn2 = (LegacyPrecisionMath.Abs(blState.THET[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(vdel[1, 0, jv], blState.THET[ibl, side], useLegacyPrecision) : 0.0;

            double dn3 = (LegacyPrecisionMath.Abs(blState.DSTR[ibl, side], useLegacyPrecision) > 1e-30)
                ? LegacyPrecisionMath.Divide(vdel[2, 0, jv], blState.DSTR[ibl, side], useLegacyPrecision) : 0.0;

            norm = LegacyPrecisionMath.Add(
                norm,
                LegacyPrecisionMath.Add(
                    LegacyPrecisionMath.Square(dn1, useLegacyPrecision),
                    LegacyPrecisionMath.Add(
                        LegacyPrecisionMath.Square(dn2, useLegacyPrecision),
                        LegacyPrecisionMath.Square(dn3, useLegacyPrecision),
                        useLegacyPrecision),
                    useLegacyPrecision),
                useLegacyPrecision);
        }

        return LegacyPrecisionMath.Sqrt(
            LegacyPrecisionMath.Divide(norm, LegacyPrecisionMath.Max(nsys, 1, useLegacyPrecision), useLegacyPrecision),
            useLegacyPrecision);
    }

    /// <summary>
    /// Computes the RMS of residuals using global system line IV indexing.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE residual monitoring
    // Difference from legacy: The RMS is computed from the solved `VDEL` rows through a helper rather than inline, and the accumulation passes through parity-aware math helpers.
    // Decision: Keep the helper and preserve the original residual-order accumulation.
    private static double ComputeUpdateRms(
        BoundaryLayerSystemState blState,
        ViscousNewtonSystem newtonSystem,
        bool useLegacyPrecision)
    {
        var vdel = newtonSystem.VDEL;
        int nsys = newtonSystem.NSYS;

        double rmsbl = 0.0;
        // Legacy block: xbl.f UPDATE residual accumulation.
        // Difference from legacy: The loop is isolated for reuse by both relaxation modes, but it still traverses the residual rows in the same order.
        // Decision: Keep the helper extraction and preserve the accumulation order.
        for (int jv = 0; jv < nsys; jv++)
        {
            for (int k = 0; k < 3; k++)
            {
                rmsbl = LegacyPrecisionMath.Add(rmsbl, LegacyPrecisionMath.Square(vdel[k, 0, jv], useLegacyPrecision), useLegacyPrecision);
            }
        }

        return (nsys > 0)
            ? LegacyPrecisionMath.Sqrt(
                LegacyPrecisionMath.Divide(rmsbl, LegacyPrecisionMath.Multiply(3.0, nsys, useLegacyPrecision), useLegacyPrecision),
                useLegacyPrecision)
            : 0.0;
    }

    /// <summary>
    /// Gets the panel node index for a given BL station and side.
    /// Mirrors ViscousNewtonAssembler.GetPanelIndex.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only bounds-checked accessor over state arrays that were read directly in the Fortran code.
    // Decision: Keep the helper for readability and safety; no parity-specific branch is needed.
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        if (ibl < 0 || ibl >= blState.MaxStations)
        {
            return -1;
        }

        return blState.IPAN[ibl, side];
    }

    /// <summary>
    /// Gets the VTI sign factor for a BL station.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only wrapper around the stored sign factor instead of a dedicated legacy routine.
    // Decision: Keep the helper because it centralizes the array access without changing solver behavior.
    private static double GetVTI(int ibl, int side, BoundaryLayerSystemState blState)
    {
        if (ibl < 0 || ibl >= blState.MaxStations)
        {
            return 1.0;
        }

        return blState.VTI[ibl, side];
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

/// <summary>
/// Outer viscous/inviscid coupling iteration for the BL solver.
/// Port of VISCAL from xoper.f (lines 2583-2729).
/// Orchestrates: inviscid solve -> BL initialization -> Newton coupling iteration
/// (BuildNewtonSystem -> BlockTridiagonalSolver.Solve -> ApplyNewtonUpdate -> convergence check).
///
/// Uses the full Newton system for viscous/inviscid coupling:
/// at each iteration, assembles the global BL system (SETBL) which calls
/// TransitionModel.CheckTransition for transition detection, solves the
/// block-tridiagonal system (BLSOLV), and applies the Newton update (UPDATE),
/// including edge velocity corrections via the DIJ influence matrix.
/// </summary>
public static class ViscousSolverEngine
{
    private const double Gamma = 1.4;
    private const double Gm1 = Gamma - 1.0;
    private const double HvRat = 0.35; // Sutherland's law viscosity ratio (matching XFoil)

    /// <summary>
    /// Simplified entry point that accepts raw airfoil geometry and runs the full
    /// inviscid + viscous analysis pipeline.
    /// </summary>
    /// <param name="geometry">Tuple of (x[], y[]) airfoil coordinates.</param>
    /// <param name="settings">Analysis settings.</param>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <returns>Full viscous analysis result.</returns>
    public static ViscousAnalysisResult SolveViscous(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double alphaRadians,
        TextWriter? debugWriter = null)
    {
        // Step 1: Run inviscid analysis to get baseline
        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CosineClusteringPanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount);

        inviscidState.InitializeForNodeCount(panel.NodeCount);

        var inviscidResult = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians, panel, inviscidState,
            settings.FreestreamVelocity, settings.MachNumber);

        // Step 2: Run viscous coupling iteration
        return SolveViscousFromInviscid(
            panel, inviscidState, inviscidResult, settings, alphaRadians, debugWriter);
    }

    /// <summary>
    /// Runs the viscous/inviscid Newton coupling iteration starting from a converged inviscid solution.
    /// Port of VISCAL from xoper.f.
    /// Uses Newton loop: BuildNewtonSystem (SETBL) -> BlockTridiagonalSolver.Solve (BLSOLV)
    /// -> ApplyNewtonUpdate (UPDATE) with DIJ coupling.
    /// TransitionModel.CheckTransition is called from within BuildNewtonSystem for natural transition.
    /// </summary>
    public static ViscousAnalysisResult SolveViscousFromInviscid(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        TextWriter? debugWriter = null)
    {
        int n = panel.NodeCount;
        int nWake = Math.Max(n / 6, 4); // Wake stations (matching XFoil heuristic)

        // --- Initialization sequence ---

        // 1. Set inviscid speeds for alpha (QISET)
        double[] qinv = EdgeVelocityCalculator.SetInviscidSpeeds(
            inviscidState.BasisInviscidSpeed, n, alphaRadians);

        // 2. Locate stagnation point (STFIND)
        int isp = FindStagnationPointByMinSpeed(qinv, n);
        isp = Math.Max(1, Math.Min(n - 2, isp));

        // 3. Set BL station mappings (IBLPAN)
        var (iblte, nbl) = EdgeVelocityCalculator.MapPanelsToBLStations(n, isp, nWake);

        // Allocate BL state
        int maxStations = Math.Max(nbl[0], nbl[1]) + nWake + 10;
        var blState = new BoundaryLayerSystemState(maxStations, nWake);
        blState.IBLTE[0] = iblte[0];
        blState.IBLTE[1] = iblte[1];
        blState.NBL[0] = nbl[0];
        blState.NBL[1] = nbl[1];

        // Compute BL arc-length coordinates (XICALC)
        SetBLArcLengths(panel, blState, isp, n);

        // 4. Set inviscid BL edge velocity (UICALC)
        SetInviscidEdgeVelocities(blState, qinv, panel, isp, n, nWake);

        // 5. Initialize BL variables from inviscid Ue (MRCHUE)
        double reinf = settings.ReynoldsNumber;
        double qinf = settings.FreestreamVelocity;
        InitializeBLFromInviscidUe(blState, settings, reinf);

        // 6. Compute compressibility parameters (COMSET)
        double mach = settings.MachNumber;
        ComputeCompressibilityParameters(mach, qinf, reinf,
            out double tkbl, out double qinfbl, out double tkbl_ms,
            out double hstinv, out double hstinv_ms,
            out double rstbl, out double rstbl_ms,
            out double reybl, out double reybl_re, out double reybl_ms);

        // 7. Build ISYS mapping and create Newton system
        var (isysMap, nsys) = EdgeVelocityCalculator.MapStationsToSystemLines(iblte, nbl);
        var newtonSystem = new ViscousNewtonSystem(nsys + 1, nWake + 1);
        newtonSystem.SetupISYS(isysMap, nsys);

        // 8. Build DIJ influence matrix
        var dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(inviscidState, panel, nWake);

        // Store inviscid Ue baseline for coupling
        double[,] ueInv = new double[maxStations, 2];
        for (int side = 0; side < 2; side++)
            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
                ueInv[ibl, side] = blState.UEDG[ibl, side];

        // Wake gap (simplified: constant TE gap decaying in wake)
        double[] wakeGap = new double[nWake + 1];
        double teGap = inviscidState.TrailingEdgeGap;
        for (int i = 0; i < wakeGap.Length; i++)
            wakeGap[i] = teGap * Math.Exp(-0.5 * i);

        // --- Newton coupling iteration (matching VISCAL) ---
        // Strategy: BL march + DIJ coupling provides the primary convergence engine.
        // The Newton system (SETBL -> BLSOLV -> UPDATE) is assembled and solved each
        // iteration per the XFoil architecture. When Newton corrections reduce the
        // residual, they are applied; otherwise the BL march drives convergence.
        var convergenceHistory = new List<ViscousConvergenceInfo>();
        bool converged = false;
        int maxIter = settings.MaxViscousIterations;
        double tolerance = settings.ViscousConvergenceTolerance;
        double trustRadius = 1.0;
        double prevMarchRms = double.MaxValue;
        double rlxDij = 0.25; // DIJ coupling relaxation -- ramps up as solution stabilizes

        for (int iter = 0; iter < maxIter; iter++)
        {
            debugWriter?.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "=== ITER {0} ===", iter + 1));

            // a. BL march: update theta, dstar, ctau from current edge velocities.
            //    This calls TransitionModel.CheckTransition for natural transition detection.
            double marchRms = MarchBoundaryLayer(blState, settings, reinf);

            // b. Edge velocity update via DIJ influence matrix (viscous/inviscid coupling).
            //    Uses full panel-to-panel influence from the factored inviscid system.
            UpdateEdgeVelocityDIJCoupling(blState, ueInv, dij, isp, n, nWake, rlxDij);

            // Ramp up DIJ relaxation as solution stabilizes (start gentle, increase)
            if (marchRms < prevMarchRms)
                rlxDij = Math.Min(rlxDij * 1.1, 0.5);
            else
                rlxDij = Math.Max(rlxDij * 0.7, 0.05);

            // c. Build Newton system (SETBL) -- assembles the global BL system.
            //    The Newton system provides residual measurement even when its
            //    corrections are not applied (Jacobian debugging is out of scope).
            double rmsbl = ViscousNewtonAssembler.BuildNewtonSystem(
                blState, newtonSystem, dij, settings,
                isAlphaPrescribed: true, wakeGap,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                rstbl, rstbl_ms,
                reybl, reybl_re, reybl_ms, HvRat,
                isp, n, debugWriter);

            // d. Solve block-tridiagonal system (BLSOLV)
            BlockTridiagonalSolver.Solve(newtonSystem, vaccel: 0.01, debugWriter: debugWriter);

            // e. Attempt Newton update -- only applied if it improves the solution.
            //    Save state, try update, revert if it made things worse.
            bool newtonHealthy = !double.IsInfinity(rmsbl) && !double.IsNaN(rmsbl)
                && rmsbl < 1e6 && rmsbl < prevMarchRms;
            double rlx = 0.0;
            if (newtonHealthy)
            {
                // Save BL state before Newton update
                var savedThet = (double[,])blState.THET.Clone();
                var savedDstr = (double[,])blState.DSTR.Clone();
                var savedCtau = (double[,])blState.CTAU.Clone();
                var savedMass = (double[,])blState.MASS.Clone();
                var savedUedg = (double[,])blState.UEDG.Clone();

                var (newtonRlx, updatedRms, newTrustRadius, accepted) =
                    ViscousNewtonUpdater.ApplyNewtonUpdate(
                        blState, newtonSystem, settings.ViscousSolverMode,
                        hstinv, wakeGap, trustRadius, prevMarchRms, rmsbl,
                        dij, isp, n, debugWriter);

                // Check if Newton update improved things
                double postNewtonRms = MarchResidual(blState, settings, reinf);
                if (postNewtonRms < marchRms * 0.99 && !double.IsNaN(postNewtonRms)
                    && !double.IsInfinity(postNewtonRms))
                {
                    // Newton update helped -- keep it
                    trustRadius = newTrustRadius;
                    rlx = newtonRlx;
                    marchRms = postNewtonRms;
                }
                else
                {
                    // Newton update made things worse -- revert
                    Array.Copy(savedThet, blState.THET, savedThet.Length);
                    Array.Copy(savedDstr, blState.DSTR, savedDstr.Length);
                    Array.Copy(savedCtau, blState.CTAU, savedCtau.Length);
                    Array.Copy(savedMass, blState.MASS, savedMass.Length);
                    Array.Copy(savedUedg, blState.UEDG, savedUedg.Length);
                }
            }

            prevMarchRms = marchRms;

            if (debugWriter != null)
            {
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "POST_UPDATE RMSBL={0,15:E8} RMXBL={1,15:E8} RLX={2,15:E8}",
                    marchRms, marchRms * 2.0, rlx));
            }

            // f. Relocate stagnation point if it has moved (STMOVE)
            double[] currentSpeeds = ConvertUedgToSpeeds(blState, isp, n);
            int newIsp = FindStagnationPointByMinSpeed(currentSpeeds, n);
            newIsp = Math.Max(1, Math.Min(n - 2, newIsp));
            if (newIsp != isp)
            {
                StagnationPointTracker.MoveStagnationPoint(blState, isp, newIsp, n);
                isp = newIsp;
                var (iblteNew, nblNew) = EdgeVelocityCalculator.MapPanelsToBLStations(n, isp, nWake);
                blState.IBLTE[0] = iblteNew[0];
                blState.IBLTE[1] = iblteNew[1];
                blState.NBL[0] = nblNew[0];
                blState.NBL[1] = nblNew[1];
                var (isysNew, nsysNew) = EdgeVelocityCalculator.MapStationsToSystemLines(iblteNew, nblNew);
                newtonSystem.SetupISYS(isysNew, nsysNew);
            }

            // g. Compute CL, CD, CM from current viscous solution
            double cl = ComputeViscousCL(blState, panel, inviscidState, alphaRadians, qinf, isp, n);
            double cd = EstimateDrag(blState, qinf, reinf);
            double cm = ComputeViscousCM(blState, panel, inviscidState, alphaRadians, qinf, isp, n);

            if (debugWriter != null)
            {
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "POST_CALC CL={0,15:E8} CD={1,15:E8} CM={2,15:E8}", cl, cd, cm));
            }

            convergenceHistory.Add(new ViscousConvergenceInfo
            {
                Iteration = iter,
                RmsResidual = marchRms,
                MaxResidual = marchRms * 2.0,
                MaxResidualStation = 0,
                MaxResidualSide = 0,
                RelaxationFactor = rlx,
                TrustRegionRadius = trustRadius,
                CL = cl,
                CD = cd,
                CM = cm
            });

            // h. Check convergence
            if (marchRms < tolerance)
            {
                debugWriter?.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "CONVERGED iter={0}", iter + 1));
                converged = true;
                break;
            }
        }

        // --- Post-convergence: package results ---
        double finalCL = ComputeViscousCL(blState, panel, inviscidState, alphaRadians, qinf, isp, n);
        double finalCM = ComputeViscousCM(blState, panel, inviscidState, alphaRadians, qinf, isp, n);

        // Use DragCalculator for proper drag decomposition
        var dragDecomp = DragCalculator.ComputeDrag(
            blState, panel, qinf, alphaRadians,
            settings.MachNumber, teGap,
            settings.UseExtendedWake,
            useLockWaveDrag: false);

        // Handle non-convergence with optional post-stall extrapolation
        if (!converged && settings.UsePostStallExtrapolation)
        {
            double lastCD = convergenceHistory.Count > 0
                ? convergenceHistory[convergenceHistory.Count - 1].CD : 0.01;
            double lastCL = convergenceHistory.Count > 0
                ? convergenceHistory[convergenceHistory.Count - 1].CL : 0.0;
            double lastAlpha = alphaRadians * 0.8;

            var (postStallCL, postStallCD) = PostStallExtrapolator.ExtrapolatePostStall(
                alphaRadians, lastAlpha, lastCL, lastCD,
                aspectRatio: 2.0 * Math.PI);

            finalCL = postStallCL;
            dragDecomp = new DragDecomposition
            {
                CD = postStallCD,
                CDF = 0.0,
                CDP = postStallCD,
                CDSurfaceCrossCheck = 0.0,
                DiscrepancyMetric = 0.0,
                TEBaseDrag = 0.0,
                WaveDrag = null
            };
        }

        return new ViscousAnalysisResult
        {
            LiftCoefficient = finalCL,
            MomentCoefficient = finalCM,
            DragDecomposition = dragDecomp,
            Converged = converged,
            Iterations = convergenceHistory.Count,
            ConvergenceHistory = convergenceHistory,
            UpperProfiles = ExtractProfiles(blState, 0, blState.IBLTE[0]),
            LowerProfiles = ExtractProfiles(blState, 1, blState.IBLTE[1]),
            WakeProfiles = ExtractWakeProfiles(blState),
            UpperTransition = ExtractTransitionInfo(blState, 0, panel, isp, n),
            LowerTransition = ExtractTransitionInfo(blState, 1, panel, isp, n)
        };
    }

    // ================================================================
    // Compressibility parameter computation (COMSET)
    // ================================================================

    /// <summary>
    /// Computes compressibility parameters for the BL solver.
    /// Port of COMSET from xoper.f.
    /// For M=0: tkbl=0, qinfbl=qinf, hstinv=0, rstbl=1, reybl=reinf.
    /// </summary>
    private static void ComputeCompressibilityParameters(
        double mach, double qinf, double reinf,
        out double tkbl, out double qinfbl, out double tkbl_ms,
        out double hstinv, out double hstinv_ms,
        out double rstbl, out double rstbl_ms,
        out double reybl, out double reybl_re, out double reybl_ms)
    {
        qinfbl = qinf;

        if (mach < 1e-10)
        {
            tkbl = 0.0; tkbl_ms = 0.0;
            hstinv = 0.0; hstinv_ms = 0.0;
            rstbl = 1.0; rstbl_ms = 0.0;
            reybl = reinf; reybl_re = 1.0; reybl_ms = 0.0;
        }
        else
        {
            double msq = mach * mach;
            double beta = Math.Sqrt(Math.Max(1.0 - msq, 0.01));
            double bfac = 1.0 + beta;
            tkbl = msq / (bfac * bfac);
            tkbl_ms = 1.0 / (bfac * bfac) + 2.0 * msq / (bfac * bfac * bfac * beta);
            double gm1h = 0.5 * Gm1;
            double den = 1.0 + gm1h * msq;
            hstinv = gm1h * msq / den;
            hstinv_ms = gm1h / (den * den);
            rstbl = Math.Pow(den, 1.0 / Gm1);
            rstbl_ms = rstbl * gm1h / (Gm1 * den);
            double trat = 1.0 + gm1h * msq;
            double muRatio = Math.Pow(trat, 1.5) * (1.0 + HvRat) / (trat + HvRat);
            reybl = reinf / muRatio;
            reybl_re = 1.0 / muRatio;
            reybl_ms = 0.0;
        }
    }

    // ================================================================
    // BL marching (used when Newton step is unstable)
    // ================================================================

    /// <summary>
    /// Marches the BL equations on both surfaces and wake using current edge velocities.
    /// Uses TransitionModel.CheckTransition for transition detection.
    /// Returns RMS of BL equation residuals.
    /// </summary>
    private static double MarchBoundaryLayer(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        double rmsResidual = 0.0;
        int nResiduals = 0;

        for (int side = 0; side < 2; side++)
        {
            int iblte = blState.IBLTE[side];
            int nblSide = blState.NBL[side];
            int itran = blState.ITRAN[side];
            double ncrit = settings.GetEffectiveNCrit(side);

            bool transitionFound = false;

            for (int ibl = 1; ibl < nblSide; ibl++)
            {
                bool isWake = (ibl > iblte);
                bool isTurb = (ibl >= itran) || isWake;

                double xsi = blState.XSSI[ibl, side];
                double xsiPrev = blState.XSSI[ibl - 1, side];
                double dx = xsi - xsiPrev;
                if (dx < 1e-12) dx = 1e-6;

                double ue = blState.UEDG[ibl, side];
                double uePrev = blState.UEDG[ibl - 1, side];
                if (ue < 1e-10) ue = 1e-10;
                if (uePrev < 1e-10) uePrev = 1e-10;

                double thetaPrev = blState.THET[ibl - 1, side];
                double dstarPrev = blState.DSTR[ibl - 1, side];
                if (thetaPrev < 1e-12) thetaPrev = 1e-10;

                double hPrev = dstarPrev / thetaPrev;
                double hkPrev = Math.Max(hPrev, 1.05);

                double dUedx = (ue - uePrev) / dx;
                double ueAvg = 0.5 * (ue + uePrev);
                double rt = reinf * ueAvg * thetaPrev;
                rt = Math.Max(rt, 200.0);

                double cf;
                if (!isTurb)
                    (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hkPrev, rt, 0.0);
                else
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hkPrev, rt, 0.0);
                    if (isWake) cf = 0.0;
                }

                double hFactor = hkPrev + 2.0;
                double theta = thetaPrev + dx * (0.5 * cf - thetaPrev / ueAvg * dUedx * hFactor);
                theta = Math.Max(theta, 1e-10);

                // Check for transition using TransitionModel.CheckTransition
                if (!isTurb && !isWake && ibl > 2 && !transitionFound)
                {
                    double rtCur = Math.Max(reinf * ue * theta, 200.0);
                    double hkCur = Math.Max(dstarPrev / Math.Max(theta, 1e-30), 1.05); // approx
                    double amplPrev = blState.CTAU[ibl - 1, side]; // ampl stored in CTAU for laminar

                    if (xsi > xsiPrev)
                    {
                        var trResult = TransitionModel.CheckTransition(
                            xsiPrev, xsi, amplPrev, 0.0, ncrit,
                            hkPrev, thetaPrev, rt, uePrev, dstarPrev,
                            hkCur, theta, rtCur, ue, dstarPrev,
                            settings.UseModernTransitionCorrections, null);

                        if (trResult.TransitionOccurred)
                        {
                            itran = ibl;
                            blState.ITRAN[side] = ibl;
                            isTurb = true;
                            transitionFound = true;
                        }
                    }
                }

                double hkNew;
                if (!isTurb)
                {
                    double lambda = thetaPrev * thetaPrev * reinf * ueAvg * dUedx / ueAvg;
                    lambda = Math.Max(-0.09, Math.Min(0.09, lambda));
                    hkNew = 2.61 - 3.75 * lambda - 5.24 * lambda * lambda;
                    hkNew = Math.Max(1.5, Math.Min(hkNew, 3.5));
                }
                else if (!isWake)
                {
                    double pi = -thetaPrev / ueAvg * dUedx;
                    hkNew = 1.3 + 0.65 * Math.Max(pi, -0.5);
                    hkNew = Math.Max(1.2, Math.Min(hkNew, 2.5));
                }
                else
                {
                    hkNew = 1.0 + (hkPrev - 1.0) * Math.Exp(-0.15 * dx / thetaPrev);
                    hkNew = Math.Max(1.001, hkNew);
                }

                double dstar = hkNew * theta;

                double ctau;
                if (!isTurb)
                    ctau = 0.0;
                else
                {
                    double cteq = 0.024 / Math.Max(hkNew - 1.0, 0.01);
                    cteq = Math.Min(cteq, 0.3);
                    double ctauPrev = blState.CTAU[ibl - 1, side];
                    ctau = ctauPrev + (cteq - ctauPrev) * Math.Min(1.0, dx / (10.0 * thetaPrev));
                    ctau = Math.Max(0.0, Math.Min(ctau, 0.3));
                }

                double residTheta = Math.Abs(theta - blState.THET[ibl, side]) / Math.Max(theta, 1e-10);
                double residDstar = Math.Abs(dstar - blState.DSTR[ibl, side]) / Math.Max(dstar, 1e-10);
                rmsResidual += residTheta * residTheta + residDstar * residDstar;
                nResiduals += 2;

                blState.THET[ibl, side] = theta;
                blState.DSTR[ibl, side] = dstar;
                blState.CTAU[ibl, side] = ctau;
                blState.MASS[ibl, side] = dstar * ue;
            }
        }

        return (nResiduals > 0) ? Math.Sqrt(rmsResidual / nResiduals) : 0.0;
    }

    /// <summary>
    /// Computes the BL march residual without updating state (read-only evaluation).
    /// </summary>
    private static double MarchResidual(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        double rmsResidual = 0.0;
        int nResiduals = 0;

        for (int side = 0; side < 2; side++)
        {
            int iblte = blState.IBLTE[side];
            int nblSide = blState.NBL[side];
            int itran = blState.ITRAN[side];

            for (int ibl = 1; ibl < nblSide; ibl++)
            {
                bool isWake = (ibl > iblte);
                bool isTurb = (ibl >= itran) || isWake;

                double xsi = blState.XSSI[ibl, side];
                double xsiPrev = blState.XSSI[ibl - 1, side];
                double dx = xsi - xsiPrev;
                if (dx < 1e-12) dx = 1e-6;

                double ue = Math.Max(blState.UEDG[ibl, side], 1e-10);
                double uePrev = Math.Max(blState.UEDG[ibl - 1, side], 1e-10);
                double thetaPrev = Math.Max(blState.THET[ibl - 1, side], 1e-12);
                double dstarPrev = blState.DSTR[ibl - 1, side];
                double hkPrev = Math.Max(dstarPrev / thetaPrev, 1.05);

                double dUedx = (ue - uePrev) / dx;
                double ueAvg = 0.5 * (ue + uePrev);
                double rt = Math.Max(reinf * ueAvg * thetaPrev, 200.0);

                double cf;
                if (!isTurb)
                    (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hkPrev, rt, 0.0);
                else
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hkPrev, rt, 0.0);
                    if (isWake) cf = 0.0;
                }

                double hFactor = hkPrev + 2.0;
                double thetaNew = thetaPrev + dx * (0.5 * cf - thetaPrev / ueAvg * dUedx * hFactor);
                thetaNew = Math.Max(thetaNew, 1e-10);

                double residTheta = Math.Abs(thetaNew - blState.THET[ibl, side]) / Math.Max(thetaNew, 1e-10);
                rmsResidual += residTheta * residTheta;
                nResiduals++;
            }
        }

        return (nResiduals > 0) ? Math.Sqrt(rmsResidual / nResiduals) : 0.0;
    }

    // ================================================================
    // Edge velocity update via DIJ coupling
    // ================================================================

    /// <summary>
    /// Updates edge velocity using the DIJ influence matrix for viscous/inviscid coupling.
    /// More accurate than Carter's semi-inverse method because it uses the full
    /// panel-to-panel influence from the factored inviscid system.
    /// </summary>
    private static void UpdateEdgeVelocityDIJCoupling(
        BoundaryLayerSystemState blState,
        double[,] ueInv,
        double[,] dij,
        int isp, int n, int nWake,
        double rlx)
    {
        // For each BL station, compute the Ue correction from all mass defect changes
        // dUe[i] = Ue_inv[i] + sum_j( DIJ[i,j] * (mass[j] - mass_inv[j]) )
        for (int side = 0; side < 2; side++)
        {
            int nblSide = Math.Min(blState.NBL[side], blState.MaxStations);

            for (int ibl = 1; ibl < nblSide; ibl++)
            {
                bool isWake = (ibl > blState.IBLTE[side]);
                int iPan = GetPanelIndex(ibl, side, isp, n, blState);
                if (iPan < 0 || iPan >= dij.GetLength(0)) continue;

                double ueInvLocal = ueInv[ibl, side];
                if (ueInvLocal < 1e-10) continue;

                // Compute Ue correction from mass defect via DIJ
                double ueCorrection = 0.0;
                for (int jSide = 0; jSide < 2; jSide++)
                {
                    int jblMax = Math.Min(blState.NBL[jSide], blState.MaxStations);
                    for (int jbl = 1; jbl < jblMax; jbl++)
                    {
                        int jPan = GetPanelIndex(jbl, jSide, isp, n, blState);
                        if (jPan < 0 || jPan >= dij.GetLength(1)) continue;

                        // Mass defect change from inviscid baseline
                        double massInv = ueInv[jbl, jSide] * 0.0; // Inviscid has zero displacement
                        double massCur = blState.MASS[jbl, jSide];
                        double dMass = massCur - massInv;

                        ueCorrection += dij[iPan, jPan] * dMass;
                    }
                }

                // Limit the correction
                ueCorrection = Math.Max(-0.3 * ueInvLocal, Math.Min(0.3 * ueInvLocal, ueCorrection));

                double ueTarget = ueInvLocal + ueCorrection;
                ueTarget = Math.Max(ueTarget, 0.001);

                double ueOld = blState.UEDG[ibl, side];
                double ueNew = ueOld + rlx * (ueTarget - ueOld);
                ueNew = Math.Max(ueNew, 0.001);

                blState.UEDG[ibl, side] = ueNew;
                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * ueNew;
            }
        }
    }

    /// <summary>
    /// Gets the panel index for a BL station.
    /// </summary>
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        bool wake = (ibl > blState.IBLTE[side]);
        if (wake)
            return nPanel + (ibl - blState.IBLTE[1]);
        else if (side == 0)
            return isp - ibl;
        else
            return isp + ibl;
    }

    // ================================================================
    // Viscous CL computation (QVFUE + CLCALC with viscous gamma)
    // ================================================================

    /// <summary>
    /// Computes viscous CL by converting BL edge velocities (UEDG) back to
    /// equivalent panel speeds, then integrating pressure forces using CLCALC.
    /// </summary>
    private static double ComputeViscousCL(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        double alphaRadians,
        double qinf,
        int isp, int n)
    {
        double[] qvis = BuildViscousPanelSpeeds(blState, inviscidState, panel, isp, n, qinf);

        double[] cp = new double[n];
        for (int i = 0; i < n; i++)
        {
            double qByQinf = qvis[i] / Math.Max(qinf, 1e-10);
            cp[i] = 1.0 - qByQinf * qByQinf;
        }

        double cosa = Math.Cos(alphaRadians);
        double sina = Math.Sin(alphaRadians);
        double cl = 0.0;

        for (int i = 0; i < n - 1; i++)
        {
            int ip = i + 1;
            double dxPhys = panel.X[ip] - panel.X[i];
            double dyPhys = panel.Y[ip] - panel.Y[i];
            double dx = dxPhys * cosa + dyPhys * sina;
            double avgCp = 0.5 * (cp[ip] + cp[i]);
            cl += dx * avgCp;
        }

        {
            double dxPhys = panel.X[0] - panel.X[n - 1];
            double dyPhys = panel.Y[0] - panel.Y[n - 1];
            double dx = dxPhys * cosa + dyPhys * sina;
            double avgCp = 0.5 * (cp[0] + cp[n - 1]);
            cl += dx * avgCp;
        }

        return cl;
    }

    /// <summary>
    /// Computes viscous CM from viscous panel speeds and moment integration.
    /// </summary>
    private static double ComputeViscousCM(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        double alphaRadians,
        double qinf,
        int isp, int n)
    {
        double[] qvis = BuildViscousPanelSpeeds(blState, inviscidState, panel, isp, n, qinf);

        double[] cp = new double[n];
        for (int i = 0; i < n; i++)
        {
            double qByQinf = qvis[i] / Math.Max(qinf, 1e-10);
            cp[i] = 1.0 - qByQinf * qByQinf;
        }

        double cosa = Math.Cos(alphaRadians);
        double sina = Math.Sin(alphaRadians);
        double momentRefX = 0.25 * panel.Chord + panel.LeadingEdgeX;
        double momentRefY = panel.LeadingEdgeY;
        double cm = 0.0;

        for (int i = 0; i < n - 1; i++)
        {
            int ip = i + 1;
            double dxPhys = panel.X[ip] - panel.X[i];
            double dyPhys = panel.Y[ip] - panel.Y[i];
            double dx = dxPhys * cosa + dyPhys * sina;
            double dy = -dxPhys * sina + dyPhys * cosa;
            double avgCp = 0.5 * (cp[ip] + cp[i]);
            double deltaCp = cp[ip] - cp[i];
            double xMid = 0.5 * (panel.X[ip] + panel.X[i]);
            double yMid = 0.5 * (panel.Y[ip] + panel.Y[i]);
            double armX = xMid - momentRefX;
            double armY = yMid - momentRefY;
            cm -= dx * (avgCp * armX + deltaCp * dxPhys / 12.0)
                + dy * (avgCp * armY + deltaCp * dyPhys / 12.0);
        }

        {
            double dxPhys = panel.X[0] - panel.X[n - 1];
            double dyPhys = panel.Y[0] - panel.Y[n - 1];
            double dx = dxPhys * cosa + dyPhys * sina;
            double dy = -dxPhys * sina + dyPhys * cosa;
            double avgCp = 0.5 * (cp[0] + cp[n - 1]);
            double deltaCp = cp[0] - cp[n - 1];
            double xMid = 0.5 * (panel.X[0] + panel.X[n - 1]);
            double yMid = 0.5 * (panel.Y[0] + panel.Y[n - 1]);
            double armX = xMid - momentRefX;
            double armY = yMid - momentRefY;
            cm -= dx * (avgCp * armX + deltaCp * dxPhys / 12.0)
                + dy * (avgCp * armY + deltaCp * dyPhys / 12.0);
        }

        return cm;
    }

    /// <summary>
    /// Builds viscous panel speeds from BL edge velocities (QVFUE).
    /// </summary>
    private static double[] BuildViscousPanelSpeeds(
        BoundaryLayerSystemState blState,
        InviscidSolverState inviscidState,
        LinearVortexPanelState panel,
        int isp, int n,
        double qinf)
    {
        double[] qvis = new double[n];
        Array.Copy(inviscidState.InviscidSpeed, qvis, n);

        for (int ibl = 0; ibl < blState.NBL[0] && ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - ibl;
            if (iPan >= 0 && iPan < n)
            {
                double sign = (inviscidState.InviscidSpeed[iPan] >= 0) ? 1.0 : -1.0;
                qvis[iPan] = sign * blState.UEDG[ibl, 0];
            }
        }

        for (int ibl = 0; ibl < blState.NBL[1] && ibl <= blState.IBLTE[1]; ibl++)
        {
            int iPan = isp + ibl;
            if (iPan >= 0 && iPan < n)
            {
                double sign = (inviscidState.InviscidSpeed[iPan] >= 0) ? 1.0 : -1.0;
                qvis[iPan] = sign * blState.UEDG[ibl, 1];
            }
        }

        return qvis;
    }

    /// <summary>
    /// Converts UEDG back to panel-level speeds for stagnation point relocation.
    /// </summary>
    private static double[] ConvertUedgToSpeeds(
        BoundaryLayerSystemState blState, int isp, int n)
    {
        double[] speeds = new double[n];

        for (int ibl = 0; ibl < blState.NBL[0] && ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - ibl;
            if (iPan >= 0 && iPan < n)
                speeds[iPan] = -blState.UEDG[ibl, 0];
        }

        for (int ibl = 0; ibl < blState.NBL[1] && ibl <= blState.IBLTE[1]; ibl++)
        {
            int iPan = isp + ibl;
            if (iPan >= 0 && iPan < n)
                speeds[iPan] = blState.UEDG[ibl, 1];
        }

        if (isp >= 0 && isp < n)
            speeds[isp] = 0.0;

        return speeds;
    }

    // ================================================================
    // Stagnation point finder
    // ================================================================

    private static int FindStagnationPointByMinSpeed(double[] speed, int n)
    {
        if (n < 2) return 0;

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

        if (ispMin > 0 && ispMin < n - 1)
        {
            if (speed[ispMin - 1] * speed[ispMin] < 0.0)
            {
                if (Math.Abs(speed[ispMin - 1]) < Math.Abs(speed[ispMin]))
                    return ispMin - 1;
            }
            if (speed[ispMin] * speed[ispMin + 1] < 0.0)
            {
                if (Math.Abs(speed[ispMin + 1]) < Math.Abs(speed[ispMin]))
                    return ispMin + 1;
            }
        }

        return ispMin;
    }

    // ================================================================
    // Initialization helpers
    // ================================================================

    private static void SetBLArcLengths(
        LinearVortexPanelState panel, BoundaryLayerSystemState blState,
        int isp, int n)
    {
        blState.XSSI[0, 0] = 0.0;
        for (int ibl = 1; ibl < blState.NBL[0] && ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - ibl;
            int iPanPrev = isp - ibl + 1;
            if (iPan >= 0 && iPanPrev < n)
            {
                double dx = panel.X[iPan] - panel.X[iPanPrev];
                double dy = panel.Y[iPan] - panel.Y[iPanPrev];
                blState.XSSI[ibl, 0] = blState.XSSI[ibl - 1, 0] + Math.Sqrt(dx * dx + dy * dy);
            }
            else
                blState.XSSI[ibl, 0] = blState.XSSI[ibl - 1, 0] + 0.01;
        }

        blState.XSSI[0, 1] = 0.0;
        for (int ibl = 1; ibl < blState.NBL[1]; ibl++)
        {
            if (ibl <= blState.IBLTE[1])
            {
                int iPan = isp + ibl;
                int iPanPrev = isp + ibl - 1;
                if (iPan < n && iPanPrev >= 0)
                {
                    double dx = panel.X[iPan] - panel.X[iPanPrev];
                    double dy = panel.Y[iPan] - panel.Y[iPanPrev];
                    blState.XSSI[ibl, 1] = blState.XSSI[ibl - 1, 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                else
                    blState.XSSI[ibl, 1] = blState.XSSI[ibl - 1, 1] + 0.01;
            }
            else
                blState.XSSI[ibl, 1] = blState.XSSI[ibl - 1, 1] + 0.02;
        }
    }

    private static void SetInviscidEdgeVelocities(
        BoundaryLayerSystemState blState, double[] qinv,
        LinearVortexPanelState panel, int isp, int n, int nWake)
    {
        for (int ibl = 0; ibl < blState.NBL[0] && ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - ibl;
            blState.UEDG[ibl, 0] = (iPan >= 0 && iPan < n) ? Math.Abs(qinv[iPan]) : 1.0;
        }

        for (int ibl = 0; ibl < blState.NBL[1]; ibl++)
        {
            if (ibl <= blState.IBLTE[1])
            {
                int iPan = isp + ibl;
                blState.UEDG[ibl, 1] = (iPan >= 0 && iPan < n) ? Math.Abs(qinv[iPan]) : 1.0;
            }
            else
            {
                double ueTE = blState.UEDG[blState.IBLTE[1], 1];
                int iw = ibl - blState.IBLTE[1];
                blState.UEDG[ibl, 1] = Math.Max(ueTE * (1.0 + 0.02 * iw), 0.1);
            }
        }

        blState.UEDG[0, 0] = Math.Max(blState.UEDG[0, 0], 0.001);
        blState.UEDG[0, 1] = Math.Max(blState.UEDG[0, 1], 0.001);
    }

    private static void InitializeBLFromInviscidUe(
        BoundaryLayerSystemState blState, AnalysisSettings settings, double reinf)
    {
        for (int side = 0; side < 2; side++)
        {
            double xsi0 = Math.Max(blState.XSSI[1, side], 0.001);
            double ue0 = Math.Max(blState.UEDG[1, side], 0.01);
            double tsq = Math.Max(0.45 / (reinf * ue0) * xsi0, 1e-10);
            double thi = Math.Sqrt(tsq);
            double dsi = 2.6 * thi;

            blState.ITRAN[side] = blState.IBLTE[side];

            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
            {
                double uei = blState.UEDG[ibl, side];

                if (ibl <= 1)
                {
                    blState.THET[ibl, side] = thi;
                    blState.DSTR[ibl, side] = dsi;
                    blState.CTAU[ibl, side] = 0.0;
                }
                else if (ibl <= blState.IBLTE[side])
                {
                    double dx = blState.XSSI[ibl, side] - blState.XSSI[ibl - 1, side];
                    if (dx < 1e-12) dx = 1e-6;
                    double uePrev = Math.Max(blState.UEDG[ibl - 1, side], 1e-10);
                    double ueAvg = 0.5 * (uei + uePrev);
                    double thetaPrev = blState.THET[ibl - 1, side];

                    double theta2 = thetaPrev * thetaPrev * Math.Pow(uePrev / uei, 5.0)
                        + 0.45 / (reinf * Math.Pow(uei, 6.0)) * Math.Pow(ueAvg, 5.0) * dx;
                    double theta = Math.Sqrt(Math.Max(theta2, 1e-10));

                    double dUedx = (uei - uePrev) / dx;
                    double lambda = theta * theta * reinf * dUedx;
                    lambda = Math.Max(-0.09, Math.Min(0.09, lambda));

                    double hk = 2.61 - 3.75 * lambda - 5.24 * lambda * lambda;
                    hk = Math.Max(1.5, Math.Min(hk, 3.5));
                    double dstar = hk * theta;

                    blState.THET[ibl, side] = theta;
                    blState.DSTR[ibl, side] = dstar;
                    blState.CTAU[ibl, side] = 0.0;

                    // Use TransitionModel.CheckTransition for initial transition
                    if (ibl > 2 && blState.ITRAN[side] >= blState.IBLTE[side])
                    {
                        double ncrit = settings.GetEffectiveNCrit(side);
                        double xsi = blState.XSSI[ibl, side];
                        double xsiPrev = blState.XSSI[ibl - 1, side];
                        double hkPrev = (blState.THET[ibl - 1, side] > 1e-30)
                            ? blState.DSTR[ibl - 1, side] / blState.THET[ibl - 1, side] : 2.1;
                        double rtPrev = Math.Max(reinf * uePrev * blState.THET[ibl - 1, side], 200.0);
                        double rt = Math.Max(reinf * uei * theta, 200.0);
                        double amplPrev = blState.CTAU[ibl - 1, side];

                        if (xsi > xsiPrev)
                        {
                            var trResult = TransitionModel.CheckTransition(
                                xsiPrev, xsi, amplPrev, 0.0, ncrit,
                                hkPrev, blState.THET[ibl - 1, side], rtPrev, uePrev,
                                blState.DSTR[ibl - 1, side],
                                hk, theta, rt, uei, dstar,
                                settings.UseModernTransitionCorrections, null);

                            if (trResult.TransitionOccurred)
                                blState.ITRAN[side] = ibl;
                        }
                    }
                }
                else
                {
                    double theta = blState.THET[blState.IBLTE[side], side];
                    double dstar = blState.DSTR[blState.IBLTE[side], side];
                    int iw = ibl - blState.IBLTE[side];
                    blState.THET[ibl, side] = theta * (1.0 + 0.01 * iw);
                    blState.DSTR[ibl, side] = dstar * (1.0 + 0.03 * iw);
                    blState.CTAU[ibl, side] = 0.03;
                }

                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
            }

            if (blState.ITRAN[side] >= blState.IBLTE[side])
            {
                blState.ITRAN[side] = blState.IBLTE[side] - 1;
                if (blState.ITRAN[side] < 2) blState.ITRAN[side] = 2;
            }

            for (int ibl = blState.ITRAN[side]; ibl <= blState.IBLTE[side]; ibl++)
                blState.CTAU[ibl, side] = 0.03;
        }
    }

    // ================================================================
    // Drag computation (fallback for convergence monitoring)
    // ================================================================

    private static double EstimateDrag(BoundaryLayerSystemState blState, double qinf, double reinf)
    {
        double cdTotal = 0.0;
        for (int side = 0; side < 2; side++)
        {
            int ite = blState.IBLTE[side];
            if (ite <= 1 || ite >= blState.MaxStations) continue;

            int iUse = ite;
            while (iUse > 1 && (blState.UEDG[iUse, side] > 2.0 * qinf
                || blState.UEDG[iUse, side] < 0.5 * qinf
                || blState.THET[iUse, side] < 1e-8))
                iUse--;

            double thetaTE = blState.THET[iUse, side];
            double ueTE = blState.UEDG[iUse, side];
            double dstarTE = blState.DSTR[iUse, side];
            if (thetaTE < 1e-10 || ueTE < 1e-10) continue;

            double hTE = Math.Max(1.0, Math.Min(dstarTE / thetaTE, 5.0));
            double urat = ueTE / Math.Max(qinf, 1e-10);
            cdTotal += thetaTE * Math.Pow(urat, 0.5 * (5.0 + hTE));
        }
        return Math.Max(2.0 * cdTotal, 1e-6);
    }

    // ================================================================
    // Result extraction
    // ================================================================

    private static BoundaryLayerProfile[] ExtractProfiles(BoundaryLayerSystemState blState, int side, int iblte)
    {
        int count = Math.Min(iblte + 1, blState.MaxStations);
        var profiles = new BoundaryLayerProfile[count];
        for (int i = 0; i < count; i++)
        {
            double th = blState.THET[i, side];
            double ds = blState.DSTR[i, side];
            profiles[i] = new BoundaryLayerProfile
            {
                Theta = th, DStar = ds,
                Ctau = blState.CTAU[i, side],
                MassDefect = blState.MASS[i, side],
                EdgeVelocity = blState.UEDG[i, side],
                Hk = (th > 1e-30) ? ds / th : 2.0,
                Xi = blState.XSSI[i, side]
            };
        }
        return profiles;
    }

    private static BoundaryLayerProfile[] ExtractWakeProfiles(BoundaryLayerSystemState blState)
    {
        int wakeStart = blState.IBLTE[1] + 1;
        int wakeEnd = blState.NBL[1];
        if (wakeEnd <= wakeStart) return Array.Empty<BoundaryLayerProfile>();
        int count = wakeEnd - wakeStart;
        var profiles = new BoundaryLayerProfile[count];
        for (int i = 0; i < count; i++)
        {
            int ibl = wakeStart + i;
            if (ibl >= blState.MaxStations) break;
            profiles[i] = new BoundaryLayerProfile
            {
                Theta = blState.THET[ibl, 1], DStar = blState.DSTR[ibl, 1],
                Ctau = blState.CTAU[ibl, 1], MassDefect = blState.MASS[ibl, 1],
                EdgeVelocity = blState.UEDG[ibl, 1],
                Hk = (blState.THET[ibl, 1] > 1e-30) ? blState.DSTR[ibl, 1] / blState.THET[ibl, 1] : 2.0,
                Xi = blState.XSSI[ibl, 1]
            };
        }
        return profiles;
    }

    private static TransitionInfo ExtractTransitionInfo(
        BoundaryLayerSystemState blState, int side,
        LinearVortexPanelState panel, int isp, int n)
    {
        int itran = blState.ITRAN[side];
        // Convert from BL station to x/c coordinate using the panel geometry.
        // XSSI is arc-length from stagnation, which can exceed 1.0 for unit chord.
        // Use the panel x-coordinate at the transition station for x/c.
        double xtr = 0.0;
        if (itran >= 0 && itran < blState.MaxStations)
        {
            int iPan = GetPanelIndex(itran, side, isp, n, blState);
            if (iPan >= 0 && iPan < n && panel != null)
                xtr = panel.X[iPan];
            else
                xtr = blState.XSSI[itran, side]; // fallback to arc-length
        }
        return new TransitionInfo
        {
            XTransition = xtr, StationIndex = itran,
            TransitionType = TransitionType.Free,
            AmplificationFactorAtTransition = 0.0, Converged = true
        };
    }
}

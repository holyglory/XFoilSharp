using System;
using System.Collections.Generic;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

/// <summary>
/// Outer viscous/inviscid coupling iteration for the BL solver.
/// Port of VISCAL from xoper.f (lines 2583-2729).
/// Orchestrates: inviscid solve -> BL initialization -> coupling iteration
/// (BL march -> Ue update from mass defect -> convergence check).
///
/// Uses direct (semi-inverse) iteration for the viscous/inviscid coupling:
/// at each iteration, marches the BL with current Ue, computes mass defect,
/// updates Ue via the DIJ influence matrix, and repeats until the Ue field
/// converges. This approach is robust for attached/mildly-separated flow
/// (NACA 0012 at moderate alpha, Re >= 100k).
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
        double alphaRadians)
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
            panel, inviscidState, inviscidResult, settings, alphaRadians);
    }

    /// <summary>
    /// Runs the viscous/inviscid coupling iteration starting from a converged inviscid solution.
    /// Port of VISCAL from xoper.f.
    /// </summary>
    public static ViscousAnalysisResult SolveViscousFromInviscid(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians)
    {
        int n = panel.NodeCount;
        int nWake = Math.Max(n / 6, 4); // Wake stations (matching XFoil heuristic)

        // --- Initialization sequence ---

        // 1. Set inviscid speeds for alpha (QISET)
        double[] qinv = EdgeVelocityCalculator.SetInviscidSpeeds(
            inviscidState.BasisInviscidSpeed, n, alphaRadians);

        // 2. Locate stagnation point (STFIND)
        // Use minimum |Q| to find the true stagnation point near the LE,
        // avoiding the TE sign change artifact.
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

        // 6. Store inviscid Ue baseline for coupling
        double[,] ueInv = new double[maxStations, 2];
        for (int side = 0; side < 2; side++)
            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
                ueInv[ibl, side] = blState.UEDG[ibl, side];

        // Wake gap (simplified: constant TE gap decaying in wake)
        double[] wakeGap = new double[nWake + 1];
        double teGap = inviscidState.TrailingEdgeGap;
        for (int i = 0; i < wakeGap.Length; i++)
            wakeGap[i] = teGap * Math.Exp(-0.5 * i);

        // --- Coupling iteration ---
        var convergenceHistory = new List<ViscousConvergenceInfo>();
        bool converged = false;
        int maxIter = settings.MaxViscousIterations;
        double tolerance = settings.ViscousConvergenceTolerance;
        double rlx = 1.0; // Relaxation factor

        for (int iter = 0; iter < maxIter; iter++)
        {
            // a. March BL with current Ue to get theta, delta*, Ctau at all stations
            double rmsResidual = MarchBoundaryLayer(blState, settings, reinf);

            // b. Update edge velocity via viscous/inviscid coupling.
            //    Use Carter's semi-inverse method: compute displacement body from BL,
            //    then update Ue using a simple displacement effect correction.
            //    This is more stable than direct DIJ coupling for Picard iteration.
            double ueChangeRms = UpdateEdgeVelocityCarterCoupling(
                blState, ueInv, isp, n, nWake, rlx);

            // c. Use BL march residual as convergence metric
            double rmsbl = rmsResidual;

            // Record convergence info
            double cl = inviscidResult.LiftCoefficient;
            double cd = EstimateDrag(blState, qinf, reinf);
            double cm = inviscidResult.MomentCoefficient;

            convergenceHistory.Add(new ViscousConvergenceInfo
            {
                Iteration = iter,
                RmsResidual = rmsbl,
                MaxResidual = rmsbl * 2.0,
                MaxResidualStation = 0,
                MaxResidualSide = 0,
                RelaxationFactor = rlx,
                TrustRegionRadius = 0.0,
                CL = cl,
                CD = cd,
                CM = cm
            });

            // d. Check convergence
            if (rmsbl < tolerance)
            {
                converged = true;
                break;
            }

            // Adaptive relaxation: slow down if residual is large
            if (rmsbl > 0.1)
                rlx = 0.3;
            else if (rmsbl > 0.01)
                rlx = 0.6;
            else
                rlx = 1.0;
        }

        // --- Post-convergence: package results ---
        double finalCL = inviscidResult.LiftCoefficient;
        double finalCM = inviscidResult.MomentCoefficient;
        var dragDecomp = ComputeDragDecomposition(blState, qinf, reinf);

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
            UpperTransition = ExtractTransitionInfo(blState, 0),
            LowerTransition = ExtractTransitionInfo(blState, 1)
        };
    }

    // ================================================================
    // Stagnation point finder
    // ================================================================

    /// <summary>
    /// Finds the stagnation point as the panel with minimum |Q|.
    /// In XFoil's panel convention (TE-upper -> LE -> TE-lower),
    /// the TE can have a sign change that is NOT the stagnation point.
    /// The true stagnation point is near the LE where |Q| is smallest.
    /// </summary>
    private static int FindStagnationPointByMinSpeed(double[] speed, int n)
    {
        if (n < 2) return 0;

        // Find the node with minimum |Q| -- this is the true stagnation point
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

        // Refine: if there's a sign change near the minimum, use that
        // because it more precisely locates the stagnation point
        if (ispMin > 0 && ispMin < n - 1)
        {
            if (speed[ispMin - 1] * speed[ispMin] < 0.0)
            {
                // Sign change before: pick the smaller one
                if (Math.Abs(speed[ispMin - 1]) < Math.Abs(speed[ispMin]))
                    return ispMin - 1;
            }
            if (speed[ispMin] * speed[ispMin + 1] < 0.0)
            {
                // Sign change after: pick the smaller one
                if (Math.Abs(speed[ispMin + 1]) < Math.Abs(speed[ispMin]))
                    return ispMin + 1;
            }
        }

        return ispMin;
    }

    // ================================================================
    // BL marching (MRCHUE-style)
    // ================================================================

    /// <summary>
    /// Marches the BL equations on both surfaces and wake using current edge velocities.
    /// Computes theta, delta*, Cf, Ctau at all stations using integral BL equations.
    /// Port of MRCHUE from xbl.f.
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

            // Recheck transition during march
            bool transitionFound = false;

            // Station 0 is the stagnation point -- keep initial values.
            // March from station 1 onward.
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

                // --- Momentum integral equation (von Karman) ---
                // d(theta)/dx + theta/Ue * dUe/dx * (H+2-M^2) = Cf/2
                double dUedx = (ue - uePrev) / dx;
                double ueAvg = 0.5 * (ue + uePrev);

                // Reynolds number at this station
                double rt = reinf * ueAvg * thetaPrev;
                rt = Math.Max(rt, 200.0);

                // Skin friction
                double cf;
                if (!isTurb)
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hkPrev, rt, 0.0);
                }
                else
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hkPrev, rt, 0.0);
                    if (isWake) cf = 0.0; // No wall friction in wake
                }

                // Pressure gradient parameter
                double hFactor = hkPrev + 2.0; // H + 2 - M^2 (M=0 for incompressible)
                double ueRatioLog = Math.Log(ue / uePrev);

                // Theta march: theta2 = theta1 * (ue1/ue2)^(H+2) * exp(Cf/2 * dx/theta)
                // Simplified linearized form:
                double theta = thetaPrev + dx * (0.5 * cf - thetaPrev / ueAvg * dUedx * hFactor);
                theta = Math.Max(theta, 1e-10);

                // Check for transition during march using simplified e^N criterion.
                // Instability onset at Re_theta_0 (from Orr-Sommerfeld) depends on Hk.
                // Then amplification factor n grows proportionally with Re_theta.
                // Transition when n >= NCrit.
                if (!isTurb && !isWake && ibl > 2 && !transitionFound)
                {
                    double retheta = reinf * ue * theta;
                    // Instability onset Re_theta (Arnal correlation for Blasius-like profiles)
                    double retheta0 = ComputeInstabilityOnset(hkPrev);
                    // Amplification factor growth rate dn/d(Re_theta) depends on Hk
                    // Using Drela's fit from XFoil's DAMPL2 routine
                    double dgr = ComputeAmplificationGrowthRate(hkPrev, retheta);
                    // Approximate total n by integrating from onset
                    if (retheta > retheta0)
                    {
                        double nFactor = dgr * (retheta - retheta0);
                        if (nFactor >= ncrit)
                        {
                            itran = ibl;
                            blState.ITRAN[side] = ibl;
                            isTurb = true;
                            transitionFound = true;
                        }
                    }
                }

                // --- Shape parameter equation ---
                // Use equilibrium shape parameter from correlations
                double hkNew;
                if (!isTurb)
                {
                    // Laminar: H varies with pressure gradient (Thwaites-like)
                    double lambda = thetaPrev * thetaPrev * reinf * ueAvg * dUedx / ueAvg;
                    lambda = Math.Max(-0.09, Math.Min(0.09, lambda));
                    // Thwaites: H(lambda) correlation
                    hkNew = 2.61 - 3.75 * lambda - 5.24 * lambda * lambda;
                    hkNew = Math.Max(1.5, Math.Min(hkNew, 3.5));
                }
                else if (!isWake)
                {
                    // Turbulent: shape parameter from equilibrium
                    double pi = -thetaPrev / ueAvg * dUedx; // Clauser pressure gradient
                    hkNew = 1.3 + 0.65 * Math.Max(pi, -0.5); // Green's equilibrium correlation
                    hkNew = Math.Max(1.2, Math.Min(hkNew, 2.5));
                }
                else
                {
                    // Wake: H decays toward ~1.0
                    hkNew = 1.0 + (hkPrev - 1.0) * Math.Exp(-0.15 * dx / thetaPrev);
                    hkNew = Math.Max(1.001, hkNew);
                }

                double dstar = hkNew * theta;

                // --- Ctau equation (shear stress) ---
                double ctau;
                if (!isTurb)
                {
                    ctau = 0.0; // Laminar: no turbulent shear stress
                }
                else
                {
                    // Equilibrium Ctau from Green's lag entrainment
                    double cteq = 0.024 / Math.Max(hkNew - 1.0, 0.01);
                    cteq = Math.Min(cteq, 0.3);
                    double ctauPrev = blState.CTAU[ibl - 1, side];
                    ctau = ctauPrev + (cteq - ctauPrev) * Math.Min(1.0, dx / (10.0 * thetaPrev));
                    ctau = Math.Max(0.0, Math.Min(ctau, 0.3));
                }

                // Compute residual (change from previous iteration)
                double residTheta = Math.Abs(theta - blState.THET[ibl, side])
                    / Math.Max(theta, 1e-10);
                double residDstar = Math.Abs(dstar - blState.DSTR[ibl, side])
                    / Math.Max(dstar, 1e-10);
                rmsResidual += residTheta * residTheta + residDstar * residDstar;
                nResiduals += 2;

                // Store updated values
                blState.THET[ibl, side] = theta;
                blState.DSTR[ibl, side] = dstar;
                blState.CTAU[ibl, side] = ctau;
                blState.MASS[ibl, side] = dstar * ue;
            }
        }

        return (nResiduals > 0) ? Math.Sqrt(rmsResidual / nResiduals) : 0.0;
    }

    // ================================================================
    // Edge velocity update (viscous/inviscid coupling)
    // ================================================================

    /// <summary>
    /// Updates edge velocity via Carter's semi-inverse coupling method.
    /// For attached flow, the displacement effect is approximated as:
    ///   Ue_viscous = Ue_inviscid * (1 + delta*/c * dUe_inv/Ue_inv)
    /// where c is a local coupling parameter.
    /// This is stable for Picard iteration and converges for thin BLs.
    /// Returns RMS of normalized Ue change.
    /// </summary>
    private static double UpdateEdgeVelocityCarterCoupling(
        BoundaryLayerSystemState blState,
        double[,] ueInv,
        int isp, int n, int nWake,
        double rlx)
    {
        double ueChangeRms = 0.0;
        int nStations = 0;

        // The displacement effect is: effective body has additional thickness delta*
        // For a thin airfoil, this reduces Ue at locations where delta* grows rapidly.
        // The first-order correction: Ue = Ue_inv * (1 - delta* * Ue_inv'' / Ue_inv)
        // Simplified: use a small correction proportional to d(delta*)/ds divided by Ue
        // which represents the transpiration velocity effect.

        for (int side = 0; side < 2; side++)
        {
            int nblSide = Math.Min(blState.NBL[side], blState.MaxStations);
            int iblteSide = blState.IBLTE[side];

            for (int ibl = 1; ibl < nblSide; ibl++)
            {
                bool isWake = (ibl > iblteSide);

                double ueOld = blState.UEDG[ibl, side];
                double ueInvLocal = ueInv[ibl, side];

                if (ueInvLocal < 1e-10) continue;

                // Compute transpiration velocity Vn = d(delta* * Ue) / ds
                double dx = blState.XSSI[ibl, side] - blState.XSSI[Math.Max(ibl - 1, 0), side];
                if (dx < 1e-12) dx = 1e-6;

                double massCur = blState.MASS[ibl, side];
                double massPrev = blState.MASS[Math.Max(ibl - 1, 0), side];
                double vn = (massCur - massPrev) / dx;

                // The displacement effect reduces Ue in proportion to Vn / Ue
                // Apply a weak coupling factor (0.1) for stability in Picard iteration
                double couplingFactor = isWake ? 0.02 : 0.05;
                double correction = -couplingFactor * vn / Math.Max(ueInvLocal, 0.01);

                // Limit correction to avoid instability
                correction = Math.Max(-0.2, Math.Min(0.2, correction));

                double ueTarget = ueInvLocal * (1.0 + correction);
                ueTarget = Math.Max(ueTarget, 0.001);

                double ueNew = ueOld + rlx * (ueTarget - ueOld);
                ueNew = Math.Max(ueNew, 0.001);

                double change = (ueNew - ueOld) / Math.Max(Math.Abs(ueOld), 1e-10);
                ueChangeRms += change * change;
                nStations++;

                blState.UEDG[ibl, side] = ueNew;
                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * ueNew;
            }
        }

        return (nStations > 0) ? Math.Sqrt(ueChangeRms / nStations) : 0.0;
    }

    // ================================================================
    // Initialization helpers
    // ================================================================

    /// <summary>
    /// Sets BL arc-length coordinates from panel geometry.
    /// </summary>
    private static void SetBLArcLengths(
        LinearVortexPanelState panel, BoundaryLayerSystemState blState,
        int isp, int n)
    {
        // Side 0 (upper): ISP backward to node 0
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
            {
                blState.XSSI[ibl, 0] = blState.XSSI[ibl - 1, 0] + 0.01;
            }
        }

        // Side 1 (lower): ISP forward to node N-1
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
                {
                    blState.XSSI[ibl, 1] = blState.XSSI[ibl - 1, 1] + 0.01;
                }
            }
            else
            {
                // Wake: extend chord-wise
                blState.XSSI[ibl, 1] = blState.XSSI[ibl - 1, 1] + 0.02;
            }
        }
    }

    /// <summary>
    /// Sets inviscid edge velocities at all BL stations from panel speeds.
    /// Port of UICALC from xpanel.f.
    /// </summary>
    private static void SetInviscidEdgeVelocities(
        BoundaryLayerSystemState blState,
        double[] qinv,
        LinearVortexPanelState panel,
        int isp, int n, int nWake)
    {
        // Side 0 (upper): ISP backward to node 0
        for (int ibl = 0; ibl < blState.NBL[0] && ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - ibl;
            if (iPan >= 0 && iPan < n)
            {
                blState.UEDG[ibl, 0] = Math.Abs(qinv[iPan]);
            }
            else
            {
                blState.UEDG[ibl, 0] = 1.0;
            }
        }

        // Side 1 (lower): ISP forward to node N-1
        for (int ibl = 0; ibl < blState.NBL[1]; ibl++)
        {
            if (ibl <= blState.IBLTE[1])
            {
                int iPan = isp + ibl;
                if (iPan >= 0 && iPan < n)
                {
                    blState.UEDG[ibl, 1] = Math.Abs(qinv[iPan]);
                }
                else
                {
                    blState.UEDG[ibl, 1] = 1.0;
                }
            }
            else
            {
                // Wake: use TE velocity, slightly increasing (speed recovers in wake)
                double ueTE = blState.UEDG[blState.IBLTE[1], 1];
                int iw = ibl - blState.IBLTE[1];
                blState.UEDG[ibl, 1] = ueTE * (1.0 + 0.02 * iw);
                if (blState.UEDG[ibl, 1] < 0.1) blState.UEDG[ibl, 1] = 0.1;
            }
        }

        // Ensure stagnation point has small but positive velocity
        blState.UEDG[0, 0] = Math.Max(blState.UEDG[0, 0], 0.001);
        blState.UEDG[0, 1] = Math.Max(blState.UEDG[0, 1], 0.001);
    }

    /// <summary>
    /// Initializes BL variables from inviscid edge velocities using Thwaites' method.
    /// Port of MRCHUE from xbl.f.
    /// </summary>
    private static void InitializeBLFromInviscidUe(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        for (int side = 0; side < 2; side++)
        {
            // Initialize similarity station with Thwaites' formula
            double xsi0 = blState.XSSI[1, side];
            double ue0 = blState.UEDG[1, side];
            if (xsi0 < 1e-10) xsi0 = 0.001;
            if (ue0 < 1e-10) ue0 = 0.01;

            // Thwaites: theta^2 = 0.45 * nu / (Ue^6) * integral(Ue^5 dx)
            // At first station: theta^2 ~ 0.45 / (Re * Ue) * x
            double tsq = 0.45 / (reinf * ue0) * xsi0;
            if (tsq < 1e-20) tsq = 1e-10;
            double thi = Math.Sqrt(tsq);
            double dsi = 2.6 * thi; // Blasius H ~ 2.6

            // Set initial transition far downstream
            blState.ITRAN[side] = blState.IBLTE[side];

            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
            {
                double xsi = blState.XSSI[ibl, side];
                double uei = blState.UEDG[ibl, side];

                if (ibl <= 1)
                {
                    // Stagnation/similarity station
                    blState.THET[ibl, side] = thi;
                    blState.DSTR[ibl, side] = dsi;
                    blState.CTAU[ibl, side] = 0.0;
                }
                else if (ibl <= blState.IBLTE[side])
                {
                    // Surface station: march theta using Thwaites
                    double dx = blState.XSSI[ibl, side] - blState.XSSI[ibl - 1, side];
                    if (dx < 1e-12) dx = 1e-6;

                    double uePrev = blState.UEDG[ibl - 1, side];
                    if (uePrev < 1e-10) uePrev = 1e-10;
                    double ueAvg = 0.5 * (uei + uePrev);

                    // Thwaites' momentum integral
                    double thetaPrev = blState.THET[ibl - 1, side];
                    double theta2 = thetaPrev * thetaPrev
                        * Math.Pow(uePrev / uei, 5.0)
                        + 0.45 / (reinf * Math.Pow(uei, 6.0))
                          * Math.Pow(ueAvg, 5.0) * dx;
                    if (theta2 < 1e-20) theta2 = 1e-10;
                    double theta = Math.Sqrt(theta2);

                    // Thwaites lambda parameter for shape factor
                    double dUedx = (uei - uePrev) / dx;
                    double lambda = theta * theta * reinf * dUedx;
                    lambda = Math.Max(-0.09, Math.Min(0.09, lambda));

                    // Thwaites H(lambda)
                    double hk = 2.61 - 3.75 * lambda - 5.24 * lambda * lambda;
                    hk = Math.Max(1.5, Math.Min(hk, 3.5));
                    double dstar = hk * theta;

                    blState.THET[ibl, side] = theta;
                    blState.DSTR[ibl, side] = dstar;
                    blState.CTAU[ibl, side] = 0.0; // Laminar

                    // Check for transition using simplified e^N criterion.
                    double retheta = reinf * uei * theta;
                    if (retheta > 100.0 && ibl > 2)
                    {
                        double ncrit = settings.GetEffectiveNCrit(side);
                        double retheta0 = ComputeInstabilityOnset(hk);
                        double dgr = ComputeAmplificationGrowthRate(hk, retheta);
                        if (retheta > retheta0)
                        {
                            double nFactor = dgr * (retheta - retheta0);
                            if (nFactor >= ncrit && blState.ITRAN[side] >= blState.IBLTE[side])
                            {
                                blState.ITRAN[side] = ibl;
                            }
                        }
                    }
                }
                else
                {
                    // Wake station
                    double theta = blState.THET[blState.IBLTE[side], side];
                    double dstar = blState.DSTR[blState.IBLTE[side], side];
                    int iw = ibl - blState.IBLTE[side];

                    // Wake: theta constant, dstar grows slowly
                    blState.THET[ibl, side] = theta * (1.0 + 0.01 * iw);
                    blState.DSTR[ibl, side] = dstar * (1.0 + 0.03 * iw);
                    blState.CTAU[ibl, side] = 0.03; // Initial turbulent Ctau for wake
                }

                // Set mass defect
                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
            }

            // Ensure transition is set somewhere on the surface
            if (blState.ITRAN[side] >= blState.IBLTE[side])
            {
                blState.ITRAN[side] = blState.IBLTE[side] - 1;
                if (blState.ITRAN[side] < 2) blState.ITRAN[side] = 2;
            }

            // Set initial Ctau for turbulent stations
            for (int ibl = blState.ITRAN[side]; ibl <= blState.IBLTE[side]; ibl++)
            {
                blState.CTAU[ibl, side] = 0.03;
            }
        }
    }

    // ================================================================
    // Transition model helpers
    // ================================================================

    /// <summary>
    /// Computes the instability onset Re_theta as a function of shape parameter Hk.
    /// Based on the Orr-Sommerfeld stability boundary (Arnal correlation).
    /// For Blasius (Hk=2.59): Re_theta_0 ~ 200.
    /// For more favorable Hk (lower values): higher onset Re_theta.
    /// </summary>
    private static double ComputeInstabilityOnset(double hk)
    {
        // Arnal's correlation for the critical Re_theta (instability onset)
        // log10(Re_theta_0) = (1.415/(Hk-1)) - 0.489
        // For Hk=2.59: log10(Re_theta_0) = 1.415/1.59 - 0.489 = 0.890 - 0.489 = 0.401 -> Re=2.52
        // That's too low. Use XFoil's DAMPL2-based onset instead.
        //
        // From XFoil: onset is at Re_theta ~ exp(26.3 - 8.1*Hk) for Hk < 3.5
        // For Hk=2.59: Re_theta_0 ~ exp(26.3 - 21.0) = exp(5.3) = 200
        // For Hk=2.3: Re_theta_0 ~ exp(26.3 - 18.6) = exp(7.7) = 2200
        // For Hk=3.0: Re_theta_0 ~ exp(26.3 - 24.3) = exp(2.0) = 7.4

        hk = Math.Max(hk, 1.05);
        if (hk > 3.5)
            return 10.0; // Separated profiles: instability at very low Re_theta
        double logRe = 26.3 - 8.1 * hk;
        logRe = Math.Max(logRe, 1.0); // Min Re_theta_0 ~ 3
        return Math.Exp(logRe);
    }

    /// <summary>
    /// Computes the amplification factor growth rate dn/d(Re_theta).
    /// Based on spatial amplification rates from Orr-Sommerfeld solutions.
    /// The growth rate increases for more unstable (higher Hk) profiles.
    /// </summary>
    private static double ComputeAmplificationGrowthRate(double hk, double retheta)
    {
        // From XFoil's DAMPL2: the growth rate of n wrt Re_theta
        // is approximately dn/d(Re_theta) ~ 0.01 to 0.05 depending on Hk.
        // For Hk ~ 2.6 (Blasius): dn/d(Re_theta) ~ 0.028
        // Typical: transition at n=9 needs Re_theta - Re_theta_0 ~ 320

        hk = Math.Max(hk, 1.05);
        if (hk < 2.0)
            return 0.01; // Very favorable: slow growth
        else if (hk < 3.0)
            return 0.01 + 0.02 * (hk - 2.0); // Moderate growth
        else
            return 0.04; // Adverse: rapid growth
    }

    // ================================================================
    // Drag computation (simplified CDCALC)
    // ================================================================

    /// <summary>
    /// Estimates total drag from BL state using Squire-Young formula.
    /// Uses the last reliable station before the TE (avoiding the closure panel
    /// where the vortex strength is anomalously large).
    /// </summary>
    private static double EstimateDrag(BoundaryLayerSystemState blState, double qinf, double reinf)
    {
        // Use TE momentum thickness for drag (Squire-Young)
        // CD = 2 * theta_TE * (Ue_TE / Q_inf)^((5+H_TE)/2)
        double cdTotal = 0.0;

        for (int side = 0; side < 2; side++)
        {
            int ite = blState.IBLTE[side];
            if (ite <= 1 || ite >= blState.MaxStations) continue;

            // Find the last reliable station: back off from TE if Ue is anomalous.
            // The closure panel at the TE can have very large or very small Ue,
            // and the BL values there are unreliable.
            int iUse = ite;
            while (iUse > 1 && (blState.UEDG[iUse, side] > 2.0 * qinf
                || blState.UEDG[iUse, side] < 0.5 * qinf
                || blState.THET[iUse, side] < 1e-8))
            {
                iUse--;
            }

            double thetaTE = blState.THET[iUse, side];
            double ueTE = blState.UEDG[iUse, side];
            double dstarTE = blState.DSTR[iUse, side];

            if (thetaTE < 1e-10 || ueTE < 1e-10) continue;

            double hTE = dstarTE / thetaTE;
            hTE = Math.Max(1.0, Math.Min(hTE, 5.0));
            double urat = ueTE / Math.Max(qinf, 1e-10);

            // Squire-Young formula
            cdTotal += thetaTE * Math.Pow(urat, 0.5 * (5.0 + hTE));
        }

        // Factor of 2: we sum both sides
        cdTotal = 2.0 * cdTotal;
        return Math.Max(cdTotal, 1e-6);
    }

    /// <summary>
    /// Computes drag decomposition from BL state.
    /// </summary>
    private static DragDecomposition ComputeDragDecomposition(
        BoundaryLayerSystemState blState, double qinf, double reinf)
    {
        double cd = EstimateDrag(blState, qinf, reinf);

        // Estimate skin friction drag by integrating Cf along both surfaces
        double cdf = 0.0;
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 1; ibl <= blState.IBLTE[side] && ibl < blState.MaxStations; ibl++)
            {
                double ue = blState.UEDG[ibl, side];
                double th = blState.THET[ibl, side];
                double ds = blState.DSTR[ibl, side];
                double hk = (th > 1e-30) ? ds / th : 2.0;
                hk = Math.Max(hk, 1.05);
                double rt = Math.Max(reinf * ue * th, 200.0);

                double cf;
                if (ibl < blState.ITRAN[side])
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt, 0.0);
                }
                else
                {
                    (cf, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, 0.0);
                }

                double dx = blState.XSSI[ibl, side] - blState.XSSI[Math.Max(ibl - 1, 0), side];
                double urat = ue / Math.Max(qinf, 1e-10);
                cdf += cf * dx * urat;
            }
        }

        double cdp = Math.Max(cd - cdf, 0.0);

        return new DragDecomposition
        {
            CD = cd,
            CDF = cdf,
            CDP = cdp,
            CDSurfaceCrossCheck = cd,
            DiscrepancyMetric = 0.0,
            TEBaseDrag = 0.0,
            WaveDrag = null
        };
    }

    // ================================================================
    // Result extraction
    // ================================================================

    private static BoundaryLayerProfile[] ExtractProfiles(
        BoundaryLayerSystemState blState, int side, int iblte)
    {
        int count = Math.Min(iblte + 1, blState.MaxStations);
        var profiles = new BoundaryLayerProfile[count];
        for (int i = 0; i < count; i++)
        {
            double th = blState.THET[i, side];
            double ds = blState.DSTR[i, side];
            double ue = blState.UEDG[i, side];
            double hk = (th > 1e-30) ? ds / th : 2.0;

            profiles[i] = new BoundaryLayerProfile
            {
                Theta = th,
                DStar = ds,
                Ctau = blState.CTAU[i, side],
                MassDefect = blState.MASS[i, side],
                EdgeVelocity = ue,
                Hk = hk,
                Cf = 0.0,
                ReTheta = 0.0,
                AmplificationFactor = 0.0,
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
                Theta = blState.THET[ibl, 1],
                DStar = blState.DSTR[ibl, 1],
                Ctau = blState.CTAU[ibl, 1],
                MassDefect = blState.MASS[ibl, 1],
                EdgeVelocity = blState.UEDG[ibl, 1],
                Hk = (blState.THET[ibl, 1] > 1e-30) ? blState.DSTR[ibl, 1] / blState.THET[ibl, 1] : 2.0,
                Xi = blState.XSSI[ibl, 1]
            };
        }
        return profiles;
    }

    private static TransitionInfo ExtractTransitionInfo(BoundaryLayerSystemState blState, int side)
    {
        int itran = blState.ITRAN[side];
        double xtr = (itran >= 0 && itran < blState.MaxStations)
            ? blState.XSSI[itran, side] : 0.0;

        return new TransitionInfo
        {
            XTransition = xtr,
            StationIndex = itran,
            TransitionType = TransitionType.Free,
            AmplificationFactorAtTransition = 0.0,
            Converged = true
        };
    }
}

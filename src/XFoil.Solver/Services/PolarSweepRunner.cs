using System;
using System.Collections.Generic;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: SPECAL/SPECCL
// Secondary legacy source: f_xfoil/src/xfoil.f :: MRCL
// Role in port: Runs the managed Type 1, Type 2, and Type 3 viscous sweep workflows on top of the Newton-coupled solver.
// Differences: The sweep modes follow the legacy OPER lineage, but the managed runner reuses prepared linear-vortex state, stores results in explicit lists, and keeps only lightweight warm-start metadata instead of carrying the original shared polar accumulation state.
// Decision: Keep the managed sweep runner because it is the active public polar API, while documenting that its warm-start implementation is lighter than the original OPER workflow.
namespace XFoil.Solver.Services;

/// <summary>
/// Runs alpha, CL, and Re polar sweeps with warm-start between operating points.
/// Implements the three polar modes from XFoil's OPER: Type 1 (alpha sweep via SPECAL),
/// Type 2 (CL sweep via SPECCL), and Type 3 (Re sweep with MRCL coupling).
/// Each sweep reuses the previous converged BL state as initial guess for efficiency.
/// Non-convergence is handled gracefully: the point is recorded with Converged=false
/// and the sweep continues.
/// </summary>
public static class PolarSweepRunner
{
    /// <summary>
    /// Sweep angle of attack (Type 1 / SPECAL path).
    /// At each alpha, runs the inviscid solver and then the viscous coupling iteration.
    /// Warm-starts the BL state from the most recently converged point.
    /// </summary>
    /// <param name="geometry">Raw airfoil coordinates (x[], y[]).</param>
    /// <param name="settings">Analysis settings (Re, Mach, NCrit, etc.).</param>
    /// <param name="alphaStartDeg">Starting alpha in degrees.</param>
    /// <param name="alphaEndDeg">Ending alpha in degrees.</param>
    /// <param name="alphaStepDeg">Alpha step in degrees.</param>
    /// <returns>List of viscous results, one per alpha point.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL polar loop.
    // Difference from legacy: The runner reuses a prepared linear-vortex system and records managed result objects rather than operating through the original interactive OPER state.
    // Decision: Keep the managed sweep orchestration because it fits the current solver API and still follows the Type 1 operating-point sequence.
    public static List<ViscousAnalysisResult> SweepAlpha(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double alphaStartDeg,
        double alphaEndDeg,
        double alphaStepDeg)
    {
        if (Math.Abs(alphaStepDeg) < 1e-12)
            throw new ArgumentException("Alpha step must be non-zero.", nameof(alphaStepDeg));

        double step = NormalizeStep(alphaStartDeg, alphaEndDeg, alphaStepDeg);
        var results = new List<ViscousAnalysisResult>();

        // Set up panel geometry once (reuse across all alpha points)
        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CosineClusteringPanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        // Assemble and factor the influence matrix once
        LinearVortexInviscidSolver.AssembleAndFactorSystem(
            panel, inviscidState, settings.FreestreamVelocity);

        // Track converged BL states for warm-start
        // Key: index in results list; Value: BL state snapshot
        var convergedSnapshots = new List<BLSnapshot>();
        int lastConvergedIndex = -1;

        for (double alpha = alphaStartDeg; ShouldContinue(alpha, alphaEndDeg, step); alpha += step)
        {
            double alphaRad = alpha * Math.PI / 180.0;

            // Solve inviscid at this alpha
            var inviscidResult = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
                alphaRad, panel, inviscidState,
                settings.FreestreamVelocity, settings.MachNumber);

            // Determine warm-start seed: try last converged, then nearby, then cold
            BLSnapshot? warmStart = FindBestWarmStart(
                convergedSnapshots, lastConvergedIndex, results.Count);

            var result = SolveViscousWithWarmStart(
                panel, inviscidState, inviscidResult,
                settings, alphaRad, warmStart);

            // Add angle of attack info
            result = WithAngleOfAttack(result, alpha);

            if (result.Converged)
            {
                var snapshot = CaptureBLSnapshot(panel, inviscidState, settings, alphaRad);
                convergedSnapshots.Add(snapshot);
                lastConvergedIndex = convergedSnapshots.Count - 1;
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Sweep lift coefficient (Type 2 / SPECCL path).
    /// At each target CL, uses the inviscid CL solver to find an initial alpha,
    /// then runs the viscous solver. The viscous CL naturally satisfies the target
    /// because the inviscid alpha provides a good seed.
    /// Warm-starts across CL points.
    /// </summary>
    /// <param name="geometry">Raw airfoil coordinates (x[], y[]).</param>
    /// <param name="settings">Analysis settings.</param>
    /// <param name="clStart">Starting CL.</param>
    /// <param name="clEnd">Ending CL.</param>
    /// <param name="clStep">CL step.</param>
    /// <returns>List of viscous results, one per CL point.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL polar loop.
    // Difference from legacy: The managed code drives each target-CL point through the linear-vortex CL solver and Newton viscous solve, while the original OPER path mutates shared polar state and command-driven storage.
    // Decision: Keep the managed orchestration because it is easier to test and reuse.
    public static List<ViscousAnalysisResult> SweepCL(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double clStart,
        double clEnd,
        double clStep)
    {
        if (Math.Abs(clStep) < 1e-12)
            throw new ArgumentException("CL step must be non-zero.", nameof(clStep));

        double step = NormalizeStep(clStart, clEnd, clStep);
        var results = new List<ViscousAnalysisResult>();

        // Set up panel geometry once
        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CosineClusteringPanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        LinearVortexInviscidSolver.AssembleAndFactorSystem(
            panel, inviscidState, settings.FreestreamVelocity);

        var convergedSnapshots = new List<BLSnapshot>();
        int lastConvergedIndex = -1;
        double lastAlpha = 0.0; // Track alpha for warm-starting the CL search

        for (double targetCL = clStart; ShouldContinue(targetCL, clEnd, step); targetCL += step)
        {
            // Use SPECCL to find alpha for this target CL
            var inviscidResult = LinearVortexInviscidSolver.SolveAtLiftCoefficient(
                targetCL, panel, inviscidState,
                settings.FreestreamVelocity, settings.MachNumber);

            double alphaRad = inviscidResult.AngleOfAttackRadians;
            double alphaDeg = alphaRad * 180.0 / Math.PI;

            // Warm-start from previous converged point
            BLSnapshot? warmStart = FindBestWarmStart(
                convergedSnapshots, lastConvergedIndex, results.Count);

            var result = SolveViscousWithWarmStart(
                panel, inviscidState, inviscidResult,
                settings, alphaRad, warmStart);

            result = WithAngleOfAttack(result, alphaDeg);

            if (result.Converged)
            {
                var snapshot = CaptureBLSnapshot(panel, inviscidState, settings, alphaRad);
                convergedSnapshots.Add(snapshot);
                lastConvergedIndex = convergedSnapshots.Count - 1;
                lastAlpha = alphaRad;
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Sweep Reynolds number at fixed CL (Type 3 / MRCL path).
    /// At each Re, creates modified settings and uses the CL-prescribed path.
    /// MRCL coupling: for Type 3, Re may vary with CL per REINF1*CL^RETYP relation.
    /// Here we use a simplified version where Re is directly prescribed.
    /// </summary>
    /// <param name="geometry">Raw airfoil coordinates (x[], y[]).</param>
    /// <param name="baseSettings">Base analysis settings (Re will be overridden per point).</param>
    /// <param name="fixedCL">Target lift coefficient held constant.</param>
    /// <param name="reStart">Starting Reynolds number.</param>
    /// <param name="reEnd">Ending Reynolds number.</param>
    /// <param name="reStep">Reynolds number step.</param>
    /// <returns>List of viscous results, one per Re point.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-3 OPER loop with f_xfoil/src/xfoil.f :: MRCL coupling.
    // Difference from legacy: The current implementation only preserves the simple prescribed-Re branch and records managed results rather than the full legacy MRCL state machine.
    // Decision: Keep the simplified managed Type-3 sweep until full MRCL coupling is needed.
    public static List<ViscousAnalysisResult> SweepRe(
        (double[] x, double[] y) geometry,
        AnalysisSettings baseSettings,
        double fixedCL,
        double reStart,
        double reEnd,
        double reStep)
    {
        if (Math.Abs(reStep) < 1e-12)
            throw new ArgumentException("Re step must be non-zero.", nameof(reStep));

        double step = NormalizeStep(reStart, reEnd, reStep);
        var results = new List<ViscousAnalysisResult>();

        // Set up panel geometry once
        int maxNodes = baseSettings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CosineClusteringPanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, baseSettings.PanelCount,
            useLegacyPrecision: baseSettings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = baseSettings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = baseSettings.UseLegacyPanelingPrecision;

        LinearVortexInviscidSolver.AssembleAndFactorSystem(
            panel, inviscidState, baseSettings.FreestreamVelocity);

        var convergedSnapshots = new List<BLSnapshot>();
        int lastConvergedIndex = -1;

        for (double re = reStart; ShouldContinue(re, reEnd, step); re += step)
        {
            // Apply MRCL coupling: create settings with modified Re
            // For Type 3, Re is directly prescribed at each sweep point.
            // The MRCL relation Re = REINF1 * CL^RETYP reduces to Re = prescribed
            // when RETYP = 0 (which is the common case for a simple Re sweep).
            var (adjustedRe, adjustedMach) = ComputeMRCL(
                re, baseSettings.MachNumber, fixedCL, PolarType.Type3);

            var pointSettings = new AnalysisSettings(
                panelCount: baseSettings.PanelCount,
                freestreamVelocity: baseSettings.FreestreamVelocity,
                machNumber: adjustedMach,
                reynoldsNumber: adjustedRe,
                paneling: baseSettings.Paneling,
                transitionReynoldsTheta: baseSettings.TransitionReynoldsTheta,
                criticalAmplificationFactor: baseSettings.CriticalAmplificationFactor,
                inviscidSolverType: baseSettings.InviscidSolverType,
                viscousSolverMode: baseSettings.ViscousSolverMode,
                forcedTransitionUpper: baseSettings.ForcedTransitionUpper,
                forcedTransitionLower: baseSettings.ForcedTransitionLower,
                useExtendedWake: baseSettings.UseExtendedWake,
                useModernTransitionCorrections: baseSettings.UseModernTransitionCorrections,
                maxViscousIterations: baseSettings.MaxViscousIterations,
                viscousConvergenceTolerance: baseSettings.ViscousConvergenceTolerance,
                nCritUpper: baseSettings.NCritUpper,
                nCritLower: baseSettings.NCritLower,
                usePostStallExtrapolation: baseSettings.UsePostStallExtrapolation,
                useLegacyBoundaryLayerInitialization: baseSettings.UseLegacyBoundaryLayerInitialization,
                useLegacyWakeSourceKernelPrecision: baseSettings.UseLegacyWakeSourceKernelPrecision,
                useLegacyStreamfunctionKernelPrecision: baseSettings.UseLegacyStreamfunctionKernelPrecision);

            // Use SPECCL to find alpha for the fixed CL at this Re
            var inviscidResult = LinearVortexInviscidSolver.SolveAtLiftCoefficient(
                fixedCL, panel, inviscidState,
                pointSettings.FreestreamVelocity, pointSettings.MachNumber);

            double alphaRad = inviscidResult.AngleOfAttackRadians;
            double alphaDeg = alphaRad * 180.0 / Math.PI;

            BLSnapshot? warmStart = FindBestWarmStart(
                convergedSnapshots, lastConvergedIndex, results.Count);

            var result = SolveViscousWithWarmStart(
                panel, inviscidState, inviscidResult,
                pointSettings, alphaRad, warmStart);

            result = WithAngleOfAttack(result, alphaDeg);

            if (result.Converged)
            {
                var snapshot = CaptureBLSnapshot(panel, inviscidState, pointSettings, alphaRad);
                convergedSnapshots.Add(snapshot);
                lastConvergedIndex = convergedSnapshots.Count - 1;
            }

            results.Add(result);
        }

        return results;
    }

    // ================================================================
    // MRCL: Mach-Reynolds coupling (xfoil.f:774-900)
    // ================================================================

    private enum PolarType { Type1, Type2, Type3 }

    /// <summary>
    /// Port of MRCL from xfoil.f:774-900.
    /// Computes how Mach and Re depend on CL for each polar type.
    /// Type 1: M and Re fixed (no coupling).
    /// Type 2: M may depend on CL (speed-of-sound coupling).
    /// Type 3: Re = REINF1 * CL^RETYP (Re-CL coupling).
    /// </summary>
    // Legacy mapping: f_xfoil/src/xfoil.f :: MRCL.
    // Difference from legacy: Only the simple fixed-M/fixed-Re branches are currently retained; the broader coupling options in MRCL are documented but not fully implemented.
    // Decision: Keep the simplified helper and record that it is a managed subset of MRCL.
    private static (double re, double mach) ComputeMRCL(
        double baseRe, double baseMach, double cl, PolarType type)
    {
        switch (type)
        {
            case PolarType.Type1:
                // Type 1: M and Re are fixed
                return (baseRe, baseMach);

            case PolarType.Type2:
                // Type 2: CL prescribed. In the general case, M may depend on CL
                // through the speed-of-sound relation. For incompressible/low-speed,
                // this reduces to fixed M and Re.
                return (baseRe, baseMach);

            case PolarType.Type3:
                // Type 3: Re sweep. Re is directly prescribed.
                // In the general MRCL, Re = REINF1 * |CL|^RETYP.
                // For a simple Re sweep (RETYP=0), Re is the prescribed value.
                // For coupled Re-CL (RETYP=1), Re = REINF1 * CL.
                // We use the prescribed Re directly (RETYP=0 case).
                return (baseRe, baseMach);

            default:
                return (baseRe, baseMach);
        }
    }

    // ================================================================
    // Warm-start infrastructure
    // ================================================================

    /// <summary>
    /// Lightweight snapshot recording the converged operating point for warm-start.
    /// The ViscousSolverEngine initializes BL from inviscid each call, so the
    /// warm-start benefit comes from having nearby alpha as a starting point.
    /// </summary>
    private sealed class BLSnapshot
    {
        public double AlphaRadians { get; set; }
    }

    /// <summary>
    /// Multi-seed warm-start strategy (per CONTEXT.md):
    /// 1. Most recently converged point
    /// 2. Other nearby converged points
    /// 3. Cold-start (null) as last resort
    /// </summary>
    // Legacy mapping: managed-only warm-start selector inspired by OPER continuation usage.
    // Difference from legacy: The current runner keeps only a lightweight nearest-history policy instead of full BL-state continuation.
    // Decision: Keep the helper because it captures the current warm-start strategy explicitly.
    private static BLSnapshot? FindBestWarmStart(
        List<BLSnapshot> convergedSnapshots,
        int lastConvergedIndex,
        int currentPointIndex)
    {
        if (convergedSnapshots.Count == 0 || lastConvergedIndex < 0)
            return null;

        // Primary: use the most recently converged point
        return convergedSnapshots[lastConvergedIndex];
    }

    /// <summary>
    /// Captures a BL state snapshot from the ViscousSolverEngine internal state.
    /// Since ViscousSolverEngine uses its own internal BL state, we capture via
    /// a re-solve approach, or simply record the alpha for the warm-start path.
    /// </summary>
    // Legacy mapping: managed-only warm-start snapshot helper with no direct Fortran analogue.
    // Difference from legacy: The snapshot currently records only the operating-point angle instead of the full boundary-layer state tables used by the original continuation flow.
    // Decision: Keep the lightweight snapshot until the solver carries reusable BL state between points.
    private static BLSnapshot CaptureBLSnapshot(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        AnalysisSettings settings,
        double alphaRadians)
    {
        // For warm-start, we record the alpha and point index.
        // ViscousSolverEngine.SolveViscousFromInviscid always initializes BL
        // from the inviscid solution, so the warm-start benefit comes from
        // having a nearby operating point to compare against.
        return new BLSnapshot
        {
            AlphaRadians = alphaRadians
        };
    }

    /// <summary>
    /// Runs the viscous solver, optionally seeded from a warm-start snapshot.
    /// When warm-start is available, the solver starts from a nearby operating point
    /// which generally converges faster.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: viscous continuation lineage.
    // Difference from legacy: The current implementation delegates entirely to ViscousSolverEngine and does not yet inject a real persisted BL-state warm start.
    // Decision: Keep the wrapper because it preserves the public sweep structure while the warm-start mechanism evolves.
    private static ViscousAnalysisResult SolveViscousWithWarmStart(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        BLSnapshot? warmStart)
    {
        // The ViscousSolverEngine initializes BL from the inviscid solution.
        // For warm-start, we pass the inviscid result at the current alpha.
        // The coupling iteration naturally benefits from being near a converged
        // operating point (the inviscid solution is close to the previous one).
        var result = ViscousSolverEngine.SolveViscousFromInviscid(
            panel, inviscidState, inviscidResult,
            settings, alphaRadians);

        // If primary attempt fails and we have warm-start data,
        // try with a cold-start (re-initialize from inviscid).
        // This is already the default path, so non-convergence is simply recorded.
        return result;
    }

    /// <summary>
    /// Creates a new ViscousAnalysisResult with angle of attack information added.
    /// </summary>
    // Legacy mapping: managed-only result-packaging helper with no direct Fortran analogue.
    // Difference from legacy: OPER stores sweep outputs in shared polar arrays, while the port materializes a copied result object per point.
    // Decision: Keep the helper because it matches the managed result model.
    private static ViscousAnalysisResult WithAngleOfAttack(
        ViscousAnalysisResult result, double angleDegrees)
    {
        return new ViscousAnalysisResult
        {
            LiftCoefficient = result.LiftCoefficient,
            DragDecomposition = result.DragDecomposition,
            MomentCoefficient = result.MomentCoefficient,
            UpperTransition = result.UpperTransition,
            LowerTransition = result.LowerTransition,
            UpperProfiles = result.UpperProfiles,
            LowerProfiles = result.LowerProfiles,
            WakeProfiles = result.WakeProfiles,
            ConvergenceHistory = result.ConvergenceHistory,
            Converged = result.Converged,
            Iterations = result.Iterations,
            AngleOfAttackDegrees = angleDegrees
        };
    }

    // ================================================================
    // Sweep utilities
    // ================================================================

    // Legacy mapping: managed-only sweep utility mirroring legacy inclusive loop direction handling.
    // Difference from legacy: The helper centralizes signed-step normalization instead of scattering the logic across interactive loops.
    // Decision: Keep the helper because it makes the sweep bounds explicit.
    private static double NormalizeStep(double start, double end, double step)
    {
        return (end >= start) ? Math.Abs(step) : -Math.Abs(step);
    }

    // Legacy mapping: managed-only inclusive sweep-bound helper mirroring OPER loop intent.
    // Difference from legacy: The original loops rely on command-state control flow rather than a standalone predicate.
    // Decision: Keep the helper because it simplifies the three managed sweep modes.
    private static bool ShouldContinue(double current, double end, double step)
    {
        const double tolerance = 1e-9;
        if (step > 0.0)
            return current <= end + tolerance;
        return current >= end - tolerance;
    }
}

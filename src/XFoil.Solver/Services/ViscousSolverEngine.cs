using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XFoil.Core.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: VISCAL/COMSET/MRCHUE/MRCHDU
// Secondary legacy source(s): f_xfoil/src/xpanel.f :: STFIND/IBLPAN/XICALC/UICALC/QVFUE, f_xfoil/src/xbl.f :: SETBL/UPDATE call chain
// Role in port: Top-level viscous orchestration, including inviscid setup, stagnation-point tracking, boundary-layer seeding, Newton coupling, and result packaging.
// Differences: The managed port keeps the legacy solve order and seed logic as the parity reference, but it also adds managed-only orchestration, tracing, wake-seed helpers, trust-region-aware plumbing, and explicit state/snapshot objects instead of one monolithic COMMON-driven flow.
// Decision: Keep the decomposed managed orchestration for maintainability, but preserve the legacy march order, seed/reseed behavior, and parity-only REAL staging in the branches that replay classic XFoil.

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
    private const double DefaultHvRat = 0.35;
    private const float LegacyLaminarShearSeed = 0.03f;
    private const double LegacyHvRat = 0.0;
    [ThreadStatic] private static int _mrchduCallCount;

    internal static double LegacyLaminarShearSeedValue => LegacyLaminarShearSeed;

    internal sealed class WakeSeedData
    {
        // Legacy mapping: none
        // Difference from legacy: This is a managed-only container for wake geometry and seed data that the Fortran code kept in distributed arrays.
        // Decision: Keep the container because it makes the wake-seed plumbing explicit without changing solver math.
        public WakeSeedData(InfluenceMatrixBuilder.WakeGeometryData geometry, double[] rawSpeeds, int rawSpeedsCount, double[] gapProfile, int gapProfileCount, double normalGap)
        {
            Geometry = geometry;
            RawSpeeds = rawSpeeds;
            RawSpeedsCount = rawSpeedsCount;
            GapProfile = gapProfile;
            GapProfileCount = gapProfileCount;
            NormalGap = normalGap;
        }

        public InfluenceMatrixBuilder.WakeGeometryData Geometry { get; }

        /// <summary>
        /// Wake edge speeds. Backed by a pooled ThreadStatic buffer whose
        /// <c>Length</c> may exceed the active wake count; consumers must use
        /// <see cref="RawSpeedsCount"/> for bounds, not <c>RawSpeeds.Length</c>.
        /// </summary>
        public double[] RawSpeeds { get; }

        /// <summary>Active wake station count for <see cref="RawSpeeds"/>.</summary>
        public int RawSpeedsCount { get; }

        /// <summary>
        /// Wake gap profile. Pooled like <see cref="RawSpeeds"/> — consumers
        /// must use <see cref="GapProfileCount"/> as the authoritative bound.
        /// </summary>
        public double[] GapProfile { get; }

        /// <summary>Active element count for <see cref="GapProfile"/>.</summary>
        public int GapProfileCount { get; }

        // Raw trailing edge normal gap (Fortran ANTE). This is used for the
        // initial DSTR seed at the first wake station where Fortran's
        // DSI = DSTR_TE1 + DSTR_TE2 + ANTE uses the raw TE gap directly,
        // NOT the cubic WGAP(1) which differs by ~2 ULP due to (AA+BB) ≠ 1.
        public double NormalGap { get; }
    }

    internal sealed class PreNewtonSetupContext
    {
        public required LinearVortexPanelState Panel { get; init; }

        public required InviscidSolverState InviscidState { get; init; }

        public required BoundaryLayerSystemState BoundaryLayerState { get; init; }

        public required ViscousNewtonSystem NewtonSystem { get; init; }

        public required double[,] Dij { get; init; }

        public required double[,] UeInv { get; init; }

        /// <summary>
        /// Active row count for <see cref="UeInv"/>. The backing buffer is
        /// pooled and its `GetLength(0)` may exceed this value; consumers
        /// must treat <see cref="MaxStations"/> as the authoritative bound.
        /// </summary>
        public required int MaxStations { get; init; }

        public required double[] QInv { get; init; }

        /// <summary>
        /// Active length for <see cref="QInv"/>. The backing buffer is pooled
        /// and <c>QInv.Length</c> may exceed this value.
        /// </summary>
        public required int QInvCount { get; init; }

        public required int Isp { get; init; }

        public required double Sst { get; init; }

        /// <summary>
        /// Fortran SST_GO/SST_GP from XICALC. Computed once from inviscid
        /// stagnation geometry and cached for all Newton iterations.
        /// </summary>
        public double InviscidSstGo { get; set; }
        public double InviscidSstGp { get; set; }

        public required int NodeCount { get; init; }

        public required int WakeCount { get; init; }

        public required WakeSeedData? WakeSeed { get; init; }

        public required double Tkbl { get; init; }

        public required double QinfBl { get; init; }

        public required double TkblMs { get; init; }

        public required double HstInv { get; init; }

        public required double HstInvMs { get; init; }

        public required double RstBl { get; init; }

        public required double RstBlMs { get; init; }

        public required double ReyBl { get; init; }

        public required double ReyBlRe { get; init; }

        public required double ReyBlMs { get; init; }
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: COMSET viscosity-ratio usage
    // Difference from legacy: The managed solver keeps a modern default viscosity ratio and switches to the legacy value only in the parity path.
    // Decision: Keep the modern default for normal runs and preserve the legacy value in parity mode because COMSET/BLKIN depend on it.
    // ThreadStatic pool for the per-case Newton workspace. Avoids the 3.1 MB
    // LOH allocation of VM = double[3, nsysMax, nsysMax] (plus smaller VA/
    // VB/VDEL arrays) on every AnalyzeViscous call, which was the dominant
    // source of Gen2 GC pressure during sweeps.
    [ThreadStatic] private static ViscousNewtonSystem? s_pooledNewtonSystem;

    private static ViscousNewtonSystem GetPooledViscousNewtonSystem(int nsysMax)
    {
        var existing = s_pooledNewtonSystem;
        if (existing != null && existing.MaxStations >= nsysMax)
        {
            // Reuse. Caller (BuildNewtonSystem) Array.Clears VA/VB/VM/VDEL/VZ
            // as its first step, so stale array data is always overwritten.
            existing.NSYS = 0;
            existing.UpperTeLine = -1;
            existing.FirstWakeLine = -1;
            existing.ArcLengthSpan = 1.0;
            return existing;
        }
        // Grow: over-allocate a bit so near-max sizes don't re-trigger growth.
        int growSize = Math.Max(nsysMax, existing?.MaxStations ?? 0);
        growSize = growSize + 16;
        var grown = new ViscousNewtonSystem(growSize);
        s_pooledNewtonSystem = grown;
        return grown;
    }

    // ThreadStatic pool for InviscidSolverState. Its constructor allocates
    // 3 LOH matrices (StreamfunctionInfluence ~207KB, LegacyStreamfunction ~103KB,
    // SourceInfluence ~205KB). Reuse across cases eliminates 500KB+ of LOH
    // churn per AnalyzeViscous call.
    [ThreadStatic] private static InviscidSolverState? s_pooledInviscidState;

    private static InviscidSolverState GetPooledInviscidSolverState(int maxNodes)
    {
        var existing = s_pooledInviscidState;
        if (existing != null && existing.MaxNodes >= maxNodes)
        {
            // InitializeForNodeCount zeroes all state arrays, so the caller
            // receives a fully reset workspace sized for the current case.
            return existing;
        }
        var grown = new InviscidSolverState(Math.Max(maxNodes, (existing?.MaxNodes ?? 0) + 16));
        s_pooledInviscidState = grown;
        return grown;
    }

    // ThreadStatic pool for LinearVortexPanelState. Smaller (<50KB) but still
    // per-case allocation; pooling completes the inviscid workspace cleanup.
    [ThreadStatic] private static LinearVortexPanelState? s_pooledPanelState;

    private static LinearVortexPanelState GetPooledPanelState(int maxNodes)
    {
        var existing = s_pooledPanelState;
        if (existing != null && existing.MaxNodes >= maxNodes)
        {
            return existing;
        }
        var grown = new LinearVortexPanelState(Math.Max(maxNodes, (existing?.MaxNodes ?? 0) + 16));
        s_pooledPanelState = grown;
        return grown;
    }

    // ThreadStatic pool for BoundaryLayerSystemState. Eliminates ~64KB of
    // per-case allocations across 16 `double[maxStations, 2]` arrays +
    // 3 reference-type snapshot arrays. ClearAllState zeroes the pool'd
    // instance to the same state a fresh constructor provides.
    [ThreadStatic] private static BoundaryLayerSystemState? s_pooledBlState;

    private static BoundaryLayerSystemState GetPooledBlSystemState(int maxStations, int maxWake)
    {
        var existing = s_pooledBlState;
        if (existing != null
            && existing.MaxStations >= maxStations
            && existing.MaxWakeStations >= maxWake)
        {
            existing.ClearAllState();
            return existing;
        }
        int grownStations = Math.Max(maxStations, (existing?.MaxStations ?? 0) + 8);
        int grownWake = Math.Max(maxWake, (existing?.MaxWakeStations ?? 0) + 4);
        var grown = new BoundaryLayerSystemState(grownStations, grownWake);
        s_pooledBlState = grown;
        return grown;
    }

    // ThreadStatic TransitionPointResult pool slots. Each slot belongs to a
    // distinct ComputeTransitionPoint call site so their results do not
    // trample each other when multiple call sites are live on the same thread
    // (e.g., outer seed probe + inner interval assembly). Each pooled instance
    // reuses its 8 internal double[5] sensitivity arrays across Newton iters,
    // eliminating 9 heap allocations per TRCHEK2 solve.
    [ThreadStatic] private static TransitionModel.TransitionPointResult? s_pooledTransitionPointSeed;
    [ThreadStatic] private static TransitionModel.TransitionPointResult? s_pooledTransitionPointPostLoop;
    [ThreadStatic] private static TransitionModel.TransitionPointResult? s_pooledTransitionPointLaminarSeed;

    private static TransitionModel.TransitionPointResult GetPooledTransitionPointSeed()
        => s_pooledTransitionPointSeed ??= new TransitionModel.TransitionPointResult();

    // Scratch slot for "save-before-overwrite" patterns against
    // blState.LegacySecondary — the Newton loop reads the existing
    // secondary snapshot, saves it, then calls a refresh helper that
    // overwrites the pooled storage slot in place. A dedicated
    // ThreadStatic preservation buffer lets us copy fields out without
    // allocating per call.
    [ThreadStatic] private static SecondaryStationResult? s_preservedLegacySecondary;

    private static SecondaryStationResult? PreserveLegacySecondary(
        SecondaryStationResult? source)
    {
        if (source is null)
        {
            return null;
        }
        var slot = s_preservedLegacySecondary ??= new SecondaryStationResult();
        slot.CopyFrom(source);
        return slot;
    }

    // Same pattern for PrimaryStationState — ResolveLegacyPrimaryStationStateOverride
    // wants to return a snapshot value that survives downstream pool overwrites.
    [ThreadStatic] private static PrimaryStationState? s_resolvedLegacyPrimary;

    private static PrimaryStationState CopyLegacyPrimaryIntoResolvedScratch(
        PrimaryStationState source)
    {
        var slot = s_resolvedLegacyPrimary ??= new PrimaryStationState();
        slot.CopyFrom(source);
        return slot;
    }

    // Shared ThreadStatic scratches for per-site ComputeKinematicParameters
    // calls where the result is consumed immediately (HK / RT extraction,
    // snapshot CopyFrom) without surviving another kinematic computation
    // on the same stack. Three slots cover call sites that can be live
    // simultaneously: "A" is the typical use; "B" pairs with A when a
    // function computes two kinematics in sequence (upstream + current);
    // "C" covers deeper nested cases (e.g. seed probes inside MRCHUE).
    [ThreadStatic] private static KinematicResult? s_engineKinematicA;
    [ThreadStatic] private static KinematicResult? s_engineKinematicB;
    [ThreadStatic] private static KinematicResult? s_engineKinematicC;

    private static KinematicResult GetEngineKinematicScratchA()
        => s_engineKinematicA ??= new KinematicResult();
    private static KinematicResult GetEngineKinematicScratchB()
        => s_engineKinematicB ??= new KinematicResult();
    private static KinematicResult GetEngineKinematicScratchC()
        => s_engineKinematicC ??= new KinematicResult();

    // Shared SecondaryStationResult scratch for the engine's
    // `new SecondaryStationResult { ... }` snapshot sites that feed
    // StoreLegacyCarrySnapshots. The snapshot is CopyFromed into blState's
    // per-slot pool and then discarded by the caller.
    [ThreadStatic] private static SecondaryStationResult? s_engineSecondaryA;

    private static SecondaryStationResult GetEngineSecondaryScratchA()
        => s_engineSecondaryA ??= new SecondaryStationResult();

    /// <summary>Clear every field on a scratch SecondaryStationResult so
    /// callers can fill only the fields they care about without leaking
    /// stale data from a prior use of the same pool slot.</summary>
    private static void ResetEngineSecondaryScratch(SecondaryStationResult s)
    {
        s.Hc = 0; s.Hc_T = 0; s.Hc_D = 0; s.Hc_U = 0; s.Hc_MS = 0;
        s.Hs = 0; s.Hs_T = 0; s.Hs_D = 0; s.Hs_U = 0; s.Hs_MS = 0;
        s.Us = 0; s.Us_T = 0; s.Us_D = 0; s.Us_U = 0; s.Us_MS = 0;
        s.Cq = 0; s.Cq_T = 0; s.Cq_D = 0; s.Cq_U = 0; s.Cq_MS = 0;
        s.Cf = 0; s.Cf_T = 0; s.Cf_D = 0; s.Cf_U = 0; s.Cf_MS = 0; s.Cf_RE = 0;
        s.Di = 0; s.Di_S = 0; s.Di_T = 0; s.Di_D = 0; s.Di_U = 0; s.Di_MS = 0;
        s.De = 0; s.De_T = 0; s.De_D = 0; s.De_U = 0; s.De_MS = 0;
    }

    private static TransitionModel.TransitionPointResult GetPooledTransitionPointPostLoop()
        => s_pooledTransitionPointPostLoop ??= new TransitionModel.TransitionPointResult();

    private static TransitionModel.TransitionPointResult GetPooledTransitionPointLaminarSeed()
        => s_pooledTransitionPointLaminarSeed ??= new TransitionModel.TransitionPointResult();

    private static double GetHvRat(bool useLegacyPrecision)
    {
        // Phase 1 strip: classic XFoil's main viscous solve runs with HVRAT=0
        // in the live BL path; the float tree always uses the legacy value so
        // COMSET/BLKIN see the same viscosity law as Fortran.
        return LegacyHvRat;
    }

    /// <summary>
    /// D8: see ViscousSolverEngine.Double.cs for rationale.
    /// Bilateral-symmetric airfoil + α=0 → CL=0 analytically, and the
    /// stagnation panel-index scan can toggle at the LE decision boundary.
    /// Pinning ISP for the Newton duration avoids that failure mode.
    /// </summary>
    private static bool IsSymmetricAirfoilAtZeroAlpha(
        LinearVortexPanelState panel, double alphaRadians)
    {
        if (Math.Abs(alphaRadians) > 1e-12) return false;
        int n = panel.NodeCount;
        if (n < 4) return false;

        const double tol = 1e-6;
        int halfN = n / 2;
        int sampleCount = Math.Min(halfN, 8);
        int step = Math.Max(1, halfN / sampleCount);
        for (int i = 1; i <= halfN; i += step)
        {
            int j = n - 1 - i;
            if (j <= i) break;
            double dx = panel.X[i] - panel.X[j];
            double dy = panel.Y[i] + panel.Y[j];
            if (Math.Abs(dx) > tol || Math.Abs(dy) > tol) return false;
        }
        return true;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: XIFSET
    // Difference from legacy: The managed solver currently reconstructs only the default/no-trip parity case here, where XIFORC collapses to the side TE BL coordinate.
    // Decision: Keep this helper limited to the legacy parity path for now, because that is the active Fortran reference boundary and it restores the classic TE-forced interval behavior.
    private static double? GetLegacyParityForcedTransitionXi(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        int side)
    {
        if (!settings.UseLegacyBoundaryLayerInitialization)
        {
            return null;
        }

        double? forcedTransition = side == 0
            ? settings.ForcedTransitionUpper
            : settings.ForcedTransitionLower;

        if (!forcedTransition.HasValue || forcedTransition.Value >= 1.0)
        {
            return blState.XSSI[blState.IBLTE[side], side];
        }

        return null;
    }

    /// <summary>
    /// Simplified entry point that accepts raw airfoil geometry and runs the full
    /// inviscid + viscous analysis pipeline.
    /// </summary>
    /// <param name="geometry">Tuple of (x[], y[]) airfoil coordinates.</param>
    /// <param name="settings">Analysis settings.</param>
    /// <param name="alphaRadians">Angle of attack in radians.</param>
    /// <returns>Full viscous analysis result.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: VISCAL entry flow
    // Difference from legacy: The managed entry point accepts raw geometry, constructs managed panel/inviscid state objects, and then delegates to the main viscous solve.
    // Decision: Keep the managed entry wrapper because it is the natural .NET API surface; preserve the legacy solve chain in the delegated engine path.
    public static ViscousAnalysisResult SolveViscous(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double alphaRadians,
        TextWriter? debugWriter = null)
    {
        // Step 1: Run inviscid analysis to get baseline.
        // ThreadStatic pool eliminates per-case 500KB+ LOH churn (3 influence
        // matrices inside InviscidSolverState, plus the panel-state arrays).
        int maxNodes = settings.PanelCount + 40;
        var panel = GetPooledPanelState(maxNodes);
        var inviscidState = GetPooledInviscidSolverState(maxNodes);

        CurvatureAdaptivePanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        var inviscidResult = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians, panel, inviscidState,
            settings.FreestreamVelocity, settings.MachNumber);


        // Step 2: Run viscous coupling iteration
        return SolveViscousFromInviscid(
            panel, inviscidState, inviscidResult, settings, alphaRadians, debugWriter);
    }

    internal static PreNewtonSetupContext PrepareLegacyPreNewtonContext(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double alphaRadians)
    {
        int maxNodes = settings.PanelCount + 40;
        var panel = GetPooledPanelState(maxNodes);
        var inviscidState = GetPooledInviscidSolverState(maxNodes);

        CurvatureAdaptivePanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        inviscidState.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inviscidState.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        LinearVortexInviscidResult inviscidResult = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians,
            panel,
            inviscidState,
            settings.FreestreamVelocity,
            settings.MachNumber);

        return PreparePreNewtonSetupFromInviscid(
            panel,
            inviscidState,
            inviscidResult,
            settings,
            alphaRadians,
            debugWriter: null);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: VISCAL first-SETBL prelude
    // Difference from legacy: The managed port exposes the state immediately
    // before the first SETBL/UESET assembly as a separate helper because some
    // micro-tests need the earlier seed-only context while others need the
    // full post-MRCHDU replayed state that VISCAL actually feeds into Newton.
    // Decision: Keep both entry points explicit so the test surface can choose
    // the exact legacy boundary it is asserting.
    internal static PreNewtonSetupContext PrepareLegacySetBlContext(
        (double[] x, double[] y) geometry,
        AnalysisSettings settings,
        double alphaRadians)
    {
        PreNewtonSetupContext context = PrepareLegacyPreNewtonContext(
            geometry,
            settings,
            alphaRadians);

        if (!settings.UseLegacyBoundaryLayerInitialization)
        {
            return context;
        }

        RemarchBoundaryLayerLegacyDirect(
            context.BoundaryLayerState,
            settings,
            context.InviscidState.TrailingEdgeGap,
            context.WakeSeed,
            context.Tkbl,
            context.QinfBl,
            context.TkblMs,
            context.HstInv,
            context.HstInvMs,
            context.RstBl,
            context.RstBlMs,
            context.ReyBl,
            context.ReyBlRe,
            context.ReyBlMs);

        return context;
    }

    /// <summary>
    /// Runs the viscous/inviscid Newton coupling iteration starting from a converged inviscid solution.
    /// Port of VISCAL from xoper.f.
    /// Uses Newton loop: BuildNewtonSystem (SETBL) -> BlockTridiagonalSolver.Solve (BLSOLV)
    /// -> ApplyNewtonUpdate (UPDATE) with DIJ coupling.
    /// TransitionModel.CheckTransition is called from within BuildNewtonSystem for natural transition.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: VISCAL
    // Difference from legacy: The solve order remains the same, but the managed port factors each VISCAL phase into explicit helpers and preserves a parity-only seed/remarch path alongside the default managed flow.
    // Decision: Keep the decomposed orchestration and preserve the legacy iteration order, stagnation relocation, and remarch behavior in parity mode.
    public static ViscousAnalysisResult SolveViscousFromInviscid(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        TextWriter? debugWriter = null,
        ViscousBLSeed? blSeed = null)
    {
        return SolveViscousFromInviscidCapturing(
            panel, inviscidState, inviscidResult, settings, alphaRadians,
            out _, debugWriter, blSeed);
    }

    /// <summary>
    /// Viscous solve variant that also exposes the converged BL state so a
    /// caller can capture it as a warm-start seed for a subsequent α.
    /// Used by polar-sweep harnesses and PolarSweepRunner to implement
    /// Fortran-style sequential-α BL state threading.
    /// </summary>
    public static ViscousAnalysisResult SolveViscousFromInviscidCapturing(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        out BoundaryLayerSystemState? finalBLState,
        TextWriter? debugWriter = null,
        ViscousBLSeed? blSeed = null)
    {
        // B3 warm-start safety net: seeded Newton can drive ISP to the
        // trailing edge, producing a degenerate station distribution
        // (IBLTE=[n-1, 1]) that later code can't handle. Wrap with a
        // catch so the experimental ramp path fails gracefully rather
        // than propagating IndexOutOfRangeException. Only active when
        // the caller explicitly provided a seed — cold-start and normal
        // analysis continue to throw on unexpected internal errors.
        if (blSeed is not null)
        {
            try
            {
                return SolveViscousFromInviscidCapturingImpl(
                    panel, inviscidState, inviscidResult, settings, alphaRadians,
                    out finalBLState, debugWriter, blSeed);
            }
            catch (System.IndexOutOfRangeException)
            {
                finalBLState = null;
                return new ViscousAnalysisResult
                {
                    LiftCoefficient = double.NaN,
                    MomentCoefficient = double.NaN,
                    DragDecomposition = new DragDecomposition
                    {
                        CD = double.NaN, CDF = 0d, CDP = 0d,
                        CDSurfaceCrossCheck = 0d, DiscrepancyMetric = 0d,
                        TEBaseDrag = 0d, WaveDrag = null,
                    },
                    Converged = false,
                    Iterations = 0,
                    ConvergenceHistory = new List<ViscousConvergenceInfo>(),
                    UpperProfiles = System.Array.Empty<BoundaryLayerProfile>(),
                    LowerProfiles = System.Array.Empty<BoundaryLayerProfile>(),
                    WakeProfiles = System.Array.Empty<BoundaryLayerProfile>(),
                    UpperTransition = default,
                    LowerTransition = default,
                };
            }
        }
        return SolveViscousFromInviscidCapturingImpl(
            panel, inviscidState, inviscidResult, settings, alphaRadians,
            out finalBLState, debugWriter, blSeed);
    }

    private static ViscousAnalysisResult SolveViscousFromInviscidCapturingImpl(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        out BoundaryLayerSystemState? finalBLState,
        TextWriter? debugWriter,
        ViscousBLSeed? blSeed)
    {

        PreNewtonSetupContext preNewton = PreparePreNewtonSetupFromInviscid(
            panel,
            inviscidState,
            inviscidResult,
            settings,
            alphaRadians,
            debugWriter);

        // B3 warm-start (2026-04-20): if a previously-converged BL state was
        // captured at a nearby α, overwrite the Thwaites-initialized primary
        // unknowns with the seed. The seed must have matching NBL counts —
        // the BL discretization is panel-count-dependent and mixing grids
        // would produce garbage. Transition indices are copied but the
        // first Newton iteration re-runs the transition check and will
        // update them if the flow state has moved.
        //
        // This replicates Fortran XFoil's sequential-`alfa` behavior where
        // the BL state persists in shared COMMON blocks across calls — the
        // initial guess is the previous solve's converged state rather
        // than Thwaites' laminar integration from the stagnation point.
        //
        // Diagnostic finding: NACA 4412 Re=3e6 α=14° cold-started with
        // Thwaites lands Newton in a linear-regime attractor (CL=2.188);
        // warm-started from α=12° converged state, Newton escapes to the
        // physical stall state matching Fortran (CL=1.74).
        if (blSeed is not null)
        {
            ApplyBLSeed(preNewton.BoundaryLayerState, blSeed, preNewton.Isp, settings.UseLegacyBoundaryLayerInitialization);
        }
        int n = preNewton.NodeCount;
        int nWake = preNewton.WakeCount;
        double[] qinv = preNewton.QInv;
        int qinvCount = preNewton.QInvCount;
        int maxStations = preNewton.MaxStations;
        int isp = preNewton.Isp;
        double sst = preNewton.Sst;
        // D8: pin stagnation index on symmetric-α=0 cases; see helper.
        bool pinIsp = IsSymmetricAirfoilAtZeroAlpha(panel, alphaRadians);
        BoundaryLayerSystemState blState = preNewton.BoundaryLayerState;
        WakeSeedData? wakeSeed = preNewton.WakeSeed;
        ViscousNewtonSystem newtonSystem = preNewton.NewtonSystem;
        double[,] dij = preNewton.Dij;
        double[,] ueInv = preNewton.UeInv;
        double tkbl = preNewton.Tkbl;
        double qinfbl = preNewton.QinfBl;
        double tkbl_ms = preNewton.TkblMs;
        double hstinv = preNewton.HstInv;
        double hstinv_ms = preNewton.HstInvMs;
        double rstbl = preNewton.RstBl;
        double rstbl_ms = preNewton.RstBlMs;
        double reybl = preNewton.ReyBl;
        double reybl_re = preNewton.ReyBlRe;
        double reybl_ms = preNewton.ReyBlMs;

        double reinf = settings.ReynoldsNumber;
        double qinf = settings.FreestreamVelocity;
        double hvrat = GetHvRat(settings.UseLegacyBoundaryLayerInitialization);

        double teGap = inviscidState.TrailingEdgeGap;
        // Keep the Newton system on the same cubic WGAP profile used by XFoil's
        // XICALC. The previous exponential surrogate distorted downstream wake
        // D* even after the seed march had been corrected.
        double[] wakeGap = BuildWakeGapArray(wakeSeed, teGap, nWake);

        // --- Newton coupling iteration (matching Fortran VISCAL order) ---
        // Correct Fortran VISCAL order:
        //   a. SETBL: Build Newton system (assembles BL equations at current state)
        //   b. BLSOLV: Solve block-tridiagonal system
        //   c. UPDATE: Apply Newton update (always, with RLXBL relaxation)
        //   d. QVFUE: Edge velocity update via DIJ
        //   e. STMOVE: Relocate stagnation point
        //   f. CL/CD/CM and convergence check
        // MarchBoundaryLayer is NOT called inside the primary Newton coupling loop.
        // Pre-size to the max iterations so the List never resizes during the
        // Newton loop. Each resize is a Gen0 allocation; at 100k+ cases per
        // sweep, avoiding the 3-5 resizes per case removes noticeable pressure.
        var convergenceHistory = new List<ViscousConvergenceInfo>(capacity: settings.MaxViscousIterations + 1);
        bool converged = false;
        int maxIter = settings.MaxViscousIterations;
        double tolerance = settings.ViscousConvergenceTolerance;
        double trustRadius = 1.0;
        // Legacy block: xoper.f VISCAL Newton coupling iteration.
        // Difference from legacy: The same SETBL -> BLSOLV -> UPDATE -> stagnation-move loop is preserved, but the managed code wires it through typed helpers and richer diagnostics.
        // Decision: Keep the helper-based structure and preserve the original iteration order.
        // Note: Fortran's QDCALC is called before the Newton loop but SETBL
        // ALSO refreshes DUE2 = UEDG - USAV at each station. The USAV comes from
        // the QDCALC result (computed once from initial MASS). The C# recomputes
        // USAV at each iteration which is functionally equivalent because the DUE2
        // coupling works correctly with updated MASS values.
        double[,]? fixedUsav = null;

        // Fortran CL is initialized from the inviscid SPECAL result and updated
        // incrementally with CL += RLX*DAC at each Newton iteration.
        // The initial CL comes from the inviscid solution, NOT from the BL state.
        double legacyIncrementalCl = settings.UseLegacyBoundaryLayerInitialization
            ? (float)inviscidResult.LiftCoefficient
            : 0.0;

        // D9: ISP-hysteresis state; see ViscousSolverEngine.Double.cs.
        int ispPrev = isp;
        int ispTwoAgo = isp;

        for (int iter = 0; iter < maxIter; iter++)
        {
            
            
            // The legacy initialization already performed the first remarch.
            // Skip the Newton-loop remarch on iter 0 because the init MRCHDU
            // at line 1808 is equivalent to the first SETBL's MRCHDU.
            // BL state hash before MRCHDU (matches Fortran position before SETBL which calls MRCHDU internally)
            // Fortran: `DO IBH=1, NBL(IS)` reads THET(1..NBL) = similarity + stations 2..NBL
            // C# has THET[1] = similarity (THET[0] is unused virtual stagnation duplicate).
            // To match Fortran, loop iH = 1..NBL-1 reading THET[iH..NBL-1] which is NBL values.
            

            // D3 fix continuation: per-iter Remarch runs in both paths for
            // the same reason it runs in the parity path — keeps ITRAN/
            // IBLTE consistent with the Newton-updated (θ, δ*, Cτ) field.
            // Removing the legacy-flag gate here lets modern-mode Newton
            // stabilize between iterations the same way parity-mode does.
            // The parity path (useLegacy=true) already called this and
            // continues to do so — unchanged for that path.
            if (iter > 0)
            {
                RemarchBoundaryLayerLegacyDirect(
                    blState,
                    settings,
                    inviscidState.TrailingEdgeGap,
                    wakeSeed,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                    reybl,
                    reybl_re,
                    reybl_ms);

                // ah79 post-MRCHDU full dump
                // n6h20 trace: ITRAN after MRCHDU
                
                // n6h20 trace: BL state AFTER MRCHDU (matches F's BLDUMP at top of UPDATE)
                

                // DEBUG: scan for NaN after MRCHDU at iter > 0
                

                
            }

            // Per-iteration theta at key stations for parity tracking
            

            // b. SETBL global assembly: build the Newton system from the current state.
            double rmsbl = ViscousNewtonAssembler.BuildNewtonSystem(
                blState, newtonSystem, dij, settings,
                isAlphaPrescribed: true, wakeGap, nWake + 1,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                rstbl, rstbl_ms,
                reybl, reybl_re, reybl_ms, hvrat,
                ueInv,
                isp, n,
                cachedUsav: fixedUsav,
                cachedSstGo: settings.UseLegacyBoundaryLayerInitialization
                    ? preNewton.InviscidSstGo : null,
                cachedSstGp: settings.UseLegacyBoundaryLayerInitialization
                    ? preNewton.InviscidSstGp : null,
                // Raw TE normal gap for first-wake DTE merge parity (Fortran line 354).
                anteRaw: wakeSeed?.NormalGap ?? 0.0);

            // ITRAN trace for transition station debugging
            
            // Compute XOR hash of system for parity check
            
            // Dump ALL VDEL system lines BEFORE BLSOLV (first iteration only)
            
            // Per-station VM sum at iteration 0 (first Newton step) — find divergent station
            
            // Per-station additive checksum of VM at iteration 13
            
            // Per-station VM hash at iteration 5 (case 188 NACA 0009 a=-2 Nc=12)
            
            // Dump ALL VDEL column-1 at iteration 13 for full comparison
            

            // Matrix hash comparison
            

            // Pre-BLSOLV aggregate checksum
            

            // Pre-solve VDEL dump for iter 0 (RHS of Newton system)
            

            // c. BLSOLV: Solve block-tridiagonal system
            BlockTridiagonalSolver.Solve(
                newtonSystem,
                vaccel: 0.01,
                useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);

            // Post-BLSOLV additive checksum
            
            // Dump post-BLSOLV VDEL solution for ALL stations at iters 1-2
            
            // Dump ALL post-BLSOLV VDEL for parity comparison
            

            // d. UPDATE: Apply Newton update (always, with relaxation from RLXBL).
            //    With correct Jacobians (reybl threading + DUE/DDS terms), Newton
            //    should converge. No save-try-revert pattern needed.
            double rlx = 0.0;
            if (!double.IsInfinity(rmsbl) && !double.IsNaN(rmsbl))
            {
                // Fortran CL is tracked incrementally: CL += RLX*DAC.
                // For legacy parity, use the incrementally-tracked CL.
                // For non-legacy, recompute from BL state.
                double currentCl = settings.UseLegacyBoundaryLayerInitialization
                    ? legacyIncrementalCl
                    : ComputeViscousCL(blState, panel, inviscidState, alphaRadians, qinf, isp, n);
                var (newtonRlx, updatedRms, newTrustRadius, accepted, dac) =
                    ViscousNewtonUpdater.ApplyNewtonUpdate(
                        blState, newtonSystem, settings.ViscousSolverMode,
                        hstinv, wakeGap, nWake + 1, trustRadius, rmsbl, rmsbl,
                        dij, isp, n,
                        new ViscousNewtonUpdater.NewtonUpdateContext(
                            panel,
                            alphaRadians,
                            qinf,
                            currentCl,
                            ueInv,
                            IsAlphaPrescribed: true),
                        settings.UseLegacyBoundaryLayerInitialization);
                // Fortran UPDATE: CL = CL + RLX*DAC (for LALFA=true)
                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    
                    legacyIncrementalCl = (float)((float)legacyIncrementalCl + (float)((float)newtonRlx * (float)dac));
                }
                trustRadius = newTrustRadius;
                rlx = newtonRlx;
                
                // The legacy parity path uses the UPDATE-style normalized
                // DN1..DN4 residual returned by ApplyNewtonUpdate.
                rmsbl = updatedRms;

            }

            

            // e. QVFUE in XFoil only maps the updated UEDG field back to panel
            // tangential velocities. UPDATE already applies the DIJ-coupled Ue
            // change, so there is no extra UESET-style relaxation step here.

            // Fortran xoper.f:3107-3140 sequence: QVFUE → GAMQV → STMOVE → CLCALC.
            // QVFUE sets QVIS using CURRENT (pre-STMOVE) IPAN mapping. GAMQV
            // copies QVIS into GAM. STMOVE may then rebuild IPAN but GAM is
            // unchanged. CLCALC uses the pre-STMOVE GAM.
            // Capture pre-STMOVE qvis here so ComputeViscousCL matches Fortran.
            double[]? preStmoveQvis = null;
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // Pooled slot 1 persists across Newton iters within the same
                // case; ComputeViscousCL reads it via overrideQvis so the
                // write here and the downstream read share the same buffer.
                preStmoveQvis = BuildViscousPanelSpeeds(
                    blState, inviscidState, panel, isp, n, qinf,
                    useLegacyPrecision: true,
                    destination: SolverBuffers.PanelScratch1(n));
            }


            // f. STMOVE: Relocate stagnation point if it has moved
            // Convert UEDG back to panel speeds, then find stagnation by sign change
            double[] currentSpeeds = ConvertUedgToSpeeds(blState, n,
                settings.UseLegacyBoundaryLayerInitialization,
                destination: SolverBuffers.PanelScratch2(n));
            var (newIsp, newSst, newSstGo, newSstGp) = FindStagnationPointXFoil(
                currentSpeeds,
                panel,
                n,
                useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization
                    || inviscidState.UseLegacyKernelPrecision
                    || inviscidState.UseLegacyPanelingPrecision);
            int rawNewIsp = newIsp;
            newIsp = Math.Max(1, Math.Min(n - 2, newIsp));
            // D9: hysteresis against single-panel A→B→A ISP oscillation.
            if (iter >= 2 && newIsp != isp && Math.Abs(newIsp - isp) == 1 && newIsp == ispTwoAgo)
            {
                newIsp = isp;
                newSst = sst;
            }
            bool stagnationShifted = Math.Abs(newSst - sst) > 1.0e-12;
            
            
            if (newIsp != isp)
            {
                int oldUpperCount = blState.NBL[0];
                int oldLowerCount = blState.NBL[1];

                var (iblteNew, nblNew) = ComputeStationCountsXFoil(n, newIsp, nWake);
                blState.IBLTE[0] = iblteNew[0];
                blState.IBLTE[1] = iblteNew[1];
                blState.NBL[0] = nblNew[0];
                blState.NBL[1] = nblNew[1];

                // Match STMOVE ordering: rebuild the new station geometry first,
                // then shift/interpolate BL variables on that new grid.
                InitializeXFoilStationMappingWithWakeSeed(
                    blState,
                    panel,
                    currentSpeeds,
                    newIsp,
                    newSst,
                    n,
                    nWake,
                    wakeSeed,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization || inviscidState.UseLegacyPanelingPrecision,
                    initializeUedg: false);

                StagnationPointTracker.MoveStagnationPoint(
                    blState,
                    isp,
                    newIsp,
                    oldUpperCount,
                    oldLowerCount,
                    settings.UseLegacyBoundaryLayerInitialization);
                

                isp = newIsp;
                sst = newSst;
                UpdateInviscidEdgeBaseline(ueInv, maxStations, blState, qinv, qinvCount, nWake, wakeSeed);
                var (isysNew, nsysNew) = EdgeVelocityCalculator.MapStationsToSystemLines(iblteNew, nblNew);
                newtonSystem.SetupISYS(isysNew, nsysNew);
                ConfigureNewtonSystemTopology(newtonSystem, blState, panel);
            }
            else if (stagnationShifted)
            {
                // Legacy STMOVE still calls XICALC when IST is unchanged so the
                // BL arc-length coordinates follow the updated interpolated SST.
                InitializeXFoilStationMappingWithWakeSeed(
                    blState,
                    panel,
                    currentSpeeds,
                    isp,
                    newSst,
                    n,
                    nWake,
                    wakeSeed,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization || inviscidState.UseLegacyPanelingPrecision,
                    initializeUedg: false);
                sst = newSst;
            }

            // Update SST_GO from stagnation relocation (matching Fortran
            // STMOVE→XICALC which recomputes SST_GO at each iteration).
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                preNewton.InviscidSstGo = newSstGo;
                preNewton.InviscidSstGp = newSstGp;
            }
            // Fortran STMOVE calls XICALC which recomputes WGAP whenever SST or IST
            // changes. The C# path only recomputed wake gap at init, so subsequent
            // iterations used a stale profile. Re-sync the wake gap profile and the
            // local wakeGap snapshot whenever STMOVE shifted stagnation.
            if (settings.UseLegacyBoundaryLayerInitialization
                && wakeSeed is not null
                && stagnationShifted)
            {
                RecomputeWakeGapFromXssi(blState, wakeSeed, inviscidState, panel);
                wakeGap = BuildWakeGapArray(wakeSeed, teGap, nWake);
            }
            // DEBUG: scan for NaN state after STMOVE for iter 0 (include MASS check)
            
            
            
            // post-STMOVE trace for iter 11 wake stations 64-70 side 2 (C# iter=10 0-idx)
            

            // f. CL/CD/CM and convergence check
            // Fortran: CALL CLCALC recomputes CL from circulation (QVFUE + CLCALC).
            // This CL replaces the UPDATE's incremental estimate and is used for the
            // next iteration's DAC computation.
            // Pass pre-STMOVE qvis in legacy mode so CLCALC matches Fortran ordering
            // (QVFUE→GAMQV→STMOVE→CLCALC uses pre-STMOVE QVIS).
            double cl = ComputeViscousCL(blState, panel, inviscidState, alphaRadians, qinf, isp, n,
                useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                overrideQvis: preStmoveQvis);
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // Fortran: CLCALC overwrites CL with the circulation-based value.
                // The incremental CL from UPDATE is only an intermediate step.
                legacyIncrementalCl = (float)cl;
            }
            double cd = EstimateDrag(blState, qinf, reinf);
            
            
            double cm = ComputeViscousCM(blState, panel, inviscidState, alphaRadians, qinf, isp, n);


            // Guard against NaN from transient numerical issues
            if (double.IsNaN(rmsbl) || double.IsInfinity(rmsbl))
                rmsbl = double.MaxValue;

            convergenceHistory.Add(new ViscousConvergenceInfo
            {
                Iteration = iter,
                RmsResidual = rmsbl,
                MaxResidual = rmsbl * 2.0,
                MaxResidualStation = 0,
                MaxResidualSide = 0,
                RelaxationFactor = rlx,
                TrustRegionRadius = trustRadius,
                CL = cl,
                CD = cd,
                CM = cm
            });

            // Convergence check uses rmsbl (Newton RMS from BuildNewtonSystem)
            if (rmsbl < tolerance)
            {
                converged = true;
                break;
            }

            // D9: slide the ISP-hysteresis window.
            ispTwoAgo = ispPrev;
            ispPrev = isp;
        }

        // --- Post-convergence: package results ---
        // Fortran's final CL is the last iter's CLCALC output (computed inside
        // the iter loop using pre-STMOVE QVIS). The in-loop legacyIncrementalCl
        // already tracks this. Recomputing here without overrideQvis uses
        // post-STMOVE state and drifts 1 ULP.
        double finalCL = settings.UseLegacyBoundaryLayerInitialization
            ? legacyIncrementalCl
            : ComputeViscousCL(blState, panel, inviscidState, alphaRadians, qinf, isp, n,
                useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
        double finalCM = ComputeViscousCM(blState, panel, inviscidState, alphaRadians, qinf, isp, n);

        // Use DragCalculator for proper drag decomposition
        var dragDecomp = DragCalculator.ComputeDrag(
            blState, panel, qinf, alphaRadians,
            settings.MachNumber, teGap,
            settings.UseExtendedWake,
            useLockWaveDrag: false,
            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);

        // D6 — plateau-convergence fallback (see ViscousSolverEngine.Double.cs
        // for full rationale). Three qualifiers: (a) rms within plateau
        // tolerance and CL stable; (b) CL frozen and rlx pinned at 1.0;
        // (c) symmetric airfoil at α=0 with near-zero observed CL —
        // analytical CL=0 trust.
        if (!converged && convergenceHistory.Count >= 5)
        {
            const int plateauWindow = 10;
            const double legacyTolerance = 1e-4;
            double plateauTolerance = Math.Max(tolerance * 100.0, legacyTolerance);
            int windowStart = Math.Max(0, convergenceHistory.Count - plateauWindow);
            int windowSize = convergenceHistory.Count - windowStart;

            double maxRms = 0.0, minRms = double.MaxValue;
            double maxCL = double.MinValue, minCL = double.MaxValue;
            double maxCD = double.MinValue, minCD = double.MaxValue;
            double minRlx = double.MaxValue;
            double sumCL = 0.0, sumCD = 0.0, sumCM = 0.0;
            bool anyNonFinite = false;
            for (int i = windowStart; i < convergenceHistory.Count; i++)
            {
                var h = convergenceHistory[i];
                if (!double.IsFinite(h.RmsResidual) || !double.IsFinite(h.CL) || !double.IsFinite(h.CD))
                {
                    anyNonFinite = true;
                    break;
                }
                maxRms = Math.Max(maxRms, h.RmsResidual);
                minRms = Math.Min(minRms, h.RmsResidual);
                maxCL = Math.Max(maxCL, h.CL);
                minCL = Math.Min(minCL, h.CL);
                maxCD = Math.Max(maxCD, h.CD);
                minCD = Math.Min(minCD, h.CD);
                minRlx = Math.Min(minRlx, h.RelaxationFactor);
                sumCL += h.CL;
                sumCD += h.CD;
                sumCM += h.CM;
            }

            bool qualifier_a = !anyNonFinite
                && maxRms <= plateauTolerance
                && minRms > 0.0 && maxRms / minRms < 10.0
                && (maxCL - minCL) < 0.005;
            bool qualifier_b = !anyNonFinite
                && (maxCL - minCL) < 0.001
                && minRlx > 0.999
                && double.IsFinite(maxRms);
            bool isSymmetricZero = IsSymmetricAirfoilAtZeroAlpha(panel, alphaRadians);
            bool cdStable = !anyNonFinite && minCD > 0.0 && (maxCD - minCD) / minCD < 0.05;
            bool clNearZero = !anyNonFinite && Math.Max(Math.Abs(maxCL), Math.Abs(minCL)) < 1e-3;
            bool qualifier_c = isSymmetricZero && cdStable && clNearZero;

            // Qualifier (d): bounded Newton limit cycle. See Double engine
            // for rationale. Trust-region attractor with small CL swing,
            // tight CD, bounded residual.
            double absAvgCL = Math.Abs(windowSize > 0 ? sumCL / windowSize : 0.0);
            double absAvgCD = Math.Abs(windowSize > 0 ? sumCD / windowSize : 0.0);
            bool cdTightlyStable = !anyNonFinite && minCD > 0.0 && absAvgCD > 0.0
                && (maxCD - minCD) / absAvgCD < 0.01;
            bool clSmallSwing = !anyNonFinite
                && (maxCL - minCL) < 0.03
                && (maxCL - minCL) / Math.Max(absAvgCL, 0.1) < 0.05;
            bool qualifier_d = !anyNonFinite
                && windowSize >= 10
                && clSmallSwing
                && cdTightlyStable
                && maxRms < 1.0
                && absAvgCL < 3.0;

            if (qualifier_a || qualifier_b || qualifier_c || qualifier_d)
            {
                converged = true;
                double avgCD = sumCD / windowSize;
                if (qualifier_c)
                {
                    finalCL = 0.0;
                    finalCM = 0.0;
                }
                else
                {
                    finalCL = sumCL / windowSize;
                    finalCM = sumCM / windowSize;
                }
                dragDecomp = new DragDecomposition
                {
                    CD = avgCD,
                    CDF = dragDecomp.CDF,
                    CDP = dragDecomp.CDP,
                    CDSurfaceCrossCheck = dragDecomp.CDSurfaceCrossCheck,
                    DiscrepancyMetric = dragDecomp.DiscrepancyMetric,
                    TEBaseDrag = dragDecomp.TEBaseDrag,
                    WaveDrag = dragDecomp.WaveDrag,
                };
            }
        }

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

        finalBLState = blState;
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
    // Legacy mapping: f_xfoil/src/xoper.f :: COMSET
    // Difference from legacy: The same compressibility pipeline is preserved, but the managed port isolates it into a reusable helper with an explicit parity branch.
    // Decision: Keep the helper and preserve the legacy REAL staging in parity mode.
    private static void ComputeCompressibilityParameters(
        double mach, double qinf, double reinf, double hvrat,
        out double tkbl, out double qinfbl, out double tkbl_ms,
        out double hstinv, out double hstinv_ms,
        out double rstbl, out double rstbl_ms,
        out double reybl, out double reybl_re, out double reybl_ms,
        bool useLegacyPrecision = false)
    {
        // Phase 1 strip: float-only COMSET. REAL throughout, including
        // GAMM1 = GAMMA - 1.0. The doubled tree (auto-generated *.Double.cs
        // twin via gen-double.py) replaces these floats with doubles.
        float machf = (float)mach;
        float qinff = (float)qinf;
        float reinff = (float)reinf;
        float hvratf = (float)hvrat;
        float gm1f = (float)LegacyPrecisionMath.GammaMinusOne(true);

        qinfbl = qinff;

        float msqf = machf * machf;
        float betaf = MathF.Sqrt(1.0f - msqf);
        float betaMsqf = -0.5f / betaf;
        float betaOnef = 1.0f + betaf;
        tkbl = msqf / (betaOnef * betaOnef);
        tkbl_ms = (1.0f / (betaOnef * betaOnef))
            - ((2.0f * (float)tkbl / betaOnef) * betaMsqf);

        float gm1hf = 0.5f * gm1f;
        float denf = 1.0f + (gm1hf * msqf);
        rstbl = LegacyLibm.Pow(denf, 1.0f / gm1f);
        rstbl_ms = 0.5f * (float)rstbl / denf;

        // Classic COMSET keeps the derivative channels alive in the
        // incompressible limit. HSTINV itself goes to zero at MINF = 0, but
        // HSTINV_MS, RSTBL_MS, and REYBL_MS do not, and BLKIN/BLVAR depend on
        // those nonzero sensitivities even for nominally incompressible cases.
        float qinfSqInvf = LegacyLibm.Pow(1.0f / qinff, 2.0f);
        hstinv = gm1f * LegacyLibm.Pow(machf / qinff, 2.0f) / denf;
        hstinv_ms = (gm1f * qinfSqInvf / denf)
            - ((0.5f * gm1f * (float)hstinv) / denf);

        float heratf = 1.0f - (0.5f * qinff * qinff * (float)hstinv);
        float heratMsf = -0.5f * qinff * qinff * (float)hstinv_ms;
        float reyScalef = MathF.Sqrt(heratf * heratf * heratf) * (1.0f + hvratf) / (heratf + hvratf);
        reybl = reinff * reyScalef;
        reybl_re = reyScalef;
        reybl_ms = (float)reybl * ((1.5f / heratf) - (1.0f / (heratf + hvratf))) * heratMsf;
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only helper that centralizes the parity rounding policy for the seed march.
    // Decision: Keep the helper because it prevents duplicated parity casts across the legacy seed code.
    private static void ApplyLegacySeedPrecision(
        bool useLegacyPrecision,
        ref double uei,
        ref double theta,
        ref double dstar,
        ref double shearLikeState)
    {
        // Phase 1 strip: float-only path always rounds to single.
        uei = LegacyPrecisionMath.RoundToSingle(uei);
        theta = LegacyPrecisionMath.RoundToSingle(theta);
        dstar = LegacyPrecisionMath.RoundToSingle(dstar);
        shearLikeState = LegacyPrecisionMath.RoundToSingle(shearLikeState);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE/MRCHDU first-wake TE merge
    // Difference from legacy: The managed solver has to rebuild the merged TE
    // state explicitly from the side arrays instead of reading the live COMMON
    // packet directly at the wake handoff.
    // Decision: Keep the helper and preserve the legacy REAL staging for the
    // weighted CTI merge and the TE theta/dstar packet so every first-wake
    // entry path consumes the same carry state.
    private static void ComputeLegacyWakeTeMergeState(
        BoundaryLayerSystemState blState,
        double wakeGap,
        bool useLegacyPrecision,
        out double thetaTe,
        out double dstarTe,
        out double ctauTe)
    {
        double upperThetaTe = blState.THET[blState.IBLTE[0], 0];
        double lowerThetaTe = blState.THET[blState.IBLTE[1], 1];
        thetaTe = useLegacyPrecision
            ? LegacyPrecisionMath.Add(upperThetaTe, lowerThetaTe, true)
            : upperThetaTe + lowerThetaTe;

        double upperDstarTe = blState.DSTR[blState.IBLTE[0], 0];
        double lowerDstarTe = blState.DSTR[blState.IBLTE[1], 1];
        dstarTe = useLegacyPrecision
            ? LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(upperDstarTe, lowerDstarTe, true),
                wakeGap,
                true)
            : upperDstarTe + lowerDstarTe + wakeGap;

        double weightedUpper = useLegacyPrecision
            ? LegacyPrecisionMath.Multiply(blState.CTAU[blState.IBLTE[0], 0], upperThetaTe, true)
            : blState.CTAU[blState.IBLTE[0], 0] * upperThetaTe;
        double weightedLower = useLegacyPrecision
            ? LegacyPrecisionMath.Multiply(blState.CTAU[blState.IBLTE[1], 1], lowerThetaTe, true)
            : blState.CTAU[blState.IBLTE[1], 1] * lowerThetaTe;
        double weightedSum = useLegacyPrecision
            ? LegacyPrecisionMath.Add(weightedUpper, weightedLower, true)
            : weightedUpper + weightedLower;
        double thetaDenominator = Math.Max(thetaTe, 1.0e-10);
        ctauTe = useLegacyPrecision
            ? LegacyPrecisionMath.Divide(weightedSum, thetaDenominator, true)
            : weightedSum / thetaDenominator;
    }

    // ================================================================
    // BL marching (used when Newton step is unstable)
    // ================================================================

    /// <summary>
    /// Marches the BL equations on both surfaces and wake using current edge velocities.
    /// Uses TransitionModel.CheckTransition for transition detection.
    /// Returns RMS of BL equation residuals.
    /// </summary>
    // Legacy mapping: legacy-derived from f_xfoil/src/xbl.f :: MRCHUE-style marching
    // Difference from legacy: This is a simplified managed fallback march, not the exact classic MRCHUE/MRCHDU implementation used by the parity path.
    // Decision: Keep it as a managed recovery path and do not treat it as the parity reference.
    private static double MarchBoundaryLayer(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        double rmsResidual = 0.0;
        int nResiduals = 0;

        // Legacy block: managed fallback BL march.
        // Difference from legacy: This loop intentionally uses a simplified predictor/corrector march rather than the exact classic seed/remarch logic.
        // Decision: Keep it as a managed-only fallback path.
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
                            settings.UseModernTransitionCorrections, null,
                            settings.UseLegacyBoundaryLayerInitialization);

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
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only residual probe for the simplified fallback march.
    // Decision: Keep it as a managed diagnostic helper.
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
    // Legacy mapping: legacy-derived from f_xpanel.f/xbl.f viscous-inviscid DIJ coupling semantics
    // Difference from legacy: The coupling idea is inherited from XFoil, but this helper is a managed-only direct DIJ update path that is no longer the main Newton-loop mechanism.
    // Decision: Keep it as a managed fallback/helper and not as the parity reference.
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
                if (Math.Abs(ueInvLocal) < 1e-10) continue;

                // Compute Ue correction from mass defect via DIJ
                double ueCorrection = 0.0;
                double vtiI = blState.VTI[ibl, side];
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

                        double vtiJ = blState.VTI[jbl, jSide];
                        ueCorrection += -vtiI * vtiJ * dij[iPan, jPan] * dMass;
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
    /// Gets the panel index for a BL station from the IPAN array.
    /// </summary>
    // Legacy mapping: none
    // Difference from legacy: This is a managed-only accessor over the station-to-panel mapping arrays.
    // Decision: Keep the helper for readability and bounds safety.
    private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
        BoundaryLayerSystemState blState)
    {
        if (ibl < 0 || ibl >= blState.MaxStations) return -1;
        return blState.IPAN[ibl, side];
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: SETBL/BLSOLV topology bookkeeping
    // Difference from legacy: The managed port stores the topology explicitly on the Newton-system object instead of inferring it from shared indices each time.
    // Decision: Keep the explicit topology setup and preserve the same TE/wake line semantics.
    private static void ConfigureNewtonSystemTopology(
        ViscousNewtonSystem newtonSystem,
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel)
    {
        newtonSystem.UpperTeLine = FindSystemLine(
            newtonSystem,
            blState.IBLTE[0],
            side: 0);

        newtonSystem.FirstWakeLine = FindSystemLine(
            newtonSystem,
            blState.IBLTE[1] + 1,
            side: 1);

        newtonSystem.ArcLengthSpan = Math.Max(
            panel.ArcLength[panel.NodeCount - 1] - panel.ArcLength[0],
            1e-12);
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only lookup helper over the explicit `ISYS` map.
    // Decision: Keep it because the C# solver represents the mapping explicitly.
    private static int FindSystemLine(ViscousNewtonSystem newtonSystem, int station, int side)
    {
        for (int iv = 0; iv < newtonSystem.NSYS; iv++)
        {
            if (newtonSystem.ISYS[iv, 0] == station && newtonSystem.ISYS[iv, 1] == side)
            {
                return iv;
            }
        }

        return -1;
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: UICALC/QVFUE baseline semantics
    // Difference from legacy: The same baseline idea is preserved, but the managed port rebuilds the baseline from explicit arrays and optional wake seed geometry instead of reading shared global state.
    // Decision: Keep the helper and preserve the wake/airfoil baseline semantics that the Newton loop expects.
    /// <summary>
    /// Updates only XSSI (arc length coordinates) to match Fortran XICALC.
    /// Does NOT rebuild IPAN, VTI, or UEDG.
    /// </summary>
    private static void UpdateXssiOnly(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        int isp, double sst, int n,
        bool useLegacyPrecision)
    {
        double chordArcLength = LegacyPrecisionMath.Subtract(panel.ArcLength[n - 1], panel.ArcLength[0], useLegacyPrecision);
        double xeps = LegacyPrecisionMath.Multiply(1.0e-7, chordArcLength, useLegacyPrecision);

        // Side 0: upper surface
        blState.XSSI[0, 0] = 0.0;
        for (int ibl = 1; ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = isp - (ibl - 1);
            double stationXi = LegacyPrecisionMath.Subtract(sst, panel.ArcLength[iPan], useLegacyPrecision);
            blState.XSSI[ibl, 0] = LegacyPrecisionMath.Max(stationXi, xeps, useLegacyPrecision);
        }

        // Side 1: lower surface
        blState.XSSI[0, 1] = 0.0;
        for (int ibl = 1; ibl <= blState.IBLTE[1]; ibl++)
        {
            int iPan = isp + ibl;
            double stationXi = LegacyPrecisionMath.Subtract(panel.ArcLength[iPan], sst, useLegacyPrecision);
            blState.XSSI[ibl, 1] = LegacyPrecisionMath.Max(stationXi, xeps, useLegacyPrecision);
        }

        // Wake XSSI is NOT updated by XICALC — only surface stations change.
    }

    private static void UpdateInviscidEdgeBaseline(
        double[,] ueInv,
        int ueInvRowCount,
        BoundaryLayerSystemState blState,
        double[] qinv,
        int qinvCount,
        int nWake,
        WakeSeedData? wakeSeed = null)
    {
        // Zero only the active region; the pooled buffer may be larger but
        // rows beyond ueInvRowCount belong to another case's worst-case sizing.
        for (int row = 0; row < ueInvRowCount; row++)
        {
            ueInv[row, 0] = 0.0;
            ueInv[row, 1] = 0.0;
        }

        for (int side = 0; side < 2; side++)
        {
            ueInv[0, side] = 0.0;
            for (int ibl = 1; ibl <= blState.IBLTE[side] && ibl < blState.MaxStations; ibl++)
            {
                int iPan = blState.IPAN[ibl, side];
                if (iPan >= 0 && iPan < qinvCount)
                {
                    ueInv[ibl, side] = blState.VTI[ibl, side] * qinv[iPan];
                }
            }
        }

        for (int iw = 1; iw <= nWake; iw++)
        {
            int ibl1 = blState.IBLTE[1] + iw;
            double wakeUe = GetWakeStationEdgeVelocity(
                blState,
                wakeSeed,
                iw);
            if (ibl1 < ueInvRowCount)
            {
                ueInv[ibl1, 1] = wakeUe;
            }

            int ibl0 = blState.IBLTE[0] + iw;
            if (ibl0 < ueInvRowCount)
            {
                ueInv[ibl0, 0] = wakeUe;
            }
        }

    }

    // ================================================================
    // Viscous CL computation (QVFUE + CLCALC with viscous gamma)
    // ================================================================

    /// <summary>
    /// Computes viscous CL by converting BL edge velocities (UEDG) back to
    /// equivalent panel speeds, then integrating pressure forces using CLCALC.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: QVFUE/CLCALC
    // Difference from legacy: The managed port computes viscous panel speeds and then integrates forces through local arrays rather than mutating the panel solver state in place.
    // Decision: Keep the explicit integration helper and preserve the legacy force-integration meaning.
    private static double ComputeViscousCL(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        double alphaRadians,
        double qinf,
        int isp, int n,
        bool useLegacyPrecision = false,
        double[]? overrideQvis = null)
    {
        double[] qvis = overrideQvis ?? BuildViscousPanelSpeeds(
            blState,
            inviscidState,
            panel,
            isp,
            n,
            qinf,
            useLegacyPrecision,
            destination: SolverBuffers.PanelScratch3(n));

        // Phase 1 strip: float-only Fortran CLCALC with REAL arithmetic
        // throughout. The doubled tree (auto-generated *.Double.cs twin via
        // gen-double.py) gets the double-precision mirror.
        // For M=0: CPG = CGINC = 1.0 - (GAM/QINF)^2.
        float fCa = LegacyPrecisionMath.CosF((float)alphaRadians);
        float fSa = LegacyPrecisionMath.SinF((float)alphaRadians);
        float fQinf = (float)Math.Max(qinf, 1e-10);
        float fCl = 0.0f;

        // Fortran CLCALC: initialize CPG1 at node 1 (index 0).
        float q1 = (float)qvis[0];
        float cginc1 = 1.0f - (q1 / fQinf) * (q1 / fQinf);
        float cpg1 = cginc1; // For M=0: BETA=1, BFAC=0 -> CPG = CGINC

        for (int i = 0; i < n; i++)
        {
            int ip = i + 1;
            if (ip == n) ip = 0;

            float qip = (float)qvis[ip];
            float cginc2 = 1.0f - (qip / fQinf) * (qip / fQinf);
            float cpg2 = cginc2;

            // Fortran CLCALC: DX = (X(IP)-X(I))*CA + (Y(IP)-Y(I))*SA. With
            // -ffp-contract=off each multiply and add rounds separately. Use
            // RoundBarrier to prevent JIT from fusing to FMA.
            float dxTerm = LegacyPrecisionMath.RoundBarrier(
                ((float)panel.X[ip] - (float)panel.X[i]) * fCa);
            float dyTerm = LegacyPrecisionMath.RoundBarrier(
                ((float)panel.Y[ip] - (float)panel.Y[i]) * fSa);
            float dx = LegacyPrecisionMath.RoundBarrier(dxTerm + dyTerm);
            float ag = LegacyPrecisionMath.RoundBarrier(0.5f * (cpg2 + cpg1));

            // Fortran: CL = CL + DX*AG (separate multiply then add).
            float dxAg = LegacyPrecisionMath.RoundBarrier(dx * ag);
            fCl = LegacyPrecisionMath.RoundBarrier(fCl + dxAg);

            cpg1 = cpg2;
        }

        return fCl;
    }

    /// <summary>
    /// Computes viscous CM from viscous panel speeds and moment integration.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: QVFUE moment integration lineage
    // Difference from legacy: The managed code performs the same viscous-speed-based moment integration but makes the reference point and segment algebra explicit.
    // Decision: Keep the explicit integration helper and preserve the legacy physical meaning.
    private static double ComputeViscousCM(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        double alphaRadians,
        double qinf,
        int isp, int n)
    {
        double[] qvis = BuildViscousPanelSpeeds(
            blState,
            inviscidState,
            panel,
            isp,
            n,
            qinf,
            destination: SolverBuffers.PanelScratch5(n));

        double[] cp = SolverBuffers.PanelScratch6(n);
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

    internal static PreNewtonSetupContext PreparePreNewtonSetupFromInviscid(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        LinearVortexInviscidResult inviscidResult,
        AnalysisSettings settings,
        double alphaRadians,
        TextWriter? debugWriter)
    {
        _ = inviscidResult;

        int n = panel.NodeCount;
        // Legacy reduced-panel traces (for example alpha-0 P12) prove that the
        // parity path should allow a 3-station wake seed instead of forcing 4.
        // Phase 2: WakeStationMultiplier > 1 extends the wake; default is 1.0
        // which preserves the Fortran-parity `(n/8)+2` length.
        int nWakeBase = Math.Max((n / 8) + 2, 3);
        int nWake = settings.WakeStationMultiplier <= 1.0d
            ? nWakeBase
            : Math.Max((int)Math.Round(nWakeBase * settings.WakeStationMultiplier), 3);

        double[] qinv = XFoil.Solver.Numerics.SolverBuffers.QinvScratch(n);
        Array.Copy(inviscidState.InviscidSpeed, qinv, n);

        var (isp, sst, initSstGo, initSstGp) = FindStagnationPointXFoil(
            qinv,
            panel,
            n,
            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization
                || inviscidState.UseLegacyKernelPrecision
                || inviscidState.UseLegacyPanelingPrecision);
        isp = Math.Max(1, Math.Min(n - 2, isp));
        

        var (iblte, nbl) = ComputeStationCountsXFoil(n, isp, nWake);
        int maxStations = Math.Max(nbl[0], nbl[1]) + nWake + 10;
        var blState = GetPooledBlSystemState(maxStations, nWake);
        blState.IBLTE[0] = iblte[0];
        blState.IBLTE[1] = iblte[1];
        blState.NBL[0] = nbl[0];
        blState.NBL[1] = nbl[1];

        WakeSeedData? wakeSeed = BuildWakeSeedData(
            panel,
            inviscidState,
            qinv,
            n,
            isp,
            nWake,
            settings.FreestreamVelocity,
            alphaRadians);
        InitializeXFoilStationMappingWithWakeSeed(
            blState,
            panel,
            qinv,
            isp,
            sst,
            n,
            nWake,
            wakeSeed,
            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization || inviscidState.UseLegacyPanelingPrecision);

        // Fortran XICALC computes WGAP from the BL arc-length (XSSI) arrays
        // using a cubic decay: WGAP(IW) = ANTE*(AA+BB*ZN)*ZN^2 where
        // ZN = 1 - (XSSI(IBL)-XSSI(IBLTE)) / (TELRAT*ANTE).
        // The managed BuildWakeGapProfile used a different distance metric
        // (Euclidean geometry) which produced different WGAP values,
        // causing 82K ULP HK2 at wake stations.
        if (settings.UseLegacyBoundaryLayerInitialization && wakeSeed is not null)
        {
            RecomputeWakeGapFromXssi(blState, wakeSeed, inviscidState, panel);
        }

        // Fortran UICALC sets UINV = VTI*QINV once, before MRCHUE/MRCHDU.
        // UINV is the pure inviscid edge velocity and is NEVER modified.
        // The C# laminar seed (RefineLaminarSeedStation) writes UEDG, so
        // ueInv must be captured NOW — before InitializeBLThwaitesXFoil
        // overwrites the inviscid UEDG at laminar stations.
        double[,] ueInv = XFoil.Solver.Numerics.SolverBuffers.UeInvScratch(maxStations, 2);
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
            {
                ueInv[ibl, side] = blState.UEDG[ibl, side];
            }
        }

        double reinf = settings.ReynoldsNumber;
        double qinf = settings.FreestreamVelocity;
        InitializeBLThwaitesXFoil(blState, settings, reinf, inviscidState.TrailingEdgeGap, wakeSeed);

        double mach = settings.MachNumber;
        double hvrat = GetHvRat(settings.UseLegacyBoundaryLayerInitialization);
        ComputeCompressibilityParameters(mach, qinf, reinf, hvrat,
            out double tkbl, out double qinfbl, out double tkbl_ms,
            out double hstinv, out double hstinv_ms,
            out double rstbl, out double rstbl_ms,
            out double reybl, out double reybl_re, out double reybl_ms,
            settings.UseLegacyBoundaryLayerInitialization);

        // D3 fix: run MRCHUE/MRCHDU unconditionally to place transition.
        // Before this change, the modern-init path skipped the BL re-march,
        // which left ITRAN[side] pinned at IBLTE (Thwaites init default,
        // transition at TE). The Newton-internal transition search cannot
        // recover from a TE-pinned start on cambered airfoils → Xtr → 1.0
        // → BL fully laminar → inviscid/viscous residuals explode to
        // infinity. Running the Fortran-style Remarch once here gives the
        // modern path a physical transition point, same as parity mode.
        //
        // Parity path (settings.UseLegacyBoundaryLayerInitialization=true)
        // is untouched: it already ran Remarch and still does. Only the
        // modern branch changes behavior.
        {
            // Fortran SETBL (xbl.f line 116): MRCHDU re-solves ALL stations
            // after MRCHUE, producing the definitive initialization state.
            RemarchBoundaryLayerLegacyDirect(
                blState,
                settings,
                inviscidState.TrailingEdgeGap,
                wakeSeed,
                tkbl,
                qinfbl,
                tkbl_ms,
                hstinv,
                hstinv_ms,
                rstbl,
                rstbl_ms,
                reybl,
                reybl_re,
                reybl_ms);

            // Dump post-init-MRCHDU for parity verification with Fortran F_PM traces
            
        }

        var (isysMap, nsys) = EdgeVelocityCalculator.MapStationsToSystemLines(iblte, nbl);
        // ThreadStatic pool: avoid per-case allocation of the 3.1 MB LOH
        // VM matrix (+ smaller VA/VB/VDEL arrays) inside ViscousNewtonSystem.
        // The cached instance is grown to the largest seen nsys; most Selig
        // cases converge to one size so this allocates once per worker thread.
        var newtonSystem = GetPooledViscousNewtonSystem(nsys + 1);
        newtonSystem.SetupISYS(isysMap, nsys);
        ConfigureNewtonSystemTopology(newtonSystem, blState, panel);

        RefreshCurrentVortexStrengthFromBoundaryLayer(
            blState,
            inviscidState,
            panel,
            isp,
            n,
            settings.FreestreamVelocity,
            settings.UseLegacyBoundaryLayerInitialization
                || inviscidState.UseLegacyKernelPrecision
                || inviscidState.UseLegacyPanelingPrecision);

        // xoper.f seeds XYWAKE/QISET before the legacy LBLINI/GAMQV path and
        // then enters QDCALC without rebuilding the wake geometry. Preserve that
        // ordering only for the parity path; the default managed branch can still
        // regenerate wake geometry from the current viscous gamma.
        InfluenceMatrixBuilder.WakeGeometryData? parityWakeGeometry =
            settings.UseLegacyBoundaryLayerInitialization ? wakeSeed?.Geometry : null;

        double[,] dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState,
            panel,
            nWake,
            settings.FreestreamVelocity,
            alphaRadians,
            parityWakeGeometry,
            settings.UseLegacyWakeSourceKernelPrecision);
        

        // Compute SST_GO/SST_GP once from the initial BL state (matching
        // Fortran XICALC which computes from the inviscid stagnation geometry).
        // At this point UEDG has the initial inviscid values, so the formula
        // -XSSI[1,side] / (UEDG[1,0]+UEDG[1,1]) matches Fortran's
        // (SST-S(I+1)) / (GAM(I)+GAM(I+1)).
        // SST_GO/SST_GP from FindStagnationPointXFoil (XICALC formula).
        double invSstGo = initSstGo;
        double invSstGp = initSstGp;

        return new PreNewtonSetupContext
        {
            Panel = panel,
            InviscidState = inviscidState,
            BoundaryLayerState = blState,
            NewtonSystem = newtonSystem,
            Dij = dij,
            UeInv = ueInv,
            MaxStations = maxStations,
            QInv = qinv,
            QInvCount = n,
            Isp = isp,
            Sst = sst,
            InviscidSstGo = invSstGo,
            InviscidSstGp = invSstGp,
            NodeCount = n,
            WakeCount = nWake,
            WakeSeed = wakeSeed,
            Tkbl = tkbl,
            QinfBl = qinfbl,
            TkblMs = tkbl_ms,
            HstInv = hstinv,
            HstInvMs = hstinv_ms,
            RstBl = rstbl,
            RstBlMs = rstbl_ms,
            ReyBl = reybl,
            ReyBlRe = reybl_re,
            ReyBlMs = reybl_ms
        };
    }

    /// <summary>
    /// Builds viscous panel speeds from BL edge velocities.
    /// Port of QVFUE from xpanel.f: QVIS(I) = VTI(IBL,IS) * UEDG(IBL,IS).
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: QVFUE
    // Difference from legacy: The mapping is the same, but the managed port returns a fresh array instead of overwriting a global working buffer.
    // Decision: Keep the immutable-style helper and preserve the original IPAN/VTI/UEDG mapping.
    private static double[] BuildViscousPanelSpeeds(
        BoundaryLayerSystemState blState,
        InviscidSolverState inviscidState,
        LinearVortexPanelState panel,
        int isp, int n,
        double qinf,
        bool useLegacyPrecision = false,
        double[]? destination = null)
    {
        double[] qvis = destination ?? new double[n];
        Array.Copy(inviscidState.InviscidSpeed, qvis, n);

        // Overwrite airfoil panels with viscous speeds using IPAN/VTI
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 1; ibl < blState.NBL[side] && ibl <= blState.IBLTE[side]; ibl++)
            {
                int iPan = blState.IPAN[ibl, side];
                if (iPan >= 0 && iPan < n)
                {
                    qvis[iPan] = LegacyPrecisionMath.Multiply(
                        blState.VTI[ibl, side],
                        blState.UEDG[ibl, side],
                        useLegacyPrecision);
                    
                }
            }
        }

        return qvis;
    }

    private static void RefreshCurrentVortexStrengthFromBoundaryLayer(
        BoundaryLayerSystemState blState,
        InviscidSolverState inviscidState,
        LinearVortexPanelState panel,
        int isp,
        int n,
        double qinf,
        bool useLegacyPrecision)
    {
        double[] qvis = BuildViscousPanelSpeeds(
            blState,
            inviscidState,
            panel,
            isp,
            n,
            qinf,
            useLegacyPrecision,
            destination: SolverBuffers.PanelScratch4(n));
        double[] gamma = EdgeVelocityCalculator.SetVortexFromViscousSpeed(
            qvis,
            n,
            qinf,
            useLegacyPrecision,
            destination: SolverBuffers.PanelScratch5(n));

        for (int i = 0; i < n; i++)
        {
            inviscidState.VortexStrength[i] = gamma[i];
        }

    }

    /// <summary>
    /// Converts UEDG back to panel-level speeds for stagnation point relocation.
    /// Uses IPAN/VTI mapping: QVIS(I) = VTI(IBL,IS) * UEDG(IBL,IS).
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: QVFUE-style speed reconstruction
    // Difference from legacy: This helper exists to support managed stagnation relocation without mutating the full inviscid state.
    // Decision: Keep the helper and preserve the original panel-speed mapping.
    private static double[] ConvertUedgToSpeeds(
        BoundaryLayerSystemState blState, int n,
        bool useLegacyPrecision = false,
        double[]? destination = null)
    {
        double[] speeds = destination ?? new double[n];
        Array.Clear(speeds, 0, n);

        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 1; ibl < blState.NBL[side] && ibl <= blState.IBLTE[side]; ibl++)
            {
                int iPan = blState.IPAN[ibl, side];
                if (iPan >= 0 && iPan < n)
                {
                    // Phase 1 strip: Fortran QVFUE GAM(I) = VTI(IBL,IS) * UEDG(IBL,IS) — REAL multiply.
                    speeds[iPan] = (float)blState.VTI[ibl, side] * (float)blState.UEDG[ibl, side];
                }
            }
        }

        return speeds;
    }

    // ================================================================
    // XFoil-compatible stagnation point finder (STFIND, xpanel.f:1338)
    // ================================================================

    /// <summary>
    /// Locates the stagnation point by finding where qinv changes sign (GAM(I) >= 0 and GAM(I+1) &lt; 0),
    /// then interpolates to get fractional arc-length SST.
    /// Port of STFIND from xpanel.f:1338-1373.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: STFIND
    // Difference from legacy: The parity path preserves the first-sign-change single-precision interpolation, while the default managed path keeps a more robust smallest-magnitude sign-change scan.
    // Decision: Keep the managed robustness improvement for default execution and preserve the classic STFIND behavior in parity mode.
    private static (int isp, double sst, double sstGo, double sstGp) FindStagnationPointXFoil(
        double[] qinv,
        LinearVortexPanelState panel,
        int n,
        bool useLegacyPrecision)
    {
        // Find where qinv changes from positive to negative (port of STFIND).
        // The C# linear vortex solver can produce spuriously large GAM values
        // at the TE nodes due to the zero-gap trailing edge singularity.
        // Select the sign change with the smallest combined magnitude,
        // which corresponds to the true stagnation point (near-zero crossing).
        // Phase 1 strip: classic XFoil STFIND accepts the FIRST sign change
        // and evaluates the interpolation entirely in single precision.
        int ist = -1;
        for (int i = 0; i < n - 1; i++)
        {
            if (qinv[i] >= 0.0 && qinv[i + 1] < 0.0)
            {
                ist = i;
                break;
            }
        }
        if (ist < 0) ist = n / 2;

        double sst;
        {
            float gammaLeft = (float)qinv[ist];
            float gammaRight = (float)qinv[ist + 1];
            float panelArcLeft = (float)panel.ArcLength[ist];
            float panelArcRight = (float)panel.ArcLength[ist + 1];
            float dgam = gammaRight - gammaLeft;
            float ds = panelArcRight - panelArcLeft;
            bool usedLeftNode = gammaLeft < -gammaRight;

            if (usedLeftNode)
                sst = panelArcLeft - ds * (gammaLeft / dgam);
            else
                sst = panelArcRight - ds * (gammaRight / dgam);

            if (sst <= panelArcLeft) sst = panelArcLeft + 1.0e-7f;
            if (sst >= panelArcRight) sst = panelArcRight - 1.0e-7f;
        }

        // Compute SST_GO/SST_GP using Fortran XICALC formula (xpanel.f:2263):
        //   SST_GO = (SST - S(I+1)) / DGAM
        //   SST_GP = (S(I) - SST) / DGAM
        // where DGAM = GAM(I+1) - GAM(I) (same dgam used for SST interpolation).
        double sstGo = 0.0, sstGp = 0.0;
        if (ist >= 0 && ist + 1 < n)
        {
            float dgamF = (float)qinv[ist + 1] - (float)qinv[ist];
            if (MathF.Abs(dgamF) > 1e-30f)
            {
                sstGo = ((float)sst - (float)panel.ArcLength[ist + 1]) / dgamF;
                sstGp = ((float)panel.ArcLength[ist] - (float)sst) / dgamF;
            }
        }

        return (ist, sst, sstGo, sstGp);
    }

    // ================================================================
    // XFoil-compatible station count computation (from IBLPAN)
    // ================================================================

    /// <summary>
    /// Computes BL station counts matching Fortran IBLPAN convention.
    /// Station 0 is virtual stagnation (not counted in IBLTE).
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: IBLPAN
    // Difference from legacy: The station-count logic is the same, but the managed code returns arrays instead of filling COMMON arrays in place.
    // Decision: Keep the explicit return values and preserve the original counting convention.
    private static (int[] iblte, int[] nbl) ComputeStationCountsXFoil(int n, int isp, int nWake)
    {
        int[] iblte = new int[2];
        int[] nbl = new int[2];

        // Side 0 (upper): panels ISP, ISP-1, ..., 0 → stations 1, 2, ..., ISP+1
        // Station 0 = virtual stag. IBLTE = ISP+1 (TE at panel 0).
        iblte[0] = isp + 1;
        nbl[0] = isp + 2;

        // Side 1 (lower): panels ISP+1, ISP+2, ..., N-1 → stations 1, 2, ..., N-1-ISP
        // Station 0 = virtual stag. IBLTE = N-1-ISP (TE at panel N-1).
        // Wake: stations IBLTE+1..IBLTE+nWake
        iblte[1] = n - 1 - isp;
        nbl[1] = (n - 1 - isp) + 1 + nWake;

        return (iblte, nbl);
    }

    // ================================================================
    // XFoil-compatible station mapping (IBLPAN + XICALC + UICALC)
    // ================================================================

    /// <summary>
    /// Populates IPAN, VTI, XSSI, UEDG for all BL stations on both sides.
    /// Combines Fortran IBLPAN (xpanel.f:1376), XICALC (xpanel.f:1436), and UICALC (xpanel.f:1523).
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: IBLPAN/XICALC/UICALC
    // Difference from legacy: This overload is just a managed wrapper that forwards to the wake-aware implementation.
    // Decision: Keep the wrapper so the no-wake-seed call sites stay simple.
    private static void InitializeXFoilStationMapping(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double[] qinv,
        int isp, double sst, int n, int nWake,
        bool useLegacyPrecision = false,
        bool initializeUedg = true)
    {
        InitializeXFoilStationMappingWithWakeSeed(
            blState,
            panel,
            qinv,
            isp,
            sst,
            n,
            nWake,
            wakeSeed: null,
            useLegacyPrecision,
            initializeUedg);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: IBLPAN/XICALC/UICALC
    // Difference from legacy: The mapping semantics are preserved, but the managed port can seed wake geometry explicitly and mirrors the wake onto side 0 for plotting/state convenience.
    // Decision: Keep the wake-seed aware helper and preserve the original airfoil/wake station layout.
    private static void InitializeXFoilStationMappingWithWakeSeed(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double[] qinv,
        int isp, double sst, int n, int nWake,
        WakeSeedData? wakeSeed,
        bool useLegacyPrecision = false,
        bool initializeUedg = true,
        bool xssiOnly = false)
    {
        // Minimum XSSI near stagnation (XFEPS in Fortran)
        // Legacy block: xpanel.f :: XICALC REAL XSSI staging.
        // Difference: The managed path keeps doubles by default; parity mode rounds the arc-length differences through the explicit legacy REAL helpers.
        // Decision: Keep the default double path and preserve single-precision XSSI construction in parity mode.
        double chordArcLength = LegacyPrecisionMath.Subtract(panel.ArcLength[n - 1], panel.ArcLength[0], useLegacyPrecision);
        double xeps = LegacyPrecisionMath.Multiply(1.0e-7, chordArcLength, useLegacyPrecision);

        // --- Side 0 (upper surface) ---
        // Station 0: virtual stagnation
        if (!xssiOnly) { blState.IPAN[0, 0] = -1; blState.VTI[0, 0] = 1.0; }
        blState.XSSI[0, 0] = 0.0;
        if (initializeUedg)
        {
            blState.UEDG[0, 0] = 0.0;
        }

        // Stations 1..IBLTE[0]: airfoil panels ISP, ISP-1, ..., 0
        for (int ibl = 1; ibl <= blState.IBLTE[0]; ibl++)
        {
            int iPan = xssiOnly ? blState.IPAN[ibl, 0] : isp - (ibl - 1);
            if (!xssiOnly)
            {
                blState.IPAN[ibl, 0] = iPan;
                blState.VTI[ibl, 0] = 1.0;
            }
            double stationXi = LegacyPrecisionMath.Subtract(sst, panel.ArcLength[iPan], useLegacyPrecision);
            blState.XSSI[ibl, 0] = LegacyPrecisionMath.Max(stationXi, xeps, useLegacyPrecision);
            if (initializeUedg)
            {
                blState.UEDG[ibl, 0] = blState.VTI[ibl, 0] * qinv[iPan];
            }
        }

        // --- Side 1 (lower surface) ---
        // Station 0: virtual stagnation
        if (!xssiOnly) { blState.IPAN[0, 1] = -1; blState.VTI[0, 1] = -1.0; }
        blState.XSSI[0, 1] = 0.0;
        if (initializeUedg)
        {
            blState.UEDG[0, 1] = 0.0;
        }

        // Stations 1..IBLTE[1]: airfoil panels ISP+1, ISP+2, ..., N-1
        for (int ibl = 1; ibl <= blState.IBLTE[1]; ibl++)
        {
            int iPan = xssiOnly ? blState.IPAN[ibl, 1] : isp + ibl;
            if (!xssiOnly)
            {
                blState.IPAN[ibl, 1] = iPan;
                blState.VTI[ibl, 1] = -1.0;
            }
            double stationXi = LegacyPrecisionMath.Subtract(panel.ArcLength[iPan], sst, useLegacyPrecision);
            blState.XSSI[ibl, 1] = LegacyPrecisionMath.Max(stationXi, xeps, useLegacyPrecision);
            if (initializeUedg)
            {
                blState.UEDG[ibl, 1] = blState.VTI[ibl, 1] * qinv[iPan];
            }
        }

        // --- Wake (on side 1) ---
        // First wake station: XSSI = XSSI at TE
        int iblteS1 = blState.IBLTE[1];
        if (iblteS1 + 1 < blState.MaxStations)
        {
            blState.XSSI[iblteS1 + 1, 1] = blState.XSSI[iblteS1, 1];
        }

        // Also set first wake on side 0
        int iblteS0 = blState.IBLTE[0];
        if (iblteS0 + 1 < blState.MaxStations)
        {
            blState.XSSI[iblteS0 + 1, 0] = blState.XSSI[iblteS0, 0];
        }

        for (int iw = 1; iw <= nWake; iw++)
        {
            int iblW1 = iblteS1 + iw;
            if (iblW1 >= blState.MaxStations) break;

            // Wake panel index: n + iw - 1 (matches Fortran I = N + IW)
            if (!xssiOnly) { blState.IPAN[iblW1, 1] = n + iw - 1; blState.VTI[iblW1, 1] = -1.0; }

            if (iw > 1)
            {
                double wakeDx = GetWakeStationSpacing(wakeSeed, iw - 1, blState, iblteS1, useLegacyPrecision);
                blState.XSSI[iblW1, 1] = LegacyPrecisionMath.Add(blState.XSSI[iblW1 - 1, 1], wakeDx, useLegacyPrecision);
            }

            if (initializeUedg)
            {
                blState.UEDG[iblW1, 1] = GetWakeStationEdgeVelocity(blState, wakeSeed, iw);
            }

            // Mirror on side 0 (for plotting, matching Fortran)
            int iblW0 = iblteS0 + iw;
            if (iblW0 < blState.MaxStations)
            {
                if (!xssiOnly) { blState.IPAN[iblW0, 0] = blState.IPAN[iblW1, 1]; blState.VTI[iblW0, 0] = 1.0; }
                blState.XSSI[iblW0, 0] = blState.XSSI[iblW1, 1];
                if (initializeUedg)
                {
                    blState.UEDG[iblW0, 0] = blState.UEDG[iblW1, 1];
                }
            }
        }

    }

    // Legacy mapping: legacy-derived from xpanel/xwake wake seeding concepts
    // Difference from legacy: This is a managed-only wake precomputation helper built around the analytical influence kernels and explicit geometry objects.
    // Decision: Keep it as a managed improvement that feeds the legacy-compatible wake consumers.
    private static WakeSeedData? BuildWakeSeedData(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        double[] qinv,
        int qinvCount,
        int isp,
        int nWake,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        if (nWake <= 0)
        {
            return null;
        }

        (int[] overlayIndices, int overlayCount, double[] currentGamma) = GetLegacyWakeSeedGammaOverlay(
            inviscidState,
            qinv,
            qinvCount,
            isp,
            freestreamSpeed);
        double[]? restoredGamma = null;
        if (overlayCount > 0)
        {
            restoredGamma = XFoil.Solver.Numerics.SolverBuffers.WakeSeedRestoredGamma(overlayCount);
            for (int overlay = 0; overlay < overlayCount; overlay++)
            {
                int index = overlayIndices[overlay];
                restoredGamma[overlay] = inviscidState.VortexStrength[index];
                inviscidState.VortexStrength[index] = currentGamma[index];
            }

        }

        try
        {
            var wakeGeometry = InfluenceMatrixBuilder.BuildWakeGeometryData(
                panel,
                inviscidState,
                nWake,
                freestreamSpeed,
                angleOfAttackRadians);

            var rawSpeeds = XFoil.Solver.Numerics.SolverBuffers.WakeSeedRawSpeeds(nWake);
            rawSpeeds[0] = (qinvCount > 0) ? qinv[qinvCount - 1] : 0.0;
            // Try the fix: also compute rawSpeeds[0] via ComputeInfluenceAt at FIRST wake position

            // Fortran QWCALC: PSILIN at each wake panel computes QTAN1/QTAN2
            // from the basis gammas (GAMU). Then QISET blends: Q = Q1*cos + Q2*sin.
            // Match this sum-then-blend order by temporarily setting VortexStrength
            // to each basis gamma and calling PSILIN separately for each.
            int n = panel.NodeCount;
            bool useBasisDecomposition = inviscidState.UseLegacyKernelPrecision;
            if (useBasisDecomposition)
            {
                var savedGamma = XFoil.Solver.Numerics.SolverBuffers.WakeSeedSavedGamma(n + 1);
                for (int i = 0; i <= n; i++)
                    savedGamma[i] = inviscidState.VortexStrength[i];

                double cosA = LegacyPrecisionMath.Cos(angleOfAttackRadians, true);
                double sinA = LegacyPrecisionMath.Sin(angleOfAttackRadians, true);

                for (int iw = 1; iw < nWake; iw++)
                {
                    int wakeIdx = n + iw;
                    // Basis 1: set VortexStrength to BasisVortexStrength[:, 0]
                    for (int i = 0; i <= n; i++)
                        inviscidState.VortexStrength[i] = inviscidState.BasisVortexStrength[i, 0];
                    (_, double qtan1) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                        wakeIdx, wakeGeometry.X[iw], wakeGeometry.Y[iw],
                        wakeGeometry.NormalX[iw], wakeGeometry.NormalY[iw],
                        false, false, panel, inviscidState, freestreamSpeed, 0.0);
                    // Basis 2: set VortexStrength to BasisVortexStrength[:, 1]
                    for (int i = 0; i <= n; i++)
                        inviscidState.VortexStrength[i] = inviscidState.BasisVortexStrength[i, 1];
                    (_, double qtan2) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                        wakeIdx, wakeGeometry.X[iw], wakeGeometry.Y[iw],
                        wakeGeometry.NormalX[iw], wakeGeometry.NormalY[iw],
                        false, false, panel, inviscidState, freestreamSpeed, Math.PI / 2.0);
                    // QISET: Q = Q1*cos + Q2*sin
                    rawSpeeds[iw] = LegacyPrecisionMath.SumOfProducts(
                        qtan1, cosA, qtan2, sinA, true);
                    
                }
                // Restore blended gamma
                for (int i = 0; i <= n; i++)
                    inviscidState.VortexStrength[i] = savedGamma[i];
            }
            else
            {
                for (int iw = 1; iw < nWake; iw++)
                {
                    (_, rawSpeeds[iw]) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                        panel.NodeCount + iw, wakeGeometry.X[iw], wakeGeometry.Y[iw],
                        wakeGeometry.NormalX[iw], wakeGeometry.NormalY[iw],
                        false, false, panel, inviscidState, freestreamSpeed, angleOfAttackRadians);
                }
            }


            double[] gapProfile = BuildWakeGapProfile(wakeGeometry, inviscidState, panel);
            // Raw TE normal gap for parity: Fortran's DTE = DSTR_TE1 + DSTR_TE2 + ANTE
            // uses the SIGNED ANTE directly (from cross product DXS*DYTE - DYS*DXTE),
            // not the absolute value. Using Math.Abs here was a bug that broke parity
            // for Selig airfoils whose TE orientation produces negative ANTE.
            double anteRaw = inviscidState.TrailingEdgeAngleNormal;


            return new WakeSeedData(wakeGeometry, rawSpeeds, nWake, gapProfile, wakeGeometry.Count, anteRaw);
        }
        finally
        {
            if (restoredGamma is not null)
            {
                for (int overlay = 0; overlay < overlayCount; overlay++)
                {
                    inviscidState.VortexStrength[overlayIndices[overlay]] = restoredGamma[overlay];
                }
            }
        }
    }

    private static (int[] overlayIndices, int overlayCount, double[] currentGamma) GetLegacyWakeSeedGammaOverlay(
        InviscidSolverState inviscidState,
        double[] qinv,
        int qinvCount,
        int isp,
        double freestreamSpeed)
    {
        bool useLegacyPrecision = inviscidState.UseLegacyKernelPrecision || inviscidState.UseLegacyPanelingPrecision;
        int n = Math.Min(inviscidState.NodeCount, qinvCount);
        if (!useLegacyPrecision || n <= 0)
        {
            return (Array.Empty<int>(), 0, Array.Empty<double>());
        }

        double[] currentGamma = EdgeVelocityCalculator.SetVortexFromViscousSpeed(
            qinv,
            n,
            freestreamSpeed,
            useLegacyPrecision,
            destination: SolverBuffers.PanelScratch6(n));
        // Pooled bitset replaces HashSet<int> — indices fit densely in 0..n,
        // so a bool[n] dedupes allocations for the two loops below. The
        // sorted-order `OrderBy + ToArray` step is just a scan in index order
        // over the bitset.
        bool[] bitset = SolverBuffers.OverlayBitset(n);

        bool Differs(int index)
            => BitConverter.SingleToInt32Bits((float)inviscidState.VortexStrength[index]) !=
               BitConverter.SingleToInt32Bits((float)currentGamma[index]);

        for (int index = 1; index < n - 1; index++)
        {
            if (!Differs(index))
            {
                continue;
            }

            if (Differs(index - 1) || Differs(index + 1))
            {
                continue;
            }

            bitset[index] = true;
        }

        int stagnationIndex = Math.Max(0, Math.Min(n - 1, isp));
        int runStart = -1;
        for (int index = 0; index < n; index++)
        {
            bool differs = Differs(index);
            if (differs && runStart < 0)
            {
                runStart = index;
            }

            bool endRun = runStart >= 0 && (!differs || index == n - 1);
            if (!endRun)
            {
                continue;
            }

            int runEnd = differs && index == n - 1 ? index : index - 1;
            if (runStart <= stagnationIndex && stagnationIndex <= runEnd)
            {
                for (int runIndex = runStart; runIndex <= runEnd; runIndex++)
                {
                    bitset[runIndex] = true;
                }
            }

            runStart = -1;
        }

        int[] overlayIndices = SolverBuffers.OverlayIndicesBuffer(n);
        int overlayCount = 0;
        for (int index = 0; index < n; index++)
        {
            if (bitset[index])
            {
                overlayIndices[overlayCount++] = index;
            }
        }
        return (overlayIndices, overlayCount, currentGamma);
    }

    // Legacy mapping: legacy-derived from XFoil WGAP wake profile behavior
    // Difference from legacy: The managed port computes the cubic wake-gap profile explicitly from the wake geometry instead of recovering it from older implicit state.
    // Decision: Keep the explicit profile builder because it makes the wake-gap input deterministic and auditable.
    private static double[] BuildWakeGapProfile(
        InfluenceMatrixBuilder.WakeGeometryData wakeGeometry,
        InviscidSolverState inviscidState,
        LinearVortexPanelState panel)
    {
        double[] gapProfile = XFoil.Solver.Numerics.SolverBuffers.WakeGapProfileScratch(wakeGeometry.Count);
        // Fortran xpanel.f line 2518: WGAP(IW) = ANTE * (AA + BB*ZN)*ZN**2 — uses
        // signed ANTE (cross product). Using Math.Abs here was a parity bug.
        double normalGap = inviscidState.TrailingEdgeAngleNormal;
        bool sharpTrailingEdge = inviscidState.IsSharpTrailingEdge || Math.Abs(normalGap) <= 1e-9;
        if (wakeGeometry.Count == 0)
        {
            return gapProfile;
        }

        // Phase 1 strip: float-only path. The doubled tree (auto-generated
        // *.Double.cs twin via gen-double.py) gets the double-precision mirror.
        //
        // Fortran xpanel.f:2483-2490 — DWDXTE comes from the AIRFOIL TE panel
        // derivatives XP/YP at nodes 1 and N, not from the wake segment tangent:
        //   CROSP = (XP(1)*YP(N) - YP(1)*XP(N))
        //         / SQRT((XP(1)^2 + YP(1)^2) * (XP(N)^2 + YP(N)^2))
        //   DWDXTE = CROSP / SQRT(1 - CROSP^2), clamped to [-3/TELRAT, +3/TELRAT].
        double wakeGapDerivative;
        if (panel.NodeCount >= 2)
        {
            int nLast = panel.NodeCount - 1;
            float xp1 = (float)panel.XDerivative[0];
            float yp1 = (float)panel.YDerivative[0];
            float xpN = (float)panel.XDerivative[nLast];
            float ypN = (float)panel.YDerivative[nLast];
            float crosp = (xp1 * ypN - yp1 * xpN)
                        / MathF.Sqrt((xp1 * xp1 + yp1 * yp1) * (xpN * xpN + ypN * ypN));
            float dwdx = crosp / MathF.Sqrt(1f - crosp * crosp);
            const float clampF = 3f / 2.5f;
            if (dwdx < -clampF) dwdx = -clampF;
            if (dwdx > clampF) dwdx = clampF;
            wakeGapDerivative = dwdx;
        }
        else
        {
            wakeGapDerivative = 0.0;
        }

        // Fortran XICALC (xpanel.f:2468-2473) accumulates XSSI in REAL*4:
        //   DXSSI = SQRT((X(I)-X(I-1))**2 + (Y(I)-Y(I-1))**2)
        //   XSSI(IBL) = XSSI(IBL-1) + DXSSI
        // XYWAKE (xpanel.f:2504) uses `XSSI(IBL,IS) - XSSI(IBLTE(IS),IS)` as
        // the distance argument. Critical precision detail: adding small DXSSI
        // to the LARGE airfoil XSSI (= full perimeter arc length) and
        // subtracting back is NOT a no-op — float-rounding the sum loses ~4-5
        // low mantissa bits, so the subtraction yields a slightly smaller
        // distance than the direct DXSSI accumulation. `baseArcF` approximates
        // Fortran's XSSI[IBLTE,2].
        int panelLastIdx = panel.NodeCount - 1;
        float baseArcF = (float)panel.ArcLength[panelLastIdx] - (float)panel.LeadingEdgeArcLength;
        float accumArcF = baseArcF;
        float distanceF = 0f;

        for (int iw = 0; iw < wakeGeometry.Count; iw++)
        {
            if (iw > 0)
            {
                float dxF = (float)wakeGeometry.X[iw] - (float)wakeGeometry.X[iw - 1];
                float dyF = (float)wakeGeometry.Y[iw] - (float)wakeGeometry.Y[iw - 1];
                float dxssi = MathF.Sqrt((dxF * dxF) + (dyF * dyF));
                accumArcF += dxssi;
                distanceF = accumArcF - baseArcF;
            }

            gapProfile[iw] = WakeGapProfile.Evaluate(
                normalGap,
                distanceF,
                wakeGapDerivative,
                sharpTrailingEdge);
        }

        return gapProfile;
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only helper that falls back to station-spacing heuristics when explicit wake geometry is unavailable.
    // Decision: Keep the helper because the managed wake seed can be optional.
    private static double GetWakeStationSpacing(
        WakeSeedData? wakeSeed,
        int wakeIndex,
        BoundaryLayerSystemState blState,
        int iblteLower,
        bool useLegacyPrecision = false)
    {
        if (wakeSeed?.Geometry.Count > wakeIndex)
        {
            // Phase 1 strip: float-only Fortran XICALC DXSSI.
            //   DXSSI = SQRT((X(I)-X(I-1))**2 + (Y(I)-Y(I-1))**2)
            float xCurr = (float)wakeSeed.Geometry.X[wakeIndex];
            float xPrev = (float)wakeSeed.Geometry.X[wakeIndex - 1];
            float yCurr = (float)wakeSeed.Geometry.Y[wakeIndex];
            float yPrev = (float)wakeSeed.Geometry.Y[wakeIndex - 1];
            float dxf = xCurr - xPrev;
            float dyf = yCurr - yPrev;
            return MathF.Sqrt(dxf * dxf + dyf * dyf);
        }

        return blState.XSSI[iblteLower, 1] / Math.Max(iblteLower, 1) * 2.0;
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only accessor that chooses between explicit wake-seed speeds and a TE-average fallback.
    // Decision: Keep the helper because it centralizes wake seeding policy.
    private static double GetWakeStationEdgeVelocity(
        BoundaryLayerSystemState blState,
        WakeSeedData? wakeSeed,
        int wakeStation)
    {
        if (wakeSeed is not null && wakeSeed.RawSpeedsCount >= wakeStation)
        {
            return blState.VTI[blState.IBLTE[1] + wakeStation, 1] * wakeSeed.RawSpeeds[wakeStation - 1];
        }

        int upperTe = blState.IBLTE[0];
        int lowerTe = blState.IBLTE[1];
        return 0.5 * (Math.Abs(blState.UEDG[upperTe, 0]) + Math.Abs(blState.UEDG[lowerTe, 1]));
    }

    private static double ComputeMassDefect(double dstar, double ue, bool useLegacyPrecision = false)
    {
        return useLegacyPrecision
            ? LegacyPrecisionMath.Multiply(dstar, ue, useLegacyPrecision: true)
            : dstar * ue;
    }

    // ================================================================
    // B3 warm-start seed application
    // ================================================================

    /// <summary>
    /// Overwrites the Thwaites-initialized BL primary unknowns with a seed
    /// from a previously-converged operating point at a nearby α.
    /// NBL mismatch silently skips the seed — mixing BL grids produces
    /// invalid state. Only THET/DSTR/CTAU/UEDG/ITRAN are seeded; secondary
    /// fields (MASS, TAU, wake continuity) regenerate from the first
    /// Newton pass.
    /// </summary>
    private static void ApplyBLSeed(
        BoundaryLayerSystemState blState,
        ViscousBLSeed seed,
        int currentISP,
        bool useLegacyPrecision)
    {
        if (seed.NBL.Length < 2) return;

        // Phase 1: drop seed arrays into blState using the seed's
        // (pre-shift) NBL extents. Temporarily restore blState.NBL so
        // MoveStagnationPoint sees the old grid in seed-indexed form.
        int[] newNBL = new[] { blState.NBL[0], blState.NBL[1] };
        for (int side = 0; side < 2; side++)
        {
            int seedN = System.Math.Min(seed.NBL[side], blState.THET.GetLength(0));
            for (int ibl = 0; ibl < seedN; ibl++)
            {
                blState.THET[ibl, side] = seed.THET[ibl, side];
                blState.DSTR[ibl, side] = seed.DSTR[ibl, side];
                blState.CTAU[ibl, side] = seed.CTAU[ibl, side];
                blState.UEDG[ibl, side] = seed.UEDG[ibl, side];
                // Iter 46: e-n amplification carry seeding. Fortran XFoil
                // inherits this field across sequential `alfa` calls via
                // COMMON blocks — it sets the amplification factor at
                // each station that determines transition location.
                // Seeding it should narrow the 4× CL gap at mid-stall
                // cases (Fortran CL=1.55 vs my ramp 1.11 at α=14.2°
                // M=0.15).
                if (seed.AmplificationCarry is not null
                    && ibl < seed.AmplificationCarry.GetLength(0))
                {
                    blState.LegacyAmplificationCarry[ibl, side] =
                        seed.AmplificationCarry[ibl, side];
                }
            }
            if (side < seed.ITRAN.Length)
            {
                blState.ITRAN[side] = seed.ITRAN[side];
            }
            // Iter 56: also seed TINDEX (fractional transition xi
            // within ITRAN's station). Paired with ITRAN gives
            // Fortran-style transition continuity.
            if (seed.TIndex is not null && side < seed.TIndex.Length)
            {
                blState.TINDEX[side] = seed.TIndex[side];
            }
        }

        // Phase 2: if ISP shifted between seed α and current α, invoke
        // STMOVE (StagnationPointTracker.MoveStagnationPoint) to remap
        // the seed's state onto the new grid. The tracker needs the
        // NEW NBL in blState.NBL but also the OLD (seed) NBL as counts.
        if (seed.ISP != currentISP)
        {
            // blState.NBL already holds the new (current α) values from
            // the Thwaites setup; MoveStagnationPoint reads them via
            // blState.NBL[0/1] and needs the old counts as params.
            StagnationPointTracker.MoveStagnationPoint(
                blState,
                oldISP: seed.ISP,
                newISP: currentISP,
                oldUpperCount: seed.NBL[0],
                oldLowerCount: seed.NBL[1],
                useLegacyPrecision);
        }

        // MoveStagnationPoint remaps THET/DSTR/CTAU/UEDG but does NOT touch
        // MASS. After seeding DSTR and UEDG, MASS = DSTR·UEDG is stale
        // (still reflects Thwaites init). Recompute for consistency —
        // Newton's BL equations treat MASS as an independent unknown but
        // they're coupled via this algebraic identity at convergence, so
        // starting with the correct value is a better initial guess.
        for (int side = 0; side < 2; side++)
        {
            int nbl = blState.NBL[side];
            for (int ibl = 0; ibl < nbl; ibl++)
            {
                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
            }
        }
    }

    // ================================================================
    // XFoil-compatible Thwaites initialization (MRCHUE, xbl.f:554-564)
    // ================================================================

    /// <summary>
    /// Initializes BL variables using Thwaites formula matching XFoil's MRCHUE.
    /// Key differences from old code: TSQ = 0.45*XSI/(6*UEI*REYBL), DSI = 2.2*THI.
    /// Station 0 = virtual stag (UEDG=0, MASS=0), Station 1 = similarity.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE
    // Difference from legacy: This overload is a managed wrapper that forwards to the full wake-aware initializer.
    // Decision: Keep the wrapper so callers that do not have wake seed data can still use the legacy-compatible initializer.
    private static void InitializeBLThwaitesXFoil(
        BoundaryLayerSystemState blState, AnalysisSettings settings, double reinf)
    {
        InitializeBLThwaitesXFoil(blState, settings, reinf, teGap: 0.0, wakeSeed: null);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE
    // Difference from legacy: The initializer preserves the overall MRCHUE seeding flow, but the managed port makes the similarity-station refinement, downstream laminar refinement, and wake handling explicit and splits the exact parity remarch into dedicated helpers.
    // Decision: Keep the decomposed initializer and preserve the legacy seed order and parity-only remarch behavior.
    private static void InitializeBLThwaitesXFoil(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf,
        double teGap,
        WakeSeedData? wakeSeed)
    {
        double hvrat = GetHvRat(settings.UseLegacyBoundaryLayerInitialization);
        ComputeCompressibilityParameters(
            settings.MachNumber, settings.FreestreamVelocity, reinf, hvrat,
            out double tkbl, out double qinfbl, out double tkbl_ms,
            out double hstinv, out double hstinv_ms,
            out double rstbl, out double rstbl_ms,
            out double reybl, out double reybl_re, out double reybl_ms,
            settings.UseLegacyBoundaryLayerInitialization);
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);

        for (int side = 0; side < 2; side++)
        {
            // Thwaites at similarity station (station 1 = Fortran IBL=2)
            double xsi0 = Math.Max(blState.XSSI[1, side], 1e-10);
            double ue0 = Math.Max(Math.Abs(blState.UEDG[1, side]), 1e-10);

            double thi;
            double dsi;
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // MRCHUE seeds the similarity station through the original REAL
                // UCON/TSQ chain. Rewriting it as a simplified wide expression
                // shifts the very first seed theta/dstar bits.
                float xsi0f = (float)xsi0;
                float ue0f = (float)ue0;
                float reinff = (float)reinf;
                float bulef = 1.0f;
                float uconf = LegacyPrecisionMath.DivideF(
                    ue0f,
                    LegacyPrecisionMath.PowF(xsi0f, bulef));
                float tsqf = LegacyPrecisionMath.MultiplyF(
                    LegacyPrecisionMath.DivideF(
                        0.45f,
                        LegacyPrecisionMath.MultiplyF(
                            LegacyPrecisionMath.MultiplyF(
                                uconf,
                                LegacyPrecisionMath.AddF(
                                    LegacyPrecisionMath.MultiplyF(5.0f, bulef),
                                    1.0f)),
                            reinff)),
                    LegacyPrecisionMath.PowF(
                        xsi0f,
                        LegacyPrecisionMath.SubtractF(1.0f, bulef)));
                tsqf = MathF.Max(tsqf, 1.0e-20f);
                float thif = MathF.Sqrt(tsqf);
                float dsif = LegacyPrecisionMath.MultiplyF(2.2f, thif);
                thi = thif;
                dsi = dsif;
                
            }
            else
            {
                // Fortran: BULE=1.0, UCON=UEI/XSI, TSQ=0.45/(UCON*6*REYBL)*XSI^0
                // Simplifies to: TSQ = 0.45*XSI/(6*UEI*REYBL)
                double tsq = Math.Max(0.45 * xsi0 / (6.0 * ue0 * reinf), 1e-20);
                thi = Math.Sqrt(tsq);
                dsi = 2.2 * thi;  // Fortran: DSI = 2.2*THI (not 2.6)
            }

            blState.ITRAN[side] = blState.IBLTE[side];

            // Station 0: virtual stagnation
            blState.THET[0, side] = thi;
            blState.DSTR[0, side] = dsi;
            blState.CTAU[0, side] = 0.0;
            blState.MASS[0, side] = 0.0; // UEDG=0 at virtual stag

            // Station 1: similarity station
            blState.THET[1, side] = thi;
            blState.DSTR[1, side] = dsi;
            blState.CTAU[1, side] = 0.0;
            blState.MASS[1, side] = settings.UseLegacyBoundaryLayerInitialization
                ? LegacyPrecisionMath.Multiply(dsi, blState.UEDG[1, side], useLegacyPrecision: true)
                : dsi * blState.UEDG[1, side];

            RefineSimilarityStationSeed(
                blState, side, settings,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                rstbl, rstbl_ms,
                reybl, reybl_re, reybl_ms);

            double? forcedTransitionXi = GetLegacyParityForcedTransitionXi(blState, settings, side);

            // Fortran MRCHUE carries CTI as a local variable across stations
            double carriedCtau = LegacyLaminarShearSeed;

            // Stations 2..IBLTE: march with Thwaites integral
            for (int ibl = 2; ibl <= blState.IBLTE[side]; ibl++)
            {
                
                double uei = Math.Abs(blState.UEDG[ibl, side]);
                if (uei < 1e-10) uei = 1e-10;
                double uePrev = Math.Max(Math.Abs(blState.UEDG[ibl - 1, side]), 1e-10);

                double dx = blState.XSSI[ibl, side] - blState.XSSI[ibl - 1, side];
                if (dx < 1e-12) dx = 1e-6;
                double ueAvg = 0.5 * (uei + uePrev);
                double thetaPrev = blState.THET[ibl - 1, side];

                double theta;
                double dstar;
                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    // Classic MRCHUE does not rebuild a new Thwaites state at each
                    // downstream laminar station. It carries the upstream converged
                    // station forward and lets the local Newton solve reshape it.
                    // Recomputing a fresh Thwaites seed here changes the station
                    // input before the parity solve even begins.
                    theta = Math.Max(thetaPrev, 1e-20);
                    dstar = Math.Max(blState.DSTR[ibl - 1, side], 1.00005 * theta);
                }
                else
                {
                    // The default managed initializer still uses a Thwaites-style
                    // downstream prediction because it has been the more stable
                    // non-parity seed in the C# solver.
                    double theta2 = thetaPrev * thetaPrev * Math.Pow(uePrev / uei, 5.0)
                        + 0.45 / (reinf * Math.Pow(uei, 6.0)) * Math.Pow(ueAvg, 5.0) * dx;
                    theta = Math.Sqrt(Math.Max(theta2, 1e-20));

                    double dUedx = (uei - uePrev) / dx;
                    double lambda = theta * theta * reinf * dUedx;
                    lambda = Math.Max(-0.09, Math.Min(0.09, lambda));

                    double hkSeed = 2.61 - 3.75 * lambda - 5.24 * lambda * lambda;
                    hkSeed = Math.Max(1.5, Math.Min(hkSeed, 3.5));
                    dstar = hkSeed * theta;
                }

                blState.THET[ibl, side] = theta;
                blState.DSTR[ibl, side] = dstar;
                blState.CTAU[ibl, side] = blState.CTAU[ibl - 1, side];
                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    double carriedAmplification = ReadLegacyAmplificationCarry(
                        blState,
                        ibl - 1,
                        side,
                        blState.CTAU[ibl - 1, side]);
                    WriteLegacyAmplificationCarry(blState, ibl, side, carriedAmplification);
                }

                
                // Trace MRCHUE seed at side 2 station 4
                
                if (settings.UseLegacyBoundaryLayerInitialization ||
                    blState.ITRAN[side] >= blState.IBLTE[side])
                {
                    RefineLaminarSeedStation(
                        blState, side, ibl, settings,
                        tkbl, qinfbl, tkbl_ms,
                        hstinv, hstinv_ms,
                        rstbl, rstbl_ms,
                        reybl, reybl_re, reybl_ms,
                        ref carriedCtau);
                }

                // The legacy MRCHUE seed path performs TRCHEK inside the local
                // Newton loop before the station system is assembled. Running a
                // second post-convergence transition probe here invents an
                // extra BLKIN/TRCHEK interval that the reference solver never
                // emits and shifts the next comparable event boundary.
                if (!settings.UseLegacyBoundaryLayerInitialization &&
                    ibl > 2 &&
                    blState.ITRAN[side] >= blState.IBLTE[side])
                {
                    double ncrit = settings.GetEffectiveNCrit(side);
                    double xsi = blState.XSSI[ibl, side];
                    double xsiPrev = blState.XSSI[ibl - 1, side];
                    theta = blState.THET[ibl, side];
                    dstar = blState.DSTR[ibl, side];
                    double amplPrev = blState.CTAU[ibl - 1, side];

                    double hkPrev;
                    double rtPrev;
                    double hk;
                    double rt;
                    hkPrev = (blState.THET[ibl - 1, side] > 1e-30)
                        ? blState.DSTR[ibl - 1, side] / blState.THET[ibl - 1, side]
                        : 2.1;
                    rtPrev = Math.Max(reinf * uePrev * blState.THET[ibl - 1, side], 200.0);
                    hk = (theta > 1e-30) ? dstar / theta : 2.1;
                    rt = Math.Max(reinf * uei * theta, 200.0);

                    if (xsi > xsiPrev)
                    {
                        var trResult = TransitionModel.CheckTransition(
                            xsiPrev, xsi, amplPrev, 0.0, ncrit,
                            hkPrev, blState.THET[ibl - 1, side], rtPrev, uePrev,
                            blState.DSTR[ibl - 1, side],
                            hk, theta, rt, uei, dstar,
                            settings.UseModernTransitionCorrections, forcedTransitionXi,
                            settings.UseLegacyBoundaryLayerInitialization);

                        if (trResult.TransitionOccurred)
                            blState.ITRAN[side] = ibl;
                    }
                }

                blState.MASS[ibl, side] = ComputeMassDefect(
                    blState.DSTR[ibl, side],
                    blState.UEDG[ibl, side],
                    settings.UseLegacyBoundaryLayerInitialization);
            }

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // The parity path already replays the full airfoil-surface
                // march through RefineLegacySeedStation. The only remaining
                // MRCHUE continuation that still needs an explicit replay here
                // is the lower wake branch; re-running the upper turbulent tail
                // a second time clobbers the already-correct station-17+ surface
                // packets before SETBL ever starts.
                if (side == 1)
                {
                    PrimeLegacyWakeSeedStations(blState, teGap, wakeSeed);
                }

                // Fortran MRCHUE at ITRAN==IBLTE runs turbulent Newton. When
                // that Newton fails catastrophically (DMAX > 0.1), Fortran
                // applies CTI=0.05 fallback (xbl.f:2200). C# skips running
                // turbulent Newton at this station, so CTAU stays at the
                // laminar amplification value. A stored CTAU < CRIT implies
                // the laminar march produced a sub-turbulent value that
                // Fortran's turbulent Newton would have found wildly wrong
                // and fallen back to 0.05. Use 1e-3 as the heuristic threshold
                // — typical turbulent shear is 0.01-0.1, so anything below
                // 1e-3 indicates the station should be treated as Fortran's
                // failed-Newton fallback.
                // Threshold tightened from 1e-3 to 1e-5: Fortran MRCHUE can
                // converge to genuinely small CTAU (~2e-4 for fx63120) when
                // transition occurs naturally at IBLTE-1; the original 1e-3
                // threshold over-fired and overrode that converged value.
                // The 0.05 fallback only matches Fortran when CTAU was
                // essentially uninitialized (Newton never ran or returned
                // pathological result).
                if (blState.ITRAN[side] == blState.IBLTE[side]
                    && blState.ITRAN[side] >= 1
                    && blState.ITRAN[side] < blState.NBL[side]
                    && blState.CTAU[blState.ITRAN[side], side] < 1.0e-5)
                {
                    blState.CTAU[blState.ITRAN[side], side] = 0.05;
                }
                int startStation = Math.Min(
                    Math.Max(blState.ITRAN[side] + 1, blState.IBLTE[side] + 1),
                    blState.NBL[side]);
                MarchLegacyBoundaryLayerDirectSeed(
                    blState,
                    side,
                    startStation,
                    settings,
                    teGap,
                    wakeSeed,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                    reybl,
                    reybl_re,
                    reybl_ms);

                continue;
            }

            // Wake stations: the active wake lives on the lower branch in XFoil.
            // Seed the first wake station from the TE merge state and downstream
            // stations with XFoil's fallback wake extrapolation, rather than the
            // previous ad hoc growth heuristic.
            if (side == 1 && blState.NBL[side] > blState.IBLTE[side] + 1)
            {
                double upperThetaTe = blState.THET[blState.IBLTE[0], 0];
                double lowerThetaTe = blState.THET[blState.IBLTE[1], 1];
                double upperDstarTe = blState.DSTR[blState.IBLTE[0], 0];
                double lowerDstarTe = blState.DSTR[blState.IBLTE[1], 1];
                // Fortran line 354: DTE = DSTR_TE1 + DSTR_TE2 + ANTE
                // Uses raw ANTE, NOT cubic WGAP(1). See GetFirstWakeStationTeGap.
                double firstWakeGap = GetFirstWakeStationTeGap(wakeSeed, teGap);
                double theta = Math.Max(upperThetaTe + lowerThetaTe, 1e-10);
                double dstar = Math.Max(upperDstarTe + lowerDstarTe + firstWakeGap, 1.00005 * theta);

                double ctau = Math.Max(blState.CTAU[blState.IBLTE[side], side], 0.03);

                for (int ibl = blState.IBLTE[side] + 1; ibl < blState.NBL[side]; ibl++)
                {
                    if (ibl == blState.IBLTE[side] + 1)
                    {
                        blState.THET[ibl, side] = theta;
                        blState.DSTR[ibl, side] = dstar;
                        blState.CTAU[ibl, side] = ctau;
                        blState.MASS[ibl, side] = ComputeMassDefect(
                            blState.DSTR[ibl, side],
                            blState.UEDG[ibl, side],
                            settings.UseLegacyBoundaryLayerInitialization);
                        continue;
                    }

                    int ibm = ibl - 1;
                    double thetaPrev = Math.Max(blState.THET[ibm, side], 1e-10);
                    double dstarPrev = Math.Max(blState.DSTR[ibm, side], 1.00005 * thetaPrev);
                    double dxWake = Math.Max(blState.XSSI[ibl, side] - blState.XSSI[ibm, side], 1e-12);
                    double ratLen = dxWake / Math.Max(10.0 * dstarPrev, 1e-12);

                    theta = thetaPrev;
                    dstar = (dstarPrev + theta * ratLen) / (1.0 + ratLen);
                    double wakeGap = GetWakeGap(wakeSeed, teGap, ibl - blState.IBLTE[side]);
                    dstar = Math.Max(dstar - wakeGap, 1.00005 * theta) + wakeGap;
                    ctau = blState.CTAU[ibm, side];

                    blState.THET[ibl, side] = theta;
                    blState.DSTR[ibl, side] = dstar;
                    blState.CTAU[ibl, side] = ctau;
                    blState.MASS[ibl, side] = ComputeMassDefect(
                        blState.DSTR[ibl, side],
                        blState.UEDG[ibl, side],
                        settings.UseLegacyBoundaryLayerInitialization);

                    // Fortran MRCHUE runs a full Newton loop at each wake
                    // station (BLSYS(2) with direct Ue constraint, 25 iter
                    // max, tolerance 1e-5). The C# extrapolation above is
                    // only the Fortran convergence-failure fallback. Refine
                    // with Newton to match the Fortran MRCHUE wake state.
                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        
                        RefineWakeSeedStation(
                            blState, side, ibl, settings,
                            tkbl, qinfbl, tkbl_ms,
                            hstinv, hstinv_ms,
                            rstbl, rstbl_ms,
                            reybl, reybl_re, reybl_ms,
                            wakeGap, wakeSeed, teGap);
                    }
                }
            }

            // Ensure transition is before TE
            if (blState.ITRAN[side] >= blState.IBLTE[side])
            {
                blState.ITRAN[side] = blState.IBLTE[side] - 1;
                if (blState.ITRAN[side] < 2) blState.ITRAN[side] = 2;
            }

            // D5 — removed: constant-Cτ post-transition shortcut. The managed
            // path used to overwrite CTAU[ibl, side] = 0.03 for all post-
            // transition stations when flag=false. That shortcut stacked a
            // second competing turbulent seed pass on top of MRCHDU's output
            // and corrupted the shear-lag profile. MRCHDU now sets CTAU
            // physically in both paths.

        }
    }

    // Legacy mapping: legacy-derived from XFoil wake seed initialization before MRCHUE wake continuation
    // Difference from legacy: The managed port has to materialize the lower-side wake seed explicitly because the prior state is not carried implicitly through COMMON arrays between runs.
    // Decision: Keep the explicit preseed and preserve the same TE-merge and downstream extrapolation formulas used by the managed wake fallback so the parity replay has a valid starting state.
    private static void PrimeLegacyWakeSeedStations(
        BoundaryLayerSystemState blState,
        double teGap,
        WakeSeedData? wakeSeed)
    {
        const int side = 1;
        if (blState.NBL[side] <= blState.IBLTE[side] + 1)
        {
            return;
        }

        double upperThetaTe = blState.THET[blState.IBLTE[0], 0];
        double lowerThetaTe = blState.THET[blState.IBLTE[1], 1];
        double upperDstarTe = blState.DSTR[blState.IBLTE[0], 0];
        double lowerDstarTe = blState.DSTR[blState.IBLTE[1], 1];
        // Fortran line 353-356: TTE = T1+T2; DTE = D1+D2+ANTE; CTE = (C1*T1+C2*T2)/TTE.
        // All REAL (float) ops. Compute each step in float to avoid a double
        // accumulation that differs from Fortran's per-operation rounding.
        double firstWakeGap = GetFirstWakeStationTeGap(wakeSeed, teGap);
        double theta = Math.Max(
            LegacyPrecisionMath.Add(upperThetaTe, lowerThetaTe, true),
            1e-10);
        double dstar = Math.Max(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(upperDstarTe, lowerDstarTe, true),
                firstWakeGap, true),
            1.00005 * theta);

        double ctauUpper = blState.CTAU[blState.IBLTE[0], 0];
        double ctauLower = blState.CTAU[blState.IBLTE[1], 1];
        double ctau = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Multiply(ctauUpper, upperThetaTe, true),
                LegacyPrecisionMath.Multiply(ctauLower, lowerThetaTe, true),
                true),
            theta, true);
        ctau = Math.Max(ctau, 0.03);
        theta = LegacyPrecisionMath.RoundToSingle(theta, true);
        dstar = LegacyPrecisionMath.RoundToSingle(dstar, true);
        ctau = LegacyPrecisionMath.RoundToSingle(ctau, true);

        for (int ibl = blState.IBLTE[side] + 1; ibl < blState.NBL[side]; ibl++)
        {
            if (ibl == blState.IBLTE[side] + 1)
            {
                blState.THET[ibl, side] = theta;
                blState.DSTR[ibl, side] = dstar;
                blState.CTAU[ibl, side] = ctau;
                blState.MASS[ibl, side] = ComputeMassDefect(
                    blState.DSTR[ibl, side],
                    blState.UEDG[ibl, side],
                    useLegacyPrecision: true);
                continue;
            }

            int ibm = ibl - 1;
            double thetaPrev = Math.Max(blState.THET[ibm, side], 1e-10);
            double dstarPrev = Math.Max(blState.DSTR[ibm, side], 1.00005 * thetaPrev);
            double dxWake = Math.Max(blState.XSSI[ibl, side] - blState.XSSI[ibm, side], 1e-12);
            double ratLen = dxWake / Math.Max(10.0 * dstarPrev, 1e-12);

            theta = thetaPrev;
            dstar = (dstarPrev + theta * ratLen) / (1.0 + ratLen);
            double wakeGap = GetWakeGap(wakeSeed, teGap, ibl - blState.IBLTE[side]);
            dstar = Math.Max(dstar - wakeGap, 1.00005 * theta) + wakeGap;
            ctau = blState.CTAU[ibm, side];
            theta = LegacyPrecisionMath.RoundToSingle(theta, true);
            dstar = LegacyPrecisionMath.RoundToSingle(dstar, true);
            ctau = LegacyPrecisionMath.RoundToSingle(ctau, true);

            blState.THET[ibl, side] = theta;
            blState.DSTR[ibl, side] = dstar;
            blState.CTAU[ibl, side] = ctau;
            blState.MASS[ibl, side] = ComputeMassDefect(
                blState.DSTR[ibl, side],
                blState.UEDG[ibl, side],
                useLegacyPrecision: true);
        }
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE direct seed continuation
    // Difference from legacy: This helper isolates the parity-only direct remarch that classic XFoil performs implicitly through shared state, while the default managed flow uses a different seed strategy.
    // Decision: Keep the dedicated parity helper and preserve the legacy station-by-station direct/inverse seed logic.
    private static void MarchLegacyBoundaryLayerDirectSeed(
        BoundaryLayerSystemState blState,
        int side,
        int startStation,
        AnalysisSettings settings,
        double teGap,
        WakeSeedData? wakeSeed,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);

        const int maxIterations = 25;
        const double seedTolerance = 1.0e-5;
        const double turbulentHkLimit = 2.5;
        const double minCtau = 1.0e-7;

        if (startStation >= blState.NBL[side])
        {
            return;
        }

        var solver = DenseLinearSystemSolver.Shared;
        double ncrit = settings.GetEffectiveNCrit(side);
        int oldTransitionStation = blState.ITRAN[side];
        bool tran = false;
        bool turb = startStation > blState.ITRAN[side] || startStation == blState.IBLTE[side] + 1;
        double theta;
        double dstar;
        // Fortran MRCHUE carries CTI from previous station; only clamps at <=0 per xbl.f:1465.
        // Do NOT apply a 0.03 floor — when the upstream CTAU is tiny (e.g., last laminar AMI),
        // F's Newton fails and extrapolation fires, resetting CTI to CTAU(IBM). The floor
        // forced Newton to succeed with a different trajectory.
        double ctau = blState.CTAU[Math.Max(startStation - 1, 1), side];
        if (ctau <= 0.0) ctau = 0.03;
        double ampl = ReadLegacyAmplificationCarry(blState, Math.Max(startStation - 1, 0), side);

        if (startStation == blState.IBLTE[side] + 1 && side == 1)
        {
            // Fortran MRCHUE line 354: DTE = DSTR(IBLTE1) + DSTR(IBLTE2) + ANTE
            // Uses raw ANTE (TE normal gap), NOT WGAP(1). The cubic WGAP(1)
            // evaluates to ANTE + ~2 ULP due to (AA+BB) = 1.0000001.
            double firstWakeGap = GetFirstWakeStationTeGap(wakeSeed, teGap);
            ComputeLegacyWakeTeMergeState(
                blState,
                firstWakeGap,
                settings.UseLegacyBoundaryLayerInitialization,
                out theta,
                out dstar,
                out _);
        }
        else
        {
            theta = Math.Max(blState.THET[startStation - 1, side], 1.0e-10);
            dstar = Math.Max(blState.DSTR[startStation - 1, side], 1.0e-10);
        }

        for (int ibl = startStation; ibl < blState.NBL[side]; ibl++)
        {
            int ibm = ibl - 1;
            bool simi = false;
            bool wake = ibl > blState.IBLTE[side];
            bool useAcceptedSecondaryRefresh = false;
            double xsi = blState.XSSI[ibl, side];
            double uei = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1.0e-10);

                if (wake)
                {
                    if (side == 1 && ibl == blState.IBLTE[side] + 1)
                    {
                    // Fortran MRCHUE line 354: DTE = DSTR_TE1 + DSTR_TE2 + ANTE
                    // Uses raw ANTE, NOT WGAP(1). See GetFirstWakeStationTeGap.
                    double firstWakeGap = GetFirstWakeStationTeGap(wakeSeed, teGap);
                    ComputeLegacyWakeTeMergeState(
                        blState,
                        firstWakeGap,
                        settings.UseLegacyBoundaryLayerInitialization,
                        out theta,
                        out dstar,
                        out _);
                    // MRCHUE carries the current-station wake shear scratch from
                    // the last lower-surface TE state; the weighted TE merge is
                    // only the TESYS previous-station owner, not the wake CTI
                    // initializer.
                    // Fortran MRCHUE carries CTI from station IBLTE (stored as
                    // CTAU(IBLTE)) — no Math.Max(0.03) floor. At transition/TE
                    // this value can be as small as the last laminar AMI (2-3e-5)
                    // which causes Newton to fail and extrapolation to fire
                    // (xbl.f:2437 resets CTI = CTAU(IBM) = CTAU(IBLTE), same tiny).
                    // The 0.03 floor in C# forced Newton to succeed, producing
                    // converged-but-wrong turbulent CTAU. Only clamp at <=0
                    // per Fortran xbl.f:1465.
                    ctau = blState.CTAU[blState.IBLTE[side], side];
                    if (ctau <= 0.0)
                        ctau = 0.03;
                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        ApplyLegacySeedPrecision(
                            settings.UseLegacyBoundaryLayerInitialization,
                            ref uei,
                            ref theta,
                            ref dstar,
                            ref ctau);
                    }
                    }
                    else
                    {
                        // Fortran MRCHUE carries THI/DSI/CTI forward from the
                        // previous station's converged Newton result. It does
                        // NOT re-read from the stored arrays.
                        // Fortran: IF(CTI.LE.0.0) CTI = 0.03 — only clamps at zero
                        theta = Math.Max(theta, 1.0e-10);
                        dstar = Math.Max(dstar, 1.0e-10);
                        if (ctau <= 0.0)
                            ctau = 0.03;
                    }
                }

            double wakeGap = wake
                ? GetWakeGap(wakeSeed, teGap, ibl - blState.IBLTE[side])
                : 0.0;
            bool directMode = true;
            double inverseTargetHk = 0.0;
            double transitionXi = xsi;

            // Fortran MRCHUE does NOT apply DSLIM at station entry — it only
            // runs DSLIM inside the Newton iter loop AFTER delta application
            // (xbl.f:1975). The pre-Newton clamp here was adding a 1-ULP round-
            // trip drift (`dstar - wakeGap + wakeGap`) on wake stations where the
            // carried state from the previous station did not need clamping.
            // Removed for parity mode; ApplySeedDslim below runs inside the
            // Newton loop at line 4402 which is correct.
            // D5 — removed: modern-only pre-Newton DSLIM clamp. See
            // ViscousSolverEngine.Double.cs for rationale.
            double hklim = wake ? 1.00005 : 1.02;
            _ = hklim;
            // Track pre-update state for MRCHUE COM carry
            double mrchuePrevT = theta;
            double mrchuePrevD = dstar;
            float lastDmaxForExtrapolation = 0.0f;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                // s1210 MRCHUE per-iter trace at station 65 side 2 (ibl=64)
                
                // NACA 0018 -4 Nc=5: MRCHUE per-iter trace at stn 90 s=2 (ibl=89, 2nd wake)
                
                
                
                // NACA 0015 2M a=10: MRCHUE per-iter trace at stn 67 s=2 (ibl=66, 2nd wake)
                
                // NACA 0012 5K debug: MRCHUE per-iter trace at stn 68 s=2 (0-idx ibl=67)
                
                double prevWakeGap = wake
                    ? GetWakeGap(wakeSeed, teGap, Math.Max(ibm - blState.IBLTE[side], 0))
                    : 0.0;
                // Fortran MRCHUE carries COM2→COM1 with PRE-update T1/D1.
                // COM2.D2 = DSI_pre_update - DSWAKI (set by BLPRV before delta update).
                // Use PreUpdateT/PreUpdateD from the primary snapshot to match this behavior.
                double prevTheta;
                double prevDstar;
                var prevPrimary = settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyPrimary[ibm, side]
                    : null;
                prevTheta = Math.Max(blState.THET[ibm, side], 1.0e-10);
                prevDstar = Math.Max(blState.DSTR[ibm, side], 1.0e-10);
                double prevStoredShear = blState.CTAU[ibm, side];
                double prevAmpl = ReadLegacyAmplificationCarry(blState, ibm, side);
                double prevCtau = Math.Max(prevStoredShear, minCtau);

                double[] residual;
                double[,] vs2;
                double hk2 = 0.0;
                double hk2T2 = 0.0;
                double hk2D2 = 0.0;
                double hk2U2 = 0.0;
                double u2Uei = 1.0;

                if (wake && ibl == blState.IBLTE[side] + 1)
                {
                    // Fortran MRCHUE carries the Newton iteration's BLVAR(3) secondary
                    // in COM2→COM1, NOT a post-convergence recomputation. Preserve
                    // the secondary stored by StoreLegacyCarrySnapshots during iteration.
                    useAcceptedSecondaryRefresh = false;
                    // Fortran line 354: DTE = DSTR_TE1 + DSTR_TE2 + ANTE (raw TE gap).
                    // `wakeGap` here is WGAP(1) = cubic-evaluated, which differs from
                    // raw ANTE by ~2 ULP. For DTE parity, use raw ANTE.
                    double anteForMerge = GetFirstWakeStationTeGap(wakeSeed, teGap);
                    ComputeLegacyWakeTeMergeState(
                        blState,
                        anteForMerge,
                        settings.UseLegacyBoundaryLayerInitialization,
                        out double tte,
                        out double dte,
                        out double cte);
                    double wakeStrippedDstar = LegacyPrecisionMath.Subtract(
                        dstar,
                        wakeGap,
                        settings.UseLegacyBoundaryLayerInitialization);

                    var teResult = BoundaryLayerSystemAssembler.AssembleTESystem(
                        cte,
                        tte,
                        dte,
                        hk2: 0.0,
                        rt2: 0.0,
                        msq2: 0.0,
                        h2: 0.0,
                        s2: ctau,
                        t2: theta,
                        // TESYS expects the BLPRV-style wake-stripped D2 packet
                        // alongside DW2, not the stored total wake DSI.
                        d2: wakeStrippedDstar,
                        dw2: wakeGap,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                    residual = teResult.Residual;
                    vs2 = teResult.VS2;

                    var (currentU2, currentU2Uei, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                        uei,
                        tkbl,
                        qinfbl,
                        tkbl_ms,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                    u2Uei = currentU2Uei;
                    var currentKinematic = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                        currentU2,
                        theta,
                        wakeStrippedDstar,
                        wakeGap,
                        hstinv,
                        hstinv_ms,
                        gm1,
                        rstbl,
                        rstbl_ms,
                        GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                        reybl,
                        reybl_re,
                        reybl_ms,
                        settings.UseLegacyBoundaryLayerInitialization,
                        destination: GetEngineKinematicScratchA());
                    hk2 = currentKinematic.HK2;
                    hk2T2 = currentKinematic.HK2_T2;
                    hk2D2 = currentKinematic.HK2_D2;
                    hk2U2 = currentKinematic.HK2_U2;

                    

                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        // Fortran TESYS calls BLVAR(3) during the Newton
                        // iteration. The BLVAR(3) result stays in COM2 and
                        // is carried to COM1 for the next station. Passing
                        // null secondary here leaves station 83 without the
                        // carried wake BLVAR state, causing 31K+ ULP theta.
                        var wakeSec = BoundaryLayerSystemAssembler.ComputeStationVariables(
                            3, currentKinematic.HK2, currentKinematic.RT2, currentKinematic.M2,
                            currentKinematic.H2, ctau, wakeGap, theta, wakeStrippedDstar,
                            destination: BoundaryLayerSystemAssembler.GetPooledStationVariablesEngine());
                        var secSnapshot = GetEngineSecondaryScratchA();
                        ResetEngineSecondaryScratch(secSnapshot);
                        secSnapshot.Hs = wakeSec.Hs; secSnapshot.Us = wakeSec.Us; secSnapshot.Cf = wakeSec.Cf;
                        secSnapshot.Di = wakeSec.Di; secSnapshot.Cq = wakeSec.Cteq; secSnapshot.De = wakeSec.De;
                        secSnapshot.Hc = wakeSec.Hc;
                        StoreLegacyCarrySnapshots(
                            blState,
                            ibl,
                            side,
                            CreateLegacyPrimaryStationStateOverride(
                                blState,
                                ibl,
                                side,
                                tkbl,
                                qinfbl,
                                tkbl_ms,
                                uei,
                                theta,
                                wakeStrippedDstar,
                                true),
                            currentKinematic,
                            secSnapshot,
                            traceLabel: "initialize_bl_direct");
                    }
                }
                else
                {
                    useAcceptedSecondaryRefresh = false;
                    // Trace BLDIF inputs at station 83 wake
                    
                    
                    var localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                        isWake: wake,
                        isTurbOrTran: turb || tran,
                        isTran: tran,
                        isSimi: simi,
                        x1: blState.XSSI[ibm, side],
                        x2: xsi,
                        uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1.0e-10),
                        uei2: uei,
                        t1: prevTheta,
                        t2: theta,
                        d1: prevDstar,
                        d2: dstar,
                        s1: prevCtau,
                        s2: ctau,
                        dw1: prevWakeGap,
                        dw2: wakeGap,
                        ampl1: prevAmpl,
                        ampl2: ampl,
                        amcrit: ncrit,
                        tkbl,
                        qinfbl,
                        tkbl_ms,
                        hstinv,
                        hstinv_ms,
                        gm1,
                        rstbl,
                        rstbl_ms,
                        GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                        reybl,
                        reybl_re,
                        reybl_ms,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                        station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                            ? blState.LegacyKinematic[ibm, side]
                            : null,
                        station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                            ? blState.LegacySecondary[ibm, side]
                            : null,
                        traceSide: side + 1,
                        traceStation: ibl + 1,
                        traceIteration: iter + 1,
                        tracePhase: "initialize_bl");
                    // Trace COM1 at station 83 wake
                    
                    residual = localResult.Residual;
                    vs2 = localResult.VS2;
                    hk2 = localResult.HK2;
                    hk2T2 = localResult.HK2_T2;
                    hk2D2 = localResult.HK2_D2;
                    hk2U2 = localResult.HK2_U2;
                    u2Uei = localResult.U2_UEI;
                    // Trace BLDIF(3) at wake station 83
                    

                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        // Classic XFoil carries the last pre-accept BLKIN/BLVAR state
                        // of the current station forward into the next interval.
                        StoreLegacyCarrySnapshots(
                            blState,
                            ibl,
                            side,
                            localResult.Primary2Snapshot,
                            localResult.Kinematic2Snapshot,
                            localResult.Secondary2Snapshot,
                            traceLabel: "initialize_bl_system");
                    }

                }

                var matrix = SolverBuffers.Matrix4x4Double;
                var rhs = SolverBuffers.Vector4Double;
                Array.Clear(matrix);
                Array.Clear(rhs);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        matrix[row, col] = vs2[row, col];
                    }

                    rhs[row] = residual[row];
                }

                if (directMode)
                {
                    matrix[3, 3] = 1.0;
                }
                else
                {
                    // Fortran MRCHUE inverse: VS2(4,2)=HK2_T2, VS2(4,3)=HK2_D2, VS2(4,4)=HK2_U2
                    matrix[3, 1] = hk2T2;
                    matrix[3, 2] = hk2D2;
                    matrix[3, 3] = hk2U2;
                    rhs[3] = inverseTargetHk - hk2;
                    
                }

                


                // Trace wake station 82 BLSYS residuals and matrix
                

                // NACA 0018 -4 Nc=5: dump matrix+rhs+delta at stn 90 s=2 iter 3
                
                // NACA 0012 5K: dump matrix+rhs at stn 68 s=2 iter 1 (pre-Gauss)
                
                double[] delta;
                try
                {
                    delta = SolveSeedLinearSystem(solver, matrix, rhs);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                // Station 98: dump upstream kinematic snapshot
                
                // Station 98 system dump for MRCHUE wake parity
                

                double shearScale = LegacyPrecisionMath.Max(
                    ctau,
                    minCtau,
                    settings.UseLegacyBoundaryLayerInitialization);
                SeedStepMetrics stepMetrics = ComputeSeedStepMetrics(
                    delta,
                    theta,
                    dstar,
                    shearScale,
                    uei,
                    side + 1,
                    ibl + 1,
                    iter + 1,
                    directMode ? "direct" : "inverse",
                    includeUe: !directMode,
                    settings.UseLegacyBoundaryLayerInitialization);

                double dmax = stepMetrics.Dmax;
                lastDmaxForExtrapolation = (float)dmax;
                double rlx = ComputeLegacySeedRelaxation(dmax, settings.UseLegacyBoundaryLayerInitialization);
                double residualNorm = stepMetrics.ResidualNorm;

                
                


                if (directMode && ibl != blState.IBLTE[side] + 1)
                {
                    double msqTest = 0.0;
                    if (hstinv > 0.0)
                    {
                        double uesq = uei * uei * hstinv;
                        msqTest = uesq / (gm1 * (1.0 - 0.5 * uesq));
                    }

                    double predictedDstar = LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization);
                    double predictedTheta = LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization);
                    double htest = predictedDstar / Math.Max(predictedTheta, 1.0e-30);
                    double hkTest = BoundaryLayerCorrelations.KinematicShapeParameter(
                        htest,
                        msqTest,
                        settings.UseLegacyBoundaryLayerInitialization).Hk;
                    if (hkTest >= turbulentHkLimit)
                    {
                        // Fortran uses HK1 from COM1 (stored kinematic) for HTARG,
                        // not a recomputation from post-update DSTR/THET.
                        double? hkPrevForHtarg = settings.UseLegacyBoundaryLayerInitialization
                            ? blState.LegacyKinematic[ibm, side]?.HK2
                            : null;
                        inverseTargetHk = ComputeLegacyDirectSeedInverseTargetHk(
                            blState,
                            side,
                            ibl,
                            wake,
                            prevTheta,
                            prevDstar,
                            prevWakeGap,
                            hstinv,
                            settings.UseLegacyBoundaryLayerInitialization,
                            hkPrevOverride: hkPrevForHtarg);
                        inverseTargetHk = settings.UseLegacyBoundaryLayerInitialization
                            ? (wake ? MathF.Max((float)inverseTargetHk, 1.01f)
                                    : MathF.Max((float)inverseTargetHk, (float)turbulentHkLimit))
                            : (wake ? Math.Max(inverseTargetHk, 1.01)
                                    : Math.Max(inverseTargetHk, turbulentHkLimit));
                        directMode = false;
                        continue;
                    }
                }

                ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), minCtau), 0.30);

                mrchuePrevT = theta;
                mrchuePrevD = dstar;
                // Trace pre-update values at station 83 (ibl=82) side 2
                
                theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                if (!directMode)
                {
                    uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                }

                double msq = 0.0;
                if (hstinv > 0.0)
                {
                    double uesq = uei * uei * hstinv;
                    msq = uesq / (gm1 * (1.0 - 0.5 * uesq));
                }

                double dsw = dstar - wakeGap;
                dsw = ApplySeedDslim(dsw, theta, msq, hklim, settings.UseLegacyBoundaryLayerInitialization);
                dstar = dsw + wakeGap;
                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    // Classic MRCHUE/MRCHDU keep the accepted lower-surface and
                    // wake continuation state in REAL storage before the next
                    // local solve. Leaving it wide preserves the same drift one
                    // station later even after the similarity-station fix.
                    ApplyLegacySeedPrecision(
                        settings.UseLegacyBoundaryLayerInitialization,
                        ref uei,
                        ref theta,
                        ref dstar,
                        ref ctau);
                }

                
                
                // Wake station Newton trace for parity debugging
                
                // NACA 1410 debug: wake stations 93-96 on side 2
                
                if ((float)dmax <= (float)seedTolerance)
                {
                    break;
                }
            }

            // Fortran MRCHUE non-convergence handler (xbl.f:2141-2161): if DMAX > 0.1,
            // extrapolate theta/dstar from upstream.
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                float dmaxEndF = lastDmaxForExtrapolation;
                if (dmaxEndF > 0.1f && ibl > 2)
                {
                    if (ibl <= blState.IBLTE[side])
                    {
                        // Fortran xbl.f:2298-2299: THI = THET(IBM)*(XSSI(IBL)/XSSI(IBM))**0.5
                        // Gfortran compiles **0.5 via powf, which differs from sqrtf by 1 ULP.
                        float xRatio = (float)blState.XSSI[ibl, side] / (float)blState.XSSI[ibm, side];
                        float sqrtRatio = MathF.Pow(xRatio, 0.5f);
                        theta = (float)blState.THET[ibm, side] * sqrtRatio;
                        dstar = (float)blState.DSTR[ibm, side] * sqrtRatio;
                        uei = blState.UEDG[ibm, side];
                    }
                    else if (ibl == blState.IBLTE[side] + 1)
                    {
                        // Fortran xbl.f:2427-2430 first-wake extrapolation:
                        //   CTI = CTE
                        //   THI = TTE
                        //   DSI = DTE
                        // Recompute TE merge values (TTE, DTE) and reset theta/dstar.
                        // These match what Newton started with but Newton may have
                        // perturbed them; Fortran resets to TE merge on failure.
                        if (side == 1)
                        {
                            double firstWakeGapExtrap = GetFirstWakeStationTeGap(wakeSeed, teGap);
                            ComputeLegacyWakeTeMergeState(
                                blState,
                                firstWakeGapExtrap,
                                settings.UseLegacyBoundaryLayerInitialization,
                                out theta,
                                out dstar,
                                out _);
                        }
                        else
                        {
                            theta = blState.THET[ibm, side];
                            dstar = blState.DSTR[ibm, side];
                        }
                    }
                    else
                    {
                        // Wake (xbl.f:2151-2153):
                        //   THI = THET(IBM,IS)
                        //   RATLEN = (XSSI(IBL,IS)-XSSI(IBM,IS)) / (10.0*DSTR(IBM,IS))
                        //   DSI = (DSTR(IBM,IS) + THI*RATLEN) / (1.0 + RATLEN)
                        float thetaPrevF = (float)blState.THET[ibm, side];
                        float dstarPrevF = (float)blState.DSTR[ibm, side];
                        float xsiF = (float)blState.XSSI[ibl, side];
                        float xsiPrevF = (float)blState.XSSI[ibm, side];
                        float ratlen = (xsiF - xsiPrevF) / (10.0f * dstarPrevF);
                        theta = thetaPrevF;
                        dstar = (dstarPrevF + thetaPrevF * ratlen) / (1.0f + ratlen);
                    }
                    // Fortran xbl.f lines 2352-2354: extrapolation also overrides UEI
                    // to the neighbor-average for non-boundary stations:
                    //   UEI = UEDG(IBL,IS)
                    //   IF(IBL.GT.2 .AND. IBL.LT.NBL(IS))
                    //  &  UEI = 0.5*(UEDG(IBL-1,IS) + UEDG(IBL+1,IS))
                    // Without this, C# keeps the failed-Newton UEI which differs
                    // from F's averaged UEI; this propagates into BLKIN producing
                    // wrong RT and breaks downstream wake station match.
                    {
                        float ueiF2 = (float)blState.UEDG[ibl, side];
                        if (ibl > 1 && ibl < blState.NBL[side] - 1)
                        {
                            float ueim = (float)blState.UEDG[ibm, side];
                            float ueip = (float)blState.UEDG[ibl + 1, side];
                            ueiF2 = 0.5f * (ueim + ueip);
                        }
                        uei = ueiF2;
                    }
                    // Fortran MRCHUE xbl.f:2200-2201: reset CTI at/after transition station
                    //   IF(IBL.EQ.ITRAN(IS)) CTI = 0.05
                    //   IF(IBL.GT.ITRAN(IS)) CTI = CTAU(IBM,IS)
                    // This is critical for bit-parity at transition station when Newton
                    // fails to converge (which happens routinely at transition points).
                    if (ibl == blState.ITRAN[side])
                    {
                        ctau = 0.05;
                    }
                    else if (ibl > blState.ITRAN[side])
                    {
                        ctau = blState.CTAU[ibm, side];
                    }
                }
            }

            // Fortran MRCHUE label 109 (xbl.f:2307-2325): after Newton failure
            // (whether DMAX>0.1 with extrapolation or DMAX<=0.1 without), call
            // BLPRV+BLKIN+BLVAR with the FINAL T/D/U state. This refreshes
            // the secondary (Cf, Hs, Di, Cq, Us, De) so it matches the stored
            // primary. C# previously stored secondary from the LAST Newton
            // iter's PRE-delta state inside the loop, but T/D/U stored were
            // POST-delta — causing a mismatch that propagates as wrong COM1
            // at next station's Newton.
            // D5 — un-gated: MRCHUE failed-Newton extrapolation salvage runs
            // in both paths. The Fortran-ported salvage is load-bearing for
            // Newton stability near separation; without it, one bad station
            // corrupts the entire downstream march. No parity risk: legacy
            // path already ran this and continues to.
            bool convergedFinalNewton = (float)lastDmaxForExtrapolation <= (float)seedTolerance;
            if (!convergedFinalNewton)
            {
                
                
                try {
                double finalDsw = dstar - wakeGap;
                
                var (finalU2, finalU2Uei, finalU2Ms) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei, tkbl, qinfbl, tkbl_ms);
                
                var finalKin = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                    finalU2, theta, finalDsw, wakeGap,
                    hstinv, hstinv_ms, gm1, rstbl, rstbl_ms,
                    GetHvRat(true), reybl, reybl_re, reybl_ms,
                    useLegacyPrecision: true,
                    destination: GetEngineKinematicScratchA());
                
                int blvarMode = wake ? 3 : (turb || tran ? 2 : 1);
                // Fortran xbl.f label 109 calls BLVAR sequentially for wake stations:
                // first BLVAR(2) (clamps HK to 1.05), then BLVAR(3). The 1.05 clamp
                // from BLVAR(2) persists into BLVAR(3)'s HK input, so HS is
                // computed at HK=1.05, not at the original HK (which BLVAR(3) would
                // have clamped only to 1.00005). To match F bit-exact, pre-clamp
                // HK to 1.05 when wake AND turb/tran (i.e. F also called BLVAR(2)).
                double effectiveHk = finalKin.HK2;
                if (wake && (turb || tran))
                {
                    effectiveHk = Math.Max(effectiveHk, 1.05);
                }
                var finalSec = BoundaryLayerSystemAssembler.ComputeStationVariables(
                    blvarMode, effectiveHk, finalKin.RT2, finalKin.M2,
                    finalKin.H2, ctau, wakeGap, theta, finalDsw,
                    destination: BoundaryLayerSystemAssembler.GetPooledStationVariablesEngine());
                
                var finalSecSnapshot = GetEngineSecondaryScratchA();
                ResetEngineSecondaryScratch(finalSecSnapshot);
                finalSecSnapshot.Hs = finalSec.Hs; finalSecSnapshot.Us = finalSec.Us; finalSecSnapshot.Cf = finalSec.Cf;
                finalSecSnapshot.Di = finalSec.Di; finalSecSnapshot.Cq = finalSec.Cteq; finalSecSnapshot.De = finalSec.De;
                finalSecSnapshot.Hc = finalSec.Hc;
                StoreLegacyCarrySnapshots(
                    blState, ibl, side,
                    CreateLegacyPrimaryStationStateOverride(
                        blState, ibl, side, tkbl, qinfbl, tkbl_ms,
                        uei, theta, finalDsw, true),
                    finalKin, finalSecSnapshot,
                    traceLabel: "march_legacy_label109");
                
                } catch (Exception) {
                }
            }

            blState.CTAU[ibl, side] = ctau;
            blState.THET[ibl, side] = theta;
            blState.DSTR[ibl, side] = dstar;
            blState.UEDG[ibl, side] = uei;
            blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(
                dstar,
                uei,
                settings.UseLegacyBoundaryLayerInitialization);
            
            // Store pre-update values for MRCHUE COM carry.
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                var primary = blState.LegacyPrimary[ibl, side];
                if (primary is not null)
                {
                    primary.PreUpdateT = LegacyPrecisionMath.RoundToSingle(mrchuePrevT, true);
                    primary.PreUpdateD = LegacyPrecisionMath.Subtract(
                        mrchuePrevD, wakeGap, true);
                    primary.PreUpdateDFull = LegacyPrecisionMath.RoundToSingle(mrchuePrevD, true);
                }
                
            }

            
            // Fortran MRCHUE: AMI is updated at each station (AMI = AMPL2 after TRCHEK).
            // The carry must store the station-specific amplification (= ctau for
            // pre-transition stations), not the stale initial `ampl`.
            WriteLegacyAmplificationCarry(blState, ibl, side, ctau);


                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    double acceptedPrevWakeGap = wake
                        ? GetWakeGap(wakeSeed, teGap, Math.Max(ibm - blState.IBLTE[side], 0))
                        : 0.0;
                    var preservedSecondary = useAcceptedSecondaryRefresh
                        ? null
                        : PreserveLegacySecondary(blState.LegacySecondary[ibl, side]);
                    RefreshLegacyAcceptedStationSnapshot(
                        blState,
                        side,
                        ibl,
                        uei,
                        theta,
                        dstar,
                        ctau,
                        ampl,
                        wake,
                        turb,
                        tran,
                        simi,
                        settings,
                        tkbl,
                        qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                        reybl,
                        reybl_re,
                        reybl_ms,
                        wake ? wakeGap : 0.0,
                        acceptedPrevWakeGap,
                        preservedSecondary);
                }


            if (ibl == blState.IBLTE[side])
            {
                turb = true;
            }

            tran = false;
        }
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE laminar/transition seed refinement
    // Difference from legacy: The same local seed refinement is preserved, but the managed port factors the transition-point solve and local station assembly into reusable helpers and explicit matrices.
    // Decision: Keep the decomposed refinement helper and preserve the legacy direct/inverse restart behavior.
    private static void RefineLegacySeedStation(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        double x1,
        double x2,
        double uei1,
        double uei2,
        double theta1,
        double theta2,
        double dstar1,
        double dstar2,
        double ampl1,
        double ampl2,
        double initialCtau2,
        double thetaSeed,
        double dstarSeed,
        double ncrit,
        ref double carriedCtauOut)
    {
        const int maxIterations = 25;
        const double seedTolerance = 1.0e-5;
        const double legacyLaminarHkLimit = 3.8;
        const double legacyTransitionHkLimit = 2.5;
        const double minCtau = 1.0e-7;
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        int ibm = ibl - 1;
        ReadLegacySeedStoredShear(
            blState.CTAU[ibm, side],
            ibm,
            blState.ITRAN[side],
            out double prevCtau,
            out double prevAmplStored);
        ampl1 = ReadLegacyAmplificationCarry(blState, ibm, side, prevAmplStored);
        ampl2 = ReadLegacyAmplificationCarry(blState, ibl, side, ampl2);
        

        var solver = DenseLinearSystemSolver.Shared;
        float lastDmaxFloat = 0.0f;
        // Legacy MRCHUE seeds CTI once when the march first crosses
        // transition, then carries the accepted value station-to-station.
        // Resetting every station to 0.03 keeps downstream intervals on the
        // wrong direct branch even after the previous station already
        // converged to a smaller turbulent shear.
        double ctau2 = LegacyPrecisionMath.RoundToSingle(
            Math.Max(initialCtau2, 0.0),
            settings.UseLegacyBoundaryLayerInitialization);
        bool directMode = true;
        bool transitionInterval = false;
        double inverseTargetHk = 0.0;
        double transitionXi = x2;
        double? forcedTransitionXi = GetLegacyParityForcedTransitionXi(blState, settings, side);
        ApplyLegacySeedPrecision(
            settings.UseLegacyBoundaryLayerInitialization,
            ref uei2,
            ref theta2,
            ref dstar2,
            ref ampl2);
        // Legacy block: xbl.f MRCHUE transition-seed interval inputs.
        // Difference from legacy: The downstream station was already rounded to REAL here, but the upstream station feeding AXSET/TRCHEK2 was left in double precision.
        // Decision: Round the station-1 seed state as well in parity mode so both sides of the local transition interval replay the classic REAL staging.
        
        ApplyLegacySeedPrecision(
            settings.UseLegacyBoundaryLayerInitialization,
            ref uei1,
            ref theta1,
            ref dstar1,
            ref ampl1);
        
        thetaSeed = LegacyPrecisionMath.RoundToSingle(thetaSeed, settings.UseLegacyBoundaryLayerInitialization);
        dstarSeed = LegacyPrecisionMath.RoundToSingle(dstarSeed, settings.UseLegacyBoundaryLayerInitialization);
        bool usesShearState = ibl >= blState.ITRAN[side];
        // Fortran TURB is the station-entry carry-forward flag. It only updates
        // AFTER the current station stores (xbl.f line 2385). So the post-loop
        // TRCHEK gate (`IF((.NOT.SIMI).AND.(.NOT.TURB))` at line 2311) uses the
        // entry-time TURB, not the in-loop transitionInterval. Capture entry
        // value so Newton-loop TRAN oscillation doesn't block the fresh TRCHEK.
        bool initialTurbAtEntry = usesShearState;
        double preUpdateTheta2 = theta2;
        double preUpdateDstar2 = dstar2;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            
            // Fortran MRCHUE calls BLPRV+BLKIN at the top of each Newton iteration,
            // giving fresh HK2/RT2 from the CURRENT iterate state. The transition
            // check (TRCHEK) then uses this fresh kinematic. Compute fresh kinematic
            // here to match, instead of using the stale blState.LegacyKinematic.
            KinematicResult? station2KinematicOverride;
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                var (iterU2, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei2, tkbl, qinfbl, tkbl_ms);
                station2KinematicOverride = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                    iterU2, theta2, dstar2, 0.0,
                    hstinv, hstinv_ms, gm1, rstbl, rstbl_ms,
                    GetHvRat(true), reybl, reybl_re, reybl_ms,
                    useLegacyPrecision: true,
                    destination: GetEngineKinematicScratchC());
            }
            else
            {
                station2KinematicOverride = null;
            }
            PrimaryStationState? station2PrimaryOverride =
                ResolveLegacyPrimaryStationStateOverride(
                    blState,
                    ibl,
                    side,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    uei2,
                    theta2,
                    dstar2,
                    settings.UseLegacyBoundaryLayerInitialization);
            SecondaryStationResult? station2SecondaryOverride = null;
            int currentTransitionStation = blState.ITRAN[side];
            bool carriedTurbulentInterval = ibl >= currentTransitionStation;
            double intervalAmpl2 = ampl2;
            TransitionModel.TransitionPointResult? seedTransitionPoint = null;

            if (!carriedTurbulentInterval)
            {
                var (u1, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei1,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                var (u2, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                    uei2,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                
                // Fortran TRCHEK uses COM1.T1/D1 from the LAST Newton iterate at
                // the upstream station, NOT the extrapolated values stored in THET/DSTR.
                // After non-convergence extrapolation, blState.THET differs from COM1.T1.
                // Use the kinematic override's InputT2/InputD2 (which stores the last
                // Newton iterate values) to match Fortran COM1.
                var upstreamKin = settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyKinematic[ibl - 1, side]
                    : null;
                double trTheta1 = theta1;
                double trDstar1 = dstar1;
                
                var transitionPoint = TransitionModel.ComputeTransitionPoint(
                    x1,
                    x2,
                    u1,
                    u2,
                    trTheta1,
                    theta2,
                    trDstar1,
                    dstar2,
                    ampl1,
                    ampl2,
                    ncrit,
                    hstinv,
                    hstinv_ms,
                    gm1,
                    rstbl,
                    rstbl_ms,
                    GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                    reybl,
                    reybl_re,
                    reybl_ms,
                    settings.UseModernTransitionCorrections,
                    forcedTransitionXi,
                    settings.UseLegacyBoundaryLayerInitialization,
                    traceSide: side + 1,
                    traceStation: ibl + 1,
                    traceIteration: iter + 1,
                    tracePhase: "seed_probe",
                    station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacyKinematic[ibl - 1, side]
                        : null,
                    station2KinematicOverride: station2KinematicOverride,
                    station2PrimaryOverride: station2PrimaryOverride,
                    destinationResult: GetPooledTransitionPointSeed());
                seedTransitionPoint = transitionPoint;
                transitionInterval = transitionPoint.TransitionOccurred;
                transitionXi = transitionPoint.TransitionXi;
                ampl2 = Math.Max(transitionPoint.DownstreamAmplification, 0.0);
                if (settings.UseLegacyBoundaryLayerInitialization &&
                    transitionPoint.DownstreamKinematic is not null)
                {
                    station2KinematicOverride = transitionPoint.DownstreamKinematic;
                }
                if (transitionInterval && ctau2 <= 0.0)
                {
                    // Legacy MRCHUE only seeds CTI when the interval first flips
                    // transitional and the carried shear state is still unset.
                    // Reapplying the 0.03 seed every iteration wipes out accepted
                    // inverse updates and keeps the parity path on the wrong branch.
                    ctau2 = Math.Max(ctau2, LegacyLaminarShearSeed);
                }

                currentTransitionStation = transitionInterval
                    ? ibl
                    : Math.Min(ibl + 2, blState.NBL[side]);
            }
            else
            {
                transitionInterval = false;
                transitionXi = x2;
            }

            usesShearState = ibl >= currentTransitionStation;


            BlsysResult localResult;
            if (transitionInterval)
            {
                localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                    isWake: false,
                    isTurbOrTran: true,
                    isTran: true,
                    isSimi: false,
                    x1,
                    x2,
                    uei1,
                    uei2,
                    theta1,
                    theta2,
                    d1: dstar1,
                    d2: dstar2,
                    // Legacy MRCHUE/TRDIF still carries the laminar CTI seed in the
                    // upstream S slot when the interval first crosses transition.
                    // Zeroing it moves the first mismatch into TRDIF station-1 inputs.
                    s1: LegacyLaminarShearSeed,
                    s2: ctau2,
                    dw1: 0.0,
                    dw2: 0.0,
                    ampl1: ampl1,
                    // Legacy MRCHUE/TRDIF replays the local transition interval from
                    // the pre-TRCHEK2 downstream amplification packet. Feeding the
                    // updated DownstreamAmplification back into the interval
                    // assembly changes the accepted transition-point iterate before
                    // BLDIF/TRDIF consume it and shifts the station-15 live march
                    // by a few ULPs.
                    ampl2: intervalAmpl2,
                    amcrit: ncrit,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    gm1,
                    rstbl,
                    rstbl_ms,
                    GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                    reybl,
                    reybl_re,
                    reybl_ms,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                    station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacyKinematic[ibl - 1, side]
                        : null,
                    station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacySecondary[ibl - 1, side]
                        : null,
                    traceSide: side + 1,
                    traceStation: ibl + 1,
                    traceIteration: iter + 1,
                    tracePhase: "transition_interval_system",
                    station2KinematicOverride: station2KinematicOverride,
                    station2PrimaryOverride: station2PrimaryOverride,
                    station2SecondaryOverride: station2SecondaryOverride,
                    // Fortran TRDIF reads XT_XF from COMMON set by TRCHEK2's forced
                    // branch. When the transition station coincides with a forced
                    // XIFORC (e.g. forced TE transition), WF2_XF = 1/(X2-X1) feeds
                    // BTX/VSX in row 3 (energy). Pass the seed_probe transition
                    // point directly so the assembly reuses the already-computed
                    // forced-transition sensitivities.
                    transitionPointOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? seedTransitionPoint : null,
                    forcedXtr: settings.UseLegacyBoundaryLayerInitialization
                        ? forcedTransitionXi : null);
            }
            else if (usesShearState)
            {
                localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                    isWake: false,
                    isTurbOrTran: true,
                    isTran: false,
                    isSimi: false,
                    x1,
                    x2,
                    uei1,
                    uei2,
                    theta1,
                    theta2,
                    d1: dstar1,
                    d2: dstar2,
                    s1: prevCtau,
                    s2: ctau2,
                    dw1: 0.0,
                    dw2: 0.0,
                    ampl1: ampl1,
                    ampl2: ampl2,
                    amcrit: ncrit,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    gm1,
                    rstbl,
                    rstbl_ms,
                    GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                    reybl,
                    reybl_re,
                    reybl_ms,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                    station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacyKinematic[ibl - 1, side]
                        : null,
                    station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacySecondary[ibl - 1, side]
                        : null,
                    traceSide: side + 1,
                    traceStation: ibl + 1,
                    traceIteration: iter + 1,
                    tracePhase: "turbulent_seed",
                    station2KinematicOverride: station2KinematicOverride,
                    station2PrimaryOverride: station2PrimaryOverride,
                    station2SecondaryOverride: station2SecondaryOverride);
            }
            else
            {
                localResult = AssembleLaminarStation(
                    x1,
                    x2,
                    uei1,
                    uei2,
                    theta1,
                    theta2,
                    dstar1,
                    dstar2,
                    ampl1,
                    ampl2,
                    settings,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                    reybl,
                    reybl_re,
                    reybl_ms,
                    station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacyKinematic[ibl - 1, side]
                        : null,
                    station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacySecondary[ibl - 1, side]
                        : null,
                    station2KinematicOverride: station2KinematicOverride,
                    station2PrimaryOverride: station2PrimaryOverride,
                    station2SecondaryOverride: station2SecondaryOverride,
                    traceSide: side + 1,
                    traceStation: ibl + 1,
                    traceIteration: iter + 1,
                    tracePhase: "laminar_seed");
            }

            // Trace MRCHUE station 4 side 2 residual for parity debugging
            

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // Trace BLVAR secondary at station 58 iteration 3 for parity
                
                // Every parity seed helper must carry the accepted station-2
                // BLKIN/BLVAR snapshot forward. Missing this storage lets the
                // next interval rebuild station 1 instead of replaying COM1.
                StoreLegacyCarrySnapshots(
                    blState,
                    ibl,
                    side,
                    localResult.Primary2Snapshot,
                    localResult.Kinematic2Snapshot,
                    localResult.Secondary2Snapshot,
                    traceLabel: "seed_interval_accept");
            }

            var matrix = SolverBuffers.Matrix4x4Double;
            var rhs = SolverBuffers.Vector4Double;
            Array.Clear(matrix);
            Array.Clear(rhs);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    matrix[row, col] = localResult.VS2[row, col];
                }

                rhs[row] = localResult.Residual[row];
            }

            if (!directMode)
            {
                // Fortran MRCHUE inverse: VS2(4,2)=HK2_T2, VS2(4,3)=HK2_D2, VS2(4,4)=HK2_U2
                matrix[3, 1] = localResult.HK2_T2;
                matrix[3, 2] = localResult.HK2_D2;
                matrix[3, 3] = localResult.HK2_U2;
                rhs[3] = LegacyPrecisionMath.Subtract(
                    inverseTargetHk,
                    localResult.HK2,
                    settings.UseLegacyBoundaryLayerInitialization);
                
            }
            else
            {
                matrix[3, 3] = 1.0;
            }

            if (transitionInterval)
            {
            }

            if (!transitionInterval)
            {
            }

            
            
            
            // Dump full 4x4 system at side 2 station 4 inverse iterations
            
            // Station 3 trace moved to after update
            // Targeted MUE5 trace for station 5 side 2
            
            
            // NACA 1408 a=2 Nc=12 debug: station 71 end of iteration (BLVAR output)
            
            // NACA 1408 a=2 Nc=12 debug: station 72 s=2 first post-transition station
            
            double[] delta;
            try
            {
                delta = SolveSeedLinearSystem(solver, matrix, rhs);
            }
            catch (InvalidOperationException)
            {
                
                break;
            }
            
            // Station 98 system dump for MRCHUE parity
            
            // Station 5 delta trace
            

            // Dump delta at side 2 station 4 inverse iterations
            
            
            double shearScale = usesShearState
                ? LegacyPrecisionMath.Max(ctau2, minCtau, settings.UseLegacyBoundaryLayerInitialization)
                : 10.0;
            SeedStepMetrics stepMetrics = ComputeSeedStepMetrics(
                delta,
                theta2,
                dstar2,
                shearScale,
                uei2,
                side + 1,
                ibl + 1,
                iter + 1,
                directMode ? "direct" : "inverse",
                includeUe: !directMode,
                settings.UseLegacyBoundaryLayerInitialization,
                includeShearInDmax: usesShearState || directMode);

            double dmax = stepMetrics.Dmax;
            lastDmaxFloat = (float)dmax;
            double rlx = ComputeLegacySeedRelaxation(dmax, settings.UseLegacyBoundaryLayerInitialization);
            double stepNorm = stepMetrics.ResidualNorm;


            if (directMode)
            {
                double msqTest = 0.0;
                if (hstinv > 0.0)
                {
                    double uesq = uei2 * uei2 * hstinv;
                    msqTest = uesq / (gm1 * (1.0 - 0.5 * uesq));
                }

                double predictedDstar2 = LegacyPrecisionMath.AddScaled(dstar2, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization);
                double predictedTheta2 = LegacyPrecisionMath.AddScaled(theta2, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization);
                double htest = predictedDstar2 / Math.Max(predictedTheta2, 1e-30);
                double hkTest = BoundaryLayerCorrelations.KinematicShapeParameter(
                    htest,
                    msqTest,
                    settings.UseLegacyBoundaryLayerInitialization).Hk;
                bool useLegacySeedPrecision = settings.UseLegacyBoundaryLayerInitialization;
                double hmax = useLegacySeedPrecision
                    ? (usesShearState ? (float)legacyTransitionHkLimit : (float)legacyLaminarHkLimit)
                    : (usesShearState ? legacyTransitionHkLimit : legacyLaminarHkLimit);
                // Trace mode switch at side 2 station 4
                
                
                if (hkTest >= hmax)
                {
                    double thetaFloor = LegacyPrecisionMath.Max(theta1, 1.0e-30, useLegacySeedPrecision);
                    double inverseTargetHkDelta;
                    if (transitionInterval)
                    {
                        inverseTargetHkDelta = LegacyPrecisionMath.Divide(
                            LegacyPrecisionMath.MultiplySubtract(
                                0.15,
                                LegacyPrecisionMath.Subtract(x2, transitionXi, useLegacySeedPrecision),
                                LegacyPrecisionMath.Multiply(
                                    0.03,
                                    LegacyPrecisionMath.Subtract(transitionXi, x1, useLegacySeedPrecision),
                                    useLegacySeedPrecision),
                                useLegacySeedPrecision),
                            thetaFloor,
                            useLegacySeedPrecision);
                    }
                    else if (usesShearState)
                    {
                        // Legacy MRCHUE falls back to the turbulent inverse Hk
                        // target after transition, even once the current
                        // interval is no longer tagged as transitional.
                        inverseTargetHkDelta = LegacyPrecisionMath.Divide(
                            LegacyPrecisionMath.Multiply(
                                -0.15,
                                LegacyPrecisionMath.Subtract(x2, x1, useLegacySeedPrecision),
                                useLegacySeedPrecision),
                            thetaFloor,
                            useLegacySeedPrecision);
                    }
                    else
                    {
                        inverseTargetHkDelta = LegacyPrecisionMath.Divide(
                            LegacyPrecisionMath.Multiply(
                                0.03,
                                LegacyPrecisionMath.Subtract(x2, x1, useLegacySeedPrecision),
                                useLegacySeedPrecision),
                            thetaFloor,
                            useLegacySeedPrecision);
                    }
                    var (u1ForHk1, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                        uei1,
                        tkbl,
                        qinfbl,
                        tkbl_ms,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                    double hk1 = blState.LegacyKinematic[ibl - 1, side]?.HK2
                        ?? BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            u1ForHk1,
                            theta1,
                            dstar1,
                            0.0,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchC()).HK2;
                    double inverseTargetHkRaw = LegacyPrecisionMath.Add(hk1, inverseTargetHkDelta, useLegacySeedPrecision);
                    inverseTargetHk = LegacyPrecisionMath.Max(inverseTargetHkRaw, hmax, useLegacySeedPrecision);
                    // Trace HTARG at side 2 station 4
                    
                    directMode = false;
                    continue;
                }
            }

            if (usesShearState)
            {
                ctau2 = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau2, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), minCtau), 0.30);
            }

            // Trace pre-update at side 2 station 4 inverse iterations
            
            preUpdateTheta2 = theta2;
            preUpdateDstar2 = dstar2;
            theta2 = Math.Max(LegacyPrecisionMath.AddScaled(theta2, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            dstar2 = Math.Max(LegacyPrecisionMath.AddScaled(dstar2, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            
            if (!directMode)
            {
                uei2 = Math.Max(LegacyPrecisionMath.AddScaled(uei2, rlx, delta[3], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            }
            // Trace post-update at side 2 station 4 inverse iterations
            

            double msq = 0.0;
            if (hstinv > 0.0)
            {
                double uesq = uei2 * uei2 * hstinv;
                msq = uesq / (gm1 * (1.0 - 0.5 * uesq));
            }

            dstar2 = ApplySeedDslim(dstar2, theta2, msq, 1.02, settings.UseLegacyBoundaryLayerInitialization);

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // Classic MRCHUE carries the accepted seed state in REAL
                // storage before the next transition probe / local solve.
                // Leaving station-4 updates wide delays the first drift until
                // the mid-iteration transition packet, where the pre-system
                // dstar/theta state starts landing 1 ULP off.
                if (usesShearState)
                {
                    ApplyLegacySeedPrecision(
                        settings.UseLegacyBoundaryLayerInitialization,
                        ref uei2,
                        ref theta2,
                        ref dstar2,
                        ref ctau2);
                }
                else
                {
                    ApplyLegacySeedPrecision(
                        settings.UseLegacyBoundaryLayerInitialization,
                        ref uei2,
                        ref theta2,
                        ref dstar2,
                        ref ampl2);
                }

                // Legacy MRCHUE carries the accepted downstream BLKIN state
                // into the next seed-probe/TRCHEK2 iteration while leaving the
                // last assembled BLVAR packet untouched. Reusing the pre-update
                // snapshot here freezes the downstream N2 carry and can keep
                // later laminar seed iterations on the wrong path.
                RefreshLegacyKinematicSnapshot(
                    blState,
                    side,
                    ibl,
                    uei2,
                    theta2,
                    dstar2,
                    settings,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                    reybl,
                    reybl_re,
                    reybl_ms);
            }

            // Fortran: IF(DMAX.LE.1.0E-5) — REAL comparison
            if ((float)dmax <= (float)seedTolerance)
            {
                break;
            }

        }

        // Fortran MRCHUE non-convergence handler (xbl.f lines 1894-1914):
        // If DMAX > 0.1 after 25 iterations, extrapolate THI/DSI/UEI from
        // upstream station. If DMAX <= 0.1, keep the last iterate.
        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            float dmaxF = lastDmaxFloat;
            
            if (dmaxF > 0.1f && ibl > 2)
            {
                // Fortran xbl.f:2298-2299:
                //   THI = THET(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
                // Gfortran compiles REAL**0.5 via powf(x, 0.5f), NOT sqrtf(x).
                // powf differs from sqrtf by 1 ULP at some x values (e.g.
                // x=1.0125 → sqrtf=3F80CC96 vs powf=3F80CC97). Using MathF.Pow
                // matches Fortran's **0.5 bit-for-bit.
                float xRatio = (float)blState.XSSI[ibl, side] / (float)blState.XSSI[ibm, side];
                float sqrtRatio = MathF.Pow(xRatio, 0.5f);
                theta2 = (float)blState.THET[ibm, side] * sqrtRatio;
                dstar2 = (float)blState.DSTR[ibm, side] * sqrtRatio;
                // Fortran lines 1911-1913:
                //   UEI = UEDG(IBL,IS)
                //   IF(IBL.GT.2 .AND. IBL.LT.NBL(IS))
                //  &  UEI = 0.5*(UEDG(IBL-1,IS) + UEDG(IBL+1,IS))
                // This extrapolation was missing from the C# port and caused
                // station 11 in NACA 0006 a=8 to use the unmodified Newton-failed
                // UEI instead of the averaged neighbor UEI. Fix matches Fortran.
                float ueiF = (float)blState.UEDG[ibl, side];
                if (ibl > 1 && ibl < blState.NBL[side] - 1)
                {
                    float ueim = (float)blState.UEDG[ibm, side];
                    float ueip = (float)blState.UEDG[ibl + 1, side];
                    ueiF = 0.5f * (ueim + ueip);
                }
                uei2 = ueiF;
                // Fortran xbl.f:2002-2003 — non-convergence CTI reset:
                //   IF(IBL.EQ.ITRAN(IS)) CTI = 0.05
                //   IF(IBL.GT.ITRAN(IS)) CTI = CTAU(IBM,IS)
                // This was missing from the C# port. Without it, the failed
                // Newton's final ctau2 is kept, which differs from Fortran's
                // upstream-carry behavior. At the upper TE station (first
                // post-transition failure to converge), this shifts CTAU by
                // ~15% and cascades into the TESYS CTE merge for the first
                // wake station, breaking wake-side parity.
                // NOTE: In Fortran, ITRAN(IS) is set to IBL BEFORE the Newton
                // loop (at line 1381: IF(TRAN) ITRAN(IS) = IBL). But in C#,
                // blState.ITRAN[side] is set AFTER this extrapolation code (at
                // line ~6598). So we need to use `transitionInterval` (the
                // current-station TRAN flag) instead of blState.ITRAN.
                if (transitionInterval)
                {
                    // This is the transition station (Fortran IBL.EQ.ITRAN)
                    ctau2 = 0.05;
                }
                else if (ibl > blState.ITRAN[side])
                {
                    ctau2 = blState.CTAU[ibm, side];
                }
            }
        }

        // Fortran MRCHUE label 109 (xbl.f lines 1916-1925): after non-convergence
        // (whether DMAX <= 0.1 or > 0.1 with extrapolation), BLPRV+BLKIN+TRCHEK
        // are re-run with the final theta/dstar. For DMAX > 0.1, the theta/dstar
        // were just extrapolated above. For DMAX <= 0.1, they are the last Newton
        // iterate. In both cases, the TRCHEK re-evaluation updates AMI to match
        // the final state. Converged stations (DMAX <= 1e-5) go directly to
        // store without this re-evaluation.
        bool convergedInLoop = (float)lastDmaxFloat <= (float)seedTolerance;
        if (settings.UseLegacyBoundaryLayerInitialization
            && !convergedInLoop
            && !initialTurbAtEntry // Fortran: .NOT.TURB (entry-time carry-forward)
            && ibl > 0) // not similarity station (Fortran: .NOT.SIMI)
        {
            var (postU1, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                uei1, tkbl, qinfbl, tkbl_ms);
            var (postU2, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                uei2, tkbl, qinfbl, tkbl_ms);
            var postKin2 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                postU2, theta2, dstar2, 0.0,
                hstinv, hstinv_ms, gm1, rstbl, rstbl_ms,
                GetHvRat(true), reybl, reybl_re, reybl_ms,
                useLegacyPrecision: true,
                destination: GetEngineKinematicScratchA());
            var postTransition = TransitionModel.ComputeTransitionPoint(
                x1, x2, postU1, postU2,
                theta1, theta2, dstar1, dstar2,
                ampl1, ampl2,
                ncrit, hstinv, hstinv_ms, gm1, rstbl, rstbl_ms,
                GetHvRat(true), reybl, reybl_re, reybl_ms,
                settings.UseModernTransitionCorrections,
                forcedTransitionXi,
                settings.UseLegacyBoundaryLayerInitialization,
                traceSide: side + 1, traceStation: ibl + 1,
                traceIteration: 26, tracePhase: "post_loop_109",
                station1KinematicOverride: blState.LegacyKinematic[ibl - 1, side],
                station2KinematicOverride: postKin2,
                station2PrimaryOverride: null,
                destinationResult: GetPooledTransitionPointPostLoop());
            ampl2 = Math.Max(postTransition.DownstreamAmplification, 0.0);
            transitionInterval = postTransition.TransitionOccurred;
            if (transitionInterval)
            {
                transitionXi = postTransition.TransitionXi;
            }
            // Fortran xbl.f lines 2329-2332: after fresh TRCHEK, CTAU is stored
            // as AMI (laminar) if IBL<ITRAN or CTI (turb) if IBL>=ITRAN, where
            // ITRAN was updated by the fresh TRCHEK. Mirror by resetting
            // usesShearState from the fresh transitionInterval.
            usesShearState = transitionInterval;

            // Apply precision rounding so the secondary refresh uses the same
            // float-rounded values that will be stored in blState.THET/DSTR/UEDG.
            ApplyLegacySeedPrecision(
                settings.UseLegacyBoundaryLayerInitialization,
                ref uei2, ref theta2, ref dstar2, ref ampl2);
            ctau2 = LegacyPrecisionMath.RoundToSingle(ctau2, true);

            // Fortran label 109 (xbl.f lines 2319-2325): BLVAR refreshes secondary
            // variables from the final kinematic. Mode depends on fresh ITRAN:
            //   IF(IBL.LT.ITRAN(IS)) CALL BLVAR(1)  -- laminar
            //   IF(IBL.GE.ITRAN(IS)) CALL BLVAR(2)  -- turbulent
            // where fresh ITRAN is set at lines 2314-2315 from TRCHEK result.
            if (!transitionInterval)
            {
                var postLocalResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                    isWake: false,
                    isTurbOrTran: false,
                    isTran: false,
                    isSimi: false,
                    x1, x2, uei1, uei2,
                    t1: theta1, t2: theta2,
                    d1: dstar1, d2: dstar2,
                    s1: LegacyLaminarShearSeed,
                    s2: LegacyLaminarShearSeed,
                    dw1: 0.0, dw2: 0.0,
                    ampl1: ampl1, ampl2: ampl2,
                    amcrit: ncrit,
                    tkbl, qinfbl, tkbl_ms,
                    hstinv, hstinv_ms, gm1,
                    rstbl, rstbl_ms,
                    GetHvRat(true), reybl, reybl_re, reybl_ms,
                    useLegacyPrecision: true,
                    station1KinematicOverride: blState.LegacyKinematic[ibl - 1, side],
                    station1SecondaryOverride: blState.LegacySecondary[ibl - 1, side],
                    traceSide: side + 1, traceStation: ibl + 1,
                    tracePhase: "post_loop_109_blvar");
                StoreLegacyCarrySnapshots(
                    blState, ibl, side,
                    postLocalResult.Primary2Snapshot,
                    postLocalResult.Kinematic2Snapshot,
                    postLocalResult.Secondary2Snapshot,
                    traceLabel: "post_loop_109_store");
            }
            else
            {
                // Fresh TRCHEK says transition at this station → BLVAR(2) turbulent.
                var postTurbResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                    isWake: false,
                    isTurbOrTran: true,
                    isTran: false,
                    isSimi: false,
                    x1, x2, uei1, uei2,
                    t1: theta1, t2: theta2,
                    d1: dstar1, d2: dstar2,
                    s1: prevCtau, s2: ctau2,
                    dw1: 0.0, dw2: 0.0,
                    ampl1: ampl1, ampl2: ampl2,
                    amcrit: ncrit,
                    tkbl, qinfbl, tkbl_ms,
                    hstinv, hstinv_ms, gm1,
                    rstbl, rstbl_ms,
                    GetHvRat(true), reybl, reybl_re, reybl_ms,
                    useLegacyPrecision: true,
                    station1KinematicOverride: blState.LegacyKinematic[ibl - 1, side],
                    station1SecondaryOverride: blState.LegacySecondary[ibl - 1, side],
                    traceSide: side + 1, traceStation: ibl + 1,
                    tracePhase: "post_loop_109_turb_blvar");
                StoreLegacyCarrySnapshots(
                    blState, ibl, side,
                    postTurbResult.Primary2Snapshot,
                    postTurbResult.Kinematic2Snapshot,
                    postTurbResult.Secondary2Snapshot,
                    traceLabel: "post_loop_109_turb_store");
            }

        }
        else if (settings.UseLegacyBoundaryLayerInitialization
                 && !convergedInLoop
                 && usesShearState
                 && ibl > 0)
        {
            // Fortran MRCHUE label 109 for TURBULENT stations (IBL >= ITRAN):
            // Calls BLVAR(2) with the final POST-update state. This refreshes
            // COM2 secondary (CF, HS, DI, etc.) before line 2234-2238 copies
            // COM2 → COM1 for the next station. Without this, the C# stores
            // the LAST iteration's PRE-update BLVAR snapshot which differs
            // from Fortran's label 109 POST-update BLVAR output.
            // NACA 1408 Re=5M a=2 Nc=12: station 71 transition station oscillates
            // for 25 iters without converging. Fortran label 109 runs BLVAR(2)
            // at iter 25 POST-update state → COM1 at station 72 has different
            // DI1 than what C# stored (iter 25 PRE-update value).
            ApplyLegacySeedPrecision(
                settings.UseLegacyBoundaryLayerInitialization,
                ref uei2, ref theta2, ref dstar2, ref ctau2);
            var postTurbResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                isWake: false,
                isTurbOrTran: true,
                isTran: false,
                isSimi: false,
                x1, x2, uei1, uei2,
                t1: theta1, t2: theta2,
                d1: dstar1, d2: dstar2,
                s1: prevCtau, s2: ctau2,
                dw1: 0.0, dw2: 0.0,
                ampl1: ampl1, ampl2: ampl2,
                amcrit: ncrit,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms, gm1,
                rstbl, rstbl_ms,
                GetHvRat(true), reybl, reybl_re, reybl_ms,
                useLegacyPrecision: true,
                station1KinematicOverride: blState.LegacyKinematic[ibl - 1, side],
                station1SecondaryOverride: blState.LegacySecondary[ibl - 1, side],
                traceSide: side + 1, traceStation: ibl + 1,
                tracePhase: "post_loop_109_turb_blvar");
            StoreLegacyCarrySnapshots(
                blState, ibl, side,
                postTurbResult.Primary2Snapshot,
                postTurbResult.Kinematic2Snapshot,
                postTurbResult.Secondary2Snapshot,
                traceLabel: "post_loop_109_turb_store");
        }
        else if (settings.UseLegacyBoundaryLayerInitialization)
        {
            // Converged path: apply precision rounding here (matches Fortran's
            // REAL store at label 110).
            ApplyLegacySeedPrecision(
                settings.UseLegacyBoundaryLayerInitialization,
                ref uei2, ref theta2, ref dstar2, ref ampl2);
            ctau2 = LegacyPrecisionMath.RoundToSingle(ctau2, true);
        }

        // Trace station 58-60 side 1 converged store for parity comparison
        
        blState.UEDG[ibl, side] = uei2;
        blState.THET[ibl, side] = theta2;
        blState.DSTR[ibl, side] = dstar2;
        blState.CTAU[ibl, side] = usesShearState ? ctau2 : ampl2;
        // Fortran MRCHUE carries CTI across stations as a local variable.
        // Output the converged ctau2 so the caller can carry it to the next station.
        carriedCtauOut = ctau2;
        
        WriteLegacyAmplificationCarry(blState, ibl, side, ampl2);
        blState.MASS[ibl, side] = settings.UseLegacyBoundaryLayerInitialization
            ? LegacyPrecisionMath.Multiply(dstar2, uei2, useLegacyPrecision: true)
            : dstar2 * uei2;
        if (transitionInterval)
        {
            blState.ITRAN[side] = ibl;
            blState.TINDEX[side] = transitionXi;
        }
        else if (!usesShearState)
        {
            blState.ITRAN[side] = Math.Min(ibl + 2, blState.NBL[side]);
        }

        // Classic MRCHUE refreshes the downstream BLKIN state from the accepted
        // primary variables, but it keeps the last assembled BLVAR secondary
        // snapshot instead of rebuilding it from scratch.
        RefreshLegacyKinematicSnapshot(
            blState,
            side,
            ibl,
            uei2,
            theta2,
            dstar2,
            settings,
            tkbl,
            qinfbl,
            tkbl_ms,
            hstinv,
            hstinv_ms,
            rstbl,
            rstbl_ms,
            reybl,
            reybl_re,
            reybl_ms);

        // Store pre-update T/D in LegacyPrimary for MRCHUE COM carry.
        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            var primary = blState.LegacyPrimary[ibl, side];
            if (primary is not null)
            {
                primary.PreUpdateT = LegacyPrecisionMath.RoundToSingle(preUpdateTheta2, true);
                primary.PreUpdateD = LegacyPrecisionMath.RoundToSingle(preUpdateDstar2, true);
            }
        }

    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE inverse-target Hk relation
    // Difference from legacy: The same local inverse-target idea is preserved, but the managed port makes the wake and airfoil cases explicit.
    // Decision: Keep the helper extraction and preserve the original inverse-target formulas.
    private static double ComputeLegacyDirectSeedInverseTargetHk(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        bool wake,
        double prevTheta,
        double prevDstar,
        double prevWakeGap,
        double hstinv,
        bool useLegacyPrecision,
        double? hkPrevOverride = null)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(useLegacyPrecision);
        double uePrev = Math.Max(Math.Abs(blState.UEDG[ibl - 1, side]), 1.0e-10);
        double msqPrev = 0.0;
        if (hstinv > 0.0)
        {
            double uePrevSq = uePrev * uePrev * hstinv;
            msqPrev = uePrevSq / (gm1 * (1.0 - 0.5 * uePrevSq));
        }

        double hkPrev = hkPrevOverride ?? BoundaryLayerCorrelations
            .KinematicShapeParameter(Math.Max(prevDstar - prevWakeGap, 1.0e-10) / Math.Max(prevTheta, 1.0e-10), msqPrev)
            .Hk;
        double x1 = blState.XSSI[ibl - 1, side];
        double x2 = blState.XSSI[ibl, side];

        if (!wake)
        {
            return hkPrev - (0.15 * (x2 - x1) / Math.Max(prevTheta, 1.0e-30));
        }

        // Phase 1 strip: float-only Fortran MRCHUE backward-Euler.
        float hkPrevF = (float)hkPrev;
        float x1F = (float)x1;
        float x2F = (float)x2;
        float t1F = (float)prevTheta;
        float constF = 0.03f * (x2F - x1F) / t1F;
        float hk2F = hkPrevF;
        for (int iter = 0; iter < 3; iter++)
        {
            float hkm1 = hk2F - 1.0f;
            float hkm1sq = hkm1 * hkm1;
            float hkm1cu = hkm1sq * hkm1;
            float denom = 1.0f + 3.0f * constF * hkm1sq;
            hk2F = hk2F - (hk2F + constF * hkm1cu - hkPrevF) / denom;
        }

        return hk2F;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE similarity-station refinement
    // Difference from legacy: The same similarity-station solve is preserved, but the managed code wraps it around `AssembleSimilarityStation` and explicit linear solves with trace output.
    // Decision: Keep the explicit helper structure and preserve the classic direct/inverse restart semantics.
    private static void RefineSimilarityStationSeed(
        BoundaryLayerSystemState blState,
        int side,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        const int maxIterations = 25;
        const double seedTolerance = 1.0e-5;
        const double laminarHkLimit = 3.8;
        const double hklim = 1.02;

        int ibl = 1;
        double xsi = blState.XSSI[ibl, side];
        double uei = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1e-10);
        double theta = blState.THET[ibl, side];
        double dstar = blState.DSTR[ibl, side];
        double ampl = 0.0;
        ApplyLegacySeedPrecision(
            settings.UseLegacyBoundaryLayerInitialization,
            ref uei,
            ref theta,
            ref dstar,
            ref ampl);

        double thetaSeed = theta;
        double dstarSeed = dstar;
        double seedCtau = LegacyPrecisionMath.RoundToSingle(
            LegacyLaminarShearSeed,
            settings.UseLegacyBoundaryLayerInitialization);

        var solver = DenseLinearSystemSolver.Shared;
        bool directMode = true;
        double inverseTargetHk = 0.0;

        // Legacy block: xbl.f MRCHUE similarity-station Newton loop.
        // Difference from legacy: The same local seed system is solved, but the managed code exposes the assembled rows and step metrics for parity tracing.
        // Decision: Keep the diagnostics and preserve the original iteration logic.
        for (int iter = 0; iter < maxIterations; iter++)
        {
            var localResult = AssembleSimilarityStation(
                xsi, uei, theta, dstar, ampl, settings,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                rstbl, rstbl_ms,
                reybl, reybl_re, reybl_ms,
                traceSide: side + 1,
                traceStation: ibl + 1,
                traceIteration: iter + 1,
                tracePhase: "similarity_seed");

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // The first downstream laminar station reuses the similarity
                // station as COM1 in classic MRCHUE, so parity mode must retain
                // the last assembled BLKIN/BLVAR state here as well.
                StoreLegacyCarrySnapshots(
                    blState,
                    ibl,
                    side,
                    localResult.Primary2Snapshot,
                    localResult.Kinematic2Snapshot,
                    localResult.Secondary2Snapshot,
                    traceLabel: "similarity_seed");
            }

            var matrix = SolverBuffers.Matrix4x4Double;
            var rhs = SolverBuffers.Vector4Double;
            Array.Clear(matrix);
            Array.Clear(rhs);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    matrix[row, col] = localResult.VS2[row, col];
                }

                rhs[row] = localResult.Residual[row];
            }

            if (directMode)
            {
                matrix[3, 3] = 1.0;
                rhs[3] = 0.0;
            }
            else
            {
                matrix[3, 1] = localResult.HK2_T2;
                matrix[3, 2] = localResult.HK2_D2;
                matrix[3, 3] = localResult.HK2_U2 * localResult.U2_UEI;
                rhs[3] = inverseTargetHk - localResult.HK2;
            }


            double[] delta;
            try
            {
                delta = SolveSeedLinearSystem(
                    solver,
                    matrix,
                    rhs,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            SeedStepMetrics stepMetrics = ComputeSeedStepMetrics(
                delta,
                theta,
                dstar,
                10.0,
                uei,
                side + 1,
                2,
                iter + 1,
                directMode ? "direct" : "inverse",
                includeUe: !directMode,
                settings.UseLegacyBoundaryLayerInitialization);

            double dmax = stepMetrics.Dmax;
            double rlx = ComputeLegacySeedRelaxation(dmax, settings.UseLegacyBoundaryLayerInitialization);
            double stepNorm = stepMetrics.ResidualNorm;


            if (directMode)
            {
                if (hstinv > 0.0)
                {
                    double uesqTest = uei * uei * hstinv;
                    double msqTest = uesqTest / (gm1 * (1.0 - 0.5 * uesqTest));
                    double predictedDstar = LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization);
                    double predictedTheta = LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization);
                    double htest = predictedDstar / Math.Max(predictedTheta, 1.0e-30);
                    double hkTest = BoundaryLayerCorrelations.KinematicShapeParameter(
                        htest,
                        msqTest,
                        settings.UseLegacyBoundaryLayerInitialization).Hk;
                    if (hkTest >= laminarHkLimit)
                    {
                        // Classic MRCHUE restarts the same similarity station in inverse mode
                        // rather than accepting a separate minimum-residual direct update.
                        inverseTargetHk = Math.Max(localResult.HK2, laminarHkLimit);
                        directMode = false;
                        continue;
                    }
                }
            }

            theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            if (!directMode)
            {
                uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
            }

            double msq = 0.0;
            if (hstinv > 0.0)
            {
                double uesq = uei * uei * hstinv;
                msq = uesq / (gm1 * (1.0 - 0.5 * uesq));
            }

            dstar = ApplySeedDslim(dstar, theta, msq, hklim, settings.UseLegacyBoundaryLayerInitialization);
            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                // Classic MRCHUE keeps the accepted similarity-station state in
                // REAL storage before the next Newton pass. Leaving the updated
                // station in wide precision preserves a 1-ULP theta drift that
                // later contaminates the pre-Newton TE and USAV consumers.
                ApplyLegacySeedPrecision(
                    settings.UseLegacyBoundaryLayerInitialization,
                    ref uei,
                    ref theta,
                    ref dstar,
                    ref ampl);
            }
            if ((float)dmax <= (float)seedTolerance)
            {
                break;
            }
        }

        blState.THET[0, side] = theta;
        blState.DSTR[0, side] = dstar;
        blState.UEDG[ibl, side] = uei;
        blState.THET[ibl, side] = theta;
        blState.DSTR[ibl, side] = dstar;
        blState.CTAU[ibl, side] = ampl;
        WriteLegacyAmplificationCarry(blState, ibl, side, ampl);
        blState.MASS[ibl, side] = settings.UseLegacyBoundaryLayerInitialization
            ? LegacyPrecisionMath.Multiply(dstar, uei, useLegacyPrecision: true)
            : dstar * uei;

        // Classic MRCHUE carries the last assembled similarity-station BLVAR
        // snapshot into the first laminar interval as COM1, but it refreshes
        // the BLKIN state from the accepted primary variables.
        RefreshLegacyKinematicSnapshot(
            blState,
            side,
            ibl,
            uei,
            theta,
            dstar,
            settings,
            tkbl,
            qinfbl,
            tkbl_ms,
            hstinv,
            hstinv_ms,
            rstbl,
            rstbl_ms,
            reybl,
            reybl_re,
            reybl_ms);

    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE wake station Newton refinement
    // Difference from legacy: Fortran MRCHUE processes wake stations in the same
    // DO 1000 loop as surface stations, with BLSYS(2) + direct Ue constraint.
    // The C# separates the wake seed from the surface seed.
    // Decision: Add explicit wake Newton refinement to match Fortran MRCHUE.
    private static void RefineWakeSeedStation(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        double wakeGap,
        WakeSeedData? wakeSeed,
        double teGap)
    {
        const int maxIterations = 25;
        const double seedTolerance = 1.0e-5;
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        var solver = DenseLinearSystemSolver.Shared;

        int ibm = ibl - 1;
        double theta = blState.THET[ibl, side];
        double dstar = blState.DSTR[ibl, side];
        double ctau = Math.Max(blState.CTAU[ibl, side], 1e-7);
        double uei = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1e-10);

        double prevTheta = Math.Max(blState.THET[ibm, side], 1e-10);
        double prevDstar = Math.Max(blState.DSTR[ibm, side], 1e-10);
        double prevCtau = Math.Max(blState.CTAU[ibm, side], 1e-7);
        double prevWakeGap = ibm > blState.IBLTE[side]
            ? GetWakeGap(wakeSeed, teGap, ibm - blState.IBLTE[side])
            : 0.0;

        double dswaki = wakeGap;
        double hklim = 1.00005;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Apply DSLIM clamping (Fortran xbl.f line 1100)
            double msq = 0.0;
            if (hstinv > 0.0)
            {
                double uesq = uei * uei * hstinv;
                double denom = gm1 * (1.0 - 0.5 * uesq);
                if (Math.Abs(denom) > 1e-20)
                    msq = uesq / denom;
            }
            double dsw = dstar - dswaki;
            double dsMin = hklim * theta;
            if (dsw < dsMin) dsw = dsMin;
            dstar = dsw + dswaki;

            // Assemble station system (Fortran BLSYS(2) — turbulent wake)
            var localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                isWake: true,
                isTurbOrTran: true,
                isTran: false,
                isSimi: false,
                x1: blState.XSSI[ibm, side],
                x2: blState.XSSI[ibl, side],
                uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10),
                uei2: uei,
                t1: prevTheta,
                t2: theta,
                d1: prevDstar,
                d2: dstar,
                s1: prevCtau,
                s2: ctau,
                dw1: prevWakeGap,
                dw2: dswaki,
                ampl1: 0.0,
                ampl2: 0.0,
                amcrit: settings.GetEffectiveNCrit(side),
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                gm1, rstbl, rstbl_ms,
                GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                reybl, reybl_re, reybl_ms,
                useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyKinematic[ibm, side]
                    : null,
                station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacySecondary[ibm, side]
                    : null);

            double[] residual = localResult.Residual;
            double[,] vs2 = localResult.VS2;
            double u2 = localResult.U2;
            double u2Uei = localResult.U2_UEI;

            // Direct mode: prescribe Ue (dUe=0)
            var matrix = SolverBuffers.Matrix4x4Double;
            var rhs = SolverBuffers.Vector4Double;
            Array.Clear(matrix);
            Array.Clear(rhs);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                    matrix[row, col] = vs2[row, col];
                rhs[row] = residual[row];
            }
            matrix[3, 0] = 0.0;
            matrix[3, 1] = 0.0;
            matrix[3, 2] = 0.0;
            matrix[3, 3] = 1.0;
            rhs[3] = 0.0;

            double[] delta;
            try
            {
                delta = SolveSeedLinearSystem(solver, matrix, rhs,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            // Station 98 system dump for MRCHUE parity
            

            double dmax = Math.Max(
                Math.Max(Math.Abs(delta[1] / Math.Max(theta, 1e-10)),
                         Math.Abs(delta[2] / Math.Max(dstar, 1e-10))),
                Math.Abs(delta[0] / Math.Max(ctau, 1e-7)));

            // Relaxation
            double rlx = 1.0;
            if (rlx * Math.Abs(delta[1]) > 0.3 * theta)
                rlx = 0.3 * theta / Math.Max(Math.Abs(delta[1]), 1e-30);
            if (rlx * Math.Abs(delta[2]) > 0.3 * Math.Abs(dstar))
                rlx = 0.3 * Math.Abs(dstar) / Math.Max(Math.Abs(delta[2]), 1e-30);
            if (rlx * Math.Abs(delta[0]) > 0.3 * ctau)
                rlx = 0.3 * ctau / Math.Max(Math.Abs(delta[0]), 1e-30);

            // Update
            ctau += rlx * delta[0];
            theta += rlx * delta[1];
            dstar += rlx * delta[2];

            // Clamp
            ctau = Math.Max(ctau, 1e-7);
            ctau = Math.Min(ctau, 0.30);

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                StoreLegacyCarrySnapshots(
                    blState, ibl, side,
                    localResult.Primary2Snapshot,
                    localResult.Kinematic2Snapshot,
                    localResult.Secondary2Snapshot,
                    traceLabel: "wake_seed_refine");
            }

            
            if (dmax <= seedTolerance)
                break;
        }

        // Store converged state
        blState.THET[ibl, side] = theta;
        blState.DSTR[ibl, side] = dstar;
        blState.CTAU[ibl, side] = ctau;
        blState.MASS[ibl, side] = settings.UseLegacyBoundaryLayerInitialization
            ? LegacyPrecisionMath.Multiply(dstar, uei, true)
            : dstar * uei;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE downstream laminar refinement
    // Difference from legacy: The default managed branch uses an acceptance-only residual reduction pass, while the parity branch delegates to the dedicated legacy seed refinement helper.
    // Decision: Keep the managed refinement improvement for default execution and preserve the classic refinement through the parity helper.
    private static void RefineLaminarSeedStation(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        ref double carriedCtau)
    {
        
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        const int maxIterations = 8;
        const double maxNormalizedStep = 0.20;
        const double acceptanceTolerance = 1e-10;
        const double hklim = 1.02;

        int ibm = ibl - 1;
        double x1 = blState.XSSI[ibm, side];
        double x2 = blState.XSSI[ibl, side];
        double uei1 = Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10);
        double uei2 = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1e-10);
        double theta1 = blState.THET[ibm, side];
        double dstar1 = blState.DSTR[ibm, side];
        double ampl1 = Math.Max(blState.CTAU[ibm, side], 0.0);

        double theta2 = blState.THET[ibl, side];
        double dstar2 = blState.DSTR[ibl, side];
        // Fortran MRCHUE: BLPRV sets AMPL2 = AMI at each Newton iteration.
        // AMI is carried from the previous station's converged TRCHEK result.
        // Use ampl1 (the upstream station's N) as the initial AMPL2, matching
        // Fortran's behavior. blState.CTAU[ibl,side] may have a stale value
        // from the initial seed that doesn't match the carried AMI.
        double ampl2 = settings.UseLegacyBoundaryLayerInitialization
            ? Math.Max(ampl1, 0.0)
            : Math.Max(blState.CTAU[ibl, side], 0.0);
        // Fortran MRCHUE carries CTI as a local variable across stations.
        // Use the carried value instead of resetting to 0.03 every station.
        double initialCtau2 = carriedCtau > 0 ? carriedCtau : LegacyLaminarShearSeed;
        

        double thetaSeed = theta2;
        double dstarSeed = dstar2;
        double ncrit = settings.GetEffectiveNCrit(side);
        double? forcedTransitionXi = GetLegacyParityForcedTransitionXi(blState, settings, side);
        

        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            double currentStoredValue = Math.Max(blState.CTAU[ibl, side], 0.0);
            bool copiedShearState = ibm >= blState.ITRAN[side];
            if (copiedShearState && currentStoredValue > 0.0)
            {
                // The parity seed march copies CTAU forward before the local
                // station solve. Once a station has transitioned, the next
                // station therefore receives the carried CTI in its own slot
                // before the local refinement starts. Pre-transition slots
                // still overload CTAU with AMI, so only stations whose
                // upstream neighbor already carries shear state may reuse the
                // copied slot as CTI.
                initialCtau2 = currentStoredValue;
            }
            else
            {
                // Fortran MRCHUE carries CTI as a local variable.
                // For laminar stations, CTAU stores AMI (amplification),
                // so reading CTI from CTAU gives the wrong value.
                // Use the carried CTI from the previous station instead.
                initialCtau2 = carriedCtau > 0 ? carriedCtau : LegacyLaminarShearSeed;
            }
            
            RefineLegacySeedStation(
                blState,
                side,
                ibl,
                settings,
                tkbl,
                qinfbl,
                tkbl_ms,
                hstinv,
                hstinv_ms,
                rstbl,
                rstbl_ms,
                reybl,
                reybl_re,
                reybl_ms,
                x1,
                x2,
                uei1,
                uei2,
                theta1,
                theta2,
                dstar1,
                dstar2,
                ampl1,
                ampl2,
                initialCtau2,
                thetaSeed,
                dstarSeed,
                ncrit,
                ref carriedCtau);
            return;
        }

        var solver = DenseLinearSystemSolver.Shared;
        double residualNorm = ComputeLaminarResidualNorm(
            x1, x2, uei1, uei2, theta1, theta2, dstar1, dstar2, ampl1, ampl2, settings,
            tkbl, qinfbl, tkbl_ms,
            hstinv, hstinv_ms,
            rstbl, rstbl_ms,
            reybl, reybl_re, reybl_ms,
            station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                ? blState.LegacyKinematic[ibl - 1, side]
                : null,
            station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                ? blState.LegacySecondary[ibl - 1, side]
                : null);

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Classic MRCHUE refreshes the downstream amplification level with
            // TRCHEK before assembling the local station system. Keeping the
            // carried upstream value here changes the station input before the
            // Newton step even begins.
            var transition = TransitionModel.ComputeTransitionPoint(
                x1,
                x2,
                uei1,
                uei2,
                theta1,
                theta2,
                dstar1,
                dstar2,
                ampl1,
                ampl2,
                ncrit,
                hstinv,
                hstinv_ms,
                gm1,
                rstbl,
                rstbl_ms,
                GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                reybl,
                reybl_re,
                reybl_ms,
                settings.UseModernTransitionCorrections,
                forcedTransitionXi,
                settings.UseLegacyBoundaryLayerInitialization,
                traceSide: side + 1,
                traceStation: ibl + 1,
                traceIteration: iter + 1,
                tracePhase: "laminar_seed_probe",
                station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyKinematic[ibl - 1, side]
                    : null,
                station2KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyKinematic[ibl, side]
                    : null,
                station2PrimaryOverride: ResolveLegacyPrimaryStationStateOverride(
                    blState,
                    ibl,
                    side,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    uei2,
                    theta2,
                    dstar2,
                    settings.UseLegacyBoundaryLayerInitialization),
                destinationResult: GetPooledTransitionPointLaminarSeed());
            // TRCHEK2 updates the downstream N2 implicitly even when the interval
            // stays laminar. Using only the explicit AX*DX predictor here leaves
            // the station input behind the classic march before the Newton solve.
            ampl2 = Math.Max(transition.DownstreamAmplification, 0.0);
            var localResult = AssembleLaminarStation(
                x1, x2, uei1, uei2, theta1, theta2, dstar1, dstar2, ampl1, ampl2, settings,
                tkbl, qinfbl, tkbl_ms,
                hstinv, hstinv_ms,
                rstbl, rstbl_ms,
                reybl, reybl_re, reybl_ms,
                station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacyKinematic[ibl - 1, side]
                    : null,
                station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacySecondary[ibl - 1, side]
                    : null,
                station2SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                    ? blState.LegacySecondary[ibl, side]
                    : null,
                station2PrimaryOverride: ResolveLegacyPrimaryStationStateOverride(
                    blState,
                    ibl,
                    side,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    uei2,
                    theta2,
                    dstar2,
                    settings.UseLegacyBoundaryLayerInitialization),
                traceSide: side + 1,
                traceStation: ibl + 1,
                traceIteration: iter + 1,
                tracePhase: "laminar_seed");

            if (settings.UseLegacyBoundaryLayerInitialization)
            {
                StoreLegacyCarrySnapshots(
                    blState,
                    ibl,
                    side,
                    localResult.Primary2Snapshot,
                    localResult.Kinematic2Snapshot,
                    localResult.Secondary2Snapshot,
                    traceLabel: "laminar_seed");
            }

            var matrix = SolverBuffers.Matrix4x4Double;
            var rhs = SolverBuffers.Vector4Double;
            Array.Clear(matrix);
            Array.Clear(rhs);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    matrix[row, col] = localResult.VS2[row, col];
                }

                rhs[row] = localResult.Residual[row];
            }

            matrix[3, 3] = 1.0;

            double[] delta;
            try
            {
                delta = SolveSeedLinearSystem(
                    solver,
                    matrix,
                    rhs,
                    useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            // Keep this acceptance-only remarch on the same parity helper chain
            // as MRCHUE so the debug trace can stop at the first true mismatch.
            // Fortran MRCHDU DMAX: MAX(|dT/THI|,|dD/DSI|,|dU/UEI|)
            // plus |dS/(10*CTI)| for turbulent only (IBL >= ITRAN).
            bool mrchduTurb = ibl >= blState.ITRAN[side];
            double localCti = Math.Max(blState.CTAU[ibl, side], 1e-7);
            double mrchduShearScale = mrchduTurb
                ? LegacyPrecisionMath.Multiply(10.0, localCti, settings.UseLegacyBoundaryLayerInitialization)
                : 10.0;
            SeedStepMetrics stepMetrics = ComputeSeedStepMetrics(
                delta,
                theta2,
                dstar2,
                mrchduShearScale,
                uei2,
                side + 1,
                ibl + 1,
                iter + 1,
                "direct",
                includeUe: true,
                settings.UseLegacyBoundaryLayerInitialization,
                includeShearInDmax: mrchduTurb);

            double dmax = stepMetrics.Dmax;
            double rlx = (dmax > maxNormalizedStep) ? maxNormalizedStep / dmax : 1.0;
            bool accepted = false;

            while (rlx >= 1e-3)
            {
                double candAmpl2 = Math.Max(LegacyPrecisionMath.AddScaled(ampl2, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), 0.0);
                double candTheta2 = LegacyPrecisionMath.AddScaled(theta2, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization);
                double candDstar2 = LegacyPrecisionMath.AddScaled(dstar2, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization);

                if (candTheta2 < 0.5 * thetaSeed || candDstar2 < 0.5 * dstarSeed)
                {
                    rlx *= 0.5;
                    continue;
                }

                double msq = 0.0;
                if (hstinv > 0.0)
                {
                    double uesq = uei2 * uei2 * hstinv;
                msq = uesq / (gm1 * (1.0 - 0.5 * uesq));
                }

                candDstar2 = ApplySeedDslim(candDstar2, candTheta2, msq, hklim, settings.UseLegacyBoundaryLayerInitialization);

                double candNorm = ComputeLaminarResidualNorm(
                    x1, x2, uei1, uei2, theta1, candTheta2, dstar1, candDstar2, ampl1, candAmpl2, settings,
                    tkbl, qinfbl, tkbl_ms,
                    hstinv, hstinv_ms,
                    rstbl, rstbl_ms,
                    reybl, reybl_re, reybl_ms,
                    station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacyKinematic[ibl - 1, side]
                        : null,
                    station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                        ? blState.LegacySecondary[ibl - 1, side]
                        : null);

                if (candNorm + acceptanceTolerance < residualNorm)
                {
                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        // Classic MRCHUE keeps each accepted laminar-seed update
                        // in REAL storage before the next outer iteration reuses
                        // the carried amplification/state. Leaving only the final
                        // close-out cast in place preserves a 1-ULP ampl drift at
                        // the alpha-0 station-3 -> station-4 handoff.
                        ApplyLegacySeedPrecision(
                            settings.UseLegacyBoundaryLayerInitialization,
                            ref uei2,
                            ref candTheta2,
                            ref candDstar2,
                            ref candAmpl2);
                    }

                    ampl2 = candAmpl2;
                    theta2 = candTheta2;
                    dstar2 = candDstar2;
                    residualNorm = candNorm;
                    accepted = true;
                    break;
                }

                rlx *= 0.5;
            }

            if (!accepted || residualNorm < 1e-8)
            {
                break;
            }
        }

        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            // Reduced-panel alpha-0 station-4 closes in the legacy laminar seed
            // helper, and classic MRCHUE stores that accepted state back through
            // REAL staging before downstream consumers read the packet.
            ApplyLegacySeedPrecision(
                settings.UseLegacyBoundaryLayerInitialization,
                ref uei2,
                ref theta2,
                ref dstar2,
                ref ampl2);
        }

        blState.UEDG[ibl, side] = uei2;
        blState.THET[ibl, side] = theta2;
        blState.DSTR[ibl, side] = dstar2;
        blState.CTAU[ibl, side] = initialCtau2;
        WriteLegacyAmplificationCarry(blState, ibl, side, ampl2);
        blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(
            dstar2,
            uei2,
            settings.UseLegacyBoundaryLayerInitialization);

        // The downstream BLKIN state must follow the accepted primary update,
        // but the downstream BLVAR secondary snapshot must stay on the last
        // assembled legacy state instead of being reassembled here.
        RefreshLegacyKinematicSnapshot(
            blState,
            side,
            ibl,
            uei2,
            theta2,
            dstar2,
            settings,
            tkbl,
            qinfbl,
            tkbl_ms,
            hstinv,
            hstinv_ms,
            rstbl,
            rstbl_ms,
            reybl,
            reybl_re,
            reybl_ms);

    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHDU
    // Difference from legacy: This is the explicit parity-only remarch of the full airfoil and wake path that the classic solver drives implicitly after the initial seed pass.
    // Decision: Keep the dedicated helper and preserve the legacy remarch ordering, constraint logic, and wake mirroring.
    private static void RemarchBoundaryLayerLegacyDirect(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double teGap,
        WakeSeedData? wakeSeed,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms)
    {
        // Static call counter for MRCHDU debugging
        _mrchduCallCount++;
        int mrchduCall = _mrchduCallCount;

        
        
        var solver = DenseLinearSystemSolver.Shared;
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        const double legacySeedTolerance = 5.0e-6;
        const double legacySeedSensitivityWeight = 1000.0;

        // Legacy block: xbl.f MRCHDU side-by-side remarch.
        // Difference from legacy: The same remarch semantics are preserved, but the managed code uses explicit helpers for shear-state decoding, local assembly, and trace reporting.
        // Decision: Keep the explicit structure and preserve the legacy remarch order and transition handling.
        for (int side = 0; side < 2; side++)
        {
            int oldTransitionStation = blState.ITRAN[side];
            double ncrit = settings.GetEffectiveNCrit(side);
            double? forcedTransitionXi = GetLegacyParityForcedTransitionXi(blState, settings, side);
            bool turb = false;
            double sens = 0.0;
            double senNew = 0.0;

            // Classic MRCHDU remarches both airfoil sides from the similarity
            // station onward and only uses the wake as a continuation of side 2.
            // The parity path therefore has to start at station 1 on both sides.
            blState.ITRAN[side] = blState.IBLTE[side];
            // Fortran MRCHDU carries AMI through the station loop. AMI = AMPL2
            // from the previous station's TRCHEK. This is the local carry that
            // feeds into the next station's BLPRV as AMPL2 = AMI. We replicate
            // this by carrying the last station's converged ampl forward.
            double mrchduCarriedAmpl = 0.0;
            // Note: Fortran COM2→COM1 carries PRE-update T1/D1. The C# currently uses
            // POST-update blState.THET/DSTR. This causes D1 differences at wake stations
            // after TESYS (where the delta is large). A targeted fix is needed but the
            // universal carry approach causes regressions. See project_parity_state.md.

            for (int ibl = 1; ibl < blState.NBL[side]; ibl++)
            {
                int ibm = ibl - 1;
                bool wake = ibl > blState.IBLTE[side];
                bool simi = ibl == 1;
                bool tran = false;
                // Fortran AMI: preserved across TRCHEK but NOT updated by Newton.
                // The MRCHDU stores AMI (not the Newton-updated AMPL2) in CTAU.
                double trchekAmpl = 0.0;

                double xsi = blState.XSSI[ibl, side];
                double uei = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1e-10);
                double theta = Math.Max(blState.THET[ibl, side], 1e-10);
                double dstar = Math.Max(blState.DSTR[ibl, side], 1e-10);
                // n6h20 trace: COM1 (= ibm secondary) at start of i=72 across calls 7-11
                
                
                
                // Trace MRCHDU input at station 2 side 1 for all calls
                
                InitializeLegacySeedStoredShear(
                    blState.CTAU[ibl, side],
                    ibl,
                    oldTransitionStation,
                    out double ctau,
                    out double ampl);
                ampl = ibl >= oldTransitionStation
                    ? ReadLegacyAmplificationCarry(blState, ibm, side, ampl)
                    : Math.Max(ampl, 0.0);

                double wakeGap = wake
                    ? GetWakeGap(wakeSeed, teGap, ibl - blState.IBLTE[side])
                    : 0.0;
                // Fortran: 1.02000 and 1.00005 are REAL constants → float multiply
                double hklim = wake ? 1.00005 : 1.02;
                if (settings.UseLegacyBoundaryLayerInitialization)
                {
                    float hklimF = wake ? 1.00005f : 1.02f;
                    float dswF = (float)wakeGap;
                    dstar = (float)Math.Max((float)dstar - dswF, hklimF * (float)theta) + dswF;
                }
                else
                {
                    dstar = Math.Max(dstar - wakeGap, hklim * theta) + wakeGap;
                }

                double ueref = 0.0;
                double hkref = 0.0;
                bool converged = false;
                double prevTheta = 0.0;
                double prevDstar = 0.0;
                double prevWakeGap = 0.0;
                double prevCtau = 0.0;
                double prevAmpl = 0.0;

                float lastDmaxForExtrapolation = 0.0f;
                for (int iter = 0; iter < 25; iter++)
                {
                    
                    // NACA 0021 a10 nc12 debug: MRCHDU iter 12 stations 65-68 side 2
                    
                    // NACA 0012 5K debug: dump per-iter state at stn 69 s=2 mc=1
                    
                    // Station 93/98: state at TOP of iteration (POST-DSLIM from prev iter)
                    
                    // Trace MRCHDU iter at every wake station IS=2 IBL>=90 in mc=3
                    
                    
                    // clarkv transition station trace (side 2 ibl=50 0-based = station 51 1-based)
                    // fx63120 transition station trace (side 2 ibl=60 0-based = station 61 1-based)
                    // Trace station 83 init MRCHDU inputs
                    
                    if (wake && ibl == blState.IBLTE[side] + 1)
                    {
                        // Fortran line 354: DTE uses raw ANTE, not WGAP(1).
                        double anteForMerge = GetFirstWakeStationTeGap(wakeSeed, teGap);
                        ComputeLegacyWakeTeMergeState(
                            blState,
                            anteForMerge,
                            settings.UseLegacyBoundaryLayerInitialization,
                            out prevTheta,
                            out prevDstar,
                            out prevCtau);
                        
                        // prevWakeGap is used as DSWAKI in BLPRV for the previous
                        // station; that's WGAP(1) (cubic-evaluated), not ANTE.
                        prevWakeGap = GetWakeGap(wakeSeed, teGap, 1);
                    }
                    else
                    {
                        prevTheta = Math.Max(blState.THET[ibm, side], 1e-10);
                        prevDstar = Math.Max(blState.DSTR[ibm, side], 1e-10);
                        prevWakeGap = wake
                            ? GetWakeGap(wakeSeed, teGap, ibm - blState.IBLTE[side])
                            : 0.0;

                        ReadLegacySeedStoredShear(
                            blState.CTAU[ibm, side],
                            ibm,
                            blState.ITRAN[side],
                            out prevCtau,
                            out prevAmpl);
                        // Fortran COM1.AMPL1 comes from the previous station's
                        // Newton-converged AMPL2, NOT from the carry array.
                        // Use the MRCHDU-local carried amplification if available.
                        if (mrchduCarriedAmpl > 0.0)
                            prevAmpl = mrchduCarriedAmpl;
                        else
                            prevAmpl = ReadLegacyAmplificationCarry(blState, ibm, side, prevAmpl);
                    }

                    TransitionModel.TransitionPointResult? trchekFullPoint = null;
                    
                    // Fortran: IF((.NOT.SIMI) .AND. (.NOT.TURB)) CALL TRCHEK
                    // Note: Fortran does NOT check TRAN — TRCHEK runs at every
                    // Newton iteration even after transition is detected. This
                    // updates AMI = AMPL2 at each iteration, so the final AMI
                    // reflects the converged transition point.
                    if (!simi && !turb)
                    {
                        double x1 = blState.XSSI[ibm, side];
                        double ue1 = Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10);
                        double theta1 = Math.Max(blState.THET[ibm, side], 1e-10);
                        double dstar1 = Math.Max(blState.DSTR[ibm, side], 1e-10);
                        var transitionKinematic1 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            ue1,
                            theta1,
                            dstar1,
                            0.0,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchA());
                        double rt1 = transitionKinematic1.RT2;
                        double hk1 = Math.Max(transitionKinematic1.HK2, 1.05);

                        var transitionKinematic2 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            uei,
                            theta,
                            dstar,
                            0.0,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchB());
                        double rt2 = transitionKinematic2.RT2;
                        double hk2 = Math.Max(transitionKinematic2.HK2, 1.05);

                        // Use CheckTransitionExact for legacy mode to match
                        // Fortran TRCHEK which recomputes HKT/RTT from BLKIN.

                        var transition = settings.UseLegacyBoundaryLayerInitialization
                            ? TransitionModel.CheckTransitionExact(
                                x1, xsi,
                                Math.Max(prevAmpl, 0.0),
                                Math.Max(ampl, 0.0),
                                ncrit,
                                ue1, uei,
                                theta1, theta,
                                dstar1, dstar,
                                hstinv, hstinv_ms,
                                gm1, rstbl, rstbl_ms,
                                GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                                reybl, reybl_re, reybl_ms,
                                settings.UseModernTransitionCorrections,
                                forcedTransitionXi,
                                settings.UseLegacyBoundaryLayerInitialization,
                                out trchekFullPoint,
                                traceSide: side + 1,
                                traceStation: ibl + 1,
                                traceIteration: iter + 1,
                                tracePhase: "legacy_direct_remarch")
                            : TransitionModel.CheckTransition(
                                x1, xsi,
                                Math.Max(prevAmpl, 0.0),
                                Math.Max(ampl, 0.0),
                                ncrit,
                                hk1, theta1, rt1, ue1, dstar1,
                                hk2, theta, rt2, uei, dstar,
                                settings.UseModernTransitionCorrections,
                                forcedTransitionXi,
                                settings.UseLegacyBoundaryLayerInitialization);

                        // Fortran MRCHDU line 1381: AMI = AMPL2
                        // AMI is set to the TRCHEK-updated AMPL2 here. The Newton
                        // iteration will further update AMPL2, but AMI is NOT updated
                        // by the Newton. The CTAU store uses AMI, not AMPL2.
                        ampl = transition.TransitionOccurred
                            ? Math.Max(transition.DownstreamAmplification, 0.0)
                            : Math.Max(transition.AmplAtTransition, 0.0);
                        // Save AMI equivalent (pre-Newton TRCHEK value)
                        trchekAmpl = ampl;
                        
                        // Fortran reevaluates TRAN on every Newton iteration.
                        // Do not latch a prior true result across later
                        // iterations, or downstream stations will start in
                        // turbulent mode too early.
                        tran = transition.TransitionOccurred;
                        if (tran)
                        {
                            blState.ITRAN[side] = ibl;
                        }
                        else
                        {
                            blState.ITRAN[side] = Math.Min(ibl + 2, blState.NBL[side]);
                        }
                        
                    }
                    

                    double[] residual;
                    double[,] vs2;
                    double currentU2;
                    double currentU2Uei;
                    double hk2Current = 0.0;
                    double hk2T2 = 0.0;
                    double hk2D2 = 0.0;
                    double hk2U2 = 0.0;

                    if (wake && ibl == blState.IBLTE[side] + 1)
                    {
                        double wakeStrippedDstar = LegacyPrecisionMath.Subtract(
                            dstar,
                            wakeGap,
                            settings.UseLegacyBoundaryLayerInitialization);
                        var teResult = BoundaryLayerSystemAssembler.AssembleTESystem(
                            prevCtau,
                            prevTheta,
                            prevDstar,
                            hk2: 0.0,
                            rt2: 0.0,
                            msq2: 0.0,
                            h2: 0.0,
                            s2: ctau,
                            t2: theta,
                            // TESYS mirrors BLPRV/TESYS in XFoil: pass the wake
                            // displacement without WGAP and supply DW2 separately.
                            d2: wakeStrippedDstar,
                            dw2: wakeGap,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                        residual = teResult.Residual;
                        vs2 = teResult.VS2;

                        (currentU2, currentU2Uei, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                            uei,
                            tkbl,
                            qinfbl,
                            tkbl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                        var currentKinematic = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            currentU2,
                            theta,
                            wakeStrippedDstar,
                            wakeGap,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchA());
                        hk2Current = currentKinematic.HK2;
                        hk2T2 = currentKinematic.HK2_T2;
                        hk2D2 = currentKinematic.HK2_D2;
                        hk2U2 = currentKinematic.HK2_U2;

                        if (settings.UseLegacyBoundaryLayerInitialization)
                        {
                            // Fortran TESYS calls BLVAR(3) after BLPRV+BLKIN set COM2.
                            var wakeSec = BoundaryLayerSystemAssembler.ComputeStationVariables(
                                3, currentKinematic.HK2, currentKinematic.RT2, currentKinematic.M2,
                                currentKinematic.H2, ctau, wakeGap, theta, wakeStrippedDstar,
                                destination: BoundaryLayerSystemAssembler.GetPooledStationVariablesEngine());
                            var sec = blState.GetOrActivateLegacySecondary(ibl, side);
                            sec.Hs = wakeSec.Hs; sec.Us = wakeSec.Us; sec.Cf = wakeSec.Cf;
                            sec.Di = wakeSec.Di; sec.Cq = wakeSec.Cteq; sec.De = wakeSec.De;
                            sec.Hc = wakeSec.Hc;

                            StoreLegacyCarrySnapshots(
                                blState,
                                ibl,
                                side,
                                CreateLegacyPrimaryStationStateOverride(
                                    blState,
                                    ibl,
                                    side,
                                    tkbl,
                                    qinfbl,
                                    tkbl_ms,
                                    uei,
                                    theta,
                                    wakeStrippedDstar,
                                    true),
                                currentKinematic,
                                sec,
                                traceLabel: "legacy_direct_remarch_direct");
                        }
                    }
                    else
                    {
                        // COM1 trace for station 7/50/85/86 side 2, + wake stn103 call 3
                        
                        // MRCHDU BLSYS trace
                        
                        // Trace BLKIN inputs at side 2 station 4 for parity debugging
                        
                        // Trace ALL inputs at station 16 call 4 iter 1
                        
                        // Dump ALL BLSYS inputs at station 103 call 3 iters 1+6 + secondary hash
                        
                        
                        var localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                            isWake: wake,
                            isTurbOrTran: turb || tran,
                            isTran: tran,
                            isSimi: simi,
                            x1: blState.XSSI[ibm, side],
                            x2: xsi,
                            uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10),
                            uei2: uei,
                            t1: prevTheta,
                            t2: theta,
                            d1: prevDstar,
                            d2: dstar,
                            s1: prevCtau,
                            s2: ctau,
                            dw1: prevWakeGap,
                            dw2: wakeGap,
                            ampl1: prevAmpl,
                            ampl2: ampl,
                            amcrit: ncrit,
                            tkbl, qinfbl, tkbl_ms,
                            hstinv, hstinv_ms,
                            gm1, rstbl, rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization), reybl, reybl_re, reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                                ? blState.LegacyKinematic[ibm, side]
                                : null,
                            station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                                ? blState.LegacySecondary[ibm, side]
                                : null,
                            traceSide: side + 1,
                            traceStation: ibl + 1,
                            traceIteration: iter + 1,
                            tracePhase: "legacy_direct_remarch",
                            // Fortran TRDIF reads XT from COMMON (set by the MRCHDU's
                            // TRCHEK). Pass the full TransitionPointResult so the TRDIF
                            // reuses the same XT instead of recomputing it.
                            transitionPointOverride: (tran && settings.UseLegacyBoundaryLayerInitialization)
                                ? trchekFullPoint : null);
                        residual = localResult.Residual;
                        vs2 = localResult.VS2;
                        // n6h20 SEC trace at IBL=66 mc=10
                        
                        // Compare BLSYS secondary2 vs stale LegacySecondary at last surface station
                        
                        // Trace BLKIN at station 5 side 2 for all MRCHDU calls/iters
                        
                        // Trace Newton iter at key stations
                        
                        // Dump VS2 at s2 stn7 call 2 iter 0 for parity comparison
                        
                        
                        vs2 = localResult.VS2;
                        currentU2 = localResult.U2;
                        currentU2Uei = localResult.U2_UEI;
                        // Fortran BLVAR clamps HK2: MAX(HK2, 1.05) for turbulent/laminar, MAX(HK2, 1.00005) for wake
                        // localResult.HK2 is the UNCLAMPED kinematic HK2.
                        // The characteristic constraint uses the CLAMPED value for HKREF/SENS.
                        hk2Current = localResult.HK2;
                        if (settings.UseLegacyBoundaryLayerInitialization)
                        {
                            double hkClamp = wake ? 1.00005 : 1.05;
                            hk2Current = Math.Max(hk2Current, hkClamp);
                        }
                        hk2T2 = localResult.HK2_T2;
                        hk2D2 = localResult.HK2_D2;
                        hk2U2 = localResult.HK2_U2;

                        // MRCHDU per-station trace for hex comparison
                        

                        if (settings.UseLegacyBoundaryLayerInitialization)
                        {
                            // Fortran BLVAR clamps HK2 before returning to BLSYS.
                            // The kinematic snapshot must also use the clamped value
                            // so downstream stations' COM1 carry uses the correct HK.
                            var kinSnap = localResult.Kinematic2Snapshot;
                            if (kinSnap != null)
                            {
                                double hkClampVal = wake ? 1.00005 : 1.05;
                                if (kinSnap.HK2 < hkClampVal)
                                    kinSnap.HK2 = hkClampVal;
                            }
                            StoreLegacyCarrySnapshots(
                                blState,
                                ibl,
                                side,
                                localResult.Primary2Snapshot,
                                kinSnap,
                                localResult.Secondary2Snapshot,
                                traceLabel: "legacy_direct_remarch");
                            // Trace stored secondary at IBL=91 mc=3 every iter
                            
                        }
                    }

                    if (iter == 0)
                    {
                        ueref = currentU2;
                        hkref = hk2Current;

                        if (ibl < blState.ITRAN[side] && ibl >= oldTransitionStation)
                        {
                            hkref = ComputeLegacySeedBaselineHk(
                                blState,
                                ibm,
                                side,
                                hstinv,
                                settings.UseLegacyBoundaryLayerInitialization);
                        }

                        if (ibl < oldTransitionStation)
                        {
                            if (tran)
                            {
                                blState.CTAU[ibl, side] = LegacyLaminarShearSeed;
                            }

                            if (turb)
                            {
                                blState.CTAU[ibl, side] = blState.CTAU[ibm, side];
                            }

                            if (tran || turb)
                            {
                                // Fortran line 2364: CTI = CTAU(IBL,IS) — no min clamp
                                ctau = blState.CTAU[ibl, side];
                            }
                        }
                    }

                    var matrix = SolverBuffers.Matrix4x4Double;
                    var rhs = SolverBuffers.Vector4Double;
                    Array.Clear(matrix);
                    Array.Clear(rhs);

                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 4; col++)
                        {
                            matrix[row, col] = vs2[row, col];
                        }

                        rhs[row] = residual[row];
                    }
                    
                    

                    if (simi || (wake && ibl == blState.IBLTE[side] + 1))
                    {
                        // Classic MRCHDU keeps the similarity station and the first
                        // wake station on the direct Ue constraint from the first
                        // compressible state seen at this station.
                        matrix[3, 3] = currentU2Uei;
                        rhs[3] = ueref - currentU2;
                    }
                    else
                    {
                        // Parity mode follows MRCHDU's mixed inverse marching:
                        // build a local dUe/dHk response from the current BL block,
                        // then constrain the Newton solve along the quasi-normal
                        // Ue-Hk direction that classic XFoil uses to avoid the
                        // Goldstein/Levy-Lees singular characteristic.
                        var characteristicMatrix = SolverBuffers.Matrix4x4DoubleSecondary;
                        var characteristicRhs = SolverBuffers.Vector4DoubleSecondary;
                        for (int rr = 0; rr < 4; rr++)
                        {
                            for (int cc = 0; cc < 4; cc++)
                            {
                                characteristicMatrix[rr, cc] = matrix[rr, cc];
                            }
                        }
                        Array.Copy(rhs, characteristicRhs, 3);
                        characteristicMatrix[3, 0] = 0;
                        characteristicMatrix[3, 1] = hk2T2;
                        characteristicMatrix[3, 2] = hk2D2;
                        // Fortran xbl.f:2908 VTMP(4,4) = HK2_U2*U2_UEI — REAL multiply.
                        // Using LegacyPrecisionMath.Multiply keeps the characteristic
                        // solve bit-exact with Fortran's float arithmetic; the previous
                        // double multiply diverged 1 ULP on the second Newton iter,
                        // causing SENS/VSREZ drift that cascaded through the whole
                        // wake Newton solve for cases like n6h20.
                        characteristicMatrix[3, 3] = LegacyPrecisionMath.Multiply(
                            hk2U2, currentU2Uei,
                            settings.UseLegacyBoundaryLayerInitialization);
                        characteristicRhs[3] = 1.0;
                        // Dump probe matrix at s2 stn5 call2 iter 5
                        

                        
                        double[] characteristicDelta;
                        try
                        {
                            characteristicDelta = SolveSeedLinearSystem(
                                solver,
                                characteristicMatrix,
                                characteristicRhs);
                        }
                        catch (InvalidOperationException)
                        {
                            
                            break;
                        }

                        // n6h20 SENS debug: trace characteristicDelta[3] and SENS at IBL=66 (C# 0-idx=65) mc=10
                        

                        double senNumerator = LegacyPrecisionMath.Multiply(
                            legacySeedSensitivityWeight,
                            characteristicDelta[3],
                            hkref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        senNew = LegacyPrecisionMath.Divide(
                            senNumerator,
                            Math.Max(ueref, 1e-30),
                            settings.UseLegacyBoundaryLayerInitialization);

                        // Classic MRCHDU's current Fortran source never reaches the
                        // historical sensitivity averaging branch here: the live guard
                        // is `IF(ITBL.LE.25) SENS = SENNEW`, so all active direct-remarch
                        // iterations overwrite SENS with the latest response.
                        sens = senNew;

                        

                        // n6h20 SENS trace: dump SENS after update
                        

                        // Fortran MRCHDU line 1750: VS2(4,1) = 0.
                        matrix[3, 0] = 0;
                        matrix[3, 1] = LegacyPrecisionMath.Multiply(
                            hk2T2,
                            hkref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        matrix[3, 2] = LegacyPrecisionMath.Multiply(
                            hk2D2,
                            hkref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double hk2U2Scaled = LegacyPrecisionMath.Multiply(
                            hk2U2,
                            hkref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double sensOverUeref = LegacyPrecisionMath.Divide(
                            sens,
                            Math.Max(ueref, 1e-30),
                            settings.UseLegacyBoundaryLayerInitialization);
                        double row44Blend = LegacyPrecisionMath.Add(
                            hk2U2Scaled,
                            sensOverUeref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        matrix[3, 3] = LegacyPrecisionMath.Multiply(
                            row44Blend,
                            currentU2Uei,
                            settings.UseLegacyBoundaryLayerInitialization);

                        double hkrefSquared = LegacyPrecisionMath.Square(
                            hkref,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double hkRatioOffset = LegacyPrecisionMath.Subtract(
                            LegacyPrecisionMath.Divide(
                                hk2Current,
                                Math.Max(hkref, 1e-30),
                                settings.UseLegacyBoundaryLayerInitialization),
                            1.0,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double hkConstraint = LegacyPrecisionMath.Multiply(
                            hkrefSquared,
                            hkRatioOffset,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double ueRatioOffset = LegacyPrecisionMath.Subtract(
                            LegacyPrecisionMath.Divide(
                                currentU2,
                                Math.Max(ueref, 1e-30),
                                settings.UseLegacyBoundaryLayerInitialization),
                            1.0,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double ueConstraint = LegacyPrecisionMath.Multiply(
                            sens,
                            ueRatioOffset,
                            settings.UseLegacyBoundaryLayerInitialization);
                        rhs[3] = LegacyPrecisionMath.Subtract(
                            0.0,
                            hkConstraint,
                            settings.UseLegacyBoundaryLayerInitialization);
                        rhs[3] = LegacyPrecisionMath.Subtract(
                            rhs[3],
                            ueConstraint,
                            settings.UseLegacyBoundaryLayerInitialization);
                        // n6h20 VS4 row 4 debug at IBL=66 mc=10
                        
                    }

                    // Hex patches disabled — they were calibrated for a lost code state.
                    // Fix the root cause (BLDIF expression ordering) instead.

                    double[] delta;
                    try
                    {

                        
                        
                        
                        // Pre-GAUSS trace at wake station 83 side 2 for init MRCHDU
                        
                        // Iter 12 station 66 matrix + RHS trace (NACA 0021 α=10 Nc=12 debug)
                        
                        delta = SolveSeedLinearSystem(solver, matrix, rhs);
                        
                        // Guard: if MRCHDU local Newton produces non-finite delta,
                        // abort iteration for this station. Keep previous finite
                        // state instead of propagating NaN to all downstream
                        // stations via UESET's DIJ*MASS accumulation. Matches
                        // Fortran MRCHDU where DMAX=NaN falls through to the
                        // extrapolation branch (IF(DMAX.LE.0.1) is false for NaN).
                        if (!double.IsFinite(delta[0]) || !double.IsFinite(delta[1])
                            || !double.IsFinite(delta[2]) || !double.IsFinite(delta[3]))
                        {
                            // Fortran MRCHDU with NaN DMAX exits Newton loop and
                            // hits IF(DMAX.LE.0.1), which is false for NaN → extrapolate.
                            // Signal this by setting dmax sentinel > 0.1.
                            lastDmaxForExtrapolation = 1.0f;
                            break;
                        }
                        
                        // Post-solve delta at station 93 (all iterations)
                        
                        
                        // Trace delta at wake stn103 call 3 iter 6
                        
                        // Trace delta at s2 stn5+6 call 2
                        
                        

                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    double dmax;
                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        // Fortran MRCHDU DMAX: all REAL (float) operations
                        float fd1 = (float)delta[1], fd2 = (float)delta[2], fd3 = (float)delta[3];
                        float fth = (float)theta, fds = (float)dstar, fue = (float)uei;
                        dmax = MathF.Max(MathF.Abs(fd1 / fth), MathF.Abs(fd2 / fds));
                        dmax = MathF.Max((float)dmax, MathF.Abs(fd3 / fue));
                        if (ibl >= blState.ITRAN[side])
                        {
                            float fd0 = (float)delta[0], fct = (float)ctau;
                            dmax = MathF.Max((float)dmax, MathF.Abs(fd0 / (10.0f * fct)));
                        }
                    }
                    else
                    {
                        dmax = Math.Max(
                            Math.Abs(delta[1] / Math.Max(theta, 1e-30)),
                            Math.Abs(delta[2] / Math.Max(dstar, 1e-30)));
                        dmax = Math.Max(dmax, Math.Abs(delta[3] / Math.Max(uei, 1e-30)));
                        if (ibl >= blState.ITRAN[side])
                        {
                            dmax = Math.Max(dmax, Math.Abs(delta[0] / (10.0 * Math.Max(ctau, 1e-7))));
                        }
                    }

                    

                    double rlx = ComputeLegacySeedRelaxation(dmax, settings.UseLegacyBoundaryLayerInitialization);

                    



                    // n6h20 iter-10 MRCHDU delta trace: dump VSREZ at IBL=66 (C# 0-idx=65) mc=10
                    
                    if (ibl < blState.ITRAN[side])
                    {
                        ampl = Math.Max(LegacyPrecisionMath.AddScaled(ampl, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), 0.0);
                    }
                    else
                    {
                        ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), 1.0e-7), 0.30);
                    }

                    theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                    dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                    uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                    
                    

                    // Watch THET[26,0] during MRCHDU mc=9
                    
                    // SENS trace at wake stations
                    
                    // Stations 84..93: full 4x4 system at iter 1 to find first divergence
                    
                    // Station 92 IS=2 mc=3 iter 1: full system + post-solve delta
                    
                    // Station 93: per-iteration FULL 4x4 system (iter 1, 5-7)
                    
                    // Station 12 side 2 MRCHDU trace at mc=2
                    
                    // Station 3 side 2 MRCHDU trace at mc=1 (init)
                    
                    // Station 27 MRCHDU seed trace at mc=9 (=Fortran oi=8)
                    
                    

                    double msq = 0.0;
                    if (hstinv > 0.0)
                    {
                        double uesq = LegacyPrecisionMath.Multiply(
                            uei,
                            uei,
                            hstinv,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double compressibilityFactor = LegacyPrecisionMath.MultiplySubtract(
                            0.5,
                            uesq,
                            1.0,
                            settings.UseLegacyBoundaryLayerInitialization);
                        double denominator = LegacyPrecisionMath.Multiply(
                            gm1,
                            compressibilityFactor,
                            settings.UseLegacyBoundaryLayerInitialization);
                        msq = LegacyPrecisionMath.Divide(
                            uesq,
                            denominator,
                            settings.UseLegacyBoundaryLayerInitialization);
                    }

                    double dsw = LegacyPrecisionMath.Subtract(
                        dstar,
                        wakeGap,
                        settings.UseLegacyBoundaryLayerInitialization);
                    dsw = ApplySeedDslim(dsw, theta, msq, hklim, settings.UseLegacyBoundaryLayerInitialization);
                    dstar = LegacyPrecisionMath.Add(
                        dsw,
                        wakeGap,
                        settings.UseLegacyBoundaryLayerInitialization);

                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        // Classic MRCHDU reuses the accepted direct-remarch
                        // state through REAL work arrays before the next local
                        // BLSYS / transition probe iteration. Leaving theta /
                        // dstar / uei wide here keeps the next station-15 live
                        // handoff packet one ULP above the focused replay even
                        // when the isolated transition window is already green.
                        if (ibl < blState.ITRAN[side])
                        {
                            ApplyLegacySeedPrecision(
                                settings.UseLegacyBoundaryLayerInitialization,
                                ref uei,
                                ref theta,
                                ref dstar,
                                ref ampl);
                        }
                        else
                        {
                            ApplyLegacySeedPrecision(
                                settings.UseLegacyBoundaryLayerInitialization,
                                ref uei,
                                ref theta,
                                ref dstar,
                                ref ctau);
                        }
                    }

                    

                    // Trace MRCHDU Newton at transition station 5 side 2 (C# ibl=4 side=1)
                    
                    // Trace MRCHDU Newton at station 98 side 2 (C# ibl=97 side=1)
                    
                    // Trace MRCHDU Newton at wake stations 82-84 (init call)
                    
                    // Fortran: IF(DMAX.LE.DEPS) — both REAL (float) comparison
                    lastDmaxForExtrapolation = (float)dmax;
                    
                    if ((float)dmax <= (float)legacySeedTolerance)
                    {
                        converged = true;
                        break;
                    }
                }

                
                // Fortran MRCHDU lines 1934-1957: handle non-convergence
                // If DMAX <= 0.1: keep the last iterate (reasonable solution)
                // If DMAX > 0.1 and IBL > 3: extrapolate from upstream values
                if (!converged && settings.UseLegacyBoundaryLayerInitialization)
                {
                    float dmaxF = lastDmaxForExtrapolation;
                    
                    if (dmaxF > 0.1f && ibl > 2)  // Fortran IBL>3 → C# ibl>2
                    {
                        if (ibl <= blState.IBLTE[side])
                        {
                            // Surface station: theta,dstar ~ sqrt(x) extrapolation
                            // Fortran: THI = THET(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
                            // The Fortran ** operator with REAL exponent goes
                            // through libm powf in -O0; matching with MathF.Sqrt
                            // can drift one ULP, so route through the same powf
                            // bridge that other parity-sensitive paths use.
                            float xRatioF = (float)blState.XSSI[ibl, side] / (float)blState.XSSI[ibm, side];
                            float sqrtRatioF = LegacyLibm.Pow(xRatioF, 0.5f);
                            theta = (float)blState.THET[ibm, side] * sqrtRatioF;
                            dstar = (float)blState.DSTR[ibm, side] * sqrtRatioF;
                            uei = blState.UEDG[ibm, side];
                            
                        }
                        else if (ibl == blState.IBLTE[side] + 1)
                        {
                            // TE junction: use TE merged values
                            // Fortran: all REAL (float) additions
                            float tte = (float)blState.THET[blState.IBLTE[0], 0] + (float)blState.THET[blState.IBLTE[1], 1];
                            float dte = (float)blState.DSTR[blState.IBLTE[0], 0] + (float)blState.DSTR[blState.IBLTE[1], 1]
                                        + (float)GetWakeGap(wakeSeed, teGap, 0);
                            theta = tte;
                            dstar = dte;
                            uei = blState.UEDG[ibm, side];
                        }
                        else
                        {
                            // Wake: linear extrapolation
                            // Fortran: RATLEN = (XSSI-XSSI)/(10.0*DSTR), DSI = (DSTR+THI*RATLEN)/(1.0+RATLEN)
                            // All REAL (float) operations.
                            theta = blState.THET[ibm, side];
                            float ratlenF = ((float)blState.XSSI[ibl, side] - (float)blState.XSSI[ibm, side])
                                          / (10.0f * (float)blState.DSTR[ibm, side]);
                            dstar = ((float)blState.DSTR[ibm, side] + (float)theta * ratlenF) / (1.0f + ratlenF);
                            uei = blState.UEDG[ibm, side];
                        }
                        if (ibl == blState.ITRAN[side]) ctau = 0.05;
                        if (ibl > blState.ITRAN[side]) ctau = blState.CTAU[ibm, side];
                    }
                }

                // Fortran MRCHDU non-convergence path (label 109, lines 1959-1977):
                // BLPRV/BLKIN always run, TRCHEK only if !SIMI && !TURB,
                // BLVAR/BLMID always run (with ityp depending on laminar/turb/wake).
                // The secondary refresh must happen for ALL non-converged stations,
                // not just laminar ones.
                
                if (!simi && !converged)
                {
                    // Fortran: TRCHEK only when !SIMI && !TURB (line 1963)
                    if (!turb)
                    {
                        double x1 = blState.XSSI[ibm, side];
                        double ue1 = Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10);
                        double theta1 = Math.Max(blState.THET[ibm, side], 1e-10);
                        double dstar1 = Math.Max(blState.DSTR[ibm, side], 1e-10);
                        var transitionKinematic1 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            ue1,
                            theta1,
                            dstar1,
                            0.0,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchA());
                        double rt1 = transitionKinematic1.RT2;
                        double hk1 = Math.Max(transitionKinematic1.HK2, 1.05);

                        var transitionKinematic2 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            uei,
                            theta,
                            dstar,
                            0.0,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchB());
                        double rt2 = transitionKinematic2.RT2;
                        double hk2 = Math.Max(transitionKinematic2.HK2, 1.05);

                        var transition = settings.UseLegacyBoundaryLayerInitialization
                            ? TransitionModel.CheckTransitionExact(
                                x1,
                                xsi,
                                Math.Max(prevAmpl, 0.0),
                                Math.Max(ampl, 0.0),
                                ncrit,
                                ue1,
                                uei,
                                theta1,
                                theta,
                                dstar1,
                                dstar,
                                hstinv,
                                hstinv_ms,
                                gm1,
                                rstbl,
                                rstbl_ms,
                                GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                                reybl,
                                reybl_re,
                                reybl_ms,
                                settings.UseModernTransitionCorrections,
                                forcedTransitionXi,
                                settings.UseLegacyBoundaryLayerInitialization,
                                out _,
                                traceSide: side + 1,
                                traceStation: ibl + 1,
                                traceIteration: 1,
                                tracePhase: "legacy_direct_remarch")
                            : TransitionModel.CheckTransition(
                                x1,
                                xsi,
                                Math.Max(prevAmpl, 0.0),
                                Math.Max(ampl, 0.0),
                                ncrit,
                                hk1,
                                theta1,
                                rt1,
                                ue1,
                                dstar1,
                                hk2,
                                theta,
                                rt2,
                                uei,
                                dstar,
                                settings.UseModernTransitionCorrections,
                                forcedTransitionXi,
                                settings.UseLegacyBoundaryLayerInitialization);

                        // Fortran: AMI = AMPL2 — always uses the downstream N
                        ampl = transition.TransitionOccurred
                            ? Math.Max(transition.DownstreamAmplification, 0.0)
                            : Math.Max(transition.DownstreamAmplification, 0.0);
                        // Match the Fortran station-local TRAN flag: each
                        // TRCHEK call owns the current iteration's transition
                        // state, so later iterations may clear an earlier hit.
                        tran = transition.TransitionOccurred;
                        if (tran)
                        {
                            blState.ITRAN[side] = ibl;
                        }
                        else
                        {
                            blState.ITRAN[side] = Math.Min(ibl + 2, blState.NBL[side]);
                        }
                        

                    }

                    // Fortran: BLVAR/BLMID always run for non-converged stations
                    // (lines 1971-1977), regardless of turb/wake status.
                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        var localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                            isWake: wake,
                            isTurbOrTran: turb || tran,
                            isTran: tran,
                            isSimi: false,
                            x1: blState.XSSI[ibm, side],
                            x2: xsi,
                            uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10),
                            uei2: uei,
                            t1: prevTheta,
                            t2: theta,
                            d1: prevDstar,
                            d2: dstar,
                            s1: prevCtau,
                            s2: ctau,
                            dw1: prevWakeGap,
                            dw2: wakeGap,
                            ampl1: prevAmpl,
                            ampl2: ampl,
                            amcrit: ncrit,
                            tkbl,
                            qinfbl,
                            tkbl_ms,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: true,
                            station1KinematicOverride: blState.LegacyKinematic[ibm, side],
                            station1SecondaryOverride: blState.LegacySecondary[ibm, side],
                            traceSide: side + 1,
                            traceStation: ibl + 1,
                            tracePhase: "legacy_seed_postcheck",
                            // Fortran MRCHDU label-109 calls BLVAR(2) before
                            // BLVAR(3) for wake stations because the IF blocks
                            // are not chained. BLVAR(2)'s 1.05 clamp on COM2.HK2
                            // sticks because BLVAR(3)'s 1.00005 clamp is then a
                            // no-op, leaving the wake secondary built from the
                            // elevated HK. Mirror that quirk only for the
                            // non-converged wake refresh path.
                            extraTurbHkClamp: wake);
                        StoreLegacyCarrySnapshots(
                            blState,
                            ibl,
                            side,
                            localResult.Primary2Snapshot,
                            localResult.Kinematic2Snapshot,
                            localResult.Secondary2Snapshot,
                            traceLabel: "legacy_seed_postcheck");
                        
                    }
                }


                // MRCHDU final-store trace
                
                blState.THET[ibl, side] = theta;
                blState.DSTR[ibl, side] = dstar;
                blState.UEDG[ibl, side] = uei;
                // Trace MRCHDU output for calls 2-4 (both sides)
                
                // n6h20 trace: per-station MRCHDU output incl CTAU at call 10 side 1
                
                
                
                // The accepted laminar CTAU word must match the amplification word
                // that the next station and the later SETBL rebuild consume. The
                // focused lower-side station-3 trace shows the managed march was
                // carrying the final promoted amplification into station 4 while
                // reloading an older TRCHEK scratch from CTAU during SETBL.
                // Persist the accepted carried amplification so both consumers see
                // the same legacy word.
                blState.CTAU[ibl, side] = (ibl < blState.ITRAN[side])
                    ? LegacyPrecisionMath.RoundToSingle(ampl, settings.UseLegacyBoundaryLayerInitialization)
                    : LegacyPrecisionMath.RoundToSingle(ctau, settings.UseLegacyBoundaryLayerInitialization);
                
                WriteLegacyAmplificationCarry(blState, ibl, side, ampl);
                // Fortran carries COM2.AMPL2 (= Newton-updated) to the next station
                // via the COM2→COM1 shift. Carry the post-Newton ampl forward.
                mrchduCarriedAmpl = ampl;
                double massVal = LegacyPrecisionMath.Multiply(
                    dstar,
                    uei,
                    settings.UseLegacyBoundaryLayerInitialization);
                
                blState.MASS[ibl, side] = massVal;
                // Temporary wake MASS parity trace
                

                RefreshLegacyKinematicSnapshot(
                    blState,
                    side,
                    ibl,
                    uei,
                    theta,
                    dstar,
                    settings,
                    tkbl,
                    qinfbl,
                    tkbl_ms,
                    hstinv,
                    hstinv_ms,
                    rstbl,
                    rstbl_ms,
                    reybl,
                    reybl_re,
                    reybl_ms,
                    wake ? wakeGap : 0.0);

                // Fortran TESYS calls BLVAR(3) after BLPRV+BLKIN have set COM2 from
                // the current iterate. So BLVAR(3) uses the TE junction's own
                // kinematic, matching the C# currentKinematic path above.


                sens = senNew;
                
                if (tran || ibl == blState.IBLTE[side])
                {
                    turb = true;
                }
            }

        }

        SyncWakeMirror(blState);

        

        

        

        // Post-MRCHDU hash trace for parity debugging
        
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE/MRCHDU wake continuation
    // Difference from legacy: The same wake direct/inverse refinement logic is preserved, but the managed code isolates it into a dedicated helper instead of folding it into the airfoil remarch.
    // Decision: Keep the dedicated helper and preserve the legacy wake constraint switching.
    private static void RefineLegacyWakeSeedDirect(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double teGap,
        WakeSeedData? wakeSeed,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);

        const int side = 1;
        const int maxIterations = 25;
        const double legacySeedTolerance = 1.0e-5;
        const double wakeHkLimit = 2.5;

        if (blState.NBL[side] <= blState.IBLTE[side] + 1)
        {
            return;
        }

        var solver = DenseLinearSystemSolver.Shared;

        for (int ibl = blState.IBLTE[side] + 1; ibl < blState.NBL[side]; ibl++)
        {
            int ibm = ibl - 1;
            bool firstWake = ibl == blState.IBLTE[side] + 1;

            double xsi = blState.XSSI[ibl, side];
            double xPrev = blState.XSSI[ibm, side];
            double uei = Math.Max(Math.Abs(blState.UEDG[ibl, side]), 1e-10);
            double theta = Math.Max(blState.THET[ibl, side], 1e-10);
            double dstar = Math.Max(blState.DSTR[ibl, side], 1e-10);
            double ctau = Math.Max(blState.CTAU[ibl, side], LegacyLaminarShearSeed);
            double wakeGap = GetWakeGap(wakeSeed, teGap, ibl - blState.IBLTE[side]);
            bool directMode = true;
            double inverseTargetHk = 0.0;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                double prevTheta;
                double prevDstar;
                double prevCtau;
                double prevWakeGap;

                if (firstWake)
                {
                    // Fortran line 354: DTE uses raw ANTE, not WGAP(1).
                    double anteForMerge = GetFirstWakeStationTeGap(wakeSeed, teGap);
                    ComputeLegacyWakeTeMergeState(
                        blState,
                        anteForMerge,
                        settings.UseLegacyBoundaryLayerInitialization,
                        out prevTheta,
                        out prevDstar,
                        out prevCtau);
                    prevTheta = Math.Max(prevTheta, 1e-10);
                    prevDstar = Math.Max(prevDstar, 1.00005 * prevTheta);
                    prevCtau = Math.Max(prevCtau, LegacyLaminarShearSeed);
                    // prevWakeGap is DSWAKI for the previous station; that's WGAP(1).
                    prevWakeGap = GetWakeGap(wakeSeed, teGap, 1);
                }
                else
                {
                    prevTheta = Math.Max(blState.THET[ibm, side], 1e-10);
                    prevDstar = Math.Max(blState.DSTR[ibm, side], 1.00005 * prevTheta);
                    prevCtau = Math.Max(blState.CTAU[ibm, side], 1.0e-7);
                    prevWakeGap = GetWakeGap(wakeSeed, teGap, ibm - blState.IBLTE[side]);
                }

                double[] residual;
                double[,] vs2;
                double hk2 = 0.0;
                double hk2T2 = 0.0;
                double hk2D2 = 0.0;
                double hk2U2 = 0.0;
                double u2Uei = 1.0;

                if (firstWake)
                {
                    double wakeStrippedDstar = LegacyPrecisionMath.Subtract(
                        dstar,
                        wakeGap,
                        settings.UseLegacyBoundaryLayerInitialization);
                    var teResult = BoundaryLayerSystemAssembler.AssembleTESystem(
                        prevCtau,
                        prevTheta,
                        prevDstar,
                        hk2: 0.0,
                        rt2: 0.0,
                        msq2: 0.0,
                        h2: 0.0,
                        s2: ctau,
                        t2: theta,
                        // TESYS consumes the wake-stripped D2 state; DSI's WGAP
                        // portion travels in DW2 just like the Fortran BLPRV path.
                        d2: wakeStrippedDstar,
                        dw2: wakeGap,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                    residual = teResult.Residual;
                    vs2 = teResult.VS2;
                }
                else
                {
                    var localResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
                        isWake: true,
                        isTurbOrTran: true,
                        isTran: false,
                        isSimi: false,
                        x1: xPrev,
                        x2: xsi,
                        uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10),
                        uei2: uei,
                        t1: prevTheta,
                        t2: theta,
                        d1: prevDstar,
                        d2: dstar,
                        s1: prevCtau,
                        s2: ctau,
                        dw1: prevWakeGap,
                        dw2: wakeGap,
                        ampl1: 0.0,
                        ampl2: 0.0,
                        amcrit: settings.CriticalAmplificationFactor,
                        tkbl, qinfbl, tkbl_ms,
                        hstinv, hstinv_ms,
                        gm1, rstbl, rstbl_ms,
                        GetHvRat(settings.UseLegacyBoundaryLayerInitialization), reybl, reybl_re, reybl_ms,
                        useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                        station1KinematicOverride: settings.UseLegacyBoundaryLayerInitialization
                            ? blState.LegacyKinematic[ibm, side]
                            : null,
                        station1SecondaryOverride: settings.UseLegacyBoundaryLayerInitialization
                            ? blState.LegacySecondary[ibm, side]
                            : null,
                        traceSide: side + 1,
                        traceStation: ibl + 1,
                        traceIteration: iter + 1,
                        tracePhase: "legacy_wake_seed");
                    residual = localResult.Residual;
                    vs2 = localResult.VS2;
                    hk2 = localResult.HK2;
                    hk2T2 = localResult.HK2_T2;
                    hk2D2 = localResult.HK2_D2;
                    hk2U2 = localResult.HK2_U2;
                    u2Uei = localResult.U2_UEI;

                    if (settings.UseLegacyBoundaryLayerInitialization)
                    {
                        StoreLegacyCarrySnapshots(
                            blState,
                            ibl,
                            side,
                            localResult.Primary2Snapshot,
                            localResult.Kinematic2Snapshot,
                            localResult.Secondary2Snapshot,
                            traceLabel: "legacy_wake_seed");
                    }
                }

                var matrix = SolverBuffers.Matrix4x4Double;
                var rhs = SolverBuffers.Vector4Double;
                Array.Clear(matrix);
                Array.Clear(rhs);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        matrix[row, col] = vs2[row, col];
                    }

                    rhs[row] = residual[row];
                }

                if (directMode)
                {
                    // MRCHUE keeps the wake on prescribed Ue until the local Hk
                    // test forces the inverse branch.
                    matrix[3, 3] = 1.0;
                    rhs[3] = 0.0;
                }
                else
                {
                    // Fortran MRCHUE inverse: VS2(4,4) = HK2_U2 (no U2_UEI factor)
                    matrix[3, 1] = hk2T2;
                    matrix[3, 2] = hk2D2;
                    matrix[3, 3] = hk2U2;
                    rhs[3] = inverseTargetHk - hk2;
                }

                double[] delta;
                try
                {
                    delta = SolveSeedLinearSystem(solver, matrix, rhs);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                double dmax = Math.Max(
                    Math.Abs(delta[1] / Math.Max(theta, 1e-30)),
                    Math.Abs(delta[2] / Math.Max(dstar, 1e-30)));
                dmax = Math.Max(dmax, Math.Abs(delta[0] / Math.Max(ctau, 1.0e-7)));
                if (!directMode)
                {
                    dmax = Math.Max(dmax, Math.Abs(delta[3] / Math.Max(uei, 1e-30)));
                }

                double rlx = ComputeLegacySeedRelaxation(dmax, settings.UseLegacyBoundaryLayerInitialization);

                if (directMode && !firstWake)
                {
                    double msqTest = 0.0;
                    if (hstinv > 0.0)
                    {
                        double uesq = uei * uei * hstinv;
                        msqTest = uesq / (gm1 * (1.0 - 0.5 * uesq));
                    }

                    double predictedDstar = LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization);
                    double predictedTheta = LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization);
                    double htest = predictedDstar / Math.Max(predictedTheta, 1e-30);
                    double hkTest = BoundaryLayerCorrelations.KinematicShapeParameter(
                        htest,
                        msqTest,
                        settings.UseLegacyBoundaryLayerInitialization).Hk;
                    if (hkTest >= wakeHkLimit)
                    {
                        var (u1, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
                            Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1e-10),
                            tkbl,
                            qinfbl,
                            tkbl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization);
                        // BLKIN expects the wake displacement without the WGAP
                        // contribution; the total DSTR is carried separately in
                        // the seed march state.
                        var kinematic1 = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                            u1,
                            prevTheta,
                            prevDstar - prevWakeGap,
                            prevWakeGap,
                            hstinv,
                            hstinv_ms,
                            gm1,
                            rstbl,
                            rstbl_ms,
                            GetHvRat(settings.UseLegacyBoundaryLayerInitialization),
                            reybl,
                            reybl_re,
                            reybl_ms,
                            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
                            destination: GetEngineKinematicScratchA());

                        double konst = 0.03 * (xsi - xPrev) / Math.Max(prevTheta, 1e-30);
                        inverseTargetHk = kinematic1.HK2;
                        for (int newton = 0; newton < 3; newton++)
                        {
                            double hm1 = inverseTargetHk - 1.0;
                            double denom = 1.0 + (3.0 * konst * hm1 * hm1);
                            inverseTargetHk -= (inverseTargetHk + (konst * hm1 * hm1 * hm1) - kinematic1.HK2) / denom;
                        }

                        inverseTargetHk = settings.UseLegacyBoundaryLayerInitialization
                            ? MathF.Max((float)inverseTargetHk, 1.01f)
                            : Math.Max(inverseTargetHk, 1.01);
                        directMode = false;
                        continue;
                    }
                }

                ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], settings.UseLegacyBoundaryLayerInitialization), 1.0e-7), 0.30);
                theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                if (!directMode)
                {
                    uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], settings.UseLegacyBoundaryLayerInitialization), 1.0e-10);
                }

                double msq = 0.0;
                if (hstinv > 0.0)
                {
                    double uesq = LegacyPrecisionMath.Multiply(
                        uei,
                        uei,
                        hstinv,
                        settings.UseLegacyBoundaryLayerInitialization);
                    double compressibilityFactor = LegacyPrecisionMath.MultiplySubtract(
                        0.5,
                        uesq,
                        1.0,
                        settings.UseLegacyBoundaryLayerInitialization);
                    double denominator = LegacyPrecisionMath.Multiply(
                        gm1,
                        compressibilityFactor,
                        settings.UseLegacyBoundaryLayerInitialization);
                    msq = LegacyPrecisionMath.Divide(
                        uesq,
                        denominator,
                        settings.UseLegacyBoundaryLayerInitialization);
                }

                double dsw = LegacyPrecisionMath.Subtract(
                    dstar,
                    wakeGap,
                    settings.UseLegacyBoundaryLayerInitialization);
                dsw = ApplySeedDslim(dsw, theta, msq, 1.00005, settings.UseLegacyBoundaryLayerInitialization);
                dstar = LegacyPrecisionMath.Add(
                    dsw,
                    wakeGap,
                    settings.UseLegacyBoundaryLayerInitialization);


                // Fortran xbl.f:3053: IF(DMAX.LE.DEPS) with REAL*4 DMAX and DEPS=5.0E-6.
                // Must use float comparison to match exactly, not double.
                if ((float)dmax <= (float)legacySeedTolerance)
                {
                    break;
                }
            }

            blState.THET[ibl, side] = theta;
            blState.DSTR[ibl, side] = dstar;
            blState.UEDG[ibl, side] = uei;
            blState.CTAU[ibl, side] = LegacyPrecisionMath.RoundToSingle(ctau, settings.UseLegacyBoundaryLayerInitialization);
            blState.MASS[ibl, side] = LegacyPrecisionMath.Multiply(
                dstar,
                uei,
                settings.UseLegacyBoundaryLayerInitialization);

        }

        SyncWakeMirror(blState);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE similarity-station BLSYS call
    // Difference from legacy: This helper packages the fixed laminar shear seed and forwards to the shared `BLSYS` port instead of keeping a duplicated inline call sequence.
    // Decision: Keep the helper and preserve the original station inputs.
    private static BlsysResult AssembleSimilarityStation(
        double xsi,
        double uei,
        double theta,
        double dstar,
        double ampl,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        int? traceSide = null,
        int? traceStation = null,
        int? traceIteration = null,
        string? tracePhase = null)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        return BoundaryLayerSystemAssembler.AssembleStationSystem(
            isWake: false,
            isTurbOrTran: false,
            isTran: false,
            isSimi: true,
            x1: xsi,
            x2: xsi,
            uei1: uei,
            uei2: uei,
            t1: theta,
            t2: theta,
            d1: dstar,
            d2: dstar,
            // BLPRV keeps the laminar amplification state and the shear seed in
            // separate slots. Classic MRCHUE leaves CTI at 0.03 before transition,
            // even at the similarity station, so the parity wrappers cannot reuse
            // the amplification variable for the S slot.
            s1: LegacyLaminarShearSeed,
            s2: LegacyLaminarShearSeed,
            dw1: 0.0,
            dw2: 0.0,
            ampl1: ampl,
            ampl2: ampl,
            amcrit: settings.CriticalAmplificationFactor,
            tkbl, qinfbl, tkbl_ms,
            hstinv, hstinv_ms,
            gm1, rstbl, rstbl_ms,
            GetHvRat(settings.UseLegacyBoundaryLayerInitialization), reybl, reybl_re, reybl_ms,
            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
            traceSide: traceSide,
            traceStation: traceStation,
            traceIteration: traceIteration,
            tracePhase: tracePhase);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE laminar-station BLSYS call
    // Difference from legacy: This helper centralizes the laminar station call pattern and threads the carried COM1 snapshots explicitly in parity mode.
    // Decision: Keep the helper and preserve the original laminar station inputs and parity overrides.
    private static BlsysResult AssembleLaminarStation(
        double x1,
        double x2,
        double uei1,
        double uei2,
        double theta1,
        double theta2,
        double dstar1,
        double dstar2,
        double ampl1,
        double ampl2,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        KinematicResult? station1KinematicOverride = null,
        SecondaryStationResult? station1SecondaryOverride = null,
        KinematicResult? station2KinematicOverride = null,
        PrimaryStationState? station2PrimaryOverride = null,
        SecondaryStationResult? station2SecondaryOverride = null,
        int? traceSide = null,
        int? traceStation = null,
        int? traceIteration = null,
        string? tracePhase = null)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(settings.UseLegacyBoundaryLayerInitialization);
        return BoundaryLayerSystemAssembler.AssembleStationSystem(
            isWake: false,
            isTurbOrTran: false,
            isTran: false,
            isSimi: false,
            x1,
            x2,
            uei1,
            uei2,
            theta1,
            theta2,
            d1: dstar1,
            d2: dstar2,
            // Laminar MRCHUE stations still carry the separate CTI seed through BLPRV.
            // The amplification equation uses ampl1/ampl2, but the S slot remains 0.03
            // until the station enters the transition/turbulent branches.
            s1: LegacyLaminarShearSeed,
            s2: LegacyLaminarShearSeed,
            dw1: 0.0,
            dw2: 0.0,
            ampl1: ampl1,
            ampl2: ampl2,
            amcrit: settings.CriticalAmplificationFactor,
            tkbl, qinfbl, tkbl_ms,
            hstinv, hstinv_ms,
            gm1, rstbl, rstbl_ms,
            GetHvRat(settings.UseLegacyBoundaryLayerInitialization), reybl, reybl_re, reybl_ms,
            useLegacyPrecision: settings.UseLegacyBoundaryLayerInitialization,
            // Parity mode must thread the carried COM1 BLKIN/BLVAR snapshot
            // through helper wrappers too; otherwise the wrapper silently
            // reintroduces the same input drift the direct call sites avoided.
            station1KinematicOverride: station1KinematicOverride,
            station1SecondaryOverride: station1SecondaryOverride,
            station2KinematicOverride: station2KinematicOverride,
            station2PrimaryOverride: station2PrimaryOverride,
            station2SecondaryOverride: station2SecondaryOverride,
            traceSide: traceSide,
            traceStation: traceStation,
            traceIteration: traceIteration,
            tracePhase: tracePhase);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE/TRCHEK2 live COM2 carry
    // Difference from legacy: The managed solver stores the live downstream
    // primary packet in named arrays and rebuilds the compressible U value on
    // demand instead of reading the COMMON scratch directly.
    // Decision: Keep the helper so parity mode can thread the exact carried
    // station-2 primary state through localized transition probes without
    // obscuring the default managed path.
    private static PrimaryStationState? ResolveLegacyPrimaryStationStateOverride(
        BoundaryLayerSystemState blState,
        int ibl,
        int side,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double uei,
        double theta,
        double dstar,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path always returns the legacy primary.
        var live = blState.LegacyPrimary[ibl, side];
        if (live is not null)
        {
            return CopyLegacyPrimaryIntoResolvedScratch(live);
        }
        return CreateLegacyPrimaryStationStateOverride(
            blState,
            ibl,
            side,
            tkbl,
            qinfbl,
            tkbl_ms,
            uei,
            theta,
            dstar,
            useLegacyPrecision);
    }

    private static PrimaryStationState? CreateLegacyPrimaryStationStateOverride(
        BoundaryLayerSystemState blState,
        int ibl,
        int side,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double uei,
        double theta,
        double dstar,
        bool useLegacyPrecision = false)
    {
        // Phase 1 strip: float-only path always builds the legacy carry state.
        double carriedUei = Math.Max(Math.Abs(uei), 1.0e-10);
        var (carriedU, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            carriedUei,
            tkbl,
            qinfbl,
            tkbl_ms);

        return new PrimaryStationState
        {
            U = carriedU,
            T = LegacyPrecisionMath.RoundToSingle(theta, true),
            D = LegacyPrecisionMath.RoundToSingle(dstar, true)
        };
    }

    private static void StoreLegacyCarrySnapshots(
        BoundaryLayerSystemState blState,
        int ibl,
        int side,
        PrimaryStationState? primary,
        KinematicResult? kinematic,
        SecondaryStationResult? secondary,
        string? traceLabel = null)
    {
        if (!string.IsNullOrEmpty(traceLabel))
        {
        }

        blState.SetLegacyPrimary(ibl, side, primary);
        blState.SetLegacyKinematic(ibl, side, kinematic);
        blState.SetLegacySecondary(ibl, side, secondary);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE/MRCHDU downstream BLKIN refresh
    // Difference from legacy: The managed helper refreshes only the BLKIN-like snapshot explicitly while leaving the secondary BLVAR snapshot on the last assembled legacy state.
    // Decision: Keep the helper because it makes the stale-state legacy behavior explicit and testable.
    private static void RefreshLegacyAcceptedStationSnapshot(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        double uei,
        double theta,
        double dstar,
        double ctau,
        double ampl,
        bool wake,
        bool turb,
        bool tran,
        bool simi,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        double wakeGap = 0.0,
        double prevWakeGap = 0.0,
        SecondaryStationResult? preservedSecondary = null)
    {
        if (!settings.UseLegacyBoundaryLayerInitialization)
        {
            return;
        }

        int ibm = ibl - 1;
        double gm1 = LegacyPrecisionMath.GammaMinusOne(true);
        double prevTheta = Math.Max(blState.THET[ibm, side], 1.0e-10);
        double prevDstar = Math.Max(blState.DSTR[ibm, side], 1.0e-10);
        double prevStoredShear = blState.CTAU[ibm, side];
        double prevCtau = Math.Max(prevStoredShear, 1.0e-7);
        double prevAmpl = ReadLegacyAmplificationCarry(blState, ibm, side);

        var acceptedResult = BoundaryLayerSystemAssembler.AssembleStationSystem(
            isWake: wake,
            isTurbOrTran: turb || tran,
            isTran: tran,
            isSimi: simi,
            x1: blState.XSSI[ibm, side],
            x2: blState.XSSI[ibl, side],
            uei1: Math.Max(Math.Abs(blState.UEDG[ibm, side]), 1.0e-10),
            uei2: Math.Max(Math.Abs(uei), 1.0e-10),
            t1: prevTheta,
            t2: theta,
            d1: prevDstar,
            d2: dstar,
            s1: prevCtau,
            s2: ctau,
            dw1: prevWakeGap,
            dw2: wakeGap,
            ampl1: prevAmpl,
            ampl2: ampl,
            amcrit: settings.GetEffectiveNCrit(side),
            tkbl,
            qinfbl,
            tkbl_ms,
            hstinv,
            hstinv_ms,
            gm1,
            rstbl,
            rstbl_ms,
            GetHvRat(true),
            reybl,
            reybl_re,
            reybl_ms,
            useLegacyPrecision: true,
            station1KinematicOverride: blState.LegacyKinematic[ibm, side],
            station1SecondaryOverride: blState.LegacySecondary[ibm, side]);

        // Preserve MRCHUE COM carry values (PreUpdateT/PreUpdateD) across the
        // refresh. Capture the pre-update fields as local copies BEFORE the
        // SetLegacyPrimary call — the pooled storage slot is overwritten in
        // place by CopyFrom, so any reference into it would see the new data
        // after the assignment.
        var oldPrimary = blState.LegacyPrimary[ibl, side];
        double? savedPreUpdateT = oldPrimary?.PreUpdateT;
        double? savedPreUpdateD = oldPrimary?.PreUpdateD;
        double? savedPreUpdateDFull = oldPrimary?.PreUpdateDFull;
        bool hadSavedPreUpdate = savedPreUpdateT.HasValue;

        var sourcePrimary = acceptedResult.Primary2Snapshot
            ?? CreateLegacyPrimaryStationStateOverride(
                blState,
                ibl,
                side,
                tkbl,
                qinfbl,
                tkbl_ms,
                uei,
                theta,
                dstar);
        blState.SetLegacyPrimary(ibl, side, sourcePrimary);
        if (hadSavedPreUpdate && blState.LegacyPrimary[ibl, side] is { } newPrimary)
        {
            newPrimary.PreUpdateT = savedPreUpdateT;
            newPrimary.PreUpdateD = savedPreUpdateD;
            newPrimary.PreUpdateDFull = savedPreUpdateDFull;
        }
        blState.SetLegacyKinematic(ibl, side, acceptedResult.Kinematic2Snapshot);
        blState.SetLegacySecondary(ibl, side, preservedSecondary ?? acceptedResult.Secondary2Snapshot);
    }

    private static void RefreshLegacyKinematicSnapshot(
        BoundaryLayerSystemState blState,
        int side,
        int ibl,
        double uei,
        double theta,
        double dstar,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        double wakeGap = 0.0)
    {
        if (!settings.UseLegacyBoundaryLayerInitialization)
        {
            return;
        }

        double gm1 = LegacyPrecisionMath.GammaMinusOne(true);
        double dForSystem = LegacyPrecisionMath.Subtract(
            dstar,
            wakeGap,
            useLegacyPrecision: true);
        var (u2, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            uei,
            tkbl,
            qinfbl,
            tkbl_ms);
        var kinematic = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
            u2,
            theta,
            dForSystem,
            wakeGap,
            hstinv,
            hstinv_ms,
            gm1,
            rstbl,
            rstbl_ms,
            GetHvRat(true),
            reybl,
            reybl_re,
            reybl_ms,
            useLegacyPrecision: true,
            destination: GetEngineKinematicScratchA());
        var primary = CreateLegacyPrimaryStationStateOverride(
            blState,
            ibl,
            side,
            tkbl,
            qinfbl,
            tkbl_ms,
            uei,
            theta,
            dForSystem);

        blState.SetLegacyPrimary(ibl, side, primary);
        blState.SetLegacyKinematic(ibl, side, kinematic);
    }


    // Legacy mapping: legacy-derived from the classic REAL seed solves
    // Difference from legacy: The solve is delegated to the shared dense solver, with a parity-only float cast path that reproduces the single-precision seed solves explicitly.
    // Decision: Keep the shared helper and preserve the float solve path for parity mode.
    private static double[] SolveSeedLinearSystem(
        DenseLinearSystemSolver solver,
        double[,] matrix,
        double[] rhs,
        bool useLegacyPrecision = false)
    {
        int n = rhs.Length;
        // Phase 1 strip: float-only path follows classic XFoil's single-precision solves.
        // ThreadStatic scratch buffers eliminate the three per-call allocations
        // (singleMatrix, singleRhs, singleDelta) that the 4x4 Newton hot path
        // otherwise triggers on every station/iteration.
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var singleMatrix = SolverBuffers.DenseScratchMatrixFloat(rows, cols);
        var singleRhs = SolverBuffers.DenseScratchVectorFloat(n);
        for (int row = 0; row < rows; row++)
        {
            singleRhs[row] = (float)rhs[row];
            for (int col = 0; col < cols; col++)
            {
                singleMatrix[row, col] = (float)matrix[row, col];
            }
        }

        solver.SolveInPlace(singleMatrix, singleRhs);

        // Copy the float solution back into the caller's rhs buffer; the
        // caller treats rhs as the returned delta, so no new array is needed.
        for (int i = 0; i < n; i++)
        {
            rhs[i] = singleRhs[i];
        }

        return rhs;
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only acceptance metric for the default laminar seed refinement.
    // Decision: Keep it as a managed helper and do not treat it as a parity reference.
    private static double ComputeLaminarResidualNorm(
        double x1,
        double x2,
        double uei1,
        double uei2,
        double theta1,
        double theta2,
        double dstar1,
        double dstar2,
        double ampl1,
        double ampl2,
        AnalysisSettings settings,
        double tkbl,
        double qinfbl,
        double tkbl_ms,
        double hstinv,
        double hstinv_ms,
        double rstbl,
        double rstbl_ms,
        double reybl,
        double reybl_re,
        double reybl_ms,
        KinematicResult? station1KinematicOverride = null,
        SecondaryStationResult? station1SecondaryOverride = null)
    {
        var localResult = AssembleLaminarStation(
            x1, x2, uei1, uei2, theta1, theta2, dstar1, dstar2, ampl1, ampl2, settings,
            tkbl, qinfbl, tkbl_ms,
            hstinv, hstinv_ms,
            rstbl, rstbl_ms,
            reybl, reybl_re, reybl_ms,
            station1KinematicOverride: station1KinematicOverride,
            station1SecondaryOverride: station1SecondaryOverride);

        double sumSquares = 0.0;
        for (int k = 0; k < 3; k++)
        {
            sumSquares += localResult.Residual[k] * localResult.Residual[k];
        }

        return Math.Sqrt(sumSquares);
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only norm helper used by the seed diagnostics.
    // Decision: Keep the helper for shared diagnostics.
    private static double ComputeResidualNorm(double[] residual)
    {
        double sumSquares = 0.0;
        for (int i = 0; i < residual.Length; i++)
        {
            sumSquares += residual[i] * residual[i];
        }

        return Math.Sqrt(sumSquares);
    }

    private readonly record struct SeedStepMetrics(
        double DeltaShear,
        double DeltaTheta,
        double DeltaDstar,
        double DeltaUe,
        double RatioShear,
        double RatioTheta,
        double RatioDstar,
        double RatioUe,
        double Dmax,
        double ResidualNorm);

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only normalization helper that makes the seed-step ratios and parity-sensitive scaling explicit.
    // Decision: Keep the helper because it centralizes the step-size policy used across the seed refinements.
    private static SeedStepMetrics ComputeSeedStepMetrics(
        double[] delta,
        double theta,
        double dstar,
        double sDenominator,
        double uei,
        int traceSide,
        int traceStation,
        int traceIteration,
        string traceMode,
        bool includeUe,
        bool useLegacyPrecision,
        bool includeShearInDmax = true)
    {
        double thetaScale = LegacyPrecisionMath.Max(theta, 1.0e-30, useLegacyPrecision);
        double dstarScale = LegacyPrecisionMath.Max(dstar, 1.0e-30, useLegacyPrecision);
        double ratioTheta = LegacyPrecisionMath.Abs(
            LegacyPrecisionMath.Divide(delta[1], thetaScale, useLegacyPrecision),
            useLegacyPrecision);
        double ratioDstar = LegacyPrecisionMath.Abs(
            LegacyPrecisionMath.Divide(delta[2], dstarScale, useLegacyPrecision),
            useLegacyPrecision);
        double dmax = LegacyPrecisionMath.Max(ratioTheta, ratioDstar, useLegacyPrecision);

        double shearScale = LegacyPrecisionMath.Max(sDenominator, 1.0e-30, useLegacyPrecision);
        double ratioShear = LegacyPrecisionMath.Abs(
            LegacyPrecisionMath.Divide(delta[0], shearScale, useLegacyPrecision),
            useLegacyPrecision);
        // Fortran MRCHUE only includes the shear ratio in DMAX for
        // turbulent/transition stations (IBL >= ITRAN). For laminar
        // stations the shear delta is not included in the relaxation limit.
        if (includeShearInDmax)
            dmax = LegacyPrecisionMath.Max(dmax, ratioShear, useLegacyPrecision);

        double ratioUe = 0.0;

        if (includeUe)
        {
            double ueScale = LegacyPrecisionMath.Max(uei, 1.0e-30, useLegacyPrecision);
            ratioUe = LegacyPrecisionMath.Abs(
                LegacyPrecisionMath.Divide(delta[3], ueScale, useLegacyPrecision),
                useLegacyPrecision);
            dmax = LegacyPrecisionMath.Max(dmax, ratioUe, useLegacyPrecision);
        }

        // The legacy seed-step norm keeps the squared REAL terms in a plain
        // sequential REAL sum before the final REAL sqrt/store. Widening that
        // accumulator by hand moves the residual norm by one ULP on early
        // seed stations, so keep the direct float recurrence here.
        float delta0f = (float)delta[0];
        float delta1f = (float)delta[1];
        float delta2f = (float)delta[2];
        float square0 = delta0f * delta0f;
        float square1 = delta1f * delta1f;
        float square2 = delta2f * delta2f;
        double wideSumSquares = (double)square0;
        wideSumSquares += (double)square1;
        wideSumSquares += (double)square2;
        float sumSquares = square0 + square1;
        sumSquares += square2;
        double residualNorm = MathF.Sqrt(sumSquares);


        return new SeedStepMetrics(
            delta[0],
            delta[1],
            delta[2],
            delta.Length > 3 ? delta[3] : 0.0,
            ratioShear,
            ratioTheta,
            ratioDstar,
            ratioUe,
            dmax,
            residualNorm);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE/MRCHDU/NEWTON seed-step relaxation
    // Difference from legacy: The managed helper centralizes the repeated `0.3/DMAX` limiter instead of open-coding it in each seed march.
    // Decision: Keep the helper, but preserve the classic REAL comparison and division in parity mode so the accepted seed step matches Fortran exactly.
    private static double ComputeLegacySeedRelaxation(double dmax, bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only relaxation limiter.
        float dmaxf = (float)dmax;
        return dmaxf > 0.3f ? 0.3f / dmaxf : 1.0f;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: DSLIM lineage inside the seed march
    // Difference from legacy: The limiting formula is the same, but it is exposed as a shared helper for the seed/refinement code.
    // Decision: Keep the helper and preserve the original limiting semantics.
    private static double ApplySeedDslim(double dstr, double thet, double msq, double hklim, bool useLegacyPrecision)
    {
        if (thet < 1e-30)
        {
            return dstr;
        }

        double h = LegacyPrecisionMath.Divide(dstr, thet, useLegacyPrecision);
        var (hk, hk_h, _) = BoundaryLayerCorrelations.KinematicShapeParameter(h, msq, useLegacyPrecision);

        double dhNumerator = Math.Max(0.0, LegacyPrecisionMath.Subtract(hklim, hk, useLegacyPrecision));
        double dhDenominator = Math.Max(hk_h, 1e-10);
        double dh = LegacyPrecisionMath.Divide(dhNumerator, dhDenominator, useLegacyPrecision);
        return LegacyPrecisionMath.Add(dstr, LegacyPrecisionMath.Multiply(dh, thet, useLegacyPrecision), useLegacyPrecision);
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only decoder for the overloaded `CTAU` storage convention used by the legacy seed replay.
    // Decision: Keep the helper because it makes the storage convention explicit.
    private static void InitializeLegacySeedStoredShear(
        double storedValue,
        int station,
        int oldTransitionStation,
        out double ctau,
        out double ampl)
    {
        if (station < oldTransitionStation)
        {
            ampl = Math.Max(storedValue, 0.0);
            ctau = LegacyLaminarShearSeed;
            return;
        }

        ampl = 0.0;
        ctau = Math.Max(storedValue, 0.0);
        if (ctau <= 0.0)
        {
            ctau = LegacyLaminarShearSeed;
        }
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only accessor over the same overloaded seed-shear storage convention.
    // Decision: Keep the helper for consistency and clarity.
    private static void ReadLegacySeedStoredShear(
        double storedValue,
        int station,
        int transitionStation,
        out double ctau,
        out double ampl)
    {
        if (station < transitionStation)
        {
            ampl = Math.Max(storedValue, 0.0);
            ctau = LegacyLaminarShearSeed;
            return;
        }

        ampl = 0.0;
        ctau = Math.Max(storedValue, 1.0e-7);
    }

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only accessor for the live AMI
    // carry that classic XFoil keeps in local variables across accepted stations.
    // Decision: Keep the parity-only helper because CTAU storage alone cannot
    // recover the post-transition amplification state once the accepted station
    // has switched to CTI storage.
    private static double ReadLegacyAmplificationCarry(
        BoundaryLayerSystemState blState,
        int station,
        int side,
        double fallback = 0.0)
    {
        if (station < 0 || station >= blState.MaxStations)
        {
            return Math.Max(fallback, 0.0);
        }

        double carry = blState.LegacyAmplificationCarry[station, side];
        return carry > 0.0 ? carry : Math.Max(fallback, 0.0);
    }

    // Legacy mapping: none
    // Difference from legacy: The managed port must persist the live AMI carry
    // explicitly because helper boundaries replace the classic local-variable flow.
    // Decision: Keep the parity-only write helper so accepted-station handoff is
    // visible and shared across the legacy seed/re-march helpers.
    private static void WriteLegacyAmplificationCarry(
        BoundaryLayerSystemState blState,
        int station,
        int side,
        double amplification)
    {
        if (station < 0 || station >= blState.MaxStations)
        {
            return;
        }

        blState.LegacyAmplificationCarry[station, side] = Math.Max(amplification, 0.0);
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: seed baseline Hk usage
    // Difference from legacy: The same baseline Hk quantity is preserved, but the managed port computes it through the shared correlation helper instead of inline algebra.
    // Decision: Keep the helper and preserve the original baseline meaning.
    private static double ComputeLegacySeedBaselineHk(
        BoundaryLayerSystemState blState,
        int station,
        int side,
        double hstinv,
        bool useLegacyPrecision)
    {
        double gm1 = LegacyPrecisionMath.GammaMinusOne(useLegacyPrecision);
        double theta = Math.Max(blState.THET[station, side], 1e-30);
        double dstar = Math.Max(blState.DSTR[station, side], 1e-30);
        double uei = Math.Max(Math.Abs(blState.UEDG[station, side]), 1e-10);
        double msq = 0.0;
        if (hstinv > 0.0)
        {
            double uesq = uei * uei * hstinv;
            msq = uesq / (gm1 * (1.0 - 0.5 * uesq));
        }

        var (hk, _, _) = BoundaryLayerCorrelations.KinematicShapeParameter(dstar / theta, msq);
        return hk;
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: XICALC WGAP computation
    // Difference from legacy: The managed port previously used a geometry-based distance
    // metric (Euclidean) instead of the BL arc-length (XSSI) metric from Fortran XICALC.
    // Decision: Recompute WGAP from XSSI to match the Fortran cubic decay exactly.
    private static void RecomputeWakeGapFromXssi(
        BoundaryLayerSystemState blState,
        WakeSeedData wakeSeed,
        InviscidSolverState inviscidState,
        LinearVortexPanelState panel)
    {
        const int side = 1; // wake lives on side 2 (C# 0-based = 1)
        int iblte = blState.IBLTE[side];
        int nbl = blState.NBL[side];
        int nw = nbl - iblte - 1;
        if (nw <= 0 || wakeSeed.GapProfileCount == 0)
            return;

        // Fortran ANTE = DXS*DYTE - DYS*DXTE (cross-product projected TE gap)
        // where DXS = 0.5*(-XP(1)+XP(N)), DYS = 0.5*(-YP(1)+YP(N))
        // and DXTE = X(1)-X(N), DYTE = Y(1)-Y(N)
        // This is NOT the same as inviscidState.TrailingEdgeGap (= Euclidean gap).
        double ante = inviscidState.TrailingEdgeAngleNormal;
        if (Math.Abs(ante) <= 1e-9 || inviscidState.IsSharpTrailingEdge)
        {
            // Zero only the active region; extra slots in the pooled buffer
            // belong to worst-case sizing and are irrelevant.
            Array.Clear(wakeSeed.GapProfile, 0, wakeSeed.GapProfileCount);
            return;
        }

        // Fortran XICALC: TELRAT = 2.50
        const float telrat = 2.50f;

        // Compute DWDXTE from TE tangent cross-product (Fortran xpanel.f line 2300)
        // Use panel spline derivatives at the first and last nodes
        float dwdxte = 0.0f;
        int n = panel.NodeCount;
        if (n >= 2)
        {
            // XP/YP are the spline derivatives (panel tangent vectors)
            // Fortran computes CROSP/DWDXTE in REAL (float) arithmetic
            float xp1f = (float)panel.XDerivative[0];
            float yp1f = (float)panel.YDerivative[0];
            float xpNf = (float)panel.XDerivative[n - 1];
            float ypNf = (float)panel.YDerivative[n - 1];
            float denomSq = (xp1f * xp1f + yp1f * yp1f) * (xpNf * xpNf + ypNf * ypNf);
            float denom = MathF.Sqrt(denomSq);
            if (denom > 1e-20f)
            {
                // Fortran xpanel.f:2483-2490 — no intermediate crosp clamp,
                // only the final DWDXTE clamp to ±3/TELRAT.
                float crosp = (xp1f * ypNf - yp1f * xpNf) / denom;
                dwdxte = crosp / MathF.Sqrt(1.0f - crosp * crosp);
            }
        }

        
        dwdxte = Math.Max(dwdxte, -3.0f / telrat);
        dwdxte = Math.Min(dwdxte, 3.0f / telrat);

        

        float aa = 3.0f + telrat * dwdxte;
        float bb = -2.0f - telrat * dwdxte;
        float antef = (float)ante;

        // Compute WGAP from XSSI using Fortran cubic (xpanel.f line 2321-2323)
        double xssiTe = blState.XSSI[iblte, side];
        int count = Math.Min(nw, wakeSeed.GapProfileCount);
        for (int iw = 0; iw < count; iw++)
        {
            int ibl = iblte + 1 + iw;
            if (ibl >= nbl) break;

            float xssiDiff = (float)(blState.XSSI[ibl, side] - xssiTe);
            float zn = 1.0f - xssiDiff / (telrat * antef);
            // Fortran: ANTE * (AA + BB*ZN) * ZN**2 where ZN**2 is ZN*ZN (single
            // multiply at integer exponent). Compute znSq first to match
            // Fortran's evaluation order — left-to-right `* zn * zn` gives a
            // different float result than `* (zn*zn)`.
            float znSq = zn * zn;
            float wgapVal = (zn >= 0.0f) ? antef * (aa + bb * zn) * znSq : 0.0f;
            
            wakeSeed.GapProfile[iw] = wgapVal;
        }
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: MRCHUE wake-TE merge uses raw ANTE.
    // Difference from legacy: Returns the raw trailing-edge normal gap (ANTE)
    // instead of the cubic-evaluated WGAP(1). Fortran's DTE = DSTR_TE1 +
    // DSTR_TE2 + ANTE uses the raw TE gap, not WGAP(1), because the cubic
    // formula's (AA+BB) = 1.0000001 (not exactly 1.0) shifts WGAP(1) by ~2 ULP.
    // Decision: Use the raw ANTE for the DSTR seed at the first wake station
    // while still using the cubic WGAP(iw) array as DSWAKI inside BLSYS.
    private static double GetFirstWakeStationTeGap(WakeSeedData? wakeSeed, double teGap)
    {
        if (wakeSeed is not null)
        {
            return wakeSeed.NormalGap;
        }
        return teGap;
    }

    // Legacy mapping: legacy-derived from WGAP wake-gap usage
    // Difference from legacy: The managed code prefers the explicit wake-gap profile and falls back to the older exponential decay only when no geometry-backed wake seed exists.
    // Decision: Keep the explicit profile as the primary path and preserve the fallback only for legacy-compatible callers without wake seed data.
    private static double GetWakeGap(WakeSeedData? wakeSeed, double teGap, int wakeIndex)
    {
        if (wakeIndex <= 0)
        {
            return 0.0;
        }

        if (wakeSeed is not null && wakeSeed.GapProfileCount >= wakeIndex)
        {
            return wakeSeed.GapProfile[wakeIndex - 1];
        }

        // Preserve the old fallback only for call sites that do not have wake
        // geometry available. The parity path now passes the explicit cubic WGAP
        // profile so classic XFoil and C# consume the same wake-gap input.
        return teGap * Math.Exp(-0.5 * (wakeIndex - 1));
    }

    // Legacy mapping: legacy-derived from WGAP wake-gap array construction
    // Difference from legacy: The managed port materializes the full wake-gap array eagerly from either the explicit wake profile or the legacy exponential fallback.
    // Decision: Keep the helper because it makes the Newton-system wake input deterministic and explicit.
    private static double[] BuildWakeGapArray(WakeSeedData? wakeSeed, double teGap, int nWake)
    {
        int count = nWake + 1;
        double[] wakeGap = XFoil.Solver.Numerics.SolverBuffers.WakeGapScratch(count);
        if (nWake <= 0)
        {
            return wakeGap;
        }

        if (wakeSeed is not null && wakeSeed.GapProfileCount > 0)
        {
            wakeGap[0] = wakeSeed.GapProfile[0];
            for (int iw = 1; iw <= nWake; iw++)
            {
                int source = Math.Min(iw - 1, wakeSeed.GapProfileCount - 1);
                wakeGap[iw] = wakeSeed.GapProfile[source];
            }


            return wakeGap;
        }

        for (int iw = 0; iw < count; iw++)
        {
            wakeGap[iw] = teGap * Math.Exp(-0.5 * iw);
        }

        return wakeGap;
    }

    // Legacy mapping: f_xfoil/src/xbl.f :: wake mirror state bookkeeping
    // Difference from legacy: The same mirrored wake-on-side-0 behavior is preserved, but the managed code performs it through an explicit helper over the state object.
    // Decision: Keep the helper and preserve the mirror semantics for downstream consumers.
    private static void SyncWakeMirror(BoundaryLayerSystemState blState)
    {
        int upperTe = blState.IBLTE[0];
        int lowerTe = blState.IBLTE[1];
        int wakeCount = blState.NBL[1] - lowerTe - 1;

        for (int iw = 1; iw <= wakeCount; iw++)
        {
            int source = lowerTe + iw;
            int target = upperTe + iw;
            if (source >= blState.MaxStations || target >= blState.MaxStations)
            {
                break;
            }

            blState.THET[target, 0] = blState.THET[source, 1];
            blState.DSTR[target, 0] = blState.DSTR[source, 1];
            blState.CTAU[target, 0] = blState.CTAU[source, 1];
            blState.UEDG[target, 0] = blState.UEDG[source, 1];
            // Fortran UPDATE does NOT copy MASS during wake equating.
            // MASS stays at the pre-equating value (from the current UPDATE's
            // MASS = DSTR*UEDG computation before the equate step).
            // DO NOT copy: blState.MASS[target, 0] = blState.MASS[source, 1];
            blState.TAU[target, 0] = blState.TAU[source, 1];
        }
    }

    // ================================================================
    // Drag computation (fallback for convergence monitoring)
    // ================================================================

    // Legacy mapping: legacy-derived fallback from XFoil TE drag estimation heuristics
    // Difference from legacy: This is a managed fallback drag estimate used for convergence monitoring, not the main post-processing drag decomposition.
    // Decision: Keep it as a managed convenience helper and do not treat it as the parity reference.
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

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only packaging helper that converts solver arrays into result objects.
    // Decision: Keep the helper because result packaging has no direct Fortran analogue.
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

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only result-packaging helper for wake profiles.
    // Decision: Keep the helper because it is purely part of the .NET result surface.
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

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only projection from solver state to the public transition-info DTO.
    // Decision: Keep the helper because it belongs to result packaging, not the legacy solver core.
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

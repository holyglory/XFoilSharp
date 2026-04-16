using System.Collections.Generic;
using XFoil.Core.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: SPECAL/SPECCL operating-point façade
// Secondary legacy source: f_xfoil/src/xfoil.f :: MRCL; f_xfoil/src/xpanel.f :: GGCALC/PSILIN lineages via delegated services
// Role in port: Provides the high-level managed analysis façade for inviscid, diagnostic viscous-prep, and Newton-coupled viscous workflows.
// Differences: The file is a managed orchestration layer with no single direct Fortran analogue; it composes multiple solver services, exposes obsolete compatibility shims, and packages results into explicit .NET objects rather than relying on the interactive XFoil command state.
// Decision: Keep the façade because it defines the supported public API surface, while documenting which paths are current, diagnostic-only, or compatibility-only.
namespace XFoil.Solver.Services;

/// <summary>
/// Primary entry point for airfoil analysis. Supports inviscid analysis (Hess-Smith
/// and linear-vortex solvers) and viscous analysis via the Newton-coupled BL solver.
///
/// The surrogate viscous pipeline (ViscousLaminarSolver, ViscousInteractionCoupler,
/// EdgeVelocityFeedbackBuilder, etc.) has been replaced by the full Newton solver
/// (ViscousSolverEngine + PolarSweepRunner). The old surrogate-pipeline methods
/// now throw NotSupportedException directing callers to the Newton path.
/// </summary>
public sealed class AirfoilAnalysisService
{
    private readonly PanelMeshGenerator panelMeshGenerator;
    private readonly HessSmithInviscidSolver inviscidSolver;
    private readonly BoundaryLayerTopologyBuilder boundaryLayerTopologyBuilder;
    private readonly ViscousStateSeedBuilder viscousStateSeedBuilder;
    private readonly ViscousStateEstimator viscousStateEstimator;
    private readonly ViscousLaminarCorrector viscousLaminarCorrector;

    // Legacy mapping: managed-only façade constructor with no direct Fortran analogue.
    // Difference from legacy: XFoil constructs and reuses global solver state implicitly; the port wires explicit service dependencies.
    // Decision: Keep the constructor because dependency injection is the right managed boundary.
    public AirfoilAnalysisService()
        : this(
            new PanelMeshGenerator(),
            new HessSmithInviscidSolver(),
            new BoundaryLayerTopologyBuilder(),
            new ViscousStateSeedBuilder(),
            new ViscousStateEstimator(),
            new ViscousLaminarCorrector())
    {
    }

    // Legacy mapping: managed-only dependency-injection constructor.
    // Difference from legacy: The original code does not compose services this way because routines operate through shared COMMON state.
    // Decision: Keep the explicit constructor for testability and API clarity.
    public AirfoilAnalysisService(
        PanelMeshGenerator panelMeshGenerator,
        HessSmithInviscidSolver inviscidSolver,
        BoundaryLayerTopologyBuilder boundaryLayerTopologyBuilder,
        ViscousStateSeedBuilder viscousStateSeedBuilder,
        ViscousStateEstimator viscousStateEstimator,
        ViscousLaminarCorrector viscousLaminarCorrector)
    {
        this.panelMeshGenerator = panelMeshGenerator ?? throw new ArgumentNullException(nameof(panelMeshGenerator));
        this.inviscidSolver = inviscidSolver ?? throw new ArgumentNullException(nameof(inviscidSolver));
        this.boundaryLayerTopologyBuilder = boundaryLayerTopologyBuilder ?? throw new ArgumentNullException(nameof(boundaryLayerTopologyBuilder));
        this.viscousStateSeedBuilder = viscousStateSeedBuilder ?? throw new ArgumentNullException(nameof(viscousStateSeedBuilder));
        this.viscousStateEstimator = viscousStateEstimator ?? throw new ArgumentNullException(nameof(viscousStateEstimator));
        this.viscousLaminarCorrector = viscousLaminarCorrector ?? throw new ArgumentNullException(nameof(viscousLaminarCorrector));
    }

    // ================================================================
    // Inviscid analysis (unchanged)
    // ================================================================

    // Legacy mapping: managed façade over f_xfoil/src/xoper.f :: SPECAL/SPECCL lineage plus the alternative linear-vortex path.
    // Difference from legacy: The managed API can dispatch between Hess-Smith and linear-vortex inviscid implementations through settings instead of a single monolithic OPER path.
    // Decision: Keep the dispatching façade because it exposes the supported inviscid runtime choices clearly.
    public InviscidAnalysisResult AnalyzeInviscid(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        if (settings.InviscidSolverType == InviscidSolverType.LinearVortex)
        {
            return AnalyzeInviscidLinearVortex(geometry, angleOfAttackDegrees, settings);
        }

        var preparedSystem = PrepareInviscidSystem(geometry, settings);
        return inviscidSolver.Analyze(preparedSystem, angleOfAttackDegrees, settings.FreestreamVelocity, settings.MachNumber);
    }

    // Legacy mapping: managed-only diagnostic façade derived from stagnation/topology bookkeeping in xpanel.f/xbl.f.
    // Difference from legacy: The topology is returned as a value object instead of being held in solver-side arrays.
    // Decision: Keep the diagnostic entry point because it supports tooling and tests cleanly.
    public BoundaryLayerTopology AnalyzeBoundaryLayerTopology(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        var analysis = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        return boundaryLayerTopologyBuilder.Build(analysis);
    }

    // Legacy mapping: managed-only diagnostic façade derived from legacy seed-state concepts.
    // Difference from legacy: The method packages seed data into explicit objects instead of exposing solver arrays.
    // Decision: Keep the diagnostic API.
    public ViscousStateSeed AnalyzeViscousStateSeed(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        var analysis = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        var topology = boundaryLayerTopologyBuilder.Build(analysis);
        return viscousStateSeedBuilder.Build(analysis, topology);
    }

    // Legacy mapping: managed-only diagnostic façade around the simplified initial-state estimator.
    // Difference from legacy: XFoil does not expose this standalone estimate step as a public API.
    // Decision: Keep it because it is useful for inspection and testing.
    public ViscousStateEstimate AnalyzeViscousInitialState(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        settings ??= new AnalysisSettings();
        var seed = AnalyzeViscousStateSeed(geometry, angleOfAttackDegrees, settings);
        return viscousStateEstimator.Estimate(seed, settings);
    }

    // ================================================================
    // Viscous analysis via Newton solver (new API)
    // ================================================================

    /// <summary>
    /// Runs a single-point viscous analysis using the Newton-coupled BL solver
    /// (ViscousSolverEngine). This is the primary viscous analysis entry point.
    /// </summary>
    /// <param name="geometry">Airfoil geometry.</param>
    /// <param name="angleOfAttackDegrees">Angle of attack in degrees.</param>
    /// <param name="settings">Analysis settings (Re, Mach, NCrit, etc.).</param>
    /// <returns>Full viscous analysis result with CL, CD, CM, drag decomposition, BL profiles.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: operating-point viscous solve lineage through VISCAL.
    // Difference from legacy: The façade extracts coordinates and delegates to ViscousSolverEngine instead of embedding the full operating-point loop itself.
    // Decision: Keep the thin wrapper because it defines the supported single-point viscous API cleanly.
    public ViscousAnalysisResult AnalyzeViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        double alphaRadians;
        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            // Fortran: ALFA = ADEG * DTOR where DTOR = ACOS(-1.0)/180.0 in REAL.
            // Use float DTOR to match Fortran's float-precision alpha.
            float dtor = MathF.Acos(-1.0f) / 180.0f;
            alphaRadians = (float)angleOfAttackDegrees * dtor;
        }
        else
        {
            alphaRadians = angleOfAttackDegrees * Math.PI / 180.0;
        }

        return ViscousSolverEngine.SolveViscous(coords, settings, alphaRadians);
    }

    /// <summary>
    /// Performs an alpha sweep using the Newton-coupled viscous solver (Type 1 polar).
    /// Delegates to PolarSweepRunner.SweepAlpha with warm-start between points.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-1 polar sweep lineage.
    // Difference from legacy: The actual sweep logic lives in PolarSweepRunner and returns managed result objects.
    // Decision: Keep the wrapper because it preserves a small public façade.
    public List<ViscousAnalysisResult> SweepViscousAlpha(
        AirfoilGeometry geometry,
        double alphaStartDegrees,
        double alphaEndDegrees,
        double alphaStepDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepAlpha(
            coords, settings, alphaStartDegrees, alphaEndDegrees, alphaStepDegrees);
    }

    /// <summary>
    /// Performs a CL sweep using the Newton-coupled viscous solver (Type 2 polar).
    /// Delegates to PolarSweepRunner.SweepCL.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-2 polar sweep lineage.
    // Difference from legacy: Delegates to the managed sweep runner instead of owning the loop locally.
    // Decision: Keep the wrapper because it exposes the public CL-sweep API cleanly.
    public List<ViscousAnalysisResult> SweepViscousCL(
        AirfoilGeometry geometry,
        double clStart,
        double clEnd,
        double clStep,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepCL(coords, settings, clStart, clEnd, clStep);
    }

    /// <summary>
    /// Performs a Reynolds number sweep at fixed CL (Type 3 polar).
    /// Delegates to PolarSweepRunner.SweepRe.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-3 polar sweep lineage with f_xfoil/src/xfoil.f :: MRCL semantics.
    // Difference from legacy: The actual loop and reduced MRCL handling live in PolarSweepRunner.
    // Decision: Keep the wrapper because it keeps the public API small.
    public List<ViscousAnalysisResult> SweepViscousRe(
        AirfoilGeometry geometry,
        double fixedCL,
        double reStart,
        double reEnd,
        double reStep,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepRe(coords, settings, fixedCL, reStart, reEnd, reStep);
    }

    // ================================================================
    // Surrogate pipeline methods (replaced -- throw NotSupportedException)
    // ================================================================

    /// <summary>
    /// [REMOVED] The surrogate ViscousIntervalSystem has been replaced by the Newton solver.
    /// Use <see cref="AnalyzeViscous"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The method no longer performs work and exists only to prevent silent behavioral drift for older callers.
    // Decision: Keep the explicit throw until the obsolete API surface can be removed.
    public ViscousIntervalSystem AnalyzeViscousIntervalSystem(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        throw new NotSupportedException(
            "The surrogate viscous interval system has been replaced by the Newton solver. " +
            "Use AnalyzeViscous() or SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate laminar correction has been replaced by the Newton solver.
    /// Use <see cref="AnalyzeViscous"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: This obsolete member only redirects callers to the current Newton path.
    // Decision: Keep the throw-only shim until callers are migrated.
    public ViscousCorrectionResult AnalyzeViscousLaminarCorrection(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int iterations = 3)
    {
        throw new NotSupportedException(
            "The surrogate laminar correction has been replaced by the Newton solver. " +
            "Use AnalyzeViscous() or SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate laminar solver has been replaced by the Newton solver.
    /// Use <see cref="AnalyzeViscous"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: This method no longer represents a supported solver path.
    // Decision: Keep the explicit failure until the obsolete API is removed.
    public ViscousSolveResult AnalyzeViscousLaminarSolve(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int maxIterations = 10,
        double residualTolerance = 0.2d)
    {
        throw new NotSupportedException(
            "The surrogate laminar solver has been replaced by the Newton solver. " +
            "Use AnalyzeViscous() or SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate viscous interaction coupler has been replaced by the Newton solver.
    /// Use <see cref="AnalyzeViscous"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The staged interaction coupler is no longer implemented here.
    // Decision: Keep the explicit throw while the obsolete API remains public.
    public ViscousInteractionResult AnalyzeViscousInteraction(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int interactionIterations = 3,
        double couplingFactor = 0.12d,
        int viscousIterations = 8,
        double residualTolerance = 0.3d)
    {
        throw new NotSupportedException(
            "The surrogate viscous interaction coupler has been replaced by the Newton solver. " +
            "Use AnalyzeViscous() or SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate displacement-coupled solver has been replaced by the Newton solver.
    /// Use <see cref="AnalyzeViscous"/> or <see cref="SweepViscousAlpha"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The old displacement-coupled path is not part of the supported runtime anymore.
    // Decision: Keep the throw-only stub until the obsolete public surface is retired.
    public DisplacementCoupledResult AnalyzeDisplacementCoupledViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int iterations = 2,
        int viscousIterations = 8,
        double residualTolerance = 0.3d,
        double displacementRelaxation = 0.5d)
    {
        throw new NotSupportedException(
            "The surrogate displacement-coupled solver has been replaced by the Newton solver. " +
            "Use AnalyzeViscous() or SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate displacement-coupled alpha sweep has been replaced.
    /// Use <see cref="SweepViscousAlpha"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use SweepViscousAlpha() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The historical sweep path has been removed in favor of PolarSweepRunner.
    // Decision: Keep the explicit throw while compatibility stubs still exist.
    public ViscousPolarSweepResult SweepDisplacementCoupledAlpha(
        AirfoilGeometry geometry,
        double alphaStartDegrees,
        double alphaEndDegrees,
        double alphaStepDegrees,
        AnalysisSettings? settings = null,
        int couplingIterations = 2,
        int viscousIterations = 8,
        double residualTolerance = 0.3d,
        double displacementRelaxation = 0.5d)
    {
        throw new NotSupportedException(
            "The surrogate displacement-coupled alpha sweep has been replaced. " +
            "Use SweepViscousAlpha() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate displacement-coupled CL sweep has been replaced.
    /// Use <see cref="SweepViscousCL"/> instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use SweepViscousCL() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The obsolete member does not implement the former path anymore.
    // Decision: Keep the throw-only stub until public cleanup is allowed.
    public ViscousLiftSweepResult SweepDisplacementCoupledLiftCoefficient(
        AirfoilGeometry geometry,
        double liftStart,
        double liftEnd,
        double liftStep,
        AnalysisSettings? settings = null,
        int couplingIterations = 2,
        int viscousIterations = 8,
        double residualTolerance = 0.3d,
        double displacementRelaxation = 0.5d,
        double initialAlphaDegrees = 0d,
        double liftTolerance = 0.05d,
        int maxIterations = 12)
    {
        throw new NotSupportedException(
            "The surrogate displacement-coupled CL sweep has been replaced. " +
            "Use SweepViscousCL() instead.");
    }

    /// <summary>
    /// [REMOVED] The surrogate displacement-coupled CL find has been replaced.
    /// Use <see cref="AnalyzeViscous"/> with the target CL approach instead.
    /// </summary>
    [Obsolete("Surrogate viscous pipeline removed. Use AnalyzeViscous() instead.")]
    // Legacy mapping: none; managed-only compatibility shim.
    // Difference from legacy: The method now exists only to redirect older callers.
    // Decision: Keep the explicit failure until the obsolete API is deleted.
    public ViscousTargetLiftResult AnalyzeDisplacementCoupledForLiftCoefficient(
        AirfoilGeometry geometry,
        double targetLiftCoefficient,
        AnalysisSettings? settings = null,
        int couplingIterations = 2,
        int viscousIterations = 8,
        double residualTolerance = 0.3d,
        double displacementRelaxation = 0.5d,
        double initialAlphaDegrees = 0d,
        double liftTolerance = 0.05d,
        int maxIterations = 12)
    {
        throw new NotSupportedException(
            "The surrogate displacement-coupled CL finder has been replaced. " +
            "Use AnalyzeViscous() or SweepViscousCL() instead.");
    }

    // ================================================================
    // Inviscid sweep methods (unchanged)
    // ================================================================

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL repeated over an alpha range.
    // Difference from legacy: The sweep uses the prepared Hess-Smith system and stores managed polar points explicitly.
    // Decision: Keep the managed sweep because it cleanly exposes the current public inviscid alpha-polar API.
    public PolarSweepResult SweepInviscidAlpha(
        AirfoilGeometry geometry,
        double alphaStartDegrees,
        double alphaEndDegrees,
        double alphaStepDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(alphaStepDegrees) < 1e-12)
        {
            throw new ArgumentException("Alpha step must be non-zero.", nameof(alphaStepDegrees));
        }

        settings ??= new AnalysisSettings();
        var preparedSystem = PrepareInviscidSystem(geometry, settings);
        var step = NormalizeStep(alphaStartDegrees, alphaEndDegrees, alphaStepDegrees);
        var points = new List<PolarPoint>();

        for (var alpha = alphaStartDegrees; ShouldContinue(alpha, alphaEndDegrees, step); alpha += step)
        {
            var result = inviscidSolver.Analyze(preparedSystem, alpha, settings.FreestreamVelocity, settings.MachNumber);
            points.Add(ToPolarPoint(result));
        }

        return new PolarSweepResult(geometry, settings, points);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL repeated over a CL range.
    // Difference from legacy: The sweep iterates through the prepared managed system and packages the results as explicit target-lift records.
    // Decision: Keep the managed implementation because it fits the public API surface cleanly.
    public InviscidLiftSweepResult SweepInviscidLiftCoefficient(
        AirfoilGeometry geometry,
        double liftStart,
        double liftEnd,
        double liftStep,
        AnalysisSettings? settings = null,
        double initialAlphaDegrees = 0d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(liftStep) < 1e-12)
        {
            throw new ArgumentException("Lift step must be non-zero.", nameof(liftStep));
        }

        settings ??= new AnalysisSettings();
        var preparedSystem = PrepareInviscidSystem(geometry, settings);
        var step = NormalizeStep(liftStart, liftEnd, liftStep);
        var points = new List<InviscidTargetLiftResult>();
        var alphaGuess = initialAlphaDegrees;

        for (var targetLift = liftStart; ShouldContinue(targetLift, liftEnd, step); targetLift += step)
        {
            var result = AnalyzeInviscidForLiftCoefficient(preparedSystem, targetLift, settings, alphaGuess);
            alphaGuess = result.AngleOfAttackDegrees;
            points.Add(new InviscidTargetLiftResult(targetLift, result));
        }

        return new InviscidLiftSweepResult(geometry, settings, points);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL.
    // Difference from legacy: The managed API exposes this as a direct public query returning an InviscidAnalysisResult instead of mutating the global operating state.
    // Decision: Keep the public helper because it is a useful capability boundary.
    public InviscidAnalysisResult AnalyzeInviscidForLiftCoefficient(
        AirfoilGeometry geometry,
        double targetLiftCoefficient,
        AnalysisSettings? settings = null,
        double initialAlphaDegrees = 0d,
        double liftTolerance = 0.01d,
        int maxIterations = 20)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (liftTolerance <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(liftTolerance), "Lift tolerance must be positive.");
        }

        if (maxIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "Iteration count must be positive.");
        }

        settings ??= new AnalysisSettings();
        var preparedSystem = PrepareInviscidSystem(geometry, settings);
        return AnalyzeInviscidForLiftCoefficient(preparedSystem, targetLiftCoefficient, settings, initialAlphaDegrees, liftTolerance, maxIterations);
    }

    // ================================================================
    // Private helpers
    // ================================================================

    // Legacy mapping: managed-only geometry extraction helper with no direct Fortran analogue.
    // Difference from legacy: The port converts immutable point objects into raw coordinate arrays before calling the low-level solvers.
    // Decision: Keep the helper because it is the natural bridge between API objects and solver arrays.
    private static (double[] x, double[] y) ExtractCoordinates(AirfoilGeometry geometry)
    {
        var points = geometry.Points;
        var inputX = new double[points.Count];
        var inputY = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            inputX[i] = points[i].X;
            inputY[i] = points[i].Y;
        }
        return (inputX, inputY);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL-style target-CL solve, managed-derived for the Hess-Smith path.
    // Difference from legacy: The root find is a simple secant-style managed loop around the prepared Hess-Smith solver instead of the original XFoil inviscid state machine.
    // Decision: Keep the helper because it provides the current public target-CL behavior.
    private InviscidAnalysisResult AnalyzeInviscidForLiftCoefficient(
        PreparedInviscidSystem preparedSystem,
        double targetLiftCoefficient,
        AnalysisSettings settings,
        double initialAlphaDegrees,
        double liftTolerance = 0.01d,
        int maxIterations = 20)
    {
        var alpha1 = initialAlphaDegrees;
        var result1 = inviscidSolver.Analyze(preparedSystem, alpha1, settings.FreestreamVelocity, settings.MachNumber);
        var error1 = targetLiftCoefficient - result1.LiftCoefficient;
        if (Math.Abs(error1) <= liftTolerance)
        {
            return result1;
        }

        double seedStep = Math.Sign(error1);
        if (seedStep == 0d)
        {
            seedStep = 1d;
        }

        var alpha2 = alpha1 + (2d * seedStep);
        var result2 = inviscidSolver.Analyze(preparedSystem, alpha2, settings.FreestreamVelocity, settings.MachNumber);
        var error2 = targetLiftCoefficient - result2.LiftCoefficient;
        if (Math.Abs(error2) <= liftTolerance)
        {
            return result2;
        }

        var bestResult = Math.Abs(error2) < Math.Abs(error1) ? result2 : result1;
        var bestError = Math.Min(Math.Abs(error1), Math.Abs(error2));

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var denominator = result2.LiftCoefficient - result1.LiftCoefficient;
            double nextAlpha;

            if (Math.Abs(denominator) < 1e-9)
            {
                nextAlpha = alpha2 + (0.5d * Math.Sign(error2 == 0d ? 1d : error2));
            }
            else
            {
                nextAlpha = alpha2 + ((targetLiftCoefficient - result2.LiftCoefficient) * (alpha2 - alpha1) / denominator);
            }

            nextAlpha = Math.Clamp(nextAlpha, -20d, 20d);
            if (Math.Abs(nextAlpha - alpha2) < 1e-6)
            {
                nextAlpha = alpha2 + (0.25d * Math.Sign(error2 == 0d ? 1d : error2));
            }

            var nextResult = inviscidSolver.Analyze(preparedSystem, nextAlpha, settings.FreestreamVelocity, settings.MachNumber);
            var nextError = targetLiftCoefficient - nextResult.LiftCoefficient;
            if (Math.Abs(nextError) < bestError)
            {
                bestError = Math.Abs(nextError);
                bestResult = nextResult;
            }

            if (Math.Abs(nextError) <= liftTolerance)
            {
                return nextResult;
            }

            alpha1 = alpha2;
            result1 = result2;
            error1 = error2;

            alpha2 = nextAlpha;
            result2 = nextResult;
            error2 = nextError;
        }

        return bestResult;
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC/SPECAL/SPECCL lineage through LinearVortexInviscidSolver.
    // Difference from legacy: The method adapts a LinearVortexInviscidResult back into the public InviscidAnalysisResult shape and fills several Hess-Smith-centric fields with placeholders.
    // Decision: Keep the adapter until the public result model is unified across inviscid back ends.
    private InviscidAnalysisResult AnalyzeInviscidLinearVortex(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings settings)
    {
        var points = geometry.Points;
        var inputX = new double[points.Count];
        var inputY = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            inputX[i] = points[i].X;
            inputY[i] = points[i].Y;
        }

        var lvResult = LinearVortexInviscidSolver.AnalyzeInviscid(
            inputX, inputY, points.Count,
            angleOfAttackDegrees,
            settings.PanelCount,
            settings.MachNumber,
            settings.UseLegacyPanelingPrecision);

        var mesh = panelMeshGenerator.Generate(geometry, settings.PanelCount, settings.Paneling);

        return new InviscidAnalysisResult(
            mesh,
            angleOfAttackDegrees,
            settings.MachNumber,
            circulation: 0.0,
            liftCoefficient: lvResult.LiftCoefficient,
            dragCoefficient: lvResult.PressureDragCoefficient,
            correctedPressureIntegratedLiftCoefficient: lvResult.LiftCoefficient,
            correctedPressureIntegratedDragCoefficient: lvResult.PressureDragCoefficient,
            pressureIntegratedLiftCoefficient: lvResult.LiftCoefficient,
            pressureIntegratedDragCoefficient: lvResult.PressureDragCoefficient,
            momentCoefficientQuarterChord: lvResult.MomentCoefficient,
            sourceStrengths: Array.Empty<double>(),
            vortexStrength: 0.0,
            pressureSamples: Array.Empty<PressureCoefficientSample>(),
            wake: new WakeGeometry(Array.Empty<WakePoint>()));
    }

    // Legacy mapping: managed-only prepared-system helper around the Hess-Smith path.
    // Difference from legacy: XFoil does not expose this prepared-system concept as a separate step.
    // Decision: Keep the helper because it supports repeated inviscid queries efficiently.
    private PreparedInviscidSystem PrepareInviscidSystem(AirfoilGeometry geometry, AnalysisSettings settings)
    {
        var mesh = panelMeshGenerator.Generate(geometry, settings.PanelCount, settings.Paneling);
        return inviscidSolver.Prepare(mesh);
    }

    // Legacy mapping: managed-only result adapter with no direct Fortran analogue.
    // Difference from legacy: The helper converts a detailed inviscid result object into the compact polar-point record used by managed sweeps.
    // Decision: Keep the adapter because it matches the managed reporting model.
    private static PolarPoint ToPolarPoint(InviscidAnalysisResult result)
    {
        return new PolarPoint(
            result.AngleOfAttackDegrees,
            result.LiftCoefficient,
            result.DragCoefficient,
            result.CorrectedPressureIntegratedLiftCoefficient,
            result.CorrectedPressureIntegratedDragCoefficient,
            result.MomentCoefficientQuarterChord,
            result.Circulation,
            result.PressureIntegratedLiftCoefficient,
            result.PressureIntegratedDragCoefficient);
    }

    // Legacy mapping: managed-only sweep utility mirroring legacy inclusive loop direction handling.
    // Difference from legacy: The helper centralizes step normalization instead of distributing it across interactive loops.
    // Decision: Keep the helper because it simplifies the sweep façade code.
    private static double NormalizeStep(double start, double end, double step)
    {
        if (end >= start)
        {
            return Math.Abs(step);
        }

        return -Math.Abs(step);
    }

    // Legacy mapping: managed-only sweep-bound helper mirroring legacy loop intent.
    // Difference from legacy: The original code relies on command-state control flow rather than a standalone predicate.
    // Decision: Keep the helper because it makes the managed sweep boundaries explicit.
    private static bool ShouldContinue(double current, double end, double step)
    {
        const double tolerance = 1e-9;
        if (step > 0d)
        {
            return current <= end + tolerance;
        }

        return current >= end - tolerance;
    }
}

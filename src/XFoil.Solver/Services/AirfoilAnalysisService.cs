using System.Collections.Generic;
using XFoil.Core.Models;
using XFoil.Solver.Models;

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

    public BoundaryLayerTopology AnalyzeBoundaryLayerTopology(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        var analysis = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        return boundaryLayerTopologyBuilder.Build(analysis);
    }

    public ViscousStateSeed AnalyzeViscousStateSeed(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        var analysis = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        var topology = boundaryLayerTopologyBuilder.Build(analysis);
        return viscousStateSeedBuilder.Build(analysis, topology);
    }

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
        double alphaRadians = angleOfAttackDegrees * Math.PI / 180.0;

        return ViscousSolverEngine.SolveViscous(coords, settings, alphaRadians);
    }

    /// <summary>
    /// Performs an alpha sweep using the Newton-coupled viscous solver (Type 1 polar).
    /// Delegates to PolarSweepRunner.SweepAlpha with warm-start between points.
    /// </summary>
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
            settings.MachNumber);

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

    private PreparedInviscidSystem PrepareInviscidSystem(AirfoilGeometry geometry, AnalysisSettings settings)
    {
        var mesh = panelMeshGenerator.Generate(geometry, settings.PanelCount, settings.Paneling);
        return inviscidSolver.Prepare(mesh);
    }

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

    private static double NormalizeStep(double start, double end, double step)
    {
        if (end >= start)
        {
            return Math.Abs(step);
        }

        return -Math.Abs(step);
    }

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

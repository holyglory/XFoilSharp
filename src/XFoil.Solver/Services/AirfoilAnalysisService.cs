using XFoil.Core.Models;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class AirfoilAnalysisService
{
    private const double DisplacementConvergenceThreshold = 1e-4;
    private const double AerodynamicConvergenceThreshold = 0.02d;
    private const int DisplacementInnerInteractionIterations = 2;
    private readonly PanelMeshGenerator panelMeshGenerator;
    private readonly HessSmithInviscidSolver inviscidSolver;
    private readonly BoundaryLayerTopologyBuilder boundaryLayerTopologyBuilder;
    private readonly ViscousStateSeedBuilder viscousStateSeedBuilder;
    private readonly ViscousStateEstimator viscousStateEstimator;
    private readonly ViscousIntervalSystemBuilder viscousIntervalSystemBuilder;
    private readonly ViscousLaminarCorrector viscousLaminarCorrector;
    private readonly ViscousLaminarSolver viscousLaminarSolver;
    private readonly ViscousInteractionCoupler viscousInteractionCoupler;
    private readonly EdgeVelocityFeedbackBuilder edgeVelocityFeedbackBuilder;
    private readonly ViscousForceEstimator viscousForceEstimator;
    private readonly DisplacementSurfaceGeometryBuilder displacementSurfaceGeometryBuilder;

    public AirfoilAnalysisService()
        : this(new PanelMeshGenerator(), new HessSmithInviscidSolver(), new BoundaryLayerTopologyBuilder(), new ViscousStateSeedBuilder(), new ViscousStateEstimator(), new ViscousIntervalSystemBuilder(), new ViscousLaminarCorrector(), new ViscousLaminarSolver(), new ViscousInteractionCoupler(), new EdgeVelocityFeedbackBuilder(), new ViscousForceEstimator(), new DisplacementSurfaceGeometryBuilder())
    {
    }

    public AirfoilAnalysisService(
        PanelMeshGenerator panelMeshGenerator,
        HessSmithInviscidSolver inviscidSolver,
        BoundaryLayerTopologyBuilder boundaryLayerTopologyBuilder,
        ViscousStateSeedBuilder viscousStateSeedBuilder,
        ViscousStateEstimator viscousStateEstimator,
        ViscousIntervalSystemBuilder viscousIntervalSystemBuilder,
        ViscousLaminarCorrector viscousLaminarCorrector,
        ViscousLaminarSolver viscousLaminarSolver,
        ViscousInteractionCoupler viscousInteractionCoupler,
        EdgeVelocityFeedbackBuilder edgeVelocityFeedbackBuilder,
        ViscousForceEstimator viscousForceEstimator,
        DisplacementSurfaceGeometryBuilder displacementSurfaceGeometryBuilder)
    {
        this.panelMeshGenerator = panelMeshGenerator ?? throw new ArgumentNullException(nameof(panelMeshGenerator));
        this.inviscidSolver = inviscidSolver ?? throw new ArgumentNullException(nameof(inviscidSolver));
        this.boundaryLayerTopologyBuilder = boundaryLayerTopologyBuilder ?? throw new ArgumentNullException(nameof(boundaryLayerTopologyBuilder));
        this.viscousStateSeedBuilder = viscousStateSeedBuilder ?? throw new ArgumentNullException(nameof(viscousStateSeedBuilder));
        this.viscousStateEstimator = viscousStateEstimator ?? throw new ArgumentNullException(nameof(viscousStateEstimator));
        this.viscousIntervalSystemBuilder = viscousIntervalSystemBuilder ?? throw new ArgumentNullException(nameof(viscousIntervalSystemBuilder));
        this.viscousLaminarCorrector = viscousLaminarCorrector ?? throw new ArgumentNullException(nameof(viscousLaminarCorrector));
        this.viscousLaminarSolver = viscousLaminarSolver ?? throw new ArgumentNullException(nameof(viscousLaminarSolver));
        this.viscousInteractionCoupler = viscousInteractionCoupler ?? throw new ArgumentNullException(nameof(viscousInteractionCoupler));
        this.edgeVelocityFeedbackBuilder = edgeVelocityFeedbackBuilder ?? throw new ArgumentNullException(nameof(edgeVelocityFeedbackBuilder));
        this.viscousForceEstimator = viscousForceEstimator ?? throw new ArgumentNullException(nameof(viscousForceEstimator));
        this.displacementSurfaceGeometryBuilder = displacementSurfaceGeometryBuilder ?? throw new ArgumentNullException(nameof(displacementSurfaceGeometryBuilder));
    }

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

    public ViscousIntervalSystem AnalyzeViscousIntervalSystem(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        settings ??= new AnalysisSettings();
        var state = AnalyzeViscousInitialState(geometry, angleOfAttackDegrees, settings);
        return viscousIntervalSystemBuilder.Build(state, settings);
    }

    public ViscousCorrectionResult AnalyzeViscousLaminarCorrection(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int iterations = 3)
    {
        settings ??= new AnalysisSettings();
        var system = AnalyzeViscousIntervalSystem(geometry, angleOfAttackDegrees, settings);
        return viscousLaminarCorrector.Correct(system, settings, iterations);
    }

    public ViscousSolveResult AnalyzeViscousLaminarSolve(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int maxIterations = 10,
        double residualTolerance = 0.2d)
    {
        settings ??= new AnalysisSettings();
        var system = AnalyzeViscousIntervalSystem(geometry, angleOfAttackDegrees, settings);
        return viscousLaminarSolver.Solve(system, settings, maxIterations, residualTolerance);
    }

    public ViscousInteractionResult AnalyzeViscousInteraction(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int interactionIterations = 3,
        double couplingFactor = 0.12d,
        int viscousIterations = 8,
        double residualTolerance = 0.3d)
    {
        settings ??= new AnalysisSettings();
        var seed = AnalyzeViscousStateSeed(geometry, angleOfAttackDegrees, settings);
        return viscousInteractionCoupler.Couple(seed, settings, interactionIterations, couplingFactor, viscousIterations, residualTolerance);
    }

    public DisplacementCoupledResult AnalyzeDisplacementCoupledViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null,
        int iterations = 2,
        int viscousIterations = 8,
        double residualTolerance = 0.3d,
        double displacementRelaxation = 0.5d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count must be positive.");
        }

        settings ??= new AnalysisSettings();
        var initialAnalysis = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        var currentGeometry = geometry;
        var finalAnalysis = initialAnalysis;
        ViscousSolveResult? finalSolve = null;
        AirfoilGeometry? displacedGeometry = null;
        var maxSurfaceDisplacement = 0d;
        var previousSurfaceDisplacement = double.PositiveInfinity;
        var previousAnalysis = initialAnalysis;
        var converged = false;
        var executedIterations = 0;
        var currentRelaxation = displacementRelaxation;
        var finalLiftDelta = double.PositiveInfinity;
        var finalMomentDelta = double.PositiveInfinity;
        var finalSeedEdgeVelocityChange = 0d;
        var finalInnerInteractionIterations = 0;
        var finalInnerInteractionConverged = false;
        ViscousStateEstimate? previousSolvedState = null;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var analysis = AnalyzeInviscid(currentGeometry, angleOfAttackDegrees, settings);
            var topology = boundaryLayerTopologyBuilder.Build(analysis);
            var seed = viscousStateSeedBuilder.Build(analysis, topology);
            if (previousSolvedState is not null)
            {
                var hybridCouplingFactor = ComputeHybridSeedCouplingFactor(currentRelaxation, previousSolvedState);
                var adjustedSeed = edgeVelocityFeedbackBuilder.ApplyDisplacementFeedback(seed, previousSolvedState, hybridCouplingFactor);
                finalSeedEdgeVelocityChange = edgeVelocityFeedbackBuilder.ComputeAverageRelativeEdgeVelocityChange(seed, adjustedSeed);
                seed = adjustedSeed;
            }

            var innerCouplingFactor = ComputeHybridSeedCouplingFactor(currentRelaxation, previousSolvedState);
            var interactionResult = viscousInteractionCoupler.Couple(
                seed,
                settings,
                DisplacementInnerInteractionIterations,
                innerCouplingFactor,
                viscousIterations,
                residualTolerance);
            finalSolve = interactionResult.SolveResult;
            previousSolvedState = finalSolve.SolvedSystem.State;
            finalInnerInteractionIterations = interactionResult.InteractionIterations;
            finalInnerInteractionConverged = interactionResult.Converged;
            finalSeedEdgeVelocityChange = Math.Max(finalSeedEdgeVelocityChange, interactionResult.AverageRelativeEdgeVelocityChange);

            currentRelaxation = ComputeAdaptiveDisplacementRelaxation(
                displacementRelaxation,
                finalSolve,
                previousSurfaceDisplacement,
                maxSurfaceDisplacement,
                previousAnalysis,
                analysis);
            var displaced = displacementSurfaceGeometryBuilder.Build(
                analysis.Mesh,
                finalSolve.SolvedSystem.State,
                geometry.Name,
                currentRelaxation);
            displacedGeometry = displaced.Geometry;
            maxSurfaceDisplacement = Math.Max(maxSurfaceDisplacement, displaced.MaxSurfaceDisplacement);
            currentGeometry = displaced.Geometry;
            finalAnalysis = AnalyzeInviscid(currentGeometry, angleOfAttackDegrees, settings);
            executedIterations = iteration + 1;
            finalLiftDelta = Math.Abs(finalAnalysis.LiftCoefficient - previousAnalysis.LiftCoefficient);
            finalMomentDelta = Math.Abs(finalAnalysis.MomentCoefficientQuarterChord - previousAnalysis.MomentCoefficientQuarterChord);

            if (finalSolve.Converged
                && finalSolve.FinalTransitionResidual <= residualTolerance
                && finalLiftDelta <= AerodynamicConvergenceThreshold
                && finalMomentDelta <= AerodynamicConvergenceThreshold
                && (displaced.MaxSurfaceDisplacement <= DisplacementConvergenceThreshold
                    || Math.Abs(displaced.MaxSurfaceDisplacement - previousSurfaceDisplacement) <= DisplacementConvergenceThreshold))
            {
                converged = true;
                break;
            }

            previousSurfaceDisplacement = displaced.MaxSurfaceDisplacement;
            previousAnalysis = finalAnalysis;
        }

        return new DisplacementCoupledResult(
            initialAnalysis,
            finalAnalysis,
            finalSolve ?? throw new InvalidOperationException("Displacement-coupled solve did not execute."),
            displacedGeometry ?? geometry,
            executedIterations,
            maxSurfaceDisplacement,
            viscousForceEstimator.EstimateProfileDragCoefficient(previousSolvedState ?? throw new InvalidOperationException("Displacement-coupled solve did not produce a viscous state.")),
            converged,
            finalInnerInteractionIterations,
            finalInnerInteractionConverged,
            finalSeedEdgeVelocityChange,
            currentRelaxation,
            double.IsFinite(finalLiftDelta) ? finalLiftDelta : 0d,
            double.IsFinite(finalMomentDelta) ? finalMomentDelta : 0d);
    }

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
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(alphaStepDegrees) < 1e-12)
        {
            throw new ArgumentException("Alpha step must be non-zero.", nameof(alphaStepDegrees));
        }

        settings ??= new AnalysisSettings();
        var step = NormalizeStep(alphaStartDegrees, alphaEndDegrees, alphaStepDegrees);
        var points = new List<ViscousPolarPoint>();

        for (var alpha = alphaStartDegrees; ShouldContinue(alpha, alphaEndDegrees, step); alpha += step)
        {
            var coupled = AnalyzeDisplacementCoupledViscous(
                geometry,
                alpha,
                settings,
                couplingIterations,
                viscousIterations,
                residualTolerance,
                displacementRelaxation);

            points.Add(new ViscousPolarPoint(
                alpha,
                coupled.FinalAnalysis.LiftCoefficient,
                coupled.EstimatedProfileDragCoefficient,
                coupled.FinalAnalysis.MomentCoefficientQuarterChord,
                coupled.FinalSolveResult.FinalSurfaceResidual,
                coupled.FinalSolveResult.FinalTransitionResidual,
                coupled.FinalSolveResult.FinalWakeResidual,
                coupled.Converged,
                coupled.InnerInteractionConverged,
                coupled.FinalDisplacementRelaxation,
                coupled.FinalSeedEdgeVelocityChange));
        }

        return new ViscousPolarSweepResult(geometry, settings, points);
    }

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
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(liftStep) < 1e-12)
        {
            throw new ArgumentException("Lift step must be non-zero.", nameof(liftStep));
        }

        settings ??= new AnalysisSettings();
        var step = NormalizeStep(liftStart, liftEnd, liftStep);
        var points = new List<ViscousTargetLiftResult>();
        var alphaGuess = initialAlphaDegrees;

        for (var targetLift = liftStart; ShouldContinue(targetLift, liftEnd, step); targetLift += step)
        {
            var result = AnalyzeDisplacementCoupledForLiftCoefficient(
                geometry,
                targetLift,
                settings,
                couplingIterations,
                viscousIterations,
                residualTolerance,
                displacementRelaxation,
                alphaGuess,
                liftTolerance,
                maxIterations);
            alphaGuess = result.SolvedAngleOfAttackDegrees;
            points.Add(result);
        }

        return new ViscousLiftSweepResult(geometry, settings, points);
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

        var alpha1 = initialAlphaDegrees;
        var result1 = AnalyzeDisplacementCoupledViscous(
            geometry,
            alpha1,
            settings,
            couplingIterations,
            viscousIterations,
            residualTolerance,
            displacementRelaxation);
        var error1 = targetLiftCoefficient - result1.FinalAnalysis.LiftCoefficient;
        if (Math.Abs(error1) <= liftTolerance)
        {
            return new ViscousTargetLiftResult(targetLiftCoefficient, alpha1, result1);
        }

        double seedStep = Math.Sign(error1);
        if (seedStep == 0d)
        {
            seedStep = 1d;
        }

        var alpha2 = alpha1 + (2d * seedStep);
        var result2 = AnalyzeDisplacementCoupledViscous(
            geometry,
            alpha2,
            settings,
            couplingIterations,
            viscousIterations,
            residualTolerance,
            displacementRelaxation);
        var error2 = targetLiftCoefficient - result2.FinalAnalysis.LiftCoefficient;
        if (Math.Abs(error2) <= liftTolerance)
        {
            return new ViscousTargetLiftResult(targetLiftCoefficient, alpha2, result2);
        }

        var bestAlpha = Math.Abs(error2) < Math.Abs(error1) ? alpha2 : alpha1;
        var bestResult = Math.Abs(error2) < Math.Abs(error1) ? result2 : result1;
        var bestError = Math.Min(Math.Abs(error1), Math.Abs(error2));

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var denominator = result2.FinalAnalysis.LiftCoefficient - result1.FinalAnalysis.LiftCoefficient;
            double nextAlpha;

            if (Math.Abs(denominator) < 1e-9)
            {
                nextAlpha = alpha2 + (0.5d * Math.Sign(error2 == 0d ? 1d : error2));
            }
            else
            {
                nextAlpha = alpha2 + ((targetLiftCoefficient - result2.FinalAnalysis.LiftCoefficient) * (alpha2 - alpha1) / denominator);
            }

            nextAlpha = Math.Clamp(nextAlpha, -20d, 20d);
            if (Math.Abs(nextAlpha - alpha2) < 1e-6)
            {
                nextAlpha = alpha2 + (0.25d * Math.Sign(error2 == 0d ? 1d : error2));
            }

            var nextResult = AnalyzeDisplacementCoupledViscous(
                geometry,
                nextAlpha,
                settings,
                couplingIterations,
                viscousIterations,
                residualTolerance,
                displacementRelaxation);
            var nextError = targetLiftCoefficient - nextResult.FinalAnalysis.LiftCoefficient;
            if (Math.Abs(nextError) < bestError)
            {
                bestError = Math.Abs(nextError);
                bestAlpha = nextAlpha;
                bestResult = nextResult;
            }

            if (Math.Abs(nextError) <= liftTolerance)
            {
                return new ViscousTargetLiftResult(targetLiftCoefficient, nextAlpha, nextResult);
            }

            alpha1 = alpha2;
            result1 = result2;
            error1 = error2;

            alpha2 = nextAlpha;
            result2 = nextResult;
            error2 = nextError;
        }

        return new ViscousTargetLiftResult(targetLiftCoefficient, bestAlpha, bestResult);
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

    private static double ComputeAdaptiveDisplacementRelaxation(
        double baseRelaxation,
        ViscousSolveResult solveResult,
        double previousSurfaceDisplacement,
        double maxSurfaceDisplacement,
        InviscidAnalysisResult previousAnalysis,
        InviscidAnalysisResult currentAnalysis)
    {
        var relaxation = baseRelaxation;
        if (solveResult.FinalTransitionResidual > 1d || solveResult.FinalSurfaceResidual > 0.5d)
        {
            relaxation *= 0.55d;
        }
        else if (solveResult.FinalTransitionResidual > 0.5d || solveResult.FinalSurfaceResidual > 0.3d)
        {
            relaxation *= 0.7d;
        }

        var liftDelta = Math.Abs(currentAnalysis.LiftCoefficient - previousAnalysis.LiftCoefficient);
        var momentDelta = Math.Abs(currentAnalysis.MomentCoefficientQuarterChord - previousAnalysis.MomentCoefficientQuarterChord);
        if (liftDelta > 0.05d || momentDelta > 0.05d)
        {
            relaxation *= 0.75d;
        }

        if (maxSurfaceDisplacement >= 0.019d
            || (double.IsFinite(previousSurfaceDisplacement) && previousSurfaceDisplacement >= 0.019d))
        {
            relaxation *= 0.6d;
        }

        return Math.Clamp(relaxation, 0.08d, baseRelaxation);
    }

    private static double ComputeHybridSeedCouplingFactor(double displacementRelaxation, ViscousStateEstimate? solvedState)
    {
        if (solvedState is null)
        {
            return Math.Clamp(0.08d + (0.12d * displacementRelaxation), 0.05d, 0.16d);
        }

        var maxTransitionResidualSignal = solvedState.UpperSurface.Stations
            .Concat(solvedState.LowerSurface.Stations)
            .Max(station => station.AmplificationFactor);
        var baseFactor = 0.08d + (0.12d * displacementRelaxation);
        if (maxTransitionResidualSignal > 2d)
        {
            baseFactor *= 0.85d;
        }

        return Math.Clamp(baseFactor, 0.05d, 0.16d);
    }
}

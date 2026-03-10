using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousLaminarSolver
{
    private const double MinimumTheta = 1e-6;
    private const double MinimumShapeFactor = 1.05d;
    private readonly ViscousIntervalSystemBuilder intervalSystemBuilder = new();
    private readonly LaminarAmplificationModel amplificationModel = new();

    public ViscousSolveResult Solve(
        ViscousIntervalSystem initialSystem,
        AnalysisSettings settings,
        int maxIterations = 10,
        double residualTolerance = 0.2d)
    {
        if (initialSystem is null)
        {
            throw new ArgumentNullException(nameof(initialSystem));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (maxIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "Iteration count must be positive.");
        }

        if (residualTolerance <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(residualTolerance), "Residual tolerance must be positive.");
        }

        var initialResidual = ComputeSurfaceResidual(initialSystem);
        var initialTransitionResidual = ComputeTransitionResidual(initialSystem);
        var initialWakeResidual = ComputeWakeResidual(initialSystem);
        var currentSystem = initialSystem;
        var currentState = initialSystem.State;
        var converged = initialResidual <= residualTolerance
                     && initialTransitionResidual <= residualTolerance
                     && initialWakeResidual <= residualTolerance;
        var iterations = 0;

        for (; iterations < maxIterations && !converged; iterations++)
        {
            var solvedUpper = SolveSurfaceBranch(currentState.UpperSurface, currentSystem.UpperSurfaceIntervals, settings);
            var solvedLower = SolveSurfaceBranch(currentState.LowerSurface, currentSystem.LowerSurfaceIntervals, settings);
            var solvedWake = SolveWakeBranch(currentState.Wake, solvedUpper, solvedLower, settings);

            currentState = new ViscousStateEstimate(currentState.Seed, solvedUpper, solvedLower, solvedWake);
            currentSystem = intervalSystemBuilder.Build(currentState, settings);
            converged = ComputeSurfaceResidual(currentSystem) <= residualTolerance
                     && ComputeTransitionResidual(currentSystem) <= residualTolerance
                     && ComputeWakeResidual(currentSystem) <= residualTolerance;
        }

        return new ViscousSolveResult(
            initialSystem,
            currentSystem,
            iterations,
            converged,
            initialResidual,
            ComputeSurfaceResidual(currentSystem),
            initialTransitionResidual,
            ComputeTransitionResidual(currentSystem),
            initialWakeResidual,
            ComputeWakeResidual(currentSystem));
    }

    private ViscousBranchState SolveSurfaceBranch(
        ViscousBranchState branch,
        IReadOnlyList<ViscousIntervalState> intervals,
        AnalysisSettings settings)
    {
        var stations = new List<ViscousStationState>(branch.Stations.Count)
        {
            branch.Stations[0]
        };

        for (var index = 1; index < branch.Stations.Count; index++)
        {
            var start = stations[^1];
            var current = branch.Stations[index];
            var updated = SolveIntervalEndState(branch.Branch, start, current, settings, index - 1);
            stations.Add(updated);
        }

        return new ViscousBranchState(branch.Branch, stations);
    }

    private ViscousStationState SolveIntervalEndState(
        BoundaryLayerBranch branch,
        ViscousStationState start,
        ViscousStationState current,
        AnalysisSettings settings,
        int intervalIndex)
    {
        var theta = Math.Max(current.MomentumThickness, MinimumTheta);
        var shapeFactor = Math.Max(current.ShapeFactor, MinimumShapeFactor);
        var state = current;

        for (var newtonIteration = 0; newtonIteration < 3; newtonIteration++)
        {
            state = CreateUpdatedStation(branch, start, current, theta, shapeFactor, settings);
            var interval = intervalSystemBuilder.BuildInterval(branch, start, state, settings, intervalIndex);
            var residual1 = interval.MomentumResidual;
            var residual2 = ComputeSecondaryResidual(interval, settings);

            if (Math.Abs(residual1) + Math.Abs(residual2) < 1e-6)
            {
                return state;
            }

            var thetaPerturbation = Math.Max(theta * 1e-4, 1e-7);
            var shapePerturbation = Math.Max(shapeFactor * 1e-4, 1e-6);

            var intervalTheta = intervalSystemBuilder.BuildInterval(
                branch,
                start,
                CreateUpdatedStation(branch, start, current, theta + thetaPerturbation, shapeFactor, settings),
                settings,
                intervalIndex);
            var intervalShape = intervalSystemBuilder.BuildInterval(
                branch,
                start,
                CreateUpdatedStation(branch, start, current, theta, shapeFactor + shapePerturbation, settings),
                settings,
                intervalIndex);

            var j11 = (intervalTheta.MomentumResidual - residual1) / thetaPerturbation;
            var thetaResidual2 = ComputeSecondaryResidual(intervalTheta, settings);
            var j21 = (thetaResidual2 - residual2) / thetaPerturbation;
            var j12 = (intervalShape.MomentumResidual - residual1) / shapePerturbation;
            var shapeResidual2 = ComputeSecondaryResidual(intervalShape, settings);
            var j22 = (shapeResidual2 - residual2) / shapePerturbation;
            var determinant = (j11 * j22) - (j12 * j21);
            if (Math.Abs(determinant) < 1e-12)
            {
                break;
            }

            var deltaTheta = ((-residual1 * j22) + (residual2 * j12)) / determinant;
            var deltaShape = ((j21 * residual1) - (j11 * residual2)) / determinant;

            theta = Math.Max(MinimumTheta, theta + (0.65d * deltaTheta));
            shapeFactor = Math.Max(MinimumShapeFactor, shapeFactor + (0.65d * deltaShape));
        }

        return CreateUpdatedStation(branch, start, current, theta, shapeFactor, settings);
    }

    private ViscousStationState CreateUpdatedStation(
        BoundaryLayerBranch branch,
        ViscousStationState start,
        ViscousStationState template,
        double theta,
        double shapeFactor,
        AnalysisSettings settings)
    {
        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        double amplificationFactor;
        ViscousFlowRegime regime;

        if (branch == BoundaryLayerBranch.Wake)
        {
            amplificationFactor = 0d;
            regime = ViscousFlowRegime.Wake;
        }
        else
        {
            var transported = amplificationModel.Advance(
                start,
                template.Xi,
                template.EdgeVelocity,
                theta,
                shapeFactor,
                settings);
            amplificationFactor = transported.AmplificationFactor;
            regime = transported.Regime;
        }

        var displacementThickness = (shapeFactor * theta) + template.WakeGap;
        var reynoldsTheta = Math.Max(1d, template.EdgeVelocity * theta / kinematicViscosity);
        var reynoldsX = Math.Max(1d, template.EdgeVelocity * Math.Max(template.Xi, 1e-7) / kinematicViscosity);
        var skinFriction = regime switch
        {
            ViscousFlowRegime.Wake => 0d,
            ViscousFlowRegime.Turbulent => 0.0576d / Math.Pow(reynoldsX, 0.2d),
            _ => 0.664d / Math.Sqrt(reynoldsX)
        };

        return new ViscousStationState(
            template.Index,
            template.Location,
            template.Xi,
            template.EdgeVelocity,
            theta,
            displacementThickness,
            shapeFactor,
            skinFriction,
            reynoldsTheta,
            template.WakeGap,
            amplificationFactor,
            regime);
    }

    private ViscousBranchState SolveWakeBranch(
        ViscousBranchState wake,
        ViscousBranchState upper,
        ViscousBranchState lower,
        AnalysisSettings settings)
    {
        var rebuiltStations = new List<ViscousStationState>(wake.Stations.Count);
        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        var theta = 0.5d * (upper.Stations[^1].MomentumThickness + lower.Stations[^1].MomentumThickness);

        for (var index = 0; index < wake.Stations.Count; index++)
        {
            var station = wake.Stations[index];
            if (index == 0)
            {
                rebuiltStations.Add(CreateUpdatedStation(BoundaryLayerBranch.Wake, wake.Stations[0], station, theta, 1.20d, settings));
                continue;
            }

            var start = rebuiltStations[^1];
            theta = SolveWakeIntervalEndTheta(start, station, settings, index - 1);
            rebuiltStations.Add(CreateUpdatedStation(BoundaryLayerBranch.Wake, start, station, theta, 1.20d, settings));
        }

        return new ViscousBranchState(wake.Branch, rebuiltStations);
    }

    private double SolveWakeIntervalEndTheta(
        ViscousStationState start,
        ViscousStationState current,
        AnalysisSettings settings,
        int intervalIndex)
    {
        var theta = Math.Max(current.MomentumThickness, MinimumTheta);

        for (var newtonIteration = 0; newtonIteration < 3; newtonIteration++)
        {
            var state = CreateUpdatedStation(BoundaryLayerBranch.Wake, start, current, theta, 1.20d, settings);
            var interval = intervalSystemBuilder.BuildInterval(BoundaryLayerBranch.Wake, start, state, settings, intervalIndex);
            var residual = interval.MomentumResidual;
            if (Math.Abs(residual) < 1e-6)
            {
                return theta;
            }

            var thetaPerturbation = Math.Max(theta * 1e-4, 1e-7);
            var intervalTheta = intervalSystemBuilder.BuildInterval(
                BoundaryLayerBranch.Wake,
                start,
                CreateUpdatedStation(BoundaryLayerBranch.Wake, start, current, theta + thetaPerturbation, 1.20d, settings),
                settings,
                intervalIndex);
            var derivative = (intervalTheta.MomentumResidual - residual) / thetaPerturbation;
            if (Math.Abs(derivative) < 1e-12)
            {
                break;
            }

            theta = Math.Max(MinimumTheta, theta - (0.65d * residual / derivative));
        }

        return theta;
    }

    private static double ComputeSurfaceResidual(ViscousIntervalSystem system)
    {
        return system.UpperSurfaceIntervals.Average(interval => Math.Abs(interval.MomentumResidual))
             + system.LowerSurfaceIntervals.Average(interval => Math.Abs(interval.MomentumResidual));
    }

    private static double ComputeTransitionResidual(ViscousIntervalSystem system)
    {
        var laminarIntervals = system.UpperSurfaceIntervals
            .Concat(system.LowerSurfaceIntervals)
            .Where(interval => interval.Kind == ViscousIntervalKind.Laminar)
            .ToArray();
        if (laminarIntervals.Length == 0)
        {
            return 0d;
        }

        return laminarIntervals.Average(interval => Math.Abs(interval.AmplificationResidual));
    }

    private static double ComputeWakeResidual(ViscousIntervalSystem system)
    {
        if (system.WakeIntervals.Count == 0)
        {
            return 0d;
        }

        return system.WakeIntervals.Average(interval => Math.Abs(interval.MomentumResidual));
    }

    private static double ComputeSecondaryResidual(ViscousIntervalState interval, AnalysisSettings settings)
    {
        if (interval.Kind != ViscousIntervalKind.Laminar)
        {
            return interval.ShapeResidual;
        }

        return interval.ShapeResidual + (interval.AmplificationResidual / Math.Max(settings.CriticalAmplificationFactor, 1d));
    }
}

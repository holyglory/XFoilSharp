using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousLaminarCorrector
{
    private const double MinimumTheta = 1e-6;
    private const double MinimumShapeFactor = 1.05d;

    public ViscousCorrectionResult Correct(
        ViscousIntervalSystem initialSystem,
        AnalysisSettings settings,
        int iterations = 3,
        double momentumRelaxation = 0.25d,
        double shapeRelaxation = 0.35d,
        double transitionRelaxation = 0.20d)
    {
        if (initialSystem is null)
        {
            throw new ArgumentNullException(nameof(initialSystem));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count must be positive.");
        }

        var builder = new ViscousIntervalSystemBuilder();
        var currentState = initialSystem.State;
        var currentSystem = initialSystem;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var correctedUpper = CorrectSurfaceBranch(currentState.UpperSurface, currentSystem.UpperSurfaceIntervals, settings, momentumRelaxation, shapeRelaxation, transitionRelaxation);
            var correctedLower = CorrectSurfaceBranch(currentState.LowerSurface, currentSystem.LowerSurfaceIntervals, settings, momentumRelaxation, shapeRelaxation, transitionRelaxation);
            var correctedWake = RebuildWakeBranch(currentState.Wake, correctedUpper, correctedLower, settings);

            currentState = new ViscousStateEstimate(currentState.Seed, correctedUpper, correctedLower, correctedWake);
            currentSystem = builder.Build(currentState, settings);
        }

        return new ViscousCorrectionResult(initialSystem, currentSystem, iterations);
    }

    private static ViscousBranchState CorrectSurfaceBranch(
        ViscousBranchState branch,
        IReadOnlyList<ViscousIntervalState> intervals,
        AnalysisSettings settings,
        double momentumRelaxation,
        double shapeRelaxation,
        double transitionRelaxation)
    {
        var amplificationModel = new LaminarAmplificationModel();
        var stations = new List<ViscousStationState>(branch.Stations.Count)
        {
            branch.Stations[0]
        };

        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        for (var index = 1; index < branch.Stations.Count; index++)
        {
            var previousCorrected = stations[^1];
            var originalStation = branch.Stations[index];
            var interval = intervals[index - 1];
            var deltaXi = Math.Max(originalStation.Xi - previousCorrected.Xi, 1e-9);

            var correctedTheta = originalStation.MomentumThickness - (momentumRelaxation * interval.MomentumResidual * deltaXi);
            correctedTheta = Math.Max(MinimumTheta, correctedTheta);

            var correctedShapeResidual = interval.ShapeResidual + ComputeNormalizedAmplificationResidual(interval, settings);
            var correctedShapeFactor = originalStation.ShapeFactor - (shapeRelaxation * correctedShapeResidual);
            correctedShapeFactor -= transitionRelaxation * ComputeNormalizedAmplificationResidual(interval, settings);
            correctedShapeFactor = Math.Max(MinimumShapeFactor, correctedShapeFactor);

            var correctedDisplacementThickness = (correctedShapeFactor * correctedTheta) + originalStation.WakeGap;
            var correctedSkinFriction = Math.Max(
                0d,
                originalStation.SkinFrictionCoefficient - (0.1d * interval.SkinFrictionResidual));
            var correctedReTheta = Math.Max(
                1d,
                originalStation.EdgeVelocity * correctedTheta / kinematicViscosity);
            var transported = amplificationModel.Advance(
                previousCorrected,
                originalStation.Xi,
                originalStation.EdgeVelocity,
                correctedTheta,
                correctedShapeFactor,
                settings);
            var correctedRegime = transported.Regime;

            if (correctedRegime == ViscousFlowRegime.Turbulent)
            {
                var reynoldsX = Math.Max(1d, originalStation.EdgeVelocity * Math.Max(originalStation.Xi, 1e-7) / kinematicViscosity);
                correctedSkinFriction = 0.0576d / Math.Pow(reynoldsX, 0.2d);
            }

            stations.Add(new ViscousStationState(
                originalStation.Index,
                originalStation.Location,
                originalStation.Xi,
                originalStation.EdgeVelocity,
                correctedTheta,
                correctedDisplacementThickness,
                correctedShapeFactor,
                correctedSkinFriction,
                correctedReTheta,
                originalStation.WakeGap,
                transported.AmplificationFactor,
                correctedRegime));
        }

        return new ViscousBranchState(branch.Branch, stations);
    }

    private static ViscousBranchState RebuildWakeBranch(
        ViscousBranchState wake,
        ViscousBranchState correctedUpper,
        ViscousBranchState correctedLower,
        AnalysisSettings settings)
    {
        var rebuiltStations = new List<ViscousStationState>(wake.Stations.Count);
        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        var theta = 0.5d * (correctedUpper.Stations[^1].MomentumThickness + correctedLower.Stations[^1].MomentumThickness);

        for (var index = 0; index < wake.Stations.Count; index++)
        {
            var station = wake.Stations[index];
            if (index > 0)
            {
                var deltaXi = Math.Max(station.Xi - wake.Stations[index - 1].Xi, 1e-9);
                theta += 0.015d * Math.Sqrt(kinematicViscosity * deltaXi / Math.Max(station.EdgeVelocity, 1e-4));
            }

            var displacementThickness = (1.20d * theta) + station.WakeGap;
            var reynoldsTheta = Math.Max(1d, station.EdgeVelocity * theta / kinematicViscosity);

            rebuiltStations.Add(new ViscousStationState(
                station.Index,
                station.Location,
                station.Xi,
                station.EdgeVelocity,
                theta,
                displacementThickness,
                1.20d,
                0d,
                reynoldsTheta,
                station.WakeGap,
                station.AmplificationFactor,
                station.Regime));
        }

        return new ViscousBranchState(wake.Branch, rebuiltStations);
    }

    private static double ComputeNormalizedAmplificationResidual(ViscousIntervalState interval, AnalysisSettings settings)
    {
        if (interval.Kind != ViscousIntervalKind.Laminar)
        {
            return 0d;
        }

        return interval.AmplificationResidual / Math.Max(settings.CriticalAmplificationFactor, 1d);
    }
}

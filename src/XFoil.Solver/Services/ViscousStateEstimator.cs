using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousStateEstimator
{
    private const double MinimumXi = 1e-7;
    private const double MinimumTheta = 1e-6;
    private const double LaminarShapeFactor = 2.59d;
    private const double WakeShapeFactor = 1.20d;
    private readonly LaminarAmplificationModel amplificationModel = new();

    public ViscousStateEstimate Estimate(ViscousStateSeed seed, AnalysisSettings settings)
    {
        if (seed is null)
        {
            throw new ArgumentNullException(nameof(seed));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        var upperSurface = EstimateSurfaceBranch(
            seed.UpperSurface,
            kinematicViscosity,
            settings);
        var lowerSurface = EstimateSurfaceBranch(
            seed.LowerSurface,
            kinematicViscosity,
            settings);
        var wakeStartTheta = 0.5d * (upperSurface.Stations[^1].MomentumThickness + lowerSurface.Stations[^1].MomentumThickness);
        var wake = EstimateWakeBranch(seed.Wake, kinematicViscosity, wakeStartTheta);

        return new ViscousStateEstimate(seed, upperSurface, lowerSurface, wake);
    }

    private ViscousBranchState EstimateSurfaceBranch(
        ViscousBranchSeed seed,
        double kinematicViscosity,
        AnalysisSettings settings)
    {
        var states = new List<ViscousStationState>(seed.Stations.Count);
        var integral = 0d;

        for (var index = 0; index < seed.Stations.Count; index++)
        {
            var station = seed.Stations[index];
            var xi = Math.Max(station.Xi, MinimumXi);
            var edgeVelocity = Math.Max(station.EdgeVelocity, 1e-4);

            if (index > 0)
            {
                var previousStation = states[^1];
                var previousXi = Math.Max(previousStation.Xi, MinimumXi);
                var previousVelocity = Math.Max(previousStation.EdgeVelocity, 1e-4);
                var deltaXi = Math.Max(xi - previousXi, 1e-9);
                integral += 0.5d * (Math.Pow(previousVelocity, 5d) + Math.Pow(edgeVelocity, 5d)) * deltaXi;
            }

            var laminarTheta = index == 0
                ? MinimumTheta
                : Math.Sqrt(Math.Max(MinimumTheta * MinimumTheta, 0.45d * kinematicViscosity * integral / Math.Pow(edgeVelocity, 6d)));
            var theta = laminarTheta;
            var shapeFactor = LaminarShapeFactor;
            var reynoldsTheta = edgeVelocity * theta / kinematicViscosity;
            var reynoldsX = edgeVelocity * xi / kinematicViscosity;
            var amplification = 0d;
            var regime = ViscousFlowRegime.Laminar;

            if (index > 0)
            {
                var previousState = states[^1];
                var transported = amplificationModel.Advance(
                    previousState,
                    station.Xi,
                    station.EdgeVelocity,
                    theta,
                    LaminarShapeFactor,
                    settings);
                amplification = transported.AmplificationFactor;
                regime = transported.Regime;
            }

            if (regime == ViscousFlowRegime.Turbulent)
            {
                if (index > 0)
                {
                    var previousState = states[^1];
                    var deltaXi = Math.Max(xi - previousState.Xi, 1e-9);
                    theta = Math.Max(
                        laminarTheta,
                        previousState.MomentumThickness + (0.5d * (0.0576d / Math.Pow(Math.Max(reynoldsX, 1e-9), 0.2d)) * deltaXi));
                }

                reynoldsTheta = edgeVelocity * theta / kinematicViscosity;
                shapeFactor = 1.4d + (0.25d / (1d + (0.001d * reynoldsTheta)));
            }

            var displacementThickness = shapeFactor * theta;
            var skinFriction = regime == ViscousFlowRegime.Turbulent
                ? 0.0576d / Math.Pow(Math.Max(reynoldsX, 1e-9), 0.2d)
                : 0.664d / Math.Sqrt(Math.Max(reynoldsX, 1e-9));

            states.Add(new ViscousStationState(
                station.Index,
                station.Location,
                station.Xi,
                station.EdgeVelocity,
                theta,
                displacementThickness,
                shapeFactor,
                skinFriction,
                reynoldsTheta,
                station.WakeGap,
                amplification,
                regime));
        }

        return new ViscousBranchState(seed.Branch, states);
    }

    private static ViscousBranchState EstimateWakeBranch(ViscousBranchSeed seed, double kinematicViscosity, double startTheta)
    {
        var states = new List<ViscousStationState>(seed.Stations.Count);
        var previousTheta = Math.Max(startTheta, MinimumTheta);

        for (var index = 0; index < seed.Stations.Count; index++)
        {
            var station = seed.Stations[index];
            var xi = index == 0 ? MinimumXi : Math.Max(station.Xi - seed.Stations[0].Xi, MinimumXi);
            var edgeVelocity = Math.Max(station.EdgeVelocity, 1e-4);

            if (index > 0)
            {
                var deltaXi = Math.Max(station.Xi - seed.Stations[index - 1].Xi, 1e-9);
                previousTheta += 0.015d * Math.Sqrt(kinematicViscosity * deltaXi / edgeVelocity);
            }

            var displacementThickness = (WakeShapeFactor * previousTheta) + station.WakeGap;
            var reynoldsTheta = edgeVelocity * previousTheta / kinematicViscosity;

            states.Add(new ViscousStationState(
                station.Index,
                station.Location,
                station.Xi,
                station.EdgeVelocity,
                previousTheta,
                displacementThickness,
                WakeShapeFactor,
                0d,
                reynoldsTheta,
                station.WakeGap,
                0d,
                ViscousFlowRegime.Wake));
        }

        return new ViscousBranchState(seed.Branch, states);
    }
}

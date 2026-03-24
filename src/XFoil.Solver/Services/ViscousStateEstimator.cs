using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: MRCHUE/COMSET initialization lineage; f_xfoil/src/xblsys.f :: transition and BL correlation families
// Role in port: Produces a managed first-pass viscous estimate for diagnostic commands from a seed state.
// Differences: There is no direct Fortran routine matching this file; it combines Thwaites-style laminar growth, a simplified e^N transport model, and a heuristic wake continuation into a standalone managed estimator.
// Decision: Keep the managed estimator for diagnostics only. Do not treat it as a parity reference for the operating-point viscous solve.
namespace XFoil.Solver.Services;

/// <summary>
/// Estimates initial BL state from a viscous state seed using Thwaites' method
/// and a simplified e^N transition criterion. Used for BL topology analysis and
/// initial state estimation before the full Newton solver.
/// </summary>
public sealed class ViscousStateEstimator
{
    private const double MinimumXi = 1e-7;
    private const double MinimumTheta = 1e-6;
    private const double LaminarShapeFactor = 2.59d;
    private const double WakeShapeFactor = 1.20d;

    // Legacy mapping: none; managed-only diagnostic entry point informed by MRCHUE/COMSET seed initialization concepts.
    // Difference from legacy: XFoil initializes the viscous state inside the main march and Newton workflow, while this method produces a standalone estimate object from a seed.
    // Decision: Keep the diagnostic API because it is useful for inspection and is intentionally separate from parity-critical flow.
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

    // Legacy mapping: none; managed-only laminar/turbulent surface estimate derived from classical boundary-layer correlations.
    // Difference from legacy: The method uses simplified Thwaites-style growth and a heuristic regime switch rather than replaying the exact XFoil surface march.
    // Decision: Keep the simplified estimator because this file exists for diagnostics, not for parity replay.
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
                var transported = AdvanceAmplification(
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

    // Legacy mapping: none; managed-only wake continuation heuristic.
    // Difference from legacy: The wake branch here is a compact estimate built from the seed and a fixed wake shape factor rather than the coupled wake march used by the main solver.
    // Decision: Keep the helper as a diagnostic approximation only.
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

    // ================================================================
    // Inlined amplification model (was LaminarAmplificationModel)
    // ================================================================

    // Legacy mapping: none; managed-only diagnostic transition-growth model informed by XFoil's transition correlations.
    // Difference from legacy: The logic uses a simplified amplification transport and regime trigger instead of the full DAMPL/TRCHEK transition machinery used by the main parity path.
    // Decision: Keep the helper for cheap diagnostic estimates, but do not use it as a substitute for the direct transition model port.
    private static (double AmplificationFactor, ViscousFlowRegime Regime) AdvanceAmplification(
        ViscousStationState start,
        double endXi,
        double endEdgeVelocity,
        double endTheta,
        double endShapeFactor,
        AnalysisSettings settings)
    {
        if (start.Regime == ViscousFlowRegime.Wake)
        {
            return (0d, ViscousFlowRegime.Wake);
        }

        if (start.Regime == ViscousFlowRegime.Turbulent)
        {
            return (Math.Max(start.AmplificationFactor, settings.CriticalAmplificationFactor), ViscousFlowRegime.Turbulent);
        }

        var kinematicViscosity = settings.FreestreamVelocity / settings.ReynoldsNumber;
        var averageTheta = Math.Max(MinimumTheta, 0.5d * (start.MomentumThickness + endTheta));
        var endReynoldsTheta = Math.Max(1d, endEdgeVelocity * endTheta / kinematicViscosity);
        var averageReynoldsTheta = 0.5d * (start.ReynoldsTheta + endReynoldsTheta);
        var averageShapeFactor = 0.5d * (start.ShapeFactor + endShapeFactor);
        var logEdgeVelocityChange = Math.Log(Math.Max(endEdgeVelocity, 1e-6) / Math.Max(start.EdgeVelocity, 1e-6));

        var instability = Math.Max(0d, (averageReynoldsTheta / settings.TransitionReynoldsTheta) - 1d);
        if (instability <= 0d)
        {
            return (start.AmplificationFactor, ViscousFlowRegime.Laminar);
        }

        var constants = BoundaryLayerCorrelationConstants.Default;
        var shapeInfluence = Math.Max(0.08d, averageShapeFactor - 1.35d);
        var adverseGradientInfluence = 1d + (constants.TransitionConstant * Math.Max(0d, -logEdgeVelocityChange));
        var favorableGradientDamping = 1d + (0.35d * Math.Max(0d, logEdgeVelocityChange));
        var reynoldsInfluence = Math.Pow(Math.Max(1d, averageReynoldsTheta / settings.TransitionReynoldsTheta), 1d / constants.TransitionExponent);
        var thetaScale = Math.Max(averageTheta * constants.GcConstant, 1e-6);

        var growthRate = instability
             * (1d + (constants.DlConstant * shapeInfluence))
             * adverseGradientInfluence
             * reynoldsInfluence
             / (thetaScale * favorableGradientDamping);

        var deltaXi = Math.Max(endXi - start.Xi, 1e-9);
        var growthIncrement = growthRate * deltaXi;

        var amplification = Math.Max(
            start.AmplificationFactor,
            start.AmplificationFactor + growthIncrement);

        if (endXi < 0.02d)
        {
            return (Math.Min(amplification, settings.CriticalAmplificationFactor - 1e-6), ViscousFlowRegime.Laminar);
        }

        if (endXi >= 0.02d && amplification >= settings.CriticalAmplificationFactor)
        {
            return (settings.CriticalAmplificationFactor, ViscousFlowRegime.Turbulent);
        }

        return (amplification, ViscousFlowRegime.Laminar);
    }
}

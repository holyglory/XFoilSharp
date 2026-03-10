using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class LaminarAmplificationModel
{
    private const double MinimumTheta = 1e-6;
    private readonly BoundaryLayerCorrelationConstants constants = BoundaryLayerCorrelationConstants.Default;

    public double ComputeGrowthRate(
        ViscousStationState start,
        double endXi,
        double endEdgeVelocity,
        double endTheta,
        double endShapeFactor,
        AnalysisSettings settings)
    {
        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (start.Regime != ViscousFlowRegime.Laminar)
        {
            return 0d;
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
            return 0d;
        }

        var shapeInfluence = Math.Max(0.08d, averageShapeFactor - 1.35d);
        var adverseGradientInfluence = 1d + (constants.TransitionConstant * Math.Max(0d, -logEdgeVelocityChange));
        var favorableGradientDamping = 1d + (0.35d * Math.Max(0d, logEdgeVelocityChange));
        var reynoldsInfluence = Math.Pow(Math.Max(1d, averageReynoldsTheta / settings.TransitionReynoldsTheta), 1d / constants.TransitionExponent);
        var thetaScale = Math.Max(averageTheta * constants.GcConstant, 1e-6);

        return instability
             * (1d + (constants.DlConstant * shapeInfluence))
             * adverseGradientInfluence
             * reynoldsInfluence
             / (thetaScale * favorableGradientDamping);
    }

    public double ComputeGrowthIncrement(
        ViscousStationState start,
        double endXi,
        double endEdgeVelocity,
        double endTheta,
        double endShapeFactor,
        AnalysisSettings settings)
    {
        var deltaXi = Math.Max(endXi - start.Xi, 1e-9);
        return ComputeGrowthRate(start, endXi, endEdgeVelocity, endTheta, endShapeFactor, settings) * deltaXi;
    }

    public (double AmplificationFactor, ViscousFlowRegime Regime) Advance(
        ViscousStationState start,
        double endXi,
        double endEdgeVelocity,
        double endTheta,
        double endShapeFactor,
        AnalysisSettings settings)
    {
        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (start.Regime == ViscousFlowRegime.Wake)
        {
            return (0d, ViscousFlowRegime.Wake);
        }

        if (start.Regime == ViscousFlowRegime.Turbulent)
        {
            return (Math.Max(start.AmplificationFactor, settings.CriticalAmplificationFactor), ViscousFlowRegime.Turbulent);
        }

        var amplification = Math.Max(
            start.AmplificationFactor,
            start.AmplificationFactor + ComputeGrowthIncrement(start, endXi, endEdgeVelocity, endTheta, endShapeFactor, settings));

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

namespace XFoil.Solver.Models;

public sealed class BoundaryLayerCorrelationConstants
{
    public static BoundaryLayerCorrelationConstants Default { get; } = new(
        shearCoefficientConstant: 5.6d,
        gaConstant: 6.70d,
        gbConstant: 0.75d,
        gcConstant: 18.0d,
        dlConstant: 0.9d,
        transitionConstant: 1.8d,
        transitionExponent: 3.3d,
        duxConstant: 1.0d,
        cfScale: 1.0d);

    public BoundaryLayerCorrelationConstants(
        double shearCoefficientConstant,
        double gaConstant,
        double gbConstant,
        double gcConstant,
        double dlConstant,
        double transitionConstant,
        double transitionExponent,
        double duxConstant,
        double cfScale)
    {
        ShearCoefficientConstant = shearCoefficientConstant;
        GaConstant = gaConstant;
        GbConstant = gbConstant;
        GcConstant = gcConstant;
        DlConstant = dlConstant;
        TransitionConstant = transitionConstant;
        TransitionExponent = transitionExponent;
        DuxConstant = duxConstant;
        CfScale = cfScale;
        CtConstant = 0.5d / (gaConstant * gaConstant * gbConstant);
    }

    public double ShearCoefficientConstant { get; }

    public double GaConstant { get; }

    public double GbConstant { get; }

    public double GcConstant { get; }

    public double DlConstant { get; }

    public double TransitionConstant { get; }

    public double TransitionExponent { get; }

    public double DuxConstant { get; }

    public double CfScale { get; }

    public double CtConstant { get; }
}

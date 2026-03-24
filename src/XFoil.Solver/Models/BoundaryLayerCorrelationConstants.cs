// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: HST/CFT/DIT correlation constants
// Role in port: Managed immutable bundle of boundary-layer correlation coefficients used by older surrogate interval workflows.
// Differences: Legacy XFoil inlines these constants inside correlation routines, while the managed port packages them as a reusable object with an explicit default set.
// Decision: Keep the managed constants object because it makes correlation tuning and testing explicit.
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

    // Legacy mapping: f_xfoil/src/xblsys.f :: inline correlation constants and derived CT scaling.
    // Difference from legacy: The constants and the derived `CtConstant` are computed once in a managed constructor instead of being repeated or implicit inside each correlation routine.
    // Decision: Keep the managed constructor because it centralizes constant definition cleanly.
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

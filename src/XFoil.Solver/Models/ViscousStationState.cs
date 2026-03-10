using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class ViscousStationState
{
    public ViscousStationState(
        int index,
        AirfoilPoint location,
        double xi,
        double edgeVelocity,
        double momentumThickness,
        double displacementThickness,
        double shapeFactor,
        double skinFrictionCoefficient,
        double reynoldsTheta,
        double wakeGap,
        double amplificationFactor,
        ViscousFlowRegime regime)
    {
        Index = index;
        Location = location;
        Xi = xi;
        EdgeVelocity = edgeVelocity;
        MomentumThickness = momentumThickness;
        DisplacementThickness = displacementThickness;
        ShapeFactor = shapeFactor;
        SkinFrictionCoefficient = skinFrictionCoefficient;
        ReynoldsTheta = reynoldsTheta;
        WakeGap = wakeGap;
        AmplificationFactor = amplificationFactor;
        Regime = regime;
    }

    public int Index { get; }

    public AirfoilPoint Location { get; }

    public double Xi { get; }

    public double EdgeVelocity { get; }

    public double MomentumThickness { get; }

    public double DisplacementThickness { get; }

    public double ShapeFactor { get; }

    public double SkinFrictionCoefficient { get; }

    public double ReynoldsTheta { get; }

    public double WakeGap { get; }

    public double AmplificationFactor { get; }

    public ViscousFlowRegime Regime { get; }
}

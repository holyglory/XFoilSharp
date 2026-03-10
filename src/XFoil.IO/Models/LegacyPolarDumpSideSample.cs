namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpSideSample
{
    public LegacyPolarDumpSideSample(double x, double pressureCoefficient, double momentumThickness, double displacementThickness, double skinFrictionCoefficient, double shearLagCoefficient)
    {
        X = x;
        PressureCoefficient = pressureCoefficient;
        MomentumThickness = momentumThickness;
        DisplacementThickness = displacementThickness;
        SkinFrictionCoefficient = skinFrictionCoefficient;
        ShearLagCoefficient = shearLagCoefficient;
    }

    public double X { get; }

    public double PressureCoefficient { get; }

    public double MomentumThickness { get; }

    public double DisplacementThickness { get; }

    public double SkinFrictionCoefficient { get; }

    public double ShearLagCoefficient { get; }
}

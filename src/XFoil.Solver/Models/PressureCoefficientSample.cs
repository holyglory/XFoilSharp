using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class PressureCoefficientSample
{
    public PressureCoefficientSample(
        AirfoilPoint location,
        double tangentialVelocity,
        double pressureCoefficient,
        double correctedPressureCoefficient)
    {
        Location = location;
        TangentialVelocity = tangentialVelocity;
        PressureCoefficient = pressureCoefficient;
        CorrectedPressureCoefficient = correctedPressureCoefficient;
    }

    public AirfoilPoint Location { get; }

    public double TangentialVelocity { get; }

    public double PressureCoefficient { get; }

    public double CorrectedPressureCoefficient { get; }
}

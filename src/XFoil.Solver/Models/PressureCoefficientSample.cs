// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: CPWRIT pressure output lineage
// Role in port: Managed DTO for one sampled pressure point on the airfoil surface.
// Differences: Legacy XFoil writes Cp samples directly from arrays, while the managed port returns them as explicit objects with location and corrected/unadjusted values.
// Decision: Keep the managed DTO because it simplifies exports, diagnostics, and tests.
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

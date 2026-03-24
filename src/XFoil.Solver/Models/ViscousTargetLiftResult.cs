// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: CLI/VISC target-lift operating-point lineage
// Role in port: Managed pair of the requested viscous lift coefficient and the solved operating point.
// Differences: Legacy XFoil presents this relationship through interactive output, while the managed port returns it as a structured object.
// Decision: Keep the managed DTO because it makes target versus solved values explicit.
namespace XFoil.Solver.Models;

public sealed class ViscousTargetLiftResult
{
    public ViscousTargetLiftResult(
        double targetLiftCoefficient,
        double solvedAngleOfAttackDegrees,
        DisplacementCoupledResult operatingPoint)
    {
        TargetLiftCoefficient = targetLiftCoefficient;
        SolvedAngleOfAttackDegrees = solvedAngleOfAttackDegrees;
        OperatingPoint = operatingPoint;
    }

    public double TargetLiftCoefficient { get; }

    public double SolvedAngleOfAttackDegrees { get; }

    public DisplacementCoupledResult OperatingPoint { get; }
}

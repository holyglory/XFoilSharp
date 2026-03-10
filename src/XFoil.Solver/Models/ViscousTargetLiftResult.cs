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

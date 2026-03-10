namespace XFoil.Solver.Models;

public sealed class InviscidTargetLiftResult
{
    public InviscidTargetLiftResult(
        double targetLiftCoefficient,
        InviscidAnalysisResult operatingPoint)
    {
        TargetLiftCoefficient = targetLiftCoefficient;
        OperatingPoint = operatingPoint ?? throw new ArgumentNullException(nameof(operatingPoint));
    }

    public double TargetLiftCoefficient { get; }

    public InviscidAnalysisResult OperatingPoint { get; }
}

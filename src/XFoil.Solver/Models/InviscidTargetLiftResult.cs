// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: CLI target-lift operating-point lineage
// Role in port: Managed pair of the requested lift coefficient and the solved operating point.
// Differences: Legacy XFoil reports target-CL solves through interactive buffers and scalars, while the managed port returns an explicit immutable result object.
// Decision: Keep the managed DTO because it clarifies target versus solved values in the public API.
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

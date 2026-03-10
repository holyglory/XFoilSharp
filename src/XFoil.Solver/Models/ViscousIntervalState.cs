namespace XFoil.Solver.Models;

public sealed class ViscousIntervalState
{
    public ViscousIntervalState(
        BoundaryLayerBranch branch,
        ViscousIntervalKind kind,
        int index,
        ViscousStationState start,
        ViscousStationState end,
        ViscousStationDerivedState startDerived,
        ViscousStationDerivedState endDerived,
        double upwindWeight,
        double logXiChange,
        double logEdgeVelocityChange,
        double logThetaChange,
        double logKinematicShapeFactorChange,
        double amplificationGrowthRate,
        double momentumResidual,
        double shapeResidual,
        double skinFrictionResidual,
        double amplificationResidual)
    {
        Branch = branch;
        Kind = kind;
        Index = index;
        Start = start;
        End = end;
        StartDerived = startDerived;
        EndDerived = endDerived;
        UpwindWeight = upwindWeight;
        LogXiChange = logXiChange;
        LogEdgeVelocityChange = logEdgeVelocityChange;
        LogThetaChange = logThetaChange;
        LogKinematicShapeFactorChange = logKinematicShapeFactorChange;
        AmplificationGrowthRate = amplificationGrowthRate;
        MomentumResidual = momentumResidual;
        ShapeResidual = shapeResidual;
        SkinFrictionResidual = skinFrictionResidual;
        AmplificationResidual = amplificationResidual;
    }

    public BoundaryLayerBranch Branch { get; }

    public ViscousIntervalKind Kind { get; }

    public int Index { get; }

    public ViscousStationState Start { get; }

    public ViscousStationState End { get; }

    public ViscousStationDerivedState StartDerived { get; }

    public ViscousStationDerivedState EndDerived { get; }

    public double UpwindWeight { get; }

    public double LogXiChange { get; }

    public double LogEdgeVelocityChange { get; }

    public double LogThetaChange { get; }

    public double LogKinematicShapeFactorChange { get; }

    public double AmplificationGrowthRate { get; }

    public double MomentumResidual { get; }

    public double ShapeResidual { get; }

    public double SkinFrictionResidual { get; }

    public double AmplificationResidual { get; }
}

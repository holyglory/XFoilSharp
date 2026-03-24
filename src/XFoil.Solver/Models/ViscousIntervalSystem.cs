// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: assembled interval-system lineage
// Role in port: Managed container for the full interval-system view used by older surrogate viscous workflows.
// Differences: Legacy XFoil keeps the same information in solver work arrays instead of returning one immutable object.
// Decision: Keep the managed container because it makes older workflow states inspectable.
namespace XFoil.Solver.Models;

public sealed class ViscousIntervalSystem
{
    public ViscousIntervalSystem(
        ViscousStateEstimate state,
        BoundaryLayerCorrelationConstants constants,
        IReadOnlyList<ViscousIntervalState> upperSurfaceIntervals,
        IReadOnlyList<ViscousIntervalState> lowerSurfaceIntervals,
        IReadOnlyList<ViscousIntervalState> wakeIntervals)
    {
        State = state;
        Constants = constants;
        UpperSurfaceIntervals = upperSurfaceIntervals;
        LowerSurfaceIntervals = lowerSurfaceIntervals;
        WakeIntervals = wakeIntervals;
    }

    public ViscousStateEstimate State { get; }

    public BoundaryLayerCorrelationConstants Constants { get; }

    public IReadOnlyList<ViscousIntervalState> UpperSurfaceIntervals { get; }

    public IReadOnlyList<ViscousIntervalState> LowerSurfaceIntervals { get; }

    public IReadOnlyList<ViscousIntervalState> WakeIntervals { get; }
}

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

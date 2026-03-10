namespace XFoil.Solver.Models;

public sealed class ViscousBranchState
{
    public ViscousBranchState(
        BoundaryLayerBranch branch,
        IReadOnlyList<ViscousStationState> stations)
    {
        Branch = branch;
        Stations = stations;
    }

    public BoundaryLayerBranch Branch { get; }

    public IReadOnlyList<ViscousStationState> Stations { get; }
}

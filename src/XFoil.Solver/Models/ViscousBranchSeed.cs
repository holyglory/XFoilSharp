namespace XFoil.Solver.Models;

public sealed class ViscousBranchSeed
{
    public ViscousBranchSeed(
        BoundaryLayerBranch branch,
        IReadOnlyList<ViscousStationSeed> stations)
    {
        Branch = branch;
        Stations = stations;
    }

    public BoundaryLayerBranch Branch { get; }

    public IReadOnlyList<ViscousStationSeed> Stations { get; }
}

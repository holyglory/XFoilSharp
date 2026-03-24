// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: per-branch viscous state arrays
// Role in port: Managed container for one solved viscous branch and its ordered stations.
// Differences: The managed port packages branch data into an immutable object instead of leaving it in parallel arrays.
// Decision: Keep the managed DTO because it simplifies result transport and testing.
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

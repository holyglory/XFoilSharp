// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: COMSET/MRCHUE seed lineage
// Role in port: Managed container for the seed stations on one boundary-layer branch.
// Differences: Legacy XFoil stores branch seed data in surface and wake arrays, while the managed port returns an explicit branch object.
// Decision: Keep the managed DTO because it clarifies seed ownership and branch identity.
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

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: per-station XI/UEDG state arrays
// Role in port: Managed DTO for one boundary-layer station location and primary edge-velocity data.
// Differences: Classic XFoil stores station values in parallel REAL arrays, while the managed port uses an explicit object per station.
// Decision: Keep the managed DTO because it improves readability and result transport.
using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class BoundaryLayerStation
{
    public BoundaryLayerStation(
        BoundaryLayerBranch branch,
        int index,
        AirfoilPoint location,
        double distanceFromStagnation,
        double edgeVelocity)
    {
        Branch = branch;
        Index = index;
        Location = location;
        DistanceFromStagnation = distanceFromStagnation;
        EdgeVelocity = edgeVelocity;
    }

    public BoundaryLayerBranch Branch { get; }

    public int Index { get; }

    public AirfoilPoint Location { get; }

    public double DistanceFromStagnation { get; }

    public double EdgeVelocity { get; }
}

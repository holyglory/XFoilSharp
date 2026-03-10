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

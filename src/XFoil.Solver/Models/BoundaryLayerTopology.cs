// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: STFIND/IBLPAN, f_xfoil/src/xbl.f :: IBLSYS topology lineage
// Role in port: Managed summary of stagnation location plus upper, lower, and wake station topologies.
// Differences: Legacy XFoil reconstructs this view implicitly from arrays and indices, while the managed port packages it into an immutable result object.
// Decision: Keep the managed topology result because it is useful for testing and diagnostics.
using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class BoundaryLayerTopology
{
    public BoundaryLayerTopology(
        AirfoilPoint stagnationPoint,
        double stagnationArcLength,
        int stagnationPanelIndex,
        IReadOnlyList<BoundaryLayerStation> upperSurfaceStations,
        IReadOnlyList<BoundaryLayerStation> lowerSurfaceStations,
        IReadOnlyList<BoundaryLayerStation> wakeStations)
    {
        StagnationPoint = stagnationPoint;
        StagnationArcLength = stagnationArcLength;
        StagnationPanelIndex = stagnationPanelIndex;
        UpperSurfaceStations = upperSurfaceStations;
        LowerSurfaceStations = lowerSurfaceStations;
        WakeStations = wakeStations;
    }

    public AirfoilPoint StagnationPoint { get; }

    public double StagnationArcLength { get; }

    public int StagnationPanelIndex { get; }

    public IReadOnlyList<BoundaryLayerStation> UpperSurfaceStations { get; }

    public IReadOnlyList<BoundaryLayerStation> LowerSurfaceStations { get; }

    public IReadOnlyList<BoundaryLayerStation> WakeStations { get; }
}

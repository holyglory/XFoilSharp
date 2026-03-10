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

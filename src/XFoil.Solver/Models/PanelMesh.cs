using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class PanelMesh
{
    public PanelMesh(
        IReadOnlyList<AirfoilPoint> nodes,
        IReadOnlyList<Panel> panels,
        bool isCounterClockwise)
    {
        Nodes = nodes;
        Panels = panels;
        IsCounterClockwise = isCounterClockwise;
    }

    public IReadOnlyList<AirfoilPoint> Nodes { get; }

    public IReadOnlyList<Panel> Panels { get; }

    public bool IsCounterClockwise { get; }
}

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: node/panel geometry buffers
// Role in port: Managed immutable airfoil mesh combining nodes, panels, and orientation metadata.
// Differences: Classic XFoil keeps nodes, panel geometry, and orientation flags in separate arrays, while the managed port returns them as one explicit mesh object.
// Decision: Keep the managed mesh container because it is the right API contract for geometry consumers.
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

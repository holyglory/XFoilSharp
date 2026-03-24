// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: panel-endpoint and normal/tangent geometry arrays
// Role in port: Managed DTO for one geometric panel on the airfoil surface.
// Differences: Legacy XFoil stores panel geometry in parallel arrays, while the managed port packages it into an explicit object per panel.
// Decision: Keep the managed DTO because it improves readability and interoperability.
using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class Panel
{
    public Panel(
        int index,
        AirfoilPoint start,
        AirfoilPoint end,
        AirfoilPoint controlPoint,
        double length,
        double tangentX,
        double tangentY,
        double normalX,
        double normalY)
    {
        Index = index;
        Start = start;
        End = end;
        ControlPoint = controlPoint;
        Length = length;
        TangentX = tangentX;
        TangentY = tangentY;
        NormalX = normalX;
        NormalY = normalY;
    }

    public int Index { get; }

    public AirfoilPoint Start { get; }

    public AirfoilPoint End { get; }

    public AirfoilPoint ControlPoint { get; }

    public double Length { get; }

    public double TangentX { get; }

    public double TangentY { get; }

    public double NormalX { get; }

    public double NormalY { get; }
}

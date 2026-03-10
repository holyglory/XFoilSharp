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

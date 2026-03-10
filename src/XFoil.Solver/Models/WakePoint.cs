using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class WakePoint
{
    public WakePoint(
        AirfoilPoint location,
        double tangentX,
        double tangentY,
        double distanceFromTrailingEdge,
        double velocityMagnitude)
    {
        Location = location;
        TangentX = tangentX;
        TangentY = tangentY;
        DistanceFromTrailingEdge = distanceFromTrailingEdge;
        VelocityMagnitude = velocityMagnitude;
    }

    public AirfoilPoint Location { get; }

    public double TangentX { get; }

    public double TangentY { get; }

    public double DistanceFromTrailingEdge { get; }

    public double VelocityMagnitude { get; }
}

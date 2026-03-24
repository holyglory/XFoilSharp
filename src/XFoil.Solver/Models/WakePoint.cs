// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: wake-point coordinate and tangent arrays
// Role in port: Managed DTO for one wake point with position, tangent, distance, and velocity magnitude.
// Differences: The managed port packages wake geometry into explicit point objects instead of parallel arrays.
// Decision: Keep the managed DTO because it improves diagnostics and downstream use.
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

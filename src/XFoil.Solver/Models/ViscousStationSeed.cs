using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class ViscousStationSeed
{
    public ViscousStationSeed(
        int index,
        AirfoilPoint location,
        double xi,
        double edgeVelocity,
        double wakeGap)
    {
        Index = index;
        Location = location;
        Xi = xi;
        EdgeVelocity = edgeVelocity;
        WakeGap = wakeGap;
    }

    public int Index { get; }

    public AirfoilPoint Location { get; }

    public double Xi { get; }

    public double EdgeVelocity { get; }

    public double WakeGap { get; }
}

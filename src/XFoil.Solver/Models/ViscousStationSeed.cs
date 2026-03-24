// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: MRCHUE seed-station lineage
// Role in port: Managed DTO for one seed station before the full viscous state is solved.
// Differences: Legacy XFoil keeps the same information in seed arrays, while the managed port packages it into an immutable point object.
// Decision: Keep the managed DTO because it simplifies seed tracing and testing.
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

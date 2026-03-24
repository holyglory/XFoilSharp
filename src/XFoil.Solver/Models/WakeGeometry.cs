// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: wake geometry arrays
// Role in port: Managed immutable container for the generated wake centerline points.
// Differences: Legacy XFoil stores wake geometry in arrays and plot buffers, while the managed port packages it into a single object.
// Decision: Keep the managed DTO because it is the clean API contract for wake consumers.
namespace XFoil.Solver.Models;

public sealed class WakeGeometry
{
    public WakeGeometry(IReadOnlyList<WakePoint> points)
    {
        Points = points;
    }

    public IReadOnlyList<WakePoint> Points { get; }
}

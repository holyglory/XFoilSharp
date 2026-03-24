// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xqdes.f :: QSPEC state
// Role in port: Managed container for one named QSpec profile.
// Differences: Legacy XFoil keeps the same information in command-local arrays and globals rather than one immutable profile object.
// Decision: Keep the managed profile object because it is the right service/output boundary.
namespace XFoil.Design.Models;

public sealed class QSpecProfile
{
    // Legacy mapping: none; this constructor packages a QSpec profile that legacy XFoil would leave in shared state.
    // Difference from legacy: The managed port validates the profile payload and freezes the sampled points.
    // Decision: Keep the managed container because it is clearer for callers and exporters.
    public QSpecProfile(
        string name,
        double angleOfAttackDegrees,
        double machNumber,
        IReadOnlyList<QSpecPoint> points)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AngleOfAttackDegrees = angleOfAttackDegrees;
        MachNumber = machNumber;
        Points = points ?? throw new ArgumentNullException(nameof(points));
    }

    public string Name { get; }

    public double AngleOfAttackDegrees { get; }

    public double MachNumber { get; }

    public IReadOnlyList<QSpecPoint> Points { get; }
}

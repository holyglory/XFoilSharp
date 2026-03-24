// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/aread.f :: AREAD, f_xfoil/src/naca.f :: NACA4/NACA5
// Role in port: Managed container for parsed or generated airfoil geometry and its file-format metadata.
// Differences: Legacy XFoil spreads the same state across COMMON arrays, name strings, and ITYPE flags instead of one immutable object.
// Decision: Keep the managed container because it gives the parser, generators, and solver a stable handoff structure; no parity-only branch is needed here.
namespace XFoil.Core.Models;

public sealed class AirfoilGeometry
{
    // Legacy mapping: none; this constructor packages geometry state that legacy XFoil keeps in separate arrays and flags.
    // Difference from legacy: The managed port validates the payload up front and stores immutable copies instead of mutating COMMON buffers in place.
    // Decision: Keep the managed validation and immutable storage because this is infrastructure rather than a parity-sensitive solver path.
    public AirfoilGeometry(
        string name,
        IReadOnlyList<AirfoilPoint> points,
        AirfoilFormat format,
        IReadOnlyList<double>? domainParameters = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An airfoil name is required.", nameof(name));
        }

        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 3)
        {
            throw new ArgumentException("At least three points are required to describe an airfoil.", nameof(points));
        }

        Name = name;
        Points = points.ToArray();
        Format = format;
        DomainParameters = domainParameters?.ToArray() ?? Array.Empty<double>();
    }

    public string Name { get; }

    public IReadOnlyList<AirfoilPoint> Points { get; }

    public AirfoilFormat Format { get; }

    public IReadOnlyList<double> DomainParameters { get; }
}

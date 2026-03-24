// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one reference-polar block parsed from comparison data files.
// Differences: No direct Fortran analogue exists because reference-polar comparison files are an external test/input artifact rather than a legacy runtime structure.
// Decision: Keep the managed DTO because it provides a stable representation for importers and tests.
namespace XFoil.IO.Models;

public sealed class LegacyReferencePolarBlock
{
    // Legacy mapping: none; managed-only constructor for one reference-polar block.
    // Difference from legacy: The original legacy runtime did not define this comparison-file object.
    // Decision: Keep the managed constructor because it validates imported block content early.
    public LegacyReferencePolarBlock(
        LegacyReferencePolarBlockKind kind,
        IReadOnlyList<LegacyReferencePolarPoint> points)
    {
        Kind = kind;
        Points = points ?? throw new ArgumentNullException(nameof(points));
    }

    public LegacyReferencePolarBlockKind Kind { get; }

    public IReadOnlyList<LegacyReferencePolarPoint> Points { get; }
}

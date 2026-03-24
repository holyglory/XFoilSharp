// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one side of a legacy polar-dump operating point.
// Differences: No direct Fortran analogue exists because the legacy dump format was streamed as lines, not modeled as nested side objects.
// Decision: Keep the managed DTO because it makes imported dump data inspectable and testable.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpSide
{
    // Legacy mapping: none; managed-only constructor for one dump side section.
    // Difference from legacy: The Fortran writer emitted this data procedurally, while the port validates it when constructing the DTO.
    // Decision: Keep the managed constructor because it makes malformed imported data fail early.
    public LegacyPolarDumpSide(
        int sideIndex,
        int leadingEdgeIndex,
        int trailingEdgeIndex,
        IReadOnlyList<LegacyPolarDumpSideSample> samples)
    {
        SideIndex = sideIndex;
        LeadingEdgeIndex = leadingEdgeIndex;
        TrailingEdgeIndex = trailingEdgeIndex;
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
    }

    public int SideIndex { get; }

    public int LeadingEdgeIndex { get; }

    public int TrailingEdgeIndex { get; }

    public IReadOnlyList<LegacyPolarDumpSideSample> Samples { get; }
}

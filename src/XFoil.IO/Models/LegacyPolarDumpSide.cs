namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpSide
{
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

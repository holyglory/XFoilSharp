namespace XFoil.IO.Models;

public sealed class LegacyReferencePolarBlock
{
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

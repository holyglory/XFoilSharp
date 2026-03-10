namespace XFoil.IO.Models;

public sealed class LegacyReferencePolarFile
{
    public LegacyReferencePolarFile(
        string label,
        IReadOnlyList<LegacyReferencePolarBlock> blocks)
    {
        Label = label ?? string.Empty;
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public string Label { get; }

    public IReadOnlyList<LegacyReferencePolarBlock> Blocks { get; }
}

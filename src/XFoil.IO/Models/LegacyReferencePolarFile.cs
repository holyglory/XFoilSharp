// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO representing one imported reference-polar comparison file.
// Differences: No direct Fortran analogue exists because this file format belongs to the managed verification/tooling layer rather than the legacy runtime.
// Decision: Keep the managed DTO because it provides a stable representation for importers and tests.
namespace XFoil.IO.Models;

public sealed class LegacyReferencePolarFile
{
    // Legacy mapping: none; managed-only constructor for one imported reference-polar file.
    // Difference from legacy: The legacy runtime did not materialize comparison files as a typed object.
    // Decision: Keep the managed constructor because it validates imported file content.
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

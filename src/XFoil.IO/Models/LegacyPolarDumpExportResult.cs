// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO summarizing the paths written during legacy polar-dump export.
// Differences: No direct Fortran analogue exists because the legacy dump workflow wrote files procedurally and did not return a structured export result.
// Decision: Keep the managed DTO because it is useful for automation and tests.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpExportResult
{
    // Legacy mapping: none; managed-only constructor validating one export-result object for a legacy dump.
    // Difference from legacy: The Fortran workflow reported output files implicitly, while the port validates explicit path fields.
    // Decision: Keep the managed constructor because it makes the exporter contract explicit.
    public LegacyPolarDumpExportResult(
        string summaryPath,
        string geometryPath,
        IReadOnlyList<string> sidePaths)
    {
        SummaryPath = summaryPath ?? throw new ArgumentNullException(nameof(summaryPath));
        GeometryPath = geometryPath ?? throw new ArgumentNullException(nameof(geometryPath));
        SidePaths = sidePaths ?? throw new ArgumentNullException(nameof(sidePaths));
    }

    public string SummaryPath { get; }

    public string GeometryPath { get; }

    public IReadOnlyList<string> SidePaths { get; }
}

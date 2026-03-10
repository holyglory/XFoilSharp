namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpExportResult
{
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

using System.Globalization;
using System.Text;
using XFoil.IO.Models;

// Legacy audit:
// Primary legacy source: none
// Role in port: Managed exporter that converts one imported legacy polar dump into a human-readable CSV archive bundle.
// Differences: No direct Fortran analogue exists because the legacy runtime emitted the original unformatted dump, not this derived archive representation.
// Decision: Keep the managed exporter because it is an intentional tooling layer on top of the legacy dump format.
namespace XFoil.IO.Services;

public sealed class LegacyPolarDumpArchiveWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    // Legacy mapping: none; managed-only archive writer for one imported legacy polar dump.
    // Difference from legacy: The original runtime wrote the binary dump itself, while this service materializes derived CSV files and a summary bundle.
    // Decision: Keep the managed exporter because it is useful tooling rather than a parity target.
    public LegacyPolarDumpExportResult Export(string summaryPath, LegacyPolarDumpFile dump)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
        {
            throw new ArgumentException("A summary path is required.", nameof(summaryPath));
        }

        if (dump is null)
        {
            throw new ArgumentNullException(nameof(dump));
        }

        var fullSummaryPath = Path.GetFullPath(summaryPath);
        var directory = Path.GetDirectoryName(fullSummaryPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var basePath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(fullSummaryPath));
        var summaryLines = new List<string>
        {
            "# XFoil.CSharp Polar Dump Export",
            $"# Airfoil: {dump.AirfoilName}",
            $"# SourceCode: {dump.SourceCode}",
            $"# Version: {FormatDouble(dump.Version)}",
            $"# IsIsesPolar: {FormatBoolean(dump.IsIsesPolar)}",
            $"# IsMachSweep: {FormatBoolean(dump.IsMachSweep)}",
            $"# ReferenceMachNumber: {FormatDouble(dump.ReferenceMachNumber)}",
            $"# ReferenceReynoldsNumber: {FormatDouble(dump.ReferenceReynoldsNumber)}",
            $"# CriticalAmplificationFactor: {FormatDouble(dump.CriticalAmplificationFactor)}",
            "PointIndex,AngleOfAttackDegrees,LiftCoefficient,DragCoefficient,StoredDragComponentCoefficient,MomentCoefficientQuarterChord,TopTransition,BottomTransition,MachNumber,UpperSampleCount,LowerSampleCount",
        };

        // Legacy block: Managed-only summary-table export over imported dump operating points.
        // Difference: This CSV summary does not exist in the original runtime; it is derived from the parsed dump object.
        // Decision: Keep the managed loop because it makes dump contents inspectable.
        for (var index = 0; index < dump.OperatingPoints.Count; index++)
        {
            var point = dump.OperatingPoints[index];
            summaryLines.Add(string.Join(
                ',',
                index + 1,
                FormatDouble(point.AngleOfAttackDegrees),
                FormatDouble(point.LiftCoefficient),
                FormatDouble(point.DragCoefficient),
                FormatDouble(point.StoredDragComponentCoefficient),
                FormatDouble(point.MomentCoefficientQuarterChord),
                FormatDouble(point.TopTransition),
                FormatDouble(point.BottomTransition),
                FormatDouble(point.MachNumber),
                point.Sides[0].Samples.Count.ToString(CultureInfo.InvariantCulture),
                point.Sides[1].Samples.Count.ToString(CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(fullSummaryPath, string.Join('\n', summaryLines) + "\n", Utf8WithoutBom);

        var geometryPath = $"{basePath}.geometry.csv";
        var geometryLines = new List<string>
        {
            "PointIndex,X,Y",
        };

        // Legacy block: Managed-only geometry archive export from the imported dump.
        // Difference: The original binary dump stored these coordinates in unformatted records rather than as CSV.
        // Decision: Keep the managed loop because it is a tooling convenience.
        for (var index = 0; index < dump.Geometry.Count; index++)
        {
            var point = dump.Geometry[index];
            geometryLines.Add($"{index + 1},{FormatDouble(point.X)},{FormatDouble(point.Y)}");
        }

        File.WriteAllText(geometryPath, string.Join('\n', geometryLines) + "\n", Utf8WithoutBom);

        var sidePaths = new List<string>();
        // Legacy block: Managed-only per-side CSV archive generation for each operating point.
        // Difference: The original runtime emitted a compact binary record stream rather than one CSV file per side.
        // Decision: Keep the managed exporter because it provides a readable archive format.
        for (var pointIndex = 0; pointIndex < dump.OperatingPoints.Count; pointIndex++)
        {
            var point = dump.OperatingPoints[pointIndex];
            foreach (var side in point.Sides)
            {
                var sidePath = $"{basePath}.point{pointIndex + 1:D3}.side{side.SideIndex}.csv";
                var sideLines = new List<string>
                {
                    $"# LeadingEdgeIndex: {side.LeadingEdgeIndex}",
                    $"# TrailingEdgeIndex: {side.TrailingEdgeIndex}",
                    "SampleIndex,X,PressureCoefficient,MomentumThickness,DisplacementThickness,SkinFrictionCoefficient,ShearLagCoefficient",
                };

                for (var sampleIndex = 0; sampleIndex < side.Samples.Count; sampleIndex++)
                {
                    var sample = side.Samples[sampleIndex];
                    sideLines.Add(string.Join(
                        ',',
                        sampleIndex + 1,
                        FormatDouble(sample.X),
                        FormatDouble(sample.PressureCoefficient),
                        FormatDouble(sample.MomentumThickness),
                        FormatDouble(sample.DisplacementThickness),
                        FormatDouble(sample.SkinFrictionCoefficient),
                        FormatDouble(sample.ShearLagCoefficient)));
                }

                File.WriteAllText(sidePath, string.Join('\n', sideLines) + "\n", Utf8WithoutBom);
                sidePaths.Add(sidePath);
            }
        }

        return new LegacyPolarDumpExportResult(fullSummaryPath, geometryPath, sidePaths);
    }

    // Legacy mapping: none; managed-only CSV-formatting helper.
    // Difference from legacy: The archive writer chooses a fixed managed decimal format for readability.
    // Decision: Keep the managed helper.
    private static string FormatDouble(double value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    // Legacy mapping: none; managed-only CSV-formatting helper.
    // Difference from legacy: Boolean values are normalized to lowercase strings for archive readability.
    // Decision: Keep the managed helper.
    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }
}

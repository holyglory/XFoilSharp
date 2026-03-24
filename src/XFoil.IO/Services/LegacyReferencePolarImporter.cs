using System.Globalization;
using XFoil.IO.Models;

// Legacy audit:
// Primary legacy source: none
// Role in port: Managed importer for external reference-polar comparison files used by tests and tooling.
// Differences: No direct Fortran analogue exists because these reference files are part of the managed verification layer rather than the legacy runtime.
// Decision: Keep the managed importer because it is useful tooling and not a parity target.
namespace XFoil.IO.Services;

public sealed class LegacyReferencePolarImporter
{
    // Legacy mapping: none; managed-only parser for external reference-polar comparison files.
    // Difference from legacy: The original runtime did not define this file format or importer.
    // Decision: Keep the managed importer because it supports regression comparisons and tests.
    public LegacyReferencePolarFile Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A reference polar path is required.", nameof(path));
        }

        using var reader = new StreamReader(path);
        string? label = null;
        var firstLine = reader.ReadLine();
        if (firstLine is not null)
        {
            if (firstLine.StartsWith('#'))
            {
                label = firstLine[1..].Trim();
            }
            else
            {
                reader.DiscardBufferedData();
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
            }
        }

        var blocks = new List<LegacyReferencePolarBlock>();
        // Legacy block: Managed-only block-by-block parse of the comparison file’s four data sections.
        // Difference: This parser is specific to the managed verification layer and has no legacy runtime twin.
        // Decision: Keep the managed loop because it materializes the external file cleanly.
        for (var blockIndex = 0; blockIndex < 4; blockIndex++)
        {
            var points = new List<LegacyReferencePolarPoint>();
            while (true)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var x = double.Parse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                var y = double.Parse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                if (x == 999d)
                {
                    break;
                }

                points.Add(new LegacyReferencePolarPoint(x, y));
            }

            blocks.Add(new LegacyReferencePolarBlock((LegacyReferencePolarBlockKind)blockIndex, points));
        }

        return new LegacyReferencePolarFile(label ?? string.Empty, blocks);
    }
}

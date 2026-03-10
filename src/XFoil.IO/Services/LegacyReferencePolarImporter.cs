using System.Globalization;
using XFoil.IO.Models;

namespace XFoil.IO.Services;

public sealed class LegacyReferencePolarImporter
{
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

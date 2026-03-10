using System.Globalization;
using System.Text.RegularExpressions;
using XFoil.IO.Models;

namespace XFoil.IO.Services;

public sealed partial class LegacyPolarImporter
{
    [GeneratedRegex(@"[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)(?:[Ee][-+]?\d+)?", RegexOptions.Compiled)]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"^\s*(?<code>.+?)\s+Version\s+(?<version>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s*$", RegexOptions.Compiled)]
    private static partial Regex VersionLineRegex();

    [GeneratedRegex(@"Calculated polar for:\s*(?<name>.*?)(?:\s+(?<elements>\d+)\s+elements)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NameLineRegex();

    [GeneratedRegex(@"^\s*(?<reType>\d)\s+(?<maType>\d)\b", RegexOptions.Compiled)]
    private static partial Regex VariationLineRegex();

    [GeneratedRegex(@"xtrf\s*=\s*(?<top>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s+\(top\)\s+(?<bottom>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s+\(bottom\)(?:\s+element\s*(?<element>\d+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TripLineRegex();

    [GeneratedRegex(@"Mach\s*=\s*(?<mach>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)).*?Re\s*=\s*(?<re>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s*e\s*6.*?Ncrit\s*=\s*(?<ncrit>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex MainParameterLineRegex();

    [GeneratedRegex(@"pi_p\s*=\s*(?<ptrat>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)).*?eta_p\s*=\s*(?<etap>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PropulsorLineRegex();

    public LegacyPolarFile Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A legacy polar path is required.", nameof(path));
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Legacy polar file is empty.");
        }

        var sourceCode = string.Empty;
        double? version = null;
        var airfoilName = string.Empty;
        var elementCount = 1;
        var reynoldsVariationType = LegacyReynoldsVariationType.Unspecified;
        var machVariationType = LegacyMachVariationType.Unspecified;
        double? referenceMachNumber = null;
        double? referenceReynoldsNumber = null;
        double? criticalAmplificationFactor = null;
        double? pressureRatio = null;
        double? thermalEfficiency = null;
        var tripSettings = new List<LegacyPolarTripSetting>();
        var headerLabelIndex = -1;
        var dataSeparatorIndex = -1;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Contains("-----", StringComparison.Ordinal))
            {
                headerLabelIndex = index - 1;
                dataSeparatorIndex = index;
                break;
            }

            var versionMatch = VersionLineRegex().Match(line);
            if (versionMatch.Success)
            {
                sourceCode = versionMatch.Groups["code"].Value.Trim();
                version = ParseDouble(versionMatch.Groups["version"].Value);
                continue;
            }

            var nameMatch = NameLineRegex().Match(line);
            if (nameMatch.Success)
            {
                airfoilName = nameMatch.Groups["name"].Value.Trim();
                if (nameMatch.Groups["elements"].Success)
                {
                    elementCount = ParseInt(nameMatch.Groups["elements"].Value);
                }

                continue;
            }

            var variationMatch = VariationLineRegex().Match(line);
            if (variationMatch.Success)
            {
                reynoldsVariationType = (LegacyReynoldsVariationType)ParseInt(variationMatch.Groups["reType"].Value);
                machVariationType = (LegacyMachVariationType)ParseInt(variationMatch.Groups["maType"].Value);
                continue;
            }

            var tripMatch = TripLineRegex().Match(line);
            if (tripMatch.Success)
            {
                var elementIndex = tripMatch.Groups["element"].Success ? ParseInt(tripMatch.Groups["element"].Value) : tripSettings.Count + 1;
                tripSettings.Add(new LegacyPolarTripSetting(
                    elementIndex,
                    ParseDouble(tripMatch.Groups["top"].Value),
                    ParseDouble(tripMatch.Groups["bottom"].Value)));
                continue;
            }

            var mainParameterMatch = MainParameterLineRegex().Match(line);
            if (mainParameterMatch.Success)
            {
                referenceMachNumber = ParseDouble(mainParameterMatch.Groups["mach"].Value);
                referenceReynoldsNumber = ParseDouble(mainParameterMatch.Groups["re"].Value) * 1_000_000d;
                criticalAmplificationFactor = ParseDouble(mainParameterMatch.Groups["ncrit"].Value);
                continue;
            }

            var propulsorMatch = PropulsorLineRegex().Match(line);
            if (propulsorMatch.Success)
            {
                pressureRatio = ParseDouble(propulsorMatch.Groups["ptrat"].Value);
                thermalEfficiency = ParseDouble(propulsorMatch.Groups["etap"].Value);
            }
        }

        if (headerLabelIndex < 0 || dataSeparatorIndex < 0)
        {
            throw new InvalidOperationException("Legacy polar file does not contain a recognizable column header.");
        }

        var headerLine = lines[headerLabelIndex];
        var separatorLine = lines[dataSeparatorIndex];
        var columns = ParseColumns(headerLine, separatorLine);
        var records = ParseRecords(lines, dataSeparatorIndex + 1, columns);
        ApplyLegacyDefaults(
            columns,
            records,
            reynoldsVariationType,
            machVariationType,
            referenceReynoldsNumber,
            referenceMachNumber,
            criticalAmplificationFactor,
            tripSettings,
            elementCount);

        return new LegacyPolarFile(
            sourceCode,
            version,
            airfoilName,
            elementCount,
            reynoldsVariationType,
            machVariationType,
            referenceMachNumber,
            referenceReynoldsNumber,
            criticalAmplificationFactor,
            pressureRatio,
            thermalEfficiency,
            tripSettings,
            columns,
            records);
    }

    private static List<LegacyPolarColumn> ParseColumns(string headerLine, string separatorLine)
    {
        var columns = new List<LegacyPolarColumn>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(separatorLine, "-+");
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var spanStart = match.Index;
            var spanLength = match.Length;
            var safeLength = Math.Max(0, Math.Min(spanLength, headerLine.Length - spanStart));
            var label = safeLength > 0
                ? headerLine.Substring(spanStart, safeLength).Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Column {index + 1}";
            }

            var baseKey = NormalizeKey(label);
            if (!counts.TryGetValue(baseKey, out var occurrence))
            {
                occurrence = 0;
            }

            occurrence++;
            counts[baseKey] = occurrence;
            var key = occurrence == 1 ? baseKey : $"{baseKey}_{occurrence}";
            columns.Add(new LegacyPolarColumn(key, label, index));
        }

        return columns;
    }

    private static IReadOnlyList<LegacyPolarRecord> ParseRecords(IReadOnlyList<string> lines, int startIndex, IReadOnlyList<LegacyPolarColumn> columns)
    {
        var records = new List<LegacyPolarRecord>();
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var matches = FloatRegex().Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Count < columns.Count)
            {
                throw new InvalidOperationException($"Legacy polar data row {index + 1} contains {matches.Count} values, expected {columns.Count}.");
            }

            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                values[columns[columnIndex].Key] = ParseDouble(matches[columnIndex].Value);
            }

            records.Add(new LegacyPolarRecord(values));
        }

        return records;
    }

    private static void ApplyLegacyDefaults(
        List<LegacyPolarColumn> columns,
        IReadOnlyList<LegacyPolarRecord> records,
        LegacyReynoldsVariationType reynoldsVariationType,
        LegacyMachVariationType machVariationType,
        double? referenceReynoldsNumber,
        double? referenceMachNumber,
        double? criticalAmplificationFactor,
        IReadOnlyList<LegacyPolarTripSetting> tripSettings,
        int elementCount)
    {
        var keyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            var baseKey = StripDuplicateSuffix(column.Key);
            if (!keyCounts.TryGetValue(baseKey, out var count))
            {
                count = 0;
            }

            keyCounts[baseKey] = Math.Max(count, GetDuplicateIndex(column.Key));
        }

        var hasRe = columns.Any(column => column.Key.Equals("Re", StringComparison.OrdinalIgnoreCase));
        var hasMach = columns.Any(column => column.Key.Equals("Mach", StringComparison.OrdinalIgnoreCase));
        var hasNcrit = columns.Any(column => column.Key.Equals("Ncrit", StringComparison.OrdinalIgnoreCase));

        if (!hasRe && referenceReynoldsNumber.HasValue)
        {
            columns.Add(new LegacyPolarColumn(CreateUniqueKey("Re", keyCounts), "Re", columns.Count));
            foreach (var record in records)
            {
                var cl = Math.Max(GetValue(record, "CL", 0.001d), 0.001d);
                record.SetValue("Re", reynoldsVariationType switch
                {
                    LegacyReynoldsVariationType.Fixed => referenceReynoldsNumber.Value,
                    LegacyReynoldsVariationType.InverseSqrtCl => referenceReynoldsNumber.Value / Math.Sqrt(cl),
                    LegacyReynoldsVariationType.InverseCl => referenceReynoldsNumber.Value / cl,
                    _ => referenceReynoldsNumber.Value,
                });
            }
        }

        if (!hasMach && referenceMachNumber.HasValue)
        {
            columns.Add(new LegacyPolarColumn(CreateUniqueKey("Mach", keyCounts), "Mach", columns.Count));
            foreach (var record in records)
            {
                var cl = Math.Max(GetValue(record, "CL", 0.001d), 0.001d);
                record.SetValue("Mach", machVariationType switch
                {
                    LegacyMachVariationType.Fixed => referenceMachNumber.Value,
                    LegacyMachVariationType.InverseSqrtCl => referenceMachNumber.Value / Math.Sqrt(cl),
                    LegacyMachVariationType.InverseCl => referenceMachNumber.Value / cl,
                    _ => referenceMachNumber.Value,
                });
            }
        }

        if (!hasNcrit && criticalAmplificationFactor.HasValue)
        {
            columns.Add(new LegacyPolarColumn(CreateUniqueKey("Ncrit", keyCounts), "Ncrit", columns.Count));
            foreach (var record in records)
            {
                record.SetValue("Ncrit", criticalAmplificationFactor.Value);
            }
        }

        for (var elementIndex = 1; elementIndex <= elementCount; elementIndex++)
        {
            var trip = tripSettings.FirstOrDefault(setting => setting.ElementIndex == elementIndex);
            if (trip is null)
            {
                continue;
            }

            var topKey = elementIndex == 1 ? "Top_Xtrip" : $"Top_Xtrip_{elementIndex}";
            var botKey = elementIndex == 1 ? "Bot_Xtrip" : $"Bot_Xtrip_{elementIndex}";
            if (!columns.Any(column => column.Key.Equals(topKey, StringComparison.OrdinalIgnoreCase)))
            {
                columns.Add(new LegacyPolarColumn(CreateUniqueKey(topKey, keyCounts), topKey, columns.Count));
                foreach (var record in records)
                {
                    record.SetValue(topKey, trip.TopTrip);
                }
            }

            if (!columns.Any(column => column.Key.Equals(botKey, StringComparison.OrdinalIgnoreCase)))
            {
                columns.Add(new LegacyPolarColumn(CreateUniqueKey(botKey, keyCounts), botKey, columns.Count));
                foreach (var record in records)
                {
                    record.SetValue(botKey, trip.BottomTrip);
                }
            }
        }
    }

    private static double GetValue(LegacyPolarRecord record, string key, double fallback)
    {
        return record.Values.TryGetValue(key, out var value) ? value : fallback;
    }

    private static string CreateUniqueKey(string preferredKey, IDictionary<string, int> counts)
    {
        var baseKey = StripDuplicateSuffix(preferredKey);
        if (!counts.TryGetValue(baseKey, out var count))
        {
            count = 0;
        }

        count++;
        counts[baseKey] = count;
        return count == 1 ? baseKey : $"{baseKey}_{count}";
    }

    private static string StripDuplicateSuffix(string key)
    {
        var match = Regex.Match(key, @"^(?<base>.+?)_(?<index>\d+)$");
        return match.Success ? match.Groups["base"].Value : key;
    }

    private static int GetDuplicateIndex(string key)
    {
        var match = Regex.Match(key, @"_(?<index>\d+)$");
        return match.Success ? ParseInt(match.Groups["index"].Value) : 1;
    }

    private static string NormalizeKey(string label)
    {
        var condensed = Regex.Replace(label.Trim(), @"\s+", "_");
        return condensed.Replace('/', '_');
    }

    private static double ParseDouble(string raw)
    {
        return double.Parse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string raw)
    {
        return int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}

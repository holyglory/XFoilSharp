using System.Globalization;
using System.Text.RegularExpressions;
using XFoil.IO.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: PACC/PWRT saved-polar output workflow
// Role in port: Managed importer for the legacy text polar files written by the original XFoil operating-point workflow.
// Differences: This code parses the legacy saved-polar text format into typed DTOs with explicit normalization and default reconstruction, rather than participating in the original runtime output path.
// Decision: Keep the managed importer because it is the correct compatibility layer for legacy saved polar files.
namespace XFoil.IO.Services;

public sealed partial class LegacyPolarImporter
{
    // Legacy mapping: none; managed-only regex factory for parsing floating-point tokens in saved polar text.
    // Difference from legacy: The original runtime emitted formatted text but did not expose a reusable token parser.
    // Decision: Keep the managed helper because it localizes the parsing rule.
    [GeneratedRegex(@"[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)(?:[Ee][-+]?\d+)?", RegexOptions.Compiled)]
    private static partial Regex FloatRegex();

    // Legacy mapping: none; managed-only regex factory for the saved-polar version/source line.
    // Difference from legacy: The original runtime wrote this line procedurally instead of parsing it back.
    // Decision: Keep the managed helper because it makes the format contract explicit.
    [GeneratedRegex(@"^\s*(?<code>.+?)\s+Version\s+(?<version>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s*$", RegexOptions.Compiled)]
    private static partial Regex VersionLineRegex();

    // Legacy mapping: none; managed-only regex factory for the saved-polar geometry/name line.
    // Difference from legacy: The original runtime emitted this header line but had no need for a parser helper.
    // Decision: Keep the managed helper.
    [GeneratedRegex(@"Calculated polar for:\s*(?<name>.*?)(?:\s+(?<elements>\d+)\s+elements)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NameLineRegex();

    // Legacy mapping: none; managed-only regex factory for the saved-polar variation-type header line.
    // Difference from legacy: This parser reconstructs typed variation metadata from emitted text.
    // Decision: Keep the managed helper.
    [GeneratedRegex(@"^\s*(?<reType>\d)\s+(?<maType>\d)\b", RegexOptions.Compiled)]
    private static partial Regex VariationLineRegex();

    // Legacy mapping: none; managed-only regex factory for trip-setting header lines in legacy polar files.
    // Difference from legacy: The old runtime emitted these values but did not parse them back into objects.
    // Decision: Keep the managed helper.
    [GeneratedRegex(@"xtrf\s*=\s*(?<top>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s+\(top\)\s+(?<bottom>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s+\(bottom\)(?:\s+element\s*(?<element>\d+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TripLineRegex();

    // Legacy mapping: none; managed-only regex factory for the Mach/Re/Ncrit header line produced by the saved-polar writer.
    // Difference from legacy: The original runtime wrote this line procedurally instead of exposing a parser.
    // Decision: Keep the managed helper.
    [GeneratedRegex(@"Mach\s*=\s*(?<mach>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)).*?Re\s*=\s*(?<re>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))\s*e\s*6.*?Ncrit\s*=\s*(?<ncrit>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex MainParameterLineRegex();

    // Legacy mapping: none; managed-only regex factory for propulsor-specific header lines in legacy polar files.
    // Difference from legacy: The original runtime emitted these values but did not parse them back into a reusable object model.
    // Decision: Keep the managed helper.
    [GeneratedRegex(@"pi_p\s*=\s*(?<ptrat>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)).*?eta_p\s*=\s*(?<etap>[-+]?(?:\d+\.\d*|\d*\.\d+|\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PropulsorLineRegex();

    // Legacy mapping: none directly; managed parser for the text format emitted by f_xfoil/src/xoper.f :: PACC/PWRT.
    // Difference from legacy: The original runtime wrote this text file, while the port reconstructs typed metadata, columns, and records from it.
    // Decision: Keep the managed importer because it is a compatibility feature, not a runtime parity path.
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

        // Legacy block: Managed-only header scan across the saved polar text file.
        // Difference: The importer recognizes the lines emitted by the original writer and converts them into structured metadata instead of just printing them.
        // Decision: Keep the managed scan because it centralizes the format contract.
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

    // Legacy mapping: none directly; managed reconstruction of saved-polar columns from the emitted header/separator lines.
    // Difference from legacy: The original runtime already knew the column layout when writing the file; the importer must infer it back from separator spans.
    // Decision: Keep the managed parser helper because it makes the textual format robustly recoverable.
    private static List<LegacyPolarColumn> ParseColumns(string headerLine, string separatorLine)
    {
        var columns = new List<LegacyPolarColumn>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(separatorLine, "-+");
        // Legacy block: Managed-only span-based reconstruction of polar column labels from the saved text header.
        // Difference: This inference step has no direct runtime twin because the original code was on the writing side of the format.
        // Decision: Keep the managed loop because it recovers the legacy column structure reliably.
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

    // Legacy mapping: none directly; managed parser for the saved-polar numeric table emitted by xoper.f.
    // Difference from legacy: The original runtime wrote these rows from in-memory data, while the importer tokenizes and reconstructs them into dictionaries.
    // Decision: Keep the managed parser helper because it provides a stable import path.
    private static IReadOnlyList<LegacyPolarRecord> ParseRecords(IReadOnlyList<string> lines, int startIndex, IReadOnlyList<LegacyPolarColumn> columns)
    {
        var records = new List<LegacyPolarRecord>();
        // Legacy block: Managed-only row-by-row parse of the saved-polar numeric table.
        // Difference: The importer rebuilds row objects from text instead of relying on the runtime data that originally produced the file.
        // Decision: Keep the managed loop because it is the core of the text import path.
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

    // Legacy mapping: none directly; managed reconstruction of omitted legacy defaults after parsing.
    // Difference from legacy: The original writer omitted some values when they were implicit in header metadata, while the importer recreates explicit per-record values and trip columns.
    // Decision: Keep the managed helper because it makes imported data self-contained and easier to consume.
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
        // Legacy block: Managed-only duplicate-key census before appending reconstructed columns.
        // Difference: This bookkeeping exists only because the importer rebuilds a normalized in-memory table.
        // Decision: Keep the managed loop because it preserves stable unique keys.
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
            // Legacy block: Managed-only reconstruction of per-record Reynolds numbers from legacy header metadata.
            // Difference: The writer could leave Reynolds implicit depending on variation mode; the importer expands it into explicit row values.
            // Decision: Keep the managed loop because it makes imported rows self-contained.
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
            // Legacy block: Managed-only reconstruction of per-record Mach numbers from legacy header metadata.
            // Difference: The importer expands a header-level legacy mode into explicit row values.
            // Decision: Keep the managed loop because it simplifies downstream use.
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
            // Legacy block: Managed-only propagation of a file-level `Ncrit` header value into explicit row data.
            // Difference: The original writer stored this as header metadata rather than a per-row field.
            // Decision: Keep the managed loop because it normalizes imported data.
            foreach (var record in records)
            {
                record.SetValue("Ncrit", criticalAmplificationFactor.Value);
            }
        }

        // Legacy block: Managed-only trip-setting expansion into explicit per-element trip columns.
        // Difference: The writer stored trip settings in header text, while the importer can reconstruct them into normalized row fields.
        // Decision: Keep the managed loop because it makes the imported table easier to consume programmatically.
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

    // Legacy mapping: none; managed-only row-lookup helper used by legacy-default reconstruction.
    // Difference from legacy: The writer had direct access to the runtime values, while the importer needs keyed row lookups.
    // Decision: Keep the managed helper.
    private static double GetValue(LegacyPolarRecord record, string key, double fallback)
    {
        return record.Values.TryGetValue(key, out var value) ? value : fallback;
    }

    // Legacy mapping: none; managed-only normalized-key allocator for imported column names.
    // Difference from legacy: The importer must synthesize stable unique keys from legacy labels after parsing.
    // Decision: Keep the managed helper because it centralizes duplicate handling.
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

    // Legacy mapping: none; managed-only helper for normalized imported column names.
    // Difference from legacy: Duplicate suffix handling is an importer concern rather than a runtime-output concern.
    // Decision: Keep the managed helper.
    private static string StripDuplicateSuffix(string key)
    {
        var match = Regex.Match(key, @"^(?<base>.+?)_(?<index>\d+)$");
        return match.Success ? match.Groups["base"].Value : key;
    }

    // Legacy mapping: none; managed-only helper for normalized imported column names.
    // Difference from legacy: The importer decodes duplicate suffixes explicitly to preserve stable keys.
    // Decision: Keep the managed helper.
    private static int GetDuplicateIndex(string key)
    {
        var match = Regex.Match(key, @"_(?<index>\d+)$");
        return match.Success ? ParseInt(match.Groups["index"].Value) : 1;
    }

    // Legacy mapping: none; managed-only label normalization helper for imported polar columns.
    // Difference from legacy: The writer emitted free-form labels, while the importer normalizes them into programmatic keys.
    // Decision: Keep the managed helper.
    private static string NormalizeKey(string label)
    {
        var condensed = Regex.Replace(label.Trim(), @"\s+", "_");
        return condensed.Replace('/', '_');
    }

    // Legacy mapping: none; managed-only numeric parser for legacy text fields.
    // Difference from legacy: The writer formatted doubles; the importer re-parses them under invariant culture.
    // Decision: Keep the managed helper.
    private static double ParseDouble(string raw)
    {
        return double.Parse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    // Legacy mapping: none; managed-only integer parser for legacy text fields.
    // Difference from legacy: The importer reconstructs integer metadata that was originally emitted as text.
    // Decision: Keep the managed helper.
    private static int ParseInt(string raw)
    {
        return int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}

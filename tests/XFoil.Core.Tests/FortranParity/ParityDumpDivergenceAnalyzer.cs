using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/compare_dumps.py dump-comparison workflow
// Secondary legacy source: tools/fortran-debug/report_final_gap.py final coefficient summary contract
// Role in port: Managed-only analyzer that turns reference/managed dump pairs into a compact first-divergence report for the parity harness.
// Differences: Classic XFoil had no self-comparison harness; this file ports the useful dump-level comparison shape into the managed test harness so ad hoc runs can report the first known mismatch without external scripts.
// Decision: Keep the analyzer in the test harness because it is parity infrastructure, not production solver behavior.
namespace XFoil.Core.Tests.FortranParity;

internal sealed record ParityDivergenceLocation(
    int Iteration,
    int Side,
    int Station,
    int SystemLine,
    string Category,
    string Detail);

internal sealed record ParityRunSummary(
    double? LiftCoefficient,
    double? DragCoefficient,
    double? MomentCoefficient,
    bool? Converged,
    int? Iterations);

internal sealed record ParityDivergenceReport(
    string ReferenceDumpPath,
    string ManagedDumpPath,
    double RelativeThreshold,
    ParityRunSummary ReferenceSummary,
    ParityRunSummary ManagedSummary,
    ParityDivergenceLocation? FirstDivergence,
    string? Note)
{
    public string ToDisplayText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== Final Gap ===");
        builder.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "reference CL={0} CD={1} CM={2}",
                FormatNumber(ReferenceSummary.LiftCoefficient),
                FormatNumber(ReferenceSummary.DragCoefficient, scientific: true),
                FormatNumber(ReferenceSummary.MomentCoefficient, scientific: true)));
        builder.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "managed   CL={0} CD={1} CM={2} converged={3} iter={4}",
                FormatNumber(ManagedSummary.LiftCoefficient),
                FormatNumber(ManagedSummary.DragCoefficient, scientific: true),
                FormatNumber(ManagedSummary.MomentCoefficient, scientific: true),
                ManagedSummary.Converged?.ToString() ?? "unknown",
                ManagedSummary.Iterations?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
        builder.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "delta     CL={0} CD={1} CM={2}",
                FormatDelta(ManagedSummary.LiftCoefficient, ReferenceSummary.LiftCoefficient),
                FormatDelta(ManagedSummary.DragCoefficient, ReferenceSummary.DragCoefficient),
                FormatDelta(ManagedSummary.MomentCoefficient, ReferenceSummary.MomentCoefficient)));

        builder.AppendLine();
        builder.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "=== First Divergence (threshold {0:P1}) ===",
                RelativeThreshold));

        if (FirstDivergence is null)
        {
            builder.AppendLine("No block-level divergence was localized from the parsed dump structure.");
        }
        else
        {
            builder.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "iter={0} side={1} station={2} iv={3} category={4}",
                    FirstDivergence.Iteration,
                    FirstDivergence.Side,
                    FirstDivergence.Station,
                    FirstDivergence.SystemLine,
                    FirstDivergence.Category));
            builder.AppendLine(FirstDivergence.Detail);
            builder.AppendLine(GetHint(FirstDivergence.Category));
        }

        if (!string.IsNullOrWhiteSpace(Note))
        {
            builder.AppendLine();
            builder.AppendLine("=== Notes ===");
            builder.AppendLine(Note);
        }

        builder.AppendLine();
        builder.AppendLine($"referenceDump={ReferenceDumpPath}");
        builder.AppendLine($"managedDump={ManagedDumpPath}");
        return builder.ToString();
    }

    private static string FormatNumber(double? value, bool scientific = false)
    {
        if (value is null)
        {
            return "n/a";
        }

        return value.Value.ToString(
            scientific ? "0.000000000e+00" : "0.000000000",
            CultureInfo.InvariantCulture);
    }

    private static string FormatDelta(double? managed, double? reference)
    {
        if (managed is null || reference is null)
        {
            return "n/a";
        }

        return (managed.Value - reference.Value).ToString("0.000000000e+00", CultureInfo.InvariantCulture);
    }

    private static string GetHint(string category)
    {
        return category switch
        {
            "BL_STATE" =>
                "Hint: the current-station state is already diverged here, so the next trace should move upstream to the producer of Ue/theta/dstar/mass before patching local residual assembly.",
            "VA" or "VB" =>
                "Hint: the primary station state still matches long enough to reach local Jacobian assembly, so the next focused rerun should trace the station-system terms feeding this block.",
            "VDEL_R" or "VDEL_S" =>
                "Hint: the divergence is in the assembled Newton update rows, so compare the local residual/Jacobian combine terms before changing any downstream updater logic.",
            "VSREZ" =>
                "Hint: the local residual vector is already diverged, so compare the block outputs and their immediate inputs before touching the solve/update path.",
            "SUMMARY" =>
                "Hint: only the final aerodynamic summary diverged in the parsed dump. Re-run with a focused trace selector to localize the earliest producer block.",
            _ =>
                "Hint: re-run with a focused trace window around this station and category, then compare inputs before patching outputs."
        };
    }
}

internal static class ParityDumpDivergenceAnalyzer
{
    private static readonly Regex IterationRegex = new(@"=== ITER\s+(\d+)\s*===", RegexOptions.Compiled);
    private static readonly Regex StationRegex = new(@"STATION IS=\s*(\d+)\s+IBL=\s*(\d+)\s+IV=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex BlStateRegex = new(
        @"BL_STATE\s+x=\s*([^\s]+)\s+Ue=\s*([^\s]+)\s+th=\s*([^\s]+)\s+ds=\s*([^\s]+)\s+m=\s*([^\s]+)",
        RegexOptions.Compiled);
    private static readonly Regex PostCalcRegex = new(
        @"POST_CALC\s+CL=\s*([^\s]+)\s+CD=\s*([^\s]+)\s+CM=\s*([^\s]+)",
        RegexOptions.Compiled);
    private static readonly Regex FinalRegex = new(
        @"FINAL\s+CL=\s*([^\s]+)\s+CD=\s*([^\s]+)\s+CM=\s*([^\s]+)\s+CONVERGED=(\w+)\s+ITER=(\d+)",
        RegexOptions.Compiled);
    private static readonly Regex FloatRegex = new(@"[+-]?\d+\.\d+(?:[Ee][+-]?\d+)?", RegexOptions.Compiled);

    public static ParityDivergenceReport Analyze(
        string referenceDumpPath,
        string managedDumpPath,
        double relativeThreshold = 0.01)
    {
        ParsedDump reference = ParseDump(referenceDumpPath);
        ParsedDump managed = ParseDump(managedDumpPath);

        ParityDivergenceLocation? firstDivergence = FindFirstDivergence(reference, managed, relativeThreshold, out string? note);
        if (firstDivergence is null &&
            !SummariesRoughlyMatch(reference.Summary, managed.Summary, relativeThreshold))
        {
            firstDivergence = new ParityDivergenceLocation(
                Iteration: managed.Summary.Iterations ?? reference.Summary.Iterations ?? 0,
                Side: 0,
                Station: 0,
                SystemLine: 0,
                Category: "SUMMARY",
                Detail: string.Format(
                    CultureInfo.InvariantCulture,
                    "Final coefficients diverged without an earlier parsed block mismatch. CL ref={0:G17} man={1:G17}; CD ref={2:G17} man={3:G17}; CM ref={4:G17} man={5:G17}.",
                    reference.Summary.LiftCoefficient ?? double.NaN,
                    managed.Summary.LiftCoefficient ?? double.NaN,
                    reference.Summary.DragCoefficient ?? double.NaN,
                    managed.Summary.DragCoefficient ?? double.NaN,
                    reference.Summary.MomentCoefficient ?? double.NaN,
                    managed.Summary.MomentCoefficient ?? double.NaN));
        }

        return new ParityDivergenceReport(
            referenceDumpPath,
            managedDumpPath,
            relativeThreshold,
            reference.Summary,
            managed.Summary,
            firstDivergence,
            note);
    }

    private static bool SummariesRoughlyMatch(ParityRunSummary reference, ParityRunSummary managed, double relativeThreshold)
    {
        return RoughlyEqual(reference.LiftCoefficient, managed.LiftCoefficient, relativeThreshold) &&
               RoughlyEqual(reference.DragCoefficient, managed.DragCoefficient, relativeThreshold) &&
               RoughlyEqual(reference.MomentCoefficient, managed.MomentCoefficient, relativeThreshold);
    }

    private static bool RoughlyEqual(double? reference, double? managed, double relativeThreshold)
    {
        if (reference is null || managed is null)
        {
            return reference == managed;
        }

        return RelativeError(reference.Value, managed.Value) <= relativeThreshold;
    }

    private static ParityDivergenceLocation? FindFirstDivergence(
        ParsedDump reference,
        ParsedDump managed,
        double relativeThreshold,
        out string? note)
    {
        note = null;
        int comparedIterations = Math.Min(reference.Iterations.Count, managed.Iterations.Count);
        if (reference.Iterations.Count != managed.Iterations.Count)
        {
            note = string.Format(
                CultureInfo.InvariantCulture,
                "Iteration counts differ: reference={0}, managed={1}.",
                reference.Iterations.Count,
                managed.Iterations.Count);
        }

        for (int iterationIndex = 0; iterationIndex < comparedIterations; iterationIndex++)
        {
            DumpIteration referenceIteration = reference.Iterations[iterationIndex];
            DumpIteration managedIteration = managed.Iterations[iterationIndex];

            var referenceBySystemLine = referenceIteration.Stations.ToDictionary(station => station.SystemLine);
            var managedBySystemLine = managedIteration.Stations.ToDictionary(station => station.SystemLine);
            int[] commonSystemLines = referenceBySystemLine.Keys.Intersect(managedBySystemLine.Keys).OrderBy(value => value).ToArray();

            if (commonSystemLines.Length == 0)
            {
                DumpStation? referenceFirst = referenceIteration.Stations.FirstOrDefault();
                DumpStation? managedFirst = managedIteration.Stations.FirstOrDefault();
                if (referenceFirst is not null && managedFirst is not null)
                {
                    return new ParityDivergenceLocation(
                        referenceIteration.Number,
                        referenceFirst.Side,
                        referenceFirst.Station,
                        referenceFirst.SystemLine,
                        "TOPOLOGY",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "No common system-line indices were found in iteration {0}. First reference station is IS={1} IBL={2} IV={3}; first managed station is IS={4} IBL={5} IV={6}.",
                            referenceIteration.Number,
                            referenceFirst.Side,
                            referenceFirst.Station,
                            referenceFirst.SystemLine,
                            managedFirst.Side,
                            managedFirst.Station,
                            managedFirst.SystemLine));
                }

                continue;
            }

            foreach (int systemLine in commonSystemLines)
            {
                DumpStation referenceStation = referenceBySystemLine[systemLine];
                DumpStation managedStation = managedBySystemLine[systemLine];

                ParityDivergenceLocation? stateMismatch = CompareDictionaryFields(
                    referenceIteration.Number,
                    referenceStation,
                    managedStation,
                    "BL_STATE",
                    referenceStation.BlState,
                    managedStation.BlState,
                    relativeThreshold);
                if (stateMismatch is not null)
                {
                    return stateMismatch;
                }

                for (int rowIndex = 0; rowIndex < 3; rowIndex++)
                {
                    ParityDivergenceLocation? vaMismatch = CompareSeries(
                        referenceIteration.Number,
                        referenceStation,
                        managedStation,
                        "VA",
                        $"row {rowIndex + 1}",
                        referenceStation.VaRows[rowIndex],
                        managedStation.VaRows[rowIndex],
                        relativeThreshold);
                    if (vaMismatch is not null)
                    {
                        return vaMismatch;
                    }

                    ParityDivergenceLocation? vbMismatch = CompareSeries(
                        referenceIteration.Number,
                        referenceStation,
                        managedStation,
                        "VB",
                        $"row {rowIndex + 1}",
                        referenceStation.VbRows[rowIndex],
                        managedStation.VbRows[rowIndex],
                        relativeThreshold);
                    if (vbMismatch is not null)
                    {
                        return vbMismatch;
                    }
                }

                ParityDivergenceLocation? vdelRMismatch = CompareSeries(
                    referenceIteration.Number,
                    referenceStation,
                    managedStation,
                    "VDEL_R",
                    "row",
                    referenceStation.VdelR,
                    managedStation.VdelR,
                    relativeThreshold);
                if (vdelRMismatch is not null)
                {
                    return vdelRMismatch;
                }

                ParityDivergenceLocation? vdelSMismatch = CompareSeries(
                    referenceIteration.Number,
                    referenceStation,
                    managedStation,
                    "VDEL_S",
                    "row",
                    referenceStation.VdelS,
                    managedStation.VdelS,
                    relativeThreshold);
                if (vdelSMismatch is not null)
                {
                    return vdelSMismatch;
                }

                ParityDivergenceLocation? vsrezMismatch = CompareSeries(
                    referenceIteration.Number,
                    referenceStation,
                    managedStation,
                    "VSREZ",
                    "row",
                    referenceStation.Vsrez,
                    managedStation.Vsrez,
                    relativeThreshold);
                if (vsrezMismatch is not null)
                {
                    return vsrezMismatch;
                }
            }
        }

        return null;
    }

    private static ParityDivergenceLocation? CompareDictionaryFields(
        int iteration,
        DumpStation referenceStation,
        DumpStation managedStation,
        string category,
        IReadOnlyDictionary<string, double> reference,
        IReadOnlyDictionary<string, double> managed,
        double relativeThreshold)
    {
        foreach ((string key, double referenceValue) in reference)
        {
            if (!managed.TryGetValue(key, out double managedValue))
            {
                return new ParityDivergenceLocation(
                    iteration,
                    referenceStation.Side,
                    referenceStation.Station,
                    referenceStation.SystemLine,
                    category,
                    $"{key} is missing on the managed side.");
            }

            double relativeError = RelativeError(referenceValue, managedValue);
            if (relativeError > relativeThreshold)
            {
                return new ParityDivergenceLocation(
                    iteration,
                    referenceStation.Side,
                    referenceStation.Station,
                    referenceStation.SystemLine,
                    category,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} Fortran={1:E8} Managed={2:E8} relErr={3:P2}",
                        key,
                        referenceValue,
                        managedValue,
                        relativeError));
            }
        }

        return null;
    }

    private static ParityDivergenceLocation? CompareSeries(
        int iteration,
        DumpStation referenceStation,
        DumpStation managedStation,
        string category,
        string label,
        IReadOnlyList<double> reference,
        IReadOnlyList<double> managed,
        double relativeThreshold)
    {
        int count = Math.Min(reference.Count, managed.Count);
        for (int index = 0; index < count; index++)
        {
            double relativeError = RelativeError(reference[index], managed[index]);
            if (relativeError > relativeThreshold)
            {
                return new ParityDivergenceLocation(
                    iteration,
                    referenceStation.Side,
                    referenceStation.Station,
                    referenceStation.SystemLine,
                    category,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} value {1} Fortran={2:E8} Managed={3:E8} relErr={4:P2}",
                        label,
                        index + 1,
                        reference[index],
                        managed[index],
                        relativeError));
            }
        }

        if (reference.Count != managed.Count)
        {
            return new ParityDivergenceLocation(
                iteration,
                referenceStation.Side,
                referenceStation.Station,
                referenceStation.SystemLine,
                category,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} length differs. Fortran={1}, Managed={2}.",
                    label,
                    reference.Count,
                    managed.Count));
        }

        return null;
    }

    private static double RelativeError(double reference, double managed)
    {
        double denominator = Math.Max(Math.Abs(reference), 1e-20);
        return Math.Abs(reference - managed) / denominator;
    }

    private static ParsedDump ParseDump(string path)
    {
        var iterations = new List<DumpIteration>();
        DumpIteration? currentIteration = null;
        DumpStation? currentStation = null;
        double? finalCl = null;
        double? finalCd = null;
        double? finalCm = null;
        bool? finalConverged = null;
        int? finalIterations = null;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Match finalMatch = FinalRegex.Match(line);
            if (finalMatch.Success)
            {
                finalCl = ParseDouble(finalMatch.Groups[1].Value);
                finalCd = ParseDouble(finalMatch.Groups[2].Value);
                finalCm = ParseDouble(finalMatch.Groups[3].Value);
                finalConverged = bool.TryParse(finalMatch.Groups[4].Value, out bool converged) ? converged : null;
                finalIterations = int.Parse(finalMatch.Groups[5].Value, CultureInfo.InvariantCulture);
                continue;
            }

            Match iterationMatch = IterationRegex.Match(line);
            if (iterationMatch.Success)
            {
                FinalizeStation(currentIteration, ref currentStation);
                currentIteration = new DumpIteration(int.Parse(iterationMatch.Groups[1].Value, CultureInfo.InvariantCulture));
                iterations.Add(currentIteration);
                continue;
            }

            Match postCalcMatch = PostCalcRegex.Match(line);
            if (postCalcMatch.Success)
            {
                FinalizeStation(currentIteration, ref currentStation);
                if (currentIteration is not null)
                {
                    currentIteration.Cl = ParseDouble(postCalcMatch.Groups[1].Value);
                    currentIteration.Cd = ParseDouble(postCalcMatch.Groups[2].Value);
                    currentIteration.Cm = ParseDouble(postCalcMatch.Groups[3].Value);
                }

                continue;
            }

            if (line.Contains("CONVERGED", StringComparison.Ordinal))
            {
                FinalizeStation(currentIteration, ref currentStation);
                if (currentIteration is not null)
                {
                    currentIteration.Converged = true;
                }

                continue;
            }

            Match stationMatch = StationRegex.Match(line);
            if (stationMatch.Success)
            {
                FinalizeStation(currentIteration, ref currentStation);
                currentStation = new DumpStation(
                    side: int.Parse(stationMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    station: int.Parse(stationMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    systemLine: int.Parse(stationMatch.Groups[3].Value, CultureInfo.InvariantCulture));
                continue;
            }

            if (currentStation is null)
            {
                continue;
            }

            Match blStateMatch = BlStateRegex.Match(line);
            if (blStateMatch.Success)
            {
                currentStation.BlState["x"] = ParseDouble(blStateMatch.Groups[1].Value);
                currentStation.BlState["Ue"] = ParseDouble(blStateMatch.Groups[2].Value);
                currentStation.BlState["th"] = ParseDouble(blStateMatch.Groups[3].Value);
                currentStation.BlState["ds"] = ParseDouble(blStateMatch.Groups[4].Value);
                currentStation.BlState["m"] = ParseDouble(blStateMatch.Groups[5].Value);
                continue;
            }

            PopulateSeriesIfMatch(line, "VA_ROW1", currentStation.VaRows[0]);
            PopulateSeriesIfMatch(line, "VA_ROW2", currentStation.VaRows[1]);
            PopulateSeriesIfMatch(line, "VA_ROW3", currentStation.VaRows[2]);
            PopulateSeriesIfMatch(line, "VB_ROW1", currentStation.VbRows[0]);
            PopulateSeriesIfMatch(line, "VB_ROW2", currentStation.VbRows[1]);
            PopulateSeriesIfMatch(line, "VB_ROW3", currentStation.VbRows[2]);
            PopulateSeriesIfMatch(line, "VDEL_R", currentStation.VdelR);
            PopulateSeriesIfMatch(line, "VDEL_S", currentStation.VdelS);
            PopulateSeriesIfMatch(line, "VSREZ", currentStation.Vsrez);
        }

        FinalizeStation(currentIteration, ref currentStation);

        DumpIteration? lastIteration = iterations.LastOrDefault();
        return new ParsedDump(
            iterations,
            new ParityRunSummary(
                LiftCoefficient: finalCl ?? lastIteration?.Cl,
                DragCoefficient: finalCd ?? lastIteration?.Cd,
                MomentCoefficient: finalCm ?? lastIteration?.Cm,
                Converged: finalConverged ?? lastIteration?.Converged,
                Iterations: finalIterations ?? lastIteration?.Number));
    }

    private static void FinalizeStation(DumpIteration? iteration, ref DumpStation? station)
    {
        if (iteration is not null && station is not null)
        {
            iteration.Stations.Add(station);
        }

        station = null;
    }

    private static void PopulateSeriesIfMatch(string line, string prefix, List<double> target)
    {
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        target.Clear();
        foreach (Match match in FloatRegex.Matches(line[prefix.Length..]))
        {
            target.Add(ParseDouble(match.Value));
        }
    }

    private static double ParseDouble(string text)
    {
        return double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private sealed record ParsedDump(IReadOnlyList<DumpIteration> Iterations, ParityRunSummary Summary);

    private sealed class DumpIteration
    {
        public DumpIteration(int number)
        {
            Number = number;
        }

        public int Number { get; }

        public List<DumpStation> Stations { get; } = new();

        public double? Cl { get; set; }

        public double? Cd { get; set; }

        public double? Cm { get; set; }

        public bool Converged { get; set; }
    }

    private sealed class DumpStation
    {
        public DumpStation(int side, int station, int systemLine)
        {
            Side = side;
            Station = station;
            SystemLine = systemLine;
        }

        public int Side { get; }

        public int Station { get; }

        public int SystemLine { get; }

        public Dictionary<string, double> BlState { get; } = new(StringComparer.Ordinal);

        public List<double>[] VaRows { get; } = { new(), new(), new() };

        public List<double>[] VbRows { get; } = { new(), new(), new() };

        public List<double> VdelR { get; } = new();

        public List<double> VdelS { get; } = new();

        public List<double> Vsrez { get; } = new();
    }
}

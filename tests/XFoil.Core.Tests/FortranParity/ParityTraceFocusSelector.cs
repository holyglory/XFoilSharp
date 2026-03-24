using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

// Legacy audit:
// Primary legacy source: src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs :: TraceSelector environment matching contract
// Secondary legacy source: tools/fortran-debug focused trace environment workflow
// Role in port: Managed-only parity-harness selector that mirrors the JSON trace capture filters when the live comparator reuses an unfocused reference trace.
// Differences: Classic XFoil had no managed-side reference replay; this selector exists only so focused managed reruns can skip unrelated reference events without regenerating a fresh Fortran trace.
// Decision: Keep the selector in the test harness because it is parity-debugging infrastructure rather than production solver behavior.
namespace XFoil.Core.Tests.FortranParity;

internal sealed class ParityTraceFocusSelector
{
    private ParityTraceFocusSelector(
        HashSet<string>? allowedKinds,
        HashSet<string>? allowedScopes,
        HashSet<string>? allowedNames,
        IReadOnlyDictionary<string, string>? requiredData,
        int? occurrence,
        int? side,
        int? station,
        int? iteration,
        int? iterationMin,
        int? iterationMax,
        string? mode)
    {
        AllowedKinds = allowedKinds;
        AllowedScopes = allowedScopes;
        AllowedNames = allowedNames;
        RequiredData = requiredData;
        Occurrence = occurrence;
        Side = side;
        Station = station;
        Iteration = iteration;
        IterationMin = iterationMin;
        IterationMax = iterationMax;
        Mode = mode;
    }

    public HashSet<string>? AllowedKinds { get; }

    public HashSet<string>? AllowedScopes { get; }

    public HashSet<string>? AllowedNames { get; }

    public IReadOnlyDictionary<string, string>? RequiredData { get; }

    public int? Occurrence { get; }

    public int? Side { get; }

    public int? Station { get; }

    public int? Iteration { get; }

    public int? IterationMin { get; }

    public int? IterationMax { get; }

    public string? Mode { get; }

    public static ParityTraceFocusSelector? FromEnvironment()
        => FromEnvironment(
            kindVar: "XFOIL_TRACE_KIND_ALLOW",
            scopeVar: "XFOIL_TRACE_SCOPE_ALLOW",
            nameVar: "XFOIL_TRACE_NAME_ALLOW",
            dataMatchVar: "XFOIL_TRACE_DATA_MATCH",
            occurrenceVar: null,
            sideVar: "XFOIL_TRACE_SIDE",
            stationVar: "XFOIL_TRACE_STATION",
            iterationVar: "XFOIL_TRACE_ITERATION",
            iterationMinVar: "XFOIL_TRACE_ITER_MIN",
            iterationMaxVar: "XFOIL_TRACE_ITER_MAX",
            modeVar: "XFOIL_TRACE_MODE");

    public static ParityTraceFocusSelector? FromTriggerEnvironment()
        => FromEnvironment(
            kindVar: "XFOIL_TRACE_TRIGGER_KIND",
            scopeVar: "XFOIL_TRACE_TRIGGER_SCOPE",
            nameVar: "XFOIL_TRACE_TRIGGER_NAME_ALLOW",
            dataMatchVar: "XFOIL_TRACE_TRIGGER_DATA_MATCH",
            occurrenceVar: "XFOIL_TRACE_TRIGGER_OCCURRENCE",
            sideVar: "XFOIL_TRACE_TRIGGER_SIDE",
            stationVar: "XFOIL_TRACE_TRIGGER_STATION",
            iterationVar: "XFOIL_TRACE_TRIGGER_ITERATION",
            iterationMinVar: "XFOIL_TRACE_TRIGGER_ITER_MIN",
            iterationMaxVar: "XFOIL_TRACE_TRIGGER_ITER_MAX",
            modeVar: "XFOIL_TRACE_TRIGGER_MODE");

    public static ParityTraceFocusSelector? FromEnvironment(
        string kindVar,
        string scopeVar,
        string nameVar,
        string dataMatchVar,
        string? occurrenceVar,
        string sideVar,
        string stationVar,
        string iterationVar,
        string iterationMinVar,
        string iterationMaxVar,
        string modeVar)
    {
        HashSet<string>? kinds = ParseSet(kindVar);
        HashSet<string>? scopes = ParseSet(scopeVar);
        HashSet<string>? names = ParseSet(nameVar);
        IReadOnlyDictionary<string, string>? requiredData = ParseKeyValuePairs(dataMatchVar);
        int? occurrence = string.IsNullOrWhiteSpace(occurrenceVar) ? null : ParseInt(occurrenceVar);
        int? side = ParseInt(sideVar);
        int? station = ParseInt(stationVar);
        int? iteration = ParseInt(iterationVar);
        int? iterationMin = ParseInt(iterationMinVar);
        int? iterationMax = ParseInt(iterationMaxVar);
        string? mode = ParseString(modeVar);

        if (kinds is null &&
            scopes is null &&
            names is null &&
            requiredData is null &&
            occurrence is null &&
            side is null &&
            station is null &&
            iteration is null &&
            iterationMin is null &&
            iterationMax is null &&
            mode is null)
        {
            return null;
        }

        return new ParityTraceFocusSelector(kinds, scopes, names, requiredData, occurrence, side, station, iteration, iterationMin, iterationMax, mode);
    }

    public bool Matches(ParityTraceRecord record)
    {
        if (AllowedKinds is not null && !AllowedKinds.Contains(record.Kind))
        {
            return false;
        }

        if (AllowedScopes is not null && !AllowedScopes.Contains(record.Scope))
        {
            return false;
        }

        if (AllowedNames is not null)
        {
            if (string.IsNullOrWhiteSpace(record.Name) || !AllowedNames.Contains(record.Name))
            {
                return false;
            }
        }

        if (RequiredData is not null)
        {
            foreach ((string key, string expectedValue) in RequiredData)
            {
                if (!TryGetFormattedDataValue(record, key, out string? actualValue) ||
                    !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (Side is not null && (!TryGetInt(record, "side", out int side) || side != Side.Value))
        {
            return false;
        }

        if (Station is not null && (!TryGetInt(record, "station", out int station) || station != Station.Value))
        {
            return false;
        }

        if (Iteration is not null && (!TryGetInt(record, "iteration", out int iteration) || iteration != Iteration.Value))
        {
            return false;
        }

        if (IterationMin is not null && (!TryGetInt(record, "iteration", out int iterationMinValue) || iterationMinValue < IterationMin.Value))
        {
            return false;
        }

        if (IterationMax is not null && (!TryGetInt(record, "iteration", out int iterationMaxValue) || iterationMaxValue > IterationMax.Value))
        {
            return false;
        }

        if (Mode is not null && (!TryGetString(record, "mode", out string? mode) || !string.Equals(mode, Mode, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static HashSet<string>? ParseSet(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var values = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        return values.Count == 0 ? null : values;
    }

    private static IReadOnlyDictionary<string, string>? ParseKeyValuePairs(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] tokens = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            int separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            {
                continue;
            }

            string key = token[..separatorIndex].Trim();
            string value = token[(separatorIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            pairs[key] = value;
        }

        return pairs.Count == 0 ? null : pairs;
    }

    private static int? ParseInt(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static string? ParseString(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static bool TryGetInt(ParityTraceRecord record, string path, out int value)
    {
        if (!record.TryGetDataField(path, out JsonElement element))
        {
            value = 0;
            return false;
        }

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            value = 0;
            return false;
        }

        if (element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.TryGetInt64(out long int64Value) &&
            int64Value >= int.MinValue &&
            int64Value <= int.MaxValue)
        {
            value = (int)int64Value;
            return true;
        }

        if (int.TryParse(element.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetString(ParityTraceRecord record, string path, out string? value)
    {
        if (!record.TryGetDataField(path, out JsonElement element))
        {
            value = null;
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetFormattedDataValue(ParityTraceRecord record, string path, out string? value)
    {
        if (!record.TryGetDataField(path, out JsonElement element))
        {
            value = null;
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long int64Value)
                ? int64Value.ToString(CultureInfo.InvariantCulture)
                : element.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

        return !string.IsNullOrWhiteSpace(value);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/json_trace.f event identity carried through kind/name/tag fields
// Secondary legacy source: src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs
// Role in port: Managed-only selector/alignment helpers for matching one Fortran trace record to its managed counterpart.
// Differences: Classic XFoil had no need for runtime-to-runtime event alignment because it only had one runtime; this file formalizes that contract for parity tests.
// Decision: Keep the managed-only alignment helpers because the parity suites need one shared definition of “same block, same indices”.
namespace XFoil.Core.Tests.FortranParity;

public sealed record TraceEventSelector(
    string Kind,
    string? Name = null,
    string? Scope = null,
    IReadOnlyDictionary<string, object?>? TagFilters = null);

public static class ParityTraceAligner
{
    public static (ParityTraceRecord Reference, ParityTraceRecord Managed) AlignSingle(
        string referencePath,
        string managedPath,
        TraceEventSelector selector)
    {
        ParityTraceRecord reference = ParityTraceLoader.FindSingle(
            referencePath,
            record => Matches(record, selector),
            $"reference {Describe(selector)}");
        ParityTraceRecord managed = ParityTraceLoader.FindSingle(
            managedPath,
            record => Matches(record, selector),
            $"managed {Describe(selector)}");
        return (reference, managed);
    }

    public static bool Matches(ParityTraceRecord record, TraceEventSelector selector)
    {
        if (!string.Equals(record.Kind, selector.Kind, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.Name is not null && !string.Equals(record.Name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.Scope is not null && !string.Equals(record.Scope, selector.Scope, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.TagFilters is null)
        {
            return true;
        }

        foreach ((string key, object? expected) in selector.TagFilters)
        {
            if (!record.TryGetDataField(key, out JsonElement field))
            {
                if (!record.TryGetTag(key, out field))
                {
                    return false;
                }
            }

            if (!ValueEquals(field, expected))
            {
                return false;
            }
        }

        return true;
    }

    public static string Describe(TraceEventSelector selector)
    {
        if (selector.TagFilters is null || selector.TagFilters.Count == 0)
        {
            return $"{selector.Kind}/{selector.Name ?? "*"}";
        }

        var parts = new List<string>();
        foreach ((string key, object? value) in selector.TagFilters)
        {
            parts.Add($"{key}={value}");
        }

        return $"{selector.Kind}/{selector.Name ?? "*"} [{string.Join(", ", parts)}]";
    }

    private static bool ValueEquals(JsonElement actual, object? expected)
    {
        if (expected is null)
        {
            return actual.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        }

        return expected switch
        {
            int intValue => actual.ValueKind == JsonValueKind.Number && actual.GetInt32() == intValue,
            long longValue => actual.ValueKind == JsonValueKind.Number && actual.GetInt64() == longValue,
            double doubleValue => actual.ValueKind == JsonValueKind.Number &&
                Math.Abs(actual.GetDouble() - doubleValue) <= 1e-12,
            string stringValue => actual.ValueKind == JsonValueKind.String &&
                string.Equals(actual.GetString(), stringValue, StringComparison.Ordinal),
            bool boolValue => actual.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                actual.GetBoolean() == boolValue,
            _ => string.Equals(actual.ToString(), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal)
        };
    }
}

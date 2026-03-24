using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Xunit.Sdk;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: tools/fortran-debug/json_trace.f numeric payload semantics for REAL-vs-double parity checks
// Role in port: Managed-only assertion helpers that enforce the “inputs first, outputs second” rule for cross-runtime parity tests.
// Differences: Classic XFoil had no automated assertion layer; this file adds explicit numeric-comparison modes and failure messages tailored to parity debugging.
// Decision: Keep the managed-only assertion helpers because they encode the audit policy the solver work now depends on.
namespace XFoil.Core.Tests.FortranParity;

public enum NumericComparisonMode
{
    ExactSingle,
    ExactDouble,
    Tolerance,
    LogicalEquivalent
}

public sealed record FieldExpectation(string Selector, NumericComparisonMode Mode = NumericComparisonMode.ExactSingle, double Tolerance = 0.0);

public static class FortranParityAssert
{
    public static void AssertInputsThenOutputs(
        ParityTraceRecord reference,
        ParityTraceRecord managed,
        IReadOnlyList<FieldExpectation> inputFields,
        IReadOnlyList<FieldExpectation> outputFields,
        string blockDescription)
    {
        foreach (FieldExpectation field in inputFields)
        {
            CompareField(reference, managed, field, "input", blockDescription);
        }

        foreach (FieldExpectation field in outputFields)
        {
            CompareField(reference, managed, field, "output", blockDescription);
        }
    }

    private static void CompareField(
        ParityTraceRecord reference,
        ParityTraceRecord managed,
        FieldExpectation expectation,
        string phase,
        string blockDescription)
    {
        object referenceValue = ResolveField(reference, expectation.Selector);
        object managedValue = ResolveField(managed, expectation.Selector);

        if (expectation.Mode == NumericComparisonMode.LogicalEquivalent)
        {
            if (!LogicalValuesMatch(referenceValue, managedValue))
            {
                throw new XunitException(
                    $"{blockDescription}: first {phase} mismatch at '{expectation.Selector}' " +
                    $"(mode={expectation.Mode}). " +
                    $"Fortran='{referenceValue}' Managed='{managedValue}'.");
            }

            return;
        }

        if (referenceValue is double referenceNumber && managedValue is double managedNumber)
        {
            string? referenceBits = ResolveBitPattern(reference, expectation.Selector, expectation.Mode);
            string? managedBits = ResolveBitPattern(managed, expectation.Selector, expectation.Mode);
            if (referenceBits is not null && managedBits is not null && IsExactBitwiseMode(expectation.Mode))
            {
                if (!string.Equals(referenceBits, managedBits, StringComparison.Ordinal))
                {
                    throw new XunitException(
                        $"{blockDescription}: first {phase} mismatch at '{expectation.Selector}' " +
                        $"(mode={expectation.Mode}). " +
                        $"Fortran={referenceNumber.ToString("G17", CultureInfo.InvariantCulture)} [{referenceBits}] " +
                        $"Managed={managedNumber.ToString("G17", CultureInfo.InvariantCulture)} [{managedBits}].");
                }

                return;
            }

            if (!NumbersMatch(referenceNumber, managedNumber, expectation.Mode, expectation.Tolerance))
            {
                throw new XunitException(
                    $"{blockDescription}: first {phase} mismatch at '{expectation.Selector}' " +
                    $"(mode={expectation.Mode}, tol={expectation.Tolerance.ToString("G17", CultureInfo.InvariantCulture)}). " +
                    $"Fortran={referenceNumber.ToString("G17", CultureInfo.InvariantCulture)}{FormatBitsSuffix(referenceBits)} " +
                    $"Managed={managedNumber.ToString("G17", CultureInfo.InvariantCulture)}{FormatBitsSuffix(managedBits)}.");
            }

            return;
        }

        if (!Equals(referenceValue, managedValue))
        {
            throw new XunitException(
                $"{blockDescription}: first {phase} mismatch at '{expectation.Selector}'. " +
                $"Fortran='{referenceValue}' Managed='{managedValue}'.");
        }
    }

    private static bool NumbersMatch(double expected, double actual, NumericComparisonMode mode, double tolerance)
    {
        return mode switch
        {
            NumericComparisonMode.ExactSingle =>
                BitConverter.SingleToInt32Bits((float)expected) == BitConverter.SingleToInt32Bits((float)actual),
            NumericComparisonMode.ExactDouble =>
                BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            NumericComparisonMode.Tolerance =>
                Math.Abs(expected - actual) <= tolerance,
            NumericComparisonMode.LogicalEquivalent =>
                throw new InvalidOperationException("LogicalEquivalent is handled before numeric comparison."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown numeric comparison mode.")
        };
    }

    private static bool LogicalValuesMatch(object referenceValue, object managedValue)
    {
        bool? referenceLogical = TryConvertToLogical(referenceValue);
        bool? managedLogical = TryConvertToLogical(managedValue);
        return referenceLogical is not null &&
               managedLogical is not null &&
               referenceLogical.Value == managedLogical.Value;
    }

    private static bool? TryConvertToLogical(object value)
    {
        return value switch
        {
            bool logical => logical,
            double number => Math.Abs(number) > 0.5,
            _ => null
        };
    }

    private static bool IsExactBitwiseMode(NumericComparisonMode mode)
    {
        return mode is NumericComparisonMode.ExactSingle or NumericComparisonMode.ExactDouble;
    }

    private static string? ResolveBitPattern(ParityTraceRecord record, string selector, NumericComparisonMode mode)
    {
        string? width = mode switch
        {
            NumericComparisonMode.ExactSingle => "f32",
            NumericComparisonMode.ExactDouble => "f64",
            _ => null
        };

        if (width is null)
        {
            return null;
        }

        if (selector.StartsWith("data.", StringComparison.Ordinal))
        {
            return TryResolveBits(record.TryGetDataBits(selector["data.".Length..], out IReadOnlyDictionary<string, string>? bits), bits, width);
        }

        if (selector.StartsWith("tags.", StringComparison.Ordinal))
        {
            return TryResolveBits(record.TryGetTagBits(selector["tags.".Length..], out IReadOnlyDictionary<string, string>? bits), bits, width);
        }

        if (selector.StartsWith("values[", StringComparison.Ordinal) && selector.EndsWith("]", StringComparison.Ordinal))
        {
            string indexText = selector["values[".Length..^1];
            if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                return TryResolveBits(record.TryGetValueBits(index, out IReadOnlyDictionary<string, string>? bits), bits, width);
            }
        }

        return null;
    }

    private static string? TryResolveBits(bool success, IReadOnlyDictionary<string, string>? bits, string width)
    {
        return success && bits is not null && bits.TryGetValue(width, out string? value)
            ? value
            : null;
    }

    private static string FormatBitsSuffix(string? bits)
    {
        return bits is null ? string.Empty : $" [{bits}]";
    }

    private static object ResolveField(ParityTraceRecord record, string selector)
    {
        if (selector.StartsWith("data.", StringComparison.Ordinal))
        {
            if (!record.TryGetDataField(selector["data.".Length..], out JsonElement dataElement))
            {
                throw new XunitException($"Field '{selector}' was not present in trace record {record.Kind}/{record.Name ?? "*"}.");
            }

            return ConvertJsonElement(dataElement);
        }

        if (selector.StartsWith("tags.", StringComparison.Ordinal))
        {
            string tag = selector["tags.".Length..];
            if (!record.TryGetTag(tag, out JsonElement tagElement))
            {
                throw new XunitException($"Tag '{selector}' was not present in trace record {record.Kind}/{record.Name ?? "*"}.");
            }

            return ConvertJsonElement(tagElement);
        }

        if (selector.StartsWith("values[", StringComparison.Ordinal) && selector.EndsWith("]", StringComparison.Ordinal))
        {
            if (record.Values is null)
            {
                throw new XunitException($"Trace record {record.Kind}/{record.Name ?? "*"} has no values array for selector '{selector}'.");
            }

            string indexText = selector["values[".Length..^1];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                index < 0 ||
                index >= record.Values.Length)
            {
                throw new XunitException($"Selector '{selector}' referenced an invalid values index.");
            }

            return record.Values[index];
        }

        return selector switch
        {
            "kind" => record.Kind,
            "scope" => record.Scope,
            "name" => record.Name ?? string.Empty,
            "runtime" => record.Runtime,
            _ => throw new XunitException($"Unsupported field selector '{selector}'.")
        };
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };
    }
}

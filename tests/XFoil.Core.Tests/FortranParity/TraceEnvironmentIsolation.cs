using System;
using System.Collections.Generic;

namespace XFoil.Core.Tests.FortranParity;

internal static class TraceEnvironmentIsolation
{
    private static readonly string[] TraceVariableNames =
    {
        "XFOIL_TRACE_KIND_ALLOW",
        "XFOIL_TRACE_SCOPE_ALLOW",
        "XFOIL_TRACE_NAME_ALLOW",
        "XFOIL_TRACE_DATA_MATCH",
        "XFOIL_TRACE_SIDE",
        "XFOIL_TRACE_STATION",
        "XFOIL_TRACE_ITERATION",
        "XFOIL_TRACE_ITER_MIN",
        "XFOIL_TRACE_ITER_MAX",
        "XFOIL_TRACE_MODE",
        "XFOIL_TRACE_TRIGGER_KIND",
        "XFOIL_TRACE_TRIGGER_SCOPE",
        "XFOIL_TRACE_TRIGGER_NAME_ALLOW",
        "XFOIL_TRACE_TRIGGER_DATA_MATCH",
        "XFOIL_TRACE_TRIGGER_SIDE",
        "XFOIL_TRACE_TRIGGER_STATION",
        "XFOIL_TRACE_TRIGGER_ITERATION",
        "XFOIL_TRACE_TRIGGER_ITER_MIN",
        "XFOIL_TRACE_TRIGGER_ITER_MAX",
        "XFOIL_TRACE_TRIGGER_MODE",
        "XFOIL_TRACE_TRIGGER_OCCURRENCE",
        "XFOIL_TRACE_RING_BUFFER",
        "XFOIL_TRACE_POST_LIMIT",
        "XFOIL_MAX_TRACE_MB"
    };

    public static IDisposable Clear()
    {
        var overrides = new Dictionary<string, string?>(TraceVariableNames.Length, StringComparer.Ordinal);
        foreach (string variableName in TraceVariableNames)
        {
            overrides[variableName] = null;
        }

        return new EnvironmentVariableScope(overrides);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach ((string key, string? value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

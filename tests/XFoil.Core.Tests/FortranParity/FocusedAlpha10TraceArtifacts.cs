using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// Legacy audit:
// Primary legacy source: tools/fortran-debug numbered alpha-10 reference and solver traces
// Secondary legacy source: none
// Role in port: Managed-only test helper that resolves the newest authoritative alpha-10 artifacts for focused parity checks.
// Differences: Classic XFoil had no managed test harness or numbered-artifact resolver; this helper exists only to keep narrow parity tests pointed at fresh traces instead of stale compatibility files.
// Decision: Keep the focused artifact resolver because alpha-10 debugging now depends on small block tests that must honor the numbered-trace policy.
namespace XFoil.Core.Tests.FortranParity;

internal static class FocusedAlpha10TraceArtifacts
{
    private const string CaseId = "n0012_re1e6_a10";

    public static string GetLatestReferenceTracePath(TraceEventSelector selector)
    {
        return GetLatestReferenceTracePath(path => ContainsMatchingRecord(path, selector));
    }

    public static string GetLatestReferenceTracePath(params string[] requiredKinds)
    {
        return GetLatestReferenceTracePath(path => ContainsAllKinds(path, requiredKinds));
    }

    public static string GetLatestManagedSolverTracePath(TraceEventSelector selector)
    {
        return GetLatestManagedSolverTracePath(path => ContainsMatchingRecord(path, selector));
    }

    public static string GetLatestManagedSolverTracePath(params string[] requiredKinds)
    {
        return GetLatestManagedSolverTracePath(path => ContainsAllKinds(path, requiredKinds));
    }

    private static string GetLatestReferenceTracePath(Func<string, bool> predicate)
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            CaseId);

        string latestNumbered = GetLatestVersionedArtifact(directory, "reference_trace.", ".jsonl", predicate);
        if (!string.IsNullOrEmpty(latestNumbered))
        {
            return latestNumbered;
        }

        return Path.Combine(directory, "reference_trace.jsonl");
    }

    private static string GetLatestManagedSolverTracePath(Func<string, bool> predicate)
    {
        string directory = FortranReferenceCases.GetFortranDebugDirectory();

        string latestNumbered = GetLatestVersionedArtifact(directory, "polar_alpha10_solver_trace.", ".jsonl", predicate);
        if (!string.IsNullOrEmpty(latestNumbered))
        {
            return latestNumbered;
        }

        return Path.Combine(directory, "polar_alpha10_solver_trace.jsonl");
    }

    private static string GetLatestVersionedArtifact(string directory, string prefix, string suffix, Func<string, bool> predicate)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        return Directory.EnumerateFiles(directory, $"{prefix}*{suffix}")
            .Select(path => new
            {
                Path = path,
                Counter = TryParseCounter(Path.GetFileName(path), prefix, suffix)
            })
            .Where(entry => entry.Counter is not null)
            .OrderByDescending(entry => entry.Counter)
            .Select(entry => entry.Path)
            .Where(predicate)
            .FirstOrDefault() ?? string.Empty;
    }

    private static bool ContainsAllKinds(string path, IReadOnlyList<string> requiredKinds)
    {
        if (requiredKinds.Count == 0)
        {
            return true;
        }

        var remaining = new HashSet<string>(requiredKinds, StringComparer.Ordinal);
        foreach (ParityTraceRecord record in ParityTraceLoader.ReadAll(path))
        {
            if (remaining.Count == 0)
            {
                return true;
            }

            _ = remaining.Remove(record.Kind);
        }

        return remaining.Count == 0;
    }

    private static bool ContainsMatchingRecord(string path, TraceEventSelector selector)
    {
        return ParityTraceLoader.ReadMatching(path, record => ParityTraceAligner.Matches(record, selector)).Count > 0;
    }

    private static long? TryParseCounter(string fileName, string prefix, string suffix)
    {
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        string counterText = fileName[prefix.Length..^suffix.Length];
        return long.TryParse(counterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long counter)
            ? counter
            : null;
    }
}

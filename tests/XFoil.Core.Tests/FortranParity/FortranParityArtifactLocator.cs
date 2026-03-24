using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace XFoil.Core.Tests.FortranParity;

internal static class FortranParityArtifactLocator
{
    private static readonly Regex NumberedTraceRegex = new(@"^reference_trace\.(\d+)\.jsonl$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberedDumpRegex = new(@"^reference_dump\.(\d+)\.txt$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string GetLatestReferenceTracePath(string directory)
        => GetLatestNumberedArtifactPath(
            directory,
            searchPattern: "reference_trace*.jsonl",
            matcher: NumberedTraceRegex,
            fallbackFileName: "reference_trace.jsonl");

    public static string GetLatestReferenceDumpPath(string directory)
        => GetLatestNumberedArtifactPath(
            directory,
            searchPattern: "reference_dump*.txt",
            matcher: NumberedDumpRegex,
            fallbackFileName: "reference_dump.txt");

    private static string GetLatestNumberedArtifactPath(
        string directory,
        string searchPattern,
        Regex matcher,
        string fallbackFileName)
    {
        Assert.True(Directory.Exists(directory), $"Reference artifact directory missing: {directory}");

        string? latestNumbered = Directory
            .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Match = matcher.Match(Path.GetFileName(path))
            })
            .Where(entry => entry.Match.Success)
            .Select(entry => new
            {
                entry.Path,
                Counter = int.Parse(entry.Match.Groups[1].Value)
            })
            .OrderBy(entry => entry.Counter)
            .Select(entry => entry.Path)
            .LastOrDefault();

        if (!string.IsNullOrWhiteSpace(latestNumbered))
        {
            return latestNumbered;
        }

        string fallback = Path.Combine(directory, fallbackFileName);
        Assert.True(File.Exists(fallback), $"Reference artifact missing: {fallback}");
        return fallback;
    }
}

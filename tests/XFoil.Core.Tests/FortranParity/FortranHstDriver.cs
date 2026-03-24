using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranHstCase(float Hk, float Rt, float Msq);

internal sealed record FortranHstCaseResult(float Hs, float HsHk, float HsRt, float HsMsq);

internal sealed record FortranHstTraceCaseResult(
    float Hs,
    float HsHk,
    float HsRt,
    float HsMsq,
    float Ho,
    float Rtz,
    float Grt,
    float Hdif,
    float Rtmp,
    float RtmpSq,
    float RtmpCu,
    float HkSq,
    float HdifSq,
    float Htmp,
    float HtmpHk,
    float HsHkTerm1,
    float HsHkTerm2,
    float HtmpRt,
    float HsRtTerm1,
    float HsRtTerm2,
    float HsRtTerm3);

internal static class FortranHstBuild
{
    private static readonly object SyncRoot = new();

    public static void EnsureBuilt()
    {
        lock (SyncRoot)
        {
            string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
            string scriptPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build_hst_driver.sh");

            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start HST driver build script: {scriptPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"HST parity driver build failed with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }
        }
    }
}

internal static class FortranHstDriver
{
    public static IReadOnlyList<FortranHstCaseResult> RunBatch(IReadOnlyList<FortranHstCase> cases)
        => RunBatchCore(cases, parseTrace: false).Results;

    public static IReadOnlyList<FortranHstTraceCaseResult> RunTraceBatch(IReadOnlyList<FortranHstCase> cases)
        => RunBatchCore(cases, parseTrace: true).TraceResults;

    private static (IReadOnlyList<FortranHstCaseResult> Results, IReadOnlyList<FortranHstTraceCaseResult> TraceResults) RunBatchCore(
        IReadOnlyList<FortranHstCase> cases,
        bool parseTrace)
    {
        if (cases.Count == 0)
        {
            return (
                Array.Empty<FortranHstCaseResult>(),
                Array.Empty<FortranHstTraceCaseResult>());
        }

        FortranHstBuild.EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-hst-driver", "hst_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-hst-parity-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(inputPath, SerializeCases(cases), Encoding.ASCII);

            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add($"\"{binaryPath}\" < \"{inputPath}\"");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start HST parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"HST parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }

            return ParseResults(stdout, cases.Count, parseTrace);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private static string SerializeCases(IReadOnlyList<FortranHstCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranHstCase @case in cases)
        {
            builder.AppendLine(
                $"{FormatSingle(@case.Hk)} {FormatSingle(@case.Rt)} {FormatSingle(@case.Msq)}");
        }

        return builder.ToString();
    }

    private static (IReadOnlyList<FortranHstCaseResult> Results, IReadOnlyList<FortranHstTraceCaseResult> TraceResults) ParseResults(
        string stdout,
        int expectedCount,
        bool parseTrace)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int declaredCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (declaredCount != expectedCount)
        {
            throw new InvalidOperationException($"HST parity driver returned {declaredCount} cases, expected {expectedCount}.");
        }

        var results = new List<FortranHstCaseResult>(expectedCount);
        List<FortranHstTraceCaseResult>? traceResults = parseTrace ? new List<FortranHstTraceCaseResult>(expectedCount) : null;
        for (int caseIndex = 0; caseIndex < expectedCount; caseIndex++)
        {
            string[] tokens = lines[lineIndex++].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 8)
            {
                throw new InvalidOperationException($"Unexpected token count in HST parity output line '{lines[lineIndex - 1]}'.");
            }

            results.Add(new FortranHstCaseResult(
                ParseSingleBits(tokens[0]),
                ParseSingleBits(tokens[2]),
                ParseSingleBits(tokens[4]),
                ParseSingleBits(tokens[6])));

            if (traceResults is not null)
            {
                if (tokens.Length < 25)
                {
                    throw new InvalidOperationException(
                        $"HST trace output line did not include intermediate tokens: '{lines[lineIndex - 1]}'.");
                }

                traceResults.Add(new FortranHstTraceCaseResult(
                    ParseSingleBits(tokens[0]),
                    ParseSingleBits(tokens[2]),
                    ParseSingleBits(tokens[4]),
                    ParseSingleBits(tokens[6]),
                    ParseSingleBits(tokens[8]),
                    ParseSingleBits(tokens[9]),
                    ParseSingleBits(tokens[10]),
                    ParseSingleBits(tokens[11]),
                    ParseSingleBits(tokens[12]),
                    ParseSingleBits(tokens[13]),
                    ParseSingleBits(tokens[14]),
                    ParseSingleBits(tokens[15]),
                    ParseSingleBits(tokens[16]),
                    ParseSingleBits(tokens[17]),
                    ParseSingleBits(tokens[18]),
                    ParseSingleBits(tokens[19]),
                    ParseSingleBits(tokens[20]),
                    ParseSingleBits(tokens[21]),
                    ParseSingleBits(tokens[22]),
                    ParseSingleBits(tokens[23]),
                    ParseSingleBits(tokens[24])));
            }
        }

        return (
            results,
            traceResults is null
                ? Array.Empty<FortranHstTraceCaseResult>()
                : traceResults);
    }

    private static float ParseSingleBits(string text)
    {
        uint bits = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

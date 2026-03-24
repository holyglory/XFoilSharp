using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranCqCase(
    int FlowType,
    float Hk,
    float Hs,
    float Us,
    float H,
    float Rt,
    float HkT,
    float HkD,
    float HkU,
    float HkMs,
    float HsT,
    float HsD,
    float HsU,
    float HsMs,
    float UsT,
    float UsD,
    float UsU,
    float UsMs,
    float HT,
    float HD,
    float RtT,
    float RtU,
    float RtMs);

internal sealed record CqHexRecord(string Kind, int FlowType, IReadOnlyList<string> Values);

internal sealed record FortranCqResult(
    IReadOnlyList<CqHexRecord> Terms,
    IReadOnlyList<CqHexRecord> DerivativeTerms,
    IReadOnlyList<CqHexRecord> Finals);

internal static class FortranCqDriver
{
    public static FortranCqResult RunBatch(IReadOnlyList<FortranCqCase> cases)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "cq_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-cq-parity-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start CQ parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"CQ parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }

            return ParseResult(stdout, cases.Count);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private static void EnsureBuilt()
    {
        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string scriptPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build_micro_drivers.sh");
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "cq_parity_driver");

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
        startInfo.ArgumentList.Add("cq");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start micro-driver build script: {scriptPath}");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            if (File.Exists(binaryPath))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Micro-driver build failed with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
        }
    }

    private static string SerializeCases(IReadOnlyList<FortranCqCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranCqCase @case in cases)
        {
            builder.AppendLine(
                $"{@case.FlowType.ToString(CultureInfo.InvariantCulture)} {FormatSingle(@case.Hk)} {FormatSingle(@case.Hs)} {FormatSingle(@case.Us)} {FormatSingle(@case.H)} {FormatSingle(@case.Rt)}");
            builder.AppendLine($"{FormatSingle(@case.HkT)} {FormatSingle(@case.HkD)} {FormatSingle(@case.HkU)} {FormatSingle(@case.HkMs)}");
            builder.AppendLine($"{FormatSingle(@case.HsT)} {FormatSingle(@case.HsD)} {FormatSingle(@case.HsU)} {FormatSingle(@case.HsMs)}");
            builder.AppendLine($"{FormatSingle(@case.UsT)} {FormatSingle(@case.UsD)} {FormatSingle(@case.UsU)} {FormatSingle(@case.UsMs)}");
            builder.AppendLine($"{FormatSingle(@case.HT)} {FormatSingle(@case.HD)}");
            builder.AppendLine($"{FormatSingle(@case.RtT)} {FormatSingle(@case.RtU)} {FormatSingle(@case.RtMs)}");
        }

        return builder.ToString();
    }

    private static FortranCqResult ParseResult(string stdout, int expectedCases)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int caseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != expectedCases)
        {
            throw new InvalidOperationException($"CQ parity driver returned {caseCount} cases, expected {expectedCases}.");
        }

        var terms = new List<CqHexRecord>(expectedCases);
        var derivativeTerms = new List<CqHexRecord>(expectedCases);
        var finals = new List<CqHexRecord>(expectedCases);

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "TERMS":
                    terms.Add(ParseRecord("TERMS", tokens, 7));
                    break;
                case "DTERM":
                    derivativeTerms.Add(ParseRecord("DTERM", tokens, 27));
                    break;
                case "FINAL":
                    finals.Add(ParseRecord("FINAL", tokens, 5));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected CQ driver output line '{lines[lineIndex]}'.");
            }
        }

        return new FortranCqResult(terms, derivativeTerms, finals);
    }

    private static CqHexRecord ParseRecord(string kind, string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 3)
        {
            throw new InvalidOperationException($"Unexpected {kind} line token count.");
        }

        int flowType = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[valueCount];
        Array.Copy(tokens, 3, values, 0, valueCount);
        return new CqHexRecord(kind, flowType, values);
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

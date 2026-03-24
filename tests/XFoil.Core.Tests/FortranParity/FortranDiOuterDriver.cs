using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranDiOuterCase(
    float S,
    float Hs,
    float Us,
    float Rt,
    float HsT,
    float HsD,
    float HsU,
    float HsMs,
    float UsT,
    float UsD,
    float UsU,
    float UsMs,
    float RtT,
    float RtU,
    float RtMs);

internal sealed record DiOuterHexRecord(IReadOnlyList<string> Values);

internal sealed record FortranDiOuterResult(
    IReadOnlyList<DiOuterHexRecord> DdTerms,
    IReadOnlyList<DiOuterHexRecord> DdlTerms);

internal static class FortranDiOuterDriver
{
    public static FortranDiOuterResult RunBatch(IReadOnlyList<FortranDiOuterCase> cases)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "di_outer_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-diouter-parity-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start DI-outer parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"DI-outer parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
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
        startInfo.ArgumentList.Add("diouter");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start micro-driver build script: {scriptPath}");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Micro-driver build failed with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
        }
    }

    private static string SerializeCases(IReadOnlyList<FortranDiOuterCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranDiOuterCase @case in cases)
        {
            builder.AppendLine($"{FormatSingle(@case.S)} {FormatSingle(@case.Hs)} {FormatSingle(@case.Us)} {FormatSingle(@case.Rt)}");
            builder.AppendLine($"{FormatSingle(@case.HsT)} {FormatSingle(@case.HsD)} {FormatSingle(@case.HsU)} {FormatSingle(@case.HsMs)}");
            builder.AppendLine($"{FormatSingle(@case.UsT)} {FormatSingle(@case.UsD)} {FormatSingle(@case.UsU)} {FormatSingle(@case.UsMs)}");
            builder.AppendLine($"{FormatSingle(@case.RtT)} {FormatSingle(@case.RtU)} {FormatSingle(@case.RtMs)}");
        }

        return builder.ToString();
    }

    private static FortranDiOuterResult ParseResult(string stdout, int expectedCases)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int caseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != expectedCases)
        {
            throw new InvalidOperationException($"DI-outer parity driver returned {caseCount} cases, expected {expectedCases}.");
        }

        var ddTerms = new List<DiOuterHexRecord>(expectedCases);
        var ddlTerms = new List<DiOuterHexRecord>(expectedCases);

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "DD":
                    if (tokens.Length != 10)
                    {
                        throw new InvalidOperationException("Unexpected DI-outer DD line token count.");
                    }

                    ddTerms.Add(new DiOuterHexRecord(new[]
                    {
                        tokens[2], tokens[3], tokens[4], tokens[5],
                        tokens[6], tokens[7], tokens[8], tokens[9]
                    }));
                    break;

                case "DDL":
                    if (tokens.Length != 10)
                    {
                        throw new InvalidOperationException("Unexpected DI-outer DDL line token count.");
                    }

                    ddlTerms.Add(new DiOuterHexRecord(new[]
                    {
                        tokens[2], tokens[3], tokens[4], tokens[5],
                        tokens[6], tokens[7], tokens[8], tokens[9]
                    }));
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected DI-outer driver output line '{lines[lineIndex]}'.");
            }
        }

        return new FortranDiOuterResult(ddTerms, ddlTerms);
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

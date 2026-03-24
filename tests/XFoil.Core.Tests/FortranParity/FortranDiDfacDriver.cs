using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranDiDfacCase(
    float Hk,
    float Hs,
    float Us,
    float Rt,
    float Msq,
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
    float RtT,
    float RtU,
    float RtMs,
    float MU,
    float MMs);

internal sealed record DiDfacHexRecord(IReadOnlyList<string> Values);

internal sealed record FortranDiDfacResult(
    IReadOnlyList<DiDfacHexRecord> Terms,
    IReadOnlyList<DiDfacHexRecord> Finals);

internal static class FortranDiDfacDriver
{
    public static FortranDiDfacResult RunBatch(IReadOnlyList<FortranDiDfacCase> cases)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "di_dfac_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-didfac-parity-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start DI-DFAC parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"DI-DFAC parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
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
        startInfo.ArgumentList.Add("didfac");

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

    private static string SerializeCases(IReadOnlyList<FortranDiDfacCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranDiDfacCase @case in cases)
        {
            builder.AppendLine($"{FormatSingle(@case.Hk)} {FormatSingle(@case.Hs)} {FormatSingle(@case.Us)} {FormatSingle(@case.Rt)} {FormatSingle(@case.Msq)}");
            builder.AppendLine($"{FormatSingle(@case.HkT)} {FormatSingle(@case.HkD)} {FormatSingle(@case.HkU)} {FormatSingle(@case.HkMs)}");
            builder.AppendLine($"{FormatSingle(@case.HsT)} {FormatSingle(@case.HsD)} {FormatSingle(@case.HsU)} {FormatSingle(@case.HsMs)}");
            builder.AppendLine($"{FormatSingle(@case.UsT)} {FormatSingle(@case.UsD)} {FormatSingle(@case.UsU)} {FormatSingle(@case.UsMs)}");
            builder.AppendLine($"{FormatSingle(@case.RtT)} {FormatSingle(@case.RtU)} {FormatSingle(@case.RtMs)}");
            builder.AppendLine($"{FormatSingle(@case.MU)} {FormatSingle(@case.MMs)}");
        }

        return builder.ToString();
    }

    private static FortranDiDfacResult ParseResult(string stdout, int expectedCases)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int caseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != expectedCases)
        {
            throw new InvalidOperationException($"DI-DFAC parity driver returned {caseCount} cases, expected {expectedCases}.");
        }

        var terms = new List<DiDfacHexRecord>(expectedCases);
        var finals = new List<DiDfacHexRecord>(expectedCases);

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "TERMS":
                    if (tokens.Length != 10)
                    {
                        throw new InvalidOperationException("Unexpected DI-DFAC TERMS line token count.");
                    }

                    terms.Add(new DiDfacHexRecord(new[]
                    {
                        tokens[2], tokens[3], tokens[4], tokens[5],
                        tokens[6], tokens[7], tokens[8], tokens[9]
                    }));
                    break;

                case "FINAL":
                    if (tokens.Length != 8)
                    {
                        throw new InvalidOperationException("Unexpected DI-DFAC FINAL line token count.");
                    }

                    finals.Add(new DiDfacHexRecord(new[]
                    {
                        tokens[2], tokens[3], tokens[4], tokens[5], tokens[6], tokens[7]
                    }));
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected DI-DFAC driver output line '{lines[lineIndex]}'.");
            }
        }

        return new FortranDiDfacResult(terms, finals);
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

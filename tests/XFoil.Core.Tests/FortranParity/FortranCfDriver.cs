using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranCfCase(
    int FlowType,
    float Hk,
    float Rt,
    float Msq,
    float HkT,
    float HkD,
    float HkU,
    float HkMs,
    float RtT,
    float RtU,
    float RtMs,
    float MU,
    float MMs,
    float RtRe);

internal sealed record CfHexTerms(int FlowType, int SelectedBranch, IReadOnlyList<string> Values);

internal sealed record CfHexDetail(int FlowType, IReadOnlyList<string> Values);

internal sealed record CfHexFinals(int FlowType, IReadOnlyList<string> Values);

internal sealed record FortranCfResult(
    IReadOnlyList<CfHexTerms> Terms,
    IReadOnlyList<CfHexDetail> Details,
    IReadOnlyList<CfHexFinals> Finals);

internal static class FortranCfDriver
{
    public static FortranCfResult RunBatch(IReadOnlyList<FortranCfCase> cases)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "cf_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-cf-parity-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start CF parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"CF parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
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
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "cf_parity_driver");

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
        startInfo.ArgumentList.Add("cf");

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

    private static string SerializeCases(IReadOnlyList<FortranCfCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranCfCase @case in cases)
        {
            builder.AppendLine(
                $"{@case.FlowType.ToString(CultureInfo.InvariantCulture)} {FormatSingle(@case.Hk)} {FormatSingle(@case.Rt)} {FormatSingle(@case.Msq)}");
            builder.AppendLine(
                $"{FormatSingle(@case.HkT)} {FormatSingle(@case.HkD)} {FormatSingle(@case.HkU)} {FormatSingle(@case.HkMs)}");
            builder.AppendLine(
                $"{FormatSingle(@case.RtT)} {FormatSingle(@case.RtU)} {FormatSingle(@case.RtMs)}");
            builder.AppendLine(
                $"{FormatSingle(@case.MU)} {FormatSingle(@case.MMs)} {FormatSingle(@case.RtRe)}");
        }

        return builder.ToString();
    }

    private static FortranCfResult ParseResult(string stdout, int expectedCases)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int caseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != expectedCases)
        {
            throw new InvalidOperationException($"CF parity driver returned {caseCount} cases, expected {expectedCases}.");
        }

        var terms = new List<CfHexTerms>(expectedCases);
        var details = new List<CfHexDetail>(expectedCases);
        var finals = new List<CfHexFinals>(expectedCases);

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "TERMS":
                    terms.Add(ParseTerms(tokens));
                    break;
                case "DTERM":
                    details.Add(ParseDetails(tokens));
                    break;
                case "FINAL":
                    finals.Add(ParseFinals(tokens));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected CF driver output line '{lines[lineIndex]}'.");
            }
        }

        return new FortranCfResult(terms, details, finals);
    }

    private static CfHexTerms ParseTerms(string[] tokens)
    {
        if (tokens.Length != 8)
        {
            throw new InvalidOperationException("Unexpected TERMS line token count.");
        }

        int flowType = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int selectedBranch = int.Parse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[4];
        Array.Copy(tokens, 4, values, 0, values.Length);
        return new CfHexTerms(flowType, selectedBranch, values);
    }

    private static CfHexFinals ParseFinals(string[] tokens)
    {
        if (tokens.Length != 9)
        {
            throw new InvalidOperationException("Unexpected FINAL line token count.");
        }

        int flowType = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[6];
        Array.Copy(tokens, 3, values, 0, values.Length);
        return new CfHexFinals(flowType, values);
    }

    private static CfHexDetail ParseDetails(string[] tokens)
    {
        if (tokens.Length != 22)
        {
            throw new InvalidOperationException("Unexpected DTERM line token count.");
        }

        int flowType = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[19];
        Array.Copy(tokens, 3, values, 0, values.Length);
        return new CfHexDetail(flowType, values);
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

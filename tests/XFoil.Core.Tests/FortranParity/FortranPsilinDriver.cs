using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranPsilinCase(
    int FieldNodeIndex,
    bool IncludeSourceTerms,
    bool IsSharpTrailingEdge,
    float FieldX,
    float FieldY,
    float FieldNormalX,
    float FieldNormalY,
    float FreestreamSpeed,
    float AngleOfAttackRadians,
    float TrailingEdgeGap,
    float TrailingEdgeAngleNormal,
    float TrailingEdgeAngleStreamwise,
    float[] X,
    float[] Y,
    float[] ArcLength,
    float[] PanelAngle,
    float[] VortexStrength,
    float[] SourceStrength);

internal sealed record PsilinHexRecord(string Kind, int PanelIndex, int Half, IReadOnlyList<string> Values);
internal sealed record PsilinPanelHexRecord(string Kind, int PanelIndex, IReadOnlyList<string> Values);
internal sealed record PsilinPairHexRecord(string Kind, int Jo, int Jp, IReadOnlyList<string> Values);
internal sealed record PsilinAccumHexRecord(string Stage, int Jo, int Jp, IReadOnlyList<string> Values);
internal sealed record PsilinResultHexRecord(string Kind, IReadOnlyList<string> Values);

internal sealed record FortranPsilinResult(
    IReadOnlyList<PsilinPanelHexRecord> PanelStates,
    IReadOnlyList<PsilinHexRecord> HalfTerms,
    IReadOnlyList<PsilinHexRecord> DzTerms,
    IReadOnlyList<PsilinHexRecord> DqTerms,
    IReadOnlyList<PsilinHexRecord> Segments,
    IReadOnlyList<PsilinPairHexRecord> VortexSegments,
    IReadOnlyList<PsilinPairHexRecord> TeCorrections,
    IReadOnlyList<PsilinAccumHexRecord> AccumStates,
    IReadOnlyList<PsilinResultHexRecord> ResultTerms,
    IReadOnlyList<PsilinResultHexRecord> Results,
    IReadOnlyList<string> FinalBits);

internal static class FortranPsilinDriver
{
    public static FortranPsilinResult RunCase(FortranPsilinCase @case)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "psilin_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-psilin-parity-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(inputPath, SerializeCase(@case), Encoding.ASCII);

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
                ?? throw new InvalidOperationException($"Failed to start PSILIN parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"PSILIN parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }

            return ParseResult(stdout);
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
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "psilin_parity_driver");

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
        startInfo.ArgumentList.Add("psilin");

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

    private static string SerializeCase(FortranPsilinCase @case)
    {
        var builder = new StringBuilder();
        builder.AppendLine("1");
        builder.Append(@case.X.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(@case.FieldNodeIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append((@case.IncludeSourceTerms ? 1 : 0).ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.AppendLine((@case.IsSharpTrailingEdge ? 1 : 0).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine($"{FormatSingle(@case.FieldX)} {FormatSingle(@case.FieldY)} {FormatSingle(@case.FieldNormalX)} {FormatSingle(@case.FieldNormalY)}");
        builder.AppendLine($"{FormatSingle(@case.FreestreamSpeed)} {FormatSingle(@case.AngleOfAttackRadians)}");
        builder.AppendLine($"{FormatSingle(@case.TrailingEdgeGap)} {FormatSingle(@case.TrailingEdgeAngleNormal)} {FormatSingle(@case.TrailingEdgeAngleStreamwise)}");

        for (int i = 0; i < @case.X.Length; i++)
        {
            builder.AppendLine(
                $"{FormatSingle(@case.X[i])} {FormatSingle(@case.Y[i])} {FormatSingle(@case.ArcLength[i])} {FormatSingle(@case.PanelAngle[i])} {FormatSingle(@case.VortexStrength[i])} {FormatSingle(@case.SourceStrength[i])}");
        }

        return builder.ToString();
    }

    private static FortranPsilinResult ParseResult(string stdout)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int caseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != 1)
        {
            throw new InvalidOperationException($"PSILIN parity driver returned {caseCount} cases, expected 1.");
        }

        var panelStates = new List<PsilinPanelHexRecord>();
        var halfTerms = new List<PsilinHexRecord>();
        var dzTerms = new List<PsilinHexRecord>();
        var dqTerms = new List<PsilinHexRecord>();
        var segments = new List<PsilinHexRecord>();
        var vortexSegments = new List<PsilinPairHexRecord>();
        var teCorrections = new List<PsilinPairHexRecord>();
        var accumStates = new List<PsilinAccumHexRecord>();
        var resultTerms = new List<PsilinResultHexRecord>();
        var results = new List<PsilinResultHexRecord>();
        string[]? finalBits = null;

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "PANEL":
                    panelStates.Add(ParsePanelTraceRecord("PANEL", tokens, 28));
                    break;
                case "HALF":
                    halfTerms.Add(ParseTraceRecord("HALF", tokens, 14));
                    break;
                case "DZ":
                    dzTerms.Add(ParseTraceRecord("DZ", tokens, 12));
                    break;
                case "DQ":
                    dqTerms.Add(ParseTraceRecord("DQ", tokens, 12));
                    break;
                case "SEG":
                    segments.Add(ParseTraceRecord("SEG", tokens, 65));
                    break;
                case "VOR":
                    vortexSegments.Add(ParsePairTraceRecord("VOR", tokens, 51));
                    break;
                case "TE":
                    teCorrections.Add(ParsePairTraceRecord("TE", tokens, 19));
                    break;
                case "ACCUM":
                    accumStates.Add(ParseAccumTraceRecord(tokens, 4));
                    break;
                case "RTERM":
                    resultTerms.Add(ParseResultTraceRecord("RTERM", tokens, 4));
                    break;
                case "RESULT":
                    results.Add(ParseResultTraceRecord("RESULT", tokens, 2));
                    break;
                case "FINAL":
                    if (tokens.Length != 21)
                    {
                        throw new InvalidOperationException($"Unexpected FINAL line '{lines[lineIndex]}'.");
                    }

                    finalBits = new string[18];
                    Array.Copy(tokens, 3, finalBits, 0, 18);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected PSILIN driver output line '{lines[lineIndex]}'.");
            }
        }

        return new FortranPsilinResult(
            panelStates,
            halfTerms,
            dzTerms,
            dqTerms,
            segments,
            vortexSegments,
            teCorrections,
            accumStates,
            resultTerms,
            results,
            finalBits ?? throw new InvalidOperationException("PSILIN parity driver did not emit a FINAL line."));
    }

    private static PsilinHexRecord ParseTraceRecord(string kind, string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 4)
        {
            throw new InvalidOperationException($"Unexpected {kind} line token count.");
        }

        int panelIndex = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int half = int.Parse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[valueCount];
        Array.Copy(tokens, 4, values, 0, valueCount);
        return new PsilinHexRecord(kind, panelIndex, half, values);
    }

    private static PsilinPanelHexRecord ParsePanelTraceRecord(string kind, string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 3)
        {
            throw new InvalidOperationException($"Unexpected {kind} line token count.");
        }

        int panelIndex = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[valueCount];
        Array.Copy(tokens, 3, values, 0, valueCount);
        return new PsilinPanelHexRecord(kind, panelIndex, values);
    }

    private static PsilinPairHexRecord ParsePairTraceRecord(string kind, string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 4)
        {
            throw new InvalidOperationException($"Unexpected {kind} line token count.");
        }

        int jo = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int jp = int.Parse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[valueCount];
        Array.Copy(tokens, 4, values, 0, valueCount);
        return new PsilinPairHexRecord(kind, jo, jp, values);
    }

    private static PsilinAccumHexRecord ParseAccumTraceRecord(string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 5)
        {
            throw new InvalidOperationException("Unexpected ACCUM line token count.");
        }

        string stage = tokens[2];
        int jo = int.Parse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int jp = int.Parse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var values = new string[valueCount];
        Array.Copy(tokens, 5, values, 0, valueCount);
        return new PsilinAccumHexRecord(stage, jo, jp, values);
    }

    private static PsilinResultHexRecord ParseResultTraceRecord(string kind, string[] tokens, int valueCount)
    {
        if (tokens.Length != valueCount + 3)
        {
            throw new InvalidOperationException($"Unexpected {kind} line token count.");
        }

        var values = new string[valueCount];
        Array.Copy(tokens, 3, values, 0, valueCount);
        return new PsilinResultHexRecord(kind, values);
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

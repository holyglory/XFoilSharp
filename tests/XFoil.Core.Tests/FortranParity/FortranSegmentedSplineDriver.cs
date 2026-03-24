using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal static class FortranSegmentedSplineDriver
{
    public static IReadOnlyList<FortranSplineCaseResult> RunBatch(
        IReadOnlyList<FortranSplineCase> cases,
        string? tracePath = null)
    {
        if (cases.Count == 0)
        {
            return Array.Empty<FortranSplineCaseResult>();
        }

        FortranSplineBuild.EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-spline-driver", "segmented_spline_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-segspl-parity-{Guid.NewGuid():N}.txt");

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
            if (!string.IsNullOrWhiteSpace(tracePath))
            {
                startInfo.Environment["XFOIL_SPLINE_TRACE_PATH"] = tracePath;
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start segmented spline parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Segmented spline parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }

            return ParseResults(stdout, cases);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private static string SerializeCases(IReadOnlyList<FortranSplineCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranSplineCase @case in cases)
        {
            builder.Append(@case.Parameters.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(@case.EvaluationParameters.Length.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            for (int i = 0; i < @case.Parameters.Length; i++)
            {
                builder.AppendLine($"{FormatSingle(@case.Parameters[i])} {FormatSingle(@case.Values[i])}");
            }

            foreach (float evaluation in @case.EvaluationParameters)
            {
                builder.AppendLine(FormatSingle(evaluation));
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<FortranSplineCaseResult> ParseResults(string stdout, IReadOnlyList<FortranSplineCase> cases)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int declaredCaseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (declaredCaseCount != cases.Count)
        {
            throw new InvalidOperationException($"Segmented spline parity driver returned {declaredCaseCount} cases, expected {cases.Count}.");
        }

        var results = new List<FortranSplineCaseResult>(cases.Count);
        foreach (FortranSplineCase @case in cases)
        {
            if (lineIndex >= lines.Length)
            {
                throw new InvalidOperationException(
                    $"Segmented spline parity driver output truncated before case header. Parsed {results.Count} of {cases.Count} cases.");
            }

            string[] headerTokens = SplitTokens(lines[lineIndex++], expectedCount: 2);
            int derivativeCount = int.Parse(headerTokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            int evaluationCount = int.Parse(headerTokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (derivativeCount != @case.Parameters.Length || evaluationCount != @case.EvaluationParameters.Length)
            {
                throw new InvalidOperationException(
                    $"Segmented spline parity driver header mismatch. Expected derivatives={@case.Parameters.Length}, evals={@case.EvaluationParameters.Length}; got derivatives={derivativeCount}, evals={evaluationCount}.");
            }

            var derivatives = new float[derivativeCount];
            for (int i = 0; i < derivativeCount; i++)
            {
                if (lineIndex >= lines.Length)
                {
                    throw new InvalidOperationException(
                        $"Segmented spline parity driver output truncated while reading derivative {i} of case {results.Count}.");
                }

                string[] tokens = SplitTokens(lines[lineIndex++], expectedCount: 2);
                derivatives[i] = ParseSingleBits(tokens[0]);
            }

            var evaluations = new float[evaluationCount];
            var evaluationDerivatives = new float[evaluationCount];
            for (int i = 0; i < evaluationCount; i++)
            {
                if (lineIndex >= lines.Length)
                {
                    throw new InvalidOperationException(
                        $"Segmented spline parity driver output truncated while reading evaluation {i} of case {results.Count}.");
                }

                string[] tokens = SplitTokens(lines[lineIndex++], expectedCount: 4);
                evaluations[i] = ParseSingleBits(tokens[0]);
                evaluationDerivatives[i] = ParseSingleBits(tokens[2]);
            }

            results.Add(new FortranSplineCaseResult(derivatives, evaluations, evaluationDerivatives));
        }

        return results;
    }

    private static string[] SplitTokens(string line, int expectedCount)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != expectedCount)
        {
            throw new InvalidOperationException($"Unexpected token count in segmented spline driver output line '{line}'. Expected {expectedCount}, got {tokens.Length}.");
        }

        return tokens;
    }

    private static float ParseSingleBits(string text)
    {
        uint bits = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static string FormatSingle(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

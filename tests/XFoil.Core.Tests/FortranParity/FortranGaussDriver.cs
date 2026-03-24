using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranGaussCase(float[,] Matrix, float[] RightHandSide);

internal sealed record FortranGaussSnapshot(
    string Phase,
    int PivotIndex,
    int RowIndex,
    float[,] Matrix,
    float[] RightHandSide);

internal static class FortranGaussDriver
{
    public static IReadOnlyList<IReadOnlyList<FortranGaussSnapshot>> RunBatch(IReadOnlyList<FortranGaussCase> cases)
    {
        if (cases.Count == 0)
        {
            return Array.Empty<IReadOnlyList<FortranGaussSnapshot>>();
        }

        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "gauss_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-gauss-parity-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start GAUSS parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"GAUSS parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
            }

            return ParseResults(stdout, cases.Count);
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
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "gauss_parity_driver");

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
        startInfo.ArgumentList.Add("gauss");

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

    private static string SerializeCases(IReadOnlyList<FortranGaussCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine(cases.Count.ToString(CultureInfo.InvariantCulture));

        foreach (FortranGaussCase @case in cases)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    builder.AppendLine(FormatSingleBits(@case.Matrix[row, column]));
                }
            }

            for (int row = 0; row < 4; row++)
            {
                builder.AppendLine(FormatSingleBits(@case.RightHandSide[row]));
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<IReadOnlyList<FortranGaussSnapshot>> ParseResults(string stdout, int caseCount)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int lineIndex = 0;
        int declaredCaseCount = int.Parse(lines[lineIndex++], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (declaredCaseCount != caseCount)
        {
            throw new InvalidOperationException($"GAUSS parity driver returned {declaredCaseCount} cases, expected {caseCount}.");
        }

        var cases = new List<List<FortranGaussSnapshot>>(caseCount);
        for (int caseIndex = 0; caseIndex < caseCount; caseIndex++)
        {
            cases.Add(new List<FortranGaussSnapshot>());
        }

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string[] tokens = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 24)
            {
                throw new InvalidOperationException($"Unexpected GAUSS driver output line '{lines[lineIndex]}'.");
            }

            int caseIndex = int.Parse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
            string phase = tokens[1];
            int pivotIndex = int.Parse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
            int rowIndex = int.Parse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture);

            var matrix = new float[4, 4];
            int tokenIndex = 4;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    matrix[row, column] = ParseSingleBits(tokens[tokenIndex++]);
                }
            }

            var rhs = new float[4];
            for (int row = 0; row < 4; row++)
            {
                rhs[row] = ParseSingleBits(tokens[tokenIndex++]);
            }

            cases[caseIndex].Add(new FortranGaussSnapshot(phase, pivotIndex, rowIndex, matrix, rhs));
        }

        return cases;
    }

    private static string FormatSingleBits(float value)
        => $"{BitConverter.SingleToInt32Bits(value):X8}";

    private static float ParseSingleBits(string text)
    {
        uint bits = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }
}

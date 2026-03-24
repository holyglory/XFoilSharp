using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFoil.Core.Tests.FortranParity;

internal sealed record FortranPswlinHalfCase(
    float X1,
    float X2,
    float Yy,
    float X1I,
    float X2I,
    float YyI,
    float X0,
    float Psum,
    float Pdif,
    float Psx0,
    float Psx1,
    float Psyy,
    float Dsio,
    float Dsim,
    float DxInv,
    float Qopi);

internal sealed record FortranPswlinHalfResult(
    string X0,
    string Psum,
    string Pdif,
    string Psx0,
    string Psx1,
    string Psyy,
    string Pdx0Term1,
    string Pdx0Term2,
    string Pdx0Term3,
    string Pdx0Accum1,
    string Pdx0Accum2,
    string Pdx0Numerator,
    string Pdx0Split,
    string Pdx0Direct,
    string Pdx1Term1,
    string Pdx1Term2,
    string Pdx1Term3,
    string Pdx1Accum1,
    string Pdx1Accum2,
    string Pdx1Numerator,
    string Pdx1Split,
    string Pdx1Direct,
    string Pdyy,
    string Psni,
    string Pdni,
    string DqJoLeft,
    string DqJoRight,
    string DqJoInner,
    string DqJo);

internal static class FortranPswlinHalfDriver
{
    public static FortranPswlinHalfResult RunCase(FortranPswlinHalfCase @case)
    {
        EnsureBuilt();

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string binaryPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "build-micro-drivers", "pswlin_half_parity_driver");
        string inputPath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-pswlinhalf-{Guid.NewGuid():N}.txt");

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
                ?? throw new InvalidOperationException($"Failed to start PSWLIN half parity driver: {binaryPath}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"PSWLIN half parity driver exited with code {process.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}");
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
        startInfo.ArgumentList.Add("pswlinhalf");

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

    private static string SerializeCase(FortranPswlinHalfCase @case)
    {
        var builder = new StringBuilder();
        builder.AppendLine("1");
        builder.AppendLine(FormatSingleBits(@case.X1));
        builder.AppendLine(FormatSingleBits(@case.X2));
        builder.AppendLine(FormatSingleBits(@case.Yy));
        builder.AppendLine(FormatSingleBits(@case.X1I));
        builder.AppendLine(FormatSingleBits(@case.X2I));
        builder.AppendLine(FormatSingleBits(@case.YyI));
        builder.AppendLine(FormatSingleBits(@case.X0));
        builder.AppendLine(FormatSingleBits(@case.Psum));
        builder.AppendLine(FormatSingleBits(@case.Pdif));
        builder.AppendLine(FormatSingleBits(@case.Psx0));
        builder.AppendLine(FormatSingleBits(@case.Psx1));
        builder.AppendLine(FormatSingleBits(@case.Psyy));
        builder.AppendLine(FormatSingleBits(@case.Dsio));
        builder.AppendLine(FormatSingleBits(@case.Dsim));
        builder.AppendLine(FormatSingleBits(@case.DxInv));
        builder.AppendLine(FormatSingleBits(@case.Qopi));
        return builder.ToString();
    }

    private static FortranPswlinHalfResult ParseResult(string stdout)
    {
        string[] lines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length != 2)
        {
            throw new InvalidOperationException($"Unexpected PSWLIN half driver output:{Environment.NewLine}{stdout}");
        }

        int caseCount = int.Parse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (caseCount != 1)
        {
            throw new InvalidOperationException($"PSWLIN half driver returned {caseCount} cases, expected 1.");
        }

        string[] tokens = lines[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 31 || !string.Equals(tokens[0], "CASE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected PSWLIN half driver result line '{lines[1]}'.");
        }

        return new FortranPswlinHalfResult(
            tokens[2], tokens[3], tokens[4], tokens[5], tokens[6], tokens[7],
            tokens[8], tokens[9], tokens[10], tokens[11], tokens[12], tokens[13], tokens[14], tokens[15],
            tokens[16], tokens[17], tokens[18], tokens[19], tokens[20], tokens[21], tokens[22], tokens[23],
            tokens[24], tokens[25], tokens[26], tokens[27], tokens[28], tokens[29], tokens[30]);
    }

    private static string FormatSingleBits(float value)
        => $"{BitConverter.SingleToInt32Bits(value):X8}";
}

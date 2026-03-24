using System;
using System.IO;
using Xunit;

namespace XFoil.Core.Tests.FortranParity;

public sealed class ParityDumpDivergenceAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsFirstParsedBlockMismatch()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referencePath = Path.Combine(tempDir, "reference_dump.txt");
            string managedPath = Path.Combine(tempDir, "managed_dump.txt");

            File.WriteAllText(
                referencePath,
                """
                === ITER 1 ===
                STATION IS= 1 IBL=   2 IV=   1
                BL_STATE x= 1.00000000E-03 Ue= 2.00000000E-01 th= 3.00000000E-03 ds= 4.00000000E-03 m= 8.00000000E-04
                VA_ROW1 1.00000000E+00 2.00000000E+00 3.00000000E+00
                VDEL_R 1.00000000E-02 2.00000000E-02 3.00000000E-02
                POST_CALC CL= 1.00000000E-01 CD= 2.00000000E-02 CM= 3.00000000E-03
                CONVERGED iter=1
                """);

            File.WriteAllText(
                managedPath,
                """
                === ITER 1 ===
                STATION IS= 1 IBL=   2 IV=   1
                BL_STATE x= 1.00000000E-03 Ue= 2.40000000E-01 th= 3.00000000E-03 ds= 4.00000000E-03 m= 8.00000000E-04
                VA_ROW1 1.00000000E+00 2.00000000E+00 3.00000000E+00
                VDEL_R 1.00000000E-02 2.00000000E-02 3.00000000E-02
                POST_CALC CL= 1.40000000E-01 CD= 2.50000000E-02 CM= 4.00000000E-03
                FINAL CL= 1.40000000E-01 CD= 2.50000000E-02 CM= 4.00000000E-03 CONVERGED=False ITER=20
                """);

            ParityDivergenceReport report = ParityDumpDivergenceAnalyzer.Analyze(referencePath, managedPath);

            Assert.NotNull(report.FirstDivergence);
            Assert.Equal("BL_STATE", report.FirstDivergence!.Category);
            Assert.Contains("Ue", report.FirstDivergence.Detail);
            Assert.Contains("delta     CL=", report.ToDisplayText());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

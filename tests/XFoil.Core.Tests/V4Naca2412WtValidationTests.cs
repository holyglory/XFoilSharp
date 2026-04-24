namespace XFoil.Core.Tests;

/// <summary>
/// V4 — NACA 2412 WT validation. Moderate-camber baseline (2 %
/// camber at 40 % chord, 12 % thick). Four-solver comparison at
/// Re=3×10⁶.
///
/// WT reference (Abbott &amp; Von Doenhoff):
///   α (°)   CL     CD
///   -4      -0.20  0.0075
///    0       0.20  0.0070
///    4       0.63  0.0075
///    8       1.00  0.0090
///   12       1.30  0.0130
/// </summary>
public class V4Naca2412WtValidationTests
{
    [Fact]
    public void V4_Naca2412_WriteWtComparisonTable()
    {
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("2412", -4.0, 3_000_000),
            new FourSolverComparisonRunner.Case("2412",  0.0, 3_000_000),
            new FourSolverComparisonRunner.Case("2412",  4.0, 3_000_000),
            new FourSolverComparisonRunner.Case("2412",  8.0, 3_000_000),
            new FourSolverComparisonRunner.Case("2412", 12.0, 3_000_000),
        };
        var wt = new (double CL, double CD)[]
        {
            (-0.20, 0.0075),
            ( 0.20, 0.0070),
            ( 0.63, 0.0075),
            ( 1.00, 0.0090),
            ( 1.30, 0.0130),
        };

        var rows = FourSolverComparisonRunner.Run(cases);
        var md = FourSolverComparisonRunner.ToMarkdownTable(rows, wt);

        string repoRoot = FindRepoRoot(
            System.IO.Directory.GetCurrentDirectory());
        string artifactDir = System.IO.Path.Combine(
            repoRoot, "tools", "validation-artifacts");
        System.IO.Directory.CreateDirectory(artifactDir);
        string outPath = System.IO.Path.Combine(artifactDir, "v4-naca2412.md");

        var header = new System.Text.StringBuilder();
        header.AppendLine("# V4 — NACA 2412 WT validation");
        header.AppendLine();
        header.AppendLine("Re=3×10⁶, M=0, free transition (nCrit=9), 161 panels.");
        header.AppendLine();
        header.AppendLine("WT reference: Abbott & Von Doenhoff, _Theory of Wing Sections_ "
            + "Appendix IV (NACA TR-824).");
        header.AppendLine();
        System.IO.File.WriteAllText(outPath, header.ToString() + md);

        var r4 = rows[2];
        Assert.True(double.IsFinite(r4.ThesisClosure.CL),
            "MSES should converge on NACA 2412 α=4°");
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = new System.IO.DirectoryInfo(startDir);
        while (dir != null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new System.InvalidOperationException(
            $"Could not find repo root from {startDir}");
    }
}

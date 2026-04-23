namespace XFoil.Core.Tests;

/// <summary>
/// V2 — NACA 0012 WT validation. Runs four-solver comparison vs
/// Abbott &amp; Von Doenhoff wind-tunnel data at Re=3×10⁶, free
/// transition. Writes a markdown table to
/// tools/validation-artifacts/v2-naca0012.md for committing.
///
/// WT reference values (Abbott &amp; Von Doenhoff, Theory of Wing
/// Sections Appendix IV, NACA TR-824):
///   α (°)  CL     CD
///   0      0.00   0.0070
///   2      0.22   0.0070
///   4      0.44   0.0075
///   6      0.65   0.0085
///   8      0.84   0.0100
///   10     1.02   0.0125
/// </summary>
public class V2Naca0012WtValidationTests
{
    [Fact]
    public void V2_Naca0012_WriteWtComparisonTable()
    {
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("0012", 0.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 2.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 4.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 6.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 8.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 10.0, 3_000_000),
        };
        var wt = new (double CL, double CD)[]
        {
            (0.00, 0.0070),
            (0.22, 0.0070),
            (0.44, 0.0075),
            (0.65, 0.0085),
            (0.84, 0.0100),
            (1.02, 0.0125),
        };

        var rows = FourSolverComparisonRunner.Run(cases);
        var md = FourSolverComparisonRunner.ToMarkdownTable(rows, wt);

        // Write artifact (repo-relative from the test bin dir).
        string repoRoot = FindRepoRoot(
            System.IO.Directory.GetCurrentDirectory());
        string artifactDir = System.IO.Path.Combine(
            repoRoot, "tools", "validation-artifacts");
        System.IO.Directory.CreateDirectory(artifactDir);
        string outPath = System.IO.Path.Combine(artifactDir, "v2-naca0012.md");

        var header = new System.Text.StringBuilder();
        header.AppendLine("# V2 — NACA 0012 WT validation");
        header.AppendLine();
        header.AppendLine("Re=3×10⁶, M=0, free transition (nCrit=9), 161 panels.");
        header.AppendLine();
        header.AppendLine("WT reference: Abbott & Von Doenhoff, _Theory of Wing Sections_ "
            + "Appendix IV (NACA TR-824).");
        header.AppendLine();
        System.IO.File.WriteAllText(outPath, header.ToString() + md);

        // Minimal assertion: at least one solver gives a finite CL at α=4°.
        var r4 = rows[2];
        Assert.True(
            double.IsFinite(r4.Parity.CL)
            || double.IsFinite(r4.Double.CL)
            || double.IsFinite(r4.Modern.CL)
            || double.IsFinite(r4.Mses.CL),
            "At least one solver should produce a finite CL on NACA 0012 α=4°");
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

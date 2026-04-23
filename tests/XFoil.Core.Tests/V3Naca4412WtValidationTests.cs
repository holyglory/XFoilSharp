namespace XFoil.Core.Tests;

/// <summary>
/// V3 — NACA 4412 WT validation (cambered baseline). Four-solver
/// comparison at Re=3×10⁶, free transition, vs Abbott &amp; Von
/// Doenhoff / Pinkerton WT data.
///
/// WT reference (approx, from Abbott Fig. 6.7 / NACA TR-824):
///   α (°)   CL     CD
///   -4      -0.04  0.0078
///    0       0.40  0.0075
///    4       0.83  0.0080
///    8       1.20  0.0095
///   12       1.48  0.0140
/// </summary>
public class V3Naca4412WtValidationTests
{
    [Fact]
    public void V3_Naca4412_WriteWtComparisonTable()
    {
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("4412", -4.0, 3_000_000),
            new FourSolverComparisonRunner.Case("4412",  0.0, 3_000_000),
            new FourSolverComparisonRunner.Case("4412",  4.0, 3_000_000),
            new FourSolverComparisonRunner.Case("4412",  8.0, 3_000_000),
            new FourSolverComparisonRunner.Case("4412", 12.0, 3_000_000),
        };
        var wt = new (double CL, double CD)[]
        {
            (-0.04, 0.0078),
            ( 0.40, 0.0075),
            ( 0.83, 0.0080),
            ( 1.20, 0.0095),
            ( 1.48, 0.0140),
        };

        var rows = FourSolverComparisonRunner.Run(cases);
        var md = FourSolverComparisonRunner.ToMarkdownTable(rows, wt);

        string repoRoot = FindRepoRoot(
            System.IO.Directory.GetCurrentDirectory());
        string artifactDir = System.IO.Path.Combine(
            repoRoot, "tools", "validation-artifacts");
        System.IO.Directory.CreateDirectory(artifactDir);
        string outPath = System.IO.Path.Combine(artifactDir, "v3-naca4412.md");

        var header = new System.Text.StringBuilder();
        header.AppendLine("# V3 — NACA 4412 WT validation");
        header.AppendLine();
        header.AppendLine("Re=3×10⁶, M=0, free transition (nCrit=9), 161 panels.");
        header.AppendLine();
        header.AppendLine("WT reference: Abbott & Von Doenhoff, _Theory of Wing Sections_ "
            + "Appendix IV (NACA TR-824) / Pinkerton.");
        header.AppendLine();
        System.IO.File.WriteAllText(outPath, header.ToString() + md);

        // At least MSES should converge at α=4° (easy cambered case).
        var r4 = rows[2];
        Assert.True(
            double.IsFinite(r4.Mses.CL),
            "MSES should produce a finite CL on NACA 4412 α=4°");
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

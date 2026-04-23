namespace XFoil.Core.Tests;

/// <summary>
/// V1 — tests for the comparison runner (self-check before V2–V4
/// use it to produce validation tables).
/// </summary>
public class FourSolverComparisonRunnerTests
{
    [Fact]
    public void Run_SingleCase_ReturnsRowWithAllFourResults()
    {
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("0012", 0.0, 3_000_000),
        };
        var rows = FourSolverComparisonRunner.Run(cases);
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal("0012", r.Airfoil);
        // α=0 symmetric: solvers that DO produce a finite result
        // must give ~0 CL. Solvers that throw/NaN are captured as
        // NaN — which is still valuable data for the validation
        // table (tells us the solver failed on this case).
        foreach (var s in new[] { r.Parity, r.Double, r.Modern, r.Mses })
        {
            if (double.IsFinite(s.CL))
                Assert.InRange(s.CL, -0.05, 0.05);
        }
    }

    [Fact]
    public void Run_MultipleAlphas_OrdersRows()
    {
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("0012", 0.0, 3_000_000),
            new FourSolverComparisonRunner.Case("0012", 4.0, 3_000_000),
        };
        var rows = FourSolverComparisonRunner.Run(cases);
        Assert.Equal(2, rows.Length);
        Assert.Equal(0.0, rows[0].AlphaDeg);
        Assert.Equal(4.0, rows[1].AlphaDeg);
    }

    [Fact]
    public void ToMarkdownTable_ProducesWellFormedMarkdown()
    {
        var rows = new[]
        {
            new FourSolverComparisonRunner.ComparisonRow(
                "0012", 4.0, 3_000_000, 0.0,
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true)),
        };
        var md = FourSolverComparisonRunner.ToMarkdownTable(rows);
        Assert.Contains("| Airfoil |", md);
        Assert.Contains("| 0012 |", md);
        Assert.Contains("0.480", md);  // CL formatted
    }

    [Fact]
    public void ToMarkdownTable_WithWtData_AddsWtColumn()
    {
        var rows = new[]
        {
            new FourSolverComparisonRunner.ComparisonRow(
                "0012", 4.0, 3_000_000, 0.0,
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true),
                new FourSolverComparisonRunner.SolverResult(0.48, 0.007, -0.01, true)),
        };
        var wt = new[] { (CL: 0.45, CD: 0.008) };
        var md = FourSolverComparisonRunner.ToMarkdownTable(rows, wt);
        Assert.Contains("WT CL", md);
        Assert.Contains("WT CD", md);
        Assert.Contains("0.450", md);
    }

    [Fact]
    public void Run_MsesPath_ConvergesOnEasyCase()
    {
        // MSES/thesis-closure is the path we're validating most
        // strongly — other XFoil paths may have bugs / NaN
        // (which is itself information for the comparison table).
        var cases = new[]
        {
            new FourSolverComparisonRunner.Case("0012", 4.0, 3_000_000),
        };
        var rows = FourSolverComparisonRunner.Run(cases);
        var r = rows[0];
        Assert.True(r.Mses.Converged, "MSES/thesis-closure should converge");
        Assert.InRange(r.Mses.CL, 0.3, 0.6);
    }
}

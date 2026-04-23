using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// V1 — Four-solver comparison runner. Given a common airfoil/α/Re
/// grid, runs all four viscous paths (Parity, Double, Modern, MSES/
/// thesis-closure) and returns CL/CD/CM for each on every row.
///
/// Used by V2/V3/V4 to produce the WT-comparison markdown tables.
/// </summary>
public static class FourSolverComparisonRunner
{
    public readonly record struct SolverResult(
        double CL, double CD, double CM, bool Converged);

    public readonly record struct ComparisonRow(
        string Airfoil, double AlphaDeg, double Reynolds, double Mach,
        SolverResult Parity, SolverResult Double, SolverResult Modern,
        SolverResult Mses);

    public readonly record struct Case(
        string Naca, double AlphaDeg, double Reynolds, double Mach = 0.0);

    public static ComparisonRow[] Run(
        System.Collections.Generic.IEnumerable<Case> cases,
        int pointCount = 161,
        double nCrit = 9.0)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var results = new System.Collections.Generic.List<ComparisonRow>();
        foreach (var c in cases)
        {
            var geom = gen.Generate4DigitClassic(c.Naca, pointCount: pointCount);

            // Float-parity path matches Fortran REAL*4 behavior and only
            // converges when invoked with the legacy-mode flags below —
            // same configuration used by the 4455-case ParallelPolarCompare
            // parity sweep. Modern defaults give it a Newton init it can't
            // recover from → silent NaN.
            var paritySettings = new AnalysisSettings(
                panelCount: pointCount - 1,
                freestreamVelocity: 1.0,
                machNumber: c.Mach,
                reynoldsNumber: c.Reynolds,
                criticalAmplificationFactor: nCrit,
                useExtendedWake: false,
                useLegacyBoundaryLayerInitialization: true,
                useLegacyPanelingPrecision: true,
                useLegacyStreamfunctionKernelPrecision: true,
                useLegacyWakeSourceKernelPrecision: true,
                useModernTransitionCorrections: false,
                maxViscousIterations: 80,
                viscousConvergenceTolerance: 1e-4);

            // Double/Modern/ThesisClosure run against their own canonical
            // configuration (modern defaults + panel count to match the
            // airfoil generator output).
            var modernSettings = new AnalysisSettings(
                panelCount: pointCount,
                freestreamVelocity: 1.0,
                machNumber: c.Mach,
                reynoldsNumber: c.Reynolds,
                nCritUpper: nCrit,
                nCritLower: nCrit);

            var parity = RunSafely(() =>
                new XFoil.Solver.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, paritySettings));
            var doubleTree = RunSafely(() =>
                new XFoil.Solver.Double.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, modernSettings));
            var modern = RunSafely(() =>
                new XFoil.Solver.Modern.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, modernSettings));
            var mses = RunSafely(() =>
                new ThesisClosureAnalysisService().AnalyzeViscous(geom, c.AlphaDeg, modernSettings));

            results.Add(new ComparisonRow(
                c.Naca, c.AlphaDeg, c.Reynolds, c.Mach,
                parity, doubleTree, modern, mses));
        }
        return results.ToArray();
    }

    private static SolverResult RunSafely(System.Func<ViscousAnalysisResult> run)
    {
        try
        {
            var r = run();
            return new SolverResult(
                CL: r.LiftCoefficient,
                CD: r.DragDecomposition.CD,
                CM: r.MomentCoefficient,
                Converged: r.Converged);
        }
        catch
        {
            return new SolverResult(
                double.NaN, double.NaN, double.NaN, false);
        }
    }

    /// <summary>
    /// Renders a ComparisonRow[] as a markdown table. Used by V2–V4
    /// to emit validation artifacts.
    /// </summary>
    public static string ToMarkdownTable(
        ComparisonRow[] rows,
        (double CL, double CD)[]? wt = null)
    {
        var sb = new System.Text.StringBuilder();
        if (wt != null && wt.Length != rows.Length)
            throw new System.ArgumentException("WT length mismatch");

        sb.AppendLine("| Airfoil | α° | Re | Parity CL | Double CL | Modern CL | Mses CL"
            + (wt != null ? " | WT CL" : "") + " |");
        sb.AppendLine("|---------|-----|-----|-----------|-----------|-----------|---------"
            + (wt != null ? "|--------" : "") + "|");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            sb.Append($"| {r.Airfoil} | {r.AlphaDeg,5:F1} | {r.Reynolds:0.0e0} | "
                + $"{F(r.Parity.CL)} | {F(r.Double.CL)} | {F(r.Modern.CL)} | {F(r.Mses.CL)}");
            if (wt != null)
                sb.Append($" | {wt[i].CL:F3}");
            sb.AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("| Airfoil | α° | Re | Parity CD | Double CD | Modern CD | Mses CD"
            + (wt != null ? " | WT CD" : "") + " |");
        sb.AppendLine("|---------|-----|-----|-----------|-----------|-----------|---------"
            + (wt != null ? "|--------" : "") + "|");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            sb.Append($"| {r.Airfoil} | {r.AlphaDeg,5:F1} | {r.Reynolds:0.0e0} | "
                + $"{F(r.Parity.CD, 4)} | {F(r.Double.CD, 4)} | {F(r.Modern.CD, 4)} | {F(r.Mses.CD, 4)}");
            if (wt != null)
                sb.Append($" | {wt[i].CD:F4}");
            sb.AppendLine(" |");
        }
        return sb.ToString();
    }

    private static string F(double x, int digits = 3)
        => double.IsFinite(x) ? x.ToString($"F{digits}") : "—";
}

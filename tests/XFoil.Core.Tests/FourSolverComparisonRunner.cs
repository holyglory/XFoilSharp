using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// V1 — Multi-branch comparison runner. Given a common airfoil/α/Re
/// grid, runs five configurations and returns CL/CD/CM for each:
///   Parity          = Float assembly + legacy flags (canonical bit-exact)
///   Double          = Double assembly + legacy flags (honest precision twin of Parity)
///   ModernDouble    = Double assembly + modern flags (simplified init pipeline)
///   ModernAssembly  = Modern assembly + modern flags (Double + multi-start rescue)
///   ThesisClosure   = ThesisClosureSolver assembly (linear-vortex + thesis BL)
/// Used by V2/V3/V4 to produce the WT-comparison markdown tables.
/// </summary>
public static class FourSolverComparisonRunner
{
    public readonly record struct SolverResult(
        double CL, double CD, double CM, bool Converged);

    public readonly record struct ComparisonRow(
        string Airfoil, double AlphaDeg, double Reynolds, double Mach,
        SolverResult Parity,
        SolverResult Double,
        SolverResult ModernDouble,
        SolverResult ModernAssembly,
        SolverResult ThesisClosure);

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

            // Legacy-flags configuration — matches the 4455-case
            // ParallelPolarCompare parity sweep. Parity and Double
            // (the honest precision twin) use this; only accumulator
            // precision (REAL*4 vs double) differs between them.
            var legacySettings = new AnalysisSettings(
                panelCount: pointCount - 1,
                freestreamVelocity: 1.0,
                machNumber: c.Mach,
                reynoldsNumber: c.Reynolds,
                criticalAmplificationFactor: nCrit,
                viscousSolverMode: ViscousSolverMode.TrustRegion,
                useExtendedWake: false,
                useLegacyBoundaryLayerInitialization: true,
                useLegacyPanelingPrecision: true,
                useLegacyStreamfunctionKernelPrecision: true,
                useLegacyWakeSourceKernelPrecision: true,
                useModernTransitionCorrections: false,
                maxViscousIterations: 80,
                viscousConvergenceTolerance: 1e-4);

            // Modern-flags configuration — activates the simplified
            // init pipeline (constant-Cτ post-transition, skipped MRCHUE
            // extrapolation, pre-entry DSLIM clamp). Used for
            // ModernDouble, ModernAssembly, and ThesisClosure.
            var modernSettings = new AnalysisSettings(
                panelCount: pointCount,
                freestreamVelocity: 1.0,
                machNumber: c.Mach,
                reynoldsNumber: c.Reynolds,
                nCritUpper: nCrit,
                nCritLower: nCrit);

            var parity = RunSafely(() =>
                new XFoil.Solver.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, legacySettings));
            var doubleLegacy = RunSafely(() =>
                new XFoil.Solver.Double.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, legacySettings));
            var modernDouble = RunSafely(() =>
                new XFoil.Solver.Double.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, modernSettings));
            var modernAssembly = RunSafely(() =>
                new XFoil.Solver.Modern.Services.AirfoilAnalysisService()
                    .AnalyzeViscous(geom, c.AlphaDeg, modernSettings));
            var thesisClosure = RunSafely(() =>
                new ThesisClosureAnalysisService().AnalyzeViscous(geom, c.AlphaDeg, modernSettings));

            results.Add(new ComparisonRow(
                c.Naca, c.AlphaDeg, c.Reynolds, c.Mach,
                parity, doubleLegacy, modernDouble, modernAssembly, thesisClosure));
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
    ///
    /// Non-converged results are emitted as "—" regardless of whether
    /// the returned value happens to be finite — a non-converged
    /// Newton's last iterate is not an answer, it's noise.
    /// </summary>
    public static string ToMarkdownTable(
        ComparisonRow[] rows,
        (double CL, double CD)[]? wt = null)
    {
        var sb = new System.Text.StringBuilder();
        if (wt != null && wt.Length != rows.Length)
            throw new System.ArgumentException("WT length mismatch");

        sb.AppendLine("| Airfoil | α° | Re | Float parity CL | Double CL | Modern Double CL | Modern asm CL | ThesisClosure CL"
            + (wt != null ? " | WT CL" : "") + " |");
        sb.AppendLine("|---------|-----|-----|-----------------|-----------|------------------|---------------|-----------------"
            + (wt != null ? "|--------" : "") + "|");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            sb.Append($"| {r.Airfoil} | {r.AlphaDeg,5:F1} | {r.Reynolds:0.0e0} | "
                + $"{Fmt(r.Parity, r.Parity.CL)} | {Fmt(r.Double, r.Double.CL)} | "
                + $"{Fmt(r.ModernDouble, r.ModernDouble.CL)} | {Fmt(r.ModernAssembly, r.ModernAssembly.CL)} | "
                + $"{Fmt(r.ThesisClosure, r.ThesisClosure.CL)}");
            if (wt != null)
                sb.Append($" | {wt[i].CL:F3}");
            sb.AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("| Airfoil | α° | Re | Float parity CD | Double CD | Modern Double CD | Modern asm CD | ThesisClosure CD"
            + (wt != null ? " | WT CD" : "") + " |");
        sb.AppendLine("|---------|-----|-----|-----------------|-----------|------------------|---------------|-----------------"
            + (wt != null ? "|--------" : "") + "|");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            sb.Append($"| {r.Airfoil} | {r.AlphaDeg,5:F1} | {r.Reynolds:0.0e0} | "
                + $"{Fmt(r.Parity, r.Parity.CD, 4)} | {Fmt(r.Double, r.Double.CD, 4)} | "
                + $"{Fmt(r.ModernDouble, r.ModernDouble.CD, 4)} | {Fmt(r.ModernAssembly, r.ModernAssembly.CD, 4)} | "
                + $"{Fmt(r.ThesisClosure, r.ThesisClosure.CD, 4)}");
            if (wt != null)
                sb.Append($" | {wt[i].CD:F4}");
            sb.AppendLine(" |");
        }
        return sb.ToString();
    }

    private static string Fmt(SolverResult r, double value, int digits = 3)
    {
        if (!r.Converged) return "—";
        return double.IsFinite(value) ? value.ToString($"F{digits}") : "—";
    }
}

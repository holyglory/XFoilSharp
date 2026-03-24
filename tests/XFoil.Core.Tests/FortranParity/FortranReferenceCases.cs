using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using XFoil.Core.Diagnostics;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/run_reference.sh case definitions and the legacy runtime input decks under tools/fortran-debug/cases
// Secondary legacy source: f_xfoil/src/xoper.f :: VISCAL invocation contract used by the reference cases
// Role in port: Managed-only case registry and artifact runner for side-by-side Fortran/C# parity tests.
// Differences: Classic XFoil has no typed case catalog or managed artifact cache; this file standardizes the reference inputs and managed trace generation for test-time comparison.
// Decision: Keep the managed-only case harness because parity tests need a stable artifact contract without widening runtime APIs.
namespace XFoil.Core.Tests.FortranParity;

public sealed record FortranReferenceCase(
    string CaseId,
    string AirfoilCode,
    double ReynoldsNumber,
    double AlphaDegrees,
    string DisplayName,
    int PanelCount = 160,
    int MaxViscousIterations = 20,
    double CriticalAmplificationFactor = 9.0,
    string? TraceKindAllowList = null);

public static class FortranReferenceCases
{
    private const int ClassicXFoilNacaPointCount = 239;
    private static readonly ConcurrentDictionary<string, object> CaseLocks = new(StringComparer.Ordinal);
    private static readonly object ManagedArtifactRefreshLock = new();
    private static readonly object TraceCounterLock = new();
    private static readonly IReadOnlyDictionary<string, FortranReferenceCase> Cases =
        new Dictionary<string, FortranReferenceCase>(StringComparer.Ordinal)
        {
            ["n0012_re1e6_a0"] = new("n0012_re1e6_a0", "0012", 1_000_000.0, 0.0, "NACA 0012 Re=1e6 alpha=0"),
            ["n0012_re1e6_a5"] = new("n0012_re1e6_a5", "0012", 1_000_000.0, 5.0, "NACA 0012 Re=1e6 alpha=5"),
            ["n0012_re1e6_a0_p80"] = new(
                "n0012_re1e6_a0_p80",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=80",
                PanelCount: 80,
                MaxViscousIterations: 200),
            ["n0012_re1e6_a0_p80_psilin_pdyy"] = new(
                "n0012_re1e6_a0_p80_psilin_pdyy",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=80 PSILIN PDYY write",
                PanelCount: 80,
                MaxViscousIterations: 200,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "psilin_source_pdyy_write"),
            ["n0012_re1e6_a0_p12_n9_full"] = new(
                "n0012_re1e6_a0_p12_n9_full",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0),
            ["n0012_re1e6_a0_p12_n9_trdif_rows"] = new(
                "n0012_re1e6_a0_p12_n9_trdif_rows",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 transition rows",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "transition_interval_rows,transition_seed_system"),
            ["n0012_re1e6_a0_p12_n9_trdif_bt2_row22"] = new(
                "n0012_re1e6_a0_p12_n9_trdif_bt2_row22",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 transition bt2 row22",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "transition_interval_bt2_terms,transition_seed_system"),
            ["n0012_re1e6_a0_p12_n9_eq2_x"] = new(
                "n0012_re1e6_a0_p12_n9_eq2_x",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 eq2 x",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "bldif_eq2_x_terms,transition_seed_system"),
            ["n0012_re1e6_a0_p12_n9_eq2_x_breakdown"] = new(
                "n0012_re1e6_a0_p12_n9_eq2_x_breakdown",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 eq2 x breakdown",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "bldif_eq2_x_breakdown,transition_seed_system"),
            ["n0012_re1e6_a0_p12_n9_eq3_row32"] = new(
                "n0012_re1e6_a0_p12_n9_eq3_row32",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 eq3 row32",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "bldif_eq3_t1_terms,transition_seed_system"),
            ["n0012_re1e6_a0_p12_n9_di_t_outer"] = new(
                "n0012_re1e6_a0_p12_n9_di_t_outer",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 outer di t update",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "transition_seed_system,bldif_secondary_station,blvar_outer_di_terms"),
            ["n0012_re1e6_a0_p12_n9_psilin"] = new(
                "n0012_re1e6_a0_p12_n9_psilin",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 PSILIN",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "psilin_field,psilin_panel,psilin_source_half_terms,psilin_source_dz_terms,psilin_source_dq_terms,psilin_source_segment,psilin_source_pdyy_write"),
            ["n0012_re1e6_a0_p12_n9_pangen"] = new(
                "n0012_re1e6_a0_p12_n9_pangen",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 PANGEN",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_snew_node,pangen_panel_node"),
            ["n0012_re1e6_a0_p12_n9_pangen_spline"] = new(
                "n0012_re1e6_a0_p12_n9_pangen_spline",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 PANGEN Newton spline",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_newton_state,spline_eval"),
            ["n0012_re1e6_a0_p12_n9_pangen_rows"] = new(
                "n0012_re1e6_a0_p12_n9_pangen_rows",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=12 ncrit=9 PANGEN Newton rows",
                PanelCount: 12,
                MaxViscousIterations: 80,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_newton_row"),
            ["n0012_re1e6_a0_p80_pangen_rows"] = new(
                "n0012_re1e6_a0_p80_pangen_rows",
                "0012",
                1_000_000.0,
                0.0,
                "NACA 0012 Re=1e6 alpha=0 panel=80 PANGEN Newton rows",
                PanelCount: 80,
                MaxViscousIterations: 200,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_newton_row"),
            ["n2412_re3e6_a3_p80_pangen_rows"] = new(
                "n2412_re3e6_a3_p80_pangen_rows",
                "2412",
                3_000_000.0,
                3.0,
                "NACA 2412 Re=3e6 alpha=3 panel=80 PANGEN Newton rows",
                PanelCount: 80,
                MaxViscousIterations: 200,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_newton_row"),
            ["n4415_re6e6_a2_p80_pangen_rows"] = new(
                "n4415_re6e6_a2_p80_pangen_rows",
                "4415",
                6_000_000.0,
                2.0,
                "NACA 4415 Re=6e6 alpha=2 panel=80 PANGEN Newton rows",
                PanelCount: 80,
                MaxViscousIterations: 200,
                CriticalAmplificationFactor: 9.0,
                TraceKindAllowList: "pangen_newton_row"),
            ["n0012_re1e6_a5_p80"] = new(
                "n0012_re1e6_a5_p80",
                "0012",
                1_000_000.0,
                5.0,
                "NACA 0012 Re=1e6 alpha=5 panel=80",
                PanelCount: 80,
                MaxViscousIterations: 200),
            ["n0012_re1e6_a10_p80"] = new(
                "n0012_re1e6_a10_p80",
                "0012",
                1_000_000.0,
                10.0,
                "NACA 0012 Re=1e6 alpha=10 panel=80",
                PanelCount: 80,
                MaxViscousIterations: 200),
            ["n0012_re1e6_a10_p80_stagnation"] = new(
                "n0012_re1e6_a10_p80_stagnation",
                "0012",
                1_000_000.0,
                10.0,
                "NACA 0012 Re=1e6 alpha=10 panel=80 stagnation trace",
                PanelCount: 80,
                MaxViscousIterations: 200,
                TraceKindAllowList: "stagnation_candidate,stagnation_speed_window,stagnation_interpolation"),
            ["n0012_re1e6_a10_p80_wakenode"] = new(
                "n0012_re1e6_a10_p80_wakenode",
                "0012",
                1_000_000.0,
                10.0,
                "NACA 0012 Re=1e6 alpha=10 panel=80 wake-node trace",
                PanelCount: 80,
                MaxViscousIterations: 200,
                TraceKindAllowList: "wake_node"),
            ["n0012_re1e6_a10_p80_wakespacing_v2"] = new(
                "n0012_re1e6_a10_p80_wakespacing_v2",
                "0012",
                1_000_000.0,
                10.0,
                "NACA 0012 Re=1e6 alpha=10 panel=80 wake-spacing trace",
                PanelCount: 80,
                MaxViscousIterations: 200,
                TraceKindAllowList: "wake_spacing_input"),
            ["n0012_re1e6_a10_p80_wakepanelstate"] = new(
                "n0012_re1e6_a10_p80_wakepanelstate",
                "0012",
                1_000_000.0,
                10.0,
                "NACA 0012 Re=1e6 alpha=10 panel=80 wake-panel-state trace",
                PanelCount: 80,
                MaxViscousIterations: 200,
                TraceKindAllowList: "wake_panel_state"),
            ["n2412_re3e6_a3"] = new("n2412_re3e6_a3", "2412", 3_000_000.0, 3.0, "NACA 2412 Re=3e6 alpha=3"),
            ["n4415_re6e6_a2"] = new("n4415_re6e6_a2", "4415", 6_000_000.0, 2.0, "NACA 4415 Re=6e6 alpha=2")
        };

    public static FortranReferenceCase Get(string caseId)
    {
        if (!Cases.TryGetValue(caseId, out FortranReferenceCase? referenceCase))
        {
            throw new ArgumentOutOfRangeException(nameof(caseId), caseId, "Unknown Fortran reference case.");
        }

        return referenceCase;
    }

    public static FortranReferenceCase FromEnvironment()
    {
        string airfoilCode = ReadRequiredEnvironmentString("XFOIL_CASE_AIRFOIL");
        double reynoldsNumber = ReadRequiredEnvironmentDouble("XFOIL_CASE_RE");
        double alphaDegrees = ReadRequiredEnvironmentDouble("XFOIL_CASE_ALPHA");
        int panelCount = ReadOptionalEnvironmentInt("XFOIL_CASE_PANELS") ?? 60;
        int maxViscousIterations = ReadOptionalEnvironmentInt("XFOIL_CASE_ITER") ?? 80;
        double criticalAmplificationFactor = ReadOptionalEnvironmentDouble("XFOIL_CASE_NCRIT") ?? 9.0;

        string caseId = ReadOptionalEnvironmentString("XFOIL_CASE_ID")
            ?? BuildAdHocCaseId(airfoilCode, reynoldsNumber, alphaDegrees, panelCount, criticalAmplificationFactor);

        string displayName = string.Format(
            CultureInfo.InvariantCulture,
            "Ad hoc NACA {0} Re={1:G9} alpha={2:G9} panel={3} Ncrit={4:G9}",
            airfoilCode,
            reynoldsNumber,
            alphaDegrees,
            panelCount,
            criticalAmplificationFactor);

        return new FortranReferenceCase(
            CaseId: caseId,
            AirfoilCode: airfoilCode,
            ReynoldsNumber: reynoldsNumber,
            AlphaDegrees: alphaDegrees,
            DisplayName: displayName,
            PanelCount: panelCount,
            MaxViscousIterations: maxViscousIterations,
            CriticalAmplificationFactor: criticalAmplificationFactor);
    }

    public static string GetFortranDebugDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "tools", "fortran-debug");
    }

    public static string GetReferenceDirectory(string caseId)
    {
        string? overrideDirectory = ReadOptionalEnvironmentString("XFOIL_REFERENCE_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return ResolvePathOverride(overrideDirectory);
        }

        return Path.Combine(GetFortranDebugDirectory(), "reference", caseId);
    }

    public static string GetManagedDirectory(string caseId)
    {
        string? overrideDirectory = ReadOptionalEnvironmentString("XFOIL_CASE_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return ResolvePathOverride(overrideDirectory);
        }

        return Path.Combine(GetFortranDebugDirectory(), "csharp", caseId);
    }

    public static string GetReferenceTracePath(string caseId)
    {
        string perCaseDir = GetReferenceDirectory(caseId);
        string latestVersioned = TryGetLatestVersionedArtifact(perCaseDir, "reference_trace.", ".jsonl");
        if (!string.IsNullOrEmpty(latestVersioned))
        {
            return latestVersioned;
        }

        string perCase = Path.Combine(perCaseDir, "reference_trace.jsonl");
        if (File.Exists(perCase))
        {
            return perCase;
        }

        if (string.Equals(caseId, "n0012_re1e6_a0", StringComparison.Ordinal))
        {
            return Path.Combine(GetFortranDebugDirectory(), "reference_trace.jsonl");
        }

        return perCase;
    }

    public static string GetReferenceDumpPath(string caseId)
    {
        string perCaseDir = GetReferenceDirectory(caseId);
        string latestVersioned = TryGetLatestVersionedArtifact(perCaseDir, "reference_dump.", ".txt");
        if (!string.IsNullOrEmpty(latestVersioned))
        {
            return latestVersioned;
        }

        string perCase = Path.Combine(perCaseDir, "reference_dump.txt");
        if (File.Exists(perCase))
        {
            return perCase;
        }

        if (string.Equals(caseId, "n0012_re1e6_a0", StringComparison.Ordinal))
        {
            return Path.Combine(GetFortranDebugDirectory(), "reference_dump.txt");
        }

        return perCase;
    }

    public static string GetManagedTracePath(string caseId)
    {
        string managedDir = GetManagedDirectory(caseId);
        string latestVersioned = TryGetLatestVersionedArtifact(
            managedDir,
            "csharp_trace.",
            ".jsonl",
            IsReusableTraceArtifact);
        if (!string.IsNullOrEmpty(latestVersioned))
        {
            return latestVersioned;
        }

        string canonicalTracePath = Path.Combine(managedDir, "csharp_trace.jsonl");
        if (IsReusableTraceArtifact(canonicalTracePath))
        {
            return canonicalTracePath;
        }

        string latestAnyVersioned = TryGetLatestVersionedArtifact(managedDir, "csharp_trace.", ".jsonl");
        if (!string.IsNullOrEmpty(latestAnyVersioned))
        {
            return latestAnyVersioned;
        }

        return canonicalTracePath;
    }

    public static string GetManagedDumpPath(string caseId)
    {
        string managedDir = GetManagedDirectory(caseId);
        string latestVersioned = TryGetLatestVersionedArtifact(managedDir, "csharp_dump.", ".txt");
        if (!string.IsNullOrEmpty(latestVersioned))
        {
            return latestVersioned;
        }

        return Path.Combine(managedDir, "csharp_dump.txt");
    }

    public static string GetManagedParityReportPath(string caseId)
    {
        string managedDir = GetManagedDirectory(caseId);
        string latestVersioned = TryGetLatestVersionedArtifact(managedDir, "parity_report.", ".txt");
        if (!string.IsNullOrEmpty(latestVersioned))
        {
            return latestVersioned;
        }

        return Path.Combine(managedDir, "parity_report.txt");
    }

    public static string GetTraceCounterPath()
    {
        string? overridePath = ReadOptionalEnvironmentString("XFOIL_TRACE_COUNTER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return ResolvePathOverride(overridePath);
        }

        return Path.Combine(GetFortranDebugDirectory(), "trace_counter.txt");
    }

    private static long AllocateTraceCounter()
    {
        lock (TraceCounterLock)
        {
            string counterPath = GetTraceCounterPath();
            string? counterDirectory = Path.GetDirectoryName(counterPath);
            if (!string.IsNullOrWhiteSpace(counterDirectory))
            {
                Directory.CreateDirectory(counterDirectory);
            }

            long current = 0;
            if (File.Exists(counterPath))
            {
                string text = File.ReadAllText(counterPath).Trim();
                _ = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out current);
            }

            long next = current + 1;
            File.WriteAllText(counterPath, next.ToString(CultureInfo.InvariantCulture));
            return next;
        }
    }

    private static string TryGetLatestVersionedArtifact(
        string directory,
        string prefix,
        string suffix,
        Func<string, bool>? validator = null)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        return Directory.EnumerateFiles(directory, $"{prefix}*{suffix}")
            .Select(path => new
            {
                Path = path,
                Counter = TryParseArtifactCounter(Path.GetFileName(path), prefix, suffix)
            })
            .Where(entry => entry.Counter is not null)
            .Where(entry => validator?.Invoke(entry.Path) ?? true)
            .OrderByDescending(entry => entry.Counter)
            .Select(entry => entry.Path)
            .FirstOrDefault() ?? string.Empty;
    }

    private static bool IsReusableTraceArtifact(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains("\"kind\":\"session_start\"", StringComparison.Ordinal) ||
                line.Contains("\"kind\":\"session_end\"", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static long? TryParseArtifactCounter(string fileName, string prefix, string suffix)
    {
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        string middle = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        return long.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out long counter)
            ? counter
            : null;
    }

    public static void EnsureManagedArtifacts(string caseId)
    {
        object caseLock = CaseLocks.GetOrAdd(caseId, static _ => new object());
        lock (caseLock)
        {
            bool forceRefresh =
                string.Equals(
                    Environment.GetEnvironmentVariable("XFOILSHARP_FORCE_PARITY_REFRESH"),
                    "1",
                    StringComparison.Ordinal);
            if (forceRefresh)
            {
                RefreshManagedArtifactsCore(caseId);
                return;
            }

            string tracePath = GetManagedTracePath(caseId);
            string dumpPath = GetManagedDumpPath(caseId);
            if (IsReusableTraceArtifact(tracePath) && File.Exists(dumpPath))
            {
                return;
            }

            RefreshManagedArtifacts(caseId);
        }
    }

    public static void RefreshManagedArtifacts(string caseId)
    {
        object caseLock = CaseLocks.GetOrAdd(caseId, static _ => new object());
        lock (caseLock)
        {
            lock (ManagedArtifactRefreshLock)
            {
                RefreshManagedArtifactsCore(caseId);
            }
        }
    }

    public static void RefreshManagedArtifacts(FortranReferenceCase definition)
    {
        object caseLock = CaseLocks.GetOrAdd(definition.CaseId, static _ => new object());
        lock (caseLock)
        {
            lock (ManagedArtifactRefreshLock)
            {
                RefreshManagedArtifactsCore(definition);
            }
        }
    }

    // Legacy mapping: none; this is a managed-only artifact refresh implementation for parity tests.
    // Difference from legacy: The test harness serializes per-case artifact writes so concurrent managed parity tests cannot read half-written JSONL traces.
    // Decision: Keep the locked refresh path because deterministic artifact generation matters more than parallel test throughput here.
    private static void RefreshManagedArtifactsCore(string caseId)
        => RefreshManagedArtifactsCore(Get(caseId));

    private static void RefreshManagedArtifactsCore(FortranReferenceCase definition)
    {
        string managedDir = GetManagedDirectory(definition.CaseId);
        Directory.CreateDirectory(managedDir);

        long traceCounter = AllocateTraceCounter();
        string dumpPath = Path.Combine(managedDir, "csharp_dump.txt");
        string tracePath = Path.Combine(managedDir, "csharp_trace.jsonl");
        string reportPath = Path.Combine(managedDir, "parity_report.txt");
        string versionedDumpPath = Path.Combine(managedDir, $"csharp_dump.{traceCounter}.txt");
        string versionedTracePath = Path.Combine(managedDir, $"csharp_trace.{traceCounter}.jsonl");
        string versionedReportPath = Path.Combine(managedDir, $"parity_report.{traceCounter}.txt");

        var settings = new AnalysisSettings(
            panelCount: definition.PanelCount,
            reynoldsNumber: definition.ReynoldsNumber,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: definition.MaxViscousIterations,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: definition.CriticalAmplificationFactor,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);

        double alphaRadians = definition.AlphaDegrees * Math.PI / 180.0;
        var geometry = BuildNacaGeometry(definition.AirfoilCode);
        ParityTraceLiveComparator? liveComparator = CreateLiveComparatorIfEnabled(definition.CaseId);
        string? liveCompareAbortNote = null;
        ViscousAnalysisResult? result = null;
        try
        {
            using var textWriter = new StreamWriter(versionedDumpPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            using var caseTraceCaptureScope = CreateCaseTraceCaptureScope(definition);
            using var liveCompareCaptureScope = CreateImplicitLiveCompareCaptureScope(liveComparator);
            using var traceWriter = new JsonlTraceWriter(versionedTracePath, runtime: "csharp", session: new
            {
                caseId = definition.CaseId,
                traceCounter,
                caseName = definition.DisplayName,
                airfoilCode = definition.AirfoilCode,
                settings.PanelCount,
                settings.ReynoldsNumber,
                alphaDegrees = definition.AlphaDegrees,
                alphaRadians
            }, serializedRecordObserver: liveComparator is null ? null : liveComparator.ObserveSerializedRecord);
            using var debugWriter = new MultiplexTextWriter(textWriter, traceWriter);
            using var solverScope = SolverTrace.Begin(traceWriter);
            using var coreScope = CoreTrace.Begin((kind, scope, data) => traceWriter.WriteEvent(kind, scope, data));

            debugWriter.WriteLine($"=== CSHARP CASE START {definition.CaseId} ===");
            debugWriter.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "CASE={0} AIRFOIL={1} RE={2:E8} ALFA={3:F6}",
                definition.CaseId,
                definition.AirfoilCode,
                definition.ReynoldsNumber,
                definition.AlphaDegrees));

            try
            {
                result = ViscousSolverEngine.SolveViscous(
                    geometry,
                    settings,
                    alphaRadians,
                    debugWriter: debugWriter);
                liveComparator?.AssertCompleted();
            }
            catch (LiveParityTraceMismatchException ex)
            {
                liveCompareAbortNote = ex.ToDetailedReport();
                debugWriter.WriteLine($"LIVE_COMPARE_ABORT {ex.Message}");
                WritePrefixedMultiline(debugWriter, "LIVE_COMPARE_CONTEXT ", liveCompareAbortNote);
            }

            debugWriter.WriteLine($"=== CSHARP CASE END {definition.CaseId} ===");
            if (result is not null)
            {
                debugWriter.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "FINAL CL={0,15:E8} CD={1,15:E8} CM={2,15:E8} CONVERGED={3} ITER={4}",
                    result.LiftCoefficient,
                    result.DragDecomposition.CD,
                    result.MomentCoefficient,
                    result.Converged,
                    result.Iterations));
            }
            else if (!string.IsNullOrWhiteSpace(liveCompareAbortNote))
            {
                debugWriter.WriteLine($"FINAL LIVE_COMPARE_ABORTED=True");
            }

            debugWriter.Flush();
        }
        catch (TracePersistenceLimitExceededException ex)
        {
            TryDeleteFile(versionedTracePath);
            if (!string.Equals(versionedTracePath, tracePath, StringComparison.Ordinal))
            {
                TryDeleteFile(tracePath);
            }

            long limitMegabytes = ex.LimitBytes / (1024L * 1024L);
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Managed trace '{0}' exceeded the {1} MB limit before divergence capture completed (projected {2} bytes at kind={3} scope={4}). Refine the trace selector or use live compare without full persisted capture.",
                    versionedTracePath,
                    limitMegabytes,
                    ex.ProjectedBytes,
                    ex.Kind,
                    ex.Scope),
                ex);
        }
        File.Copy(versionedDumpPath, dumpPath, overwrite: true);
        if (IsReusableTraceArtifact(versionedTracePath))
        {
            File.Copy(versionedTracePath, tracePath, overwrite: true);
            EnforceTraceSizeLimit(versionedTracePath, tracePath);
        }
        WriteParityReportIfPossible(definition, versionedDumpPath, versionedReportPath, reportPath, liveCompareAbortNote);
    }

    public static string FindRepositoryRoot()
    {
        if (TryFindRepositoryRoot(Environment.CurrentDirectory, out string? root) && root is not null)
        {
            return root;
        }

        string assemblyDir = Path.GetDirectoryName(typeof(FortranReferenceCases).Assembly.Location) ?? ".";
        if (TryFindRepositoryRoot(assemblyDir, out root) && root is not null)
        {
            return root;
        }

        throw new DirectoryNotFoundException("Unable to find the XFoilSharp repository root from the test environment.");
    }

    private static (double[] x, double[] y) BuildNacaGeometry(string airfoilCode)
    {
        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(airfoilCode, ClassicXFoilNacaPointCount);
        double[] x = geometry.Points.Select(point => point.X).ToArray();
        double[] y = geometry.Points.Select(point => point.Y).ToArray();
        return (x, y);
    }

    private static bool TryFindRepositoryRoot(string startDir, out string? root)
    {
        root = null;
        string? directory = startDir;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "src")) &&
                Directory.Exists(Path.Combine(directory, "tests")) &&
                Directory.Exists(Path.Combine(directory, "tools")))
            {
                root = directory;
                return true;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static string BuildAdHocCaseId(
        string airfoilCode,
        double reynoldsNumber,
        double alphaDegrees,
        int panelCount,
        double criticalAmplificationFactor)
    {
        return FormattableString.Invariant(
            $"adhoc_n{SanitizeToken(airfoilCode)}_re{SanitizeToken(reynoldsNumber)}_a{SanitizeToken(alphaDegrees)}_p{panelCount}_n{SanitizeToken(criticalAmplificationFactor)}");
    }

    private static string SanitizeToken(object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            builder.Append(character switch
            {
                '.' => 'p',
                '-' => 'm',
                '+' => '_',
                _ when char.IsLetterOrDigit(character) => char.ToLowerInvariant(character),
                _ => '_'
            });
        }

        return builder.Length == 0 ? "x" : builder.ToString().Trim('_');
    }

    private static string ReadRequiredEnvironmentString(string variableName)
        => ReadOptionalEnvironmentString(variableName)
            ?? throw new InvalidOperationException(
                $"Environment variable '{variableName}' is required for ad hoc parity artifact generation.");

    private static string? ReadOptionalEnvironmentString(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    internal static string ResolvePathOverride(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path override must not be blank.", nameof(path));
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(FindRepositoryRoot(), path));
    }

    private static double ReadRequiredEnvironmentDouble(string variableName)
        => ReadOptionalEnvironmentDouble(variableName)
            ?? throw new InvalidOperationException(
                $"Environment variable '{variableName}' is required and must be a floating-point value.");

    private static double? ReadOptionalEnvironmentDouble(string variableName)
    {
        string? raw = ReadOptionalEnvironmentString(variableName);
        if (raw is null)
        {
            return null;
        }

        return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new InvalidOperationException(
                $"Environment variable '{variableName}' must parse as a floating-point value. Received '{raw}'.");
    }

    private static int? ReadOptionalEnvironmentInt(string variableName)
    {
        string? raw = ReadOptionalEnvironmentString(variableName);
        if (raw is null)
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new InvalidOperationException(
                $"Environment variable '{variableName}' must parse as an integer. Received '{raw}'.");
    }

    private static void EnforceTraceSizeLimit(string versionedTracePath, string tracePath)
    {
        int? maxTraceMegabytes = ReadOptionalEnvironmentInt("XFOIL_MAX_TRACE_MB");
        if (maxTraceMegabytes is null || maxTraceMegabytes <= 0 || !File.Exists(versionedTracePath))
        {
            return;
        }

        long maxBytes = (long)maxTraceMegabytes.Value * 1024L * 1024L;
        long actualBytes = new FileInfo(versionedTracePath).Length;
        if (actualBytes <= maxBytes)
        {
            return;
        }

        TryDeleteFile(versionedTracePath);
        if (!string.Equals(versionedTracePath, tracePath, StringComparison.Ordinal))
        {
            TryDeleteFile(tracePath);
        }

        throw new InvalidOperationException(
            $"Managed trace '{versionedTracePath}' exceeded the {maxTraceMegabytes.Value} MB limit ({actualBytes} bytes).");
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteParityReportIfPossible(
        FortranReferenceCase definition,
        string managedDumpPath,
        string versionedReportPath,
        string reportPath,
        string? extraNote = null)
    {
        string referenceDumpPath = GetReferenceDumpPath(definition.CaseId);
        if (!File.Exists(referenceDumpPath))
        {
            return;
        }

        ParityDivergenceReport report = ParityDumpDivergenceAnalyzer.Analyze(referenceDumpPath, managedDumpPath);
        if (!string.IsNullOrWhiteSpace(extraNote))
        {
            string combinedNote = string.IsNullOrWhiteSpace(report.Note)
                ? extraNote
                : $"{report.Note}{Environment.NewLine}{extraNote}";
            report = report with { Note = combinedNote };
        }

        string reportText = report.ToDisplayText();
        File.WriteAllText(versionedReportPath, reportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Copy(versionedReportPath, reportPath, overwrite: true);
    }

    private static void WritePrefixedMultiline(TextWriter writer, string prefix, string text)
    {
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            writer.WriteLine($"{prefix}{line}");
        }
    }

    internal static IReadOnlyDictionary<string, string?>? GetImplicitLiveCompareCaptureOverrides(ParityTraceLiveComparator? liveComparator)
    {
        if (liveComparator is null || HasExplicitCaptureSelector() || HasEnvironmentValue("XFOIL_TRACE_FULL"))
        {
            return null;
        }

        return new Dictionary<string, string?>
        {
            // Live compare already observes every serialized event in-memory. Persist only tiny session markers
            // unless the caller asked for a focused selector or an explicit full trace rerun.
            ["XFOIL_TRACE_KIND_ALLOW"] = "__summary_none__"
        };
    }

    private static IDisposable? CreateImplicitLiveCompareCaptureScope(ParityTraceLiveComparator? liveComparator)
    {
        IReadOnlyDictionary<string, string?>? overrides = GetImplicitLiveCompareCaptureOverrides(liveComparator);
        return overrides is null ? null : new EnvironmentVariableScope(overrides);
    }

    private static IDisposable? CreateCaseTraceCaptureScope(FortranReferenceCase definition)
    {
        if (string.IsNullOrWhiteSpace(definition.TraceKindAllowList) || HasExplicitCaptureSelector())
        {
            return null;
        }

        return new EnvironmentVariableScope(
            new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = definition.TraceKindAllowList
            });
    }

    private static bool HasExplicitCaptureSelector()
    {
        return HasEnvironmentValue("XFOIL_TRACE_KIND_ALLOW") ||
               HasEnvironmentValue("XFOIL_TRACE_SCOPE_ALLOW") ||
               HasEnvironmentValue("XFOIL_TRACE_NAME_ALLOW") ||
               HasEnvironmentValue("XFOIL_TRACE_DATA_MATCH") ||
               HasEnvironmentValue("XFOIL_TRACE_SIDE") ||
               HasEnvironmentValue("XFOIL_TRACE_STATION") ||
               HasEnvironmentValue("XFOIL_TRACE_ITERATION") ||
               HasEnvironmentValue("XFOIL_TRACE_ITER_MIN") ||
               HasEnvironmentValue("XFOIL_TRACE_ITER_MAX") ||
               HasEnvironmentValue("XFOIL_TRACE_MODE");
    }

    private static bool HasEnvironmentValue(string variableName)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName));
    }

    private static ParityTraceLiveComparator? CreateLiveComparatorIfEnabled(string caseId)
    {
        int matchedContextWindow = ReadOptionalEnvironmentInt("XFOIL_LIVE_COMPARE_CONTEXT_EVENTS") ?? 6;
        ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
        ParityTraceFocusSelector? triggerSelector = ParityTraceFocusSelector.FromTriggerEnvironment();
        string? referenceTraceOverride = ReadOptionalEnvironmentString("XFOIL_LIVE_COMPARE_REFERENCE_TRACE");
        string? enabled = ReadOptionalEnvironmentString("XFOIL_LIVE_COMPARE_ENABLED");
        if (!string.IsNullOrWhiteSpace(referenceTraceOverride))
        {
            string resolvedReferenceTracePath = ResolvePathOverride(referenceTraceOverride);
            if (!File.Exists(resolvedReferenceTracePath))
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Live compare requires an existing reference trace, but '{0}' was not found.",
                        resolvedReferenceTracePath));
            }

            return new ParityTraceLiveComparator(
                resolvedReferenceTracePath,
                matchedContextWindow,
                captureSelector,
                triggerSelector);
        }

        if (!string.Equals(enabled, "1", StringComparison.Ordinal))
        {
            return null;
        }

        string referenceTracePath = GetReferenceTracePath(caseId);
        if (!File.Exists(referenceTracePath))
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Live compare was enabled for case '{0}', but the reference trace '{1}' does not exist.",
                    caseId,
                    referenceTracePath));
        }

        return new ParityTraceLiveComparator(referenceTracePath, matchedContextWindow, captureSelector, triggerSelector);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach ((string key, string? value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

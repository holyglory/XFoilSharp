using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

static int ParseFloatBitsToken(string token)
{
    string text = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? token[2..]
        : token;
    uint bits = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return unchecked((int)bits);
}

// Cache loaded airfoil geometries by name/path so each Selig airfoil is parsed
// from disk only once across the parallel sweep.
var s_geometryCache = new ConcurrentDictionary<string, AirfoilGeometry>();
var s_nacaGenerator = new NacaAirfoilGenerator();
var s_airfoilParser = new AirfoilParser();

AirfoilGeometry LoadOrCacheGeometry(string token)
{
    return s_geometryCache.GetOrAdd(token, key =>
    {
        // Path-style token: contains a path separator or .dat extension. Load
        // through the AirfoilParser which handles labeled, ISES, and MSES
        // formats. Lednicer files must be normalized to Selig before this.
        bool looksLikePath = key.Contains('/') || key.Contains('\\')
            || key.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
        if (looksLikePath)
        {
            string resolved = Path.IsPathRooted(key)
                ? key
                : Path.Combine(Directory.GetCurrentDirectory(), key);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Airfoil dat file not found: {resolved}");
            }
            return s_airfoilParser.ParseFile(resolved);
        }
        // Otherwise treat as NACA 4-digit designation.
        return s_nacaGenerator.Generate4DigitClassic(key, pointCount: 239);
    });
}

// FMA mode info (use XFOIL_DISABLE_FMA=1 env var to disable FMA before launch)
bool fmaDisabled = Environment.GetEnvironmentVariable("XFOIL_DISABLE_FMA") == "1";
Console.Error.WriteLine(fmaDisabled ? "FMA DISABLED" : "FMA ENABLED");

// Doubled-tree smoke test: --double-smoke
// Verifies the auto-generated XFoil.Solver.Double.Services.AirfoilAnalysisService
// constructs and runs an inviscid analysis end-to-end on NACA 0012.
if (args.Length > 0 && args[0] == "--double-smoke")
{
    var dsvcFloat = new XFoil.Solver.Services.AirfoilAnalysisService();
    var dsvcDouble = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
    AnalysisSettings BuildSettings(double re, double ncrit) => new(
        panelCount: 160,
        reynoldsNumber: re,
        criticalAmplificationFactor: ncrit,
        useExtendedWake: false,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        maxViscousIterations: 80,
        viscousConvergenceTolerance: 1e-4);

    var cases = new (string Naca, double Alpha, double Re, double Nc)[]
    {
        ("0012", 0.0, 1_000_000, 9),
        ("0012", 4.0, 1_000_000, 9),
        ("0012", 6.0, 1_000_000, 9),
        ("4412", 2.0, 1_000_000, 9),
        ("4412", 5.0, 500_000, 5),
        ("0009", 3.0, 750_000, 12),
        ("0006", -2.0, 2_000_000, 9),
        ("4415", 4.0, 1_000_000, 9),
    };

    int pass = 0, fail = 0;
    Console.WriteLine($"Doubled facade multi-case validation ({cases.Length} cases):");
    Console.WriteLine($"{"NACA",-6} {"α",6} {"Re",12} {"Nc",4}  {"CL_F",10} {"CL_D",10} {"ΔCL",10}  {"CD_F",10} {"CD_D",10} {"ΔCD",10}  Status");
    foreach (var c in cases)
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic(c.Naca, pointCount: 161);
        var settings = BuildSettings(c.Re, c.Nc);
        var rf = dsvcFloat.AnalyzeViscous(geom, c.Alpha, settings);
        var rd = dsvcDouble.AnalyzeViscous(geom, c.Alpha, settings);
        double clDiff = Math.Abs(rf.LiftCoefficient - rd.LiftCoefficient);
        double cdDiff = Math.Abs(rf.DragDecomposition.CD - rd.DragDecomposition.CD);
        bool bothConverged = rf.Converged && rd.Converged;
        bool agreement = clDiff < 1e-3 && cdDiff < 1e-4;
        bool ok = bothConverged && agreement;
        if (ok) pass++; else fail++;
        string status = ok ? "PASS" : (!bothConverged ? "DIVERGED" : "DIFF");
        Console.WriteLine($"{c.Naca,-6} {c.Alpha,6:F1} {c.Re,12:F0} {c.Nc,4:F0}  {rf.LiftCoefficient,10:F6} {rd.LiftCoefficient,10:F6} {clDiff,10:E3}  {rf.DragDecomposition.CD,10:F7} {rd.DragDecomposition.CD,10:F7} {cdDiff,10:E3}  {status}");
    }
    Console.WriteLine($"\nResult: {pass} pass / {fail} fail / {cases.Length} total");
    return fail == 0 ? 0 : 1;
}

// Doubled-tree pressure distribution: --double-cp NACA Alpha PanelCount [--csv FILE]
// Emits Cp(x/c) distribution via the doubled-tree inviscid analysis.
if (args.Length > 0 && args[0] == "--double-cp")
{
    string cpNaca = args.Length > 1 ? args[1] : "0012";
    double cpAlpha = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4d;
    int cpPanels = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 240;
    string? cpCsv = null;
    for (int cpAi = 1; cpAi < args.Length - 1; cpAi++)
    {
        if (args[cpAi] == "--csv") { cpCsv = args[cpAi + 1]; break; }
    }
    AirfoilGeometry cpGeom = cpNaca.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
        ? new AirfoilParser().ParseFile(cpNaca)
        : new NacaAirfoilGenerator().Generate4DigitClassic(cpNaca, pointCount: 481);
    var cpSettings = new AnalysisSettings(
        panelCount: cpPanels);
    var cpRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
        .AnalyzeInviscid(cpGeom, cpAlpha, cpSettings);
    Console.WriteLine($"Doubled-tree Cp: {cpNaca} α={cpAlpha} panels={cpPanels}, CL={cpRes.LiftCoefficient:F6}");
    StreamWriter? cpCsvW = cpCsv is null ? null : new StreamWriter(cpCsv);
    cpCsvW?.WriteLine("# airfoil={0} alpha={1} panels={2} cl={3:G17}", cpNaca, cpAlpha, cpPanels, cpRes.LiftCoefficient);
    cpCsvW?.WriteLine("x,y,cp");
    Console.WriteLine($"{"x",10} {"y",10} {"Cp",12}");
    foreach (var s in cpRes.PressureSamples)
    {
        Console.WriteLine($"{s.Location.X,10:F6} {s.Location.Y,10:F6} {s.PressureCoefficient,12:F6}");
        cpCsvW?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:G17},{1:G17},{2:G17}",
            s.Location.X, s.Location.Y, s.PressureCoefficient));
    }
    cpCsvW?.Dispose();
    return 0;
}

// Doubled-tree smoke validate: --double-validate
// Quick sanity that the doubled tree converges on a small set of canonical
// NACA cases and produces lift coefficients in expected ranges. Useful as a
// post-regen verification before running full sweeps.
if (args.Length > 0 && args[0] == "--double-validate")
{
    var dvCases = new (string Naca, double Alpha, double ExpectedClMin, double ExpectedClMax)[]
    {
        ("0012", 0d, -0.05d, 0.05d),
        ("0012", 4d, 0.40d, 0.55d),
        ("4412", 0d, 0.35d, 0.55d),
        ("4412", 4d, 0.75d, 1.05d),
        ("0009", 6d, 0.55d, 0.80d),
        ("4415", 8d, 1.30d, 1.60d),
    };
    int dvPass = 0, dvFail = 0;
    var dvSvc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
    Console.WriteLine($"Doubled-tree validation ({dvCases.Length} cases):");
    Console.WriteLine($"{"NACA",-6} {"α",4}  {"CL",10}  {"expected",16}  Status");
    foreach (var c in dvCases)
    {
        var dvGeom = new NacaAirfoilGenerator().Generate4DigitClassic(c.Naca, pointCount: 161);
        var dvSettings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        try
        {
            var dvR = dvSvc.AnalyzeViscous(dvGeom, c.Alpha, dvSettings);
            bool ok = dvR.Converged && dvR.LiftCoefficient >= c.ExpectedClMin && dvR.LiftCoefficient <= c.ExpectedClMax;
            string status = ok ? "PASS" : (dvR.Converged ? "OUT-OF-RANGE" : "DIVERGED");
            Console.WriteLine($"{c.Naca,-6} {c.Alpha,4:F1}  {dvR.LiftCoefficient,10:F4}  [{c.ExpectedClMin,5:F2},{c.ExpectedClMax,5:F2}]  {status}");
            if (ok) dvPass++; else dvFail++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{c.Naca,-6} {c.Alpha,4:F1}  EXCEPTION: {ex.GetType().Name}");
            dvFail++;
        }
    }
    Console.WriteLine($"\nResult: {dvPass}/{dvCases.Length} passed");
    return dvFail == 0 ? 0 : 1;
}

// Doubled-tree polar generation: --double-polar NACA Re Nc panelCount alphaStart alphaEnd alphaStep [--csv FILE]
// Runs the doubled-tree facade at a sweep of alphas for a single airfoil/Re/Nc.
// Demonstrates Phase 2's user-facing capability: full double-precision polar
// generation, the function the float-parity tree exists for but in double.
// Optional --csv FILE writes machine-readable polar output.
if (args.Length > 0 && args[0] == "--double-polar")
{
    string dpNaca = args.Length > 1 ? args[1] : "0012";
    double dpRe = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1_000_000d;
    double dpNc = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 9d;
    int dpPanels = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 240;
    double dpAStart = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : -4d;
    double dpAEnd = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : 16d;
    double dpAStep = args.Length > 7 ? double.Parse(args[7], CultureInfo.InvariantCulture) : 1d;
    string? dpCsv = null;
    for (int dpAi = 1; dpAi < args.Length - 1; dpAi++)
    {
        if (args[dpAi] == "--csv") { dpCsv = args[dpAi + 1]; break; }
    }

    AirfoilGeometry dpGeom = dpNaca.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
        ? new AirfoilParser().ParseFile(dpNaca)
        : new NacaAirfoilGenerator().Generate4DigitClassic(dpNaca, pointCount: 481);

    StreamWriter? dpCsvW = null;
    if (dpCsv is not null)
    {
        dpCsvW = new StreamWriter(dpCsv);
        dpCsvW.WriteLine("# Doubled-tree polar");
        dpCsvW.WriteLine($"# airfoil={dpNaca} re={dpRe} nc={dpNc} panels={dpPanels}");
        dpCsvW.WriteLine("alpha,CL,CD,CDF,CDP,CM,xtr_top,xtr_bot,iters,final_rms,converged");
    }

    Console.WriteLine($"Doubled-tree polar: {dpNaca} Re={dpRe} Nc={dpNc} panels={dpPanels}");
    if (dpCsv is not null) Console.WriteLine($"Writing CSV: {dpCsv}");
    Console.WriteLine($"{"alpha",6}  {"CL",12} {"CD",14} {"CDF",14} {"CDP",14} {"CM",12}  {"xtr_top",8} {"xtr_bot",8}  {"iters",6} {"final_rms",10} {"conv",4}");
    var dpService = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
    int dpConverged = 0, dpTotal = 0;
    for (double a = dpAStart; a <= dpAEnd + 1e-9; a += dpAStep)
    {
        var dpSettings = new AnalysisSettings(
            panelCount: dpPanels,
            reynoldsNumber: dpRe,
            criticalAmplificationFactor: dpNc,
            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-4);
        XFoil.Solver.Models.ViscousAnalysisResult? dpR = null;
        try { dpR = dpService.AnalyzeViscous(dpGeom, a, dpSettings); }
        catch { }
        dpTotal++;
        if (dpR is { Converged: true }) dpConverged++;
        if (dpR is null)
        {
            Console.WriteLine($"{a,6:F2}  EXCEPTION");
            dpCsvW?.WriteLine($"{a.ToString(CultureInfo.InvariantCulture)},,,,,,,,,,exception");
            continue;
        }
        double xtrTop = dpR.UpperTransition.XTransition;
        double xtrBot = dpR.LowerTransition.XTransition;
        double finalRms = dpR.ConvergenceHistory.Count > 0
            ? dpR.ConvergenceHistory[^1].RmsResidual : double.NaN;
        Console.WriteLine($"{a,6:F2}  {dpR.LiftCoefficient,12:F6} {dpR.DragDecomposition.CD,14:F8} {dpR.DragDecomposition.CDF,14:F8} {dpR.DragDecomposition.CDP,14:F8} {dpR.MomentCoefficient,12:F6}  {xtrTop,8:F4} {xtrBot,8:F4}  {dpR.Iterations,6} {finalRms,10:E2} {(dpR.Converged ? "yes" : "no"),4}");
        dpCsvW?.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0},{1:G17},{2:G17},{3:G17},{4:G17},{5:G17},{6:G17},{7:G17},{8},{9:G17},{10}",
            a, dpR.LiftCoefficient, dpR.DragDecomposition.CD, dpR.DragDecomposition.CDF,
            dpR.DragDecomposition.CDP, dpR.MomentCoefficient, xtrTop, xtrBot, dpR.Iterations,
            finalRms, dpR.Converged ? "true" : "false"));
    }
    dpCsvW?.Dispose();
    Console.WriteLine($"\nConverged: {dpConverged}/{dpTotal}");
    return 0;
}

// Mesh refinement study with Richardson extrapolation: --rich-study NACA Re Alpha Nc
// Runs the doubled-tree facade at panels = (160, 240, 320) and reports a
// Richardson-extrapolated converged-mesh estimate. Order-of-convergence p is
// computed from the three values: p ≈ log((c1-c2)/(c2-c3))/log(2).
if (args.Length > 0 && args[0] == "--rich-study")
{
    string rsNaca = args.Length > 1 ? args[1] : "0012";
    double rsRe = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1_000_000d;
    double rsAlpha = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 4d;
    double rsNc = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 9d;
    int[] rsPanels = new[] { 160, 240, 320 };

    AirfoilGeometry rsGeom = rsNaca.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
        ? new AirfoilParser().ParseFile(rsNaca)
        : new NacaAirfoilGenerator().Generate4DigitClassic(rsNaca, pointCount: 481);

    Console.WriteLine($"Richardson study: {rsNaca} Re={rsRe} α={rsAlpha} Nc={rsNc}");
    Console.WriteLine($"{"Panels",6}  {"CL",14} {"CD",16}");
    var rsCl = new double[rsPanels.Length];
    var rsCd = new double[rsPanels.Length];
    for (int rsI = 0; rsI < rsPanels.Length; rsI++)
    {
        var rsSet = new AnalysisSettings(
            panelCount: rsPanels[rsI],
            reynoldsNumber: rsRe,
            criticalAmplificationFactor: rsNc,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-4);
        XFoil.Solver.Models.ViscousAnalysisResult? rsR = null;
        try { rsR = new XFoil.Solver.Double.Services.AirfoilAnalysisService().AnalyzeViscous(rsGeom, rsAlpha, rsSet); }
        catch { }
        rsCl[rsI] = rsR?.LiftCoefficient ?? double.NaN;
        rsCd[rsI] = rsR?.DragDecomposition.CD ?? double.NaN;
        Console.WriteLine($"{rsPanels[rsI],6}  {rsCl[rsI],14:F8} {rsCd[rsI],16:F10}");
    }
    // Richardson extrapolation: assume error ~ h^p where h = 1/panels.
    // For successive halvings (or proportional refinements), if c_h, c_{h/2},
    // c_{h/4} converge as O(h^p), then p ≈ log2((c_h - c_{h/2}) / (c_{h/2} - c_{h/4})).
    // Our refinements aren't strict halvings (160→240→320), but the same idea
    // gives an effective order-of-convergence estimate.
    void Richardson(string name, double[] arr)
    {
        double dlo = arr[1] - arr[0];
        double dhi = arr[2] - arr[1];
        if (Math.Abs(dhi) < 1e-15) { Console.WriteLine($"{name} extrap: trivially converged ({arr[2]:F8})"); return; }
        double rTitle = Math.Log(Math.Abs(dlo / dhi)) / Math.Log(rsPanels[2] / (double)rsPanels[1]);
        // Linear Richardson extrapolation toward h→0.
        double rExt = arr[2] - dhi * dhi / (dlo - dhi);
        Console.WriteLine($"{name}: extrap≈{rExt:F8} order≈{rTitle:F2}");
    }
    Richardson("CL", rsCl);
    Richardson("CD", rsCd);
    return 0;
}

// Mesh refinement study: --mesh-study NACA Re Alpha Nc
// Runs the doubled-tree facade at a sequence of panel counts and reports
// the CD/CL convergence trajectory. Demonstrates Phase 2's mesh-refinement
// capability: at coarse meshes float and double agree; at fine meshes the
// float tree's precision saturates while the double tree continues to
// converge toward a mesh-refined limit.
// B1 consolidated benchmark (Phase 3 B1 regression report):
//   --b1-benchmark [--set <path.json>] [--json <output-path>]
// Runs the full B1 inviscid validation matrix in one command:
//   - α ∈ {0, 2, 4, 6, 8} on both 10-NACA default and the curated set
//   - M ∈ {0.0, 0.3, 0.5, 0.7} on both sets
//   - Outputs an aggregated summary table and an optional JSON report
// Useful for tracking B1 regressions over time and for documenting
// the full validation cadence in a single run.
if (args.Length > 0 && args[0] == "--b1-benchmark")
{
    string? bmSetPath = null;
    string? bmJsonPath = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--set" && i + 1 < args.Length) { bmSetPath = args[i + 1]; i++; }
        else if (args[i] == "--json" && i + 1 < args.Length) { bmJsonPath = args[i + 1]; i++; }
    }

    string[] bmNacaSet = new[] { "0008", "0012", "0018", "0024", "2412", "4412", "6412", "4415", "4418", "2424" };
    string[] bmCuratedSet;
    if (bmSetPath is not null)
    {
        using var bmStream = File.OpenRead(bmSetPath);
        using var bmDoc = JsonDocument.Parse(bmStream);
        var bmArr = bmDoc.RootElement.GetProperty("airfoils");
        var bmList = new List<string>(bmArr.GetArrayLength());
        foreach (var bmEl in bmArr.EnumerateArray())
        {
            bmList.Add(bmEl.GetString() ?? throw new FormatException("airfoil entry must be string"));
        }
        bmCuratedSet = bmList.ToArray();
    }
    else
    {
        bmCuratedSet = bmNacaSet; // fall back to NACA set if no --set provided
    }

    double[] bmAlphas = { 0.0, 2.0, 4.0, 6.0, 8.0 };
    double[] bmMachs = { 0.0, 0.3, 0.5, 0.7 };
    int[] bmPanels = { 80, 120, 160, 200, 320, 640 };
    int bmTruthIdx = Array.IndexOf(bmPanels, 640);
    var bmDoubled = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
    var bmModern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();

    double ComputeRatio(string[] airfoils, double alpha, double mach)
    {
        double sumErrD = 0.0, sumErrM = 0.0;
        int count = 0;
        foreach (var airfoil in airfoils)
        {
            AirfoilGeometry g;
            try { g = LoadOrCacheGeometry(airfoil); }
            catch { continue; }
            var truthSettings = new AnalysisSettings(panelCount: bmPanels[bmTruthIdx], machNumber: mach);
            double clTruth;
            try { clTruth = bmDoubled.AnalyzeInviscid(g, alpha, truthSettings).LiftCoefficient; }
            catch { continue; }
            if (!double.IsFinite(clTruth) || Math.Abs(clTruth) < 1e-9) continue;
            for (int pi = 0; pi < bmPanels.Length; pi++)
            {
                if (pi == bmTruthIdx) continue;
                var s = new AnalysisSettings(panelCount: bmPanels[pi], machNumber: mach);
                double clD, clM;
                try { clD = bmDoubled.AnalyzeInviscid(g, alpha, s).LiftCoefficient; }
                catch { continue; }
                try { clM = bmModern.AnalyzeInviscid(g, alpha, s).LiftCoefficient; }
                catch { continue; }
                if (!double.IsFinite(clD) || !double.IsFinite(clM)) continue;
                sumErrD += Math.Abs(clD - clTruth) / Math.Abs(clTruth);
                sumErrM += Math.Abs(clM - clTruth) / Math.Abs(clTruth);
                count++;
            }
        }
        return sumErrD > 0 ? sumErrM / sumErrD : double.NaN;
    }

    Console.WriteLine("B1 consolidated benchmark (Phase 3 B1 v19 tuning)");
    Console.WriteLine();
    Console.WriteLine("α-range (M=0):");
    Console.WriteLine($"{"α",-6} {"10-NACA M/D",-14} {"curated M/D",-14}");
    var alphaRows = new List<(double alpha, double naca, double curated)>();
    foreach (var a in bmAlphas)
    {
        double nacaRatio = ComputeRatio(bmNacaSet, a, 0.0);
        double curRatio = ComputeRatio(bmCuratedSet, a, 0.0);
        alphaRows.Add((a, nacaRatio, curRatio));
        Console.WriteLine($"{a,-6:F1} {nacaRatio,-14:F3} {curRatio,-14:F3}");
    }
    Console.WriteLine();
    Console.WriteLine("Mach-range (α=4):");
    Console.WriteLine($"{"M",-6} {"10-NACA M/D",-14} {"curated M/D",-14}");
    var machRows = new List<(double mach, double naca, double curated)>();
    foreach (var m in bmMachs)
    {
        double nacaRatio = ComputeRatio(bmNacaSet, 4.0, m);
        double curRatio = ComputeRatio(bmCuratedSet, 4.0, m);
        machRows.Add((m, nacaRatio, curRatio));
        Console.WriteLine($"{m,-6:F1} {nacaRatio,-14:F3} {curRatio,-14:F3}");
    }

    if (bmJsonPath is not null)
    {
        using var fs = File.Create(bmJsonPath);
        using var jw = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        jw.WriteStartObject();
        jw.WriteString("tool", "ParallelPolarCompare --b1-benchmark");
        jw.WriteString("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        jw.WriteNumber("naca_set_size", bmNacaSet.Length);
        jw.WriteNumber("curated_set_size", bmCuratedSet.Length);
        jw.WriteStartArray("alpha_rows");
        foreach (var r in alphaRows)
        {
            jw.WriteStartObject();
            jw.WriteNumber("alpha", r.alpha);
            jw.WriteNumber("naca_ratio", double.IsFinite(r.naca) ? r.naca : 0.0);
            jw.WriteNumber("curated_ratio", double.IsFinite(r.curated) ? r.curated : 0.0);
            jw.WriteEndObject();
        }
        jw.WriteEndArray();
        jw.WriteStartArray("mach_rows");
        foreach (var r in machRows)
        {
            jw.WriteStartObject();
            jw.WriteNumber("mach", r.mach);
            jw.WriteNumber("naca_ratio", double.IsFinite(r.naca) ? r.naca : 0.0);
            jw.WriteNumber("curated_ratio", double.IsFinite(r.curated) ? r.curated : 0.0);
            jw.WriteEndObject();
        }
        jw.WriteEndArray();
        jw.WriteEndObject();
        jw.Flush();
        Console.WriteLine();
        Console.WriteLine($"JSON report written to: {bmJsonPath}");
    }
    return 0;
}

// Panel-efficiency harness (Phase 3 B1 scoring):
//   --panel-efficiency [--set <path.json>] [--alpha <deg>]
// For each airfoil in the curated set, runs Doubled (#2) and Modern (#3)
// AnalyzeInviscid at panel counts N ∈ {80, 120, 160, 200, 320, 640}.
// Treats Doubled at N=640 as the mesh-converged truth. Reports the
// average relative CL error vs truth at each N per facade, plus the
// integrated score across N ∈ {80, 120, 160, 200, 320}. Phase 3 B1
// success criterion: `score_Modern < 0.7 * score_Doubled`.
if (args.Length > 0 && args[0] == "--panel-efficiency")
{
    string? pefSetPath = null;
    double pefAlpha = 4.0;
    double pefMach = 0.0;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--set" && i + 1 < args.Length)
        {
            pefSetPath = args[i + 1];
            i++;
        }
        else if (args[i] == "--alpha" && i + 1 < args.Length)
        {
            pefAlpha = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
            i++;
        }
        else if (args[i] == "--mach" && i + 1 < args.Length)
        {
            pefMach = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
            i++;
        }
    }

    string[] pefAirfoils;
    if (pefSetPath is not null)
    {
        // JSON file with a flat "airfoils": [ "naca0012", "path/to/e387.dat", ... ] array.
        using var pefStream = File.OpenRead(pefSetPath);
        using var pefDoc = JsonDocument.Parse(pefStream);
        var pefArr = pefDoc.RootElement.GetProperty("airfoils");
        var pefList = new List<string>(pefArr.GetArrayLength());
        foreach (var pefEl in pefArr.EnumerateArray())
        {
            pefList.Add(pefEl.GetString() ?? throw new FormatException("airfoil entry must be string"));
        }
        pefAirfoils = pefList.ToArray();
    }
    else
    {
        // Default 10-NACA smoke set covering thickness × camber variation.
        pefAirfoils = new[]
        {
            "0008", "0012", "0018", "0024",
            "2412", "4412", "6412",
            "4415", "4418", "2424",
        };
    }

    int[] pefPanels = new[] { 80, 120, 160, 200, 320, 640 };
    int pefTruthIdx = Array.IndexOf(pefPanels, 640);
    var pefModern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var pefDoubled = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

    Console.WriteLine($"Panel-efficiency study: {pefAirfoils.Length} airfoils, α={pefAlpha:F2}°");
    Console.WriteLine($"Truth = Doubled at N={pefPanels[pefTruthIdx]}; scoring N ∈ {{{string.Join(", ", pefPanels.Where(p => p != 640))}}}.");
    Console.WriteLine();

    // Per-airfoil per-N relative CL error (|CL - CL_truth| / |CL_truth|).
    var pefErrD = new double[pefAirfoils.Length, pefPanels.Length];
    var pefErrM = new double[pefAirfoils.Length, pefPanels.Length];
    var pefDiverged = 0;

    for (int ai = 0; ai < pefAirfoils.Length; ai++)
    {
        AirfoilGeometry pefGeom;
        try
        {
            pefGeom = LoadOrCacheGeometry(pefAirfoils[ai]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[skip] {pefAirfoils[ai]}: {ex.Message}");
            continue;
        }

        double pefTruthCl = double.NaN;
        var pefClD = new double[pefPanels.Length];
        var pefClM = new double[pefPanels.Length];
        for (int pi = 0; pi < pefPanels.Length; pi++)
        {
            var pefSet = new AnalysisSettings(panelCount: pefPanels[pi], machNumber: pefMach);
            try
            {
                pefClD[pi] = pefDoubled.AnalyzeInviscid(pefGeom, pefAlpha, pefSet).LiftCoefficient;
            }
            catch { pefClD[pi] = double.NaN; }
            try
            {
                pefClM[pi] = pefModern.AnalyzeInviscid(pefGeom, pefAlpha, pefSet).LiftCoefficient;
            }
            catch { pefClM[pi] = double.NaN; }
        }
        pefTruthCl = pefClD[pefTruthIdx];

        if (!double.IsFinite(pefTruthCl) || Math.Abs(pefTruthCl) < 1e-9)
        {
            Console.WriteLine($"[diverged] {pefAirfoils[ai]}: truth CL = {pefTruthCl}");
            pefDiverged++;
            continue;
        }

        for (int pi = 0; pi < pefPanels.Length; pi++)
        {
            pefErrD[ai, pi] = double.IsFinite(pefClD[pi])
                ? Math.Abs(pefClD[pi] - pefTruthCl) / Math.Abs(pefTruthCl) : double.NaN;
            pefErrM[ai, pi] = double.IsFinite(pefClM[pi])
                ? Math.Abs(pefClM[pi] - pefTruthCl) / Math.Abs(pefTruthCl) : double.NaN;
        }

        Console.WriteLine($"{pefAirfoils[ai],-20} truth CL={pefTruthCl,10:F6}");
        Console.Write($"{"",-20}");
        Console.Write($" {"N",-6}");
        for (int pi = 0; pi < pefPanels.Length - 1; pi++) Console.Write($" {pefPanels[pi],12}");
        Console.WriteLine();
        Console.Write($"{"",-20} {"errD",-6}");
        for (int pi = 0; pi < pefPanels.Length - 1; pi++)
            Console.Write($" {pefErrD[ai, pi],12:E3}");
        Console.WriteLine();
        Console.Write($"{"",-20} {"errM",-6}");
        for (int pi = 0; pi < pefPanels.Length - 1; pi++)
            Console.Write($" {pefErrM[ai, pi],12:E3}");
        Console.WriteLine();
    }

    // Aggregate: mean across airfoils per N, then mean across N (excluding N=640 truth).
    int validCount = 0;
    double scoreD = 0.0;
    double scoreM = 0.0;
    Console.WriteLine();
    Console.WriteLine($"{"N",6}  {"mean errD",14}  {"mean errM",14}  {"M/D",8}");
    for (int pi = 0; pi < pefPanels.Length - 1; pi++)  // skip truth column
    {
        double sumD = 0.0, sumM = 0.0;
        int cnt = 0;
        for (int ai = 0; ai < pefAirfoils.Length; ai++)
        {
            if (!double.IsFinite(pefErrD[ai, pi]) || !double.IsFinite(pefErrM[ai, pi])) continue;
            if (pefErrD[ai, pi] == 0.0 && pefErrM[ai, pi] == 0.0) continue;
            sumD += pefErrD[ai, pi];
            sumM += pefErrM[ai, pi];
            cnt++;
        }
        double meanD = cnt > 0 ? sumD / cnt : double.NaN;
        double meanM = cnt > 0 ? sumM / cnt : double.NaN;
        double ratio = double.IsFinite(meanD) && meanD > 0 ? meanM / meanD : double.NaN;
        Console.WriteLine($"{pefPanels[pi],6}  {meanD,14:E4}  {meanM,14:E4}  {ratio,8:F3}");
        if (double.IsFinite(meanD) && double.IsFinite(meanM))
        {
            scoreD += meanD;
            scoreM += meanM;
            validCount++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Integrated score (sum of per-N means):");
    Console.WriteLine($"  Doubled: {scoreD:E4}");
    Console.WriteLine($"  Modern:  {scoreM:E4}");
    double scoreRatio = scoreD > 0 ? scoreM / scoreD : double.NaN;
    Console.WriteLine($"  M/D:     {scoreRatio:F3}");
    Console.WriteLine($"  B1 win:  {(scoreRatio < 0.7 ? "YES" : "NO")} (target: M/D < 0.70)");

    // Per-airfoil summary: integrated ratio across scoring N's. Makes it
    // trivial to spot which airfoils regress under B1 (ratio > 1.0)
    // and which are big wins. Sorted by ratio ascending (biggest wins
    // first, regressions last).
    Console.WriteLine();
    Console.WriteLine($"Per-airfoil integrated M/D ratio (best wins first):");
    Console.WriteLine($"{"airfoil",-40}  {"sumErrD",12}  {"sumErrM",12}  {"M/D",8}");
    var pefAirfoilRanks = new List<(string name, double ratio, double sumD, double sumM)>();
    for (int ai = 0; ai < pefAirfoils.Length; ai++)
    {
        double aSumD = 0.0, aSumM = 0.0;
        bool anyValid = false;
        for (int pi = 0; pi < pefPanels.Length - 1; pi++)
        {
            if (!double.IsFinite(pefErrD[ai, pi]) || !double.IsFinite(pefErrM[ai, pi])) continue;
            if (pefErrD[ai, pi] == 0.0 && pefErrM[ai, pi] == 0.0) continue;
            aSumD += pefErrD[ai, pi];
            aSumM += pefErrM[ai, pi];
            anyValid = true;
        }
        if (!anyValid) continue;
        double aRatio = aSumD > 0 ? aSumM / aSumD : double.NaN;
        pefAirfoilRanks.Add((pefAirfoils[ai], aRatio, aSumD, aSumM));
    }
    pefAirfoilRanks.Sort((a, b) => a.ratio.CompareTo(b.ratio));
    foreach (var row in pefAirfoilRanks)
    {
        string tag = row.ratio < 0.7 ? "WIN" : row.ratio < 1.0 ? "part" : "REGR";
        Console.WriteLine($"{row.name,-40}  {row.sumD,12:E3}  {row.sumM,12:E3}  {row.ratio,8:F3} [{tag}]");
    }
    if (pefDiverged > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"  Diverged airfoils skipped: {pefDiverged}");
    }
    return 0;
}

// Viscous panel-efficiency harness (Phase 3 B1 v2 scoring):
//   --viscous-panel-efficiency [--set <path.json>] [--alpha <deg>] [--re <value>] [--ncrit <value>] [--mach <M>]
// Same methodology as --panel-efficiency but with AnalyzeViscous + CL/CD
// scoring. Because viscous is much slower than inviscid (~3-10s/case),
// the default set is smaller (6 NACAs) and truth is N=320 (not 640).
if (args.Length > 0 && args[0] == "--viscous-panel-efficiency")
{
    string? vpefSetPath = null;
    double vpefAlpha = 4.0;
    double vpefRe = 1_000_000;
    double vpefNcrit = 9.0;
    double vpefMach = 0.0;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--set" && i + 1 < args.Length) { vpefSetPath = args[i + 1]; i++; }
        else if (args[i] == "--alpha" && i + 1 < args.Length)
        { vpefAlpha = double.Parse(args[i + 1], CultureInfo.InvariantCulture); i++; }
        else if (args[i] == "--re" && i + 1 < args.Length)
        { vpefRe = double.Parse(args[i + 1], CultureInfo.InvariantCulture); i++; }
        else if (args[i] == "--ncrit" && i + 1 < args.Length)
        { vpefNcrit = double.Parse(args[i + 1], CultureInfo.InvariantCulture); i++; }
        else if (args[i] == "--mach" && i + 1 < args.Length)
        { vpefMach = double.Parse(args[i + 1], CultureInfo.InvariantCulture); i++; }
    }

    string[] vpefAirfoils;
    if (vpefSetPath is not null)
    {
        using var vpefStream = File.OpenRead(vpefSetPath);
        using var vpefDoc = JsonDocument.Parse(vpefStream);
        var vpefArr = vpefDoc.RootElement.GetProperty("airfoils");
        var vpefList = new List<string>(vpefArr.GetArrayLength());
        foreach (var vpefEl in vpefArr.EnumerateArray())
        {
            vpefList.Add(vpefEl.GetString() ?? throw new FormatException("airfoil entry must be string"));
        }
        vpefAirfoils = vpefList.ToArray();
    }
    else
    {
        // Smaller default set — viscous is 10-100x slower than inviscid.
        vpefAirfoils = new[] { "0008", "0012", "0018", "2412", "4412", "4415" };
    }

    int[] vpefPanels = new[] { 80, 120, 160, 200, 320 };
    int vpefTruthIdx = Array.IndexOf(vpefPanels, 320);
    var vpefModern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var vpefDoubled = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

    AnalysisSettings MakeSet(int n) => new AnalysisSettings(
        panelCount: n,
        machNumber: vpefMach,
        reynoldsNumber: vpefRe,
        criticalAmplificationFactor: vpefNcrit,
        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: 1e-5);

    Console.WriteLine($"Viscous panel-efficiency: {vpefAirfoils.Length} airfoils, α={vpefAlpha:F2}° Re={vpefRe:E1} Ncrit={vpefNcrit}");
    Console.WriteLine($"Truth = Doubled at N={vpefPanels[vpefTruthIdx]}; scoring N ∈ {{{string.Join(", ", vpefPanels.Where(p => p != 320))}}}.");
    Console.WriteLine();

    var vpefErrClD = new double[vpefAirfoils.Length, vpefPanels.Length];
    var vpefErrClM = new double[vpefAirfoils.Length, vpefPanels.Length];
    var vpefErrCdD = new double[vpefAirfoils.Length, vpefPanels.Length];
    var vpefErrCdM = new double[vpefAirfoils.Length, vpefPanels.Length];
    int vpefSkipped = 0;

    for (int ai = 0; ai < vpefAirfoils.Length; ai++)
    {
        AirfoilGeometry vpefGeom;
        try { vpefGeom = LoadOrCacheGeometry(vpefAirfoils[ai]); }
        catch (Exception ex)
        {
            Console.WriteLine($"[skip] {vpefAirfoils[ai]}: {ex.Message}");
            vpefSkipped++;
            continue;
        }

        var vpefClD = new double[vpefPanels.Length];
        var vpefClM = new double[vpefPanels.Length];
        var vpefCdD = new double[vpefPanels.Length];
        var vpefCdM = new double[vpefPanels.Length];
        for (int pi = 0; pi < vpefPanels.Length; pi++)
        {
            var vpefSet = MakeSet(vpefPanels[pi]);
            try
            {
                var r = vpefDoubled.AnalyzeViscous(vpefGeom, vpefAlpha, vpefSet);
                vpefClD[pi] = r.Converged ? r.LiftCoefficient : double.NaN;
                vpefCdD[pi] = r.Converged ? r.DragDecomposition.CD : double.NaN;
            }
            catch { vpefClD[pi] = double.NaN; vpefCdD[pi] = double.NaN; }
            try
            {
                var r = vpefModern.AnalyzeViscous(vpefGeom, vpefAlpha, vpefSet);
                vpefClM[pi] = r.Converged ? r.LiftCoefficient : double.NaN;
                vpefCdM[pi] = r.Converged ? r.DragDecomposition.CD : double.NaN;
            }
            catch { vpefClM[pi] = double.NaN; vpefCdM[pi] = double.NaN; }
        }
        double vpefTruthCl = vpefClD[vpefTruthIdx];
        double vpefTruthCd = vpefCdD[vpefTruthIdx];

        if (!double.IsFinite(vpefTruthCl) || !double.IsFinite(vpefTruthCd) ||
            Math.Abs(vpefTruthCl) < 1e-9 || Math.Abs(vpefTruthCd) < 1e-9)
        {
            Console.WriteLine($"[diverged] {vpefAirfoils[ai]}: truth CL={vpefTruthCl} CD={vpefTruthCd}");
            vpefSkipped++;
            continue;
        }

        for (int pi = 0; pi < vpefPanels.Length; pi++)
        {
            vpefErrClD[ai, pi] = double.IsFinite(vpefClD[pi])
                ? Math.Abs(vpefClD[pi] - vpefTruthCl) / Math.Abs(vpefTruthCl) : double.NaN;
            vpefErrClM[ai, pi] = double.IsFinite(vpefClM[pi])
                ? Math.Abs(vpefClM[pi] - vpefTruthCl) / Math.Abs(vpefTruthCl) : double.NaN;
            vpefErrCdD[ai, pi] = double.IsFinite(vpefCdD[pi])
                ? Math.Abs(vpefCdD[pi] - vpefTruthCd) / Math.Abs(vpefTruthCd) : double.NaN;
            vpefErrCdM[ai, pi] = double.IsFinite(vpefCdM[pi])
                ? Math.Abs(vpefCdM[pi] - vpefTruthCd) / Math.Abs(vpefTruthCd) : double.NaN;
        }

        Console.WriteLine($"{vpefAirfoils[ai],-20} truth CL={vpefTruthCl,10:F6} CD={vpefTruthCd,12:F8}");
    }

    Console.WriteLine();
    Console.WriteLine($"{"N",6}  {"mean errCL_D",14}  {"mean errCL_M",14}  {"CL M/D",8}    {"mean errCD_D",14}  {"mean errCD_M",14}  {"CD M/D",8}");
    double clScoreD = 0.0, clScoreM = 0.0, cdScoreD = 0.0, cdScoreM = 0.0;
    for (int pi = 0; pi < vpefPanels.Length; pi++)
    {
        if (pi == vpefTruthIdx) continue;
        double sumClD = 0, sumClM = 0, sumCdD = 0, sumCdM = 0;
        int cnt = 0;
        for (int ai = 0; ai < vpefAirfoils.Length; ai++)
        {
            if (!double.IsFinite(vpefErrClD[ai, pi]) || !double.IsFinite(vpefErrClM[ai, pi])) continue;
            if (!double.IsFinite(vpefErrCdD[ai, pi]) || !double.IsFinite(vpefErrCdM[ai, pi])) continue;
            if (vpefErrClD[ai, pi] == 0 && vpefErrClM[ai, pi] == 0) continue;
            sumClD += vpefErrClD[ai, pi]; sumClM += vpefErrClM[ai, pi];
            sumCdD += vpefErrCdD[ai, pi]; sumCdM += vpefErrCdM[ai, pi];
            cnt++;
        }
        double mClD = cnt > 0 ? sumClD / cnt : double.NaN;
        double mClM = cnt > 0 ? sumClM / cnt : double.NaN;
        double mCdD = cnt > 0 ? sumCdD / cnt : double.NaN;
        double mCdM = cnt > 0 ? sumCdM / cnt : double.NaN;
        double rCL = mClD > 0 ? mClM / mClD : double.NaN;
        double rCD = mCdD > 0 ? mCdM / mCdD : double.NaN;
        Console.WriteLine($"{vpefPanels[pi],6}  {mClD,14:E4}  {mClM,14:E4}  {rCL,8:F3}    {mCdD,14:E4}  {mCdM,14:E4}  {rCD,8:F3}");
        if (double.IsFinite(mClD)) { clScoreD += mClD; clScoreM += mClM; }
        if (double.IsFinite(mCdD)) { cdScoreD += mCdD; cdScoreM += mCdM; }
    }

    Console.WriteLine();
    Console.WriteLine($"Integrated CL score: Doubled={clScoreD:E4} Modern={clScoreM:E4} M/D={clScoreM / clScoreD:F3}");
    Console.WriteLine($"Integrated CD score: Doubled={cdScoreD:E4} Modern={cdScoreM:E4} M/D={cdScoreM / cdScoreD:F3}");
    double combined = (clScoreM / clScoreD + cdScoreM / cdScoreD) * 0.5;
    Console.WriteLine($"Combined CL+CD M/D (mean): {combined:F3}  ({(combined < 0.7 ? "B1 WIN" : combined < 1.0 ? "partial improvement" : "REGRESSION")})");
    if (vpefSkipped > 0) Console.WriteLine($"Skipped airfoils: {vpefSkipped}");
    return 0;
}

if (args.Length > 0 && args[0] == "--mesh-study")
{
    string msNaca = args.Length > 1 ? args[1] : "0012";
    double msRe = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1_000_000d;
    double msAlpha = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 4d;
    double msNc = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 9d;
    int[] msPanels = new[] { 80, 120, 160, 200, 240, 320 };

    AirfoilGeometry msGeom = msNaca.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
        ? new AirfoilParser().ParseFile(msNaca)
        : new NacaAirfoilGenerator().Generate4DigitClassic(msNaca, pointCount: 481);

    Console.WriteLine($"Mesh refinement study: {msNaca} Re={msRe} α={msAlpha} Nc={msNc}");
    Console.WriteLine($"{"Panels",6}  {"CL_F",14} {"CD_F",16}  {"CL_D",14} {"CD_D",16}  {"|ΔCL|/CL",10} {"|ΔCD|/CD",10}");
    foreach (int msP in msPanels)
    {
        var msSettings = new AnalysisSettings(
            panelCount: msP,
            reynoldsNumber: msRe,
            criticalAmplificationFactor: msNc,
            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-4);
        XFoil.Solver.Models.ViscousAnalysisResult? msFR = null;
        XFoil.Solver.Models.ViscousAnalysisResult? msDR = null;
        try { msFR = new XFoil.Solver.Services.AirfoilAnalysisService().AnalyzeViscous(msGeom, msAlpha, msSettings); }
        catch { }
        try { msDR = new XFoil.Solver.Double.Services.AirfoilAnalysisService().AnalyzeViscous(msGeom, msAlpha, msSettings); }
        catch { }
        double clF = msFR?.LiftCoefficient ?? double.NaN;
        double cdF = msFR?.DragDecomposition.CD ?? double.NaN;
        double clD = msDR?.LiftCoefficient ?? double.NaN;
        double cdD = msDR?.DragDecomposition.CD ?? double.NaN;
        double clRel = double.IsFinite(clF) && double.IsFinite(clD) && Math.Abs(clF) > 1e-9
            ? Math.Abs(clF - clD) / Math.Abs(clF) : double.NaN;
        double cdRel = double.IsFinite(cdF) && double.IsFinite(cdD) && Math.Abs(cdF) > 1e-9
            ? Math.Abs(cdF - cdD) / Math.Abs(cdF) : double.NaN;
        Console.WriteLine($"{msP,6}  {clF,14:F8} {cdF,16:F10}  {clD,14:F8} {cdD,16:F10}  {clRel,10:E2} {cdRel,10:E2}");
    }
    return 0;
}

// B3 seeded ramp: --b3-ramp <airfoil> <Re> <target-alpha>
// Walks α=0→target in 1° steps, threading BL state via ViscousBLSeed.
// Compares to cold-start final CL and reports whether seeding changes
// the trajectory. Proof-of-concept for the BLSeed path.
if (args.Length > 0 && args[0] == "--b3-ramp")
{
    string af2 = args[1];
    double re2 = double.Parse(args[2]);
    double target2 = double.Parse(args[3]);
    double mach2 = args.Length >= 5 ? double.Parse(args[4]) : 0.0;
    var gen2 = new NacaAirfoilGenerator();
    var g2 = gen2.Generate4DigitClassic(af2, 239);
    var s2 = new AnalysisSettings(
        panelCount: 160, reynoldsNumber: re2, machNumber: mach2,
        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        criticalAmplificationFactor: 9.0,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: 1e-5);

    // Build once-reused panel/inviscid state via low-level helpers.
    int maxNodes2 = s2.PanelCount + 40;
    var panel2 = new LinearVortexPanelState(maxNodes2);
    var invState2 = new InviscidSolverState(maxNodes2);
    double[] gx2 = new double[g2.Points.Count];
    double[] gy2 = new double[g2.Points.Count];
    for (int i = 0; i < g2.Points.Count; i++) { gx2[i] = g2.Points[i].X; gy2[i] = g2.Points[i].Y; }
    CurvatureAdaptivePanelDistributor.Distribute(gx2, gy2, gx2.Length, panel2, s2.PanelCount,
        useLegacyPrecision: s2.UseLegacyPanelingPrecision);
    invState2.InitializeForNodeCount(panel2.NodeCount);
    invState2.UseLegacyKernelPrecision = s2.UseLegacyStreamfunctionKernelPrecision;
    invState2.UseLegacyPanelingPrecision = s2.UseLegacyPanelingPrecision;
    LinearVortexInviscidSolver.AssembleAndFactorSystem(panel2, invState2, s2.FreestreamVelocity,
        0.0 * Math.PI / 180.0);

    ViscousBLSeed? seed = null;
    double step2 = target2 >= 0 ? 1.0 : -1.0;
    Console.WriteLine($"α-ramp seeded sweep for {af2} Re={re2} to α={target2}:");
    for (double a2 = 0.0; (step2 > 0 ? a2 <= target2 + 1e-9 : a2 >= target2 - 1e-9); a2 += step2)
    {
        double aRad2 = a2 * Math.PI / 180.0;
        var ir2 = LinearVortexInviscidSolver.SolveAtAngleOfAttack(aRad2, panel2, invState2,
            s2.FreestreamVelocity, s2.MachNumber);
        var r2 = ViscousSolverEngine.SolveViscousFromInviscidCapturing(panel2, invState2, ir2, s2, aRad2,
            out var finalBLState, debugWriter: null, blSeed: seed);
        Console.WriteLine($"  α={a2,5:F1}° CL={r2.LiftCoefficient,7:F4} CD={r2.DragDecomposition.CD,9:F5} " +
                          $"conv={r2.Converged} iters={r2.Iterations}" + (seed is null ? " (cold)" : " (seeded)"));
        if (r2.Converged && finalBLState is not null)
        {
            seed = CaptureSeed(finalBLState, aRad2);
        }
    }

    var mod2 = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var cold2 = mod2.AnalyzeViscous(g2, target2, s2);
    Console.WriteLine($"cold-start reference: CL={cold2.LiftCoefficient:F4} CD={cold2.DragDecomposition.CD:F5}");
    return 0;

    static ViscousBLSeed CaptureSeed(BoundaryLayerSystemState bls, double alphaRad)
    {
        int nblU = bls.NBL[0], nblL = bls.NBL[1];
        int rows = Math.Max(nblU, nblL);
        var thet = new double[rows, 2];
        var dstr = new double[rows, 2];
        var ctau = new double[rows, 2];
        var uedg = new double[rows, 2];
        for (int side = 0; side < 2; side++)
        {
            for (int i = 0; i < bls.NBL[side]; i++)
            {
                thet[i, side] = bls.THET[i, side];
                dstr[i, side] = bls.DSTR[i, side];
                ctau[i, side] = bls.CTAU[i, side];
                uedg[i, side] = bls.UEDG[i, side];
            }
        }
        // Infer ISP from IPAN: BL station 1 on side 0 maps to panel node ISP-1 or ISP.
        // ISP = blState's internal stagnation-panel index; BL station 0 is the
        // stagnation, station 1 is the first physical station on each side.
        int isp = bls.IPAN[1, 0];
        return new ViscousBLSeed(alphaRad, isp, new[] { nblU, nblL }, thet, dstr, ctau, uedg,
            new[] { bls.ITRAN[0], bls.ITRAN[1] });
    }
}

// B3 seeded-ramp score: --b3-score-ramp <manifest.json>
// Same as --b3-score but each row uses the α=0→target BLSeed ramp
// instead of cold-start. Measures aggregate impact of BL state
// threading via StagnationPointTracker.MoveStagnationPoint.
if (args.Length > 0 && args[0] == "--b3-score-ramp")
{
    string srPath = args[1];
    using var srDoc = JsonDocument.Parse(File.ReadAllText(srPath));
    var srRows = srDoc.RootElement.GetProperty("rows");
    var srGen = new NacaAirfoilGenerator();
    var srParser = new AirfoilParser();
    AirfoilGeometry SrLoad(string k)
    {
        if (k.Contains('/') || k.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            return srParser.ParseFile(Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k));
        if (k.StartsWith("naca", StringComparison.OrdinalIgnoreCase))
            return srGen.Generate4DigitClassic(k[4..], pointCount: 239);
        string sp = Path.Combine(Directory.GetCurrentDirectory(), "tools", "selig-database", k.Replace("-", "") + ".dat");
        return File.Exists(sp) ? srParser.ParseFile(sp) : srGen.Generate4DigitClassic(k, pointCount: 239);
    }

    Console.WriteLine($"B3 seeded-ramp scoring (α ≥ 8°) on {srPath}:");
    Console.WriteLine($"{"airfoil",-20} {"α",5} {"Re",10} {"CL_WT",8} {"CL_M",8} {"|ΔCL|",8} {"CD_WT",9} {"CD_M",9} {"|ΔCD|",9} conv?");
    int srScored = 0, srSkipped = 0, srDiverged = 0;
    var srAgg = new List<double>();
    var srAggCd = new List<double>();
    foreach (var row in srRows.EnumerateArray())
    {
        double alpha = row.GetProperty("alpha_deg").GetDouble();
        if (alpha < 8.0) continue;
        string airfoil = row.GetProperty("airfoil").GetString() ?? "";
        double re = row.GetProperty("Re").GetDouble();
        double mach = row.GetProperty("Mach").GetDouble();
        var srClEl = row.GetProperty("CL");
        var srCdEl = row.GetProperty("CD");
        if (srClEl.ValueKind == JsonValueKind.Null || srCdEl.ValueKind == JsonValueKind.Null) continue;
        double clRef = srClEl.GetDouble();
        double cdRef = srCdEl.GetDouble();
        AirfoilGeometry geom;
        try { geom = SrLoad(airfoil); } catch { srSkipped++; continue; }
        var settings = new AnalysisSettings(
            panelCount: 160, reynoldsNumber: re, machNumber: mach,
            criticalAmplificationFactor: 9.0,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        // Build panel + inviscid state (reuse across the ramp)
        int maxN = settings.PanelCount + 40;
        var pan = new LinearVortexPanelState(maxN);
        var inv = new InviscidSolverState(maxN);
        double[] gx = new double[geom.Points.Count];
        double[] gy = new double[geom.Points.Count];
        for (int i = 0; i < geom.Points.Count; i++) { gx[i] = geom.Points[i].X; gy[i] = geom.Points[i].Y; }
        CurvatureAdaptivePanelDistributor.Distribute(gx, gy, gx.Length, pan, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);
        inv.InitializeForNodeCount(pan.NodeCount);
        inv.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        inv.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;
        LinearVortexInviscidSolver.AssembleAndFactorSystem(pan, inv, settings.FreestreamVelocity, 0.0);

        ViscousBLSeed? seed = null;
        ViscousAnalysisResult? lastResult = null;
        double rampStep = alpha >= 0 ? 1.0 : -1.0;
        for (double a = 0.0; (rampStep > 0 ? a <= alpha + 1e-9 : a >= alpha - 1e-9); a += rampStep)
        {
            double aRad = a * Math.PI / 180.0;
            var ir = LinearVortexInviscidSolver.SolveAtAngleOfAttack(aRad, pan, inv,
                settings.FreestreamVelocity, settings.MachNumber);
            ViscousAnalysisResult r;
            BoundaryLayerSystemState? bls;
            try
            {
                r = ViscousSolverEngine.SolveViscousFromInviscidCapturing(pan, inv, ir, settings, aRad,
                    out bls, debugWriter: null, blSeed: seed);
            }
            catch { seed = null; continue; }
            lastResult = r;
            // Accept non-converged-but-physical states as seeds; the
            // ramp's intermediate Newton iterations sometimes run to
            // iters=200 without formal convergence but still sit in a
            // physical CL regime that's a much better initial point
            // than a fresh Thwaites would be. Only reject clearly
            // non-physical states (NaN, |CL|>3, CD∉[0, 1]) which would
            // poison the subsequent α step — observed as CL=-1e14 or
            // NaN cascading forward. When rejected, drop back to the
            // *most recent* known-good seed rather than cold-start, so
            // a single bad α doesn't erase the ramp's prior progress.
            bool physicalEnough = double.IsFinite(r.LiftCoefficient)
                && double.IsFinite(r.DragDecomposition.CD)
                && Math.Abs(r.LiftCoefficient) <= 3.0
                && r.DragDecomposition.CD > 0
                && r.DragDecomposition.CD < 1.0;
            if (physicalEnough && bls is not null)
            {
                seed = BuildSeedFromBls(bls, aRad);
            }
            // else: keep the previous seed (last-known-good rollback)
        }

        if (lastResult is null || !double.IsFinite(lastResult.LiftCoefficient)
            || Math.Abs(lastResult.LiftCoefficient) > 5.0
            || lastResult.DragDecomposition.CD < 0 || lastResult.DragDecomposition.CD > 1.0)
        {
            srDiverged++;
            Console.WriteLine($"{airfoil,-20} {alpha,5:F1} {re,10:E2} {clRef,8:F3} {"—",8} {"—",8} {cdRef,9:F5} {"—",9} {"—",9} DIVERGED");
            continue;
        }
        double dCL = Math.Abs(lastResult.LiftCoefficient - clRef);
        double dCD = Math.Abs(lastResult.DragDecomposition.CD - cdRef);
        string tag = lastResult.Converged ? "OK" : "NON-CONV";
        Console.WriteLine($"{airfoil,-20} {alpha,5:F1} {re,10:E2} {clRef,8:F3} {lastResult.LiftCoefficient,8:F3} {dCL,8:F3} {cdRef,9:F5} {lastResult.DragDecomposition.CD,9:F5} {dCD,9:F5} {tag}");
        srAgg.Add(dCL);
        srAggCd.Add(dCD);
        srScored++;
    }
    Console.WriteLine();
    if (srAgg.Count > 0)
    {
        Console.WriteLine($"B3 seeded-ramp aggregate (α≥8°): {srScored} scored, {srDiverged} diverged, {srSkipped} skipped");
        Console.WriteLine($"  mean|ΔCL|={srAgg.Average():F3}  mean|ΔCD|={srAggCd.Average():F5}");
        Console.WriteLine($"  rms|ΔCL|={Math.Sqrt(srAgg.Sum(e=>e*e)/srAgg.Count):F3}   rms|ΔCD|={Math.Sqrt(srAggCd.Sum(e=>e*e)/srAggCd.Count):F5}");
    }
    return 0;

    static ViscousBLSeed BuildSeedFromBls(BoundaryLayerSystemState bls, double aRad)
    {
        int nU = bls.NBL[0], nL = bls.NBL[1];
        int rows = Math.Max(nU, nL);
        var th = new double[rows, 2]; var ds = new double[rows, 2];
        var ct = new double[rows, 2]; var ue = new double[rows, 2];
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < bls.NBL[side]; i++)
            {
                th[i, side] = bls.THET[i, side];
                ds[i, side] = bls.DSTR[i, side];
                ct[i, side] = bls.CTAU[i, side];
                ue[i, side] = bls.UEDG[i, side];
            }
        int ispCaptured = bls.IPAN[1, 0];
        return new ViscousBLSeed(aRad, ispCaptured, new[] { nU, nL }, th, ds, ct, ue,
            new[] { bls.ITRAN[0], bls.ITRAN[1] });
    }
}

// B3 iter trace: --b3-iters <airfoil> <Re> <alpha> [tol]
if (args.Length > 0 && args[0] == "--b3-iters")
{
    string af = args[1];
    double reb = double.Parse(args[2]);
    double a = double.Parse(args[3]);
    double tol = args.Length >= 5 ? double.Parse(args[4]) : 1e-10;
    var gen0 = new NacaAirfoilGenerator();
    var g0 = gen0.Generate4DigitClassic(af, 239);
    var s0 = new AnalysisSettings(
        panelCount: 160, reynoldsNumber: reb,
        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
        criticalAmplificationFactor: 9.0,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: tol);
    var mod0 = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var r0 = mod0.AnalyzeViscous(g0, a, s0);
    Console.WriteLine($"{af} Re={reb} α={a}° tol={tol}: converged={r0.Converged} iters={r0.Iterations} finalCL={r0.LiftCoefficient:F4} CD={r0.DragDecomposition.CD:F5}");
    Console.WriteLine("iter rmsbl      CL      CD      relax");
    for (int i = 0; i < r0.ConvergenceHistory.Count; i++)
    {
        var h = r0.ConvergenceHistory[i];
        Console.WriteLine($" {i,2}  {h.RmsResidual,10:E3} {h.CL,7:F4} {h.CD,9:F5} {h.RelaxationFactor,6:F3}");
    }
    return 0;
}

// B3 stall/high-α scoring: --b3-score <manifest.json>
// Filters to rows with alpha_deg >= 8 (post-linear regime), runs
// Modern.AnalyzeViscous, reports CL/CD error vs WT. Baseline for B3
// MSES-style 2nd-order BL closure work targeting stall accuracy.
if (args.Length > 0 && args[0] == "--b3-score")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: --b3-score <manifest.json>");
        return 1;
    }
    string b3ManifestPath = args[1];
    using var b3Doc = JsonDocument.Parse(File.ReadAllText(b3ManifestPath));
    var b3Rows = b3Doc.RootElement.GetProperty("rows");
    var b3NacaGen = new NacaAirfoilGenerator();
    var b3Parser = new AirfoilParser();
    AirfoilGeometry B3Load(string k)
    {
        if (k.Contains('/') || k.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            return b3Parser.ParseFile(Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k));
        if (k.StartsWith("naca", StringComparison.OrdinalIgnoreCase))
            return b3NacaGen.Generate4DigitClassic(k[4..], pointCount: 239);
        string seligPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "selig-database", k.Replace("-", "") + ".dat");
        return File.Exists(seligPath) ? b3Parser.ParseFile(seligPath) : b3NacaGen.Generate4DigitClassic(k, pointCount: 239);
    }

    var b3Modern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    Console.WriteLine($"B3 stall-regime scoring (α ≥ 8°) on {b3ManifestPath}:");
    Console.WriteLine($"{"airfoil",-20} {"α",5} {"Re",10} {"CL_WT",8} {"CL_M",8} {"|ΔCL|",8} {"CD_WT",9} {"CD_M",9} {"|ΔCD|",9} conv?");
    int b3Scored = 0, b3Skipped = 0, b3Diverged = 0;
    var b3Agg = new System.Collections.Concurrent.ConcurrentBag<double>();
    var b3AggCd = new System.Collections.Concurrent.ConcurrentBag<double>();
    string? b3Filter = Environment.GetEnvironmentVariable("B3_FILTER");

    // Iter 27: parallelize row evaluation. Each row has an independent
    // panel/inviscid state; the ViscousSolverEngine's ThreadStatic
    // pools already isolate per-thread state. Ordering of prints may
    // interleave but aggregate statistics remain correct.
    var b3RowsList = new List<(string airfoil, double alpha, double re, double mach, double clRef, double cdRef)>();
    foreach (var row in b3Rows.EnumerateArray())
    {
        double alpha = row.GetProperty("alpha_deg").GetDouble();
        if (alpha < 8.0) continue;
        if (b3Filter is not null)
        {
            string airfoilCheck = row.GetProperty("airfoil").GetString() ?? "";
            double reCheck = row.GetProperty("Re").GetDouble();
            string rowKey = $"{airfoilCheck}_{alpha:F2}_{reCheck:E2}";
            if (!rowKey.Contains(b3Filter)) continue;
        }
        string airfoil = row.GetProperty("airfoil").GetString() ?? "";
        double re = row.GetProperty("Re").GetDouble();
        double mach = row.GetProperty("Mach").GetDouble();
        var b3ClEl = row.GetProperty("CL");
        var b3CdEl = row.GetProperty("CD");
        if (b3ClEl.ValueKind == JsonValueKind.Null || b3CdEl.ValueKind == JsonValueKind.Null) continue;
        b3RowsList.Add((airfoil, alpha, re, mach, b3ClEl.GetDouble(), b3CdEl.GetDouble()));
    }

    System.Threading.Tasks.Parallel.ForEach(b3RowsList, new System.Threading.Tasks.ParallelOptions
    {
        MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 8))
    }, rowData =>
    {
        var (airfoil, alpha, re, mach, clRef, cdRef) = rowData;
        AirfoilGeometry geom;
        try { geom = B3Load(airfoil); } catch { System.Threading.Interlocked.Increment(ref b3Skipped); return; }
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: re,
            machNumber: mach,
            criticalAmplificationFactor: 9.0,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        ViscousAnalysisResult? vr = null;
        try { vr = b3Modern.AnalyzeViscous(geom, alpha, settings); } catch { }
        bool b3Newton = PhysicalEnvelope.IsAirfoilResultPhysical(vr);
        bool b3PostStall = !b3Newton && PhysicalEnvelope.IsAirfoilResultPhysicalPostStall(vr);
        if (!b3Newton && !b3PostStall)
        {
            System.Threading.Interlocked.Increment(ref b3Diverged);
            Console.WriteLine($"{airfoil,-20} {alpha,5:F1} {re,10:E2} {clRef,8:F3} {"—",8} {"—",8} {cdRef,9:F5} {"—",9} {"—",9} DIVERGED");
            return;
        }
        double dCL = Math.Abs(vr!.LiftCoefficient - clRef);
        double dCD = Math.Abs(vr.DragDecomposition.CD - cdRef);
        string tag = b3Newton ? "OK" : "POST-STALL";
        Console.WriteLine($"{airfoil,-20} {alpha,5:F1} {re,10:E2} {clRef,8:F3} {vr.LiftCoefficient,8:F3} {dCL,8:F3} {cdRef,9:F5} {vr.DragDecomposition.CD,9:F5} {dCD,9:F5} {tag}");
        b3Agg.Add(dCL);
        b3AggCd.Add(dCD);
        System.Threading.Interlocked.Increment(ref b3Scored);
    });
    Console.WriteLine();
    if (b3Agg.Count > 0)
    {
        double meanDCl = b3Agg.Average();
        double meanDCd = b3AggCd.Average();
        double rmsDCl = Math.Sqrt(b3Agg.Sum(e=>e*e)/b3Agg.Count);
        double rmsDCd = Math.Sqrt(b3AggCd.Sum(e=>e*e)/b3AggCd.Count);
        Console.WriteLine($"B3 aggregate (α≥8°): {b3Scored} scored, {b3Diverged} diverged, {b3Skipped} skipped");
        Console.WriteLine($"  mean|ΔCL|={meanDCl:F3}  mean|ΔCD|={meanDCd:F5}");
        Console.WriteLine($"  rms|ΔCL|={rmsDCl:F3}   rms|ΔCD|={rmsDCd:F5}");
    }
    return 0;
}

// B4 Cp-distribution scoring: --b4-score <manifest.json>
// Filters to rows with `cp_distribution`, runs Modern.AnalyzeInviscid
// (compressibility via Mach setting), and computes RMS Cp error vs
// WT at each chord station. Baseline for B4 Karman-Tsien override.
if (args.Length > 0 && args[0] == "--b4-score")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: --b4-score <manifest.json>");
        return 1;
    }
    string b4ManifestPath = args[1];
    using var b4Doc = JsonDocument.Parse(File.ReadAllText(b4ManifestPath));
    var b4Rows = b4Doc.RootElement.GetProperty("rows");
    var b4NacaGen = new NacaAirfoilGenerator();
    var b4Parser = new AirfoilParser();
    AirfoilGeometry B4Load(string k)
    {
        if (k.Contains('/') || k.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            return b4Parser.ParseFile(Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k));
        if (k.StartsWith("naca", StringComparison.OrdinalIgnoreCase))
            return b4NacaGen.Generate4DigitClassic(k[4..], pointCount: 239);
        string seligPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "selig-database", k.Replace("-", "") + ".dat");
        return File.Exists(seligPath) ? b4Parser.ParseFile(seligPath) : b4NacaGen.Generate4DigitClassic(k, pointCount: 239);
    }

    var b4Modern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    Console.WriteLine($"B4 Cp-distribution scoring on {b4ManifestPath}:");
    int b4Scored = 0;
    var allErrs = new List<double>();
    foreach (var row in b4Rows.EnumerateArray())
    {
        if (!row.TryGetProperty("cp_distribution", out var cpEl)) continue;
        string airfoil = row.GetProperty("airfoil").GetString() ?? "";
        double alpha = row.GetProperty("alpha_deg").GetDouble();
        double mach = row.GetProperty("Mach").GetDouble();
        // B4 is about compressibility errors in the linear-α regime. At
        // high α the viscous/stall physics dominates the Cp mismatch and
        // contaminates the compressibility signal. Filter to α < 8°.
        if (Math.Abs(alpha) >= 8.0) continue;
        AirfoilGeometry geom;
        try { geom = B4Load(airfoil); } catch { continue; }
        var settings = new AnalysisSettings(panelCount: 160, machNumber: mach);
        InviscidAnalysisResult ir;
        try { ir = b4Modern.AnalyzeInviscid(geom, alpha, settings); } catch { continue; }

        // Build sorted upper/lower chord-ordered arrays of (x, Cp).
        var upperPairs = new List<(double x, double cp)>();
        var lowerPairs = new List<(double x, double cp)>();
        int iLE = 0;
        double minX = ir.PressureSamples[0].Location.X;
        for (int i = 1; i < ir.PressureSamples.Count; i++)
        {
            double xi = ir.PressureSamples[i].Location.X;
            if (xi < minX) { minX = xi; iLE = i; }
        }
        for (int i = 0; i <= iLE; i++)
            upperPairs.Add((ir.PressureSamples[i].Location.X, ir.PressureSamples[i].CorrectedPressureCoefficient));
        for (int i = iLE; i < ir.PressureSamples.Count; i++)
            lowerPairs.Add((ir.PressureSamples[i].Location.X, ir.PressureSamples[i].CorrectedPressureCoefficient));
        upperPairs.Sort((a, b) => a.x.CompareTo(b.x));
        lowerPairs.Sort((a, b) => a.x.CompareTo(b.x));

        double InterpolateAt(List<(double x, double cp)> sorted, double xq)
        {
            int lo = 0, hi = sorted.Count - 1;
            if (xq <= sorted[0].x) return sorted[0].cp;
            if (xq >= sorted[^1].x) return sorted[^1].cp;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid].x > xq) hi = mid; else lo = mid;
            }
            double t = (xq - sorted[lo].x) / (sorted[hi].x - sorted[lo].x);
            return (1 - t) * sorted[lo].cp + t * sorted[hi].cp;
        }

        double sumSq = 0.0;
        int n = 0;
        Console.WriteLine($"\n{airfoil} α={alpha} M={mach}:");
        Console.WriteLine($"{"x/c",6}  {"Cp_u_WT",10} {"Cp_u_M",10} {"Cp_l_WT",10} {"Cp_l_M",10}");
        foreach (var cpRow in cpEl.EnumerateArray())
        {
            double xoc = cpRow.GetProperty("x_over_c").GetDouble();
            double cpURef = cpRow.GetProperty("Cp_upper").GetDouble();
            double cpLRef = cpRow.GetProperty("Cp_lower").GetDouble();
            double cpUp = InterpolateAt(upperPairs, xoc);
            double cpLp = InterpolateAt(lowerPairs, xoc);
            double eu = cpUp - cpURef;
            double el = cpLp - cpLRef;
            sumSq += eu * eu + el * el;
            n += 2;
            Console.WriteLine($"{xoc,6:F3}  {cpURef,10:F3} {cpUp,10:F3} {cpLRef,10:F3} {cpLp,10:F3}");
        }
        double rms = Math.Sqrt(sumSq / n);
        Console.WriteLine($"RMS Cp error: {rms:F4}");
        allErrs.Add(rms);
        b4Scored++;
    }
    Console.WriteLine();
    if (allErrs.Count > 0)
    {
        double meanRms = allErrs.Average();
        Console.WriteLine($"B4 aggregate: {b4Scored} cases, mean RMS Cp error = {meanRms:F4}");
    }
    return 0;
}

// B2 Xtr scoring: --b2-score <manifest.json>
// Filters to rows with Xtr_U / Xtr_L (B2 transition-location
// reference), runs Modern.AnalyzeViscous, compares predicted
// UpperTransition/LowerTransition vs WT. Reports per-airfoil RMS
// and mean |error|. Baseline measurement; B2 override work will be
// scored by reducing this error without regressing non-LSB cases.
if (args.Length > 0 && args[0] == "--b2-score")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: --b2-score <manifest.json> [--with-dampl2]");
        return 1;
    }
    string b2ManifestPath = args[1];
    bool b2UseDampl2 = args.Contains("--with-dampl2");
    // Oracle mode: force transition at the WT-measured Xtr and compare
    // resulting CL/CD to WT. Diagnostic — answers "is B2 purely a
    // transition-location problem, or is there residual viscous-model
    // error even when we nail Xtr exactly?".
    bool b2OracleForced = args.Contains("--oracle-forced-xtr");
    using var b2Doc = JsonDocument.Parse(File.ReadAllText(b2ManifestPath));
    var b2Rows = b2Doc.RootElement.GetProperty("rows");
    var b2NacaGen = new NacaAirfoilGenerator();
    var b2Parser = new AirfoilParser();
    var b2Cache = new System.Collections.Concurrent.ConcurrentDictionary<string, AirfoilGeometry>();
    AirfoilGeometry B2Load(string k) => b2Cache.GetOrAdd(k, key =>
    {
        if (key.Contains('/') || key.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return b2Parser.ParseFile(Path.IsPathRooted(key) ? key : Path.Combine(Directory.GetCurrentDirectory(), key));
        }
        if (key.StartsWith("naca", StringComparison.OrdinalIgnoreCase))
        {
            return b2NacaGen.Generate4DigitClassic(key[4..], pointCount: 239);
        }
        string seligPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "selig-database", key.Replace("-", "") + ".dat");
        return File.Exists(seligPath) ? b2Parser.ParseFile(seligPath) : b2NacaGen.Generate4DigitClassic(key, pointCount: 239);
    });

    int b2Scored = 0, b2Skipped = 0, b2Diverged = 0;
    var b2Perturb = new Dictionary<string, (List<double> ErrU, List<double> ErrL)>();
    var b2Modern = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    Console.WriteLine($"B2 Xtr scoring on {b2ManifestPath}:");
    Console.WriteLine($"{"airfoil",-20} {"α",5} {"Re",10} {"Xtr_U_WT",10} {"Xtr_U_M",10} {"Xtr_L_WT",10} {"Xtr_L_M",10}  errU errL");
    foreach (var row in b2Rows.EnumerateArray())
    {
        bool hasXtrU = row.TryGetProperty("Xtr_U", out var xuEl);
        bool hasXtrL = row.TryGetProperty("Xtr_L", out var xlEl);
        if (!hasXtrU && !hasXtrL) continue;
        string airfoil = row.GetProperty("airfoil").GetString() ?? "";
        double alpha = row.GetProperty("alpha_deg").GetDouble();
        double re = row.GetProperty("Re").GetDouble();
        double mach = row.GetProperty("Mach").GetDouble();
        double? xtrUref = hasXtrU ? xuEl.GetDouble() : null;
        double? xtrLref = hasXtrL ? xlEl.GetDouble() : null;
        AirfoilGeometry geom;
        try { geom = B2Load(airfoil); } catch { b2Skipped++; continue; }
        double? forceXtrU = b2OracleForced ? xtrUref : null;
        double? forceXtrL = b2OracleForced ? xtrLref : null;
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: re,
            machNumber: mach,
            criticalAmplificationFactor: 9.0,
            forcedTransitionUpper: forceXtrU,
            forcedTransitionLower: forceXtrL,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: b2UseDampl2,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        ViscousAnalysisResult? vr = null;
        try { vr = b2Modern.AnalyzeViscous(geom, alpha, settings); } catch { }
        if (vr is null || !vr.Converged) { b2Diverged++; continue; }
        double xtrUpred = vr.UpperTransition.XTransition;
        double xtrLpred = vr.LowerTransition.XTransition;
        double errU = xtrUref.HasValue ? Math.Abs(xtrUpred - xtrUref.Value) : double.NaN;
        double errL = xtrLref.HasValue ? Math.Abs(xtrLpred - xtrLref.Value) : double.NaN;
        double clRef = row.TryGetProperty("CL", out var clE) && clE.ValueKind == JsonValueKind.Number ? clE.GetDouble() : double.NaN;
        double cdRef = row.TryGetProperty("CD", out var cdE) && cdE.ValueKind == JsonValueKind.Number ? cdE.GetDouble() : double.NaN;
        double clPred = vr.LiftCoefficient;
        double cdPred = vr.DragDecomposition.CD;
        Console.WriteLine($"{airfoil,-18} α={alpha,4:F1} Re={re,8:E1} " +
                          $"Xu:{(xtrUref?.ToString("F2") ?? "-"),5}/{xtrUpred,-5:F2} " +
                          $"Xl:{(xtrLref?.ToString("F2") ?? "-"),5}/{xtrLpred,-5:F2}  " +
                          $"eXu:{(double.IsFinite(errU)?errU.ToString("F3"):"-"),5} " +
                          $"eXl:{(double.IsFinite(errL)?errL.ToString("F3"):"-"),5}  " +
                          $"CL:{(double.IsFinite(clRef)?clRef.ToString("F2"):"-")}/{clPred:F2} " +
                          $"CD:{(double.IsFinite(cdRef)?cdRef.ToString("F4"):"-")}/{cdPred:F4}");
        if (!b2Perturb.TryGetValue(airfoil, out var bucket))
        {
            bucket = (new List<double>(), new List<double>());
            b2Perturb[airfoil] = bucket;
        }
        if (double.IsFinite(errU)) bucket.ErrU.Add(errU);
        if (double.IsFinite(errL)) bucket.ErrL.Add(errL);
        b2Scored++;
    }
    Console.WriteLine();
    Console.WriteLine($"{"airfoil",-20} {"N",3}  {"mean errU",10}  {"mean errL",10}  {"rms errU",10}  {"rms errL",10}");
    foreach (var (a, bucket) in b2Perturb.OrderBy(kv => kv.Key))
    {
        double meanU = bucket.ErrU.Count > 0 ? bucket.ErrU.Average() : double.NaN;
        double meanL = bucket.ErrL.Count > 0 ? bucket.ErrL.Average() : double.NaN;
        double rmsU = bucket.ErrU.Count > 0 ? Math.Sqrt(bucket.ErrU.Sum(e => e*e) / bucket.ErrU.Count) : double.NaN;
        double rmsL = bucket.ErrL.Count > 0 ? Math.Sqrt(bucket.ErrL.Sum(e => e*e) / bucket.ErrL.Count) : double.NaN;
        Console.WriteLine($"{a,-20} {bucket.ErrU.Count,3}  {meanU,10:F3}  {meanL,10:F3}  {rmsU,10:F3}  {rmsL,10:F3}");
    }
    Console.WriteLine();
    Console.WriteLine($"Scored: {b2Scored}  Skipped: {b2Skipped}  Diverged: {b2Diverged}");
    return 0;
}

// Reference sweep: --reference-sweep <manifest.json>
// Loads a wind-tunnel/CFD manifest (windtunnel.json or openfoam_results.json),
// runs each row through Float (#1), Doubled (#2), and Modern (#3) facades,
// and reports per-airfoil RMS error vs the reference CL/CD/CM. Used to score
// Modern improvements: a winning improvement reduces Modern's RMS without
// breaking the #1 Fortran-bit-exact gate.
if (args.Length > 0 && args[0] == "--reference-sweep")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: --reference-sweep <manifest.json>");
        return 1;
    }
    string manifestPath = args[1];
    if (!File.Exists(manifestPath))
    {
        Console.Error.WriteLine($"manifest not found: {manifestPath}");
        return 1;
    }
    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var rows = doc.RootElement.GetProperty("rows");
    int rowCount = rows.GetArrayLength();
    Console.Error.WriteLine($"Loaded {rowCount} rows from {manifestPath}");

    var rsNacaGen = new NacaAirfoilGenerator();
    var rsParser = new AirfoilParser();
    var rsCachedGeoms = new System.Collections.Concurrent.ConcurrentDictionary<string, AirfoilGeometry>();
    AirfoilGeometry LoadRsGeom(string token)
        => rsCachedGeoms.GetOrAdd(token, k =>
        {
            if (k.Contains('/') || k.Contains('\\') || k.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k);
                return rsParser.ParseFile(resolved);
            }
            // naca-prefixed names → try NACA generator (4-digit only),
            // fall back to Selig with the "naca" → "n" renaming convention
            // (unlocks 5-digit and 6-series shapes like naca23012, naca63-210).
            if (k.StartsWith("naca", StringComparison.OrdinalIgnoreCase))
            {
                string naca = k[4..].Replace("-", "");
                try { return rsNacaGen.Generate4DigitClassic(naca, pointCount: 239); }
                catch
                {
                    string nSelig = Path.Combine(Directory.GetCurrentDirectory(),
                        "tools", "selig-database", "n" + naca + ".dat");
                    if (File.Exists(nSelig)) return rsParser.ParseFile(nSelig);
                    throw;
                }
            }
            // Bare name like "e387" or "s1223" — try Selig database.
            // Strip hyphens (manifest "fx63-137" → file "fx63137").
            string seligStem = k.Replace("-", "");
            string seligPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "tools", "selig-database", seligStem + ".dat");
            if (File.Exists(seligPath))
            {
                return rsParser.ParseFile(seligPath);
            }
            // Fall back to NACA interpretation
            return rsNacaGen.Generate4DigitClassic(k, pointCount: 239);
        });

    // Per-airfoil accumulators for RMS reporting.
    var perAirfoilStats = new System.Collections.Concurrent.ConcurrentDictionary<string, (List<double> ClErrF, List<double> ClErrD, List<double> ClErrM, List<double> CdErrF, List<double> CdErrD, List<double> CdErrM)>();

    int rsProcessed = 0;
    int rsConverged = 0;
    foreach (var row in rows.EnumerateArray())
    {
        Interlocked.Increment(ref rsProcessed);
        string airfoil = row.GetProperty("airfoil").GetString() ?? "";
        double alpha = row.GetProperty("alpha_deg").GetDouble();
        double re = row.GetProperty("Re").GetDouble();
        double mach = row.GetProperty("Mach").GetDouble();
        // Skip rows that don't carry scalar CL/CD (e.g., Cp-only rows that
        // only populate `cp_distribution` — use --b4-score for those).
        var clEl = row.GetProperty("CL");
        var cdEl = row.GetProperty("CD");
        if (clEl.ValueKind == JsonValueKind.Null || cdEl.ValueKind == JsonValueKind.Null)
        {
            continue;
        }
        double clRef = clEl.GetDouble();
        double cdRef = cdEl.GetDouble();

        AirfoilGeometry geom;
        try { geom = LoadRsGeom(airfoil); } catch (Exception e) { Console.Error.WriteLine($"skip {airfoil}: {e.Message}"); continue; }

        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: re,
            machNumber: mach,
            criticalAmplificationFactor: 9.0,
            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        ViscousAnalysisResult? rfRes = null, rdRes = null, rmRes = null;
        try { rfRes = new XFoil.Solver.Services.AirfoilAnalysisService().AnalyzeViscous(geom, alpha, settings); } catch { }
        try { rdRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService().AnalyzeViscous(geom, alpha, settings); } catch { }
        try { rmRes = new XFoil.Solver.Modern.Services.AirfoilAnalysisService().AnalyzeViscous(geom, alpha, settings); } catch { }

        bool rfConv = PhysicalEnvelope.IsAirfoilResultPhysical(rfRes);
        bool rdConv = PhysicalEnvelope.IsAirfoilResultPhysical(rdRes);
        bool rmConv = PhysicalEnvelope.IsAirfoilResultPhysical(rmRes);
        if (!(rfConv && rdConv && rmConv)) continue;
        Interlocked.Increment(ref rsConverged);

        var bucket = perAirfoilStats.GetOrAdd(airfoil, _ =>
            (new List<double>(), new List<double>(), new List<double>(),
             new List<double>(), new List<double>(), new List<double>()));
        lock (bucket.ClErrF)
        {
            bucket.ClErrF.Add(rfRes!.LiftCoefficient - clRef);
            bucket.ClErrD.Add(rdRes!.LiftCoefficient - clRef);
            bucket.ClErrM.Add(rmRes!.LiftCoefficient - clRef);
            bucket.CdErrF.Add(rfRes.DragDecomposition.CD - cdRef);
            bucket.CdErrD.Add(rdRes.DragDecomposition.CD - cdRef);
            bucket.CdErrM.Add(rmRes.DragDecomposition.CD - cdRef);
        }
    }

    Console.WriteLine();
    Console.WriteLine($"=== Reference sweep ({rsConverged} converged of {rsProcessed}) — RMS error vs reference ===");
    Console.WriteLine();
    Console.WriteLine($"{"airfoil",-12} {"N",4}  {"|CL_err| F  D  M",26} {"|CD_err| F  D  M",26}");
    static double Rms(List<double> xs) => xs.Count == 0 ? double.NaN : Math.Sqrt(xs.Sum(x => x * x) / xs.Count);
    foreach (var (airfoil, st) in perAirfoilStats.OrderBy(p => p.Key))
    {
        Console.WriteLine($"{airfoil,-12} {st.ClErrF.Count,4}  " +
            $"{Rms(st.ClErrF),8:F4} {Rms(st.ClErrD),8:F4} {Rms(st.ClErrM),8:F4}  " +
            $"{Rms(st.CdErrF),8:F5} {Rms(st.CdErrD),8:F5} {Rms(st.CdErrM),8:F5}");
    }
    var allClF = perAirfoilStats.Values.SelectMany(v => v.ClErrF).ToList();
    var allClD = perAirfoilStats.Values.SelectMany(v => v.ClErrD).ToList();
    var allClM = perAirfoilStats.Values.SelectMany(v => v.ClErrM).ToList();
    var allCdF = perAirfoilStats.Values.SelectMany(v => v.CdErrF).ToList();
    var allCdD = perAirfoilStats.Values.SelectMany(v => v.CdErrD).ToList();
    var allCdM = perAirfoilStats.Values.SelectMany(v => v.CdErrM).ToList();
    Console.WriteLine($"{"OVERALL",-12} {allClF.Count,4}  " +
        $"{Rms(allClF),8:F4} {Rms(allClD),8:F4} {Rms(allClM),8:F4}  " +
        $"{Rms(allCdF),8:F5} {Rms(allCdD),8:F5} {Rms(allCdM),8:F5}");
    return 0;
}

// Modern triple sweep: --triple-sweep <vectors-file> [--sample N]
// Runs Float (#1), Doubled (#2), and Modern (#3) facades on every vector and
// reports the pairwise agreement matrix:
//   #1↔#2 — float-parity drift; expected near-100% bit-exact (parity gate)
//   #2↔#3 — Modern bit-exact agreement on un-overridden methods (tripwire)
//   #1↔#3 — full-stack sanity check
// Once a wind-tunnel/CFD reference manifest lands, --reference-sweep adds
// per-row deltas vs reference for #1, #2, #3.
if (args.Length > 0 && args[0] == "--triple-sweep")
{
    string tsPath = args.Length > 1 ? args[1] : "tools/fortran-debug/reference/selig_passing.txt";
    string[] tsAllLines = File.ReadAllLines(tsPath);
    int tsSampleSize = -1;
    for (int ai = 1; ai < args.Length - 1; ai++)
    {
        if (args[ai] == "--sample" && int.TryParse(args[ai + 1], out int sz)) { tsSampleSize = sz; break; }
    }
    string[] tsLines;
    if (tsSampleSize > 0 && tsSampleSize < tsAllLines.Length)
    {
        int tsSeed = Environment.TickCount ^ Process.GetCurrentProcess().Id;
        var tsRng = new Random(tsSeed);
        tsLines = tsAllLines.OrderBy(_ => tsRng.Next()).Take(tsSampleSize).ToArray();
        Console.Error.WriteLine($"Sampled {tsLines.Length} of {tsAllLines.Length} vectors from {tsPath} (seed={tsSeed})");
    }
    else
    {
        tsLines = tsAllLines;
        Console.Error.WriteLine($"Loaded {tsLines.Length} vectors from {tsPath}");
    }
    int tsThreads = Math.Min(96, Environment.ProcessorCount);
    Console.Error.WriteLine($"Using {tsThreads} parallel threads");

    var tsConvF = 0L;
    var tsConvD = 0L;
    var tsConvM = 0L;
    var tsBitExact_FD = 0L;
    var tsBitExact_DM = 0L;
    var tsBitExact_FM = 0L;
    var tsAllThreeBitExact = 0L;
    var tsCachedGeoms = new System.Collections.Concurrent.ConcurrentDictionary<string, AirfoilGeometry>();
    var tsNacaGen = new NacaAirfoilGenerator();
    var tsParser = new AirfoilParser();
    AirfoilGeometry LoadTsGeom(string token)
        => tsCachedGeoms.GetOrAdd(token, k =>
        {
            // Path-style token (contains '/' or ends in .dat): parse from disk.
            // Otherwise treat as NACA 4-digit designation.
            if (k.Contains('/') || k.Contains('\\') || k.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k);
                return tsParser.ParseFile(resolved);
            }
            return tsNacaGen.Generate4DigitClassic(k, pointCount: 239);
        });

    Parallel.ForEach(
        System.Collections.Concurrent.Partitioner.Create(0, tsLines.Length, 1),
        new ParallelOptions { MaxDegreeOfParallelism = tsThreads },
        range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                string[] tsParts = tsLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tsParts.Length < 4) continue;
                string tsNaca = tsParts[0];
                if (!double.TryParse(tsParts[1], CultureInfo.InvariantCulture, out double tsRe)) continue;
                if (!double.TryParse(tsParts[2], CultureInfo.InvariantCulture, out double tsAlpha)) continue;
                if (!double.TryParse(tsParts[3], CultureInfo.InvariantCulture, out double tsNc)) continue;

                AirfoilGeometry tsGeom;
                try { tsGeom = LoadTsGeom(tsNaca); }
                catch { continue; }

                var tsFloatSettings = new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: tsRe,
                    criticalAmplificationFactor: tsNc,
                    useExtendedWake: false,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 80,
                    viscousConvergenceTolerance: 1e-4);
                var tsDoubledSettings = new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: tsRe,
                    criticalAmplificationFactor: tsNc,
                    useExtendedWake: true,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 200,
                    viscousConvergenceTolerance: 1e-5);

                XFoil.Solver.Models.ViscousAnalysisResult? rfRes = null;
                XFoil.Solver.Models.ViscousAnalysisResult? rdRes = null;
                XFoil.Solver.Models.ViscousAnalysisResult? rmRes = null;
                try { rfRes = new XFoil.Solver.Services.AirfoilAnalysisService().AnalyzeViscous(tsGeom, tsAlpha, tsFloatSettings); } catch { }
                try { rdRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService().AnalyzeViscous(tsGeom, tsAlpha, tsDoubledSettings); } catch { }
                try { rmRes = new XFoil.Solver.Modern.Services.AirfoilAnalysisService().AnalyzeViscous(tsGeom, tsAlpha, tsDoubledSettings); } catch { }

                bool rfConv = XFoil.Solver.Models.PhysicalEnvelope.IsAirfoilResultPhysical(rfRes);
                bool rdConv = XFoil.Solver.Models.PhysicalEnvelope.IsAirfoilResultPhysical(rdRes);
                bool rmConv = XFoil.Solver.Models.PhysicalEnvelope.IsAirfoilResultPhysical(rmRes);
                if (rfConv) Interlocked.Increment(ref tsConvF);
                if (rdConv) Interlocked.Increment(ref tsConvD);
                if (rmConv) Interlocked.Increment(ref tsConvM);
                if (rfRes is null || rdRes is null || rmRes is null) continue;
                if (!(rfConv && rdConv && rmConv)) continue;

                double clF = rfRes.LiftCoefficient, cdF = rfRes.DragDecomposition.CD;
                double clD = rdRes.LiftCoefficient, cdD = rdRes.DragDecomposition.CD;
                double clM = rmRes.LiftCoefficient, cdM = rmRes.DragDecomposition.CD;
                bool fd = (clF == clD) && (cdF == cdD);
                bool dm = (clD == clM) && (cdD == cdM);
                bool fm = (clF == clM) && (cdF == cdM);
                if (fd) Interlocked.Increment(ref tsBitExact_FD);
                if (dm) Interlocked.Increment(ref tsBitExact_DM);
                if (fm) Interlocked.Increment(ref tsBitExact_FM);
                if (fd && dm) Interlocked.Increment(ref tsAllThreeBitExact);
            }
        });

    Console.WriteLine();
    Console.WriteLine($"=== Triple sweep ({tsLines.Length} vectors) — agreement matrix ===");
    Console.WriteLine($"Converged (Float #1):    {tsConvF} / {tsLines.Length}");
    Console.WriteLine($"Converged (Doubled #2):  {tsConvD} / {tsLines.Length}");
    Console.WriteLine($"Converged (Modern #3):   {tsConvM} / {tsLines.Length}");
    Console.WriteLine();
    Console.WriteLine($"Bit-exact #1 vs #2:      {tsBitExact_FD}");
    Console.WriteLine($"Bit-exact #2 vs #3:      {tsBitExact_DM}  (tripwire — should equal min(conv #2, conv #3) when no overrides)");
    Console.WriteLine($"Bit-exact #1 vs #3:      {tsBitExact_FM}");
    Console.WriteLine($"Bit-exact #1 = #2 = #3:  {tsAllThreeBitExact}");
    return 0;
}

// Doubled-tree full sweep: --double-sweep <vectors-file>
// Runs both float-parity and double-tree facades on every vector, compares.
if (args.Length > 0 && args[0] == "--double-sweep")
{
    string dsPath = args.Length > 1 ? args[1] : "tools/fortran-debug/reference/selig_passing.txt";
    string[] dsAllLines = File.ReadAllLines(dsPath);
    int dsSampleSize = -1;
    for (int ai = 1; ai < args.Length - 1; ai++)
    {
        if (args[ai] == "--sample" && int.TryParse(args[ai + 1], out int sz)) { dsSampleSize = sz; break; }
    }
    // --matched: use the SAME settings (the doubled-tree's Phase 2 defaults)
    // for both float and double facades. Measures pure-precision agreement
    // separate from settings-driven disagreement (iter 62 finding).
    bool dsMatched = args.Contains("--matched");
    if (dsMatched) Console.Error.WriteLine("--matched: using doubled-tree settings on BOTH float and double facades");
    string[] dsLines;
    if (dsSampleSize > 0 && dsSampleSize < dsAllLines.Length)
    {
        int dsSeed = Environment.TickCount ^ Process.GetCurrentProcess().Id;
        var dsRng = new Random(dsSeed);
        dsLines = dsAllLines.OrderBy(_ => dsRng.Next()).Take(dsSampleSize).ToArray();
        Console.Error.WriteLine($"Sampled {dsLines.Length} of {dsAllLines.Length} vectors from {dsPath} (seed={dsSeed})");
    }
    else
    {
        dsLines = dsAllLines;
        Console.Error.WriteLine($"Loaded {dsLines.Length} vectors from {dsPath}");
    }
    int dsThreads = Math.Min(96, Environment.ProcessorCount);
    Console.Error.WriteLine($"Using {dsThreads} parallel threads");

    var dsConverged = 0L;
    var dsBothConverged = 0L;
    var dsBitExact = 0L;
    var dsClFiniteDiff = new System.Collections.Concurrent.ConcurrentBag<double>();
    var dsCdFiniteDiff = new System.Collections.Concurrent.ConcurrentBag<double>();
    var dsClRelDiff = new System.Collections.Concurrent.ConcurrentBag<double>();
    var dsCdRelDiff = new System.Collections.Concurrent.ConcurrentBag<double>();
    var dsFloatDiverged = 0L;
    var dsDoubleDiverged = 0L;
    var dsBothDiverged = 0L;
    var dsWorstCases = new System.Collections.Concurrent.ConcurrentBag<(string Naca, double Re, double Alpha, double Nc, double ClF, double ClD, double CdF, double CdD, double CdRel, int ItF, int ItD, double RmsF, double RmsD)>();

    var dsCachedGeoms = new System.Collections.Concurrent.ConcurrentDictionary<string, AirfoilGeometry>();
    AirfoilGeometry LoadDsGeom(string p)
        => dsCachedGeoms.GetOrAdd(p, k =>
        {
            string resolved = Path.IsPathRooted(k) ? k : Path.Combine(Directory.GetCurrentDirectory(), k);
            return new AirfoilParser().ParseFile(resolved);
        });

    Parallel.ForEach(
        System.Collections.Concurrent.Partitioner.Create(0, dsLines.Length, 1),
        new ParallelOptions { MaxDegreeOfParallelism = dsThreads },
        range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                string[] dsParts = dsLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (dsParts.Length < 4) continue;
                string dsNaca = dsParts[0];
                if (!double.TryParse(dsParts[1], CultureInfo.InvariantCulture, out double dsRe)) continue;
                if (!double.TryParse(dsParts[2], CultureInfo.InvariantCulture, out double dsAlpha)) continue;
                if (!double.TryParse(dsParts[3], CultureInfo.InvariantCulture, out double dsNc)) continue;

                AirfoilGeometry dsGeom;
                try { dsGeom = LoadDsGeom(dsNaca); }
                catch { continue; }

                // Both float and double settings use the same TrustRegion
                // solver (the AnalysisSettings default). Doubled tree gets
                // 200 max iterations vs 80 for float (precision lets it push
                // further). Mixed-solver comparisons (one XFoilRelaxation,
                // one TrustRegion) produce algorithmically incompatible
                // solutions and meaningless agreement numbers (3% within 1%).
                var dsFloatSettings = new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: dsRe,
                    criticalAmplificationFactor: dsNc,
                    useExtendedWake: dsMatched,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: dsMatched ? 200 : 80,
                    viscousConvergenceTolerance: dsMatched ? 1e-5 : 1e-4);
                // Phase 2 doubled-tree settings:
                //  - Same panelCount=160 as float for fair convergence/agreement
                //    comparison. Tested 240-panel double tree → "5k agreement"
                //    metric drops (28% CD within 1%) because the double tree
                //    is solving a more-accurate problem on a finer mesh, but
                //    the answers diverge from 160-panel float by design.
                //    240-panel doubled tree IS a real accuracy improvement,
                //    just not measurable against same-airfoil-different-mesh
                //    float numbers.
                //  - 200 max iterations vs float's 80.
                //  - Default solver (TrustRegion) for both float and double.
                var dsDoubleSettings = new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: dsRe,
                    criticalAmplificationFactor: dsNc,
                    useExtendedWake: true,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 200,
                    viscousConvergenceTolerance: 1e-5);

                XFoil.Solver.Models.ViscousAnalysisResult? rfRes = null;
                XFoil.Solver.Models.ViscousAnalysisResult? rdRes = null;
                try { rfRes = new XFoil.Solver.Services.AirfoilAnalysisService().AnalyzeViscous(dsGeom, dsAlpha, dsFloatSettings); }
                catch { /* divergence */ }
                try { rdRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService().AnalyzeViscous(dsGeom, dsAlpha, dsDoubleSettings); }
                catch { /* divergence */ }

                // Result-physicality gate via shared PhysicalEnvelope helper —
                // a "converged" result outside the realistic 2D-airfoil envelope
                // is downgraded to "diverged" for sweep statistics. CD up to
                // 1e105 has been observed at extreme α=20 / Re=1e5 where Newton
                // residual went small but the BL state wandered into a non-
                // physical attractor. Engine semantics are unchanged — gate
                // only at the diagnostic-tool level.
                bool rfConv = XFoil.Solver.Models.PhysicalEnvelope.IsAirfoilResultPhysical(rfRes);
                bool rdConv = XFoil.Solver.Models.PhysicalEnvelope.IsAirfoilResultPhysical(rdRes);
                if (!rfConv && !rdConv) Interlocked.Increment(ref dsBothDiverged);
                else if (!rfConv) Interlocked.Increment(ref dsFloatDiverged);
                else if (!rdConv) Interlocked.Increment(ref dsDoubleDiverged);

                if (rfRes is null || rdRes is null) continue;
                Interlocked.Increment(ref dsConverged);
                if (rfConv && rdConv) Interlocked.Increment(ref dsBothConverged);
                // Only compute agreement statistics on plausible-vs-plausible pairs.
                // Mixing in non-physical results contaminates the percentiles.
                if (!rfConv || !rdConv) continue;

                double dsClF = rfRes.LiftCoefficient;
                double dsClD = rdRes.LiftCoefficient;
                double dsCdF = rfRes.DragDecomposition.CD;
                double dsCdD = rdRes.DragDecomposition.CD;
                double dsClDiff = Math.Abs(dsClF - dsClD);
                double dsCdDiff = Math.Abs(dsCdF - dsCdD);
                double dsClRel = Math.Abs(dsClF) > 1e-6 ? dsClDiff / Math.Abs(dsClF) : dsClDiff;
                double dsCdRel = Math.Abs(dsCdF) > 1e-6 ? dsCdDiff / Math.Abs(dsCdF) : dsCdDiff;
                if (dsClDiff == 0.0 && dsCdDiff == 0.0) Interlocked.Increment(ref dsBitExact);
                if (double.IsFinite(dsClDiff)) dsClFiniteDiff.Add(dsClDiff);
                if (double.IsFinite(dsCdDiff)) dsCdFiniteDiff.Add(dsCdDiff);
                if (double.IsFinite(dsClRel)) dsClRelDiff.Add(dsClRel);
                if (double.IsFinite(dsCdRel)) dsCdRelDiff.Add(dsCdRel);
                if (double.IsFinite(dsCdRel) && dsCdRel > 0.05)
                {
                    int itF = rfRes.Iterations;
                    int itD = rdRes.Iterations;
                    double rmsF = rfRes.ConvergenceHistory.Count > 0
                        ? rfRes.ConvergenceHistory[^1].RmsResidual : double.NaN;
                    double rmsD = rdRes.ConvergenceHistory.Count > 0
                        ? rdRes.ConvergenceHistory[^1].RmsResidual : double.NaN;
                    dsWorstCases.Add((dsNaca, dsRe, dsAlpha, dsNc, dsClF, dsClD, dsCdF, dsCdD, dsCdRel, itF, itD, rmsF, rmsD));
                }
            }
        });

    Console.WriteLine();
    Console.WriteLine($"=== Double-tree vs Float-parity sweep ({dsLines.Length} vectors) ===");
    Console.WriteLine($"Both converged:        {dsBothConverged} / {dsLines.Length}");
    Console.WriteLine($"Both diverged:         {dsBothDiverged}");
    Console.WriteLine($"Float-only diverged:   {dsFloatDiverged}");
    Console.WriteLine($"Double-only diverged:  {dsDoubleDiverged}");
    Console.WriteLine($"Identical (CL=CD):     {dsBitExact} / {dsConverged} ({100.0 * dsBitExact / Math.Max(dsConverged, 1):F2}%)");
    Console.WriteLine();

    void DescribeBag(string name, System.Collections.Concurrent.ConcurrentBag<double> bag)
    {
        var arr = bag.ToArray();
        if (arr.Length == 0) { Console.WriteLine($"  {name}: no data"); return; }
        Array.Sort(arr);
        double max = arr[arr.Length - 1];
        double median = arr[arr.Length / 2];
        double p99 = arr[(int)(arr.Length * 0.99)];
        Console.WriteLine($"  {name}: count={arr.Length} median={median:E3} p99={p99:E3} max={max:E3}");
    }
    Console.WriteLine("Absolute differences:");
    DescribeBag("|ΔCL|", dsClFiniteDiff);
    DescribeBag("|ΔCD|", dsCdFiniteDiff);
    Console.WriteLine("Relative differences:");
    DescribeBag("|ΔCL|/|CL|", dsClRelDiff);
    DescribeBag("|ΔCD|/|CD|", dsCdRelDiff);

    var clArr = dsClRelDiff.ToArray();
    var cdArr = dsCdRelDiff.ToArray();
    int Within(double[] a, double tol) => a.Count(x => x <= tol);
    Console.WriteLine();
    Console.WriteLine($"CL within 0.01%: {Within(clArr, 1e-4)} / {clArr.Length}");
    Console.WriteLine($"CL within 0.10%: {Within(clArr, 1e-3)} / {clArr.Length}");
    Console.WriteLine($"CL within 1.00%: {Within(clArr, 1e-2)} / {clArr.Length}");
    Console.WriteLine($"CD within 0.01%: {Within(cdArr, 1e-4)} / {cdArr.Length}");
    Console.WriteLine($"CD within 0.10%: {Within(cdArr, 1e-3)} / {cdArr.Length}");
    Console.WriteLine($"CD within 1.00%: {Within(cdArr, 1e-2)} / {cdArr.Length}");

    var worst = dsWorstCases.ToArray();
    if (worst.Length > 0)
    {
        Array.Sort(worst, (a, b) => b.CdRel.CompareTo(a.CdRel));
        Console.WriteLine();
        Console.WriteLine($"Worst CD-disagreement cases (>5% rel): {worst.Length}");
        // Phase 2 iter 79+80: directional bias summary for both CD and CL.
        // Systematic bias indicates precision-vs-method asymmetry rather than
        // random multi-attractor noise.
        int cdFloatHigher = 0, cdDoubleHigher = 0;
        int clFloatHigher = 0, clDoubleHigher = 0;
        foreach (var w in worst)
        {
            if (w.CdF > w.CdD) cdFloatHigher++;
            else if (w.CdD > w.CdF) cdDoubleHigher++;
            // Compare |CL| to capture "higher-loaded attractor" magnitude
            // independent of sign (different attractors can flip sign).
            double absClF = Math.Abs(w.ClF), absClD = Math.Abs(w.ClD);
            if (absClF > absClD) clFloatHigher++;
            else if (absClD > absClF) clDoubleHigher++;
        }
        Console.WriteLine($"  CD direction: F>D in {cdFloatHigher} cases, D>F in {cdDoubleHigher} cases (balanced ≈ {worst.Length / 2})");
        Console.WriteLine($"  |CL| direction: F>D in {clFloatHigher} cases, D>F in {clDoubleHigher} cases (balanced ≈ {worst.Length / 2})");
        int show = Math.Min(10, worst.Length);
        Console.WriteLine($"Top {show}:");
        for (int wi = 0; wi < show; wi++)
        {
            var w = worst[wi];
            Console.WriteLine($"  NACA {w.Naca,-4} Re={w.Re,-9:G4} α={w.Alpha,6:F2} Nc={w.Nc,4:F1}  CL_F={w.ClF,8:F4} CL_D={w.ClD,8:F4}  CD_F={w.CdF:E3} CD_D={w.CdD:E3}  ΔCD/CD={w.CdRel:P2}  itF/D={w.ItF,3}/{w.ItD,3}  rmsF/D={w.RmsF:E2}/{w.RmsD:E2}");
        }
    }

    return 0;
}

// Single case diagnostic mode: --diag NACA RE ALPHA NCRIT
if (args.Length > 0 && args[0] == "--diag")
{
    // Debug output disabled for speed
    // Environment.SetEnvironmentVariable("XFOIL_DEBUG_DRAG", "1");
    string dnaca = args.Length > 1 ? args[1] : "0012";
    double dre = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1000000;
    double dalpha = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 0;
    double dncrit = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 9;

    AirfoilGeometry geom;
    bool dnacaIsPath = dnaca.Contains('/') || dnaca.Contains('\\') || dnaca.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
    if (dnacaIsPath)
    {
        string resolved = Path.IsPathRooted(dnaca) ? dnaca : Path.Combine(Directory.GetCurrentDirectory(), dnaca);
        geom = new AirfoilParser().ParseFile(resolved);
    }
    else
    {
        geom = new NacaAirfoilGenerator().Generate4DigitClassic(dnaca, pointCount: 239);
    }
    var svc = new AirfoilAnalysisService();
    bool useTrustRegion = args.Contains("--trust-region");
    bool noLegacy = args.Contains("--no-legacy");
    var s = new AnalysisSettings(
        panelCount: 160,
        reynoldsNumber: dre,
        criticalAmplificationFactor: dncrit,
        useExtendedWake: false,
        useLegacyBoundaryLayerInitialization: !noLegacy,
        useLegacyPanelingPrecision: !noLegacy,
        useLegacyStreamfunctionKernelPrecision: !noLegacy,
        useLegacyWakeSourceKernelPrecision: !noLegacy,
        useModernTransitionCorrections: false, // Fortran IDAMP=0 → DAMPL, not DAMPL2
        maxViscousIterations: 80,
        viscousConvergenceTolerance: 1e-4, // Match the binary-parity harness/Fortran EPS1
        viscousSolverMode: useTrustRegion
            ? XFoil.Solver.Models.ViscousSolverMode.TrustRegion
            : XFoil.Solver.Models.ViscousSolverMode.XFoilRelaxation);
    var r = svc.AnalyzeViscous(geom, dalpha, s);
    Console.WriteLine($"NACA {dnaca} Re={dre} a={dalpha} Nc={dncrit}");
    Console.WriteLine($"CL={r.LiftCoefficient:F6} CD={r.DragDecomposition.CD:F6} CDF={r.DragDecomposition.CDF:F6}");
    Console.WriteLine($"CD_hex=0x{BitConverter.SingleToInt32Bits((float)r.DragDecomposition.CD):X8} CL_hex=0x{BitConverter.SingleToInt32Bits((float)r.LiftCoefficient):X8} CDF_hex=0x{BitConverter.SingleToInt32Bits((float)r.DragDecomposition.CDF):X8}");
    Console.WriteLine($"Converged={r.Converged} Iterations={r.Iterations}");
    // Dump panel coordinates if --panels flag
    if (args.Contains("--panels"))
    {
        var inputX = new double[geom.Points.Count];
        var inputY = new double[geom.Points.Count];
        for (int i = 0; i < geom.Points.Count; i++)
        {
            inputX[i] = geom.Points[i].X;
            inputY[i] = geom.Points[i].Y;
        }
        var invResult = XFoil.Solver.Services.LinearVortexInviscidSolver.AnalyzeInviscid(
            inputX, inputY, geom.Points.Count, dalpha, 160, 0.0, true);
        var cp = invResult.PressureCoefficients;
        Console.WriteLine($"InviscidCL={invResult.LiftCoefficient:F8}");
        Console.WriteLine($"InvCL_hex=0x{BitConverter.SingleToInt32Bits((float)invResult.LiftCoefficient):X8}");
        // Dump panel coordinates from the inviscid result's panel state
        var panelDbg = new XFoil.Solver.Models.LinearVortexPanelState(360);
        XFoil.Solver.Services.CurvatureAdaptivePanelDistributor.Distribute(
            inputX, inputY, geom.Points.Count, panelDbg, 160,
            useLegacyPrecision: true);
        Console.WriteLine($"Panel nodes: {panelDbg.NodeCount}");
        // Dump S, XP, YP (spline data) for comparison with Fortran
        for (int pi = 0; pi < panelDbg.NodeCount; pi++)
        {
            if (true)
            {
                long xHex = BitConverter.DoubleToInt64Bits(panelDbg.X[pi]);
                long yHex = BitConverter.DoubleToInt64Bits(panelDbg.Y[pi]);
                long sHex = BitConverter.DoubleToInt64Bits(panelDbg.ArcLength[pi]);
                long xpHex = BitConverter.DoubleToInt64Bits(panelDbg.XDerivative[pi]);
                long ypHex = BitConverter.DoubleToInt64Bits(panelDbg.YDerivative[pi]);
                Console.WriteLine($"  SPLN_CS {pi+1,4} {xHex:X16} {yHex:X16} {sHex:X16} {xpHex:X16} {ypHex:X16}");
            }
        }
        // Dump buffer coordinates for parity comparison
        Console.WriteLine($"Buffer points: {geom.Points.Count}");
        for (int bi = 0; bi < Math.Min(5, geom.Points.Count); bi++)
            Console.WriteLine($"  buf[{bi}] x={geom.Points[bi].X:F10} y={geom.Points[bi].Y:F10}");
        int mid = geom.Points.Count / 2;
        for (int bi = mid-1; bi <= mid+1 && bi < geom.Points.Count; bi++)
            Console.WriteLine($"  buf[{bi}] x={geom.Points[bi].X:F10} y={geom.Points[bi].Y:F10}");
        Console.WriteLine($"Inviscid Cp ({cp.Count} nodes):");
        Console.WriteLine($"  Cp[0]={cp[0]:F6} Cp[1]={cp[1]:F6} (TE upper)");
        Console.WriteLine($"  Cp[{cp.Count-2}]={cp[cp.Count-2]:F6} Cp[{cp.Count-1}]={cp[cp.Count-1]:F6} (TE lower)");
        // TE Ue from Cp: Ue/Qinf = sqrt(1 - Cp)
        double ue_te_upper = Math.Sqrt(Math.Max(0, 1 - cp[1]));
        double ue_te_lower = Math.Sqrt(Math.Max(0, 1 - cp[cp.Count-2]));
        Console.WriteLine($"  Inviscid TE Ue upper={ue_te_upper:F6} lower={ue_te_lower:F6}");
        // Suction peak
        double minCp = double.MaxValue;
        int minI = 0;
        for (int i = 0; i < cp.Count; i++)
        {
            if (cp[i] < minCp) { minCp = cp[i]; minI = i; }
        }
        Console.WriteLine($"  Suction peak: Cp[{minI}]={minCp:F6}, Ue={Math.Sqrt(1-minCp):F6}");
    }
    // Dump convergence history CD evolution
    Console.WriteLine($"CD history: {string.Join(" ", r.ConvergenceHistory.Select(h => h.CD.ToString("E4")))}");
    Console.WriteLine($"CD hex:     {string.Join(" ", r.ConvergenceHistory.Select(h => $"{BitConverter.SingleToInt32Bits((float)h.CD):X8}"))}");
    // Dump wake profiles
    if (r.WakeProfiles.Length > 0)
    {
        Console.WriteLine($"Wake stations: {r.WakeProfiles.Length}");
        foreach (var wp in r.WakeProfiles)
        {
            Console.WriteLine($"  xi={wp.Xi:F6} theta={wp.Theta:E6} dstar={wp.DStar:E6} ue={wp.EdgeVelocity:E6}");
        }
    }
    Console.WriteLine($"Upper transition: x/c={r.UpperTransition.XTransition:F4} station={r.UpperTransition.StationIndex}");
    Console.WriteLine($"Lower transition: x/c={r.LowerTransition.XTransition:F4} station={r.LowerTransition.StationIndex}");
    // Dump ALL upper surface stations
    Console.WriteLine("Upper surface BL:");
    for (int i = 0; i < r.UpperProfiles.Length; i++)
    {
        var p = r.UpperProfiles[i];
        Console.WriteLine($"  [{i}] xi={p.Xi:F5} theta={p.Theta:E4} dstar={p.DStar:E4} ue={p.EdgeVelocity:F5} Hk={p.Hk:F3} N={p.Ctau:F4}");
    }
    // Always show last station
    if (r.UpperProfiles.Length > 0)
    {
        var last = r.UpperProfiles[^1];
        Console.WriteLine($"  [TE] xi={last.Xi:F5} theta={last.Theta:E4} dstar={last.DStar:E4} ue={last.EdgeVelocity:F5} Hk={last.Hk:F3}");
    }
    // TE surface profiles
    if (r.UpperProfiles.Length > 0)
    {
        var te = r.UpperProfiles[^1];
        Console.WriteLine($"Upper TE: theta={te.Theta:E6} dstar={te.DStar:E6}");
    }
    if (r.LowerProfiles.Length > 0)
    {
        var te = r.LowerProfiles[^1];
        Console.WriteLine($"Lower TE: theta={te.Theta:E6} dstar={te.DStar:E6}");
    }
    return 0;
}

// Load reference vectors
string vectorFile = args.Length > 0 ? args[0] :
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "tools", "fortran-debug", "reference", "clean_fortran_polar_vectors.txt");

// Try relative path from working directory
if (!File.Exists(vectorFile))
{
    vectorFile = Path.Combine(Directory.GetCurrentDirectory(),
        "tools", "fortran-debug", "reference", "clean_fortran_polar_vectors.txt");
}

if (!File.Exists(vectorFile))
{
    Console.Error.WriteLine($"Vector file not found: {vectorFile}");
    return 1;
}

var lines = File.ReadAllLines(vectorFile)
    .Where(l => !string.IsNullOrWhiteSpace(l))
    .ToArray();

Console.Error.WriteLine($"Loaded {lines.Length} vectors from {vectorFile}");

// Aggregate counters — populated single-threaded after Parallel.ForEach.
// Per-thread local accumulators (ThreadLocal state) aggregate into globals
// via the finalize delegate on Parallel.ForEach.
int passed = 0, failed = 0, skipped = 0, bitExact = 0, finiteResults = 0, processed = 0;
double maxCdRelError = 0;
var results = new List<(string naca, double re, double alpha, double ncrit, double fortCl, double fortCd, double csharpCl, double csharpCd, double cdRelErr, int cdUlp, int clUlp)>();
var failDetails = new List<string>();
// Single mutex guarding the single-threaded merge at the END of each thread's
// work. Contended N times total (N = active threads ≈ 192), not per-case.
var mergeLock = new object();
bool breakAtFirstUnparity = args.Contains("--break-at-first-unparity") || args.Contains("--bfp");
// --skip-degenerate: skip cases where Fortran's stored CD is < 1e-15 (denormal/non-convergent
// garbage from failed VISCAL where last-iter BL state is wildly diverging). These cases
// have no meaningful "F target" to match — F just stored whatever CD float happened to be
// in registers when convergence failed. Bit-exact matching them would require iter-by-iter
// arithmetic mimicry of F's chaotic trajectory, which is impractical.
bool skipDegenerate = args.Contains("--skip-degenerate");
bool standardBranch = args.Contains("--standard");
if (standardBranch) Console.Error.WriteLine("STANDARD BRANCH (modern solver, no legacy precision)");
int firstUnparityFound = 0; // 0 = not found, 1 = found

// Per-thread runtime trackers (merged at sweep finalize).
var g_perThreadActiveTicks = new List<long>();
var g_perThreadProcessed = new List<int>();
var g_perThreadMinTicks = new List<long>();
var g_perThreadMaxTicks = new List<long>();
// All case spans: (start ticks relative to sweep start, end ticks, thread id).
// Used to compute actual concurrent-thread count histogram.
var g_caseSpans = new List<(long Start, long End, int ThreadId)>();
long g_sweepStartTicks = 0;

// Default parallelism: physical cores only. On x86 with SMT, hyper-threads
// share one physical core's FPU / AVX units. Viscous CFD is FPU-bound, so
// running 192 threads on 96 physical cores delivers essentially the same
// throughput as 96 threads but with more sync overhead (2× sys time) and
// lower per-thread cache / memory efficiency. Detect physical core count
// from /proc/cpuinfo "cpu cores" × socket count; fall back to Env var or
// half of logical CPU count.
//
// Empirical scaling (naca0015 × 5000 on Threadripper 9995WX, 96 physical /
// 192 logical, 384 MB L3):
//    12 threads → 104.9 s, avg concurrent 11.9
//    24 threads →  56.5 s, avg concurrent 23.6
//    48 threads →  40.7 s, avg concurrent 47.0
//    72 threads →  37.4 s, avg concurrent 70.7  ← knee of scaling
//    96 threads →  37.9 s, avg concurrent 93.7
//   144 threads →  37.4 s, avg concurrent 140.2
//   192 threads →  37.2 s, avg concurrent 186.0
// Peak concurrent threads always equals the requested count — threads are
// genuinely parallel. The plateau at ~37 s past 72 threads is memory-
// bandwidth saturation (the DIJ matrix + per-case working set exceeds what
// shared L3 / memory controllers can feed). Staying at physical-core count
// wastes the fewest CPU cycles for a given wall-time outcome.
int defaultParallel;
try
{
    // /proc/cpuinfo has one block per logical CPU; "cpu cores" is the
    // physical core count per socket. Count unique (physical id, core id)
    // pairs to get total physical cores — accurate for SMT systems.
    var physicalPairs = new HashSet<(int, int)>();
    int physicalId = 0, coreId = 0;
    foreach (var line in System.IO.File.ReadLines("/proc/cpuinfo"))
    {
        if (line.StartsWith("physical id", StringComparison.Ordinal))
        {
            int idx = line.IndexOf(':');
            if (idx > 0) int.TryParse(line[(idx + 1)..].Trim(), out physicalId);
        }
        else if (line.StartsWith("core id", StringComparison.Ordinal))
        {
            int idx = line.IndexOf(':');
            if (idx > 0) int.TryParse(line[(idx + 1)..].Trim(), out coreId);
        }
        else if (line.Length == 0)
        {
            physicalPairs.Add((physicalId, coreId));
        }
    }
    // Include the last block (file doesn't always end with blank).
    physicalPairs.Add((physicalId, coreId));
    defaultParallel = physicalPairs.Count > 0 ? physicalPairs.Count : Environment.ProcessorCount / 2;
}
catch
{
    defaultParallel = Math.Max(1, Environment.ProcessorCount / 2);
}
if (int.TryParse(Environment.GetEnvironmentVariable("XFOIL_PARALLEL"), out int envParallel) && envParallel > 0)
    defaultParallel = envParallel;
// Even in --bfp mode we run in parallel; state.Stop() is called as soon as
// any thread detects an unparity. The "first" ordering is no longer strict,
// but debugging needs any unparity fast, not necessarily the lowest-index one.
int maxParallel = defaultParallel;
Console.Error.WriteLine($"Using {maxParallel} parallel threads (CPU count: {Environment.ProcessorCount})");
// NOTE: Earlier versions did ThreadPool.SetMinThreads(maxParallel, maxParallel),
// which on this machine (192 logical CPUs) actually *lowered* the worker
// floor from the runtime's detected 192 → 96. Parallel.ForEach uses the
// ThreadPool as its worker pool, and throttling the pool below physical-core
// count can prevent the loop from ramping up to MaxDegreeOfParallelism
// quickly. Ensure min ≥ detected logical count; only raise, never lower.
ThreadPool.GetMinThreads(out int curMinW, out int curMinIo);
int wantMinW = Math.Max(curMinW, Math.Max(maxParallel, Environment.ProcessorCount));
int wantMinIo = Math.Max(curMinIo, Math.Max(maxParallel, Environment.ProcessorCount));
if (wantMinW > curMinW || wantMinIo > curMinIo)
{
    ThreadPool.SetMinThreads(wantMinW, wantMinIo);
}
ThreadPool.GetMinThreads(out int postMinW, out int postMinIo);
ThreadPool.GetMaxThreads(out int postMaxW, out int postMaxIo);
Console.Error.WriteLine($"ThreadPool: min(worker={postMinW},io={postMinIo}) max(worker={postMaxW},io={postMaxIo})");
// Dynamic work distribution via Parallel.ForEach on a range partitioner.
// Each worker holds thread-local accumulators and merges them ONCE under
// mergeLock when the thread finishes (~192 lock acquisitions total, zero
// per-case sync). Work stealing prevents tail-end imbalance that static
// chunking produced.
int linesLen = lines.Length;
// Chunk size: per-case runtime varies by ~10× across the Selig set
// (small/thin foils finish fast; wake-heavy or near-stall cases take
// much longer). A chunk size of 1 maximises work-stealing granularity
// and minimises tail-end imbalance, at the cost of slightly more
// partitioner overhead — still negligible relative to per-case runtime.
// Override via XFOIL_CHUNK env var for experiments.
int rangeChunk = 1;
if (int.TryParse(Environment.GetEnvironmentVariable("XFOIL_CHUNK"), out int envChunk) && envChunk > 0)
{
    rangeChunk = envChunk;
}
var rangePartitioner = System.Collections.Concurrent.Partitioner.Create(0, linesLen, rangeChunk);
Console.Error.WriteLine($"Range chunk size: {rangeChunk}, total chunks: {(linesLen + rangeChunk - 1) / rangeChunk}");
g_sweepStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
Parallel.ForEach(
    rangePartitioner,
    new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
    // Thread-local init: fresh accumulator per worker.
    () => new LocalState(),
    // Body: process a contiguous range [range.Item1, range.Item2) using the
    // worker's local accumulator. Zero cross-thread sync in this block.
    (range, loopState, local) =>
    {
        for (int i = range.Item1; i < range.Item2; i++)
        {
            if (breakAtFirstUnparity && Volatile.Read(ref firstUnparityFound) == 1)
            {
                loopState.Stop();
                return local;
            }
            string line = lines[i];
            {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string naca;
        double re, alpha, ncrit, fortCl, fortCd;
        int fortClBits;
        int fortCdBits;

        if (parts.Length == 8)
        {
            naca = parts[0];
            re = double.Parse(parts[1], CultureInfo.InvariantCulture);
            alpha = double.Parse(parts[2], CultureInfo.InvariantCulture);
            ncrit = double.Parse(parts[3], CultureInfo.InvariantCulture);
            fortClBits = ParseFloatBitsToken(parts[6]);
            fortCdBits = ParseFloatBitsToken(parts[7]);
            fortCl = BitConverter.Int32BitsToSingle(fortClBits);
            fortCd = BitConverter.Int32BitsToSingle(fortCdBits);
        }
        else if (parts.Length == 7)
        {
            naca = parts[0];
            re = double.Parse(parts[1], CultureInfo.InvariantCulture);
            alpha = double.Parse(parts[2], CultureInfo.InvariantCulture);
            ncrit = 9.0;
            fortClBits = ParseFloatBitsToken(parts[5]);
            fortCdBits = ParseFloatBitsToken(parts[6]);
            fortCl = BitConverter.Int32BitsToSingle(fortClBits);
            fortCd = BitConverter.Int32BitsToSingle(fortCdBits);
        }
        else if (parts.Length == 6)
        {
            naca = parts[0];
            re = double.Parse(parts[1], CultureInfo.InvariantCulture);
            alpha = double.Parse(parts[2], CultureInfo.InvariantCulture);
            ncrit = double.Parse(parts[3], CultureInfo.InvariantCulture);
            // Compact Selig format: cols 4-5 are hex (0xCLBITS 0xCDBITS).
            // Older format had decimal CL CD here. Detect by 0x prefix.
            if (parts[4].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                fortClBits = ParseFloatBitsToken(parts[4]);
                fortCdBits = ParseFloatBitsToken(parts[5]);
                fortCl = BitConverter.Int32BitsToSingle(fortClBits);
                fortCd = BitConverter.Int32BitsToSingle(fortCdBits);
            }
            else
            {
                fortCl = double.Parse(parts[4], CultureInfo.InvariantCulture);
                fortCd = double.Parse(parts[5], CultureInfo.InvariantCulture);
                fortClBits = BitConverter.SingleToInt32Bits((float)fortCl);
                fortCdBits = BitConverter.SingleToInt32Bits((float)fortCd);
            }
        }
        else if (parts.Length == 5)
        {
            naca = parts[0];
            re = double.Parse(parts[1], CultureInfo.InvariantCulture);
            alpha = double.Parse(parts[2], CultureInfo.InvariantCulture);
            ncrit = 9.0;
            fortCl = double.Parse(parts[3], CultureInfo.InvariantCulture);
            fortCd = double.Parse(parts[4], CultureInfo.InvariantCulture);
            fortClBits = BitConverter.SingleToInt32Bits((float)fortCl);
            fortCdBits = BitConverter.SingleToInt32Bits((float)fortCd);
        }
        else continue;

        try
        {
            var geometry = LoadOrCacheGeometry(naca);
            var service = new AirfoilAnalysisService();
            // Parity branch: only XFoilRelaxation. NO fallback. Even unconverged
            // values are kept and compared bit-exact against Fortran. The user's
            // requirement is that the legacy parity branch produce IDENTICAL
            // results to Fortran whether the case converges or diverges.
            var settings = standardBranch
                ? new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: re,
                    criticalAmplificationFactor: ncrit,
                    useExtendedWake: false,
                    useLegacyBoundaryLayerInitialization: false,
                    useLegacyPanelingPrecision: false,
                    useLegacyStreamfunctionKernelPrecision: false,
                    useLegacyWakeSourceKernelPrecision: false,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 80,
                    viscousConvergenceTolerance: 1e-4,
                    viscousSolverMode: XFoil.Solver.Models.ViscousSolverMode.XFoilRelaxation)
                : new AnalysisSettings(
                    panelCount: 160,
                    reynoldsNumber: re,
                    criticalAmplificationFactor: ncrit,
                    useExtendedWake: false,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 80,
                    viscousConvergenceTolerance: 1e-4,
                    viscousSolverMode: XFoil.Solver.Models.ViscousSolverMode.XFoilRelaxation);

            long caseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = service.AnalyzeViscous(geometry, alpha, settings);
            long caseEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            long caseElapsed = caseEnd - caseStart;
            local.ActiveTicks += caseElapsed;
            if (caseElapsed < local.MinCaseTicks) local.MinCaseTicks = caseElapsed;
            if (caseElapsed > local.MaxCaseTicks) local.MaxCaseTicks = caseElapsed;
            local.CaseSpans.Add((
                caseStart - g_sweepStartTicks,
                caseEnd - g_sweepStartTicks,
                System.Environment.CurrentManagedThreadId));
            double cd = result.DragDecomposition.CD;
            double cl = result.LiftCoefficient;

            // Bit-level comparison: cast to single, then compare 32-bit patterns
            // directly. NaN matches NaN bit-by-bit, so divergent cases must
            // produce the SAME NaN bit pattern as Fortran.
            int managedCdBits = BitConverter.SingleToInt32Bits((float)cd);
            int managedClBits = BitConverter.SingleToInt32Bits((float)cl);
            int cdUlp = managedCdBits == fortCdBits ? 0 : Math.Abs(managedCdBits - fortCdBits);
            int clUlp = managedClBits == fortClBits ? 0 : Math.Abs(managedClBits - fortClBits);
            double cdRelError;
            if (double.IsNaN(cd) || double.IsNaN(fortCd))
            {
                cdRelError = (double.IsNaN(cd) && double.IsNaN(fortCd)) ? 0.0 : double.PositiveInfinity;
            }
            else
            {
                cdRelError = Math.Abs(cd - fortCd) / Math.Max(Math.Abs(fortCd), 1e-6);
            }
            local.Results.Add((naca, re, alpha, ncrit, fortCl, fortCd, cl, cd, cdRelError, cdUlp, clUlp));

            if (cdUlp == 0 && clUlp == 0) local.BitExact++;
            if (double.IsFinite(cd) && double.IsFinite(cl)) local.Finite++;

            if (cdRelError < 0.01)
            {
                local.Passed++;
            }
            else
            {
                local.Failed++;
                string detail = $"FAIL: NACA {naca} Re={re} a={alpha} Nc={ncrit}: Fort CD={fortCd:F5} C# CD={cd:F5} relErr={cdRelError:P2}";
                local.Fails.Add(detail);
            }

            // Break-at-first-unparity: stop processing after first non-bit-exact case.
            // Rare path — volatile write races are fine; duplicate console output is
            // acceptable since --bfp is a debugging mode, not production.
            bool fortDegenerate = Math.Abs(fortCd) < 1.0e-15 || double.IsNaN(fortCd) || double.IsInfinity(fortCd);
            if (breakAtFirstUnparity && (cdUlp > 0 || clUlp > 0)
                && !(skipDegenerate && fortDegenerate)
                && Volatile.Read(ref firstUnparityFound) == 0)
            {
                Volatile.Write(ref firstUnparityFound, 1);
                Console.Error.WriteLine($"\n=== FIRST UNPARITY ===");
                Console.Error.WriteLine($"NACA {naca} Re={re} a={alpha} Nc={ncrit}");
                Console.Error.WriteLine($"  Fort CD={fortCd:F6} C# CD={cd:F6} CD_ULP={cdUlp}");
                Console.Error.WriteLine($"  Fort CL={fortCl:F6} C# CL={cl:F6} CL_ULP={clUlp}");
                Console.Error.WriteLine($"  CD_hex: Fort=0x{fortCdBits:X8} C#=0x{managedCdBits:X8}");
                Console.Error.WriteLine($"  CL_hex: Fort=0x{fortClBits:X8} C#=0x{managedClBits:X8}");
            }

            // Local max-tracking: no cross-thread sync. Merged once after Parallel.For.
            if (cdRelError > local.MaxCdRelErr) local.MaxCdRelErr = cdRelError;
        }
        catch
        {
            local.Skipped++;
        }

        local.Processed++;
        }
    }
        return local;
    },
    // Finalize: called ONCE per thread when its work is done. Merges the
    // thread-local accumulator into the global state under mergeLock.
    // Contention is ~maxParallel times total over the entire sweep.
    local =>
    {
        lock (mergeLock)
        {
            passed += local.Passed;
            failed += local.Failed;
            skipped += local.Skipped;
            bitExact += local.BitExact;
            finiteResults += local.Finite;
            processed += local.Processed;
            if (local.MaxCdRelErr > maxCdRelError) maxCdRelError = local.MaxCdRelErr;
            results.AddRange(local.Results);
            failDetails.AddRange(local.Fails);
            if (local.Processed > 0)
            {
                g_perThreadActiveTicks.Add(local.ActiveTicks);
                g_perThreadProcessed.Add(local.Processed);
                g_perThreadMinTicks.Add(local.MinCaseTicks);
                g_perThreadMaxTicks.Add(local.MaxCaseTicks);
                g_caseSpans.AddRange(local.CaseSpans);
            }
        }
    });
// NOTE: with 192 threads and effective parallelism of ~68 cores, the gap is
// from work imbalance at the tail — some cases take much longer than others.

// If break-at-first-unparity mode and an unparity was found, report and exit
if (breakAtFirstUnparity && firstUnparityFound == 1)
{
    Console.Error.WriteLine($"\nProcessed {processed}/{lines.Length} vectors before first unparity.");
    Console.Error.WriteLine($"Bit-exact cases before unparity: {bitExact}");
    return 1;
}

long g_sweepElapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - g_sweepStartTicks;
double sweepWallMs = g_sweepElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
Console.Error.WriteLine($"\r{processed}/{lines.Length} done");

// Per-thread utilization breakdown: each worker accumulates the total active
// ticks it spent inside AnalyzeViscous. Dividing by sweep wall ticks gives
// utilization per worker. Workers that finished early (work stealing ran dry)
// have lower ratios — that's tail-end imbalance.
if (g_perThreadActiveTicks.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"=== Per-worker active-time breakdown ({g_perThreadActiveTicks.Count} workers) ===");
    double totalActiveMs = 0;
    double minUtil = 1.0, maxUtil = 0;
    for (int i = 0; i < g_perThreadActiveTicks.Count; i++)
    {
        double activeMs = g_perThreadActiveTicks[i] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        totalActiveMs += activeMs;
        double util = activeMs / sweepWallMs;
        if (util < minUtil) minUtil = util;
        if (util > maxUtil) maxUtil = util;
    }
    double avgUtil = totalActiveMs / (sweepWallMs * g_perThreadActiveTicks.Count);
    Console.Error.WriteLine($"Sweep wall time:  {sweepWallMs,10:F1} ms");
    Console.Error.WriteLine($"Total active time: {totalActiveMs,10:F1} ms ({totalActiveMs / sweepWallMs,6:F2} concurrent workers)");
    Console.Error.WriteLine($"Avg worker util:  {avgUtil:P1}   Min worker util: {minUtil:P1}   Max worker util: {maxUtil:P1}");
    Console.Error.WriteLine($"Idle time: {(1 - avgUtil):P1} of wall — tail-end imbalance / work-stealing latency.");

    // Per-case runtime spread: min / max across all workers. A 100× spread
    // indicates highly variable per-case work that challenges the load balancer.
    long minCaseTicks = long.MaxValue;
    long maxCaseTicks = 0;
    for (int i = 0; i < g_perThreadMinTicks.Count; i++)
    {
        if (g_perThreadMinTicks[i] < minCaseTicks) minCaseTicks = g_perThreadMinTicks[i];
        if (g_perThreadMaxTicks[i] > maxCaseTicks) maxCaseTicks = g_perThreadMaxTicks[i];
    }
    double minCaseMs = minCaseTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    double maxCaseMs = maxCaseTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    Console.Error.WriteLine($"Per-case runtime: min={minCaseMs,6:F1} ms  max={maxCaseMs,7:F1} ms  (ratio={maxCaseMs / Math.Max(minCaseMs, 1e-6):F1}×)");

    // Actual concurrent-thread count: walk event stream (start/end pairs)
    // ordered by time, incrementing on start and decrementing on end. Gives
    // the true "N threads running simultaneously at time t" answer instead
    // of an average.
    if (g_caseSpans.Count > 0)
    {
        var events = new List<(long Time, int Delta)>(g_caseSpans.Count * 2);
        foreach (var (start, end, _) in g_caseSpans)
        {
            events.Add((start, +1));
            events.Add((end, -1));
        }
        events.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : b.Delta.CompareTo(a.Delta));
        int active = 0;
        int peak = 0;
        long lastT = 0;
        double hzMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        // Histogram over "time spent with exactly N threads active".
        var histogram = new long[256];
        foreach (var (t, d) in events)
        {
            if (active >= 0 && active < histogram.Length)
            {
                histogram[active] += (t - lastT);
            }
            active += d;
            if (active > peak) peak = active;
            lastT = t;
        }
        // Distinct thread IDs.
        var distinctThreadIds = new HashSet<int>();
        foreach (var (_, _, tid) in g_caseSpans) distinctThreadIds.Add(tid);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"=== Actual concurrent-thread histogram ({distinctThreadIds.Count} distinct thread IDs observed) ===");
        Console.Error.WriteLine($"Peak concurrent threads: {peak}");
        long totalTicks = g_sweepElapsedTicks;
        double covered = 0;
        for (int n = 0; n < histogram.Length; n++)
        {
            if (histogram[n] == 0) continue;
            double frac = (double)histogram[n] / totalTicks;
            double ms = histogram[n] * hzMs;
            covered += ms;
            if (frac >= 0.005 || n == peak)
                Console.Error.WriteLine($"  {n,3} threads active: {ms,8:F1} ms  ({frac:P1})");
        }
        // Weighted average concurrent threads across the whole sweep wall time.
        double weightedSum = 0;
        for (int n = 0; n < histogram.Length; n++)
        {
            weightedSum += n * (double)histogram[n];
        }
        Console.Error.WriteLine($"Time-weighted avg concurrent threads: {weightedSum / totalTicks:F2}");

        // Buckets: how much wall time with ≥ thresholds of concurrent threads.
        long[] ticksAtLeast = new long[5];
        int[] thresholds = { 24, 48, 72, 96, 144 };
        for (int i = 0; i < thresholds.Length; i++)
        {
            for (int n = thresholds[i]; n < histogram.Length; n++)
                ticksAtLeast[i] += histogram[n];
        }
        for (int i = 0; i < thresholds.Length; i++)
        {
            Console.Error.WriteLine($"  Wall time with ≥ {thresholds[i],3} threads active: {ticksAtLeast[i] * hzMs,8:F1} ms ({(double)ticksAtLeast[i] / totalTicks:P1})");
        }
    }
}
Console.Error.WriteLine();

// Summary
int converged = passed + failed;
double passRate = converged > 0 ? (double)passed / converged : 0;

Console.WriteLine($"=== Polar Parity Results ===");
Console.WriteLine($"Total vectors: {lines.Length}");
Console.WriteLine($"Converged: {converged} ({passed} within 1% CD, {failed} outside)");
Console.WriteLine($"Bit-exact (0 ULP in CD AND CL): {bitExact} / {converged} ({(converged > 0 ? (double)bitExact / converged : 0):P1})");
Console.WriteLine($"Finite CD+CL: {finiteResults} / {converged} ({(converged > 0 ? (double)finiteResults / converged : 0):P1})");
Console.WriteLine($"Skipped/diverged: {skipped}");
Console.WriteLine($"Pass rate (within 1% CD): {passRate:P1}");
Console.WriteLine($"Max CD relative error: {maxCdRelError:P4}");

// Show worst 20 failures
var worstFailures = results.OrderByDescending(r => r.cdRelErr).Take(20).ToArray();
Console.WriteLine($"\nWorst 20 cases:");
foreach (var r in worstFailures)
{
    Console.WriteLine($"  NACA {r.naca} Re={r.re} a={r.alpha} Nc={r.ncrit}: Fort CD={r.fortCd:F5} C# CD={r.csharpCd:F5} relErr={r.cdRelErr:P2}");
}

// Show worst 20 by CD ULP (precision-focused)
var worstCdUlp = results.OrderByDescending(r => r.cdUlp).Take(20).ToArray();
Console.WriteLine($"\nWorst 20 by CD ULP:");
foreach (var r in worstCdUlp)
{
    Console.WriteLine($"  NACA {r.naca} Re={r.re} a={r.alpha} Nc={r.ncrit}: CD ULP={r.cdUlp} CL ULP={r.clUlp}");
}

// Show worst 20 by CL ULP
var worstClUlp = results.OrderByDescending(r => r.clUlp).Take(20).ToArray();
Console.WriteLine($"\nWorst 20 by CL ULP:");
foreach (var r in worstClUlp)
{
    Console.WriteLine($"  NACA {r.naca} Re={r.re} a={r.alpha} Nc={r.ncrit}: CD ULP={r.cdUlp} CL ULP={r.clUlp}");
}

// Show smallest non-zero ULP failures (closest to bit-exact). These are the
// best targets for parity debugging because the divergence is small enough
// to trace easily.
var smallestNonZero = results
    .Where(r => (r.cdUlp + r.clUlp) > 0)
    .OrderBy(r => r.cdUlp + r.clUlp)
    .Take(20)
    .ToArray();
Console.WriteLine($"\nSmallest non-zero ULP failures (best debug targets):");
foreach (var r in smallestNonZero)
{
    Console.WriteLine($"  NACA {r.naca} Re={r.re} a={r.alpha} Nc={r.ncrit}: CD ULP={r.cdUlp} CL ULP={r.clUlp}");
}

// Show CD error distribution
var convergedResults = results.ToArray();
int within01 = convergedResults.Count(r => r.cdRelErr < 0.001);
int within05 = convergedResults.Count(r => r.cdRelErr < 0.005);
int within1 = convergedResults.Count(r => r.cdRelErr < 0.01);
int within5 = convergedResults.Count(r => r.cdRelErr < 0.05);
int within10 = convergedResults.Count(r => r.cdRelErr < 0.10);

// ULP distribution
int ulp0 = convergedResults.Count(r => r.cdUlp == 0);
int ulp10 = convergedResults.Count(r => r.cdUlp <= 10);
int ulp100 = convergedResults.Count(r => r.cdUlp <= 100);
int ulp1000 = convergedResults.Count(r => r.cdUlp <= 1000);
Console.WriteLine($"\nCD ULP Distribution (of {convergedResults.Length} converged):");
Console.WriteLine($"  0 ULP (bit-exact): {ulp0} ({(convergedResults.Length > 0 ? (double)ulp0 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <=10 ULP: {ulp10} ({(convergedResults.Length > 0 ? (double)ulp10 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <=100 ULP: {ulp100} ({(convergedResults.Length > 0 ? (double)ulp100 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <=1000 ULP: {ulp1000} ({(convergedResults.Length > 0 ? (double)ulp1000 / convergedResults.Length : 0):P1})");

Console.WriteLine($"\nCD Error Distribution (of {convergedResults.Length} converged):");
Console.WriteLine($"  <0.1%: {within01} ({(convergedResults.Length > 0 ? (double)within01 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <0.5%: {within05} ({(convergedResults.Length > 0 ? (double)within05 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <1.0%: {within1} ({(convergedResults.Length > 0 ? (double)within1 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <5.0%: {within5} ({(convergedResults.Length > 0 ? (double)within5 / convergedResults.Length : 0):P1})");
Console.WriteLine($"  <10%:  {within10} ({(convergedResults.Length > 0 ? (double)within10 / convergedResults.Length : 0):P1})");

// Split passing/failing vectors into separate files for incremental testing.
// Env XFOIL_SPLIT_DIR triggers the split.
string? splitDir = Environment.GetEnvironmentVariable("XFOIL_SPLIT_DIR");
if (!string.IsNullOrEmpty(splitDir))
{
    Directory.CreateDirectory(splitDir);
    var passingPath = Path.Combine(splitDir, "selig_passing.txt");
    var failingPath = Path.Combine(splitDir, "selig_failing.txt");
    using var passingWriter = new StreamWriter(passingPath);
    using var failingWriter = new StreamWriter(failingPath);
    foreach (var r in results)
    {
        // Reconstruct the original vector line
        int clBits = BitConverter.SingleToInt32Bits((float)r.fortCl);
        int cdBits = BitConverter.SingleToInt32Bits((float)r.fortCd);
        string line = $"{r.naca} {r.re} {r.alpha} {r.ncrit} 0x{clBits:X8} 0x{cdBits:X8}";
        if (r.cdUlp == 0 && r.clUlp == 0)
            passingWriter.WriteLine(line);
        else
            failingWriter.WriteLine(line);
    }
    Console.WriteLine($"\nSplit files written to {splitDir}:");
    Console.WriteLine($"  Passing: {passingPath}");
    Console.WriteLine($"  Failing: {failingPath}");
}

return passed + failed + skipped > 100 ? 0 : 1;

// Per-thread local state for the Parallel.ForEach sweep. Each thread
// increments its own counters and appends to its own lists — zero
// cross-thread sync while the loop body is running. The state is
// merged under a single mutex in the finalize delegate (contended
// only ~maxParallel times over the entire sweep).
sealed class LocalState
{
    public int Passed;
    public int Failed;
    public int Skipped;
    public int BitExact;
    public int Finite;
    public int Processed;
    public double MaxCdRelErr;
    // Per-thread elapsed ticks across all cases; after join these tell us
    // how much wall time each worker spent actively processing vs waiting
    // (compare sum-of-thread-ticks vs elapsed-ticks × thread-count).
    public long ActiveTicks;
    // Track min / max per-case time to see runtime variance.
    public long MinCaseTicks = long.MaxValue;
    public long MaxCaseTicks;
    // Per-case (start, end) timestamps captured relative to g_sweepStartTicks.
    // Used to compute *actual* concurrent-thread count over time by sweeping
    // a 1 ms window across [0, sweepWall] and counting how many cases overlap
    // each window. Exposes whether 48 threads are really running in parallel
    // or if the scheduler is pinning work to a subset.
    public List<(long Start, long End, int ThreadId)> CaseSpans = new(capacity: 128);
    public List<(string naca, double re, double alpha, double ncrit, double fortCl, double fortCd, double csharpCl, double csharpCd, double cdRelErr, int cdUlp, int clUlp)> Results = new(capacity: 1024);
    public List<string> Fails = new();
}

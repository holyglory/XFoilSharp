using System.Collections.Concurrent;
using System.Globalization;
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
        inviscidSolverType: InviscidSolverType.LinearVortex,
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
        XFoil.Solver.Services.CosineClusteringPanelDistributor.Distribute(
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

// Aggregate counters — merged AFTER Parallel.For from per-worker slot arrays.
// No shared counters inside the parallel loop.
int passed = 0, failed = 0, skipped = 0, bitExact = 0, finiteResults = 0, processed = 0;
double maxCdRelError = 0;
// Fails list: sized to max workers; each worker writes its own fails into
// its dedicated slot. Merged single-threaded after Parallel.For.
int maxWorkers = Environment.ProcessorCount;
var perWorkerFails = new List<string>[maxWorkers];
var perWorkerPassed = new int[maxWorkers];
var perWorkerFailed = new int[maxWorkers];
var perWorkerSkipped = new int[maxWorkers];
var perWorkerBitExact = new int[maxWorkers];
var perWorkerFinite = new int[maxWorkers];
var perWorkerProcessed = new int[maxWorkers];
var perWorkerMaxCdRelErr = new double[maxWorkers];
var perWorkerResults = new List<(string naca, double re, double alpha, double ncrit, double fortCl, double fortCd, double csharpCl, double csharpCd, double cdRelErr, int cdUlp, int clUlp)>[maxWorkers];
var results = new List<(string naca, double re, double alpha, double ncrit, double fortCl, double fortCd, double csharpCl, double csharpCd, double cdRelErr, int cdUlp, int clUlp)>();
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

// Allow override via XFOIL_PARALLEL env var; default to ProcessorCount (avoid oversubscription)
int defaultParallel = Environment.ProcessorCount;
if (int.TryParse(Environment.GetEnvironmentVariable("XFOIL_PARALLEL"), out int envParallel) && envParallel > 0)
    defaultParallel = envParallel;
// Even in --bfp mode we run in parallel; state.Stop() is called as soon as
// any thread detects an unparity. The "first" ordering is no longer strict,
// but debugging needs any unparity fast, not necessarily the lowest-index one.
int maxParallel = defaultParallel;
Console.Error.WriteLine($"Using {maxParallel} parallel threads (CPU count: {Environment.ProcessorCount})");
ThreadPool.SetMinThreads(maxParallel, maxParallel);
ThreadPool.SetMaxThreads(maxParallel * 2, maxParallel * 2);
// Static range chunking: each worker processes a contiguous slice of the
// input. Zero cross-thread sync on the hot path (no Interlocked, no shared
// counter, no lock). Load balance is uniform to within one case because
// per-case runtime is roughly stable (~5-50 ms).
int linesLen = lines.Length;
int chunkSize = (linesLen + maxParallel - 1) / maxParallel;
Parallel.For(0, maxParallel, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, workerId =>
{
    int startIdx = workerId * chunkSize;
    int endIdx = Math.Min(startIdx + chunkSize, linesLen);
    if (startIdx >= linesLen) return;

    // Per-worker local accumulators. All writes during the sweep land in local
    // stack variables or the worker's own array slot — zero cross-thread sync.
    int localPassed = 0, localFailed = 0, localSkipped = 0, localBitExact = 0, localFinite = 0;
    int localProcessed = 0;
    double localMaxCdRelErr = 0.0;
    var localResults = new List<(string naca, double re, double alpha, double ncrit, double fortCl, double fortCd, double csharpCl, double csharpCd, double cdRelErr, int cdUlp, int clUlp)>(capacity: endIdx - startIdx);
    var localFails = new List<string>();
    perWorkerResults[workerId] = localResults;
    perWorkerFails[workerId] = localFails;

    try
    {
    for (int i = startIdx; i < endIdx; i++)
    {
        if (breakAtFirstUnparity && Volatile.Read(ref firstUnparityFound) == 1) return;
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
        else return;

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
                    inviscidSolverType: InviscidSolverType.LinearVortex,
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
                    inviscidSolverType: InviscidSolverType.LinearVortex,
                    useExtendedWake: false,
                    useLegacyBoundaryLayerInitialization: true,
                    useLegacyPanelingPrecision: true,
                    useLegacyStreamfunctionKernelPrecision: true,
                    useLegacyWakeSourceKernelPrecision: true,
                    useModernTransitionCorrections: false,
                    maxViscousIterations: 80,
                    viscousConvergenceTolerance: 1e-4,
                    viscousSolverMode: XFoil.Solver.Models.ViscousSolverMode.XFoilRelaxation);

            var result = service.AnalyzeViscous(geometry, alpha, settings);
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
            localResults.Add((naca, re, alpha, ncrit, fortCl, fortCd, cl, cd, cdRelError, cdUlp, clUlp));

            if (cdUlp == 0 && clUlp == 0) localBitExact++;
            if (double.IsFinite(cd) && double.IsFinite(cl)) localFinite++;

            if (cdRelError < 0.01)
            {
                localPassed++;
            }
            else
            {
                localFailed++;
                string detail = $"FAIL: NACA {naca} Re={re} a={alpha} Nc={ncrit}: Fort CD={fortCd:F5} C# CD={cd:F5} relErr={cdRelError:P2}";
                localFails.Add(detail);
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
            if (cdRelError > localMaxCdRelErr) localMaxCdRelErr = cdRelError;
        }
        catch
        {
            localSkipped++;
        }

        localProcessed++;
        }
    }
    }
    finally
    {
        // Write per-worker results into this worker's dedicated slots. Each slot
        // is written by exactly one thread, so no cross-thread sync is needed —
        // the main thread reads all slots after Parallel.For completes.
        perWorkerPassed[workerId] = localPassed;
        perWorkerFailed[workerId] = localFailed;
        perWorkerSkipped[workerId] = localSkipped;
        perWorkerBitExact[workerId] = localBitExact;
        perWorkerFinite[workerId] = localFinite;
        perWorkerProcessed[workerId] = localProcessed;
        perWorkerMaxCdRelErr[workerId] = localMaxCdRelErr;
    }
});

// Single-threaded aggregation after Parallel.For completes. No sync.
var failDetails = new List<string>();
for (int w = 0; w < maxWorkers; w++)
{
    passed += perWorkerPassed[w];
    failed += perWorkerFailed[w];
    skipped += perWorkerSkipped[w];
    bitExact += perWorkerBitExact[w];
    finiteResults += perWorkerFinite[w];
    processed += perWorkerProcessed[w];
    if (perWorkerMaxCdRelErr[w] > maxCdRelError) maxCdRelError = perWorkerMaxCdRelErr[w];
    if (perWorkerResults[w] is not null) results.AddRange(perWorkerResults[w]!);
    if (perWorkerFails[w] is not null) failDetails.AddRange(perWorkerFails[w]!);
}
// NOTE: with 192 threads and effective parallelism of ~68 cores, the gap is
// from work imbalance at the tail — some cases take much longer than others.

// If break-at-first-unparity mode and an unparity was found, report and exit
if (breakAtFirstUnparity && firstUnparityFound == 1)
{
    Console.Error.WriteLine($"\nProcessed {processed}/{lines.Length} vectors before first unparity.");
    Console.Error.WriteLine($"Bit-exact cases before unparity: {bitExact}");
    return 1;
}

Console.Error.WriteLine($"\r{processed}/{lines.Length} done");
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

// Per-case allocation profiler for the viscous solver.
//
// Measures the per-thread heap allocation count for a single AnalyzeViscous
// invocation, after the ThreadStatic pools and JIT caches are warm. Used to
// guide "remove every remaining heap allocation from the hot path" work.

using System.Globalization;
using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

static AnalysisSettings MakeSettings(double re, double ncrit)
    => new AnalysisSettings(
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

Environment.SetEnvironmentVariable("XFOIL_DISABLE_FMA", "1");

string datPath = args.Length > 0 ? args[0] : "tools/selig-database/naca0012.dat";
double re = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 1_000_000;
double alpha = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4;
double ncrit = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 9;
int iterations = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 20;
int warmup = 5;

var parser = new AirfoilParser();
var geom = parser.ParseFile(datPath);
var service = new AirfoilAnalysisService();
var settings = MakeSettings(re, ncrit);

// Warmup — populate all ThreadStatic pools and JIT the hot code.
for (int i = 0; i < warmup; i++)
{
    _ = service.AnalyzeViscous(geom, alpha, settings);
}

// Force full GC before measurement so we're not mixing the pool-warmup
// allocations into the steady-state number.
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

Console.WriteLine($"=== Per-case allocations on {datPath} Re={re} a={alpha} nCrit={ncrit} ===");
Console.WriteLine($"{"iter",5} {"bytes",12} {"Δbytes",12} {"cd",10} {"cl",10}");
long prevTotal = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < iterations; i++)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = service.AnalyzeViscous(geom, alpha, settings);
    long after = GC.GetAllocatedBytesForCurrentThread();
    Console.WriteLine($"{i,5} {after,12:N0} {after - before,12:N0} {res.DragDecomposition.CD,10:F6} {res.LiftCoefficient,10:F6}");
}
long finalTotal = GC.GetAllocatedBytesForCurrentThread();
double avgBytes = (finalTotal - prevTotal) / (double)iterations;
Console.WriteLine();
Console.WriteLine($"Steady-state avg allocations/case: {avgBytes:N0} bytes");
Console.WriteLine($"Gen0: {GC.CollectionCount(0)} Gen1: {GC.CollectionCount(1)} Gen2: {GC.CollectionCount(2)}");

// -----------------------------------------------------------------
// Phase-level breakdown. Repeatedly call specific sub-phases so we
// can attribute the per-case budget to each. These numbers are upper
// bounds — some phases overlap but the relative scale is informative.
// -----------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Phase breakdown (per call of each phase) ===");

// ExtractCoordinates cost
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long b = GC.GetAllocatedBytesForCurrentThread();
    var pointsCount = geom.Points.Count;
    var inputX = new double[pointsCount];
    var inputY = new double[pointsCount];
    for (int i = 0; i < pointsCount; i++) { inputX[i] = geom.Points[i].X; inputY[i] = geom.Points[i].Y; }
    long a = GC.GetAllocatedBytesForCurrentThread();
    Console.WriteLine($"  ExtractCoordinates: {a - b,10:N0} bytes (once per case)");
}

// Settings ctor cost
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long b = GC.GetAllocatedBytesForCurrentThread();
    var s = MakeSettings(re, ncrit);
    long a = GC.GetAllocatedBytesForCurrentThread();
    Console.WriteLine($"  AnalysisSettings ctor: {a - b,10:N0} bytes");
}

// Full AnalyzeViscous trial count of bytes
{
    long deltas = 0; int n = 20;
    for (int i = 0; i < n; i++)
    {
        long b = GC.GetAllocatedBytesForCurrentThread();
        _ = service.AnalyzeViscous(geom, alpha, settings);
        long a = GC.GetAllocatedBytesForCurrentThread();
        deltas += (a - b);
    }
    Console.WriteLine($"  AnalyzeViscous full: {deltas / n,10:N0} bytes/call (avg of {n})");
}

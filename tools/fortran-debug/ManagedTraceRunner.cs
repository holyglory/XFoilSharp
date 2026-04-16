using System;
using System.Globalization;
using System.IO;
using System.Text;
using XFoil.Core.Diagnostics;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

if (args.Length < 7)
{
    Console.Error.WriteLine("usage: ManagedTraceRunner <tracePath> <dumpPath> <naca> <re> <alphaDeg> <panels> <ncrit> [maxIter]");
    return 2;
}

string tracePath = Path.GetFullPath(args[0]);
string dumpPath = Path.GetFullPath(args[1]);
string naca = args[2];
double reynolds = double.Parse(args[3], CultureInfo.InvariantCulture);
double alphaDegrees = double.Parse(args[4], CultureInfo.InvariantCulture);
int panels = int.Parse(args[5], CultureInfo.InvariantCulture);
double ncrit = double.Parse(args[6], CultureInfo.InvariantCulture);
int maxIter = args.Length > 7 ? int.Parse(args[7], CultureInfo.InvariantCulture) : 80;

Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
Directory.CreateDirectory(Path.GetDirectoryName(dumpPath)!);

var airfoil = new NacaAirfoilGenerator().Generate4DigitClassic(naca, 239, useLegacyPrecision: true);
double[] x = new double[airfoil.Points.Count];
double[] y = new double[airfoil.Points.Count];
for (int i = 0; i < airfoil.Points.Count; i++)
{
    x[i] = airfoil.Points[i].X;
    y[i] = airfoil.Points[i].Y;
}
var settings = new AnalysisSettings(
    panelCount: panels,
    reynoldsNumber: reynolds,
    machNumber: 0.0,
    inviscidSolverType: InviscidSolverType.LinearVortex,
    viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
    useModernTransitionCorrections: false,
    useExtendedWake: false,
    maxViscousIterations: maxIter,
    viscousConvergenceTolerance: 1e-4,
    criticalAmplificationFactor: ncrit,
    useLegacyBoundaryLayerInitialization: true,
    useLegacyWakeSourceKernelPrecision: true,
    useLegacyStreamfunctionKernelPrecision: true,
    useLegacyPanelingPrecision: true);

double alphaRadians = alphaDegrees * Math.PI / 180.0;

using var textWriter = new StreamWriter(dumpPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
{
    caseId = $"adhoc_n{naca}_re{reynolds}_a{alphaDegrees}_p{panels}_n{ncrit}",
    caseName = "ManagedTraceRunner",
    airfoilCode = naca,
    settings.PanelCount,
    settings.ReynoldsNumber,
    alphaDegrees,
    alphaRadians
});
using var debugWriter = new MultiplexTextWriter(textWriter, traceWriter);
using var solverScope = SolverTrace.Begin(traceWriter);
using var coreScope = CoreTrace.Begin((kind, scope, data) => traceWriter.WriteEvent(kind, scope, data));

debugWriter.WriteLine($"=== CSHARP CASE START {naca} ===");
debugWriter.WriteLine(string.Format(
    CultureInfo.InvariantCulture,
    "CASE={0} AIRFOIL={1} RE={2:E8} ALFA={3:F6}",
    "adhoc",
    naca,
    reynolds,
    alphaDegrees));

var result = ViscousSolverEngine.SolveViscous(
    (x, y),
    settings,
    alphaRadians,
    debugWriter: debugWriter);

debugWriter.WriteLine($"=== CSHARP CASE END {naca} ===");
debugWriter.WriteLine(string.Format(
    CultureInfo.InvariantCulture,
    "FINAL CL={0,15:E8} CD={1,15:E8} CM={2,15:E8} CONVERGED={3} ITER={4}",
    result.LiftCoefficient,
    result.DragDecomposition.CD,
    result.MomentCoefficient,
    result.Converged,
    result.Iterations));

debugWriter.Flush();
return 0;

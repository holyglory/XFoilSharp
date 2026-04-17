namespace XFoil.Solver.Diagnostics;

public static class DebugFlags
{
    public static readonly bool DisableFma =
        Environment.GetEnvironmentVariable("XFOIL_DISABLE_FMA") == "1";
}

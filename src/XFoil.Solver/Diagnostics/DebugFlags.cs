namespace XFoil.Solver.Diagnostics;

/// <summary>
/// Cached environment-variable flags. Reading env vars is effectively a syscall
/// on Linux under parallel load; caching at JIT init removes that bottleneck.
/// </summary>
public static class DebugFlags
{
    public static readonly bool SetBlHex =
        Environment.GetEnvironmentVariable("XFOIL_SETBL_HEX") == "1";
    public static readonly bool BldifDebug =
        Environment.GetEnvironmentVariable("XFOIL_BLDIF_DEBUG") == "1";
    public static readonly bool ParityTrace =
        Environment.GetEnvironmentVariable("XFOIL_PARITY_TRACE") == "1";
    public static readonly bool DisableFma =
        Environment.GetEnvironmentVariable("XFOIL_DISABLE_FMA") == "1";
    public static readonly bool N6H20Trace =
        Environment.GetEnvironmentVariable("XFOIL_N6H20_TRACE") == "1";
    public static readonly bool N6H20CqTrace =
        Environment.GetEnvironmentVariable("XFOIL_N6H20_CQ_TRACE") == "1";
    public static readonly bool N6H20CqOnlyS2Ibl66 =
        Environment.GetEnvironmentVariable("XFOIL_N6H20_CQ_ONLY_S2IBL66") == "1";
    public static readonly bool N6H20DiTrace =
        Environment.GetEnvironmentVariable("XFOIL_N6H20_DI_TRACE") == "1";
    public static readonly bool DumpAllClStep =
        Environment.GetEnvironmentVariable("XFOIL_DUMP_ALL_CL_STEP") == "1";
    public static readonly bool DumpFullBl =
        Environment.GetEnvironmentVariable("XFOIL_DUMP_FULL_BL") == "1";
    public static readonly bool PfcmTrace =
        Environment.GetEnvironmentVariable("XFOIL_PFCM_TRACE") == "1";
    public static readonly bool PreSolveDump =
        Environment.GetEnvironmentVariable("XFOIL_PRE_SOLVE_DUMP") == "1";
    public static readonly bool StfloTrace =
        Environment.GetEnvironmentVariable("XFOIL_STFLO_TRACE") == "1";
}

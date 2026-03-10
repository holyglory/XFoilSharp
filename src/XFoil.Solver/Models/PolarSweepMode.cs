namespace XFoil.Solver.Models;

/// <summary>
/// Defines the polar sweep mode for viscous analysis.
/// Matches XFoil's Type 1/2/3 polar types.
/// </summary>
public enum PolarSweepMode
{
    /// <summary>
    /// Type 1: Sweep over angle of attack at fixed Re and Mach.
    /// </summary>
    AlphaSweep = 1,

    /// <summary>
    /// Type 2: Sweep over lift coefficient at fixed Re and Mach.
    /// </summary>
    CLSweep = 2,

    /// <summary>
    /// Type 3: Sweep over Reynolds number at fixed CL (Re varies with CL via REINF1*CL^RETYP relation).
    /// </summary>
    ReSweep = 3
}

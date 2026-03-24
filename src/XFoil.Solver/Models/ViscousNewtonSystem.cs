// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: BLSOLV
// Secondary legacy source: f_xfoil/src/xbl.f :: SETBL/UPDATE system-assembly usage
// Role in port: Managed mutable workspace for the block-tridiagonal Newton system solved by the viscous boundary-layer step.
// Differences: The data layout still mirrors the legacy solver structure, but the managed port names the arrays explicitly, uses global line indexing, and adds a compatibility constructor and properties for clearer ownership.
// Decision: Keep the managed workspace because it preserves the BLSOLV layout while making the system state auditable and reusable.
using System;

namespace XFoil.Solver.Models;

/// <summary>
/// Pre-allocated Newton system arrays for the block-tridiagonal viscous BL solver.
/// Matches the BLSOLV structure from xsolve.f.
/// Arrays are indexed by global system line IV (0..NSYS-1) rather than per-side
/// BL station index, preventing side-0/side-1 overwrite collisions.
/// </summary>
public sealed class ViscousNewtonSystem
{
    /// <summary>
    /// Creates a new Newton system workspace with pre-allocated arrays.
    /// The primary sizing parameter is <paramref name="nsysMax"/> (total global system lines),
    /// which prevents per-side ibl indexing collisions.
    /// </summary>
    /// <param name="nsysMax">Maximum total global system lines. Arrays are indexed [.., .., iv]
    /// where iv ranges 0..nsysMax-1. This replaces the old maxStations parameter.</param>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV workspace allocation.
    // Difference from legacy: The constructor allocates named managed arrays and uses global system-line indexing explicitly instead of static COMMON storage.
    // Decision: Keep the managed constructor because explicit ownership prevents side-collision bugs and improves testability.
    public ViscousNewtonSystem(int nsysMax)
    {
        MaxStations = nsysMax;

        // VA(3,2,NSYS) -- diagonal blocks indexed by global system line.
        // [eq, localBlock, iv] where iv is the global line number.
        VA = new double[3, 2, nsysMax];

        // VB(3,2,NSYS) -- sub-diagonal blocks indexed by global system line.
        VB = new double[3, 2, nsysMax];

        // VM(3,NSYS,NSYS) -- full mass-defect coupling matrix indexed by
        // [equation, coupled system line, system line].
        VM = new double[3, nsysMax, nsysMax];

        // VDEL(3,2,NSYS) -- right-hand side / Newton step deltas indexed by global system line.
        VDEL = new double[3, 2, nsysMax];

        // VZ(3,2) -- TE coupling block (couples upper and lower surfaces).
        VZ = new double[3, 2];

        // ISYS(NSYS,2) -- global system line to (ibl, side) mapping.
        // ISYS[iv, 0] = ibl, ISYS[iv, 1] = side
        ISYS = new int[nsysMax + 1, 2];

        // Total number of system lines (set during system assembly).
        NSYS = 0;
    }

    /// <summary>
    /// Backward-compatible constructor. The second argument is ignored because
    /// VM now always spans the full NSYS coupling width.
    /// </summary>
    // Legacy mapping: none; this overload is a managed compatibility shim for older callers.
    // Difference from legacy: The second argument is ignored because the managed VM array always spans full NSYS coupling width.
    // Decision: Keep the overload until callers are fully normalized because it avoids churn in existing code.
    public ViscousNewtonSystem(int nsysMax, int _)
        : this(nsysMax)
    {
    }

    /// <summary>Maximum system lines (array third dimension size).</summary>
    public int MaxStations { get; }

    /// <summary>
    /// Backward-compatible alias for the VM coupling width.
    /// </summary>
    // Legacy mapping: none; this computed property is a managed compatibility alias.
    // Difference from legacy: The wake width is derived from the allocated coupling matrix instead of being stored as a separate size scalar.
    // Decision: Keep the alias because it eases migration of older managed callers.
    public int MaxWake => VM.GetLength(1);

    /// <summary>
    /// Diagonal blocks of the block-tridiagonal system.
    /// Indexed [equation (0-2), localBlock (0-1), iv (global system line)].
    /// </summary>
    public double[,,] VA { get; }

    /// <summary>
    /// Sub-diagonal blocks.
    /// Indexed [equation (0-2), localBlock (0-1), iv (global system line)].
    /// </summary>
    public double[,,] VB { get; }

    /// <summary>
    /// Mass-defect coupling matrix.
    /// Indexed [equation (0-2), coupled system line, iv (global system line)].
    /// </summary>
    public double[,,] VM { get; }

    /// <summary>
    /// Right-hand side / Newton step solution deltas.
    /// Indexed [equation (0-2), slot (0=value, 1=reserved), iv (global system line)].
    /// </summary>
    public double[,,] VDEL { get; }

    /// <summary>
    /// TE coupling block that couples upper and lower surfaces at the trailing edge.
    /// Indexed [equation (0-2), side (0-1)].
    /// </summary>
    public double[,] VZ { get; }

    /// <summary>
    /// Global system line to (ibl, side) mapping.
    /// ISYS[iv, 0] = ibl (BL station index), ISYS[iv, 1] = side (0=upper, 1=lower).
    /// </summary>
    public int[,] ISYS { get; private set; }

    /// <summary>
    /// Total number of active system lines.
    /// </summary>
    public int NSYS { get; set; }

    /// <summary>
    /// Global system line index of the upper trailing-edge station.
    /// </summary>
    public int UpperTeLine { get; set; } = -1;

    /// <summary>
    /// Global system line index of the first wake station on the lower/wake side.
    /// </summary>
    public int FirstWakeLine { get; set; } = -1;

    /// <summary>
    /// Total surface arc-length span, used for BLSOLV acceleration thresholds.
    /// </summary>
    public double ArcLengthSpan { get; set; } = 1.0;

    /// <summary>
    /// Copies the ISYS mapping from EdgeVelocityCalculator into the system and sets NSYS.
    /// </summary>
    /// <param name="isysMapping">The ISYS mapping array from EdgeVelocityCalculator.MapStationsToSystemLines.
    /// isysMapping[lineNum, 0] = ibl, isysMapping[lineNum, 1] = side.</param>
    /// <param name="nsysCount">Total number of system lines.</param>
    // Legacy mapping: f_xfoil/src/xbl.f :: ISYS line-map initialization before BLSOLV.
    // Difference from legacy: The mapping copy and NSYS assignment are centralized in one helper instead of being spread across assembly code.
    // Decision: Keep the helper because it makes the line-mapping contract explicit.
    public void SetupISYS(int[,] isysMapping, int nsysCount)
    {
        NSYS = nsysCount;
        int rows = Math.Min(isysMapping.GetLength(0), ISYS.GetLength(0));
        for (int i = 0; i < rows; i++)
        {
            ISYS[i, 0] = isysMapping[i, 0];
            ISYS[i, 1] = isysMapping[i, 1];
        }
    }
}

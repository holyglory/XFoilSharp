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
    /// <param name="maxWake">Maximum wake stations (IZX) for VM second dimension.</param>
    public ViscousNewtonSystem(int nsysMax, int maxWake)
    {
        MaxStations = nsysMax;
        MaxWake = maxWake;

        // VA(3,2,NSYS) -- diagonal blocks indexed by global system line.
        // [eq, localBlock, iv] where iv is the global line number.
        VA = new double[3, 2, nsysMax];

        // VB(3,2,NSYS) -- sub-diagonal blocks indexed by global system line.
        VB = new double[3, 2, nsysMax];

        // VM(3,IZX,NSYS) -- mass defect influence column indexed by global system line.
        VM = new double[3, maxWake, nsysMax];

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

    /// <summary>Maximum system lines (array third dimension size).</summary>
    public int MaxStations { get; }

    /// <summary>Maximum wake stations (VM second dimension size).</summary>
    public int MaxWake { get; }

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
    /// Mass defect influence column.
    /// Indexed [equation (0-2), wake coupling index, iv (global system line)].
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
    /// Copies the ISYS mapping from EdgeVelocityCalculator into the system and sets NSYS.
    /// </summary>
    /// <param name="isysMapping">The ISYS mapping array from EdgeVelocityCalculator.MapStationsToSystemLines.
    /// isysMapping[lineNum, 0] = ibl, isysMapping[lineNum, 1] = side.</param>
    /// <param name="nsysCount">Total number of system lines.</param>
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

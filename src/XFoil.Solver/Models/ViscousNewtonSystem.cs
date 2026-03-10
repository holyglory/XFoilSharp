namespace XFoil.Solver.Models;

/// <summary>
/// Pre-allocated Newton system arrays for the block-tridiagonal viscous BL solver.
/// Matches the BLSOLV structure from xsolve.f.
/// </summary>
public sealed class ViscousNewtonSystem
{
    /// <summary>
    /// Creates a new Newton system workspace with pre-allocated arrays.
    /// </summary>
    /// <param name="maxStations">Maximum BL stations (IVX).</param>
    /// <param name="maxWake">Maximum wake stations (IZX).</param>
    public ViscousNewtonSystem(int maxStations, int maxWake)
    {
        MaxStations = maxStations;
        MaxWake = maxWake;

        // VA(3,2,IVX) -- diagonal blocks of the block-tridiagonal system.
        VA = new double[3, 2, maxStations];

        // VB(3,2,IVX) -- sub-diagonal blocks.
        VB = new double[3, 2, maxStations];

        // VM(3,IZX,IVX) -- mass defect influence column.
        // The third dimension in the mass influence column represents
        // the coupling between BL stations and wake mass defect.
        VM = new double[3, maxWake, maxStations];

        // VDEL(3,2,IVX) -- right-hand side / Newton step deltas.
        VDEL = new double[3, 2, maxStations];

        // VZ(3,2) -- TE coupling block (couples upper and lower surfaces).
        VZ = new double[3, 2];

        // ISYS(IVX,2) -- BL station to global system line mapping.
        ISYS = new int[maxStations, 2];

        // Total number of system lines (set during system assembly).
        NSYS = 0;
    }

    /// <summary>Maximum BL stations.</summary>
    public int MaxStations { get; }

    /// <summary>Maximum wake stations.</summary>
    public int MaxWake { get; }

    /// <summary>
    /// Diagonal blocks of the block-tridiagonal system.
    /// Indexed [equation (0-2), side (0-1), station].
    /// </summary>
    public double[,,] VA { get; }

    /// <summary>
    /// Sub-diagonal blocks.
    /// Indexed [equation (0-2), side (0-1), station].
    /// </summary>
    public double[,,] VB { get; }

    /// <summary>
    /// Mass defect influence column.
    /// Indexed [equation (0-2), wake station, BL station].
    /// </summary>
    public double[,,] VM { get; }

    /// <summary>
    /// Right-hand side / Newton step solution deltas.
    /// Indexed [equation (0-2), side (0-1), station].
    /// </summary>
    public double[,,] VDEL { get; }

    /// <summary>
    /// TE coupling block that couples upper and lower surfaces at the trailing edge.
    /// Indexed [equation (0-2), side (0-1)].
    /// </summary>
    public double[,] VZ { get; }

    /// <summary>
    /// BL station to global system line mapping.
    /// Indexed [station, side].
    /// </summary>
    public int[,] ISYS { get; }

    /// <summary>
    /// Total number of system lines (set during assembly).
    /// </summary>
    public int NSYS { get; set; }
}

namespace XFoil.Solver.Models;

/// <summary>
/// Mutable workspace holding boundary layer state for both surfaces and wake.
/// Matches XFoil's COMMON/BLPAR/ arrays: UEDG, THET, DSTR, CTAU, MASS, XSSI, etc.
/// All arrays are 0-based. Side index: 0 = upper/side-1, 1 = lower/side-2.
/// </summary>
public sealed class BoundaryLayerSystemState
{
    /// <summary>
    /// Creates a new BL system state with pre-allocated arrays.
    /// </summary>
    /// <param name="maxStations">Maximum number of BL stations per surface (IVX in Fortran).</param>
    /// <param name="maxWakeStations">Maximum number of wake stations.</param>
    public BoundaryLayerSystemState(int maxStations, int maxWakeStations)
    {
        MaxStations = maxStations;
        MaxWakeStations = maxWakeStations;

        // Panel node index for each BL station, per side.
        // IPAN(IVX,2) in Fortran. Station 0 = -1 (virtual stagnation, no panel).
        IPAN = new int[maxStations, 2];

        // Tangential velocity sign for each BL station, per side.
        // VTI(IVX,2) in Fortran. +1 for upper (side 0), -1 for lower (side 1).
        VTI = new double[maxStations, 2];

        // Edge velocity at each BL station, per side.
        // UEDG(IVX,2) in Fortran.
        UEDG = new double[maxStations, 2];

        // Momentum thickness at each station, per side.
        // THET(IVX,2) in Fortran.
        THET = new double[maxStations, 2];

        // Displacement thickness at each station, per side.
        // DSTR(IVX,2) in Fortran.
        DSTR = new double[maxStations, 2];

        // Shear stress coefficient at each station, per side.
        // CTAU(IVX,2) in Fortran.
        CTAU = new double[maxStations, 2];

        // Mass defect (delta* x Ue) at each station, per side.
        // MASS(IVX,2) in Fortran.
        MASS = new double[maxStations, 2];

        // BL arc-length coordinate at each station, per side.
        // XSSI(IVX,2) in Fortran.
        XSSI = new double[maxStations, 2];

        // Transition station index, per side.
        // ITRAN(2) in Fortran.
        ITRAN = new int[2];

        // Trailing edge station index, per side.
        // IBLTE(2) in Fortran.
        IBLTE = new int[2];

        // Number of BL stations per side.
        // NBL(2) in Fortran.
        NBL = new int[2];

        // Interpolated transition xi location, per side.
        TINDEX = new double[2];
    }

    /// <summary>Maximum stations per surface.</summary>
    public int MaxStations { get; }

    /// <summary>Maximum wake stations.</summary>
    public int MaxWakeStations { get; }

    /// <summary>Panel node index for each BL station, indexed [station, side]. -1 for virtual stagnation.</summary>
    public int[,] IPAN { get; }

    /// <summary>Tangential velocity sign, indexed [station, side]. +1 upper, -1 lower.</summary>
    public double[,] VTI { get; }

    /// <summary>Edge velocity, indexed [station, side].</summary>
    public double[,] UEDG { get; }

    /// <summary>Momentum thickness, indexed [station, side].</summary>
    public double[,] THET { get; }

    /// <summary>Displacement thickness, indexed [station, side].</summary>
    public double[,] DSTR { get; }

    /// <summary>Shear stress coefficient, indexed [station, side].</summary>
    public double[,] CTAU { get; }

    /// <summary>Mass defect (delta* x Ue), indexed [station, side].</summary>
    public double[,] MASS { get; }

    /// <summary>BL arc-length coordinate, indexed [station, side].</summary>
    public double[,] XSSI { get; }

    /// <summary>Transition station index, per side.</summary>
    public int[] ITRAN { get; }

    /// <summary>TE station index, per side.</summary>
    public int[] IBLTE { get; }

    /// <summary>Number of BL stations, per side.</summary>
    public int[] NBL { get; }

    /// <summary>Interpolated transition xi location, per side.</summary>
    public double[] TINDEX { get; }

    /// <summary>Whether the viscous solution has converged.</summary>
    public bool Converged { get; set; }

    /// <summary>Current RMS residual of the BL system.</summary>
    public double RmsResidual { get; set; }

    /// <summary>Station/side where the maximum residual occurs.</summary>
    public int MaxResidualLocation { get; set; }

    /// <summary>Current Newton iteration count.</summary>
    public int Iteration { get; set; }

    /// <summary>
    /// Initializes station counts for both surfaces and the wake.
    /// Sets NBL and IBLTE based on the provided counts.
    /// </summary>
    /// <param name="side1">Number of BL stations on side 1 (upper surface, stagnation to TE).</param>
    /// <param name="side2">Number of BL stations on side 2 (lower surface, stagnation to TE).</param>
    /// <param name="wake">Number of wake stations (appended after side 2 TE).</param>
    public void InitializeForStationCounts(int side1, int side2, int wake)
    {
        // Side 1: stations 0..side1-1, TE at station side1-1
        IBLTE[0] = side1 - 1;
        NBL[0] = side1;

        // Side 2: stations 0..side2-1+wake, TE at station side2-1
        // Wake stations follow side 2 after the TE.
        IBLTE[1] = side2 - 1;
        NBL[1] = side2 + wake;
    }
}

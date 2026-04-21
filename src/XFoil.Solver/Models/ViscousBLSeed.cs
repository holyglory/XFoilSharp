namespace XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: none (Fortran XFoil threads BL state via shared COMMON
// blocks across sequential OPER alfa/CL calls — there is no explicit seed type).
// Role in port: Managed carrier for the boundary-layer state arrays captured
// from a previously-converged operating point, so a subsequent SolveViscous
// call can skip the Thwaites cold-start and begin Newton iteration from the
// warm state — replicating Fortran's sequential-α behavior.
// Difference from legacy: Explicit typed seed instead of implicit COMMON state.

/// <summary>
/// Snapshot of the boundary-layer state at a converged operating point,
/// usable as initial condition for a subsequent viscous solve at a nearby
/// α. Indexing of the 2D arrays is [station, side], matching the engine's
/// internal representation (side 0 = upper, side 1 = lower).
///
/// Only the primary BL unknowns are captured (THET, DSTR, CTAU, UEDG);
/// transition-tracking and wake-continuity fields are intentionally left
/// to be recomputed by the first Newton pass from the primary state.
/// This is a minimal seed, not a full state snapshot.
/// </summary>
public sealed class ViscousBLSeed
{
    public ViscousBLSeed(
        double alphaRadians,
        int isp,
        int[] nbl,
        double[,] thet,
        double[,] dstr,
        double[,] ctau,
        double[,] uedg,
        int[] itran,
        double[,]? amplificationCarry = null,
        double[]? tindex = null)
    {
        AlphaRadians = alphaRadians;
        ISP = isp;
        NBL = nbl;
        THET = thet;
        DSTR = dstr;
        CTAU = ctau;
        UEDG = uedg;
        ITRAN = itran;
        AmplificationCarry = amplificationCarry;
        TIndex = tindex;
    }

    public double AlphaRadians { get; }
    /// <summary>Stagnation panel index at the seed's α (pre-shift).</summary>
    public int ISP { get; }
    public int[] NBL { get; }
    public double[,] THET { get; }
    public double[,] DSTR { get; }
    public double[,] CTAU { get; }
    public double[,] UEDG { get; }
    public int[] ITRAN { get; }

    /// <summary>
    /// Iter 46: e-n amplification factor carry, indexed [station, side].
    /// Fortran XFoil carries this across sequential `alfa` commands via
    /// COMMON blocks — it sets where transition will occur at the next α.
    /// When null, Newton reseeds amplification tracking from scratch.
    /// </summary>
    public double[,]? AmplificationCarry { get; }

    /// <summary>
    /// Iter 56: transition interpolation xi (per side). Carries fractional
    /// transition location within the transition station. Complements
    /// `ITRAN` (integer station index) for precise transition continuity.
    /// </summary>
    public double[]? TIndex { get; }
}

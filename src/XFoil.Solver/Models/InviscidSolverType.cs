namespace XFoil.Solver.Models;

/// <summary>
/// Selects which inviscid solver formulation to use for the aerodynamic analysis.
/// </summary>
public enum InviscidSolverType
{
    /// <summary>
    /// Hess-Smith constant-strength source/vortex panel method (existing solver).
    /// </summary>
    HessSmith,

    /// <summary>
    /// Linear-vorticity streamfunction panel method (XFoil parity solver).
    /// </summary>
    LinearVortex
}

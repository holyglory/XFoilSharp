// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: classic inviscid kernel lineage
// Role in port: Managed enum selecting which inviscid solver implementation to run.
// Differences: Classic XFoil did not expose multiple interchangeable inviscid kernels through a typed public option; this is a managed extension point.
// Decision: Keep the enum because solver selection is an intentional managed capability.
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

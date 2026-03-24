// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: laminar/turbulent/wake regime selection
// Role in port: Managed enum naming the active flow regime at a boundary-layer station.
// Differences: Legacy XFoil encoded the same distinction through flags and branch logic, while the managed port exposes it as a typed enum.
// Decision: Keep the enum because it makes regime transitions explicit.
namespace XFoil.Solver.Models;

public enum ViscousFlowRegime
{
    Laminar = 0,
    Turbulent = 1,
    Wake = 2
}

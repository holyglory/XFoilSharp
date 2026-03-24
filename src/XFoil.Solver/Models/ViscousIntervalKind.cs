// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: laminar/turbulent/wake interval equations
// Role in port: Managed enum naming the interval-equation family active between two viscous stations.
// Differences: Legacy XFoil inferred the interval kind from branch state and transition location, while the managed port stores it explicitly.
// Decision: Keep the enum because it makes interval assembly intent visible.
namespace XFoil.Solver.Models;

public enum ViscousIntervalKind
{
    Laminar = 1,
    Turbulent = 2,
    Wake = 3
}

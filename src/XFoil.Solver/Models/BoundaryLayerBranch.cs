// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/XFOIL.INC :: side-1/side-2/wake indexing
// Role in port: Managed enum naming the upper-surface, lower-surface, and wake boundary-layer branches.
// Differences: Legacy XFoil uses integer side indices and implicit wake conventions; the managed port exposes them as a typed enum.
// Decision: Keep the enum because it makes branch identity explicit without touching parity math.
namespace XFoil.Solver.Models;

public enum BoundaryLayerBranch
{
    Upper = 0,
    Lower = 1,
    Wake = 2
}

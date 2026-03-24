// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/XFOIL.INC :: X/Y coordinate arrays
// Role in port: Managed value object for a single airfoil coordinate pair.
// Differences: Legacy XFoil stores coordinates in parallel REAL arrays rather than a point record.
// Decision: Keep the managed record because it simplifies geometry transforms, parsing, and tests; no parity-only branch is needed here.
namespace XFoil.Core.Models;

public readonly record struct AirfoilPoint(double X, double Y);

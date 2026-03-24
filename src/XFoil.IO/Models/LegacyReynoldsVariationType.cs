// Legacy audit:
// Primary legacy source: none
// Role in port: Managed enum representing Reynolds-variation modes encoded by legacy polar files.
// Differences: No standalone Fortran enum existed; the legacy code represented these modes through formatted output tokens and session state.
// Decision: Keep the managed enum because it makes legacy file semantics explicit and type-safe.
namespace XFoil.IO.Models;

public enum LegacyReynoldsVariationType
{
    Unspecified = 0,
    Fixed = 1,
    InverseSqrtCl = 2,
    InverseCl = 3,
}

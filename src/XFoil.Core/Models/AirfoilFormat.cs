// Legacy audit:
// Primary legacy source: f_xfoil/src/aread.f :: AREAD
// Secondary legacy source: f_xfoil/src/xfoil.f :: LOAD/ISAV/MSAV file-format conventions
// Role in port: Managed enum describing the coordinate-file classifications consumed by the .NET parser and exporters.
// Differences: Legacy XFoil carries these format distinctions as integer ITYPE flags and raw command/file state instead of a typed enum.
// Decision: Keep the managed enum because it makes the IO layer explicit and type-safe; no parity-only branch is needed here.
namespace XFoil.Core.Models;

public enum AirfoilFormat
{
    PlainCoordinates,
    LabeledCoordinates,
    Ises,
    Mses
}

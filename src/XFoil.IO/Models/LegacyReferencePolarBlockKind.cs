// Legacy audit:
// Primary legacy source: none
// Role in port: Managed enum identifying the kinds of reference-polar comparison blocks used by tests and tooling.
// Differences: No direct Fortran analogue exists because this categorization belongs to the managed comparison/import layer.
// Decision: Keep the managed enum because it makes the external comparison-file structure explicit.
namespace XFoil.IO.Models;

public enum LegacyReferencePolarBlockKind
{
    DragVersusLift = 0,
    AlphaVersusLift = 1,
    AlphaVersusMoment = 2,
    TransitionVersusLift = 3,
}

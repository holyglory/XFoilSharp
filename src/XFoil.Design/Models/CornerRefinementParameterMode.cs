// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: CADD-style corner refinement workflow
// Role in port: Managed enum selecting how contour-corner refinement parameters are interpreted.
// Differences: Legacy XFoil exposes similar choices procedurally through command arguments rather than a typed enum.
// Decision: Keep the managed enum because it gives the service and CLI layers a safe API surface.
namespace XFoil.Design.Models;

public enum CornerRefinementParameterMode
{
    Uniform,
    ArcLength,
}

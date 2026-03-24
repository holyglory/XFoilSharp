// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed enum describing the origin choice for geometry-scaling helpers.
// Differences: Legacy XFoil does not expose this exact abstraction; scaling choices are embedded in command-specific workflows.
// Decision: Keep the managed enum because it makes the scaling API explicit without introducing parity concerns.
namespace XFoil.Design.Models;

public enum GeometryScaleOrigin
{
    LeadingEdge,
    TrailingEdge,
    Point,
}

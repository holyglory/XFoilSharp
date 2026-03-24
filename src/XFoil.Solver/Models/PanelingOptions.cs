// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: PANGEN weighting controls
// Role in port: Managed immutable panel-redistribution weights for curvature, leading-edge, and trailing-edge clustering.
// Differences: Legacy XFoil applies comparable weights through interactive paneling state, while the managed port validates and packages them as one explicit options object.
// Decision: Keep the managed options object because it makes paneling behavior configurable and testable.
namespace XFoil.Solver.Models;

public sealed class PanelingOptions
{
    // Legacy mapping: f_xfoil/src/xpanel.f :: PANGEN weighting controls.
    // Difference from legacy: Weight validation is explicit and centralized instead of being handled indirectly by the interactive paneling flow.
    // Decision: Keep the managed constructor because explicit validation improves robustness.
    public PanelingOptions(
        double curvatureWeight = 1.0d,
        double leadingEdgeWeight = 1.5d,
        double trailingEdgeWeight = 0.35d)
    {
        if (curvatureWeight < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(curvatureWeight), "Curvature weight cannot be negative.");
        }

        if (leadingEdgeWeight < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(leadingEdgeWeight), "Leading-edge weight cannot be negative.");
        }

        if (trailingEdgeWeight < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(trailingEdgeWeight), "Trailing-edge weight cannot be negative.");
        }

        CurvatureWeight = curvatureWeight;
        LeadingEdgeWeight = leadingEdgeWeight;
        TrailingEdgeWeight = trailingEdgeWeight;
    }

    public double CurvatureWeight { get; }

    public double LeadingEdgeWeight { get; }

    public double TrailingEdgeWeight { get; }
}

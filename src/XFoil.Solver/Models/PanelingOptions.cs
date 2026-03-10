namespace XFoil.Solver.Models;

public sealed class PanelingOptions
{
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

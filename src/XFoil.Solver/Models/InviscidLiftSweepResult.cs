using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class InviscidLiftSweepResult
{
    public InviscidLiftSweepResult(
        AirfoilGeometry geometry,
        AnalysisSettings settings,
        IReadOnlyList<InviscidTargetLiftResult> points)
    {
        Geometry = geometry;
        Settings = settings;
        Points = points;
    }

    public AirfoilGeometry Geometry { get; }

    public AnalysisSettings Settings { get; }

    public IReadOnlyList<InviscidTargetLiftResult> Points { get; }
}

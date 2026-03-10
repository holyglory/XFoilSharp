using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class PolarSweepResult
{
    public PolarSweepResult(
        AirfoilGeometry geometry,
        AnalysisSettings settings,
        IReadOnlyList<PolarPoint> points)
    {
        Geometry = geometry;
        Settings = settings;
        Points = points;
    }

    public AirfoilGeometry Geometry { get; }

    public AnalysisSettings Settings { get; }

    public IReadOnlyList<PolarPoint> Points { get; }
}

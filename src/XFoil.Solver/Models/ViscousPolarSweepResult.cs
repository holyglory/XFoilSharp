using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class ViscousPolarSweepResult
{
    public ViscousPolarSweepResult(
        AirfoilGeometry geometry,
        AnalysisSettings settings,
        IReadOnlyList<ViscousPolarPoint> points)
    {
        Geometry = geometry;
        Settings = settings;
        Points = points;
    }

    public AirfoilGeometry Geometry { get; }

    public AnalysisSettings Settings { get; }

    public IReadOnlyList<ViscousPolarPoint> Points { get; }
}

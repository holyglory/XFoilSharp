using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class ViscousLiftSweepResult
{
    public ViscousLiftSweepResult(
        AirfoilGeometry geometry,
        AnalysisSettings settings,
        IReadOnlyList<ViscousTargetLiftResult> points)
    {
        Geometry = geometry;
        Settings = settings;
        Points = points;
    }

    public AirfoilGeometry Geometry { get; }

    public AnalysisSettings Settings { get; }

    public IReadOnlyList<ViscousTargetLiftResult> Points { get; }
}

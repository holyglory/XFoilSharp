namespace XFoil.Solver.Models;

public sealed class WakeGeometry
{
    public WakeGeometry(IReadOnlyList<WakePoint> points)
    {
        Points = points;
    }

    public IReadOnlyList<WakePoint> Points { get; }
}

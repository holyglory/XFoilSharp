// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: ASEQ operating-point sweep lineage, f_xfoil/src/xoper.f :: PACC/PWRT
// Role in port: Managed immutable result for an angle-of-attack inviscid sweep.
// Differences: Legacy XFoil collects the same points in interactive polar buffers, while the managed port returns a stable sweep object.
// Decision: Keep the managed container because it is a better fit for batch APIs and tests.
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

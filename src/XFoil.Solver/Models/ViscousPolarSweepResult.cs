// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: ASEQ/VISC sweep lineage, f_xfoil/src/xoper.f :: PACC/PWRT
// Role in port: Managed immutable result for a viscous alpha sweep.
// Differences: Legacy XFoil stores sweep points in interactive polar buffers, while the managed port returns a stable sweep object.
// Decision: Keep the managed sweep container because it fits batch use and testing better.
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

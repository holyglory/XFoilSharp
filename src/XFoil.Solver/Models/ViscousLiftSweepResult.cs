// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: CSEQ/VISC lift-sweep lineage
// Role in port: Managed immutable result for a viscous lift-target sweep.
// Differences: Legacy XFoil accumulates these points in interactive polar buffers, while the managed port returns a stable sweep object.
// Decision: Keep the managed sweep container because it is better for batch APIs and tests.
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

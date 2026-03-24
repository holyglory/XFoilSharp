// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: CLI/CSEQ operating-point sweep lineage
// Role in port: Managed container for a lift-target inviscid sweep and its operating-point results.
// Differences: Legacy XFoil accumulates sweep points in interactive polar buffers, while the managed port returns an explicit immutable sweep object.
// Decision: Keep the managed container because it is better for batch use and tests.
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

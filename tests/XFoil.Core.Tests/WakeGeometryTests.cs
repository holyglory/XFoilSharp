using XFoil.Core.Services;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xwake.f and wake marching logic in xfoil.f
// Secondary legacy source: f_xfoil/src/xpanel.f trailing-edge/wake setup
// Role in port: Verifies the managed wake-geometry construction derived from the legacy inviscid wake march.
// Differences: The managed analysis result exposes wake points as immutable objects instead of hidden solver arrays.
// Decision: Keep the managed wake result because it makes the legacy-derived geometry easy to validate and consume.
namespace XFoil.Core.Tests;

public sealed class WakeGeometryTests
{
    [Fact]
    // Legacy mapping: legacy inviscid wake marching sequence.
    // Difference from legacy: Downstream-distance monotonicity is asserted on the managed wake object rather than observed indirectly in later calculations.
    // Decision: Keep the managed invariant because it is a direct regression for the wake march.
    public void InviscidAnalysis_ProducesWakeWithIncreasingDownstreamDistance()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscid(geometry, 4d);

        Assert.True(result.Wake.Points.Count >= 6);
        for (var index = 1; index < result.Wake.Points.Count; index++)
        {
            Assert.True(result.Wake.Points[index].DistanceFromTrailingEdge > result.Wake.Points[index - 1].DistanceFromTrailingEdge);
        }
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xpanel.f trailing-edge wake start setup.
    // Difference from legacy: The test reads the wake origin and downstream extent from the managed result instead of the legacy wake arrays.
    // Decision: Keep the managed state check because it preserves the same wake anchoring behavior with clearer visibility.
    public void WakeStartsAtTrailingEdgeAndPointsDownstream()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);

        var result = service.AnalyzeInviscid(geometry, 2d);
        var wake = result.Wake;

        Assert.InRange(wake.Points[0].DistanceFromTrailingEdge, -1e-9, 1e-9);
        Assert.True(wake.Points[^1].Location.X > wake.Points[0].Location.X);
    }

    [Fact]
    // Legacy mapping: legacy inviscid wake direction under positive incidence.
    // Difference from legacy: The managed test asserts wake deflection directly instead of inferring it from plotted or printed wake coordinates.
    // Decision: Keep the managed regression because it is the clearest check of the same physical trend.
    public void PositiveAlphaWake_DeflectsBelowTrailingEdge()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var result = service.AnalyzeInviscid(geometry, 4d);

        Assert.True(result.Wake.Points[^1].Location.Y < result.Wake.Points[0].Location.Y);
    }
}

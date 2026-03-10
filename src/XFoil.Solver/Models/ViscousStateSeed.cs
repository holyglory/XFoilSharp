namespace XFoil.Solver.Models;

public sealed class ViscousStateSeed
{
    public ViscousStateSeed(
        BoundaryLayerTopology topology,
        ViscousBranchSeed upperSurface,
        ViscousBranchSeed lowerSurface,
        ViscousBranchSeed wake,
        double trailingEdgeGap,
        double trailingEdgeNormalGap,
        double trailingEdgeStreamwiseGap)
    {
        Topology = topology;
        UpperSurface = upperSurface;
        LowerSurface = lowerSurface;
        Wake = wake;
        TrailingEdgeGap = trailingEdgeGap;
        TrailingEdgeNormalGap = trailingEdgeNormalGap;
        TrailingEdgeStreamwiseGap = trailingEdgeStreamwiseGap;
    }

    public BoundaryLayerTopology Topology { get; }

    public ViscousBranchSeed UpperSurface { get; }

    public ViscousBranchSeed LowerSurface { get; }

    public ViscousBranchSeed Wake { get; }

    public double TrailingEdgeGap { get; }

    public double TrailingEdgeNormalGap { get; }

    public double TrailingEdgeStreamwiseGap { get; }
}

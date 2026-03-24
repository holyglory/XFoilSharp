// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: COMSET/MRCHUE seed lineage
// Role in port: Managed container for the full upper, lower, and wake seed state plus trailing-edge gap metadata.
// Differences: Legacy XFoil stores these quantities in several arrays and gap scalars, while the managed port packages them into one explicit seed object.
// Decision: Keep the managed DTO because it makes seed transport and inspection straightforward.
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

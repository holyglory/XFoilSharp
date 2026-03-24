// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: assembled viscous state arrays
// Role in port: Managed container for the full solved upper, lower, and wake branch states built from a seed.
// Differences: Legacy XFoil leaves this state in distributed arrays, while the managed port packages it into an immutable object.
// Decision: Keep the managed DTO because it clarifies the solved-state boundary.
namespace XFoil.Solver.Models;

public sealed class ViscousStateEstimate
{
    public ViscousStateEstimate(
        ViscousStateSeed seed,
        ViscousBranchState upperSurface,
        ViscousBranchState lowerSurface,
        ViscousBranchState wake)
    {
        Seed = seed;
        UpperSurface = upperSurface;
        LowerSurface = lowerSurface;
        Wake = wake;
    }

    public ViscousStateSeed Seed { get; }

    public ViscousBranchState UpperSurface { get; }

    public ViscousBranchState LowerSurface { get; }

    public ViscousBranchState Wake { get; }
}

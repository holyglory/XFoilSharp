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

namespace XFoil.Solver.Models;

public sealed class ViscousStationDerivedState
{
    public ViscousStationDerivedState(
        double edgeMachSquared,
        double densityRatio,
        double kinematicShapeFactor,
        double densityShapeFactor,
        double energyShapeFactor,
        double slipVelocityRatio,
        double effectiveEdgeVelocity)
    {
        EdgeMachSquared = edgeMachSquared;
        DensityRatio = densityRatio;
        KinematicShapeFactor = kinematicShapeFactor;
        DensityShapeFactor = densityShapeFactor;
        EnergyShapeFactor = energyShapeFactor;
        SlipVelocityRatio = slipVelocityRatio;
        EffectiveEdgeVelocity = effectiveEdgeVelocity;
    }

    public double EdgeMachSquared { get; }

    public double DensityRatio { get; }

    public double KinematicShapeFactor { get; }

    public double DensityShapeFactor { get; }

    public double EnergyShapeFactor { get; }

    public double SlipVelocityRatio { get; }

    public double EffectiveEdgeVelocity { get; }
}

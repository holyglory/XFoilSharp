// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: BLVAR/BLMID derived-state correlations
// Role in port: Managed DTO for one station's derived kinematic and compressibility quantities.
// Differences: Legacy XFoil computes these values transiently inside the boundary-layer kernels, while the managed port packages them explicitly for diagnostics and interval assembly.
// Decision: Keep the managed DTO because it improves solver observability.
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

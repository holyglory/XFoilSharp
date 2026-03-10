using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousIntervalSystemBuilder
{
    private const double Gamma = 1.4d;
    private readonly BoundaryLayerCorrelationConstants constants = BoundaryLayerCorrelationConstants.Default;
    private readonly LaminarAmplificationModel amplificationModel = new();

    public ViscousIntervalSystem Build(ViscousStateEstimate state, AnalysisSettings settings)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return new ViscousIntervalSystem(
            state,
            constants,
            BuildBranchIntervals(state.UpperSurface, settings),
            BuildBranchIntervals(state.LowerSurface, settings),
            BuildBranchIntervals(state.Wake, settings));
    }

    public ViscousIntervalState BuildInterval(
        BoundaryLayerBranch branch,
        ViscousStationState start,
        ViscousStationState end,
        AnalysisSettings settings,
        int index = 0)
    {
        var startDerived = BuildDerivedState(start, settings, branch == BoundaryLayerBranch.Wake);
        var endDerived = BuildDerivedState(end, settings, branch == BoundaryLayerBranch.Wake);
        var kind = DetermineIntervalKind(branch, start, end);
        var upwindWeight = ComputeUpwindWeight(startDerived.KinematicShapeFactor, endDerived.KinematicShapeFactor, kind);
        var deltaXi = Math.Max(end.Xi - start.Xi, 1e-9);
        var averageTheta = 0.5d * (start.MomentumThickness + end.MomentumThickness);
        var averageShapeFactor = 0.5d * (start.ShapeFactor + end.ShapeFactor);
        var averageSkinFriction = 0.5d * (start.SkinFrictionCoefficient + end.SkinFrictionCoefficient) * constants.CfScale;
        var averageEffectiveVelocity = 0.5d * (startDerived.EffectiveEdgeVelocity + endDerived.EffectiveEdgeVelocity);
        var edgeVelocityGradient = (endDerived.EffectiveEdgeVelocity - startDerived.EffectiveEdgeVelocity) / Math.Max(averageEffectiveVelocity, 1e-9);
        var amplificationGrowthRate = kind == ViscousIntervalKind.Laminar
            ? amplificationModel.ComputeGrowthRate(
                start,
                end.Xi,
                end.EdgeVelocity,
                end.MomentumThickness,
                end.ShapeFactor,
                settings)
            : 0d;

        var momentumResidual =
            ((end.MomentumThickness - start.MomentumThickness) / deltaXi)
            + ((averageShapeFactor + 2d) * averageTheta * edgeVelocityGradient / deltaXi)
            - (0.5d * averageSkinFriction);
        var shapeResidual = endDerived.KinematicShapeFactor - startDerived.KinematicShapeFactor;
        var expectedWakeSkinFriction = kind == ViscousIntervalKind.Wake ? 0d : averageSkinFriction;
        var skinFrictionResidual = averageSkinFriction - expectedWakeSkinFriction;
        var amplificationResidual = kind == ViscousIntervalKind.Laminar
            ? ((end.AmplificationFactor - start.AmplificationFactor) - (amplificationGrowthRate * deltaXi))
            : 0d;

        return new ViscousIntervalState(
            branch,
            kind,
            index,
            start,
            end,
            startDerived,
            endDerived,
            upwindWeight,
            Math.Log(Math.Max(end.Xi, 1e-9) / Math.Max(start.Xi, 1e-9)),
            Math.Log(Math.Max(endDerived.EffectiveEdgeVelocity, 1e-9) / Math.Max(startDerived.EffectiveEdgeVelocity, 1e-9)),
            Math.Log(Math.Max(end.MomentumThickness, 1e-12) / Math.Max(start.MomentumThickness, 1e-12)),
            Math.Log(Math.Max(endDerived.KinematicShapeFactor - 1d, 1e-9) / Math.Max(startDerived.KinematicShapeFactor - 1d, 1e-9)),
            amplificationGrowthRate,
            momentumResidual,
            shapeResidual,
            skinFrictionResidual,
            amplificationResidual);
    }

    private IReadOnlyList<ViscousIntervalState> BuildBranchIntervals(ViscousBranchState branch, AnalysisSettings settings)
    {
        var intervals = new List<ViscousIntervalState>(Math.Max(0, branch.Stations.Count - 1));
        for (var index = 1; index < branch.Stations.Count; index++)
        {
            intervals.Add(BuildInterval(branch.Branch, branch.Stations[index - 1], branch.Stations[index], settings, index - 1));
        }

        return intervals;
    }

    private ViscousStationDerivedState BuildDerivedState(ViscousStationState station, AnalysisSettings settings, bool isWake)
    {
        var velocityRatio = station.EdgeVelocity / settings.FreestreamVelocity;
        var edgeMachSquared = settings.MachNumber * settings.MachNumber * velocityRatio * velocityRatio;
        edgeMachSquared = Math.Min(edgeMachSquared, 0.95d);
        var temperatureRatio = 1d + (0.5d * (Gamma - 1d) * edgeMachSquared);
        var densityRatio = Math.Pow(temperatureRatio, -1d / (Gamma - 1d));
        var floor = isWake ? 1.00005d : 1.05d;
        var kinematicShapeFactor = Math.Max(floor, station.ShapeFactor + (0.08d * edgeMachSquared));
        var densityShapeFactor = Math.Max(kinematicShapeFactor, station.ShapeFactor * (1d + (0.12d * edgeMachSquared)));
        var energyShapeFactor = Math.Max(1.0001d, kinematicShapeFactor - 0.35d + (0.015d * Math.Log10(Math.Max(station.ReynoldsTheta, 10d))));
        var slipVelocityRatio = 0.5d * energyShapeFactor * (1d - ((kinematicShapeFactor - 1d) / (constants.GbConstant * Math.Max(station.ShapeFactor, 1.01d))));
        var slipClamp = isWake ? 0.99995d : 0.98d;
        slipVelocityRatio = Math.Clamp(slipVelocityRatio, 0d, slipClamp);
        var effectiveEdgeVelocity = station.EdgeVelocity * (1d - 0.5d * constants.CtConstant * edgeMachSquared);

        return new ViscousStationDerivedState(
            edgeMachSquared,
            densityRatio,
            kinematicShapeFactor,
            densityShapeFactor,
            energyShapeFactor,
            slipVelocityRatio,
            Math.Max(effectiveEdgeVelocity, 1e-6));
    }

    private static double ComputeUpwindWeight(double hk1, double hk2, ViscousIntervalKind kind)
    {
        var hdCon = kind == ViscousIntervalKind.Wake ? 1d / (hk2 * hk2) : 5d / (hk2 * hk2);
        var argument = Math.Abs((hk2 - 1d) / Math.Max(hk1 - 1d, 1e-9));
        var hl = Math.Log(Math.Max(argument, 1e-9));
        var hlSq = Math.Min(hl * hl, 15d);
        var ehh = Math.Exp(-hlSq * hdCon);
        return 1d - (0.5d * ehh);
    }

    private static ViscousIntervalKind DetermineIntervalKind(
        BoundaryLayerBranch branch,
        ViscousStationState start,
        ViscousStationState end)
    {
        if (branch == BoundaryLayerBranch.Wake)
        {
            return ViscousIntervalKind.Wake;
        }

        if (start.Regime == ViscousFlowRegime.Turbulent || end.Regime == ViscousFlowRegime.Turbulent)
        {
            return ViscousIntervalKind.Turbulent;
        }

        return ViscousIntervalKind.Laminar;
    }
}

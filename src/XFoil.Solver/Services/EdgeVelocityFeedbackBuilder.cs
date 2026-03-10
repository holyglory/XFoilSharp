using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class EdgeVelocityFeedbackBuilder
{
    public ViscousStateSeed ApplyDisplacementFeedback(
        ViscousStateSeed seed,
        ViscousStateEstimate solvedState,
        double couplingFactor)
    {
        if (seed is null)
        {
            throw new ArgumentNullException(nameof(seed));
        }

        if (solvedState is null)
        {
            throw new ArgumentNullException(nameof(solvedState));
        }

        if (couplingFactor <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(couplingFactor), "Coupling factor must be positive.");
        }

        return new ViscousStateSeed(
            seed.Topology,
            ApplyBranchFeedback(seed.UpperSurface, solvedState.UpperSurface, couplingFactor),
            ApplyBranchFeedback(seed.LowerSurface, solvedState.LowerSurface, couplingFactor),
            ApplyBranchFeedback(seed.Wake, solvedState.Wake, 0.5d * couplingFactor),
            seed.TrailingEdgeGap,
            seed.TrailingEdgeNormalGap,
            seed.TrailingEdgeStreamwiseGap);
    }

    public double ComputeAverageRelativeEdgeVelocityChange(ViscousStateSeed initialSeed, ViscousStateSeed finalSeed)
    {
        if (initialSeed is null)
        {
            throw new ArgumentNullException(nameof(initialSeed));
        }

        if (finalSeed is null)
        {
            throw new ArgumentNullException(nameof(finalSeed));
        }

        var pairs = PairStations(initialSeed.UpperSurface, finalSeed.UpperSurface)
            .Concat(PairStations(initialSeed.LowerSurface, finalSeed.LowerSurface))
            .Concat(PairStations(initialSeed.Wake, finalSeed.Wake))
            .ToArray();

        return pairs
            .Where(pair => pair.Initial.Xi > 1e-6)
            .Average(pair =>
            {
                var scale = Math.Max(Math.Max(pair.Initial.EdgeVelocity, pair.Final.EdgeVelocity), 1e-3);
                return Math.Abs(pair.Final.EdgeVelocity - pair.Initial.EdgeVelocity) / scale;
            });
    }

    private static ViscousBranchSeed ApplyBranchFeedback(
        ViscousBranchSeed seedBranch,
        ViscousBranchState solvedBranch,
        double couplingFactor)
    {
        var updatedStations = new List<ViscousStationSeed>(seedBranch.Stations.Count);

        for (var index = 0; index < seedBranch.Stations.Count; index++)
        {
            var seedStation = seedBranch.Stations[index];
            var sourceIndex = MapIndex(index, seedBranch.Stations.Count, solvedBranch.Stations.Count);
            var gradient = EstimateDisplacementGradient(solvedBranch.Stations, sourceIndex);
            var correction = Math.Clamp(1d - (couplingFactor * gradient), 0.7d, 1.3d);
            var correctedEdgeVelocity = Math.Max(1e-4, seedStation.EdgeVelocity * correction);

            updatedStations.Add(new ViscousStationSeed(
                seedStation.Index,
                seedStation.Location,
                seedStation.Xi,
                correctedEdgeVelocity,
                seedStation.WakeGap));
        }

        return new ViscousBranchSeed(seedBranch.Branch, updatedStations);
    }

    private static double EstimateDisplacementGradient(IReadOnlyList<ViscousStationState> stations, int index)
    {
        if (stations.Count == 1)
        {
            return 0d;
        }

        if (index == 0)
        {
            return ComputeGradient(stations[0], stations[1]);
        }

        if (index == stations.Count - 1)
        {
            return ComputeGradient(stations[^2], stations[^1]);
        }

        var leftGradient = ComputeGradient(stations[index - 1], stations[index]);
        var rightGradient = ComputeGradient(stations[index], stations[index + 1]);
        return 0.5d * (leftGradient + rightGradient);
    }

    private static double ComputeGradient(ViscousStationState start, ViscousStationState end)
    {
        var deltaXi = Math.Max(end.Xi - start.Xi, 1e-9);
        return (end.DisplacementThickness - start.DisplacementThickness) / deltaXi;
    }

    private static int MapIndex(int index, int sourceCount, int targetCount)
    {
        if (targetCount <= 1 || sourceCount <= 1)
        {
            return 0;
        }

        var fraction = (double)index / (sourceCount - 1);
        return Math.Clamp((int)Math.Round(fraction * (targetCount - 1)), 0, targetCount - 1);
    }

    private static IEnumerable<(ViscousStationSeed Initial, ViscousStationSeed Final)> PairStations(
        ViscousBranchSeed initial,
        ViscousBranchSeed final)
    {
        for (var index = 0; index < initial.Stations.Count; index++)
        {
            yield return (initial.Stations[index], final.Stations[index]);
        }
    }
}

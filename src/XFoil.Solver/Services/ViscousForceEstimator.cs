using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousForceEstimator
{
    public double EstimateProfileDragCoefficient(ViscousStateEstimate state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        return EstimateSurfaceBranchDrag(state.UpperSurface) + EstimateSurfaceBranchDrag(state.LowerSurface);
    }

    private static double EstimateSurfaceBranchDrag(ViscousBranchState branch)
    {
        if (branch is null)
        {
            throw new ArgumentNullException(nameof(branch));
        }

        if (branch.Branch == BoundaryLayerBranch.Wake || branch.Stations.Count < 2)
        {
            return 0d;
        }

        var drag = 0d;
        for (var index = 1; index < branch.Stations.Count; index++)
        {
            var start = branch.Stations[index - 1];
            var end = branch.Stations[index];
            var dx = end.Location.X - start.Location.X;
            var dy = end.Location.Y - start.Location.Y;
            var ds = Math.Sqrt((dx * dx) + (dy * dy));
            if (ds <= 1e-12)
            {
                continue;
            }

            var averageSkinFriction = 0.5d * (start.SkinFrictionCoefficient + end.SkinFrictionCoefficient);
            var streamwiseAlignment = Math.Abs(dx) / ds;
            drag += averageSkinFriction * ds * streamwiseAlignment;
        }

        return Math.Max(0d, drag);
    }
}

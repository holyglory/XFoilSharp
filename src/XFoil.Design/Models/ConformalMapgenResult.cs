using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class ConformalMapgenResult
{
    public ConformalMapgenResult(
        AirfoilGeometry geometry,
        IReadOnlyList<ConformalMappingCoefficient> coefficients,
        int circlePointCount,
        int iterationCount,
        bool converged,
        double maxCoefficientCorrection,
        double initialTrailingEdgeResidual,
        double finalTrailingEdgeResidual,
        AirfoilPoint targetTrailingEdgeGap,
        AirfoilPoint achievedTrailingEdgeGap,
        double targetTrailingEdgeAngleDegrees,
        double achievedTrailingEdgeAngleDegrees)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Coefficients = coefficients?.ToArray() ?? throw new ArgumentNullException(nameof(coefficients));
        CirclePointCount = circlePointCount;
        IterationCount = iterationCount;
        Converged = converged;
        MaxCoefficientCorrection = maxCoefficientCorrection;
        InitialTrailingEdgeResidual = initialTrailingEdgeResidual;
        FinalTrailingEdgeResidual = finalTrailingEdgeResidual;
        TargetTrailingEdgeGap = targetTrailingEdgeGap;
        AchievedTrailingEdgeGap = achievedTrailingEdgeGap;
        TargetTrailingEdgeAngleDegrees = targetTrailingEdgeAngleDegrees;
        AchievedTrailingEdgeAngleDegrees = achievedTrailingEdgeAngleDegrees;
    }

    public AirfoilGeometry Geometry { get; }

    public IReadOnlyList<ConformalMappingCoefficient> Coefficients { get; }

    public int CirclePointCount { get; }

    public int IterationCount { get; }

    public bool Converged { get; }

    public double MaxCoefficientCorrection { get; }

    public double InitialTrailingEdgeResidual { get; }

    public double FinalTrailingEdgeResidual { get; }

    public AirfoilPoint TargetTrailingEdgeGap { get; }

    public AirfoilPoint AchievedTrailingEdgeGap { get; }

    public double TargetTrailingEdgeAngleDegrees { get; }

    public double AchievedTrailingEdgeAngleDegrees { get; }
}

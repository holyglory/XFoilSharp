using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xmdes.f :: MAPGEN
// Role in port: Managed result object for conformal-mapping design execution.
// Differences: Legacy XFoil leaves the same outputs distributed across working arrays and state instead of packaging them into one immutable result.
// Decision: Keep the managed result object because it is the right API boundary for design services and has no direct legacy analogue.
namespace XFoil.Design.Models;

public sealed class ConformalMapgenResult
{
    // Legacy mapping: none; this constructor packages outputs from a MAPGEN-derived workflow that legacy XFoil leaves in mutable state.
    // Difference from legacy: The managed port validates and freezes the result payload instead of exposing raw working arrays.
    // Decision: Keep the constructor-based DTO because it is clearer for callers and tests.
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

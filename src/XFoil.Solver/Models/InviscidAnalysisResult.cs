// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: ALFA operating-point output
// Role in port: Managed immutable operating-point result for one inviscid solve, including pressures, circulation, and wake geometry.
// Differences: Legacy XFoil stores these outputs across solver arrays and report scalars, while the managed port packages them into one return object.
// Decision: Keep the managed result object because it is the clean API surface for the inviscid solver.
namespace XFoil.Solver.Models;

public sealed class InviscidAnalysisResult
{
    public InviscidAnalysisResult(
        int panelCount,
        double angleOfAttackDegrees,
        double machNumber,
        double circulation,
        double liftCoefficient,
        double dragCoefficient,
        double correctedPressureIntegratedLiftCoefficient,
        double correctedPressureIntegratedDragCoefficient,
        double pressureIntegratedLiftCoefficient,
        double pressureIntegratedDragCoefficient,
        double momentCoefficientQuarterChord,
        IReadOnlyList<double> sourceStrengths,
        double vortexStrength,
        IReadOnlyList<PressureCoefficientSample> pressureSamples,
        WakeGeometry wake)
    {
        PanelCount = panelCount;
        AngleOfAttackDegrees = angleOfAttackDegrees;
        MachNumber = machNumber;
        Circulation = circulation;
        LiftCoefficient = liftCoefficient;
        DragCoefficient = dragCoefficient;
        CorrectedPressureIntegratedLiftCoefficient = correctedPressureIntegratedLiftCoefficient;
        CorrectedPressureIntegratedDragCoefficient = correctedPressureIntegratedDragCoefficient;
        PressureIntegratedLiftCoefficient = pressureIntegratedLiftCoefficient;
        PressureIntegratedDragCoefficient = pressureIntegratedDragCoefficient;
        MomentCoefficientQuarterChord = momentCoefficientQuarterChord;
        SourceStrengths = sourceStrengths;
        VortexStrength = vortexStrength;
        PressureSamples = pressureSamples;
        Wake = wake;
    }

    public int PanelCount { get; }

    public double AngleOfAttackDegrees { get; }

    public double MachNumber { get; }

    public double Circulation { get; }

    public double LiftCoefficient { get; }

    public double DragCoefficient { get; }

    public double CorrectedPressureIntegratedLiftCoefficient { get; }

    public double CorrectedPressureIntegratedDragCoefficient { get; }

    public double PressureIntegratedLiftCoefficient { get; }

    public double PressureIntegratedDragCoefficient { get; }

    public double MomentCoefficientQuarterChord { get; }

    public IReadOnlyList<double> SourceStrengths { get; }

    public double VortexStrength { get; }

    public IReadOnlyList<PressureCoefficientSample> PressureSamples { get; }

    public WakeGeometry Wake { get; }
}

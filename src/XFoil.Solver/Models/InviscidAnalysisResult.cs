namespace XFoil.Solver.Models;

public sealed class InviscidAnalysisResult
{
    public InviscidAnalysisResult(
        PanelMesh mesh,
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
        Mesh = mesh;
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

    public PanelMesh Mesh { get; }

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

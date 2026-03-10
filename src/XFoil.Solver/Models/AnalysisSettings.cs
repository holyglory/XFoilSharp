namespace XFoil.Solver.Models;

public sealed class AnalysisSettings
{
    public AnalysisSettings(
        int panelCount = 120,
        double freestreamVelocity = 1d,
        double machNumber = 0d,
        double reynoldsNumber = 1_000_000d,
        PanelingOptions? paneling = null,
        double transitionReynoldsTheta = 320d,
        double criticalAmplificationFactor = 9.0d)
    {
        if (panelCount < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(panelCount), "Panel count must be at least 16.");
        }

        if (freestreamVelocity <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(freestreamVelocity), "Freestream velocity must be positive.");
        }

        if (machNumber < 0d || machNumber >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(machNumber), "Mach number must be in the range [0, 1).");
        }

        if (reynoldsNumber <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(reynoldsNumber), "Reynolds number must be positive.");
        }

        if (transitionReynoldsTheta <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(transitionReynoldsTheta), "Transition Reynolds-theta threshold must be positive.");
        }

        if (criticalAmplificationFactor <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(criticalAmplificationFactor), "Critical amplification factor must be positive.");
        }

        PanelCount = panelCount;
        FreestreamVelocity = freestreamVelocity;
        MachNumber = machNumber;
        ReynoldsNumber = reynoldsNumber;
        Paneling = paneling ?? new PanelingOptions();
        TransitionReynoldsTheta = transitionReynoldsTheta;
        CriticalAmplificationFactor = criticalAmplificationFactor;
    }

    public int PanelCount { get; }

    public double FreestreamVelocity { get; }

    public double MachNumber { get; }

    public double ReynoldsNumber { get; }

    public PanelingOptions Paneling { get; }

    public double TransitionReynoldsTheta { get; }

    public double CriticalAmplificationFactor { get; }
}

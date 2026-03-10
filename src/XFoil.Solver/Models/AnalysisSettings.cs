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
        double criticalAmplificationFactor = 9.0d,
        InviscidSolverType inviscidSolverType = InviscidSolverType.HessSmith,
        ViscousSolverMode viscousSolverMode = ViscousSolverMode.TrustRegion,
        double? forcedTransitionUpper = null,
        double? forcedTransitionLower = null,
        bool useExtendedWake = true,
        bool useModernTransitionCorrections = true,
        int maxViscousIterations = 50,
        double viscousConvergenceTolerance = 1e-6,
        double? nCritUpper = null,
        double? nCritLower = null,
        bool usePostStallExtrapolation = false)
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

        if (maxViscousIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxViscousIterations), "Max viscous iterations must be at least 1.");
        }

        if (viscousConvergenceTolerance <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(viscousConvergenceTolerance), "Viscous convergence tolerance must be positive.");
        }

        PanelCount = panelCount;
        FreestreamVelocity = freestreamVelocity;
        MachNumber = machNumber;
        ReynoldsNumber = reynoldsNumber;
        Paneling = paneling ?? new PanelingOptions();
        TransitionReynoldsTheta = transitionReynoldsTheta;
        CriticalAmplificationFactor = criticalAmplificationFactor;
        InviscidSolverType = inviscidSolverType;
        ViscousSolverMode = viscousSolverMode;
        ForcedTransitionUpper = forcedTransitionUpper;
        ForcedTransitionLower = forcedTransitionLower;
        UseExtendedWake = useExtendedWake;
        UseModernTransitionCorrections = useModernTransitionCorrections;
        MaxViscousIterations = maxViscousIterations;
        ViscousConvergenceTolerance = viscousConvergenceTolerance;
        NCritUpper = nCritUpper;
        NCritLower = nCritLower;
        UsePostStallExtrapolation = usePostStallExtrapolation;
    }

    public int PanelCount { get; }

    public double FreestreamVelocity { get; }

    public double MachNumber { get; }

    public double ReynoldsNumber { get; }

    public PanelingOptions Paneling { get; }

    public double TransitionReynoldsTheta { get; }

    public double CriticalAmplificationFactor { get; }

    /// <summary>
    /// Selects which inviscid solver to use for the analysis.
    /// Default is <see cref="Models.InviscidSolverType.HessSmith"/> to preserve backward compatibility.
    /// </summary>
    public InviscidSolverType InviscidSolverType { get; }

    /// <summary>
    /// Selects which viscous Newton solver strategy to use.
    /// Default is <see cref="Models.ViscousSolverMode.TrustRegion"/>.
    /// </summary>
    public ViscousSolverMode ViscousSolverMode { get; }

    /// <summary>
    /// Forced transition location on the upper surface (x/c).
    /// Null for free transition (e^N method).
    /// If the forced location is downstream of the natural transition, free transition is used.
    /// </summary>
    public double? ForcedTransitionUpper { get; }

    /// <summary>
    /// Forced transition location on the lower surface (x/c).
    /// Null for free transition (e^N method).
    /// </summary>
    public double? ForcedTransitionLower { get; }

    /// <summary>
    /// When true, wake is marched until convergence (dtheta/dxi &lt; epsilon).
    /// When false, uses XFoil's fixed-length wake (for Phase 4 verification).
    /// </summary>
    public bool UseExtendedWake { get; }

    /// <summary>
    /// When true, applies modern database corrections to the e^N transition model.
    /// When false, uses original XFoil correlations (for Phase 4 verification).
    /// </summary>
    public bool UseModernTransitionCorrections { get; }

    /// <summary>
    /// Maximum number of Newton iterations for the viscous solver.
    /// Default is 50 for trust-region mode; recommended 20 for XFoil relaxation mode.
    /// </summary>
    public int MaxViscousIterations { get; }

    /// <summary>
    /// Convergence tolerance for the viscous solver (RMS residual threshold).
    /// Default is 1e-6 for trust-region mode.
    /// </summary>
    public double ViscousConvergenceTolerance { get; }

    /// <summary>
    /// Per-surface critical amplification factor for the upper surface.
    /// When null, uses the global <see cref="CriticalAmplificationFactor"/>.
    /// </summary>
    public double? NCritUpper { get; }

    /// <summary>
    /// Per-surface critical amplification factor for the lower surface.
    /// When null, uses the global <see cref="CriticalAmplificationFactor"/>.
    /// </summary>
    public double? NCritLower { get; }

    /// <summary>
    /// When true, enables Viterna-Corrigan post-stall extrapolation.
    /// Default is false (reports only converged results).
    /// </summary>
    public bool UsePostStallExtrapolation { get; }

    /// <summary>
    /// Returns the effective NCrit for the given side.
    /// Uses per-surface override if set, otherwise falls back to the global CriticalAmplificationFactor.
    /// </summary>
    /// <param name="side">0 for upper surface, 1 for lower surface.</param>
    /// <returns>The effective NCrit value.</returns>
    public double GetEffectiveNCrit(int side)
    {
        return side switch
        {
            0 => NCritUpper ?? CriticalAmplificationFactor,
            1 => NCritLower ?? CriticalAmplificationFactor,
            _ => CriticalAmplificationFactor
        };
    }
}

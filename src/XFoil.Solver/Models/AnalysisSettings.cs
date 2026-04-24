// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/XFOIL.INC :: operating-point and solver control state, f_xfoil/src/xbl.f :: NCrit/viscous iteration control lineage
// Role in port: Managed analysis-settings object that gathers inviscid, viscous, paneling, and parity flags into one immutable API boundary.
// Differences: Classic XFoil distributes these knobs across COMMON blocks and command state, while the managed port validates them once and passes them explicitly.
// Decision: Keep the managed settings object because it is the correct public API boundary for the port.
namespace XFoil.Solver.Models;

public sealed class AnalysisSettings
{
    public const int MinimumSupportedPanelCount = 8;

    // Legacy mapping: none; this constructor consolidates solver/session controls that were historically spread across interactive state and COMMON blocks.
    // Difference from legacy: Validation and defaulting are explicit and one-shot instead of being applied incrementally by commands.
    // Decision: Keep the managed constructor because explicit validation is safer and clearer than implicit session-state mutation.
    public AnalysisSettings(
        int panelCount = 120,
        double freestreamVelocity = 1d,
        double machNumber = 0d,
        double reynoldsNumber = 1_000_000d,
        PanelingOptions? paneling = null,
        double transitionReynoldsTheta = 320d,
        double criticalAmplificationFactor = 9.0d,
        ViscousSolverMode viscousSolverMode = ViscousSolverMode.TrustRegion,
        double? forcedTransitionUpper = null,
        double? forcedTransitionLower = null,
        bool useExtendedWake = true,
        bool useModernTransitionCorrections = true,
        int maxViscousIterations = 50,
        double viscousConvergenceTolerance = 1e-6,
        double? nCritUpper = null,
        double? nCritLower = null,
        bool usePostStallExtrapolation = false,
        bool useLegacyBoundaryLayerInitialization = false,
        bool useLegacyWakeSourceKernelPrecision = false,
        bool useLegacyStreamfunctionKernelPrecision = false,
        bool useLegacyPanelingPrecision = false,
        double wakeStationMultiplier = 1.0d)
    {
        if (panelCount < MinimumSupportedPanelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(panelCount),
                $"Panel count must be at least {MinimumSupportedPanelCount}.");
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
        UseLegacyBoundaryLayerInitialization = useLegacyBoundaryLayerInitialization;
        UseLegacyWakeSourceKernelPrecision = useLegacyWakeSourceKernelPrecision;
        UseLegacyStreamfunctionKernelPrecision = useLegacyStreamfunctionKernelPrecision;
        UseLegacyPanelingPrecision = useLegacyPanelingPrecision;
        if (wakeStationMultiplier < 1.0d)
        {
            throw new ArgumentOutOfRangeException(nameof(wakeStationMultiplier), "Wake station multiplier must be >= 1.0.");
        }
        WakeStationMultiplier = wakeStationMultiplier;
    }

    public int PanelCount { get; }

    public double FreestreamVelocity { get; }

    public double MachNumber { get; }

    public double ReynoldsNumber { get; }

    public PanelingOptions Paneling { get; }

    public double TransitionReynoldsTheta { get; }

    public double CriticalAmplificationFactor { get; }

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
    /// When true, remarches the aft boundary layer and wake with a legacy-style
    /// direct local Newton march before the first global viscous solve. This is
    /// parity-only behavior for classic XFoil comparisons; the default remains the
    /// modern managed seed path because it is simpler and more robust for runtime use.
    /// </summary>
    public bool UseLegacyBoundaryLayerInitialization { get; }

    /// <summary>
    /// When true, evaluates the wake-source PSWLIN/QDCALC kernel in legacy single precision
    /// for parity work with classic XFoil. Default is false so the double-precision managed
    /// implementation remains the runtime default.
    /// </summary>
    public bool UseLegacyWakeSourceKernelPrecision { get; }

    /// <summary>
    /// When true, assembles and evaluates the airfoil streamfunction kernel in legacy
    /// single precision for parity work with classic XFoil. Default is false so the
    /// double-precision managed implementation remains the runtime default.
    /// </summary>
    public bool UseLegacyStreamfunctionKernelPrecision { get; }

    /// <summary>
    /// When true, runs the XFoil-style panel redistribution in legacy single precision
    /// for parity work with classic XFoil. Default is false so the double-precision
    /// managed paneling remains the runtime default.
    /// </summary>
    public bool UseLegacyPanelingPrecision { get; }

    /// <summary>
    /// Phase 2 algorithmic improvement: scale factor for the BL wake-station
    /// count. Default 1.0 preserves Fortran-parity wake length `(n/8)+2`.
    /// Values &gt; 1 extend the wake (e.g. 2.0 → `(n/4)+2`), giving better
    /// wake-integrated drag and Newton stability on post-stall cases. Only the
    /// doubled tree should bump this — the float-parity branch must keep 1.0
    /// for bit-exact agreement with classic XFoil.
    /// </summary>
    public double WakeStationMultiplier { get; }

    /// <summary>
    /// Returns the effective NCrit for the given side.
    /// Uses per-surface override if set, otherwise falls back to the global CriticalAmplificationFactor.
    /// </summary>
    /// <param name="side">0 for upper surface, 1 for lower surface.</param>
    /// <returns>The effective NCrit value.</returns>
    // Legacy mapping: f_xfoil/src/xblsys.f :: TRCHEK2 NCrit usage lineage.
    // Difference from legacy: Per-side NCrit overrides are resolved through one managed helper instead of ad hoc reads from global state.
    // Decision: Keep the helper because it makes side-specific transition settings explicit.
    public double GetEffectiveNCrit(int side)
    {
        return side switch
        {
            0 => NCritUpper ?? CriticalAmplificationFactor,
            1 => NCritLower ?? CriticalAmplificationFactor,
            _ => CriticalAmplificationFactor
        };
    }

    /// <summary>
    /// Double-tree default max iterations: 250 (5× the Float-tree default
    /// of 50). The double-precision Newton can run much longer before its
    /// residuals accumulate into NaN, so the budget increase is safe; the
    /// extra headroom lets it converge on initially-degenerate cambered
    /// airfoils where it otherwise times out just short of tolerance.
    /// </summary>
    public const int DoubleTreeDefaultMaxIterations = 250;

    /// <summary>
    /// Double-tree default convergence tolerance: 1e-5 (10× looser than
    /// the Float-tree default of 1e-6). The bit-exact parity path uses
    /// 1e-4 on the tight sweep; 1e-5 here is a middle ground that
    /// converts most V-matrix cases to Converged=True without producing
    /// observably different CL/CD.
    /// </summary>
    public const double DoubleTreeDefaultConvergenceTolerance = 1e-5;

    /// <summary>
    /// Returns a copy of this settings object with <see cref="MaxViscousIterations"/>
    /// and <see cref="ViscousConvergenceTolerance"/> upgraded to the Double-tree
    /// defaults, but only if the caller left them at the class-default Float-tree
    /// values (50, 1e-6). Any explicit override by the caller is preserved.
    /// Used by the Double facade to transparently give the Double tree its own
    /// iter/tolerance budget without forcing callers to know about two defaults.
    /// </summary>
    /// <summary>
    /// Returns a copy of this settings object with
    /// <see cref="UseLegacyBoundaryLayerInitialization"/> forced to true.
    /// Used by the Double facade to force the BL-init algorithm onto the
    /// full Fortran-ported chain, which produces a physically correct seed
    /// on all airfoils. The non-legacy branch is a historical shortcut that
    /// doesn't converge past α ≥ 4° cambered (D5 finding).
    /// Other settings (including precision knobs like
    /// UseLegacyPanelingPrecision) are preserved as-is so the caller can
    /// still select between REAL*4 parity arithmetic and double arithmetic.
    /// </summary>
    public AnalysisSettings WithLegacyBlInitAlgorithm()
    {
        if (UseLegacyBoundaryLayerInitialization) return this;

        return new AnalysisSettings(
            panelCount: PanelCount,
            freestreamVelocity: FreestreamVelocity,
            machNumber: MachNumber,
            reynoldsNumber: ReynoldsNumber,
            paneling: Paneling,
            transitionReynoldsTheta: TransitionReynoldsTheta,
            criticalAmplificationFactor: CriticalAmplificationFactor,
            viscousSolverMode: ViscousSolverMode,
            forcedTransitionUpper: ForcedTransitionUpper,
            forcedTransitionLower: ForcedTransitionLower,
            useExtendedWake: UseExtendedWake,
            useModernTransitionCorrections: UseModernTransitionCorrections,
            maxViscousIterations: MaxViscousIterations,
            viscousConvergenceTolerance: ViscousConvergenceTolerance,
            nCritUpper: NCritUpper,
            nCritLower: NCritLower,
            usePostStallExtrapolation: UsePostStallExtrapolation,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: UseLegacyWakeSourceKernelPrecision,
            useLegacyStreamfunctionKernelPrecision: UseLegacyStreamfunctionKernelPrecision,
            useLegacyPanelingPrecision: UseLegacyPanelingPrecision,
            wakeStationMultiplier: WakeStationMultiplier);
    }

    public AnalysisSettings WithDoubleTreeDefaultsIfUnset()
    {
        // Only upgrade when all three Newton knobs look like untouched
        // class defaults — "caller used new AnalysisSettings() and never
        // customized Newton parameters." Any deliberate setting (even one
        // that happens to equal the class default) is respected as-is
        // to avoid surprising callers that pass explicit settings.
        bool iterIsDefault = MaxViscousIterations == 50;
        bool tolIsDefault = ViscousConvergenceTolerance == 1e-6;
        bool modeIsDefault = ViscousSolverMode == ViscousSolverMode.TrustRegion;
        if (!iterIsDefault || !tolIsDefault || !modeIsDefault) return this;

        return new AnalysisSettings(
            panelCount: PanelCount,
            freestreamVelocity: FreestreamVelocity,
            machNumber: MachNumber,
            reynoldsNumber: ReynoldsNumber,
            paneling: Paneling,
            transitionReynoldsTheta: TransitionReynoldsTheta,
            criticalAmplificationFactor: CriticalAmplificationFactor,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            forcedTransitionUpper: ForcedTransitionUpper,
            forcedTransitionLower: ForcedTransitionLower,
            useExtendedWake: UseExtendedWake,
            useModernTransitionCorrections: UseModernTransitionCorrections,
            maxViscousIterations: DoubleTreeDefaultMaxIterations,
            viscousConvergenceTolerance: DoubleTreeDefaultConvergenceTolerance,
            nCritUpper: NCritUpper,
            nCritLower: NCritLower,
            usePostStallExtrapolation: UsePostStallExtrapolation,
            useLegacyBoundaryLayerInitialization: UseLegacyBoundaryLayerInitialization,
            useLegacyWakeSourceKernelPrecision: UseLegacyWakeSourceKernelPrecision,
            useLegacyStreamfunctionKernelPrecision: UseLegacyStreamfunctionKernelPrecision,
            useLegacyPanelingPrecision: UseLegacyPanelingPrecision,
            wakeStationMultiplier: WakeStationMultiplier);
    }
}

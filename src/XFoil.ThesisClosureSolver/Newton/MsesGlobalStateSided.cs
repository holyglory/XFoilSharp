namespace XFoil.ThesisClosureSolver.Newton;

/// <summary>
/// R5.4 — Per-side global state for the reshaped Phase 5 Newton
/// system. Replaces the R5.0 ThesisClosureGlobalState (which used a single
/// BL station block) with a layout that mirrors physical topology:
///
///   [ γ_0..γ_N                    (N+1)    inviscid vortex strengths
///   | σ_airfoil_0..σ_N             (N+1)    airfoil source strengths
///   | σ_wake_0..σ_{Nw-1}            (Nw)    wake source strengths
///   | δ*_upper, θ_upper, Cτ_upper   (3·Nu)  upper-surface BL state
///   | δ*_lower, θ_lower, Cτ_lower   (3·Nl)  lower-surface BL state
///   | δ*_wake,  θ_wake,  Cτ_wake    (3·Nw)  wake BL state          ]
///
/// The original ThesisClosureGlobalState is kept alongside for the existing
/// P4.6 γ-only gate test.
/// </summary>
public sealed class ThesisClosureGlobalStateSided
{
    public int GammaCount { get; }
    public int SigmaAirfoilCount { get; }
    public int SigmaWakeCount { get; }
    public int UpperCount { get; }
    public int LowerCount { get; }
    public int WakeCount { get; }

    public int GammaOffset => 0;
    public int SigmaAirfoilOffset => GammaCount;
    public int SigmaWakeOffset => SigmaAirfoilOffset + SigmaAirfoilCount;
    public int UpperDstarOffset => SigmaWakeOffset + SigmaWakeCount;
    public int UpperThetaOffset => UpperDstarOffset + UpperCount;
    public int UpperCTauOffset => UpperThetaOffset + UpperCount;
    public int LowerDstarOffset => UpperCTauOffset + UpperCount;
    public int LowerThetaOffset => LowerDstarOffset + LowerCount;
    public int LowerCTauOffset => LowerThetaOffset + LowerCount;
    public int WakeDstarOffset => LowerCTauOffset + LowerCount;
    public int WakeThetaOffset => WakeDstarOffset + WakeCount;
    public int WakeCTauOffset => WakeThetaOffset + WakeCount;
    public int StateSize =>
        GammaCount + SigmaAirfoilCount + SigmaWakeCount
        + 3 * (UpperCount + LowerCount + WakeCount);

    public ThesisClosureGlobalStateSided(
        int gammaCount,
        int sigmaAirfoilCount,
        int sigmaWakeCount,
        int upperCount,
        int lowerCount,
        int wakeCount)
    {
        if (gammaCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(gammaCount));
        if (sigmaAirfoilCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(sigmaAirfoilCount));
        if (sigmaWakeCount < 0) throw new System.ArgumentOutOfRangeException(nameof(sigmaWakeCount));
        if (upperCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(upperCount));
        if (lowerCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(lowerCount));
        if (wakeCount < 0) throw new System.ArgumentOutOfRangeException(nameof(wakeCount));
        GammaCount = gammaCount;
        SigmaAirfoilCount = sigmaAirfoilCount;
        SigmaWakeCount = sigmaWakeCount;
        UpperCount = upperCount;
        LowerCount = lowerCount;
        WakeCount = wakeCount;
    }

    public readonly record struct SidedState(
        double[] Gamma,
        double[] SigmaAirfoil,
        double[] SigmaWake,
        double[] UpperDstar, double[] UpperTheta, double[] UpperCTau,
        double[] LowerDstar, double[] LowerTheta, double[] LowerCTau,
        double[] WakeDstar,  double[] WakeTheta,  double[] WakeCTau);

    public double[] Pack(SidedState s)
    {
        Validate(s);
        var packed = new double[StateSize];
        System.Array.Copy(s.Gamma,        0, packed, GammaOffset,        GammaCount);
        System.Array.Copy(s.SigmaAirfoil, 0, packed, SigmaAirfoilOffset, SigmaAirfoilCount);
        System.Array.Copy(s.SigmaWake,    0, packed, SigmaWakeOffset,    SigmaWakeCount);
        System.Array.Copy(s.UpperDstar,   0, packed, UpperDstarOffset,   UpperCount);
        System.Array.Copy(s.UpperTheta,   0, packed, UpperThetaOffset,   UpperCount);
        System.Array.Copy(s.UpperCTau,    0, packed, UpperCTauOffset,    UpperCount);
        System.Array.Copy(s.LowerDstar,   0, packed, LowerDstarOffset,   LowerCount);
        System.Array.Copy(s.LowerTheta,   0, packed, LowerThetaOffset,   LowerCount);
        System.Array.Copy(s.LowerCTau,    0, packed, LowerCTauOffset,    LowerCount);
        System.Array.Copy(s.WakeDstar,    0, packed, WakeDstarOffset,    WakeCount);
        System.Array.Copy(s.WakeTheta,    0, packed, WakeThetaOffset,    WakeCount);
        System.Array.Copy(s.WakeCTau,     0, packed, WakeCTauOffset,     WakeCount);
        return packed;
    }

    public SidedState Unpack(double[] state)
    {
        if (state.Length != StateSize)
            throw new System.ArgumentException(
                $"state length {state.Length} ≠ expected {StateSize}");
        var g  = new double[GammaCount];
        var sa = new double[SigmaAirfoilCount];
        var sw = new double[SigmaWakeCount];
        var ud = new double[UpperCount]; var ut = new double[UpperCount]; var uc = new double[UpperCount];
        var ld = new double[LowerCount]; var lt = new double[LowerCount]; var lc = new double[LowerCount];
        var wd = new double[WakeCount];  var wt = new double[WakeCount];  var wc = new double[WakeCount];
        System.Array.Copy(state, GammaOffset,        g,  0, GammaCount);
        System.Array.Copy(state, SigmaAirfoilOffset, sa, 0, SigmaAirfoilCount);
        System.Array.Copy(state, SigmaWakeOffset,    sw, 0, SigmaWakeCount);
        System.Array.Copy(state, UpperDstarOffset,   ud, 0, UpperCount);
        System.Array.Copy(state, UpperThetaOffset,   ut, 0, UpperCount);
        System.Array.Copy(state, UpperCTauOffset,    uc, 0, UpperCount);
        System.Array.Copy(state, LowerDstarOffset,   ld, 0, LowerCount);
        System.Array.Copy(state, LowerThetaOffset,   lt, 0, LowerCount);
        System.Array.Copy(state, LowerCTauOffset,    lc, 0, LowerCount);
        System.Array.Copy(state, WakeDstarOffset,    wd, 0, WakeCount);
        System.Array.Copy(state, WakeThetaOffset,    wt, 0, WakeCount);
        System.Array.Copy(state, WakeCTauOffset,     wc, 0, WakeCount);
        return new SidedState(g, sa, sw, ud, ut, uc, ld, lt, lc, wd, wt, wc);
    }

    private void Validate(SidedState s)
    {
        if (s.Gamma.Length != GammaCount) throw new System.ArgumentException("γ length mismatch");
        if (s.SigmaAirfoil.Length != SigmaAirfoilCount) throw new System.ArgumentException("σ_airfoil length mismatch");
        if (s.SigmaWake.Length != SigmaWakeCount) throw new System.ArgumentException("σ_wake length mismatch");
        if (s.UpperDstar.Length != UpperCount || s.UpperTheta.Length != UpperCount || s.UpperCTau.Length != UpperCount)
            throw new System.ArgumentException("upper BL length mismatch");
        if (s.LowerDstar.Length != LowerCount || s.LowerTheta.Length != LowerCount || s.LowerCTau.Length != LowerCount)
            throw new System.ArgumentException("lower BL length mismatch");
        if (s.WakeDstar.Length != WakeCount || s.WakeTheta.Length != WakeCount || s.WakeCTau.Length != WakeCount)
            throw new System.ArgumentException("wake BL length mismatch");
    }
}

namespace XFoil.MsesSolver.Newton;

/// <summary>
/// P4.1 — Global state vector for the MSES viscous-inviscid couple.
/// Phase 5 per Drela thesis §6 requires a single Newton solve
/// over the combined system of:
///
///   γ  (vortex strengths at panel nodes)        N+1 values
///   σ  (source strengths at panel nodes)        N+1 values
///   δ* (displacement thickness per BL station)  N_bl values
///   θ  (momentum thickness per BL station)      N_bl values
///   Cτ (max shear-stress coefficient per stn)   N_bl values
///
/// This class owns layout, pack/unpack, and index metadata. The
/// actual residual assembler (P4.2) and Newton loop (P4.4) consume
/// these helpers so that layout changes (e.g. adding wake stations
/// in P6) stay in one place.
///
/// Layout:
///   offset 0            .. GammaCount-1                     γ_0..γ_N
///   offset GammaOffset+GammaCount ... SigmaOffset+SigmaCount-1
///                                                           σ_0..σ_N
///   offset DstarOffset ...                                  δ*_0..
///   offset ThetaOffset ...                                  θ_0..
///   offset CTauOffset  ...                                  Cτ_0..
/// </summary>
public sealed class MsesGlobalState
{
    public int GammaCount { get; }
    public int SigmaCount { get; }
    public int BLStationCount { get; }

    public int GammaOffset => 0;
    public int SigmaOffset => GammaCount;
    public int DstarOffset => GammaCount + SigmaCount;
    public int ThetaOffset => DstarOffset + BLStationCount;
    public int CTauOffset => ThetaOffset + BLStationCount;
    public int StateSize => GammaCount + SigmaCount + 3 * BLStationCount;

    /// <summary>
    /// Constructs the layout metadata. Pass γ and σ node counts
    /// separately to keep the layout future-proof against designs
    /// that discretize them differently.
    /// </summary>
    public MsesGlobalState(int gammaCount, int sigmaCount, int blStationCount)
    {
        if (gammaCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(gammaCount));
        if (sigmaCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(sigmaCount));
        if (blStationCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(blStationCount));
        GammaCount = gammaCount;
        SigmaCount = sigmaCount;
        BLStationCount = blStationCount;
    }

    /// <summary>
    /// Concatenates γ, σ, δ*, θ, Cτ into a single state vector.
    /// </summary>
    public double[] Pack(
        double[] gamma, double[] sigma,
        double[] dStar, double[] theta, double[] cTau)
    {
        if (gamma.Length != GammaCount) throw new System.ArgumentException("γ length mismatch");
        if (sigma.Length != SigmaCount) throw new System.ArgumentException("σ length mismatch");
        if (dStar.Length != BLStationCount) throw new System.ArgumentException("δ* length mismatch");
        if (theta.Length != BLStationCount) throw new System.ArgumentException("θ length mismatch");
        if (cTau.Length != BLStationCount) throw new System.ArgumentException("Cτ length mismatch");

        var state = new double[StateSize];
        System.Array.Copy(gamma, 0, state, GammaOffset, GammaCount);
        System.Array.Copy(sigma, 0, state, SigmaOffset, SigmaCount);
        System.Array.Copy(dStar, 0, state, DstarOffset, BLStationCount);
        System.Array.Copy(theta, 0, state, ThetaOffset, BLStationCount);
        System.Array.Copy(cTau, 0, state, CTauOffset, BLStationCount);
        return state;
    }

    /// <summary>
    /// Splits a packed state vector into its γ, σ, δ*, θ, Cτ slices.
    /// </summary>
    public (double[] Gamma, double[] Sigma, double[] DStar, double[] Theta, double[] CTau)
        Unpack(double[] state)
    {
        if (state.Length != StateSize)
            throw new System.ArgumentException(
                $"state length {state.Length} ≠ expected {StateSize}");
        var gamma = new double[GammaCount];
        var sigma = new double[SigmaCount];
        var dStar = new double[BLStationCount];
        var theta = new double[BLStationCount];
        var cTau = new double[BLStationCount];
        System.Array.Copy(state, GammaOffset, gamma, 0, GammaCount);
        System.Array.Copy(state, SigmaOffset, sigma, 0, SigmaCount);
        System.Array.Copy(state, DstarOffset, dStar, 0, BLStationCount);
        System.Array.Copy(state, ThetaOffset, theta, 0, BLStationCount);
        System.Array.Copy(state, CTauOffset, cTau, 0, BLStationCount);
        return (gamma, sigma, dStar, theta, cTau);
    }

    /// <summary>
    /// Classifies a state-vector index into one of the variable
    /// categories. Useful for per-class scaling and residual
    /// debugging.
    /// </summary>
    public enum VarKind { Gamma, Sigma, DStar, Theta, CTau }

    public VarKind Kind(int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= StateSize)
            throw new System.ArgumentOutOfRangeException(nameof(stateIndex));
        if (stateIndex < SigmaOffset) return VarKind.Gamma;
        if (stateIndex < DstarOffset) return VarKind.Sigma;
        if (stateIndex < ThetaOffset) return VarKind.DStar;
        if (stateIndex < CTauOffset) return VarKind.Theta;
        return VarKind.CTau;
    }
}

// Auto-generated extraction of nested public/internal classes from
// BoundaryLayerSystemAssembler.cs into top-level classes in the same
// XFoil.Solver.Services namespace. Phase 1 of the float→double tree split
// requires these data classes to be top-level (not nested) so that
// BoundaryLayerSystemAssembler.Double.cs (auto-generated *.Double.cs twin)
// can reference them via short name without conflicting with the doubled-
// namespace nested-type collision.
//
// Do not nest these back into BoundaryLayerSystemAssembler — that breaks
// gen-double's BLSA mirror.
namespace XFoil.Solver.Services;

internal sealed class BldifEq2Inputs
{
    public int Itype;
    public double X1, X2;
    public double U1, U2;
    public double T1, T2;
    public double Dw1, Dw2;
    public double H1, H1_T1, H1_D1;
    public double H2, H2_T2, H2_D2;
    public double M1, M1_U1;
    public double M2, M2_U2;
    public double Cfm, Cfm_T1, Cfm_D1, Cfm_U1, Cfm_T2, Cfm_D2, Cfm_U2;
    public double Cf1, Cf1_T1, Cf1_D1, Cf1_U1;
    public double Cf2, Cf2_T2, Cf2_D2, Cf2_U2;
    public double XLog, ULog, TLog, DdLog;
    public bool UseLegacyPrecision;
    public int TraceSide, TraceStation, TraceIteration;
}

internal sealed class BldifEq2Result
{
    public double Residual;
    public double Ha, Ma, Xa, Ta, Hwa;
    public double CfxCenter, CfxPanels, Cfx, Btmp;
    public double CfxCfm, CfxCf1, CfxCf2, CfxT2;
    public double CfxX1, CfxX2;
    public double ZCfx, ZHa, ZHwa, ZMa, ZXl, ZUl, ZTl;
    public double ZCfm, ZCf1, ZCf2;
    public double ZT1, ZT2, ZX1XlogTerm, ZX1CfxTerm, ZX1, ZX2XlogTerm, ZX2CfxTerm, ZX2, ZU1, ZU2;
    public double VS1_22, VS1_23, VS1_24, VS1_X;
    public double VS2_22, VS2_23, VS2_24, VS2_X;
}

public class KinematicResult
{
    public double M2, M2_U2, M2_MS;
    public double R2, R2_U2, R2_MS;
    public double H2, H2_D2, H2_T2;
    public double HK2, HK2_U2, HK2_T2, HK2_D2, HK2_MS;
    public double RT2, RT2_U2, RT2_T2, RT2_MS, RT2_RE;
    /// <summary>
    /// The stripped D2 (D-DW) that was passed to ComputeKinematicParameters.
    /// Used by the COM carry mechanism to provide consistent d1 at the next station.
    /// </summary>
    public double InputD2;
    /// <summary>The T2 that was passed to ComputeKinematicParameters.</summary>
    public double InputT2;

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only snapshot helper; the Fortran code kept these values in shared arrays instead of cloning them into an object.
    // Decision: Keep the clone helper because parity debugging needs stable copies of pre-update state.
    public KinematicResult Clone()
    {
        var clone = new KinematicResult();
        clone.CopyFrom(this);
        return clone;
    }

    /// <summary>
    /// Copy all fields from <paramref name="source"/> into this instance,
    /// used by ThreadStatic-pooled callers that want Clone's snapshot
    /// semantics without the per-call heap allocation.
    /// </summary>
    public void CopyFrom(KinematicResult source)
    {
        M2 = source.M2;
        M2_U2 = source.M2_U2;
        M2_MS = source.M2_MS;
        R2 = source.R2;
        R2_U2 = source.R2_U2;
        R2_MS = source.R2_MS;
        H2 = source.H2;
        H2_D2 = source.H2_D2;
        H2_T2 = source.H2_T2;
        HK2 = source.HK2;
        HK2_U2 = source.HK2_U2;
        HK2_T2 = source.HK2_T2;
        HK2_D2 = source.HK2_D2;
        HK2_MS = source.HK2_MS;
        RT2 = source.RT2;
        RT2_U2 = source.RT2_U2;
        RT2_T2 = source.RT2_T2;
        RT2_MS = source.RT2_MS;
        RT2_RE = source.RT2_RE;
        InputD2 = source.InputD2;
        InputT2 = source.InputT2;
    }
}

public class PrimaryStationState
{
    public double U, T, D;
    /// <summary>
    /// Pre-Newton-update T/D for MRCHUE COM carry. Fortran COM2.D2/T2
    /// are set by BLPRV at the START of each Newton iteration (pre-update).
    /// These fields are only set when the station's Newton runs multiple
    /// iterations (pre != post). When null/zero, use D/T instead.
    /// </summary>
    public double? PreUpdateT, PreUpdateD;
    /// <summary>
    /// Full pre-update DSTR (including wake gap) for MRCHUE COM carry.
    /// Fortran COM1.D1 carries DSI_pre_update - DSWAKI; managed callers
    /// need the full pre-update DSI to pass d1 + dw1 consistently.
    /// </summary>
    public double? PreUpdateDFull;
    public PrimaryStationState Clone()
    {
        var clone = new PrimaryStationState();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(PrimaryStationState source)
    {
        U = source.U;
        T = source.T;
        D = source.D;
        PreUpdateT = source.PreUpdateT;
        PreUpdateD = source.PreUpdateD;
        PreUpdateDFull = source.PreUpdateDFull;
    }
}

public class StationVariables
{
    public double Cf, Hs, Di, Cteq, Us, De, Hc;
}

public class MidpointResult
{
    public double Cfm, Cfm_Hka, Cfm_Rta, Cfm_Ma;
}

internal sealed class BldifLogTerms
{
    public double XLog;
    public double ULog;
    public double TLog;
    public double HLog;
    public double DdLog;
    public double XRatio;
    public double URatio;
    public double TRatio;
    public double HRatio;
}

public class BldifResult
{
    // Fixed-size Jacobian/residual blocks; allocated once per instance.
    // When an instance is reused via SolverBuffers pooling, the arrays
    // are zeroed in ResetForReuse() rather than reallocated.
    public double[] Residual = new double[3];
    public double[,] VS1 = new double[3, 5]; // 3x5 Jacobian block for station 1
    public double[,] VS2 = new double[3, 5]; // 3x5 Jacobian block for station 2
    /// <summary>
    /// Arc-length sensitivity VSX(3) from TRDIF transition interval.
    /// Non-zero only at the transition station. Set by BTX computation.
    /// </summary>
    public double[] VSX = new double[3];
    public KinematicResult? CarryKinematicSnapshot;
    public SecondaryStationResult? Secondary2Snapshot;

    // Pre-allocated storage for the two snapshot refs above. The public
    // nullable fields alias these slots when live and are set to null
    // on ResetForReuse, matching the Clone-based semantics without the
    // per-call heap allocation.
    private readonly KinematicResult _carryKinematicStorage = new();
    private readonly SecondaryStationResult _secondaryStorage = new();

    internal void SetCarryKinematicSnapshot(KinematicResult source)
    {
        _carryKinematicStorage.CopyFrom(source);
        CarryKinematicSnapshot = _carryKinematicStorage;
    }

    internal void SetSecondary2Snapshot(SecondaryStationResult? source)
    {
        if (source is null)
        {
            Secondary2Snapshot = null;
            return;
        }
        if (!ReferenceEquals(source, _secondaryStorage))
        {
            _secondaryStorage.CopyFrom(source);
        }
        Secondary2Snapshot = _secondaryStorage;
    }

    /// <summary>
    /// Publishes the pooled secondary storage slot as the live
    /// <see cref="Secondary2Snapshot"/> and returns it so the caller can
    /// write fields directly, avoiding an intermediate copy.
    /// </summary>
    internal SecondaryStationResult PrepareSecondary2Snapshot()
    {
        Secondary2Snapshot = _secondaryStorage;
        return _secondaryStorage;
    }

    internal void ResetForReuse()
    {
        Array.Clear(Residual, 0, Residual.Length);
        Array.Clear(VS1, 0, VS1.Length);
        Array.Clear(VS2, 0, VS2.Length);
        Array.Clear(VSX, 0, VSX.Length);
        CarryKinematicSnapshot = null;
        Secondary2Snapshot = null;
    }
}

public class BlsysResult
{
    public double[] Residual = new double[3];
    public double[,] VS1 = new double[3, 5];
    public double[,] VS2 = new double[3, 5];
    /// <summary>
    /// Arc-length sensitivity vector VSX(3). Fortran BLSYS: VSX = BLX + BTX.
    /// Used in SETBL for the XI_ULE coupling in both VM matrix and VDEL RHS.
    /// </summary>
    public double[] VSX = new double[3];
    public double U2;
    public double U2_UEI;
    public double HK2;
    public double HK2_U2;
    public double HK2_T2;
    public double HK2_D2;
    public PrimaryStationState? Primary2Snapshot;
    public KinematicResult? Kinematic2Snapshot;
    public SecondaryStationResult? Secondary2Snapshot;
    public double StaleVs121;

    // Pooled storage for Primary2Snapshot when a caller-provided override
    // is not available — replaces `new PrimaryStationState { ... }` per
    // station per Newton iter.
    private readonly PrimaryStationState _primaryScratch = new();

    /// <summary>
    /// Publishes the pooled primary scratch slot as
    /// <see cref="Primary2Snapshot"/> and returns it so the caller can
    /// assign U/T/D directly without allocating.
    /// </summary>
    internal PrimaryStationState PreparePrimary2Snapshot()
    {
        Primary2Snapshot = _primaryScratch;
        return _primaryScratch;
    }

    internal void ResetForReuse()
    {
        Array.Clear(Residual, 0, Residual.Length);
        Array.Clear(VS1, 0, VS1.Length);
        Array.Clear(VS2, 0, VS2.Length);
        Array.Clear(VSX, 0, VSX.Length);
        U2 = U2_UEI = HK2 = HK2_U2 = HK2_T2 = HK2_D2 = StaleVs121 = 0.0;
        Primary2Snapshot = null;
        Kinematic2Snapshot = null;
        Secondary2Snapshot = null;
    }
}

public class SecondaryStationResult
{
    public double Hc, Hc_T, Hc_D, Hc_U, Hc_MS;
    public double Hs, Hs_T, Hs_D, Hs_U, Hs_MS;
    public double Us, Us_T, Us_D, Us_U, Us_MS;
    public double Cq, Cq_T, Cq_D, Cq_U, Cq_MS;
    public double Cf, Cf_T, Cf_D, Cf_U, Cf_MS, Cf_RE;
    public double Di, Di_S, Di_T, Di_D, Di_U, Di_MS;
    public double De, De_T, De_D, De_U, De_MS;

    // Legacy mapping: none
    // Difference from legacy: This is a managed-only snapshot helper used to carry secondary-state values across parity-sensitive call sites.
    // Decision: Keep the clone helper because it makes the stale-state parity behavior explicit and testable.
    public SecondaryStationResult Clone()
    {
        var clone = new SecondaryStationResult();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(SecondaryStationResult source)
    {
        Hc = source.Hc; Hc_T = source.Hc_T; Hc_D = source.Hc_D; Hc_U = source.Hc_U; Hc_MS = source.Hc_MS;
        Hs = source.Hs; Hs_T = source.Hs_T; Hs_D = source.Hs_D; Hs_U = source.Hs_U; Hs_MS = source.Hs_MS;
        Us = source.Us; Us_T = source.Us_T; Us_D = source.Us_D; Us_U = source.Us_U; Us_MS = source.Us_MS;
        Cq = source.Cq; Cq_T = source.Cq_T; Cq_D = source.Cq_D; Cq_U = source.Cq_U; Cq_MS = source.Cq_MS;
        Cf = source.Cf; Cf_T = source.Cf_T; Cf_D = source.Cf_D; Cf_U = source.Cf_U; Cf_MS = source.Cf_MS; Cf_RE = source.Cf_RE;
        Di = source.Di; Di_S = source.Di_S; Di_T = source.Di_T; Di_D = source.Di_D; Di_U = source.Di_U; Di_MS = source.Di_MS;
        De = source.De; De_T = source.De_T; De_D = source.De_D; De_U = source.De_U; De_MS = source.De_MS;
    }
}


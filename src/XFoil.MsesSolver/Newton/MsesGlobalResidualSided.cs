using XFoil.MsesSolver.Inviscid;
using XFoil.MsesSolver.Topology;

namespace XFoil.MsesSolver.Newton;

/// <summary>
/// R5.5 — Per-side residual assembler for the reshaped Phase 5
/// Newton system. Uses <see cref="MsesGlobalStateSided"/> (per-side
/// layout) and <see cref="SurfaceTopology"/> to march BL along
/// physical paths (upper stagnation→TE_upper, lower stagnation→
/// TE_lower).
///
/// Rows produced (in order):
///   N    inviscid flow-tangency at panel midpoints
///        (γ + σ_airfoil + σ_wake contributions)
///   1    Kutta γ_0 + γ_N = 0
///   N+1  σ_airfoil constraints (σ = d(Ue·δ*)/ds per node)
///   Nw   σ_wake placeholder (identity; filled in R5.7)
///   3·Nu upper BL: momentum / shape-param / Cτ-lag at each upper station
///   3·Nl lower BL: same on lower
///   3·Nw wake BL placeholder (identity; R5.6 fills TE-merge + wake residuals)
///
/// Topology is fixed at construction time (stagnation location
/// frozen from the initial inviscid probe). Re-detecting stag
/// during the Newton iteration would change residual indexing
/// dynamically — that's an advanced optimization deferred past
/// R5.8 gate.
/// </summary>
public sealed class MsesGlobalResidualSided
{
    private readonly MsesGlobalStateSided _layout;
    private readonly MsesInviscidPanelSolver.PanelizedGeometry _pg;
    private readonly SurfaceTopology.Topology _topology;
    private readonly WakeDiscretization.WakePanels? _wake;
    private readonly double[,] _aGammaNormal;
    private readonly double[,] _aSigmaNormal;
    private readonly double[,] _aGammaTangent;
    private readonly double[,] _aSigmaTangent;
    private readonly double[,]? _aWakeSigmaNormal;
    private readonly double[,]? _aWakeSigmaTangent;
    private readonly double _freestreamSpeed;
    private readonly double _alphaRadians;
    private readonly double _kinematicViscosity;
    private readonly double _machEdge;
    private readonly double _initialTheta;

    public MsesGlobalResidualSided(
        MsesGlobalStateSided layout,
        MsesInviscidPanelSolver.PanelizedGeometry pg,
        SurfaceTopology.Topology topology,
        double freestreamSpeed,
        double alphaRadians,
        double kinematicViscosity = 1e-6,
        double machEdge = 0.0,
        double initialTheta = 1e-5,
        WakeDiscretization.WakePanels? wake = null)
    {
        int n = pg.PanelCount;
        if (layout.GammaCount != n + 1) throw new System.ArgumentException(
            $"layout.GammaCount ({layout.GammaCount}) must match N+1 ({n+1})");
        if (layout.SigmaAirfoilCount != n + 1) throw new System.ArgumentException(
            $"layout.SigmaAirfoilCount ({layout.SigmaAirfoilCount}) must match N+1 ({n+1})");
        if (layout.UpperCount != topology.Upper.PanelIndices.Length)
            throw new System.ArgumentException("layout upper count ≠ topology upper");
        if (layout.LowerCount != topology.Lower.PanelIndices.Length)
            throw new System.ArgumentException("layout lower count ≠ topology lower");
        if (wake.HasValue && layout.WakeCount != wake.Value.Length.Length)
            throw new System.ArgumentException(
                $"layout.WakeCount ({layout.WakeCount}) must match wake.Length.Length ({wake.Value.Length.Length})");

        _layout = layout;
        _pg = pg;
        _topology = topology;
        _wake = wake;
        _freestreamSpeed = freestreamSpeed;
        _alphaRadians = alphaRadians;
        _kinematicViscosity = kinematicViscosity;
        _machEdge = machEdge;
        _initialTheta = initialTheta;
        _aGammaNormal = MsesInviscidPanelSolver.BuildVortexNormalInfluenceMatrix(pg);
        _aSigmaNormal = MsesInviscidPanelSolver.BuildSourceNormalInfluenceMatrix(pg);
        _aGammaTangent = MsesInviscidPanelSolver.BuildVortexInfluenceMatrix(pg);
        _aSigmaTangent = MsesInviscidPanelSolver.BuildSourceTangentInfluenceMatrix(pg);
        if (wake.HasValue && layout.WakeCount > 0)
        {
            _aWakeSigmaNormal = MsesInviscidPanelSolver.BuildWakeSourceInfluenceMatrix(
                pg, wake.Value, normal: true);
            _aWakeSigmaTangent = MsesInviscidPanelSolver.BuildWakeSourceInfluenceMatrix(
                pg, wake.Value, normal: false);
        }
    }

    public double[] Compute(double[] state)
    {
        if (state.Length != _layout.StateSize)
            throw new System.ArgumentException(
                $"state length {state.Length} ≠ expected {_layout.StateSize}");
        int n = _pg.PanelCount;
        var u = _layout.Unpack(state);
        var r = new double[_layout.StateSize];
        double vx = _freestreamSpeed * System.Math.Cos(_alphaRadians);
        double vy = _freestreamSpeed * System.Math.Sin(_alphaRadians);

        // 1. Inviscid flow-tangency rows.
        for (int i = 0; i < n; i++)
        {
            double rowSum = 0.0;
            for (int k = 0; k < n + 1; k++)
            {
                rowSum += _aGammaNormal[i, k] * u.Gamma[k]
                        + _aSigmaNormal[i, k] * u.SigmaAirfoil[k];
            }
            if (_aWakeSigmaNormal is not null)
            {
                for (int j = 0; j < _layout.WakeCount; j++)
                    rowSum += _aWakeSigmaNormal[i, j] * u.SigmaWake[j];
            }
            rowSum += vx * _pg.NormalX[i] + vy * _pg.NormalY[i];
            r[i] = rowSum;
        }
        // 2. Kutta row.
        r[n] = u.Gamma[0] + u.Gamma[n];

        // 3. Compute surface Ue at each panel midpoint.
        var ueMid = new double[n];
        for (int i = 0; i < n; i++)
        {
            double ue = vx * _pg.TangentX[i] + vy * _pg.TangentY[i];
            for (int k = 0; k < n + 1; k++)
            {
                ue += _aGammaTangent[i, k] * u.Gamma[k]
                    + _aSigmaTangent[i, k] * u.SigmaAirfoil[k];
            }
            if (_aWakeSigmaTangent is not null)
            {
                for (int j = 0; j < _layout.WakeCount; j++)
                    ue += _aWakeSigmaTangent[i, j] * u.SigmaWake[j];
            }
            ueMid[i] = System.Math.Abs(ue);
        }

        // 4. Upper BL rows, marching from stagnation toward TE_upper.
        MarchBlResiduals(
            u.UpperDstar, u.UpperTheta, u.UpperCTau,
            _topology.Upper, ueMid,
            r, _layout.UpperDstarOffset, _layout.UpperThetaOffset,
            _layout.UpperCTauOffset);

        // 5. Lower BL rows.
        MarchBlResiduals(
            u.LowerDstar, u.LowerTheta, u.LowerCTau,
            _topology.Lower, ueMid,
            r, _layout.LowerDstarOffset, _layout.LowerThetaOffset,
            _layout.LowerCTauOffset);

        // 6. σ_airfoil constraint. Per node k: σ = d(Ue·δ*)/ds.
        //    Upper nodes (k < stagNode): tie to upper BL state.
        //    Lower nodes (k > stagNode): tie to lower BL state.
        //    Stagnation node (k == stagNode): anchor σ = 0.
        ComputeSigmaAirfoilResiduals(u, ueMid, r);

        // 7. Wake σ placeholder (R5.7 fills the real coupling).
        for (int k = 0; k < _layout.SigmaWakeCount; k++)
            r[_layout.SigmaWakeOffset + k] = u.SigmaWake[k];

        // 8. Wake BL residuals. If _wake is null, fall back to
        //    identity placeholder.
        if (_wake.HasValue && _layout.WakeCount > 0)
        {
            ComputeWakeBlResiduals(u, r);
        }
        else
        {
            for (int k = 0; k < _layout.WakeCount; k++)
            {
                r[_layout.WakeDstarOffset + k] = u.WakeDstar[k];
                r[_layout.WakeThetaOffset + k] = u.WakeTheta[k];
                r[_layout.WakeCTauOffset + k] = u.WakeCTau[k];
            }
        }
        return r;
    }

    /// <summary>
    /// R5.6 — Wake BL residuals. Station 0 is TE-merge (thesis eq.
    /// 6.63 sharp-TE). Stations 1..Nw-1 march as free-shear BL
    /// (Cf=0) with wake-panel arc-length from the WakePanels geometry.
    /// Wake Ue for now is taken as freestream (|V∞|) along the
    /// wake direction — true wake Ue recovery would come from σ_wake
    /// contribution in R5.7.
    /// </summary>
    private void ComputeWakeBlResiduals(
        MsesGlobalStateSided.SidedState u, double[] r)
    {
        int nw = _layout.WakeCount;
        int nu = _layout.UpperCount;
        int nl = _layout.LowerCount;
        // Upper TE is the last upper station (farthest from stag).
        double thetaU_TE = u.UpperTheta[nu - 1];
        double dStarU_TE = u.UpperDstar[nu - 1];
        double cTauU_TE = u.UpperCTau[nu - 1];
        double thetaL_TE = u.LowerTheta[nl - 1];
        double dStarL_TE = u.LowerDstar[nl - 1];
        double cTauL_TE = u.LowerCTau[nl - 1];

        // Station 0: TE-merge constraint.
        var te = MsesBoundaryLayerResidual.TEMergeResiduals(
            thetaU_TE, thetaL_TE, dStarU_TE, dStarL_TE,
            cTauU_TE, cTauL_TE,
            u.WakeTheta[0], u.WakeDstar[0], u.WakeCTau[0]);
        r[_layout.WakeThetaOffset + 0] = te.RTheta;
        r[_layout.WakeDstarOffset + 0] = te.RDstar;
        r[_layout.WakeCTauOffset + 0] = te.RCTau;

        // Stations 1..nw-1: free-shear march with Cf=0.
        var wake = _wake!.Value;
        for (int k = 1; k < nw; k++)
        {
            double thetaPrev = u.WakeTheta[k - 1];
            double thetaCur = u.WakeTheta[k];
            double dStarPrev = u.WakeDstar[k - 1];
            double dStarCur = u.WakeDstar[k];
            double hPrev = thetaPrev > 1e-18 ? dStarPrev / thetaPrev : 1.4;
            double hCur = thetaCur > 1e-18 ? dStarCur / thetaCur : 1.4;
            // Wake Ue ≈ freestream for this first pass (no σ_wake yet).
            double ueMid = _freestreamSpeed;
            double dx = System.Math.Max(
                wake.ArcLengthFromTE[k] - wake.ArcLengthFromTE[k - 1], 1e-12);
            r[_layout.WakeThetaOffset + k] = MsesBoundaryLayerResidual.MomentumResidual(
                thetaPrev, thetaCur, hPrev, hCur, ueMid, ueMid, dx,
                _kinematicViscosity, _machEdge, isWake: true);
            r[_layout.WakeDstarOffset + k] = MsesBoundaryLayerResidual.ShapeParamResidual(
                thetaPrev, thetaCur, hPrev, hCur, u.WakeCTau[k], ueMid, ueMid, dx,
                _kinematicViscosity, _machEdge, isWake: true);
            r[_layout.WakeCTauOffset + k] = MsesBoundaryLayerResidual.LagResidual(
                u.WakeCTau[k - 1], u.WakeCTau[k], thetaPrev, thetaCur, hPrev, hCur,
                ueMid, ueMid, dx, _kinematicViscosity, _machEdge);
        }
    }

    private void MarchBlResiduals(
        double[] dStar, double[] theta, double[] cTau,
        SurfaceTopology.SurfaceStations side,
        double[] ueMid,
        double[] r,
        int dStarOffset, int thetaOffset, int cTauOffset)
    {
        int ns = side.PanelIndices.Length;
        // Station 0 anchor: pin to laminar-like initial conditions
        // near the stagnation point.
        r[thetaOffset + 0] = theta[0] - _initialTheta;
        r[dStarOffset + 0] = dStar[0] - _initialTheta * 2.5;  // H ≈ 2.5
        r[cTauOffset + 0] = cTau[0];

        for (int k = 1; k < ns; k++)
        {
            int panelPrev = side.PanelIndices[k - 1];
            int panelCur = side.PanelIndices[k];
            double thetaPrev = theta[k - 1];
            double thetaCur = theta[k];
            double dStarPrev = dStar[k - 1];
            double dStarCur = dStar[k];
            double hPrev = thetaPrev > 1e-18 ? dStarPrev / thetaPrev : 1.4;
            double hCur = thetaCur > 1e-18 ? dStarCur / thetaCur : 1.4;
            double uePrev = ueMid[panelPrev];
            double ueCur = ueMid[panelCur];
            double dx = System.Math.Max(side.ArcLength[k] - side.ArcLength[k - 1], 1e-12);

            r[thetaOffset + k] = MsesBoundaryLayerResidual.MomentumResidual(
                thetaPrev, thetaCur, hPrev, hCur, uePrev, ueCur, dx,
                _kinematicViscosity, _machEdge);
            r[dStarOffset + k] = MsesBoundaryLayerResidual.ShapeParamResidual(
                thetaPrev, thetaCur, hPrev, hCur, cTau[k], uePrev, ueCur, dx,
                _kinematicViscosity, _machEdge);
            r[cTauOffset + k] = MsesBoundaryLayerResidual.LagResidual(
                cTau[k - 1], cTau[k], thetaPrev, thetaCur, hPrev, hCur,
                uePrev, ueCur, dx, _kinematicViscosity, _machEdge);
        }
    }

    private void ComputeSigmaAirfoilResiduals(
        MsesGlobalStateSided.SidedState u, double[] ueMid, double[] r)
    {
        int n = _pg.PanelCount;
        int stagNode = _topology.StagnationNodeIndex;
        // For node k ∈ (0, stagNode): on UPPER side. The upper side
        // goes from stag (node stagNode) outward to node 0 along
        // descending panel indices. Node k's "incoming" direction
        // from the stag perspective: panel k is between nodes k and
        // k+1. The upper surface walking order TE-ward (for node
        // indexing perspective) means "toward node 0".
        //
        // σ at node k ties the change of Ue·δ* across panel k-1 to
        // panel k. For simplicity in R5.5 we anchor σ to zero at
        // all airfoil nodes (placeholder); the real σ = d(Ue·δ*)/ds
        // coupling comes in once we verify the basic march is
        // producing sensible residuals. This keeps R5.5 bounded.
        for (int k = 0; k < n + 1; k++)
        {
            r[_layout.SigmaAirfoilOffset + k] = u.SigmaAirfoil[k];
        }
    }
}

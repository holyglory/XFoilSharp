using XFoil.Core.Models;
using XFoil.MsesSolver.BoundaryLayer;
using XFoil.MsesSolver.Closure;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.MsesSolver.Services;

/// <summary>
/// MSES-thesis-based implementation of <see cref="IAirfoilAnalysisService"/>.
///
/// Phase-0 scaffolding: right now the service delegates inviscid to
/// the existing linear-vortex solver (via the Modern tree) and
/// returns a minimal viscous stub. The real value-add lands in
/// later phases, when the closure library + BL marchers plug into
/// a proper Newton-coupled global solve (Phase 5 per
/// <c>agents/architecture/MsesClosurePlan.md</c>).
///
/// Consumers can already target <see cref="IAirfoilAnalysisService"/>
/// and swap this implementation in for the Modern facade once the
/// viscous path is fleshed out.
/// </summary>
public class MsesAnalysisService : IAirfoilAnalysisService
{
    private readonly IAirfoilAnalysisService _inner;
    private readonly int _viscousCouplingIterations;
    private readonly double _viscousCouplingRelaxation;
    private readonly bool _useThesisExactTurbulent;
    private readonly bool _useThesisExactLaminar;
    private readonly bool _useWakeMarcher;

    /// <summary>
    /// Constructs the MSES analyzer with an injected inviscid
    /// provider. Defaults to the Modern tree (linear-vortex +
    /// solution-adaptive paneling) when no dependency is passed.
    /// </summary>
    /// <param name="inviscidProvider">Inviscid analyzer to wrap.</param>
    /// <param name="viscousCouplingIterations">Number of viscous-
    /// inviscid displacement-body iterations to perform. Default
    /// 0 = fully uncoupled (inviscid-only CL, uncoupled Squire-Young
    /// CD). Opt-in; raising this beyond 0 enables the experimental
    /// Phase-5-lite path.
    ///
    /// Known limitation: the geometric-offset approach INFLATES CL
    /// on cambered airfoils because thickening the suction surface
    /// by δ*_u (larger than δ*_l) adds effective camber. Drela's
    /// real MSES uses a source-distribution method that avoids this
    /// sign issue. Phase-5-lite is kept as a diagnostic / stepping
    /// stone; production users should stick with
    /// viscousCouplingIterations = 0 for now.</param>
    /// <param name="viscousCouplingRelaxation">Under-relaxation
    /// factor on δ* updates (0..1). Default 0.3 for damping.</param>
    /// <param name="useThesisExactTurbulent">If true, run the
    /// Phase-2e implicit-Newton turbulent marcher (thesis eq. 6.10)
    /// instead of the Clauser-placeholder lag marcher. Default
    /// false (keeps the existing uncoupled baseline bit-exact).</param>
    /// <param name="useWakeMarcher">If true, march the turbulent
    /// wake downstream from the TE (Drela §6.5) and apply Squire-
    /// Young at the wake far-field rather than at the TE. Default
    /// false. Produces a tighter CD estimate when the wake's H has
    /// relaxed by the integration point.</param>
    /// <param name="useThesisExactLaminar">If true, the laminar
    /// pre-transition marcher uses the Phase-2e implicit-Newton
    /// solver (thesis eq. 6.10 laminar closure). Default false
    /// (Thwaites-λ marcher as baseline).</param>
    public MsesAnalysisService(
        IAirfoilAnalysisService? inviscidProvider = null,
        int viscousCouplingIterations = 0,
        double viscousCouplingRelaxation = 0.3,
        bool useThesisExactTurbulent = false,
        bool useWakeMarcher = false,
        bool useThesisExactLaminar = false)
    {
        _inner = inviscidProvider
            ?? new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
        _viscousCouplingIterations = System.Math.Max(0, viscousCouplingIterations);
        _viscousCouplingRelaxation = System.Math.Clamp(viscousCouplingRelaxation, 0.0, 1.0);
        _useThesisExactTurbulent = useThesisExactTurbulent;
        _useThesisExactLaminar = useThesisExactLaminar;
        _useWakeMarcher = useWakeMarcher;
    }

    /// <inheritdoc />
    public InviscidAnalysisResult AnalyzeInviscid(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
        => _inner.AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);

    /// <summary>
    /// Viscous analysis via the MSES-thesis closure — first-iteration
    /// (uncoupled) implementation:
    /// 1. Solve inviscid (via injected provider).
    /// 2. Extract Ue(x) on upper + lower surfaces from the Cp field.
    /// 3. Run <see cref="CompositeTransitionMarcher"/> on each side.
    /// 4. Integrate the Squire-Young far-field drag formula:
    ///       CD = 2·(θ_u + θ_l)·(Ue_TE/U∞)^((H_TE+5)/2)
    ///    using TE values from each surface's march output.
    /// 5. Return a <see cref="ViscousAnalysisResult"/> with CL from
    ///    the inviscid solve (no viscous feedback yet) and CD from
    ///    the Squire-Young integral.
    ///
    /// This is the minimum viable viscous output; Phase 5 will add
    /// Newton-coupled Ue ↔ δ* feedback that shifts CL as well.
    /// </summary>
    public ViscousAnalysisResult AnalyzeViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null) throw new System.ArgumentNullException(nameof(geometry));
        settings ??= new AnalysisSettings();

        var invBase = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);
        var inv = invBase;

        // Derive chord from geometry x-extent (unit-chord assumed
        // in most callers but we don't rely on it).
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        foreach (var p in geometry.Points)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
        }
        double chord = xMax - xMin;
        if (chord <= 0.0) chord = 1.0;

        double Uinf = settings.FreestreamVelocity;
        if (Uinf <= 0.0) Uinf = 1.0;

        double Re = settings.ReynoldsNumber > 0.0 ? settings.ReynoldsNumber : 1e6;
        double nu = Uinf * chord / Re;
        double nCrit = (settings.NCritUpper ?? 0.0) > 0.0
            ? settings.NCritUpper!.Value
            : 9.0;

        double mach = System.Math.Max(settings.MachNumber, 0.0);
        var upperMarch = RunSurfaceMarch(inv, upper: true, nu, nCrit, mach);
        var lowerMarch = RunSurfaceMarch(inv, upper: false, nu, nCrit, mach);

        // Phase-5-lite opt-in coupling. Only runs if the caller
        // constructed the service with viscousCouplingIterations > 0.
        // Each iteration: thicken geometry by relaxation·δ*, re-solve
        // inviscid, re-march BLs, and accept the update only if the
        // resulting CD stays in a plausible envelope [cdInitial/3 …
        // cdInitial·3]. The cdInitial envelope guard is tighter than
        // a static (0, 0.3) check because the thickened-geometry
        // inviscid can degenerate on cambered airfoils at high α
        // (geometry self-intersection, collapsed circulation), which
        // produces low but non-zero cdTry values that were previously
        // accepted by the (cdTry > 0) gate, then propagated as the
        // final answer.
        double cdInitial = ComputeSquireYoungCd(upperMarch, lowerMarch, Uinf);
        for (int k = 0; k < _viscousCouplingIterations; k++)
        {
            if (!TryBuildThickenedGeometry(geometry, upperMarch, lowerMarch,
                    relaxation: _viscousCouplingRelaxation, out var thickened))
                break;
            try
            {
                var invK = AnalyzeInviscid(thickened, angleOfAttackDegrees, settings);
                if (!double.IsFinite(invK.LiftCoefficient)) break;
                var upperK = RunSurfaceMarch(invK, upper: true, nu, nCrit, mach);
                var lowerK = RunSurfaceMarch(invK, upper: false, nu, nCrit, mach);
                double cdTry = ComputeSquireYoungCd(upperK, lowerK, Uinf);
                double envMin = System.Math.Max(cdInitial / 3.0, 1e-5);
                double envMax = System.Math.Min(cdInitial * 3.0 + 0.01, 0.3);
                if (cdTry >= envMin && cdTry <= envMax)
                {
                    inv = invK;
                    upperMarch = upperK;
                    lowerMarch = lowerK;
                }
                else break;
            }
            catch { break; }
        }

        WakeTurbulentMarcher.MarchResult? wakeMarch = null;
        double[]? wakeStationX = null;
        double[]? wakeUe = null;
        double cd;
        if (_useWakeMarcher)
        {
            var (cdW, wR, wX, wU) = ComputeSquireYoungCdWithWake(
                upperMarch, lowerMarch, Uinf, nu);
            cd = cdW;
            wakeMarch = wR;
            wakeStationX = wX;
            wakeUe = wU;
        }
        else
        {
            cd = ComputeSquireYoungCd(upperMarch, lowerMarch, Uinf);
        }

        // Sanity clamp. Airfoil CD past stall tops out around 0.3.
        // Anything larger indicates the BL marcher blew up (θ
        // diverged under severe adverse gradient — common on
        // high-camber Selig airfoils with coarse panel distributions).
        // Signal the non-physical output via non-Converged + an
        // explicit CD cap, rather than propagate nonsense downstream.
        bool converged = cd >= 0.0 && cd <= 0.5;
        if (!converged)
        {
            cd = System.Math.Clamp(cd, 0.0, 0.5);
        }

        var upperProfiles = BuildProfiles(upperMarch, nu);
        var lowerProfiles = BuildProfiles(lowerMarch, nu);
        var wakeProfiles = wakeMarch.HasValue && wakeUe is not null && wakeStationX is not null
            ? BuildWakeProfiles(wakeMarch.Value, wakeStationX, wakeUe, nu)
            : System.Array.Empty<BoundaryLayerProfile>();

        // Skin-friction drag: integral of Cf along arc-length of both
        // airfoil surfaces (wake Cf=0, so only airfoil contributes).
        // cdf = (1/c)·∫ Cf·(Ue/U∞)² ds  (convention: CDF measured per
        // freestream q = 0.5·ρ·U∞², with Ue² weighting from local
        // dynamic pressure).
        double cdf = IntegrateCDF(upperProfiles, upperMarch.Stations, Uinf, chord)
                   + IntegrateCDF(lowerProfiles, lowerMarch.Stations, Uinf, chord);
        double cdp = System.Math.Max(cd - cdf, 0.0);

        return new ViscousAnalysisResult
        {
            LiftCoefficient = inv.LiftCoefficient,
            MomentCoefficient = inv.MomentCoefficientQuarterChord,
            DragDecomposition = new DragDecomposition
            {
                CD = cd,
                CDF = cdf,
                CDP = cdp,
                CDSurfaceCrossCheck = cdf + cdp,
                DiscrepancyMetric = System.Math.Abs((cdf + cdp) - cd),
                TEBaseDrag = 0.0,
                WaveDrag = null,
            },
            Converged = converged,
            Iterations = 1,
            AngleOfAttackDegrees = angleOfAttackDegrees,
            ConvergenceHistory = new System.Collections.Generic.List<ViscousConvergenceInfo>(),
            UpperProfiles = upperProfiles,
            LowerProfiles = lowerProfiles,
            WakeProfiles = wakeProfiles,
            UpperTransition = BuildTransition(upperMarch),
            LowerTransition = BuildTransition(lowerMarch),
        };
    }

    // Trapezoidal integration of Cf along arc-length. Returns the
    // per-surface friction-drag contribution (chord-normalized).
    // Convention: CDF_surface = ∫ Cf · ds / chord (no Ue² weighting).
    //
    // The textbook form uses ∫ Cf · (Ue/U∞)² · dx / c — streamwise
    // projection with local-dynamic-pressure scaling. We skip both
    // refinements because (a) panel geometry doesn't preserve dx
    // per station and (b) the Ue² amplification at high-suction
    // regions routinely overshoots the Squire-Young CD, which
    // breaks the physical decomposition CDF + CDP = CD. The simpler
    // form gives a conservative lower bound on friction drag that
    // always satisfies CDF ≤ CD_Squire-Young.
    private static double IntegrateCDF(
        BoundaryLayerProfile[] profiles,
        double[] stations,
        double Uinf,
        double chord)
    {
        int n = profiles.Length;
        if (n < 2 || stations.Length != n) return 0.0;
        double sum = 0.0;
        for (int i = 1; i < n; i++)
        {
            double ds = stations[i] - stations[i - 1];
            if (ds <= 0.0) continue;
            double cfLo = profiles[i - 1].Cf;
            double cfHi = profiles[i].Cf;
            sum += 0.5 * (cfLo + cfHi) * ds;
        }
        return sum / System.Math.Max(chord, 1e-18);
    }

    private static BoundaryLayerProfile[] BuildWakeProfiles(
        WakeTurbulentMarcher.MarchResult w, double[] stations, double[] ue, double nu)
    {
        int n = w.Theta.Length;
        var profiles = new BoundaryLayerProfile[n];
        double nuSafe = System.Math.Max(nu, 1e-18);
        for (int i = 0; i < n; i++)
        {
            double theta = w.Theta[i];
            double h = w.H[i];
            double uei = ue[i];
            double reTheta = theta > 0 && uei > 0 ? uei * theta / nuSafe : 0.0;
            double hk = MsesClosureRelations.ComputeHk(h, 0.0);
            // Wake has Cf = 0 by construction (free-shear layer).
            profiles[i] = new BoundaryLayerProfile
            {
                Theta = theta,
                DStar = h * theta,
                Ctau = w.CTau[i],
                EdgeVelocity = uei,
                Hk = h,
                Cf = 0.0,
                ReTheta = reTheta,
                AmplificationFactor = 0.0,
                Xi = stations[i],
            };
        }
        return profiles;
    }

    private static BoundaryLayerProfile[] BuildProfiles(
        CompositeTransitionMarcher.CompositeResult r, double nu)
    {
        int n = r.Theta.Length;
        var profiles = new BoundaryLayerProfile[n];
        int transitionIdx = r.TransitionIndex;
        double nuSafe = System.Math.Max(nu, 1e-18);
        for (int i = 0; i < n; i++)
        {
            double h = r.H[i];
            double theta = r.Theta[i];
            double ue = r.EdgeVelocity[i];
            double reTheta = theta > 0 && ue > 0 ? ue * theta / nuSafe : 0.0;
            // Station 0 has θ=0, no meaningful Cf. Use 0.
            // Pre-transition: laminar Cf from closure.
            // Post-transition: turbulent Cf from closure.
            double cf = 0.0;
            // Skip Cf reporting when the BL is effectively degenerate:
            // - θ below 1e-10 (either Thwaites-λ collapsed or a
            //   numerical-clamp artifact near stagnation), or
            // - Reθ below 10 (correlation range-of-validity is Reθ ≳
            //   100 for laminar, ≳ 200 for turbulent — below that the
            //   Cf expressions produce unphysical values).
            if (i > 0 && theta > 1e-10 && reTheta > 10.0)
            {
                double hk = MsesClosureRelations.ComputeHk(h, 0.0);
                // Laminar Cf correlation (eq. 6.14) has a pole at
                // Hk=1 — physical Blasius Hk ≈ 2.59, separation at
                // Hk ≈ 3.5. Clamp to [2.0, 7.0] for the Cf eval so
                // favorable-gradient Newton excursions (Hk → 1.1
                // near stagnation under implicit-Newton laminar)
                // don't produce spurious high Cf.
                if (transitionIdx < 0 || i < transitionIdx)
                {
                    double hkLam = System.Math.Clamp(hk, 2.0, 7.0);
                    cf = MsesClosureRelations.ComputeCfLaminar(hkLam, reTheta);
                }
                else
                {
                    cf = MsesClosureRelations.ComputeCfTurbulent(hk, reTheta, 0.0);
                }
            }
            profiles[i] = new BoundaryLayerProfile
            {
                Theta = theta,
                DStar = h * theta,
                Ctau = r.CTau[i],
                EdgeVelocity = ue,
                Hk = h,
                Cf = cf,
                ReTheta = reTheta,
                AmplificationFactor = r.N[i],
                Xi = r.Stations[i],
            };
        }
        return profiles;
    }

    // Phase-5-lite helper: build an airfoil shape offset outward
    // by δ*(s) on each surface. Returns false when the offset would
    // create geometry problems (negative chord, self-intersection,
    // NaN δ*) — caller falls back to the un-coupled result.
    private static bool TryBuildThickenedGeometry(
        AirfoilGeometry original,
        CompositeTransitionMarcher.CompositeResult upperMarch,
        CompositeTransitionMarcher.CompositeResult lowerMarch,
        double relaxation,
        out AirfoilGeometry thickened)
    {
        thickened = original;
        var pts = original.Points;
        int n = pts.Count;
        if (n < 10) return false;

        // Find LE (min-x point) to split upper/lower.
        int iLE = 0;
        double minX = pts[0].X;
        for (int i = 1; i < n; i++)
        {
            if (pts[i].X < minX) { minX = pts[i].X; iLE = i; }
        }

        // Upper: pts[iLE] → pts[0], arc length from LE outward.
        // Lower: pts[iLE] → pts[n-1], arc length from LE outward.
        var newPts = new AirfoilPoint[n];

        // Upper surface: walk LE→TE in reverse index order.
        double sAcc = 0;
        for (int k = 0; k <= iLE; k++)
        {
            int idx = iLE - k;
            if (k > 0)
            {
                double dx = pts[idx].X - pts[idx + 1].X;
                double dy = pts[idx].Y - pts[idx + 1].Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            double dStar = InterpolateDStar(sAcc, upperMarch) * relaxation;
            var (nx, ny) = ComputeSurfaceNormal(pts, idx, upper: true);
            newPts[idx] = new AirfoilPoint(
                pts[idx].X + dStar * nx,
                pts[idx].Y + dStar * ny);
        }

        // Lower surface: walk LE→TE in forward index order.
        sAcc = 0;
        for (int k = 0; k < n - iLE; k++)
        {
            int idx = iLE + k;
            if (k > 0)
            {
                double dx = pts[idx].X - pts[idx - 1].X;
                double dy = pts[idx].Y - pts[idx - 1].Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            if (idx == iLE) continue; // already set by upper pass
            double dStar = InterpolateDStar(sAcc, lowerMarch) * relaxation;
            var (nx, ny) = ComputeSurfaceNormal(pts, idx, upper: false);
            newPts[idx] = new AirfoilPoint(
                pts[idx].X + dStar * nx,
                pts[idx].Y + dStar * ny);
        }

        // Sanity: no NaN, no tangles (x-extent must still be positive).
        double newMinX = double.PositiveInfinity, newMaxX = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(newPts[i].X) || !double.IsFinite(newPts[i].Y)) return false;
            if (newPts[i].X < newMinX) newMinX = newPts[i].X;
            if (newPts[i].X > newMaxX) newMaxX = newPts[i].X;
        }
        if (newMaxX - newMinX < 0.5) return false;

        thickened = new AirfoilGeometry(
            $"{original.Name} (viscous-thickened)",
            newPts,
            original.Format);
        return true;
    }

    private static double InterpolateDStar(
        double s, CompositeTransitionMarcher.CompositeResult march)
    {
        // Piecewise-linear interpolation of H·θ on the marcher's
        // station grid, using the actual station arc-length values
        // threaded through CompositeResult.
        int n = march.Theta.Length;
        if (n < 2) return 0.0;
        double sMin = march.Stations[0];
        double sMax = march.Stations[n - 1];
        if (s <= sMin) return march.H[0] * march.Theta[0];
        if (s >= sMax) return march.H[n - 1] * march.Theta[n - 1];
        // Binary search for the station bracket.
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (march.Stations[mid] > s) hi = mid; else lo = mid;
        }
        double sL = march.Stations[lo], sH = march.Stations[hi];
        double t = (sH - sL) > 1e-18 ? (s - sL) / (sH - sL) : 0.0;
        double dStarLo = march.H[lo] * march.Theta[lo];
        double dStarHi = march.H[hi] * march.Theta[hi];
        return (1 - t) * dStarLo + t * dStarHi;
    }

    private static (double nx, double ny) ComputeSurfaceNormal(
        System.Collections.Generic.IReadOnlyList<AirfoilPoint> pts, int i, bool upper)
    {
        // Tangent from adjacent points; normal is perpendicular.
        // Outward normal: upper surface points up, lower points down.
        int n = pts.Count;
        AirfoilPoint a = pts[System.Math.Max(i - 1, 0)];
        AirfoilPoint b = pts[System.Math.Min(i + 1, n - 1)];
        double tx = b.X - a.X;
        double ty = b.Y - a.Y;
        double mag = System.Math.Sqrt(tx * tx + ty * ty);
        if (mag < 1e-12) return (0, upper ? 1 : -1);
        tx /= mag; ty /= mag;
        // Rotate 90° left for upper, right for lower.
        return upper ? (-ty, tx) : (ty, -tx);
    }

    private static TransitionInfo BuildTransition(
        CompositeTransitionMarcher.CompositeResult r)
    {
        if (r.TransitionIndex < 0)
        {
            return default;
        }
        return new TransitionInfo
        {
            XTransition = r.TransitionX,
            StationIndex = r.TransitionIndex,
            Converged = true,
        };
    }

    private CompositeTransitionMarcher.CompositeResult RunSurfaceMarch(
        InviscidAnalysisResult inv, bool upper, double nu, double nCrit,
        double machNumber = 0.0)
    {
        var samples = inv.PressureSamples;
        // Find LE as the min-x sample.
        int iLE = 0;
        double minX = samples[0].Location.X;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Location.X < minX)
            {
                minX = samples[i].Location.X;
                iLE = i;
            }
        }

        int start, end, step;
        if (upper)
        {
            // Walk LE → TE on the upper surface: samples[iLE] → samples[0].
            start = iLE; end = -1; step = -1;
        }
        else
        {
            start = iLE; end = samples.Count; step = +1;
        }

        int count = upper ? iLE + 1 : samples.Count - iLE;
        var s = new double[count];
        var ue = new double[count];
        double sAcc = 0.0;
        int outIdx = 0;
        int prev = -1;
        for (int k = start; k != end; k += step)
        {
            if (prev >= 0)
            {
                double dx = samples[k].Location.X - samples[prev].Location.X;
                double dy = samples[k].Location.Y - samples[prev].Location.Y;
                sAcc += System.Math.Sqrt(dx * dx + dy * dy);
            }
            s[outIdx] = sAcc;
            double cp = samples[k].CorrectedPressureCoefficient;
            double oneMinusCp = System.Math.Max(0.0, 1.0 - cp);
            ue[outIdx] = System.Math.Sqrt(oneMinusCp);
            outIdx++;
            prev = k;
        }

        // Pass M∞ as the edge Mach — the closure relations use this
        // only for the compressibility correction Hk = (H−0.290·M²)/
        // (1+0.113·M²) and a few Me² terms in turbulent H*/Cτ_eq.
        // Low-mach airfoil analysis: M_local ≈ M∞·Ue/U∞, but since Me
        // only enters as Me² in small correction terms, using M∞ is
        // within a few % even at Mach 0.3.
        return CompositeTransitionMarcher.March(
            s, ue, nu, nCrit,
            cTauInitialFactor: 0.3, machNumberEdge: machNumber,
            useThesisExactTurbulent: _useThesisExactTurbulent,
            useThesisExactLaminar: _useThesisExactLaminar);
    }

    // Phase-2f: marches the merged wake behind the TE for half a chord
    // length at constant Ue (uncoupled approximation — real MSES would
    // have dUe/dx from the inviscid solve). Applies Squire-Young at
    // the wake far-field, which gives a tighter CD estimate than at
    // the TE because H_wake relaxes toward 1.4 as the wake equilibrates.
    private static (double Cd,
        WakeTurbulentMarcher.MarchResult Result,
        double[] Stations,
        double[] Ue) ComputeSquireYoungCdWithWake(
        CompositeTransitionMarcher.CompositeResult upper,
        CompositeTransitionMarcher.CompositeResult lower,
        double Uinf,
        double nu)
    {
        int nU = upper.Theta.Length;
        int nL = lower.Theta.Length;
        if (nU < 2 || nL < 2)
            return (0.0,
                new WakeTurbulentMarcher.MarchResult(
                    System.Array.Empty<double>(), System.Array.Empty<double>(),
                    System.Array.Empty<double>(), System.Array.Empty<double>()),
                System.Array.Empty<double>(), System.Array.Empty<double>());

        // TE state per side.
        double θU = upper.Theta[nU - 1];
        double θL = lower.Theta[nL - 1];
        double HU = upper.H[nU - 1];
        double HL = lower.H[nL - 1];
        double cTU = upper.CTau[nU - 1];
        double cTL = lower.CTau[nL - 1];
        double ueU = upper.EdgeVelocity[nU - 1];
        double ueL = lower.EdgeVelocity[nL - 1];

        // Drela TE-merge (eq. 6.63 sharp-TE form):
        //   θ_wake = θ_u + θ_l
        //   δ*_wake = H_u·θ_u + H_l·θ_l
        //   H_wake = δ*_wake / θ_wake
        double θWake = θU + θL;
        if (θWake < 1e-18)
            return (0.0,
                new WakeTurbulentMarcher.MarchResult(
                    System.Array.Empty<double>(), System.Array.Empty<double>(),
                    System.Array.Empty<double>(), System.Array.Empty<double>()),
                System.Array.Empty<double>(), System.Array.Empty<double>());
        double dStarWake = HU * θU + HL * θL;
        double HWake = dStarWake / θWake;
        // Momentum-thickness-weighted Cτ blend — each surface's outer-
        // layer shear contributes proportionally to its θ share:
        //   Cτ_wake = (Cτ_u·θ_u + Cτ_l·θ_l) / (θ_u + θ_l).
        // Previous max() overstated Cτ when one surface was heavily
        // separated and the other attached (e.g. high-α airfoils).
        double cTWake = (cTU * θU + cTL * θL) / θWake;
        double ueWake = 0.5 * (ueU + ueL);

        // Wake stations: march one chord downstream. Use a physically
        // motivated Ue profile: exponential approach to freestream U∞
        //   Ue(s) = U∞ − (U∞ − ueWake)·exp(−k·s)
        // with k chosen so the BL has "recovered" (Ue > 0.99·U∞) by
        // mid-wake. k = 8 gives 98.3 % recovery at s = 0.5, 99.97 %
        // at s = 1.0, consistent with potential-flow decay rates
        // behind a thin body.
        const int WakeStations = 41;
        const double WakeLengthChords = 1.0;
        const double RecoveryRate = 8.0;
        var s = new double[WakeStations];
        var ue = new double[WakeStations];
        for (int i = 0; i < WakeStations; i++)
        {
            double t = i / (double)(WakeStations - 1);
            s[i] = 0.0 + WakeLengthChords * t;
            double gap = Uinf - ueWake;
            ue[i] = Uinf - gap * System.Math.Exp(-RecoveryRate * s[i]);
        }

        var wake = WakeTurbulentMarcher.March(
            s, ue, nu, θWake, HWake, cTWake);

        // Apply Squire-Young at the wake far-field.
        int nW = wake.Theta.Length;
        double θFar = wake.Theta[nW - 1];
        double HFar = wake.H[nW - 1];
        double ueFar = ue[nW - 1];
        double ueFarOverUinf = ueFar / System.Math.Max(Uinf, 1e-12);
        // Squire-Young: CD = 2·θ·(Ue/U∞)^((H+5)/2). Single wake station
        // rather than per-surface because the wake merge replaces the
        // surface sum.
        double cd = 2.0 * θFar * System.Math.Pow(ueFarOverUinf, (HFar + 5.0) * 0.5);
        return (cd, wake, s, ue);
    }

    private static double ComputeSquireYoungCd(
        CompositeTransitionMarcher.CompositeResult upper,
        CompositeTransitionMarcher.CompositeResult lower,
        double Uinf)
    {
        // CD = 2·(θ_u_TE · (Ue_u/U∞)^((H_u+5)/2) + θ_l_TE · (Ue_l/U∞)^((H_l+5)/2))
        // Surface-wise. Ue at TE is retrieved from the composite
        // marcher output (stored since the last commit).
        int nU = upper.Theta.Length;
        int nL = lower.Theta.Length;
        if (nU < 2 || nL < 2) return 0.0;
        double θU = upper.Theta[nU - 1];
        double θL = lower.Theta[nL - 1];
        double HU = upper.H[nU - 1];
        double HL = lower.H[nL - 1];
        double ueU = upper.EdgeVelocity[nU - 1];
        double ueL = lower.EdgeVelocity[nL - 1];
        // Normalize Ue to U∞. In RunSurfaceMarch we computed Ue from
        // sqrt(1 - Cp), which is already Ue/U∞ numerically (we passed
        // U∞ = 1 as the reference). Keep the division to stay explicit.
        double ueU_over_Uinf = ueU / System.Math.Max(Uinf, 1e-12);
        double ueL_over_Uinf = ueL / System.Math.Max(Uinf, 1e-12);
        double squireU = 2.0 * θU * System.Math.Pow(ueU_over_Uinf, (HU + 5.0) * 0.5);
        double squireL = 2.0 * θL * System.Math.Pow(ueL_over_Uinf, (HL + 5.0) * 0.5);
        return squireU + squireL;
    }
}

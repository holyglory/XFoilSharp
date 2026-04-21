using XFoil.Core.Models;
using XFoil.MsesSolver.BoundaryLayer;
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

    /// <summary>
    /// Constructs the MSES analyzer with an injected inviscid
    /// provider. Defaults to the Modern tree (linear-vortex +
    /// solution-adaptive paneling) when no dependency is passed.
    /// </summary>
    public MsesAnalysisService(IAirfoilAnalysisService? inviscidProvider = null)
    {
        _inner = inviscidProvider
            ?? new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
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

        var upperMarch = RunSurfaceMarch(inv, upper: true, nu, nCrit);
        var lowerMarch = RunSurfaceMarch(inv, upper: false, nu, nCrit);

        // Phase-5-lite coupling probe (2026-04-21): attempted a single
        // viscous-inviscid iteration via displacement-body geometry
        // offset. Regressed 5 MSES tests (monotonicity, convergence)
        // because the δ*(s) interpolation in geometry arc-length
        // doesn't match the marcher's station arc-length closely
        // enough, and the re-solved Ue(x) caused spurious θ
        // oscillation. Disabled until a proper arc-length-aware
        // coupling is implemented (Phase 5 main). Helpers
        // TryBuildThickenedGeometry / InterpolateDStar /
        // ComputeSurfaceNormal are preserved below for the real
        // Phase-5 iteration.

        double cd = ComputeSquireYoungCd(upperMarch, lowerMarch, Uinf);

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

        var upperProfiles = BuildProfiles(upperMarch);
        var lowerProfiles = BuildProfiles(lowerMarch);

        return new ViscousAnalysisResult
        {
            LiftCoefficient = inv.LiftCoefficient,
            MomentCoefficient = inv.MomentCoefficientQuarterChord,
            DragDecomposition = new DragDecomposition
            {
                CD = cd,
                CDF = cd, // not separable without wake integration
                CDP = 0.0,
                CDSurfaceCrossCheck = cd,
                DiscrepancyMetric = 0.0,
                TEBaseDrag = 0.0,
                WaveDrag = null,
            },
            Converged = converged,
            Iterations = 1,
            AngleOfAttackDegrees = angleOfAttackDegrees,
            ConvergenceHistory = new System.Collections.Generic.List<ViscousConvergenceInfo>(),
            UpperProfiles = upperProfiles,
            LowerProfiles = lowerProfiles,
            WakeProfiles = System.Array.Empty<BoundaryLayerProfile>(),
            UpperTransition = BuildTransition(upperMarch),
            LowerTransition = BuildTransition(lowerMarch),
        };
    }

    private static BoundaryLayerProfile[] BuildProfiles(
        CompositeTransitionMarcher.CompositeResult r)
    {
        int n = r.Theta.Length;
        var profiles = new BoundaryLayerProfile[n];
        for (int i = 0; i < n; i++)
        {
            double h = r.H[i];
            double theta = r.Theta[i];
            profiles[i] = new BoundaryLayerProfile
            {
                Theta = theta,
                DStar = h * theta, // δ* = H·θ (incompressible)
                Ctau = r.CTau[i],
                EdgeVelocity = r.EdgeVelocity[i],
                Hk = h, // Me=0 → Hk = H
                AmplificationFactor = r.N[i],
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
            double dStar = InterpolateDStar(sAcc, upperMarch);
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
            double dStar = InterpolateDStar(sAcc, lowerMarch);
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
        // station grid. Clamp to endpoints if s is out of range.
        int n = march.Theta.Length;
        if (n < 2) return 0.0;
        // We don't have station s-values in CompositeResult; the
        // marcher assumes stations are the ones the caller passed.
        // Approximate: s index via linear scan vs. chord fraction.
        // Since the caller's station arc-length matches the geometry
        // surface, this is the same arc length.
        // Fallback: linearly distribute stations over [0, chord-length]
        // of the surface — close enough for Phase-5-lite.
        // A cleaner version would thread the station array through
        // CompositeResult; deferred.
        if (s <= 0) return march.H[0] * march.Theta[0];
        if (s >= 1.0) return march.H[n - 1] * march.Theta[n - 1]; // assumes unit chord
        double frac = s / 1.0;
        int iLo = System.Math.Clamp((int)(frac * (n - 1)), 0, n - 2);
        double t = frac * (n - 1) - iLo;
        double dStarLo = march.H[iLo] * march.Theta[iLo];
        double dStarHi = march.H[iLo + 1] * march.Theta[iLo + 1];
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
        InviscidAnalysisResult inv, bool upper, double nu, double nCrit)
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

        return CompositeTransitionMarcher.March(s, ue, nu, nCrit);
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

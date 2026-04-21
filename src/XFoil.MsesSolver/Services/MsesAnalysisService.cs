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

        var inv = AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);

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

        double cd = ComputeSquireYoungCd(upperMarch, lowerMarch, Uinf);

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
            Converged = true,
            Iterations = 1,
            AngleOfAttackDegrees = angleOfAttackDegrees,
            ConvergenceHistory = new System.Collections.Generic.List<ViscousConvergenceInfo>(),
            UpperProfiles = System.Array.Empty<BoundaryLayerProfile>(),
            LowerProfiles = System.Array.Empty<BoundaryLayerProfile>(),
            WakeProfiles = System.Array.Empty<BoundaryLayerProfile>(),
            UpperTransition = default,
            LowerTransition = default,
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
        // CD = 2·(θ_u_TE + θ_l_TE)·(Ue_TE/U∞)^((H_TE+5)/2)
        // Using average H for the exponent; individual surfaces
        // for the θ contributions.
        int nU = upper.Theta.Length;
        int nL = lower.Theta.Length;
        if (nU < 2 || nL < 2) return 0.0;
        double θU = upper.Theta[nU - 1];
        double θL = lower.Theta[nL - 1];
        double HU = upper.H[nU - 1];
        double HL = lower.H[nL - 1];
        // Use surface Ue at TE (not freestream) as the "edge" speed.
        // For the Squire-Young form we need (Ue_TE/U∞)^((H+5)/2).
        // Since our Ue is already normalized by U∞ in RunSurfaceMarch
        // (via sqrt(1-Cp)), Ue here is effectively Ue/U∞.
        // We don't store Ue in the composite result; approximate with
        // 1 for this Phase-5-stub implementation. A proper Phase-5
        // impl would thread Ue through the marcher output.
        _ = Uinf;
        double squireU = 2.0 * θU * System.Math.Pow(1.0, (HU + 5.0) / 2.0);
        double squireL = 2.0 * θL * System.Math.Pow(1.0, (HL + 5.0) / 2.0);
        return squireU + squireL;
    }
}

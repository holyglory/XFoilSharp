using XFoil.ThesisClosureSolver.Coupling;

namespace XFoil.Core.Tests;

/// <summary>
/// Unit tests for the source-distribution coupling helpers
/// (Phase-5 viscous-inviscid coupling building blocks).
/// </summary>
public class SourceDistributionCouplingTests
{
    [Fact]
    public void ComputeDisplacementSource_LinearGrowth_ReturnsPositiveConstant()
    {
        // δ*(s) = 0.01·s on s ∈ [0, 1] with Ue=1 gives
        // σ = d(Ue·δ*)/ds = d(0.01·s)/ds = 0.01 everywhere.
        int n = 21;
        var stations = new double[n];
        var ue = new double[n];
        var dStar = new double[n];
        for (int i = 0; i < n; i++)
        {
            stations[i] = i / (double)(n - 1);
            ue[i] = 1.0;
            dStar[i] = 0.01 * stations[i];
        }
        var sigma = SourceDistributionCoupling.ComputeDisplacementSource(stations, ue, dStar);
        for (int i = 0; i < n; i++)
        {
            Assert.True(System.Math.Abs(sigma[i] - 0.01) < 1e-9,
                $"station {i}: σ={sigma[i]:F6}, expected 0.01");
        }
    }

    [Fact]
    public void ComputeDisplacementSource_UeAcceleration_ReflectsProductRule()
    {
        // δ*(s) = 0.01, Ue(s) = 1 + s.
        // Ue·δ* = 0.01·(1+s), d/ds = 0.01 constant.
        int n = 21;
        var stations = new double[n];
        var ue = new double[n];
        var dStar = new double[n];
        for (int i = 0; i < n; i++)
        {
            stations[i] = i / (double)(n - 1);
            ue[i] = 1.0 + stations[i];
            dStar[i] = 0.01;
        }
        var sigma = SourceDistributionCoupling.ComputeDisplacementSource(stations, ue, dStar);
        for (int i = 0; i < n; i++)
        {
            Assert.True(System.Math.Abs(sigma[i] - 0.01) < 1e-9,
                $"station {i}: σ={sigma[i]:F6}, expected 0.01");
        }
    }

    [Fact]
    public void IntegrateSourceUeDelta_UniformPositiveSource_MostlyZeroFarFromEdges()
    {
        // Uniform σ=1 on s ∈ [0, 1]: by symmetry the Hilbert
        // integral at the midpoint cancels (equal positive
        // contributions from downstream and upstream with opposite
        // denominator signs). Magnitude stays small except near
        // the endpoints.
        int n = 21;
        var stations = new double[n];
        var sigma = new double[n];
        for (int i = 0; i < n; i++)
        {
            stations[i] = i / (double)(n - 1);
            sigma[i] = 1.0;
        }
        var dUe = SourceDistributionCoupling.IntegrateSourceUeDelta(stations, sigma);
        // Midpoint should be near zero.
        int mid = n / 2;
        Assert.True(System.Math.Abs(dUe[mid]) < 0.1,
            $"Midpoint |ΔUe| should be small under uniform σ; got {dUe[mid]:F4}");
    }

    [Fact]
    public void IntegrateSourceUeDelta_AdverseGrowth_HasHilbertSignStructure()
    {
        // δ* ramping on s ∈ [0.5, 1] → σ is a step-like pulse on
        // that interval. The Hilbert transform of a step σ over
        // [a, b] evaluated inside [a, b] is
        //   ΔUe(s) ∝ ln((s − a) / (b − s))
        // which is NEGATIVE for s near a (forward of centroid),
        // zero at the centroid (s = (a+b)/2 = 0.75), and POSITIVE
        // for s near b (aft of centroid). The corresponding
        // physical picture: BL displacement decelerates flow just
        // ahead of the growth region and accelerates it through
        // the downstream end. This is the sign structure that
        // makes source-distribution coupling reduce CL on
        // cambered airfoils (the suction-surface δ* grows fastest
        // near the TE; the aft ΔUe > 0 region contributes
        // differently to ∮Cp·dx than geometric thickening does).
        int n = 51;
        var stations = new double[n];
        var ue = new double[n];
        var dStar = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = i / (double)(n - 1);
            stations[i] = s;
            ue[i] = 1.0;
            dStar[i] = s < 0.5 ? 0.0 : 0.02 * (s - 0.5) / 0.5;
        }
        var sigma = SourceDistributionCoupling.ComputeDisplacementSource(stations, ue, dStar);
        var dUe = SourceDistributionCoupling.IntegrateSourceUeDelta(stations, sigma);

        // Forward of σ support: ΔUe < 0 (pure downstream source
        // contributions with s − ξ < 0).
        int i30 = (int)System.Math.Round(0.30 * (n - 1));
        Assert.True(dUe[i30] < 0.0,
            $"ΔUe at s=0.30 (forward of σ support) should be negative; got {dUe[i30]:F4}");

        // Centroid of σ support ≈ 0.75: sign crossing.
        int i70 = (int)System.Math.Round(0.70 * (n - 1));
        int i85 = (int)System.Math.Round(0.85 * (n - 1));
        Assert.True(dUe[i70] < dUe[i85],
            $"ΔUe should grow from forward (s=0.7, {dUe[i70]:F4}) to aft (s=0.85, {dUe[i85]:F4})");
        Assert.True(dUe[i85] > 0.0,
            $"ΔUe at s=0.85 (aft of centroid) should be positive; got {dUe[i85]:F4}");
    }

    [Fact]
    public void IntegrateSourceUeDelta_ZeroSource_ReturnsZeroPerturbation()
    {
        // σ = 0 ⇒ ΔUe = 0 identically. Numerically, not structurally
        // (the loop still runs, so verify it returns zeros).
        int n = 11;
        var stations = new double[n];
        var sigma = new double[n];
        for (int i = 0; i < n; i++) stations[i] = i / (double)(n - 1);
        var dUe = SourceDistributionCoupling.IntegrateSourceUeDelta(stations, sigma);
        foreach (var d in dUe) Assert.Equal(0.0, d);
    }

    [Fact]
    public void ComputeDisplacementSource_RejectsShortInput()
    {
        Assert.Throws<System.ArgumentException>(() =>
            SourceDistributionCoupling.ComputeDisplacementSource(
                new[] { 0.0 }, new[] { 1.0 }, new[] { 0.01 }));
    }

    [Fact]
    public void ComputeDisplacementSource_RejectsLengthMismatch()
    {
        Assert.Throws<System.ArgumentException>(() =>
            SourceDistributionCoupling.ComputeDisplacementSource(
                new[] { 0.0, 0.5, 1.0 },
                new[] { 1.0, 1.0 },
                new[] { 0.01, 0.01, 0.01 }));
    }
}

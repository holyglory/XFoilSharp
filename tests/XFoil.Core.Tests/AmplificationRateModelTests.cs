using XFoil.ThesisClosureSolver.Closure;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase-3 scaffolding tests for Drela's e^N amplification rate.
/// Pins the neutral-stability boundary, the Hk-dependent rate slope,
/// sub-critical zero output, and the n-factor constants.
/// </summary>
public class AmplificationRateModelTests
{
    [Fact]
    public void ComputeReThetaCritical_FlatPlateBlasius_MatchesClassicalValue()
    {
        // Blasius (Hk=2.59) neutral-stability Reθ ≈ 520 per classical
        // Orr-Sommerfeld analysis. Drela's fit gives a close value.
        double actual = AmplificationRateModel.ComputeReThetaCritical(2.59);
        Assert.InRange(actual, 200.0, 800.0);
    }

    [Fact]
    public void ComputeReThetaCritical_DropsAsHkRises()
    {
        // Adverse gradient (Hk↑) destabilizes TS waves — neutral Reθ
        // drops. This is why transition moves upstream as the flow
        // approaches separation.
        double reLow = AmplificationRateModel.ComputeReThetaCritical(2.2);
        double reHigh = AmplificationRateModel.ComputeReThetaCritical(3.5);
        Assert.True(reHigh < reLow,
            $"Expected neutral-Reθ drop from Hk=2.2 ({reLow}) to Hk=3.5 ({reHigh})");
    }

    [Fact]
    public void ComputeDAmplificationDReTheta_Positive()
    {
        // Rate must be non-negative (physical growth, not decay).
        for (double Hk = 1.5; Hk <= 5.0; Hk += 0.25)
        {
            double rate = AmplificationRateModel.ComputeDAmplificationDReTheta(Hk);
            Assert.True(rate >= 0.0, $"dÑ/dReθ negative at Hk={Hk}: {rate}");
        }
    }

    [Fact]
    public void ComputeAmplificationRate_BelowNeutralReθ_ReturnsZero()
    {
        // Sub-critical Reθ: flow is stable, no amplification.
        double reCrit = AmplificationRateModel.ComputeReThetaCritical(2.59);
        double actual = AmplificationRateModel.ComputeAmplificationRate(
            Hk: 2.59, ReTheta: 0.5 * reCrit, theta: 1e-3);
        Assert.Equal(0.0, actual);
    }

    [Fact]
    public void ComputeAmplificationRate_AboveNeutralReθ_Positive()
    {
        // Super-critical Reθ: TS waves grow.
        double reCrit = AmplificationRateModel.ComputeReThetaCritical(2.59);
        double actual = AmplificationRateModel.ComputeAmplificationRate(
            Hk: 2.59, ReTheta: 3.0 * reCrit, theta: 1e-3);
        Assert.True(actual > 0, $"Expected positive Ñ growth at Re=3·Reθ₀, got {actual}");
    }

    [Fact]
    public void ComputeAmplificationRate_DegenerateInputs_ReturnsZero()
    {
        // θ=0 or negative — no BL to amplify through.
        double actual = AmplificationRateModel.ComputeAmplificationRate(
            Hk: 2.59, ReTheta: 1000.0, theta: 0.0);
        Assert.Equal(0.0, actual);
    }

    [Fact]
    public void NCritConstants_HaveExpectedValues()
    {
        Assert.Equal(9.0, AmplificationRateModel.NCritStandard);
        Assert.Equal(11.0, AmplificationRateModel.NCritQuietTunnel);
    }
}

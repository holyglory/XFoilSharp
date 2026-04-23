using XFoil.MsesSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.4 — per-side global state pack/unpack tests.
/// </summary>
public class MsesGlobalStateSidedTests
{
    [Fact]
    public void Constructor_ComputesLayoutCorrectly()
    {
        // 5 panels (6 γ + 6 σ_airfoil), 10 wake panels (Nw=10),
        // upper=3, lower=2. 6+6+10+3·(3+2+10) = 67.
        var s = new MsesGlobalStateSided(6, 6, 10, 3, 2, 10);
        Assert.Equal(0, s.GammaOffset);
        Assert.Equal(6, s.SigmaAirfoilOffset);
        Assert.Equal(12, s.SigmaWakeOffset);
        Assert.Equal(22, s.UpperDstarOffset);
        Assert.Equal(25, s.UpperThetaOffset);
        Assert.Equal(28, s.UpperCTauOffset);
        Assert.Equal(31, s.LowerDstarOffset);
        Assert.Equal(33, s.LowerThetaOffset);
        Assert.Equal(35, s.LowerCTauOffset);
        Assert.Equal(37, s.WakeDstarOffset);
        Assert.Equal(47, s.WakeThetaOffset);
        Assert.Equal(57, s.WakeCTauOffset);
        Assert.Equal(67, s.StateSize);
    }

    [Fact]
    public void Pack_Unpack_RoundTrip()
    {
        var layout = new MsesGlobalStateSided(3, 3, 4, 2, 2, 4);
        var state = new MsesGlobalStateSided.SidedState(
            Gamma: new[] { 1.0, 2.0, 3.0 },
            SigmaAirfoil: new[] { 4.0, 5.0, 6.0 },
            SigmaWake: new[] { 7.0, 8.0, 9.0, 10.0 },
            UpperDstar: new[] { 11.0, 12.0 },
            UpperTheta: new[] { 13.0, 14.0 },
            UpperCTau: new[] { 15.0, 16.0 },
            LowerDstar: new[] { 17.0, 18.0 },
            LowerTheta: new[] { 19.0, 20.0 },
            LowerCTau: new[] { 21.0, 22.0 },
            WakeDstar: new[] { 23.0, 24.0, 25.0, 26.0 },
            WakeTheta: new[] { 27.0, 28.0, 29.0, 30.0 },
            WakeCTau: new[] { 31.0, 32.0, 33.0, 34.0 });
        var packed = layout.Pack(state);
        Assert.Equal(layout.StateSize, packed.Length);

        var unpacked = layout.Unpack(packed);
        Assert.Equal(state.Gamma, unpacked.Gamma);
        Assert.Equal(state.SigmaAirfoil, unpacked.SigmaAirfoil);
        Assert.Equal(state.SigmaWake, unpacked.SigmaWake);
        Assert.Equal(state.UpperDstar, unpacked.UpperDstar);
        Assert.Equal(state.UpperTheta, unpacked.UpperTheta);
        Assert.Equal(state.UpperCTau, unpacked.UpperCTau);
        Assert.Equal(state.LowerDstar, unpacked.LowerDstar);
        Assert.Equal(state.LowerTheta, unpacked.LowerTheta);
        Assert.Equal(state.LowerCTau, unpacked.LowerCTau);
        Assert.Equal(state.WakeDstar, unpacked.WakeDstar);
        Assert.Equal(state.WakeTheta, unpacked.WakeTheta);
        Assert.Equal(state.WakeCTau, unpacked.WakeCTau);
    }

    [Fact]
    public void Pack_WrongLength_Throws()
    {
        var layout = new MsesGlobalStateSided(3, 3, 2, 2, 2, 2);
        var state = new MsesGlobalStateSided.SidedState(
            Gamma: new[] { 1.0 },  // wrong!
            SigmaAirfoil: new[] { 1.0, 2.0, 3.0 },
            SigmaWake: new[] { 1.0, 2.0 },
            UpperDstar: new[] { 1.0, 2.0 },
            UpperTheta: new[] { 1.0, 2.0 },
            UpperCTau: new[] { 1.0, 2.0 },
            LowerDstar: new[] { 1.0, 2.0 },
            LowerTheta: new[] { 1.0, 2.0 },
            LowerCTau: new[] { 1.0, 2.0 },
            WakeDstar: new[] { 1.0, 2.0 },
            WakeTheta: new[] { 1.0, 2.0 },
            WakeCTau: new[] { 1.0, 2.0 });
        Assert.Throws<System.ArgumentException>(() => layout.Pack(state));
    }

    [Fact]
    public void Unpack_WrongLength_Throws()
    {
        var layout = new MsesGlobalStateSided(3, 3, 2, 2, 2, 2);
        Assert.Throws<System.ArgumentException>(
            () => layout.Unpack(new double[5]));
    }

    [Fact]
    public void Constructor_NonPositive_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new MsesGlobalStateSided(0, 1, 1, 1, 1, 1));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new MsesGlobalStateSided(1, 1, 1, 0, 1, 1));
    }

    [Fact]
    public void Constructor_ZeroWakeIsOk()
    {
        // Wake can be zero (e.g. if we want to test airfoil-only
        // coupling without the wake). Upper/lower still required.
        var layout = new MsesGlobalStateSided(2, 2, 0, 1, 1, 0);
        Assert.Equal(2 + 2 + 0 + 3 * (1 + 1 + 0), layout.StateSize);
    }
}

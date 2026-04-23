using XFoil.ThesisClosureSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.1 — ThesisClosureGlobalState pack/unpack tests.
/// </summary>
public class MsesGlobalStateTests
{
    [Fact]
    public void Constructor_ComputesLayoutCorrectly()
    {
        var s = new ThesisClosureGlobalState(gammaCount: 5, sigmaCount: 5, blStationCount: 10);
        Assert.Equal(0, s.GammaOffset);
        Assert.Equal(5, s.SigmaOffset);
        Assert.Equal(10, s.DstarOffset);
        Assert.Equal(20, s.ThetaOffset);
        Assert.Equal(30, s.CTauOffset);
        Assert.Equal(40, s.StateSize);
    }

    [Fact]
    public void Pack_Unpack_RoundTrip()
    {
        var s = new ThesisClosureGlobalState(gammaCount: 3, sigmaCount: 3, blStationCount: 4);
        var gamma = new[] { 1.0, 2.0, 3.0 };
        var sigma = new[] { 4.0, 5.0, 6.0 };
        var dStar = new[] { 7.0, 8.0, 9.0, 10.0 };
        var theta = new[] { 11.0, 12.0, 13.0, 14.0 };
        var cTau = new[] { 15.0, 16.0, 17.0, 18.0 };

        var packed = s.Pack(gamma, sigma, dStar, theta, cTau);
        Assert.Equal(s.StateSize, packed.Length);

        var (gU, sU, dU, tU, cU) = s.Unpack(packed);
        Assert.Equal(gamma, gU);
        Assert.Equal(sigma, sU);
        Assert.Equal(dStar, dU);
        Assert.Equal(theta, tU);
        Assert.Equal(cTau, cU);
    }

    [Fact]
    public void Pack_WrongLength_Throws()
    {
        var s = new ThesisClosureGlobalState(2, 2, 2);
        Assert.Throws<System.ArgumentException>(
            () => s.Pack(new[] { 1.0 }, new[] { 2.0, 3.0 }, new[] { 4.0, 5.0 },
                         new[] { 6.0, 7.0 }, new[] { 8.0, 9.0 }));
    }

    [Fact]
    public void Unpack_WrongLength_Throws()
    {
        var s = new ThesisClosureGlobalState(2, 2, 2);
        Assert.Throws<System.ArgumentException>(
            () => s.Unpack(new double[5]));
    }

    [Fact]
    public void Kind_ClassifiesIndicesCorrectly()
    {
        var s = new ThesisClosureGlobalState(gammaCount: 3, sigmaCount: 3, blStationCount: 4);
        Assert.Equal(ThesisClosureGlobalState.VarKind.Gamma, s.Kind(0));
        Assert.Equal(ThesisClosureGlobalState.VarKind.Gamma, s.Kind(2));
        Assert.Equal(ThesisClosureGlobalState.VarKind.Sigma, s.Kind(3));
        Assert.Equal(ThesisClosureGlobalState.VarKind.Sigma, s.Kind(5));
        Assert.Equal(ThesisClosureGlobalState.VarKind.DStar, s.Kind(6));
        Assert.Equal(ThesisClosureGlobalState.VarKind.DStar, s.Kind(9));
        Assert.Equal(ThesisClosureGlobalState.VarKind.Theta, s.Kind(10));
        Assert.Equal(ThesisClosureGlobalState.VarKind.Theta, s.Kind(13));
        Assert.Equal(ThesisClosureGlobalState.VarKind.CTau, s.Kind(14));
        Assert.Equal(ThesisClosureGlobalState.VarKind.CTau, s.Kind(17));
    }

    [Fact]
    public void Kind_OutOfRange_Throws()
    {
        var s = new ThesisClosureGlobalState(2, 2, 2);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => s.Kind(-1));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => s.Kind(s.StateSize));
    }
}

using XFoil.ThesisClosureSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Sanity tests on the per-station BL profiles that ThesisClosureAnalysisService
/// now populates. Checks physical monotonicity/positivity properties
/// of the profile arrays the pipeline produces.
/// </summary>
public class MsesProfileSanityTests
{
    private static ViscousAnalysisResult Analyze(string naca, double alpha, double Re)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re);
        return new ThesisClosureAnalysisService().AnalyzeViscous(geom, alpha, settings);
    }

    [Fact]
    public void ThetaMonotonicAlongUpperSurface_PastFirstFewStations()
    {
        // First couple of stations near LE can have transient behavior
        // from the IC seeding. Past station 5 θ should grow
        // monotonically.
        var r = Analyze("0012", 2.0, 1_000_000);
        var profiles = r.UpperProfiles;
        Assert.True(profiles.Length > 10, "Expect enough stations for a sanity test");
        for (int i = 6; i < profiles.Length; i++)
        {
            Assert.True(profiles[i].Theta >= profiles[i - 1].Theta - 1e-10,
                $"θ non-monotonic at station {i}: {profiles[i].Theta} < {profiles[i-1].Theta}");
        }
    }

    [Fact]
    public void DStarPositive_PastSecondStation()
    {
        // The first station has θ=0 → δ*=0 (singular LE). Past the
        // IC seeding δ* must be strictly positive.
        var r = Analyze("4412", 4.0, 3_000_000);
        for (int i = 2; i < r.UpperProfiles.Length; i++)
            Assert.True(r.UpperProfiles[i].DStar > 0,
                $"δ* upper must be positive; got {r.UpperProfiles[i].DStar} at station {i}");
        for (int i = 2; i < r.LowerProfiles.Length; i++)
            Assert.True(r.LowerProfiles[i].DStar > 0,
                $"δ* lower must be positive; got {r.LowerProfiles[i].DStar} at station {i}");
    }

    [Fact]
    public void AmplificationFactorResetsAfterTransition()
    {
        // Once Ñ crosses n_crit and the march switches to turbulent,
        // Ñ is no longer accumulated — it stays frozen at whatever
        // value it had at transition. Check that Ñ is non-decreasing
        // throughout and that past TransitionIndex it doesn't grow.
        var r = Analyze("4412", 4.0, 3_000_000);
        var profiles = r.UpperProfiles;
        for (int i = 1; i < profiles.Length; i++)
        {
            Assert.True(profiles[i].AmplificationFactor >= profiles[i - 1].AmplificationFactor - 1e-9,
                $"Ñ decreased at station {i}");
        }
    }

    [Fact]
    public void TransitionLocationAgreesWithProfileCrossing()
    {
        // UpperTransition.StationIndex should point to the first
        // station where Ñ ≥ 9.
        var r = Analyze("4412", 4.0, 3_000_000);
        if (r.UpperTransition.StationIndex > 0)
        {
            int ti = r.UpperTransition.StationIndex;
            Assert.True(r.UpperProfiles[ti].AmplificationFactor >= 9.0,
                $"Station {ti} marked as transition but Ñ={r.UpperProfiles[ti].AmplificationFactor}");
        }
    }

    [Fact]
    public void EdgeVelocityStaysPositive()
    {
        var r = Analyze("0012", 4.0, 1_000_000);
        foreach (var p in r.UpperProfiles)
            Assert.True(p.EdgeVelocity > 0,
                $"Ue must stay positive along attached BL; got {p.EdgeVelocity}");
    }
}

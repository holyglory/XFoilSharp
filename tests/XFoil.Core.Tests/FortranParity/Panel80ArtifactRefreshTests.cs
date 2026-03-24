using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/cases/n0012_re1e6_a0_p80.in, n0012_re1e6_a5_p80.in, n0012_re1e6_a10_p80.in
// Secondary legacy source: tests/XFoil.Core.Tests/FortranParity/FortranReferenceCases.cs managed artifact harness
// Role in port: Small-rung parity harness tests that generate reduced-panel managed artifacts for alpha=0/5/10 without paying the full 160-panel cost.
// Differences: Classic XFoil had no unit-test entry points for artifact refresh; these tests exist purely to make the smaller-case parity ladder runnable under `dotnet test`.
// Decision: Keep the reduced-panel artifact refresh tests because they let the parity workflow run simpler cases in parallel before moving back up to the heavier alpha-10 ladder.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
public sealed class Panel80ArtifactRefreshTests
{
    [Fact]
    // Legacy mapping: reduced-panel NACA 0012 alpha=0 reference case driver.
    // Difference from legacy: The test only refreshes managed artifacts; comparison stays in the focused parity tests and scripts. Decision: Keep the harness because it turns the small a=0 rung into a cheap reproducible test target.
    public void Naca0012_Re1e6_Alpha0_P80_RefreshManagedArtifacts()
    {
        RefreshAndAssertArtifacts("n0012_re1e6_a0_p80");
    }

    [Fact]
    // Legacy mapping: reduced-panel NACA 0012 alpha=5 reference case driver.
    // Difference from legacy: The managed harness emits structured artifacts instead of interactive OPER output. Decision: Keep the harness because alpha=5 is the middle rung between the easy baseline and the current alpha-10 target.
    public void Naca0012_Re1e6_Alpha5_P80_RefreshManagedArtifacts()
    {
        RefreshAndAssertArtifacts("n0012_re1e6_a5_p80");
    }

    [Fact]
    // Legacy mapping: reduced-panel NACA 0012 alpha=10 reference case driver.
    // Difference from legacy: The test exists only to refresh the small alpha-10 managed artifacts that feed the focused parity descent. Decision: Keep the harness because it makes the current viscous-debug target much cheaper to rerun.
    public void Naca0012_Re1e6_Alpha10_P80_RefreshManagedArtifacts()
    {
        RefreshAndAssertArtifacts("n0012_re1e6_a10_p80");
    }

    private static void RefreshAndAssertArtifacts(string caseId)
    {
        FortranReferenceCases.RefreshManagedArtifacts(caseId);

        string tracePath = FortranReferenceCases.GetManagedTracePath(caseId);
        string dumpPath = FortranReferenceCases.GetManagedDumpPath(caseId);
        Assert.True(File.Exists(tracePath), $"Managed trace missing for case {caseId}: {tracePath}");
        Assert.True(File.Exists(dumpPath), $"Managed dump missing for case {caseId}: {dumpPath}");
    }
}

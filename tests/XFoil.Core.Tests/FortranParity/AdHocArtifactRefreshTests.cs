using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/run_reference.sh and the OPER/VPAR command flow in f_xfoil/src/xoper.f
// Secondary legacy source: tests/XFoil.Core.Tests/FortranParity/FortranReferenceCases.cs ad hoc managed artifact harness
// Role in port: Environment-driven entry point for cheap parity scouting runs that vary panel count, Reynolds number, alpha, and Ncrit without adding new fixed test cases.
// Differences: Classic XFoil had no unit-test runner for parameterized artifact generation; this exists purely to make small focused parity runs scriptable.
// Decision: Keep the ad hoc runner because it lets the parity workflow reuse the managed harness while staying cheap enough for repeated focused probes.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
public sealed class AdHocArtifactRefreshTests
{
    [Fact]
    public void RefreshManagedArtifacts_FromEnvironment()
    {
        FortranReferenceCase definition = FortranReferenceCases.FromEnvironment();
        FortranReferenceCases.RefreshManagedArtifacts(definition);

        string tracePath = FortranReferenceCases.GetManagedTracePath(definition.CaseId);
        string dumpPath = FortranReferenceCases.GetManagedDumpPath(definition.CaseId);
        string reportPath = FortranReferenceCases.GetManagedParityReportPath(definition.CaseId);

        Assert.True(File.Exists(tracePath), $"Managed trace missing for case {definition.CaseId}: {tracePath}");
        Assert.True(File.Exists(dumpPath), $"Managed dump missing for case {definition.CaseId}: {dumpPath}");
        if (File.Exists(FortranReferenceCases.GetReferenceDumpPath(definition.CaseId)))
        {
            Assert.True(File.Exists(reportPath), $"Managed parity report missing for case {definition.CaseId}: {reportPath}");
        }
    }
}

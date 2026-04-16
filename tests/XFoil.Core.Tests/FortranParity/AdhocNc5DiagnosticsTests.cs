using System.IO;
using Xunit;

namespace XFoil.Core.Tests.FortranParity;

public sealed class AdhocNc5DiagnosticsTests
{
    [Fact]
    public void GenerateAdhocNc5LegacySeedArtifacts()
    {
        const string caseId = "adhoc_n0003_re500000_am2_p160_n5";
        var definition = new FortranReferenceCase(
            CaseId: caseId,
            AirfoilCode: "0003",
            ReynoldsNumber: 500000.0,
            AlphaDegrees: -2.0,
            DisplayName: "Ad hoc NACA 0003 Re=500000 alpha=-2 panel=160 Ncrit=5",
            PanelCount: 160,
            MaxViscousIterations: 80,
            CriticalAmplificationFactor: 5.0,
            TraceKindAllowList: "legacy_seed_iteration,legacy_seed_final_system,legacy_seed_final_delta,legacy_seed_final,pangen_newton_state,pangen_newton_row,pangen_newton_delta,pangen_panel_node");

        FortranReferenceCases.RefreshManagedArtifacts(definition);

        Assert.True(File.Exists(FortranReferenceCases.GetManagedTracePath(caseId)));
        Assert.True(File.Exists(FortranReferenceCases.GetManagedDumpPath(caseId)));
    }
}

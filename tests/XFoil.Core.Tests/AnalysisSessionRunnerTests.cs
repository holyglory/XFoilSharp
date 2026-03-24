using System.Text.Json;
using XFoil.IO.Models;
using XFoil.IO.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: OPER, ASEQ, CSEQ
// Secondary legacy source: f_xfoil/src/polfit.f and saved-polar file workflows
// Role in port: Verifies the managed batch-session runner that composes legacy-derived solver and import/export operations into reproducible manifests.
// Differences: The legacy program is interactive and file-command driven, while the managed runner serializes whole sessions from JSON manifests and artifact policies.
// Decision: Keep the managed orchestration layer and validate that it correctly wraps the legacy-derived analysis behaviors.
namespace XFoil.Core.Tests;

public sealed class AnalysisSessionRunnerTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ/CSEQ operating workflows.
    // Difference from legacy: This test exercises a managed manifest runner that batches multiple analyses rather than stepping through the legacy command interface.
    // Decision: Keep the managed session-level regression because batch automation is an intentional port extension over the same underlying analysis lineage.
    public void Run_CreatesSummaryAndRequestedArtifacts()
    {
        var runner = new AnalysisSessionRunner();
        var root = Path.Combine(Path.GetTempPath(), $"xfoil-session-{Guid.NewGuid():N}");
        var manifestPath = Path.Combine(root, "session.json");
        var outputDirectory = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);
            var manifest = new AnalysisSessionManifest
            {
                Name = "Regression Session",
                Airfoil = new SessionAirfoilDefinition
                {
                    Naca = "0012",
                },
                Sweeps = new[]
                {
                    new SessionSweepDefinition
                    {
                        Name = "alpha-baseline",
                        Kind = "inviscid-alpha",
                        Output = "alpha-baseline.csv",
                        Start = 0d,
                        End = 2d,
                        Step = 2d,
                        PanelCount = 100,
                    },
                    new SessionSweepDefinition
                    {
                        Name = "cl-coupled",
                        Kind = "viscous-cl",
                        Output = "cl-coupled.csv",
                        Start = 0.1d,
                        End = 0.3d,
                        Step = 0.2d,
                        PanelCount = 100,
                        ReynoldsNumber = 500_000d,
                        CouplingIterations = 1,
                        ViscousIterations = 4,
                        ResidualTolerance = 1.0d,
                        DisplacementRelaxation = 0.3d,
                        TransitionReynoldsTheta = 100d,
                        CriticalAmplificationFactor = 3d,
                    },
                },
            };

            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            var result = runner.Run(manifestPath, outputDirectory);

            Assert.Equal("Regression Session", result.SessionName);
            Assert.Equal(2, result.Artifacts.Count);
            Assert.True(File.Exists(result.SummaryPath));

            var alphaArtifact = result.Artifacts.Single(artifact => artifact.Kind == "inviscid-alpha");
            var viscousLiftArtifact = result.Artifacts.Single(artifact => artifact.Kind == "viscous-cl");
            Assert.True(File.Exists(alphaArtifact.OutputPath));
            Assert.True(File.Exists(viscousLiftArtifact.OutputPath));

            var alphaContent = File.ReadAllText(alphaArtifact.OutputPath);
            var viscousLiftContent = File.ReadAllText(viscousLiftArtifact.OutputPath);
            Assert.Contains("# Kind: InviscidAlphaSweep", alphaContent);
            Assert.Contains("alpha,CL,CD,CM,converged", viscousLiftContent);

            var summaryContent = File.ReadAllText(result.SummaryPath);
            Assert.Contains("\"SessionName\": \"Regression Session\"", summaryContent);
            Assert.Contains("\"Kind\": \"inviscid-alpha\"", summaryContent);
            Assert.Contains("\"Kind\": \"viscous-cl\"", summaryContent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    // Legacy mapping: saved-polar import/export workflows derived from legacy polar files.
    // Difference from legacy: The managed session runner imports a legacy artifact inside a manifest-driven pipeline instead of through interactive file commands.
    // Decision: Keep the managed integration test because it protects the compatibility wrapper around legacy polar data.
    public void Run_ImportsLegacyPolarArtifactIntoSession()
    {
        var runner = new AnalysisSessionRunner();
        var root = Path.Combine(Path.GetTempPath(), $"xfoil-legacy-session-{Guid.NewGuid():N}");
        var manifestPath = Path.Combine(root, "session.json");
        var outputDirectory = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);
            var legacyInputPath = TestDataPaths.GetRunsFixturePath("e387_09.100");
            var manifest = new AnalysisSessionManifest
            {
                Name = "Legacy Import Session",
                Sweeps = new[]
                {
                    new SessionSweepDefinition
                    {
                        Name = "legacy-e387",
                        Kind = "legacy-polar-import",
                        InputPath = legacyInputPath,
                        Output = "legacy-e387.csv",
                    },
                },
            };

            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            var result = runner.Run(manifestPath, outputDirectory);

            Assert.Equal("Legacy Import Session", result.SessionName);
            Assert.Equal("Imported legacy polar", result.GeometryName);
            Assert.Single(result.Artifacts);
            Assert.Equal("legacy-polar-import", result.Artifacts[0].Kind);
            Assert.True(File.Exists(result.Artifacts[0].OutputPath));

            var csv = File.ReadAllText(result.Artifacts[0].OutputPath);
            Assert.Contains("# Kind: LegacySavedPolarImport", csv);
            Assert.Contains("alpha,CL,CD,CDp,CM,Top_Xtr,Bot_Xtr", csv);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

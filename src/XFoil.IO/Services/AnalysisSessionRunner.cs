using System.Text;
using System.Text.Json;
using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.IO.Models;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: none
// Role in port: Managed orchestration layer that runs session manifests, invokes solver/import services, and writes summarized outputs.
// Differences: No direct Fortran analogue exists because the legacy workflow was interactive and command-driven; this runner composes multiple managed services into a batch/session API.
// Decision: Keep the managed orchestrator because it is a deliberate automation layer above the legacy solver and file formats.
namespace XFoil.IO.Services;

public sealed class AnalysisSessionRunner
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly AirfoilParser parser;
    private readonly NacaAirfoilGenerator nacaGenerator;
    private readonly AirfoilAnalysisService analysisService;
    private readonly PolarCsvExporter polarExporter;
    private readonly LegacyPolarImporter legacyPolarImporter;
    private readonly LegacyReferencePolarImporter legacyReferencePolarImporter;
    private readonly LegacyPolarDumpImporter legacyPolarDumpImporter;
    private readonly LegacyPolarDumpArchiveWriter legacyPolarDumpArchiveWriter;

    // Legacy mapping: none; managed-only convenience constructor for the session runner.
    // Difference from legacy: The legacy runtime did not construct a service graph; this overload wires together the default managed dependencies.
    // Decision: Keep the managed constructor because it simplifies batch use.
    public AnalysisSessionRunner()
        : this(
            new AirfoilParser(),
            new NacaAirfoilGenerator(),
            new AirfoilAnalysisService(),
            new PolarCsvExporter(),
            new LegacyPolarImporter(),
            new LegacyReferencePolarImporter(),
            new LegacyPolarDumpImporter(),
            new LegacyPolarDumpArchiveWriter())
    {
    }

    // Legacy mapping: none; managed-only dependency-injection constructor for the session runner.
    // Difference from legacy: The old runtime relied on global state rather than explicit injectable services.
    // Decision: Keep the managed constructor because it improves testability and composition.
    public AnalysisSessionRunner(
        AirfoilParser parser,
        NacaAirfoilGenerator nacaGenerator,
        AirfoilAnalysisService analysisService,
        PolarCsvExporter polarExporter,
        LegacyPolarImporter legacyPolarImporter,
        LegacyReferencePolarImporter legacyReferencePolarImporter,
        LegacyPolarDumpImporter legacyPolarDumpImporter,
        LegacyPolarDumpArchiveWriter legacyPolarDumpArchiveWriter)
    {
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
        this.nacaGenerator = nacaGenerator ?? throw new ArgumentNullException(nameof(nacaGenerator));
        this.analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        this.polarExporter = polarExporter ?? throw new ArgumentNullException(nameof(polarExporter));
        this.legacyPolarImporter = legacyPolarImporter ?? throw new ArgumentNullException(nameof(legacyPolarImporter));
        this.legacyReferencePolarImporter = legacyReferencePolarImporter ?? throw new ArgumentNullException(nameof(legacyReferencePolarImporter));
        this.legacyPolarDumpImporter = legacyPolarDumpImporter ?? throw new ArgumentNullException(nameof(legacyPolarDumpImporter));
        this.legacyPolarDumpArchiveWriter = legacyPolarDumpArchiveWriter ?? throw new ArgumentNullException(nameof(legacyPolarDumpArchiveWriter));
    }

    // Legacy mapping: none; managed-only batch-session entry point.
    // Difference from legacy: The method loads a JSON manifest, resolves geometry, dispatches sweeps/imports, and writes a summary object instead of interacting through command prompts and global state.
    // Decision: Keep the managed orchestration because it is the purpose of the session layer.
    public AnalysisSessionRunResult Run(string manifestPath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("A manifest path is required.", nameof(manifestPath));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        var manifest = LoadManifest(manifestPath);
        if (manifest.Airfoil is null && manifest.Sweeps.Any(SweepRequiresGeometry))
        {
            throw new InvalidOperationException("Session manifest must define an airfoil source for analysis sweeps.");
        }

        if (manifest.Sweeps.Count == 0)
        {
            throw new InvalidOperationException("Session manifest must define at least one sweep.");
        }

        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestFullPath) ?? Directory.GetCurrentDirectory();
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var geometry = manifest.Airfoil is null ? null : LoadGeometry(manifest.Airfoil, manifestDirectory);
        var artifacts = new List<AnalysisSessionArtifact>();

        // Legacy block: Managed-only session sweep dispatch over the manifest-defined work items.
        // Difference: The legacy workflow required manual command sequencing instead of one declarative loop over sweep definitions.
        // Decision: Keep the managed loop because it is the core value of the session runner.
        for (var index = 0; index < manifest.Sweeps.Count; index++)
        {
            var sweep = manifest.Sweeps[index];
            artifacts.Add(RunSweep(sweep, geometry, manifestDirectory, fullOutputDirectory, index));
        }

        var summaryPath = Path.Combine(fullOutputDirectory, "session-summary.json");
        var summary = new AnalysisSessionRunResult
        {
            SessionName = string.IsNullOrWhiteSpace(manifest.Name) ? geometry?.Name ?? "Legacy Polar Session" : manifest.Name,
            GeometryName = geometry?.Name ?? "Imported legacy polar",
            OutputDirectory = fullOutputDirectory,
            Artifacts = artifacts,
            SummaryPath = summaryPath,
        };

        var summaryJson = JsonSerializer.Serialize(summary, CreateWriteOptions());
        File.WriteAllText(summaryPath, summaryJson, Utf8WithoutBom);
        return summary;
    }

    // Legacy mapping: none; managed-only dispatcher from one manifest sweep kind to the appropriate service path.
    // Difference from legacy: The old runtime selected commands interactively, while the runner routes one normalized sweep kind through a switch expression.
    // Decision: Keep the managed dispatcher because it makes session behavior explicit.
    private AnalysisSessionArtifact RunSweep(SessionSweepDefinition sweep, AirfoilGeometry? geometry, string manifestDirectory, string outputDirectory, int index)
    {
        if (sweep is null)
        {
            throw new ArgumentNullException(nameof(sweep));
        }

        var kind = NormalizeKind(sweep.Kind);
        var artifactName = string.IsNullOrWhiteSpace(sweep.Name) ? $"{index + 1:D2}-{kind}" : sweep.Name;
        var outputFileName = string.IsNullOrWhiteSpace(sweep.Output)
            ? $"{SanitizeFileName(artifactName)}.csv"
            : sweep.Output;
        var outputPath = Path.Combine(outputDirectory, outputFileName);

        return kind switch
        {
            "inviscid-alpha" => ExportInviscidAlphaSweep(sweep, RequireGeometry(geometry), artifactName, outputPath),
            "inviscid-cl" => ExportInviscidLiftSweep(sweep, RequireGeometry(geometry), artifactName, outputPath),
            "viscous-alpha" => ExportViscousAlphaSweep(sweep, RequireGeometry(geometry), artifactName, outputPath),
            "viscous-cl" => ExportViscousLiftSweep(sweep, RequireGeometry(geometry), artifactName, outputPath),
            "legacy-polar-import" => ImportLegacyPolar(sweep, manifestDirectory, artifactName, outputPath),
            "legacy-reference-polar-import" => ImportLegacyReferencePolar(sweep, manifestDirectory, artifactName, outputPath),
            "legacy-polar-dump-import" => ImportLegacyPolarDump(sweep, manifestDirectory, artifactName, outputPath),
            _ => throw new InvalidOperationException($"Unsupported sweep kind '{sweep.Kind}'."),
        };
    }

    // Legacy mapping: none directly; managed wrapper around the legacy polar text importer.
    // Difference from legacy: The session runner resolves the input path, imports the file, and re-exports a normalized CSV artifact in one step.
    // Decision: Keep the managed wrapper because it is part of the session automation layer.
    private AnalysisSessionArtifact ImportLegacyPolar(SessionSweepDefinition sweep, string manifestDirectory, string artifactName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(sweep.InputPath))
        {
            throw new InvalidOperationException("Legacy polar import sweep requires 'inputPath'.");
        }

        var resolvedInputPath = Path.IsPathRooted(sweep.InputPath)
            ? sweep.InputPath
            : Path.Combine(manifestDirectory, sweep.InputPath);
        var legacyPolar = legacyPolarImporter.Import(resolvedInputPath);
        polarExporter.Export(outputPath, legacyPolar);
        return CreateArtifact(artifactName, "legacy-polar-import", outputPath, legacyPolar.Records.Count);
    }

    // Legacy mapping: none directly; managed wrapper around the reference-polar importer.
    // Difference from legacy: This path belongs to the managed verification/tooling layer and has no interactive XFoil counterpart.
    // Decision: Keep the managed wrapper because it is useful for batch comparisons.
    private AnalysisSessionArtifact ImportLegacyReferencePolar(SessionSweepDefinition sweep, string manifestDirectory, string artifactName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(sweep.InputPath))
        {
            throw new InvalidOperationException("Legacy reference polar import sweep requires 'inputPath'.");
        }

        var resolvedInputPath = Path.IsPathRooted(sweep.InputPath)
            ? sweep.InputPath
            : Path.Combine(manifestDirectory, sweep.InputPath);
        var referencePolar = legacyReferencePolarImporter.Import(resolvedInputPath);
        polarExporter.Export(outputPath, referencePolar);
        var pointCount = referencePolar.Blocks.Sum(block => block.Points.Count);
        return CreateArtifact(artifactName, "legacy-reference-polar-import", outputPath, pointCount);
    }

    // Legacy mapping: none directly; managed wrapper around the legacy dump importer and archive writer.
    // Difference from legacy: The runner imports one legacy binary dump and materializes an archive artifact bundle rather than participating in the original dump generation.
    // Decision: Keep the managed wrapper because it turns old dump files into batch artifacts.
    private AnalysisSessionArtifact ImportLegacyPolarDump(SessionSweepDefinition sweep, string manifestDirectory, string artifactName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(sweep.InputPath))
        {
            throw new InvalidOperationException("Legacy polar dump import sweep requires 'inputPath'.");
        }

        var resolvedInputPath = Path.IsPathRooted(sweep.InputPath)
            ? sweep.InputPath
            : Path.Combine(manifestDirectory, sweep.InputPath);
        var dump = legacyPolarDumpImporter.Import(resolvedInputPath);
        var export = legacyPolarDumpArchiveWriter.Export(outputPath, dump);
        return CreateArtifact(artifactName, "legacy-polar-dump-import", export.SummaryPath, dump.OperatingPoints.Count);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ/ALFA command workflow lineage via the managed analysis service.
    // Difference from legacy: The runner delegates the actual sweep to the solver service and only owns artifact wiring.
    // Decision: Keep the managed wrapper because session orchestration should stay separate from solver logic.
    private AnalysisSessionArtifact ExportInviscidAlphaSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateInviscidSettings(sweep);
        var result = analysisService.SweepInviscidAlpha(geometry, sweep.Start, sweep.End, sweep.Step, settings);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "inviscid-alpha", outputPath, result.Points.Count);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: CSEQ/CL command workflow lineage via the managed analysis service.
    // Difference from legacy: The runner delegates the lift-target sweep to the solver service and only owns artifact creation.
    // Decision: Keep the managed wrapper because orchestration belongs here, not the solver itself.
    private AnalysisSessionArtifact ExportInviscidLiftSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateInviscidSettings(sweep);
        var result = analysisService.SweepInviscidLiftCoefficient(geometry, sweep.Start, sweep.End, sweep.Step, settings);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "inviscid-cl", outputPath, result.Points.Count);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: viscous alpha-sequence workflow lineage via the managed solver service.
    // Difference from legacy: The runner writes a simple managed CSV projection of the viscous sweep results instead of relying on the legacy interactive polar accumulation path.
    // Decision: Keep the managed wrapper because it is a batch/export concern.
    private AnalysisSessionArtifact ExportViscousAlphaSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        // Surrogate displacement-coupled pipeline replaced by Newton solver.
        // Use SweepViscousAlpha for the Newton path.
        var settings = CreateViscousSettings(sweep);
        var results = analysisService.SweepViscousAlpha(
            geometry,
            sweep.Start,
            sweep.End,
            sweep.Step,
            settings);
        // Write a simple CSV of the viscous polar points
        var lines = new List<string> { "alpha,CL,CD,CM,converged,CDF,CDP" };
        // Legacy block: Managed-only quick CSV projection of the viscous alpha sweep for session artifacts.
        // Difference: The old workflow accumulated polar files interactively instead of writing this reduced session-format CSV.
        // Decision: Keep the managed loop because it serves the automation layer.
        foreach (var r in results)
        {
            lines.Add($"{r.AngleOfAttackDegrees:F4},{r.LiftCoefficient:F6},{r.DragDecomposition.CD:F6},{r.MomentCoefficient:F6},{r.Converged},{r.DragDecomposition.CDF:F6},{r.DragDecomposition.CDP:F6}");
        }
        System.IO.File.WriteAllLines(outputPath, lines);
        return CreateArtifact(artifactName, "viscous-alpha", outputPath, results.Count);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: viscous CL-sequence workflow lineage via the managed solver service.
    // Difference from legacy: The runner writes a simplified managed CSV artifact rather than reusing the interactive polar accumulation path.
    // Decision: Keep the managed wrapper because it is a batch/export concern.
    private AnalysisSessionArtifact ExportViscousLiftSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        // Surrogate displacement-coupled pipeline replaced by Newton solver.
        // Use SweepViscousCL for the Newton path.
        var settings = CreateViscousSettings(sweep);
        var results = analysisService.SweepViscousCL(
            geometry,
            sweep.Start,
            sweep.End,
            sweep.Step,
            settings);
        var lines = new List<string> { "alpha,CL,CD,CM,converged,CDF,CDP" };
        // Legacy block: Managed-only quick CSV projection of the viscous CL sweep for session artifacts.
        // Difference: The old workflow accumulated these results through interactive runtime state instead of one explicit session artifact file.
        // Decision: Keep the managed loop because it supports the automation layer.
        foreach (var r in results)
        {
            lines.Add($"{r.AngleOfAttackDegrees:F4},{r.LiftCoefficient:F6},{r.DragDecomposition.CD:F6},{r.MomentCoefficient:F6},{r.Converged},{r.DragDecomposition.CDF:F6},{r.DragDecomposition.CDP:F6}");
        }
        System.IO.File.WriteAllLines(outputPath, lines);
        return CreateArtifact(artifactName, "viscous-cl", outputPath, results.Count);
    }

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4/NACA5 and f_xfoil/src/aread.f :: AREAD lineage through managed geometry services.
    // Difference from legacy: The runner selects between NACA generation and DAT parsing through explicit manifest fields instead of interactive commands.
    // Decision: Keep the managed wrapper because it is the right session-level geometry source selector.
    private AirfoilGeometry LoadGeometry(SessionAirfoilDefinition airfoil, string manifestDirectory)
    {
        if (!string.IsNullOrWhiteSpace(airfoil.Naca))
        {
            return nacaGenerator.Generate4Digit(airfoil.Naca);
        }

        if (!string.IsNullOrWhiteSpace(airfoil.FilePath))
        {
            var path = airfoil.FilePath;
            var resolvedPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(manifestDirectory, path);
            return parser.ParseFile(resolvedPath);
        }

        throw new InvalidOperationException("Airfoil source must define either 'naca' or 'filePath'.");
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: inviscid operating-point setup lineage through `AnalysisSettings`.
    // Difference from legacy: The session runner creates a managed settings object instead of pushing values into global runtime state.
    // Decision: Keep the managed helper because it is the right boundary between session input and solver configuration.
    private static AnalysisSettings CreateInviscidSettings(SessionSweepDefinition sweep)
    {
        return new AnalysisSettings(
            panelCount: sweep.PanelCount ?? 120,
            machNumber: sweep.MachNumber ?? 0d);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: viscous operating-point setup lineage through `AnalysisSettings`.
    // Difference from legacy: The session runner creates a managed settings object instead of mutating legacy runtime globals.
    // Decision: Keep the managed helper because it makes sweep configuration explicit.
    private static AnalysisSettings CreateViscousSettings(SessionSweepDefinition sweep)
    {
        return new AnalysisSettings(
            panelCount: sweep.PanelCount ?? 120,
            machNumber: sweep.MachNumber ?? 0d,
            reynoldsNumber: sweep.ReynoldsNumber ?? 1_000_000d,
            transitionReynoldsTheta: sweep.TransitionReynoldsTheta ?? 320d,
            criticalAmplificationFactor: sweep.CriticalAmplificationFactor ?? 9d);
    }

    // Legacy mapping: none; managed-only artifact DTO factory.
    // Difference from legacy: The legacy runtime did not return artifact objects.
    // Decision: Keep the managed helper because it centralizes artifact construction.
    private static AnalysisSessionArtifact CreateArtifact(string name, string kind, string outputPath, int pointCount)
    {
        return new AnalysisSessionArtifact
        {
            Name = name,
            Kind = kind,
            OutputPath = outputPath,
            PointCount = pointCount,
        };
    }

    // Legacy mapping: none; managed-only manifest normalization helper.
    // Difference from legacy: Command kinds are normalized from manifest input rather than selected interactively.
    // Decision: Keep the managed helper because it simplifies dispatch.
    private static string NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InvalidOperationException("Sweep kind is required.");
        }

        return kind.Trim().ToLowerInvariant();
    }

    // Legacy mapping: none; managed-only sweep classification helper.
    // Difference from legacy: The batch runner determines geometry requirements from declarative sweep kinds rather than from command context.
    // Decision: Keep the managed helper because it makes manifest validation explicit.
    private static bool SweepRequiresGeometry(SessionSweepDefinition sweep)
    {
        var kind = NormalizeKind(sweep.Kind);
        return kind is not "legacy-polar-import"
            and not "legacy-reference-polar-import"
            and not "legacy-polar-dump-import";
    }

    // Legacy mapping: none; managed-only validation helper.
    // Difference from legacy: The runner checks geometry availability explicitly before dispatch rather than assuming interactive session state.
    // Decision: Keep the managed helper.
    private static AirfoilGeometry RequireGeometry(AirfoilGeometry? geometry)
    {
        return geometry ?? throw new InvalidOperationException("Session sweep requires an airfoil geometry.");
    }

    // Legacy mapping: none; managed-only file-system helper.
    // Difference from legacy: File names are sanitized for automated artifact writing, which is a batch-layer concern.
    // Decision: Keep the managed helper because it protects the session output path logic.
    private static string SanitizeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = raw.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(buffer);
    }

    // Legacy mapping: none; managed-only JSON manifest loader.
    // Difference from legacy: The interactive runtime had no JSON manifest format.
    // Decision: Keep the managed helper because it is central to the session automation layer.
    private static AnalysisSessionManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<AnalysisSessionManifest>(json, CreateReadOptions())
            ?? throw new InvalidOperationException("Could not deserialize session manifest.");
    }

    // Legacy mapping: none; managed-only JSON reader options for session manifests.
    // Difference from legacy: JSON parsing is a .NET automation-layer concern with no Fortran counterpart.
    // Decision: Keep the managed helper.
    private static JsonSerializerOptions CreateReadOptions()
    {
        return new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
    }

    // Legacy mapping: none; managed-only JSON writer options for session summaries.
    // Difference from legacy: The legacy runtime did not emit JSON summary objects.
    // Decision: Keep the managed helper because it centralizes session-summary formatting.
    private static JsonSerializerOptions CreateWriteOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
        };
    }
}

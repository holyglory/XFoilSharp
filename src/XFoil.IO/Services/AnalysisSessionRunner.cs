using System.Text;
using System.Text.Json;
using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.IO.Models;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

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

    private AnalysisSessionArtifact ExportInviscidAlphaSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateInviscidSettings(sweep);
        var result = analysisService.SweepInviscidAlpha(geometry, sweep.Start, sweep.End, sweep.Step, settings);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "inviscid-alpha", outputPath, result.Points.Count);
    }

    private AnalysisSessionArtifact ExportInviscidLiftSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateInviscidSettings(sweep);
        var result = analysisService.SweepInviscidLiftCoefficient(geometry, sweep.Start, sweep.End, sweep.Step, settings);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "inviscid-cl", outputPath, result.Points.Count);
    }

    private AnalysisSessionArtifact ExportViscousAlphaSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateViscousSettings(sweep);
        var result = analysisService.SweepDisplacementCoupledAlpha(
            geometry,
            sweep.Start,
            sweep.End,
            sweep.Step,
            settings,
            sweep.CouplingIterations ?? 2,
            sweep.ViscousIterations ?? 8,
            sweep.ResidualTolerance ?? 0.3d,
            sweep.DisplacementRelaxation ?? 0.5d);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "viscous-alpha", outputPath, result.Points.Count);
    }

    private AnalysisSessionArtifact ExportViscousLiftSweep(SessionSweepDefinition sweep, AirfoilGeometry geometry, string artifactName, string outputPath)
    {
        var settings = CreateViscousSettings(sweep);
        var result = analysisService.SweepDisplacementCoupledLiftCoefficient(
            geometry,
            sweep.Start,
            sweep.End,
            sweep.Step,
            settings,
            sweep.CouplingIterations ?? 2,
            sweep.ViscousIterations ?? 8,
            sweep.ResidualTolerance ?? 0.3d,
            sweep.DisplacementRelaxation ?? 0.5d);
        polarExporter.Export(outputPath, result);
        return CreateArtifact(artifactName, "viscous-cl", outputPath, result.Points.Count);
    }

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

    private static AnalysisSettings CreateInviscidSettings(SessionSweepDefinition sweep)
    {
        return new AnalysisSettings(
            panelCount: sweep.PanelCount ?? 120,
            machNumber: sweep.MachNumber ?? 0d);
    }

    private static AnalysisSettings CreateViscousSettings(SessionSweepDefinition sweep)
    {
        return new AnalysisSettings(
            panelCount: sweep.PanelCount ?? 120,
            machNumber: sweep.MachNumber ?? 0d,
            reynoldsNumber: sweep.ReynoldsNumber ?? 1_000_000d,
            transitionReynoldsTheta: sweep.TransitionReynoldsTheta ?? 320d,
            criticalAmplificationFactor: sweep.CriticalAmplificationFactor ?? 9d);
    }

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

    private static string NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InvalidOperationException("Sweep kind is required.");
        }

        return kind.Trim().ToLowerInvariant();
    }

    private static bool SweepRequiresGeometry(SessionSweepDefinition sweep)
    {
        var kind = NormalizeKind(sweep.Kind);
        return kind is not "legacy-polar-import"
            and not "legacy-reference-polar-import"
            and not "legacy-polar-dump-import";
    }

    private static AirfoilGeometry RequireGeometry(AirfoilGeometry? geometry)
    {
        return geometry ?? throw new InvalidOperationException("Session sweep requires an airfoil geometry.");
    }

    private static string SanitizeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = raw.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(buffer);
    }

    private static AnalysisSessionManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<AnalysisSessionManifest>(json, CreateReadOptions())
            ?? throw new InvalidOperationException("Could not deserialize session manifest.");
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        return new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
        };
    }
}

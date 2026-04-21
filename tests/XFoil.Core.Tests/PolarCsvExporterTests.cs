using XFoil.Core.Models;
using XFoil.IO.Models;
using XFoil.IO.Services;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f polar reporting conventions
// Secondary legacy source: saved-polar file formats and legacy import/export workflows
// Role in port: Verifies the managed CSV exporter that serializes inviscid, viscous, lift-sweep, and legacy-polar results.
// Differences: The legacy program wrote text through interactive/reporting routines, while the managed port provides deterministic CSV formatting and filesystem helpers.
// Decision: Keep the managed exporter because it is a .NET-specific reporting layer over legacy-derived analysis results.
namespace XFoil.Core.Tests;

public sealed class PolarCsvExporterTests
{
    [Fact]
    // Legacy mapping: legacy polar-report output conventions for inviscid alpha sweeps.
    // Difference from legacy: The managed test checks deterministic CSV metadata and row formatting instead of free-form legacy text output.
    // Decision: Keep the managed serialization test because deterministic export is a port-specific contract.
    public void Format_InviscidSweep_IncludesMetadataAndDataRows()
    {
        var exporter = new PolarCsvExporter();
        var sweep = new PolarSweepResult(
            CreateGeometry(),
            new AnalysisSettings(panelCount: 120, machNumber: 0.2d),
            new[]
            {
                new PolarPoint(
                    0d,
                    0.512345d,
                    0.010203d,
                    0.498765d,
                    0.011223d,
                    -0.024680d,
                    0.987654d,
                    0.476543d,
                    0.012345d),
            });

        var content = exporter.Format(sweep);

        Assert.Contains("# Kind: InviscidAlphaSweep\n", content);
        Assert.Contains("# Geometry: TestFoil\n", content);
        Assert.Contains("AngleOfAttackDegrees,LiftCoefficient,DragCoefficient,CorrectedPressureIntegratedLiftCoefficient,CorrectedPressureIntegratedDragCoefficient,MomentCoefficientQuarterChord,Circulation,PressureIntegratedLiftCoefficient,PressureIntegratedDragCoefficient\n", content);
        Assert.Contains("0.000000,0.512345,0.010203,0.498765,0.011223,-0.024680,0.987654,0.476543,0.012345\n", content);
    }

    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: Parent-directory creation and file writing semantics are managed filesystem concerns rather than legacy solver behavior.
    // Decision: Keep the managed-only export test because it protects the convenience contract of the .NET exporter.
    public void Export_CreatesParentDirectoryAndWritesCsvFile()
    {
        var exporter = new PolarCsvExporter();
        var root = Path.Combine(Path.GetTempPath(), $"xfoil-csv-export-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(root, "nested", "polar.csv");

        try
        {
            var sweep = new PolarSweepResult(
                CreateGeometry(),
                new AnalysisSettings(panelCount: 100),
                new[]
                {
                    new PolarPoint(
                        -1d,
                        -0.101d,
                        0.009d,
                        -0.100d,
                        0.010d,
                        0.001d,
                        -0.202d,
                        -0.098d,
                        0.011d),
                });

            exporter.Export(outputPath, sweep);

            Assert.True(File.Exists(outputPath));
            var content = File.ReadAllText(outputPath);
            Assert.Contains("# Geometry: TestFoil\n", content);
            Assert.Contains("-1.000000,-0.101000,0.009000,-0.100000,0.010000,0.001000,-0.202000,-0.098000,0.011000\n", content);
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
    // Legacy mapping: f_xfoil/src/xfoil.f target-lift sweep reporting.
    // Difference from legacy: The managed exporter emits an explicit target-lift CSV column rather than legacy console-style output.
    // Decision: Keep the managed schema because the port exposes lift sweeps as structured data products.
    public void Format_InviscidLiftSweep_IncludesTargetLiftColumn()
    {
        var exporter = new PolarCsvExporter();
        var operatingPoint = new InviscidAnalysisResult(
            panelCount: 2,
            angleOfAttackDegrees: 3d,
            machNumber: 0.1d,
            circulation: 0.801d,
            liftCoefficient: 0.402d,
            dragCoefficient: 0.012d,
            correctedPressureIntegratedLiftCoefficient: 0.400d,
            correctedPressureIntegratedDragCoefficient: 0.014d,
            pressureIntegratedLiftCoefficient: 0.398d,
            pressureIntegratedDragCoefficient: 0.013d,
            momentCoefficientQuarterChord: -0.022d,
            sourceStrengths: Array.Empty<double>(),
            vortexStrength: 0.801d,
            pressureSamples: Array.Empty<PressureCoefficientSample>(),
            wake: new WakeGeometry(Array.Empty<WakePoint>()));
        var sweep = new InviscidLiftSweepResult(
            CreateGeometry(),
            new AnalysisSettings(panelCount: 120, machNumber: 0.1d),
            new[]
            {
                new InviscidTargetLiftResult(0.400d, operatingPoint),
            });

        var content = exporter.Format(sweep);

        Assert.Contains("# Kind: InviscidLiftSweep\n", content);
        Assert.Contains("TargetLiftCoefficient,SolvedAngleOfAttackDegrees,LiftCoefficient,DragCoefficient,CorrectedPressureIntegratedLiftCoefficient,CorrectedPressureIntegratedDragCoefficient,MomentCoefficientQuarterChord,Circulation,PressureIntegratedLiftCoefficient,PressureIntegratedDragCoefficient\n", content);
        Assert.Contains("0.400000,3.000000,0.402000,0.012000,0.400000,0.014000,-0.022000,0.801000,0.398000,0.013000\n", content);
    }

    [Fact]
    // Legacy mapping: legacy saved-polar file columns and metadata.
    // Difference from legacy: The managed exporter normalizes imported legacy polar data into the same deterministic CSV framework as native port results.
    // Decision: Keep the managed compatibility export because it unifies reporting across legacy and native sources.
    public void Format_LegacyPolar_IncludesLegacyMetadataAndColumns()
    {
        var exporter = new PolarCsvExporter();
        var polar = new LegacyPolarFile(
            sourceCode: "XFOIL",
            version: 6.99d,
            airfoilName: "Legacy Test",
            elementCount: 1,
            reynoldsVariationType: LegacyReynoldsVariationType.Fixed,
            machVariationType: LegacyMachVariationType.Fixed,
            referenceMachNumber: 0d,
            referenceReynoldsNumber: 100_000d,
            criticalAmplificationFactor: 9d,
            pressureRatio: null,
            thermalEfficiency: null,
            tripSettings: new[]
            {
                new LegacyPolarTripSetting(1, 1d, 1d),
            },
            columns: new[]
            {
                new LegacyPolarColumn("alpha", "alpha", 0),
                new LegacyPolarColumn("CL", "CL", 1),
                new LegacyPolarColumn("Top_Xtr", "Top Xtr", 2),
            },
            records: new[]
            {
                new LegacyPolarRecord(new Dictionary<string, double>
                {
                    ["alpha"] = 2d,
                    ["CL"] = 0.501d,
                    ["Top_Xtr"] = 0.873d,
                }),
            });

        var content = exporter.Format(polar);

        Assert.Contains("# Kind: LegacySavedPolarImport\n", content);
        Assert.Contains("# SourceCode: XFOIL\n", content);
        Assert.Contains("# ReferenceReynoldsNumber: 100000.000000\n", content);
        Assert.Contains("# TripElement1: Top=1.000000,Bottom=1.000000\n", content);
        Assert.Contains("alpha,CL,Top_Xtr\n", content);
        Assert.Contains("2.000000,0.501000,0.873000\n", content);
    }

    private static AirfoilGeometry CreateGeometry()
    {
        return new AirfoilGeometry(
            "TestFoil",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.5d, 0.08d),
                new AirfoilPoint(0d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
    }

}

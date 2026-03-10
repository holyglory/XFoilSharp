using XFoil.Core.Models;
using XFoil.IO.Models;
using XFoil.IO.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

public sealed class PolarCsvExporterTests
{
    [Fact]
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
    public void Format_ViscousSweep_IncludesViscousColumnsAndLowerCaseBooleans()
    {
        var exporter = new PolarCsvExporter();
        var sweep = new ViscousPolarSweepResult(
            CreateGeometry(),
            new AnalysisSettings(
                panelCount: 140,
                machNumber: 0.15d,
                reynoldsNumber: 750_000d,
                transitionReynoldsTheta: 110d,
                criticalAmplificationFactor: 7d),
            new[]
            {
                new ViscousPolarPoint(
                    2d,
                    0.634210d,
                    0.023456d,
                    -0.031415d,
                    0.210987d,
                    0.056789d,
                    0.004321d,
                    outerConverged: true,
                    innerInteractionConverged: false,
                    finalDisplacementRelaxation: 0.275d,
                    finalSeedEdgeVelocityChange: 0.008765d),
            });

        var content = exporter.Format(sweep);

        Assert.Contains("# Kind: ViscousAlphaSweep\n", content);
        Assert.Contains("# ReynoldsNumber: 750000.000000\n", content);
        Assert.Contains("AngleOfAttackDegrees,LiftCoefficient,EstimatedProfileDragCoefficient,MomentCoefficientQuarterChord,FinalSurfaceResidual,FinalTransitionResidual,FinalWakeResidual,OuterConverged,InnerInteractionConverged,FinalDisplacementRelaxation,FinalSeedEdgeVelocityChange\n", content);
        Assert.Contains("2.000000,0.634210,0.023456,-0.031415,0.210987,0.056789,0.004321,true,false,0.275000,0.008765\n", content);
    }

    [Fact]
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
    public void Format_InviscidLiftSweep_IncludesTargetLiftColumn()
    {
        var exporter = new PolarCsvExporter();
        var operatingPoint = new InviscidAnalysisResult(
            mesh: CreateMesh(),
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

    private static PanelMesh CreateMesh()
    {
        var points = new[]
        {
            new AirfoilPoint(1d, 0d),
            new AirfoilPoint(0.5d, 0.1d),
            new AirfoilPoint(0d, 0d),
        };

        var panels = new[]
        {
            new Panel(0, points[0], points[1], new AirfoilPoint(0.75d, 0.05d), 0.509902d, -0.980581d, 0.196116d, -0.196116d, -0.980581d),
            new Panel(1, points[1], points[2], new AirfoilPoint(0.25d, 0.05d), 0.509902d, -0.980581d, -0.196116d, 0.196116d, -0.980581d),
        };

        return new PanelMesh(points, panels, isCounterClockwise: true);
    }
}

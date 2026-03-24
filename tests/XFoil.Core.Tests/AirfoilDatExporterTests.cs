using System.Globalization;
using XFoil.Core.Models;
using XFoil.IO.Services;

// Legacy audit:
// Primary legacy source: none
// Role in port: Verifies the managed DAT exporter that serializes airfoil geometry for .NET workflows and fixture generation.
// Differences: The legacy code wrote geometry through interactive file commands, while this port exposes deterministic formatting and filesystem helpers as managed services.
// Decision: Keep the managed implementation and tests because there is no single direct Fortran DAT-export analogue to replay.
namespace XFoil.Core.Tests;

public sealed class AirfoilDatExporterTests
{
    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: This test checks deterministic managed string formatting, which is a .NET-specific serialization concern rather than a legacy solver formula.
    // Decision: Keep the managed-only test because reproducible text output is part of the port's public contract.
    public void Format_IncludesGeometryNameHeaderAndUsesDeterministicPointFormatting()
    {
        var exporter = new AirfoilDatExporter();
        var geometry = new AirfoilGeometry(
            "DeterministicFoil",
            new[]
            {
                new AirfoilPoint(1.23456789d, -0.5d),
                new AirfoilPoint(0.3333333d, 0.0000004d),
                new AirfoilPoint(0d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var content = exporter.Format(geometry);

            Assert.Equal(
                "DeterministicFoil\n1.234568 -0.500000\n0.333333 0.000000\n0.000000 0.000000\n",
                content);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: Parent-directory creation is a managed filesystem behavior with no direct Fortran counterpart.
    // Decision: Keep the managed-only test because the exporter intentionally owns this convenience behavior in the port.
    public void Export_CreatesParentDirectoryOnExport()
    {
        var exporter = new AirfoilDatExporter();
        var geometry = new AirfoilGeometry(
            "ExportFoil",
            new[]
            {
                new AirfoilPoint(1d, 0d),
                new AirfoilPoint(0.5d, 0.1d),
                new AirfoilPoint(0d, 0d),
            },
            AirfoilFormat.PlainCoordinates);
        var root = Path.Combine(Path.GetTempPath(), $"xfoil-dat-export-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(root, "nested", "airfoils", "exportfoil.dat");

        try
        {
            exporter.Export(outputPath, geometry);

            Assert.True(File.Exists(outputPath));
            Assert.Equal(exporter.Format(geometry), File.ReadAllText(outputPath));
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

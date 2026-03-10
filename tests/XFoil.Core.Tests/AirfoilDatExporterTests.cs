using System.Globalization;
using XFoil.Core.Models;
using XFoil.IO.Services;

namespace XFoil.Core.Tests;

public sealed class AirfoilDatExporterTests
{
    [Fact]
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

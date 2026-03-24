using System.Globalization;
using System.Text;
using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Role in port: Managed exporter for standard DAT airfoil coordinate files consumed by XFoil-style tooling.
// Differences: No direct Fortran analogue exists because the legacy workflow wrote coordinate files procedurally from the interactive session rather than through a reusable service object.
// Decision: Keep the managed exporter because it is the correct IO boundary for the .NET API.
namespace XFoil.IO.Services;

public sealed class AirfoilDatExporter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    // Legacy mapping: none; managed-only DAT formatter for an airfoil geometry.
    // Difference from legacy: The original runtime wrote DAT-style coordinates procedurally, while the port exposes a reusable formatting method.
    // Decision: Keep the managed formatter because it is the natural service boundary.
    public string Format(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var lines = new List<string>
        {
            geometry.Name,
        };

        // Legacy block: DAT-style coordinate row emission.
        // Difference: The managed exporter formats the immutable point list directly instead of streaming from interactive session arrays.
        // Decision: Keep the equivalent managed loop.
        foreach (var point in geometry.Points)
        {
            lines.Add(
                $"{point.X.ToString("F6", CultureInfo.InvariantCulture)} {point.Y.ToString("F6", CultureInfo.InvariantCulture)}");
        }

        return string.Join('\n', lines) + "\n";
    }

    // Legacy mapping: none; managed-only DAT file writer.
    // Difference from legacy: File creation and directory handling are explicit in the service instead of being part of a command-driven runtime path.
    // Decision: Keep the managed exporter because it makes file output deterministic and reusable.
    public void Export(string path, AirfoilGeometry geometry)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, Format(geometry), Utf8WithoutBom);
    }
}

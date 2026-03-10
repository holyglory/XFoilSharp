using System.Globalization;
using System.Text;
using XFoil.Core.Models;

namespace XFoil.IO.Services;

public sealed class AirfoilDatExporter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

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

        foreach (var point in geometry.Points)
        {
            lines.Add(
                $"{point.X.ToString("F6", CultureInfo.InvariantCulture)} {point.Y.ToString("F6", CultureInfo.InvariantCulture)}");
        }

        return string.Join('\n', lines) + "\n";
    }

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

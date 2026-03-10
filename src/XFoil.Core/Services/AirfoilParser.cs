using System.Globalization;
using XFoil.Core.Models;

namespace XFoil.Core.Services;

public sealed class AirfoilParser
{
    public AirfoilGeometry ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        return ParseLines(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path));
    }

    public AirfoilGeometry ParseLines(IEnumerable<string> lines, string fallbackName = "UNNAMED")
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        var filtered = lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();

        if (filtered.Count == 0)
        {
            throw new InvalidDataException("The airfoil input did not contain any coordinate data.");
        }

        var firstLineTokens = Tokenize(filtered[0]);
        var firstLineIsCoordinate = TryParsePoint(firstLineTokens, out _);

        var name = firstLineIsCoordinate ? fallbackName : filtered[0];
        var startIndex = firstLineIsCoordinate ? 0 : 1;
        var format = firstLineIsCoordinate ? AirfoilFormat.PlainCoordinates : AirfoilFormat.LabeledCoordinates;
        var domainParameters = Array.Empty<double>();

        if (!firstLineIsCoordinate && filtered.Count > 1)
        {
            var secondLineNumbers = ParseNumericTokens(Tokenize(filtered[1]));
            if (secondLineNumbers.Count is 4 or 5)
            {
                format = AirfoilFormat.Ises;
                domainParameters = secondLineNumbers.ToArray();
                startIndex = 2;
            }
        }

        var elements = SplitElements(filtered.Skip(startIndex));
        if (elements.Count == 0 || elements[0].Count == 0)
        {
            throw new InvalidDataException("No coordinate records were found after parsing headers.");
        }

        if (elements.Count > 1)
        {
            format = AirfoilFormat.Mses;
        }

        var points = elements[0]
            .Select(ParsePoint)
            .ToArray();

        return new AirfoilGeometry(name, points, format, domainParameters);
    }

    private static IReadOnlyList<List<string>> SplitElements(IEnumerable<string> lines)
    {
        var elements = new List<List<string>>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            var tokens = Tokenize(line);
            if (IsElementSeparator(tokens))
            {
                if (current.Count > 0)
                {
                    elements.Add(current);
                    current = new List<string>();
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            elements.Add(current);
        }

        return elements;
    }

    private static bool IsElementSeparator(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return false;
        }

        return TryParseDouble(tokens[0], out var x)
            && TryParseDouble(tokens[1], out var y)
            && Math.Abs(x - 999d) < 1e-9
            && Math.Abs(y - 999d) < 1e-9;
    }

    private static AirfoilPoint ParsePoint(string line)
    {
        var tokens = Tokenize(line);
        if (!TryParsePoint(tokens, out var point))
        {
            throw new InvalidDataException($"Invalid coordinate line: '{line}'.");
        }

        return point;
    }

    private static bool TryParsePoint(IReadOnlyList<string> tokens, out AirfoilPoint point)
    {
        point = default;
        if (tokens.Count < 2)
        {
            return false;
        }

        if (!TryParseDouble(tokens[0], out var x) || !TryParseDouble(tokens[1], out var y))
        {
            return false;
        }

        point = new AirfoilPoint(x, y);
        return true;
    }

    private static List<double> ParseNumericTokens(IReadOnlyList<string> tokens)
    {
        var values = new List<double>(tokens.Count);
        foreach (var token in tokens)
        {
            if (!TryParseDouble(token, out var value))
            {
                return new List<double>();
            }

            values.Add(value);
        }

        return values;
    }

    private static bool TryParseDouble(string token, out double value)
    {
        return double.TryParse(
            token,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string[] Tokenize(string line)
    {
        return line
            .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

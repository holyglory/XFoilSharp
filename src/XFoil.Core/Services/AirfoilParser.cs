using System.Globalization;
using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/aread.f :: AREAD
// Secondary legacy source: f_xfoil/src/userio.f :: GETFLT
// Role in port: Parses airfoil coordinate files into managed geometry objects and format metadata.
// Differences: The managed parser is non-interactive, returns structured format/domain information, and always materializes the first element instead of reproducing the legacy prompt-driven element-selection workflow.
// Decision: Keep the managed parser because it is a clearer IO layer for the port; no parity-only branch is needed here.
namespace XFoil.Core.Services;

public sealed class AirfoilParser
{
    // Legacy mapping: f_xfoil/src/aread.f :: AREAD file-open wrapper.
    // Difference from legacy: The managed port delegates the file read to .NET APIs and then reuses the line parser instead of reading through a Fortran logical unit.
    // Decision: Keep the wrapper because it cleanly separates filesystem access from format parsing.
    public AirfoilGeometry ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        return ParseLines(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path));
    }

    // Legacy mapping: f_xfoil/src/aread.f :: AREAD.
    // Difference from legacy: This method mirrors the same plain/labeled/ISES/MSES detection rules and `999 999` element splitting, but it does so without interactive prompts and returns managed metadata.
    // Decision: Keep the managed refactor because it preserves the important file semantics while fitting the .NET API surface.
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

    // Legacy mapping: f_xfoil/src/aread.f :: per-element read loop.
    // Difference from legacy: The managed code groups element lines up front rather than streaming through a logical unit and prompting for the requested element.
    // Decision: Keep the eager grouping because the managed API always returns the first parsed element deterministically.
    private static IReadOnlyList<List<string>> SplitElements(IEnumerable<string> lines)
    {
        var elements = new List<List<string>>();
        var current = new List<string>();

        // Legacy block: AREAD multi-element scan with `999.0 999.0` sentinels.
        // Difference: The managed parser accumulates the groups in memory instead of jumping between loop labels and requested-element state.
        // Decision: Keep the managed grouping because it is simpler and non-interactive.
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

    // Legacy mapping: f_xfoil/src/aread.f :: `999.0 999.0` separator test.
    // Difference from legacy: The managed helper uses tolerant double parsing and comparison rather than direct REAL equality on already-read values.
    // Decision: Keep the managed predicate because it is the right adaptation for text parsing in .NET.
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

    // Legacy mapping: f_xfoil/src/aread.f :: coordinate record decode.
    // Difference from legacy: Invalid records become managed exceptions instead of a global load-failure branch.
    // Decision: Keep the explicit exception because it gives callers actionable parser errors.
    private static AirfoilPoint ParsePoint(string line)
    {
        var tokens = Tokenize(line);
        if (!TryParsePoint(tokens, out var point))
        {
            throw new InvalidDataException($"Invalid coordinate line: '{line}'.");
        }

        return point;
    }

    // Legacy mapping: f_xfoil/src/aread.f :: GETFLT-backed point parse.
    // Difference from legacy: This helper returns a typed success/failure result instead of mutating an output buffer and error flag.
    // Decision: Keep the managed helper because it composes cleanly with the parser pipeline.
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

    // Legacy mapping: f_xfoil/src/userio.f :: GETFLT.
    // Difference from legacy: The managed helper collects doubles into a list instead of filling a caller-provided REAL array and count.
    // Decision: Keep the managed list-based helper because it matches the parser's higher-level structure.
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

    // Legacy mapping: f_xfoil/src/userio.f :: GETFLT numeric conversion.
    // Difference from legacy: The port uses invariant-culture `double.TryParse` instead of list-directed Fortran reads on a temporary record buffer.
    // Decision: Keep the managed conversion helper because it is the correct .NET equivalent for non-interactive parsing.
    private static bool TryParseDouble(string token, out double value)
    {
        return double.TryParse(
            token,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    // Legacy mapping: f_xfoil/src/userio.f :: GETFLT token splitting.
    // Difference from legacy: The managed port delegates tokenization to `string.Split` rather than manually scanning a mutable record buffer for delimiters.
    // Decision: Keep the managed tokenization because it is concise and preserves the same whitespace/comma semantics needed by the parser.
    private static string[] Tokenize(string line)
    {
        return line
            .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

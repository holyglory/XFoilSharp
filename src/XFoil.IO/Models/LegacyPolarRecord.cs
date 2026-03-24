// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one row in a legacy polar table.
// Differences: No direct Fortran analogue exists because the legacy workflow wrote row values through formatted output and read them back positionally rather than through a dictionary-backed object.
// Decision: Keep the managed DTO because it makes column/value access flexible for importers and tests.
namespace XFoil.IO.Models;

public sealed class LegacyPolarRecord
{
    // Legacy mapping: none; managed-only constructor for one parsed legacy polar row.
    // Difference from legacy: The port copies values into a case-insensitive dictionary instead of leaving them in positional arrays.
    // Decision: Keep the managed constructor because it improves ergonomic access to imported rows.
    public LegacyPolarRecord(IReadOnlyDictionary<string, double> values)
    {
        this.values = values is null
            ? throw new ArgumentNullException(nameof(values))
            : new Dictionary<string, double>(values, StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, double> values;

    public IReadOnlyDictionary<string, double> Values => values;

    // Legacy mapping: none; managed-only mutation helper for one parsed polar row.
    // Difference from legacy: The legacy file format had fixed columns, while the port allows controlled key-based updates inside the DTO.
    // Decision: Keep the managed helper because it is useful for normalization in importer code.
    public void SetValue(string key, double value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A key is required.", nameof(key));
        }

        values[key] = value;
    }
}

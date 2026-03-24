// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO describing one named column in a legacy polar table.
// Differences: No direct Fortran analogue exists because the legacy code emitted column labels procedurally during formatted file I/O instead of constructing a named object.
// Decision: Keep the managed DTO because it makes parsed polar tables explicit and reusable.
namespace XFoil.IO.Models;

public sealed class LegacyPolarColumn
{
    // Legacy mapping: none; managed-only validation wrapper for one legacy polar column descriptor.
    // Difference from legacy: The Fortran writer printed column names directly, while the port validates and stores them in a constructor.
    // Decision: Keep the managed constructor because it catches malformed parsed data early.
    public LegacyPolarColumn(string key, string label, int position)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A column key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A column label is required.", nameof(label));
        }

        Key = key;
        Label = label;
        Position = position;
    }

    public string Key { get; }

    public string Label { get; }

    public int Position { get; }
}

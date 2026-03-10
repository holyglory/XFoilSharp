namespace XFoil.IO.Models;

public sealed class LegacyPolarRecord
{
    public LegacyPolarRecord(IReadOnlyDictionary<string, double> values)
    {
        this.values = values is null
            ? throw new ArgumentNullException(nameof(values))
            : new Dictionary<string, double>(values, StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, double> values;

    public IReadOnlyDictionary<string, double> Values => values;

    public void SetValue(string key, double value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A key is required.", nameof(key));
        }

        values[key] = value;
    }
}

namespace XFoil.IO.Models;

public sealed class LegacyPolarColumn
{
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

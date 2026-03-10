namespace XFoil.Design.Models;

public sealed class ModalSpectrum
{
    public ModalSpectrum(string name, IReadOnlyList<ModalCoefficient> coefficients)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A spectrum name is required.", nameof(name));
        }

        Name = name;
        Coefficients = coefficients?.ToArray() ?? throw new ArgumentNullException(nameof(coefficients));
    }

    public string Name { get; }

    public IReadOnlyList<ModalCoefficient> Coefficients { get; }
}

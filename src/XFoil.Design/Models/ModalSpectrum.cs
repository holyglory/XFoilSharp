// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xmdes.f :: MAPGEN/PERT modal spectra
// Role in port: Managed container for a named modal spectrum.
// Differences: Legacy XFoil uses modal arrays and command context rather than a named spectrum object.
// Decision: Keep the managed container because it makes modal outputs explicit and reusable.
namespace XFoil.Design.Models;

public sealed class ModalSpectrum
{
    // Legacy mapping: none; this constructor packages a modal spectrum that legacy XFoil would keep in command-local arrays.
    // Difference from legacy: The managed port validates the spectrum name and freezes the coefficient list.
    // Decision: Keep the managed container because it is clearer for service consumers.
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

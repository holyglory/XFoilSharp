// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xmdes.f :: MAPGEN/PERT modal workflows
// Role in port: Managed representation of one modal inverse-design coefficient.
// Differences: Legacy XFoil stores modal coefficients in arrays rather than a dedicated object per mode.
// Decision: Keep the managed DTO because it makes modal spectra explicit and easy to serialize.
namespace XFoil.Design.Models;

public sealed class ModalCoefficient
{
    // Legacy mapping: none; this constructor packages one modal coefficient that legacy XFoil would keep in indexed arrays.
    // Difference from legacy: The managed port records both raw and filtered values in a named object.
    // Decision: Keep the DTO because it is clearer for downstream consumers.
    public ModalCoefficient(int modeIndex, double coefficient, double filteredCoefficient)
    {
        ModeIndex = modeIndex;
        Coefficient = coefficient;
        FilteredCoefficient = filteredCoefficient;
    }

    public int ModeIndex { get; }

    public double Coefficient { get; }

    public double FilteredCoefficient { get; }
}

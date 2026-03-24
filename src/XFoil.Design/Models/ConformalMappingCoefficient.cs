// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xmdes.f :: MAPGEN
// Role in port: Managed representation of one conformal-mapping mode coefficient.
// Differences: Legacy XFoil stores these values in modal arrays rather than a dedicated object per coefficient.
// Decision: Keep the managed value object because it makes the spectrum output explicit and enumerable.
namespace XFoil.Design.Models;

public sealed class ConformalMappingCoefficient
{
    // Legacy mapping: none; this constructor packages one modal coefficient that legacy XFoil would keep in array slots.
    // Difference from legacy: The managed port exposes each coefficient as a named object rather than an indexed REAL pair.
    // Decision: Keep the DTO because it improves readability without affecting legacy behavior.
    public ConformalMappingCoefficient(int modeIndex, double realPart, double imaginaryPart)
    {
        ModeIndex = modeIndex;
        RealPart = realPart;
        ImaginaryPart = imaginaryPart;
    }

    public int ModeIndex { get; }

    public double RealPart { get; }

    public double ImaginaryPart { get; }
}

using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xmdes.f :: MDES inverse-design workflow
// Role in port: Managed result object for modal inverse-design execution.
// Differences: Legacy XFoil leaves these outputs in geometry arrays and modal work arrays instead of returning a dedicated summary.
// Decision: Keep the managed result object because it is a better service boundary for the port.
namespace XFoil.Design.Models;

public sealed class ModalInverseExecutionResult
{
    // Legacy mapping: none; this constructor packages the output of a modal inverse-design execution.
    // Difference from legacy: The managed port exposes displacement metrics and the resulting spectrum directly instead of requiring callers to inspect shared state.
    // Decision: Keep the structured result because it makes the design workflow testable.
    public ModalInverseExecutionResult(
        AirfoilGeometry geometry,
        ModalSpectrum spectrum,
        double maxNormalDisplacement,
        double rmsNormalDisplacement)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Spectrum = spectrum ?? throw new ArgumentNullException(nameof(spectrum));
        MaxNormalDisplacement = maxNormalDisplacement;
        RmsNormalDisplacement = rmsNormalDisplacement;
    }

    public AirfoilGeometry Geometry { get; }

    public ModalSpectrum Spectrum { get; }

    public double MaxNormalDisplacement { get; }

    public double RmsNormalDisplacement { get; }
}

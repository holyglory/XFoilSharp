using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class ModalInverseExecutionResult
{
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

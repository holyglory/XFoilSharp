using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xqdes.f :: QDES execution workflow
// Role in port: Managed result object for executed QSpec-based inverse design.
// Differences: Legacy XFoil mutates geometry and `QSPEC` state directly instead of packaging the outcome into one immutable object.
// Decision: Keep the managed result object because it is a better fit for the service API and tests.
namespace XFoil.Design.Models;

public sealed class QSpecExecutionResult
{
    // Legacy mapping: none; this constructor packages the outcome of a QDES-derived execution step.
    // Difference from legacy: The managed port surfaces displacement and speed-ratio metrics explicitly instead of requiring manual inspection of the updated state.
    // Decision: Keep the result object because it makes the workflow auditable.
    public QSpecExecutionResult(
        AirfoilGeometry geometry,
        double maxNormalDisplacement,
        double rmsNormalDisplacement,
        double maxSpeedRatioDelta)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        MaxNormalDisplacement = maxNormalDisplacement;
        RmsNormalDisplacement = rmsNormalDisplacement;
        MaxSpeedRatioDelta = maxSpeedRatioDelta;
    }

    public AirfoilGeometry Geometry { get; }

    public double MaxNormalDisplacement { get; }

    public double RmsNormalDisplacement { get; }

    public double MaxSpeedRatioDelta { get; }
}

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xqdes.f :: QDES target-edit workflow
// Role in port: Managed result object for QSpec profile edits.
// Differences: Legacy XFoil edits `QSPEC` arrays in place and does not return a dedicated edit summary object.
// Decision: Keep the managed result object because it gives the QSpec service a clear API contract.
namespace XFoil.Design.Models;

public sealed class QSpecEditResult
{
    // Legacy mapping: none; this constructor packages the output of a managed QSpec edit operation.
    // Difference from legacy: The managed port returns the modified range and smoothing metadata explicitly instead of leaving them implicit in array updates.
    // Decision: Keep the result object because it improves observability.
    public QSpecEditResult(
        QSpecProfile profile,
        int modifiedStartIndex,
        int modifiedEndIndex,
        bool matchedEndpointSlope,
        double smoothingLength)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ModifiedStartIndex = modifiedStartIndex;
        ModifiedEndIndex = modifiedEndIndex;
        MatchedEndpointSlope = matchedEndpointSlope;
        SmoothingLength = smoothingLength;
    }

    public QSpecProfile Profile { get; }

    public int ModifiedStartIndex { get; }

    public int ModifiedEndIndex { get; }

    public bool MatchedEndpointSlope { get; }

    public double SmoothingLength { get; }
}

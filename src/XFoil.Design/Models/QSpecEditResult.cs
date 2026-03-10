namespace XFoil.Design.Models;

public sealed class QSpecEditResult
{
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

using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class ContourModificationResult
{
    public ContourModificationResult(
        AirfoilGeometry geometry,
        int modifiedStartIndex,
        int modifiedEndIndex,
        int controlPointCount,
        bool matchedEndpointSlope)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        ModifiedStartIndex = modifiedStartIndex;
        ModifiedEndIndex = modifiedEndIndex;
        ControlPointCount = controlPointCount;
        MatchedEndpointSlope = matchedEndpointSlope;
    }

    public AirfoilGeometry Geometry { get; }

    public int ModifiedStartIndex { get; }

    public int ModifiedEndIndex { get; }

    public int ControlPointCount { get; }

    public bool MatchedEndpointSlope { get; }
}

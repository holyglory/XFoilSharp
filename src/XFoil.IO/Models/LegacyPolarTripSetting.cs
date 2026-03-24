// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one trip-setting tuple associated with a legacy polar file.
// Differences: No direct Fortran analogue exists because the legacy workflow tracked trip settings in shared arrays and formatted headers rather than a named object.
// Decision: Keep the managed DTO because it preserves the legacy header semantics in a reusable form.
namespace XFoil.IO.Models;

public sealed class LegacyPolarTripSetting
{
    // Legacy mapping: none; managed-only value-object constructor for one legacy trip setting.
    // Difference from legacy: The original code carried these values in header state rather than a typed object.
    // Decision: Keep the managed constructor because it makes the parsed header explicit.
    public LegacyPolarTripSetting(int elementIndex, double topTrip, double bottomTrip)
    {
        ElementIndex = elementIndex;
        TopTrip = topTrip;
        BottomTrip = bottomTrip;
    }

    public int ElementIndex { get; }

    public double TopTrip { get; }

    public double BottomTrip { get; }
}

namespace XFoil.IO.Models;

public sealed class LegacyPolarTripSetting
{
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

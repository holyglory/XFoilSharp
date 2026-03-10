namespace XFoil.Core.Models;

public sealed class AirfoilMetrics
{
    public AirfoilMetrics(
        AirfoilPoint leadingEdge,
        AirfoilPoint trailingEdgeMidpoint,
        double chord,
        double totalArcLength,
        double maxThickness,
        double maxCamber)
    {
        LeadingEdge = leadingEdge;
        TrailingEdgeMidpoint = trailingEdgeMidpoint;
        Chord = chord;
        TotalArcLength = totalArcLength;
        MaxThickness = maxThickness;
        MaxCamber = maxCamber;
    }

    public AirfoilPoint LeadingEdge { get; }

    public AirfoilPoint TrailingEdgeMidpoint { get; }

    public double Chord { get; }

    public double TotalArcLength { get; }

    public double MaxThickness { get; }

    public double MaxCamber { get; }
}

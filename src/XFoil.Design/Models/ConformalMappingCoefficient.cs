namespace XFoil.Design.Models;

public sealed class ConformalMappingCoefficient
{
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

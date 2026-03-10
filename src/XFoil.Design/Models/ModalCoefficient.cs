namespace XFoil.Design.Models;

public sealed class ModalCoefficient
{
    public ModalCoefficient(int modeIndex, double coefficient, double filteredCoefficient)
    {
        ModeIndex = modeIndex;
        Coefficient = coefficient;
        FilteredCoefficient = filteredCoefficient;
    }

    public int ModeIndex { get; }

    public double Coefficient { get; }

    public double FilteredCoefficient { get; }
}

namespace XFoil.Design.Models;

public sealed class QSpecProfile
{
    public QSpecProfile(
        string name,
        double angleOfAttackDegrees,
        double machNumber,
        IReadOnlyList<QSpecPoint> points)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AngleOfAttackDegrees = angleOfAttackDegrees;
        MachNumber = machNumber;
        Points = points ?? throw new ArgumentNullException(nameof(points));
    }

    public string Name { get; }

    public double AngleOfAttackDegrees { get; }

    public double MachNumber { get; }

    public IReadOnlyList<QSpecPoint> Points { get; }
}

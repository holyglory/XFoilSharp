namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpFile
{
    public LegacyPolarDumpFile(
        string airfoilName,
        string sourceCode,
        double version,
        double referenceMachNumber,
        double referenceReynoldsNumber,
        double criticalAmplificationFactor,
        LegacyMachVariationType machVariationType,
        LegacyReynoldsVariationType reynoldsVariationType,
        bool isIsesPolar,
        bool isMachSweep,
        IReadOnlyList<LegacyPolarDumpGeometryPoint> geometry,
        IReadOnlyList<LegacyPolarDumpOperatingPoint> operatingPoints)
    {
        AirfoilName = airfoilName ?? throw new ArgumentNullException(nameof(airfoilName));
        SourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
        Version = version;
        ReferenceMachNumber = referenceMachNumber;
        ReferenceReynoldsNumber = referenceReynoldsNumber;
        CriticalAmplificationFactor = criticalAmplificationFactor;
        MachVariationType = machVariationType;
        ReynoldsVariationType = reynoldsVariationType;
        IsIsesPolar = isIsesPolar;
        IsMachSweep = isMachSweep;
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        OperatingPoints = operatingPoints ?? throw new ArgumentNullException(nameof(operatingPoints));
    }

    public string AirfoilName { get; }

    public string SourceCode { get; }

    public double Version { get; }

    public double ReferenceMachNumber { get; }

    public double ReferenceReynoldsNumber { get; }

    public double CriticalAmplificationFactor { get; }

    public LegacyMachVariationType MachVariationType { get; }

    public LegacyReynoldsVariationType ReynoldsVariationType { get; }

    public bool IsIsesPolar { get; }

    public bool IsMachSweep { get; }

    public IReadOnlyList<LegacyPolarDumpGeometryPoint> Geometry { get; }

    public IReadOnlyList<LegacyPolarDumpOperatingPoint> OperatingPoints { get; }
}

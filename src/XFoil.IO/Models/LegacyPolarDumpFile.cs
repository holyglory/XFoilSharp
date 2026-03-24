// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO representing the contents of a legacy XFoil polar-dump file.
// Differences: No direct Fortran analogue exists because the legacy code wrote dump contents through formatted output and COMMON state rather than a structured record type.
// Decision: Keep the managed DTO because it gives the importer/exporter a stable in-memory representation of the legacy file format.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpFile
{
    // Legacy mapping: none; managed-only constructor for one parsed legacy polar-dump file.
    // Difference from legacy: The Fortran dump writer emitted these fields directly to disk, while the port validates and stores them in an immutable object.
    // Decision: Keep the managed constructor because it makes parsed dump state explicit.
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

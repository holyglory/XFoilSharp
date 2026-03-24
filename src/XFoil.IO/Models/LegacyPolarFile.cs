// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO representing one parsed legacy XFoil polar file.
// Differences: No direct Fortran analogue exists because the legacy polar writer and reader operated through formatted I/O and shared state instead of a structured model object.
// Decision: Keep the managed DTO because it gives the importer/exporter a stable representation of the on-disk polar format.
namespace XFoil.IO.Models;

public sealed class LegacyPolarFile
{
    // Legacy mapping: none; managed-only constructor for one parsed legacy polar file.
    // Difference from legacy: The Fortran path emitted these values procedurally, while the port validates and stores them in an immutable object.
    // Decision: Keep the managed constructor because it makes the parsed file contract explicit.
    public LegacyPolarFile(
        string sourceCode,
        double? version,
        string airfoilName,
        int elementCount,
        LegacyReynoldsVariationType reynoldsVariationType,
        LegacyMachVariationType machVariationType,
        double? referenceMachNumber,
        double? referenceReynoldsNumber,
        double? criticalAmplificationFactor,
        double? pressureRatio,
        double? thermalEfficiency,
        IReadOnlyList<LegacyPolarTripSetting> tripSettings,
        IReadOnlyList<LegacyPolarColumn> columns,
        IReadOnlyList<LegacyPolarRecord> records)
    {
        SourceCode = sourceCode ?? string.Empty;
        Version = version;
        AirfoilName = airfoilName ?? throw new ArgumentNullException(nameof(airfoilName));
        ElementCount = elementCount;
        ReynoldsVariationType = reynoldsVariationType;
        MachVariationType = machVariationType;
        ReferenceMachNumber = referenceMachNumber;
        ReferenceReynoldsNumber = referenceReynoldsNumber;
        CriticalAmplificationFactor = criticalAmplificationFactor;
        PressureRatio = pressureRatio;
        ThermalEfficiency = thermalEfficiency;
        TripSettings = tripSettings ?? throw new ArgumentNullException(nameof(tripSettings));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Records = records ?? throw new ArgumentNullException(nameof(records));
    }

    public string SourceCode { get; }

    public double? Version { get; }

    public string AirfoilName { get; }

    public int ElementCount { get; }

    public LegacyReynoldsVariationType ReynoldsVariationType { get; }

    public LegacyMachVariationType MachVariationType { get; }

    public double? ReferenceMachNumber { get; }

    public double? ReferenceReynoldsNumber { get; }

    public double? CriticalAmplificationFactor { get; }

    public double? PressureRatio { get; }

    public double? ThermalEfficiency { get; }

    public IReadOnlyList<LegacyPolarTripSetting> TripSettings { get; }

    public IReadOnlyList<LegacyPolarColumn> Columns { get; }

    public IReadOnlyList<LegacyPolarRecord> Records { get; }
}

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed result container for the older surrogate viscous-correction workflow.
// Differences: This object belongs to a managed historical workflow rather than to a direct classic XFoil runtime path.
// Decision: Keep the DTO because it documents and transports the older managed workflow cleanly.
namespace XFoil.Solver.Models;

public sealed class ViscousCorrectionResult
{
    public ViscousCorrectionResult(
        ViscousIntervalSystem initialSystem,
        ViscousIntervalSystem correctedSystem,
        int iterations)
    {
        InitialSystem = initialSystem;
        CorrectedSystem = correctedSystem;
        Iterations = iterations;
    }

    public ViscousIntervalSystem InitialSystem { get; }

    public ViscousIntervalSystem CorrectedSystem { get; }

    public int Iterations { get; }
}

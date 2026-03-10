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

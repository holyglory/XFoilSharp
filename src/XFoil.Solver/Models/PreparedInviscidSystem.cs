namespace XFoil.Solver.Models;

public sealed class PreparedInviscidSystem
{
    public PreparedInviscidSystem(
        PanelMesh mesh,
        double[,] sourceTangentialInfluence,
        double[,] vortexTangentialInfluence,
        IReadOnlyList<double> unitFreestreamXSourceStrengths,
        double unitFreestreamXVortexStrength,
        IReadOnlyList<double> unitFreestreamYSourceStrengths,
        double unitFreestreamYVortexStrength)
    {
        Mesh = mesh;
        SourceTangentialInfluence = sourceTangentialInfluence;
        VortexTangentialInfluence = vortexTangentialInfluence;
        UnitFreestreamXSourceStrengths = unitFreestreamXSourceStrengths;
        UnitFreestreamXVortexStrength = unitFreestreamXVortexStrength;
        UnitFreestreamYSourceStrengths = unitFreestreamYSourceStrengths;
        UnitFreestreamYVortexStrength = unitFreestreamYVortexStrength;
    }

    public PanelMesh Mesh { get; }

    public double[,] SourceTangentialInfluence { get; }

    public double[,] VortexTangentialInfluence { get; }

    public IReadOnlyList<double> UnitFreestreamXSourceStrengths { get; }

    public double UnitFreestreamXVortexStrength { get; }

    public IReadOnlyList<double> UnitFreestreamYSourceStrengths { get; }

    public double UnitFreestreamYVortexStrength { get; }
}

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xpanel.f :: QISET influence/basis setup lineage
// Role in port: Managed container for a preassembled inviscid influence system and its unit freestream basis solutions.
// Differences: Legacy XFoil keeps these matrices and basis vectors in solver work arrays, while the managed port packages them for reuse across operating points.
// Decision: Keep the managed container because it is the right reuse boundary for prepared inviscid solves.
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

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/orrs/src :: linear-vorticity formulation lineage, f_xfoil/src/xpanel.f :: inviscid coefficient output lineage
// Role in port: Managed immutable result for the alternative linear-vorticity inviscid solver.
// Differences: This solver/result shape is a managed extension beyond the classic XFoil Hess-Smith path, so the object has no one-to-one legacy runtime counterpart.
// Decision: Keep the managed result object because it is the right contract for the alternative solver implementation.
namespace XFoil.Solver.Models;

/// <summary>
/// Immutable result container for the linear-vorticity inviscid analysis.
/// Contains lift, moment, and pressure drag coefficients along with the pressure distribution.
/// </summary>
public sealed class LinearVortexInviscidResult
{
    /// <summary>
    /// Creates a new inviscid result with the specified aerodynamic coefficients and pressure distribution.
    /// </summary>
    /// <param name="liftCoefficient">Lift coefficient (CL).</param>
    /// <param name="momentCoefficient">Moment coefficient about the quarter-chord (CM).</param>
    /// <param name="pressureDragCoefficient">Pressure drag coefficient (CDP).</param>
    /// <param name="liftCoefficientAlphaDerivative">Derivative of CL with respect to angle of attack (dCL/dalpha).</param>
    /// <param name="liftCoefficientMachSquaredDerivative">Derivative of CL with respect to Mach squared (dCL/dM^2).</param>
    /// <param name="pressureCoefficients">Pressure coefficient (Cp) distribution at each node.</param>
    /// <param name="angleOfAttackRadians">Angle of attack in radians.</param>
    public LinearVortexInviscidResult(
        double liftCoefficient,
        double momentCoefficient,
        double pressureDragCoefficient,
        double liftCoefficientAlphaDerivative,
        double liftCoefficientMachSquaredDerivative,
        IReadOnlyList<double> pressureCoefficients,
        double angleOfAttackRadians)
    {
        LiftCoefficient = liftCoefficient;
        MomentCoefficient = momentCoefficient;
        PressureDragCoefficient = pressureDragCoefficient;
        LiftCoefficientAlphaDerivative = liftCoefficientAlphaDerivative;
        LiftCoefficientMachSquaredDerivative = liftCoefficientMachSquaredDerivative;
        PressureCoefficients = pressureCoefficients ?? throw new ArgumentNullException(nameof(pressureCoefficients));
        AngleOfAttackRadians = angleOfAttackRadians;
    }

    /// <summary>
    /// Lift coefficient (CL).
    /// </summary>
    public double LiftCoefficient { get; }

    /// <summary>
    /// Moment coefficient about the quarter-chord (CM).
    /// </summary>
    public double MomentCoefficient { get; }

    /// <summary>
    /// Pressure drag coefficient (CDP).
    /// </summary>
    public double PressureDragCoefficient { get; }

    /// <summary>
    /// Derivative of lift coefficient with respect to angle of attack (dCL/dalpha).
    /// </summary>
    public double LiftCoefficientAlphaDerivative { get; }

    /// <summary>
    /// Derivative of lift coefficient with respect to Mach number squared (dCL/dM^2).
    /// </summary>
    public double LiftCoefficientMachSquaredDerivative { get; }

    /// <summary>
    /// Pressure coefficient (Cp) distribution at each node.
    /// </summary>
    public IReadOnlyList<double> PressureCoefficients { get; }

    /// <summary>
    /// Angle of attack in radians.
    /// </summary>
    public double AngleOfAttackRadians { get; }
}

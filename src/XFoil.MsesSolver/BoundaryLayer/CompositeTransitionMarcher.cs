using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.BoundaryLayer;

/// <summary>
/// End-to-end BL marcher that runs the laminar transition tracker
/// up to the first-crossing of Ñ = n_crit, then hands off to the
/// Cτ-lag turbulent marcher from that station onward.
///
/// Phase-3b glue in the MSES port. Output arrays are full-length
/// (one entry per station) with the turbulent-region fields filled
/// in from the handoff point to the end, and laminar-region fields
/// taken from the transition marcher up to (and including) the
/// transition station.
///
/// If transition never happens the output is purely laminar; Cτ
/// stays at 0 for all stations.
/// </summary>
public static class CompositeTransitionMarcher
{
    public readonly record struct CompositeResult(
        double[] Theta,
        double[] H,
        double[] N,
        double[] CTau,
        double[] EdgeVelocity,
        double[] Stations,
        int TransitionIndex,
        double TransitionX,
        bool IsTurbulentAtEnd);

    /// <summary>
    /// Runs a full laminar→turbulent march.
    /// </summary>
    /// <param name="stations">Stations, ascending.</param>
    /// <param name="edgeVelocity">Ue at each station.</param>
    /// <param name="kinematicViscosity">ν.</param>
    /// <param name="nCrit">Critical n-factor (default 9).</param>
    /// <param name="cTauInitialFactor">Fraction of local Cτ_eq to
    /// seed the turbulent Cτ with at transition. Drela's MSES
    /// practice: start from 0.3·Cτ_eq to capture the initial
    /// rise in shear. Lower values delay the full turbulent
    /// attachment; higher values approximate a pre-equilibrated
    /// BL (unphysical at transition).</param>
    /// <param name="machNumberEdge">Edge Mach. Default 0.</param>
    /// <param name="useThesisExactTurbulent">If true, run the Phase-2e
    /// <see cref="ThesisExactTurbulentMarcher"/> (implicit Newton on H
    /// via eq. 6.10) for the turbulent leg instead of the Clauser-
    /// placeholder <see cref="ClosureBasedTurbulentLagMarcher"/>.
    /// Default false (keeps the existing uncoupled baseline).</param>
    /// <param name="useThesisExactLaminar">If true, drive the laminar
    /// leg's (θ, H) through <see cref="ThesisExactLaminarMarcher"/>
    /// (implicit-Newton on the laminar closure) instead of the
    /// Phase-2b Thwaites-λ marcher. Ñ tracking uses the same envelope
    /// e^N logic regardless. Default false.</param>
    public static CompositeResult March(
        double[] stations,
        double[] edgeVelocity,
        double kinematicViscosity,
        double nCrit = 9.0,
        double cTauInitialFactor = 0.3,
        double machNumberEdge = 0.0,
        bool useThesisExactTurbulent = false,
        bool useThesisExactLaminar = false)
    {
        var lam = LaminarTransitionMarcher.March(
            stations, edgeVelocity, kinematicViscosity, nCrit, machNumberEdge,
            useThesisExactLaminar: useThesisExactLaminar);

        int n = stations.Length;
        var theta = new double[n];
        var H = new double[n];
        var NAmp = new double[n];
        var cTau = new double[n];
        System.Array.Copy(lam.Theta, theta, n);
        System.Array.Copy(lam.H, H, n);
        System.Array.Copy(lam.N, NAmp, n);

        // Expose the caller's Ue array directly (same reference, same length).
        if (lam.TransitionIndex < 0)
        {
            // Stayed laminar to the end.
            return new CompositeResult(theta, H, NAmp, cTau,
                EdgeVelocity: edgeVelocity,
                Stations: stations,
                TransitionIndex: -1,
                TransitionX: double.NaN,
                IsTurbulentAtEnd: false);
        }

        // Hand off at the transition station. Build a turbulent
        // sub-sweep covering [transitionIdx … n-1]. If transition
        // happened at the very last station, there's no tail to
        // march (tailLen < 2 would break the turbulent marcher's
        // "need ≥2 stations" guard), so degrade to laminar-to-TE.
        int tIdx = lam.TransitionIndex;
        int tailLen = n - tIdx;
        if (tailLen < 2)
        {
            return new CompositeResult(theta, H, NAmp, cTau,
                EdgeVelocity: edgeVelocity,
                Stations: stations,
                TransitionIndex: -1,
                TransitionX: double.NaN,
                IsTurbulentAtEnd: false);
        }
        var tailStations = new double[tailLen];
        var tailUe = new double[tailLen];
        for (int k = 0; k < tailLen; k++)
        {
            tailStations[k] = stations[tIdx + k];
            tailUe[k] = edgeVelocity[tIdx + k];
        }

        double thetaAtTrans = theta[tIdx];
        // Turbulent initial H is ≈ 1.4 (Drela's canonical value for
        // the start of a fully-developed turbulent BL). A direct
        // copy of laminar H at transition (often 2.5+) would blow up
        // the turbulent closure.
        const double HTransInit = 1.4;

        double Ue = edgeVelocity[tIdx];
        double ReThetaTrans = Ue * thetaAtTrans
            / System.Math.Max(kinematicViscosity, 1e-18);
        double HkTrans = MsesClosureRelations.ComputeHk(HTransInit, machNumberEdge);
        double cTauEqTrans = MsesClosureRelations.ComputeCTauEquilibrium(
            HkTrans, ReThetaTrans, machNumberEdge);
        double cTau0 = cTauInitialFactor * cTauEqTrans;

        if (useThesisExactTurbulent)
        {
            var turb = ThesisExactTurbulentMarcher.March(
                tailStations, tailUe, kinematicViscosity,
                thetaAtTrans, HTransInit, cTau0, machNumberEdge);
            for (int k = 0; k < tailLen; k++)
            {
                theta[tIdx + k] = turb.Theta[k];
                H[tIdx + k] = turb.H[k];
                cTau[tIdx + k] = turb.CTau[k];
            }
        }
        else
        {
            var turb = ClosureBasedTurbulentLagMarcher.March(
                tailStations, tailUe, kinematicViscosity,
                thetaAtTrans, HTransInit, cTau0, machNumberEdge);
            for (int k = 0; k < tailLen; k++)
            {
                theta[tIdx + k] = turb.Theta[k];
                H[tIdx + k] = turb.H[k];
                cTau[tIdx + k] = turb.CTau[k];
            }
        }

        return new CompositeResult(theta, H, NAmp, cTau,
            EdgeVelocity: edgeVelocity,
            Stations: stations,
            TransitionIndex: tIdx,
            TransitionX: lam.TransitionX,
            IsTurbulentAtEnd: true);
    }
}

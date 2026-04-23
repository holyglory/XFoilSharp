using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.ThesisClosureSolver.Topology;

/// <summary>
/// R5.1 — Detects the stagnation point on an airfoil surface from
/// the signed tangential-velocity distribution Ue(s) at the panel
/// midpoints. Ue changes sign at the stagnation point because
/// panel tangent flips direction relative to the physical flow
/// (upper surface tangent points LE-ward, lower TE-ward).
///
/// Output: `StagnationLocation` with PanelIndex = first panel
/// whose Ue has the "lower-surface sign" and InterpolationFraction
/// ∈ [0, 1) telling how far into that panel the zero-crossing
/// occurred (0 = at the node between panel-1 and this panel,
/// 1 = at the node between this panel and panel+1).
/// </summary>
public static class StagnationDetector
{
    public readonly record struct StagnationLocation(
        int PanelIndex,
        double InterpolationFraction);

    /// <summary>
    /// Detects the stagnation panel from the signed panel-midpoint
    /// Ue array. Expected sign convention: Ue &lt; 0 on the side
    /// where tangent points opposite the physical flow (upper
    /// surface with conventional TE→upper→LE→lower→TE ordering),
    /// Ue &gt; 0 on the other side. Stagnation is the zero crossing.
    /// </summary>
    /// <param name="ueMid">Signed tangential velocity at each panel midpoint.</param>
    public static StagnationLocation Detect(double[] ueMid)
    {
        if (ueMid is null) throw new System.ArgumentNullException(nameof(ueMid));
        int n = ueMid.Length;
        if (n < 2) throw new System.ArgumentException(
            "need at least 2 panels to detect stagnation", nameof(ueMid));

        // Find first sign change from negative to positive (upper → lower
        // in the conventional ordering) or vice versa.
        for (int i = 1; i < n; i++)
        {
            if (System.Math.Sign(ueMid[i - 1]) != System.Math.Sign(ueMid[i])
                && ueMid[i - 1] != 0.0 && ueMid[i] != 0.0)
            {
                // Linear interpolation: zero lies at
                //   α = Ue[i-1] / (Ue[i-1] - Ue[i]) ∈ [0, 1]
                // relative to panel i-1's midpoint → panel i's midpoint.
                double alpha = ueMid[i - 1] / (ueMid[i - 1] - ueMid[i]);
                return new StagnationLocation(
                    PanelIndex: i,
                    InterpolationFraction: System.Math.Clamp(alpha, 0.0, 1.0));
            }
        }
        // No sign change found — degenerate case. Return mid-panel
        // as best guess.
        return new StagnationLocation(
            PanelIndex: n / 2,
            InterpolationFraction: 0.5);
    }

    /// <summary>
    /// Convenience: runs inviscid solve to get γ, computes midpoint
    /// Ue, and detects stagnation. Intended for diagnostics and
    /// tests, not the Newton inner loop (which builds Ue from its
    /// current state).
    /// </summary>
    public static StagnationLocation DetectFromGeometry(
        ThesisClosurePanelSolver.PanelizedGeometry pg,
        double freestreamSpeed,
        double alphaRadians)
    {
        var aT = ThesisClosurePanelSolver.BuildVortexInfluenceMatrix(pg);
        int n = pg.PanelCount;
        // Solve inviscid to get γ.
        var inv = ThesisClosurePanelSolver.SolveInviscid(
            pg, freestreamSpeed, alphaRadians, chord: 1.0);
        double vx = freestreamSpeed * System.Math.Cos(alphaRadians);
        double vy = freestreamSpeed * System.Math.Sin(alphaRadians);
        var ueMid = new double[n];
        for (int i = 0; i < n; i++)
        {
            double ue = vx * pg.TangentX[i] + vy * pg.TangentY[i];
            for (int k = 0; k < n + 1; k++) ue += aT[i, k] * inv.Gamma[k];
            ueMid[i] = ue;
        }
        return Detect(ueMid);
    }
}

using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.ThesisClosureSolver.Topology;

/// <summary>
/// R5.2 — Per-surface station topology built from the panelized
/// geometry and a stagnation location. Splits the CCW-walking
/// panel list at the stagnation point into upper and lower
/// surfaces, each marching from stagnation toward its respective
/// trailing edge with its own arc-length coordinate starting at 0.
///
/// Panel convention used throughout:
///   panel 0 = TE_upper (starts at upper-TE node, ends at next)
///   panel i, i &lt; stagnation = upper surface (CCW walk goes LE-ward)
///   panel i, i ≥ stagnation = lower surface (CCW walk goes TE-ward)
///   panel N-1 = TE_lower (ends at lower-TE node)
///
/// Current simplification: stagnation InterpolationFraction is
/// snapped to the nearest NODE (rounded to 0 or 1). This loses
/// sub-panel stagnation-point precision but gives a clean panel-
/// to-side partition. Sub-panel precision can be added later once
/// the topology+Newton wiring is working.
/// </summary>
public static class SurfaceTopology
{
    /// <summary>
    /// Per-surface station list: for each station i, which panel
    /// in the original geometry it corresponds to, and its arc-
    /// length distance from the stagnation point.
    /// </summary>
    public readonly record struct SurfaceStations(
        int[] PanelIndices,
        double[] ArcLength);

    public readonly record struct Topology(
        SurfaceStations Upper,
        SurfaceStations Lower,
        int StagnationNodeIndex);

    /// <summary>
    /// Builds the per-surface topology. The stagnation node is the
    /// node between panel (stag.PanelIndex - 1) and panel
    /// stag.PanelIndex, after snapping to the nearer side.
    /// </summary>
    public static Topology Build(
        ThesisClosurePanelSolver.PanelizedGeometry pg,
        StagnationDetector.StagnationLocation stag)
    {
        if (stag.PanelIndex < 0 || stag.PanelIndex >= pg.PanelCount)
            throw new System.ArgumentOutOfRangeException(
                nameof(stag), "stagnation PanelIndex out of range");

        // Snap to nearest node. α < 0.5 → stag at node stag.PanelIndex
        // (left edge of panel stag.PanelIndex); α ≥ 0.5 → stag at node
        // stag.PanelIndex + 1 (right edge).
        int stagNode = stag.InterpolationFraction < 0.5
            ? stag.PanelIndex
            : stag.PanelIndex + 1;
        // Clamp so there's at least one panel on each side.
        stagNode = System.Math.Clamp(stagNode, 1, pg.PanelCount - 1);

        // Upper panels: indices [0, stagNode - 1]. Walking order on
        // upper from stag toward TE_upper: panel stagNode-1, stagNode-2,
        // ..., 0. Arc-length increases from 0 at stag to sum of lengths
        // at TE_upper.
        int nUpper = stagNode;
        var upperIdx = new int[nUpper];
        var upperS = new double[nUpper];
        double sAcc = 0.0;
        for (int k = 0; k < nUpper; k++)
        {
            int panel = stagNode - 1 - k;  // from stag going back to 0
            upperIdx[k] = panel;
            // Arc-length measured from stag: stag-node to panel's mid
            // is half the panel plus any preceding panels.
            if (k == 0) sAcc = 0.5 * pg.Length[panel];
            else sAcc += 0.5 * (pg.Length[upperIdx[k - 1]] + pg.Length[panel]);
            upperS[k] = sAcc;
        }

        // Lower panels: indices [stagNode, N-1]. Walking order on
        // lower from stag toward TE_lower: panel stagNode, stagNode+1,
        // ..., N-1.
        int nLower = pg.PanelCount - stagNode;
        var lowerIdx = new int[nLower];
        var lowerS = new double[nLower];
        sAcc = 0.0;
        for (int k = 0; k < nLower; k++)
        {
            int panel = stagNode + k;
            lowerIdx[k] = panel;
            if (k == 0) sAcc = 0.5 * pg.Length[panel];
            else sAcc += 0.5 * (pg.Length[lowerIdx[k - 1]] + pg.Length[panel]);
            lowerS[k] = sAcc;
        }

        return new Topology(
            Upper: new SurfaceStations(upperIdx, upperS),
            Lower: new SurfaceStations(lowerIdx, lowerS),
            StagnationNodeIndex: stagNode);
    }
}

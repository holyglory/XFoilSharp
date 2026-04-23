namespace XFoil.ThesisClosureSolver.Topology;

/// <summary>
/// R5.3 — Wake panel discretization. Builds an N_wake-panel wake
/// descending from the airfoil's trailing edge along the freestream
/// direction, geometrically stretched so the first wake panel
/// matches the airfoil's TE panel length (smooth-transition-from-
/// airfoil convention).
///
/// Half-chord total wake length is the default (0.5·c); this is
/// long enough for the BL to equilibrate toward freestream H ≈ 1.4
/// for attached cases. Length is configurable.
///
/// Wake goes in the freestream direction: at α=0 that's +x; at
/// positive α wake tilts down by α radians. This is a first-pass
/// approximation — in real MSES the wake follows a streamline
/// curve which can be iteratively updated. For coupling purposes
/// a straight wake is typically adequate.
/// </summary>
public static class WakeDiscretization
{
    public readonly record struct WakePanels(
        double[] NodeX,
        double[] NodeY,
        double[] MidX,
        double[] MidY,
        double[] TangentX,
        double[] TangentY,
        double[] NormalX,
        double[] NormalY,
        double[] Length,
        double[] ArcLengthFromTE);

    /// <summary>
    /// Builds the wake. TE node is NOT duplicated in NodeX/NodeY —
    /// they start at the first wake node PAST the TE (so NodeX[0] is
    /// the downstream end of the first wake panel; the upstream end
    /// of that panel is the TE node at (teX, teY)). This matches the
    /// panel convention used for the airfoil.
    ///
    /// Panel i is between (teX, teY) if i=0, else between
    /// (NodeX[i-1], NodeY[i-1]) and (NodeX[i], NodeY[i]).
    /// </summary>
    /// <param name="teX">TE node x.</param>
    /// <param name="teY">TE node y.</param>
    /// <param name="firstPanelLength">Length to match (from airfoil TE panel).</param>
    /// <param name="panelCount">Number of wake panels (typical 15–30).</param>
    /// <param name="totalLength">Total wake length. Default 0.5·c = 0.5 when chord=1.</param>
    /// <param name="alphaRadians">Angle of attack (sets wake direction).</param>
    public static WakePanels Build(
        double teX, double teY,
        double firstPanelLength,
        int panelCount = 20,
        double totalLength = 0.5,
        double alphaRadians = 0.0)
    {
        if (panelCount < 2) throw new System.ArgumentOutOfRangeException(
            nameof(panelCount), "need at least 2 wake panels");
        if (firstPanelLength <= 0) throw new System.ArgumentOutOfRangeException(
            nameof(firstPanelLength));
        if (totalLength <= 0 || totalLength <= firstPanelLength)
            throw new System.ArgumentOutOfRangeException(
                nameof(totalLength),
                $"total ({totalLength}) must exceed first-panel length ({firstPanelLength})");

        // Geometric stretch: lengths L_i = L_0·r^i. Sum to totalLength:
        //   Σ L_0·r^i = L_0 · (r^N − 1)/(r − 1) = totalLength
        // Solve for r by Newton iteration; initial guess r=1.1.
        double r = FindGeometricRatio(firstPanelLength, totalLength, panelCount);

        double dx = System.Math.Cos(alphaRadians);
        double dy = System.Math.Sin(alphaRadians);

        var nodeX = new double[panelCount];
        var nodeY = new double[panelCount];
        var midX = new double[panelCount];
        var midY = new double[panelCount];
        var tx = new double[panelCount];
        var ty = new double[panelCount];
        var nx = new double[panelCount];
        var ny = new double[panelCount];
        var len = new double[panelCount];
        var sFromTE = new double[panelCount];

        double sAcc = 0.0;
        double curX = teX;
        double curY = teY;
        for (int i = 0; i < panelCount; i++)
        {
            double L_i = firstPanelLength * System.Math.Pow(r, i);
            len[i] = L_i;
            double endX = curX + L_i * dx;
            double endY = curY + L_i * dy;
            nodeX[i] = endX;
            nodeY[i] = endY;
            midX[i] = 0.5 * (curX + endX);
            midY[i] = 0.5 * (curY + endY);
            tx[i] = dx;
            ty[i] = dy;
            nx[i] = -dy;
            ny[i] = dx;
            // Arc-length from TE to panel MIDPOINT.
            sAcc += 0.5 * L_i + (i > 0 ? 0.5 * len[i - 1] : 0.0);
            sFromTE[i] = sAcc;
            curX = endX;
            curY = endY;
        }
        return new WakePanels(nodeX, nodeY, midX, midY, tx, ty, nx, ny, len, sFromTE);
    }

    /// <summary>
    /// Finds r such that L_0·(r^N − 1)/(r − 1) = total. Newton
    /// iteration on f(r) = L_0·(r^N − 1) − total·(r − 1).
    /// </summary>
    private static double FindGeometricRatio(double firstLen, double total, int N)
    {
        // If total = N·firstLen exactly, r = 1 (uniform spacing).
        // Use Newton around that.
        double r = total / (N * firstLen);  // first-order guess
        if (r <= 1.0) r = 1.0 + 1e-3;
        for (int iter = 0; iter < 50; iter++)
        {
            double rN = System.Math.Pow(r, N);
            double f = firstLen * (rN - 1.0) - total * (r - 1.0);
            double df = firstLen * N * System.Math.Pow(r, N - 1) - total;
            if (System.Math.Abs(df) < 1e-15) break;
            double step = f / df;
            r -= step;
            if (r <= 1.0) r = 1.0 + 1e-6;
            if (System.Math.Abs(step) < 1e-12) break;
        }
        return r;
    }
}

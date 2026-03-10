using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

/// <summary>
/// Compressibility parameters computed from the Karman-Tsien correction.
/// </summary>
/// <param name="Beta">Prandtl-Glauert factor: sqrt(1 - M^2).</param>
/// <param name="KarmanTsienFactor">KT correction factor: M^2 / (2 * (1 + Beta)).</param>
public readonly record struct CompressibilityParameters(double Beta, double KarmanTsienFactor);

/// <summary>
/// Static service implementing the geometry pipeline for the linear-vorticity panel method.
/// Provides normal computation (port of NCALC), panel angles (port of APCALC),
/// trailing-edge gap analysis (port of TECALC), compressibility parameters (port of COMSET),
/// and branch-cut-continuous atan2 (port of ATANC).
/// All methods use 0-based indexing.
/// </summary>
public static class PanelGeometryBuilder
{
    /// <summary>
    /// Computes outward unit normal vectors at each node using spline derivatives.
    /// Port of XFoil's NCALC routine (xpanel.f lines 51-84).
    /// Uses <see cref="ParametricSpline.FitSegmented"/> to compute spline derivatives,
    /// then rotates tangent vectors 90 degrees to get outward normals.
    /// Corner handling: at nodes where consecutive arc-length values are identical
    /// (segment break), the normals from the two segments are averaged and renormalized.
    /// </summary>
    /// <param name="panel">Panel state with X, Y, ArcLength populated. NormalX and NormalY are written.</param>
    public static void ComputeNormals(LinearVortexPanelState panel)
    {
        int n = panel.NodeCount;
        if (n <= 1)
        {
            return;
        }

        // Fit segmented splines to get dX/dS and dY/dS
        // The derivatives are temporarily stored in NormalX/NormalY arrays,
        // then overwritten with the actual normals below.
        var xDerivative = new double[n];
        var yDerivative = new double[n];

        ParametricSpline.FitSegmented(panel.X, xDerivative, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.Y, yDerivative, panel.ArcLength, n);

        // Store the spline derivatives back into the panel state
        Array.Copy(xDerivative, panel.XDerivative, n);
        Array.Copy(yDerivative, panel.YDerivative, n);

        // Compute outward normals from tangent rotation: normal = (dY/dS, -dX/dS) / magnitude
        for (int i = 0; i < n; i++)
        {
            double sx = yDerivative[i];
            double sy = -xDerivative[i];
            double magnitude = Math.Sqrt(sx * sx + sy * sy);

            panel.NormalX[i] = sx / magnitude;
            panel.NormalY[i] = sy / magnitude;
        }

        // Average normal vectors at corner points (where arc-length values are identical)
        for (int i = 0; i < n - 1; i++)
        {
            if (panel.ArcLength[i] == panel.ArcLength[i + 1])
            {
                double sx = 0.5 * (panel.NormalX[i] + panel.NormalX[i + 1]);
                double sy = 0.5 * (panel.NormalY[i] + panel.NormalY[i + 1]);
                double magnitude = Math.Sqrt(sx * sx + sy * sy);

                panel.NormalX[i] = sx / magnitude;
                panel.NormalY[i] = sy / magnitude;
                panel.NormalX[i + 1] = sx / magnitude;
                panel.NormalY[i + 1] = sy / magnitude;
            }
        }
    }

    /// <summary>
    /// Computes panel angles at each node using the XFoil convention: atan2(dy, -dx)
    /// where dx and dy are the panel tangent direction components.
    /// Port of XFoil's APCALC routine (xpanel.f lines 22-48).
    /// Note: for the last node (TE panel), uses the closing panel from node N-1 to node 0.
    /// </summary>
    /// <param name="panel">Panel state with X, Y populated. PanelAngle[] is written.</param>
    /// <param name="state">Inviscid solver state (IsSharpTrailingEdge is read).</param>
    public static void ComputePanelAngles(LinearVortexPanelState panel, InviscidSolverState state)
    {
        int n = panel.NodeCount;

        // Interior panels: i = 0 to N-2
        for (int i = 0; i < n - 1; i++)
        {
            double sx = panel.X[i + 1] - panel.X[i];
            double sy = panel.Y[i + 1] - panel.Y[i];

            if (sx == 0.0 && sy == 0.0)
            {
                // Degenerate panel -- use normal direction as fallback
                panel.PanelAngle[i] = Math.Atan2(-panel.NormalY[i], -panel.NormalX[i]);
            }
            else
            {
                panel.PanelAngle[i] = Math.Atan2(sx, -sy);
            }
        }

        // TE panel (last node wraps to first node)
        int last = n - 1;
        if (state.IsSharpTrailingEdge)
        {
            panel.PanelAngle[last] = Math.PI;
        }
        else
        {
            double sx = panel.X[0] - panel.X[last];
            double sy = panel.Y[0] - panel.Y[last];
            panel.PanelAngle[last] = Math.Atan2(-sx, sy) + Math.PI;
        }
    }

    /// <summary>
    /// Analyzes the trailing edge gap geometry: computes gap magnitude, sharp/finite flag,
    /// and the normal and streamwise decomposition of the gap direction.
    /// Port of XFoil's TECALC routine (xfoil.f lines 2270-2308).
    /// </summary>
    /// <param name="panel">Panel state with X, Y, XDerivative, YDerivative, Chord populated.</param>
    /// <param name="state">Inviscid solver state. TE properties are written.</param>
    public static void ComputeTrailingEdgeGeometry(LinearVortexPanelState panel, InviscidSolverState state)
    {
        int n = panel.NodeCount;
        int last = n - 1;

        // TE gap base vector (from last node to first node, matching Fortran X(1)-X(N))
        double dxTE = panel.X[0] - panel.X[last];
        double dyTE = panel.Y[0] - panel.Y[last];

        // Bisector direction: average of negated first-node tangent and last-node tangent
        // In Fortran: DXS = 0.5*(-XP(1) + XP(N)), DYS = 0.5*(-YP(1) + YP(N))
        double dxS = 0.5 * (-panel.XDerivative[0] + panel.XDerivative[last]);
        double dyS = 0.5 * (-panel.YDerivative[0] + panel.YDerivative[last]);

        // Normal and streamwise projected TE gap areas
        // ANTE = DXS*DYTE - DYS*DXTE (cross product -- normal component)
        // ASTE = DXS*DXTE + DYS*DYTE (dot product -- streamwise component)
        state.TrailingEdgeAngleNormal = dxS * dyTE - dyS * dxTE;
        state.TrailingEdgeAngleStreamwise = dxS * dxTE + dyS * dyTE;

        // Total TE gap magnitude
        state.TrailingEdgeGap = Math.Sqrt(dxTE * dxTE + dyTE * dyTE);

        // Sharp TE flag
        state.IsSharpTrailingEdge = state.TrailingEdgeGap < 0.0001 * panel.Chord;
    }

    /// <summary>
    /// Computes Karman-Tsien compressibility correction parameters.
    /// Port of XFoil's COMSET routine (xfoil.f lines 1019-1044).
    /// At M=0: Beta=1, KarmanTsienFactor=0 (degenerates to incompressible).
    /// </summary>
    /// <param name="machNumber">Freestream Mach number (0 &lt;= M &lt; 1).</param>
    /// <returns>Compressibility parameters.</returns>
    public static CompressibilityParameters ComputeCompressibilityParameters(double machNumber)
    {
        double beta = Math.Sqrt(1.0 - machNumber * machNumber);
        double bfac = machNumber * machNumber / (2.0 * (1.0 + beta));
        return new CompressibilityParameters(beta, bfac);
    }

    /// <summary>
    /// Returns atan2(y, x) with branch-cut continuation relative to a reference angle.
    /// Adjusts the result by +/-2*PI to stay within PI of the reference angle,
    /// preventing discontinuous jumps across the atan2 branch cut (-PI/+PI).
    /// Port of XFoil's ATANC function (xutils.f lines 68-112).
    /// </summary>
    /// <param name="y">Y coordinate.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="referenceAngle">Previous/reference angle to maintain continuity with.</param>
    /// <returns>Angle in radians, continuous with respect to referenceAngle.</returns>
    public static double ContinuousAtan2(double y, double x, double referenceAngle)
    {
        const double twoPi = 2.0 * Math.PI;

        double newAngle = Math.Atan2(y, x);
        double deltaTheta = newAngle - referenceAngle;

        // Remove multiples of 2*PI to keep delta within (-PI, PI]
        // Fortran: DTCORR = DTHET - TPI*INT( (DTHET + SIGN(PI,DTHET))/TPI )
        double correction = deltaTheta - twoPi * Math.Truncate((deltaTheta + Math.CopySign(Math.PI, deltaTheta)) / twoPi);

        return referenceAngle + correction;
    }
}

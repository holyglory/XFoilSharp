using System.Numerics;
using XFoil.Solver.Diagnostics;
using XFoil.Core.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: NCALC/APCALC
// Secondary legacy source: f_xfoil/src/xfoil.f :: TECALC/COMSET; f_xfoil/src/xutils.f :: ATANC
// Role in port: Rebuilds the geometry-side preprocessing routines used by the panel solver and viscous setup.
// Differences: The core routines are direct ports, but the managed version splits each routine into reusable methods, keeps explicit parity toggles, and routes legacy REAL-sensitive operations through LegacyPrecisionMath instead of implicit single-precision temporaries.
// Decision: Keep the decomposed managed structure and preserve the parity branches where the legacy geometry preprocessing must replay exactly.
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
    // Legacy mapping: f_xfoil/src/xpanel.f :: NCALC wrapper.
    // Difference from legacy: The managed port exposes an overload pair so callers can choose the parity replay path explicitly.
    // Decision: Keep the overload split because it makes legacy precision intent explicit at the call site.
    public static void ComputeNormals(LinearVortexPanelState panel)
        => ComputeNormals(panel, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xpanel.f :: NCALC precision-selecting wrapper.
    // Difference from legacy: The original routine relies on REAL arithmetic implicitly, while the managed port chooses between double and parity float execution explicitly.
    // Decision: Keep the explicit precision gate because it centralizes parity behavior cleanly.
    public static void ComputeNormals(LinearVortexPanelState panel, bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The doubled tree (auto-generated
        // *.Double.cs twin via gen-double.py) gets the double-precision mirror.
        ComputeNormalsCore<float>(panel);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: NCALC.
    // Difference from legacy: The spline-fit and normal rotation logic are equivalent, but the managed port names the derivative arrays and uses explicit fused helpers where legacy REAL staging changes the resulting ULPs.
    // Decision: Keep the clearer managed decomposition and preserve the fused parity operations at the normalization boundary.
    private static void ComputeNormalsCore<T>(LinearVortexPanelState panel)
        where T : struct, IFloatingPointIeee754<T>
    {
        int n = panel.NodeCount;
        if (n <= 1)
        {
            return;
        }

        T[] x;
        T[] y;
        T[] s;
        T[] xDerivative;
        T[] yDerivative;
        if (typeof(T) == typeof(double))
        {
            x = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbXDouble(n);
            y = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbYDouble(n);
            s = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbSDouble(n);
            xDerivative = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbXDerivDouble(n);
            yDerivative = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbYDerivDouble(n);
        }
        else
        {
            x = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbXFloat(n);
            y = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbYFloat(n);
            s = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbSFloat(n);
            xDerivative = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbXDerivFloat(n);
            yDerivative = (T[])(object)XFoil.Solver.Numerics.SolverBuffers.PgbYDerivFloat(n);
        }
        for (int i = 0; i < n; i++)
        {
            x[i] = T.CreateChecked(panel.X[i]);
            y[i] = T.CreateChecked(panel.Y[i]);
            s[i] = T.CreateChecked(panel.ArcLength[i]);
        }

        ParametricSpline.FitSegmented(x, xDerivative, s, n);
        ParametricSpline.FitSegmented(y, yDerivative, s, n);

        // Store the spline derivatives back into the panel state
        for (int i = 0; i < n; i++)
        {
            panel.XDerivative[i] = double.CreateChecked(xDerivative[i]);
            panel.YDerivative[i] = double.CreateChecked(yDerivative[i]);

            
        }

        // Compute outward normals from tangent rotation: normal = (dY/dS, -dX/dS) / magnitude
        for (int i = 0; i < n; i++)
        {
            T sx = yDerivative[i];
            T sy = -xDerivative[i];
            T magnitudeSquared = LegacyPrecisionMath.FusedMultiplyAdd(sx, sx, sy * sy);
            T magnitude = T.Sqrt(magnitudeSquared);

            panel.NormalX[i] = double.CreateChecked(sx / magnitude);
            panel.NormalY[i] = double.CreateChecked(sy / magnitude);

            
        }

        // Average normal vectors at corner points (where arc-length values are identical)
        for (int i = 0; i < n - 1; i++)
        {
            if (panel.ArcLength[i] == panel.ArcLength[i + 1])
            {
                T sx = T.CreateChecked(0.5) * (T.CreateChecked(panel.NormalX[i]) + T.CreateChecked(panel.NormalX[i + 1]));
                T sy = T.CreateChecked(0.5) * (T.CreateChecked(panel.NormalY[i]) + T.CreateChecked(panel.NormalY[i + 1]));
                T magnitudeSquared = LegacyPrecisionMath.FusedMultiplyAdd(sx, sx, sy * sy);
                T magnitude = T.Sqrt(magnitudeSquared);
                double nx = double.CreateChecked(sx / magnitude);
                double ny = double.CreateChecked(sy / magnitude);

                panel.NormalX[i] = nx;
                panel.NormalY[i] = ny;
                panel.NormalX[i + 1] = nx;
                panel.NormalY[i + 1] = ny;
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
    // Legacy mapping: f_xfoil/src/xpanel.f :: APCALC wrapper.
    // Difference from legacy: The managed port exposes a non-parity convenience overload around the shared APCALC core.
    // Decision: Keep the wrapper because it simplifies the default call sites without changing algorithmic behavior.
    public static void ComputePanelAngles(LinearVortexPanelState panel, InviscidSolverState state)
        => ComputePanelAngles(panel, state, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xpanel.f :: APCALC precision-selecting wrapper.
    // Difference from legacy: The legacy routine always executes in REAL precision, while this overload chooses managed double or parity float execution explicitly.
    // Decision: Keep the explicit precision selection so parity replay remains local to this geometry stage.
    public static void ComputePanelAngles(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: legacy parity uses libm atan2f directly because
        // MathF.Atan2 drifts 1-3 ULP from glibc atan2f at certain input
        // combinations. The drift compounds through the streamfunction
        // influence matrix and breaks bit-exact parity for thicker airfoils
        // (NACA 0009+). The doubled tree (auto-generated *.Double.cs twin via
        // gen-double.py) replaces atan2f with the IEEE double Math.Atan2.
        ComputePanelAnglesLegacyFloat(panel, state);
    }

    private static void ComputePanelAnglesLegacyFloat(LinearVortexPanelState panel, InviscidSolverState state)
    {
        int n = panel.NodeCount;

        for (int i = 0; i < n - 1; i++)
        {
            float sx = (float)panel.X[i + 1] - (float)panel.X[i];
            float sy = (float)panel.Y[i + 1] - (float)panel.Y[i];

            if (sx == 0f && sy == 0f)
            {
                panel.PanelAngle[i] = LegacyLibm.Atan2(-(float)panel.NormalY[i], -(float)panel.NormalX[i]);
            }
            else
            {
                panel.PanelAngle[i] = LegacyLibm.Atan2(sx, -sy);
            }
        }

        int last = n - 1;
        if (state.IsSharpTrailingEdge)
        {
            panel.PanelAngle[last] = MathF.PI;
        }
        else
        {
            float sx = (float)panel.X[0] - (float)panel.X[last];
            float sy = (float)panel.Y[0] - (float)panel.Y[last];
            panel.PanelAngle[last] = LegacyLibm.Atan2(-sx, sy) + MathF.PI;
        }
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: APCALC.
    // Difference from legacy: The tangent-angle assembly is equivalent, but the managed version isolates degenerate-panel fallback handling and sharp-TE branching in named code paths.
    // Decision: Keep the clearer branching while preserving the same APCALC angle convention and parity precision choice.
    private static void ComputePanelAnglesCore<T>(LinearVortexPanelState panel, InviscidSolverState state)
        where T : struct, IFloatingPointIeee754<T>
    {
        int n = panel.NodeCount;

        // Interior panels: i = 0 to N-2
        for (int i = 0; i < n - 1; i++)
        {
            T sx = T.CreateChecked(panel.X[i + 1]) - T.CreateChecked(panel.X[i]);
            T sy = T.CreateChecked(panel.Y[i + 1]) - T.CreateChecked(panel.Y[i]);

            if (sx == T.Zero && sy == T.Zero)
            {
                // Degenerate panel -- use normal direction as fallback
                panel.PanelAngle[i] = double.CreateChecked(
                    T.Atan2(-T.CreateChecked(panel.NormalY[i]), -T.CreateChecked(panel.NormalX[i])));
            }
            else
            {
                panel.PanelAngle[i] = double.CreateChecked(T.Atan2(sx, -sy));
            }
        }

        // TE panel (last node wraps to first node)
        int last = n - 1;
        if (state.IsSharpTrailingEdge)
        {
            panel.PanelAngle[last] = double.CreateChecked(T.Pi);
        }
        else
        {
            T sx = T.CreateChecked(panel.X[0]) - T.CreateChecked(panel.X[last]);
            T sy = T.CreateChecked(panel.Y[0]) - T.CreateChecked(panel.Y[last]);
            panel.PanelAngle[last] = double.CreateChecked(T.Atan2(-sx, sy) + T.Pi);
        }
    }

    /// <summary>
    /// Analyzes the trailing edge gap geometry: computes gap magnitude, sharp/finite flag,
    /// and the normal and streamwise decomposition of the gap direction.
    /// Port of XFoil's TECALC routine (xfoil.f lines 2270-2308).
    /// </summary>
    /// <param name="panel">Panel state with X, Y, XDerivative, YDerivative, Chord populated.</param>
    /// <param name="state">Inviscid solver state. TE properties are written.</param>
    // Legacy mapping: f_xfoil/src/xfoil.f :: TECALC wrapper.
    // Difference from legacy: The managed port exposes a default overload that routes into the shared TE geometry core.
    // Decision: Keep the wrapper because it provides a cleaner API boundary around the direct legacy routine.
    public static void ComputeTrailingEdgeGeometry(LinearVortexPanelState panel, InviscidSolverState state)
        => ComputeTrailingEdgeGeometry(panel, state, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xfoil.f :: TECALC precision-selecting wrapper.
    // Difference from legacy: The original routine depends on REAL evaluation order implicitly, while the managed version makes the legacy-precision choice explicit.
    // Decision: Keep the explicit gate because it localizes parity selection to the TE preprocessing boundary.
    public static void ComputeTrailingEdgeGeometry(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The doubled tree (auto-generated
        // *.Double.cs twin via gen-double.py) gets the double-precision mirror.
        ComputeTrailingEdgeGeometryCore<float>(panel, state);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: TECALC.
    // Difference from legacy: The vector projections and sharp-TE test are materially the same, but the managed code spells out the bisector and gap decomposition with named locals.
    // Decision: Keep the named managed formulation and preserve the same geometric relations used by TECALC.
    private static void ComputeTrailingEdgeGeometryCore<T>(LinearVortexPanelState panel, InviscidSolverState state)
        where T : struct, IFloatingPointIeee754<T>
    {
        int n = panel.NodeCount;
        int last = n - 1;

        // TE gap base vector (from last node to first node, matching Fortran X(1)-X(N))
        T dxTE = T.CreateChecked(panel.X[0]) - T.CreateChecked(panel.X[last]);
        T dyTE = T.CreateChecked(panel.Y[0]) - T.CreateChecked(panel.Y[last]);

        // Bisector direction: average of negated first-node tangent and last-node tangent
        // In Fortran: DXS = 0.5*(-XP(1) + XP(N)), DYS = 0.5*(-YP(1) + YP(N))
        T dxS = T.CreateChecked(0.5) * (-T.CreateChecked(panel.XDerivative[0]) + T.CreateChecked(panel.XDerivative[last]));
        T dyS = T.CreateChecked(0.5) * (-T.CreateChecked(panel.YDerivative[0]) + T.CreateChecked(panel.YDerivative[last]));

        // Normal and streamwise projected TE gap areas
        // ANTE = DXS*DYTE - DYS*DXTE (cross product -- normal component)
        // ASTE = DXS*DXTE + DYS*DYTE (dot product -- streamwise component)
        state.TrailingEdgeAngleNormal = double.CreateChecked((dxS * dyTE) - (dyS * dxTE));
        state.TrailingEdgeAngleStreamwise = double.CreateChecked((dxS * dxTE) + (dyS * dyTE));

        // GDB: dump ANTE inputs
        

        // Total TE gap magnitude
        T gap = T.Sqrt((dxTE * dxTE) + (dyTE * dyTE));
        state.TrailingEdgeGap = double.CreateChecked(gap);

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
    // Legacy mapping: f_xfoil/src/xfoil.f :: COMSET.
    // Difference from legacy: This helper returns the compressibility scalars as a value object instead of writing them into COMMON-backed state.
    // Decision: Keep the managed return shape because it is clearer for callers while preserving the same COMSET formula.
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
    // Legacy mapping: f_xfoil/src/xutils.f :: ATANC.
    // Difference from legacy: The branch-cut continuation is the same, but the managed port packages it as a general-purpose helper that other geometry code can call directly.
    // Decision: Keep the helper as-is because it is a faithful ATANC port in a more reusable shape.
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

    /// <summary>
    /// Legacy precision (float) overload of ContinuousAtan2 — uses libm atan2f
    /// to match Fortran's REAL ATAN2 bit-for-bit.
    /// </summary>
    public static double ContinuousAtan2(double y, double x, double referenceAngle, bool useLegacyPrecision)
    {
        // Phase 1 strip: legacy float path. Uses libm atan2f to match Fortran's
        // REAL ATAN2 bit-for-bit. The doubled tree replaces atan2f with the
        // IEEE double Math.Atan2.
        const float twoPi = 2.0f * MathF.PI;

        float newAngle = LegacyLibm.Atan2((float)y, (float)x);
        float deltaTheta = newAngle - (float)referenceAngle;

        float correction = deltaTheta - twoPi * MathF.Truncate((deltaTheta + MathF.CopySign(MathF.PI, deltaTheta)) / twoPi);

        return (float)referenceAngle + correction;
    }
}

using XFoil.Core.Models;

namespace XFoil.MsesSolver.Inviscid;

/// <summary>
/// Clean-room linear-vortex panel solver for the MSES Phase 5
/// viscous-inviscid global-Newton couple. Does NOT share code with
/// XFoil.Solver — that path is parity-gated and cannot be modified.
///
/// P1 scope: pure inviscid, linear-varying γ on each panel. The
/// panel discretization and geometry metadata from P1.1 feed into
/// the influence-matrix assembly in P1.2.
///
/// References:
///   Katz & Plotkin "Low-Speed Aerodynamics" §11.4 (linear-vortex
///     panel method).
///   Drela 1986 MIT thesis §2–§3 (MSES inviscid formulation —
///     conceptually equivalent for incompressible).
/// </summary>
public static class MsesInviscidPanelSolver
{
    /// <summary>
    /// Panel-discretization metadata produced by
    /// <see cref="DiscretizePanels"/>.
    /// </summary>
    public readonly record struct PanelizedGeometry(
        int PanelCount,
        double[] NodeX,
        double[] NodeY,
        double[] MidX,
        double[] MidY,
        double[] TangentX,
        double[] TangentY,
        double[] NormalX,
        double[] NormalY,
        double[] Length);

    /// <summary>
    /// Builds panel-wise geometry metadata from an airfoil. Input
    /// points are used directly as panel nodes (N panels from N+1
    /// nodes). Panel tangent and normal follow XFoil convention:
    /// tangent points along the contour (from node i to i+1);
    /// normal is 90° counter-clockwise from tangent (i.e. outward
    /// for standard TE → upper-surface → LE → lower-surface → TE
    /// ordering).
    /// </summary>
    public static PanelizedGeometry DiscretizePanels(AirfoilGeometry geometry)
    {
        if (geometry is null) throw new System.ArgumentNullException(nameof(geometry));
        var pts = geometry.Points;
        int nodeCount = pts.Count;
        if (nodeCount < 3) throw new System.ArgumentException(
            "need at least 3 nodes to form an airfoil panelization", nameof(geometry));
        int panelCount = nodeCount - 1;

        var nx = new double[nodeCount];
        var ny = new double[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            nx[i] = pts[i].X;
            ny[i] = pts[i].Y;
        }

        var mx = new double[panelCount];
        var my = new double[panelCount];
        var tx = new double[panelCount];
        var ty = new double[panelCount];
        var nmx = new double[panelCount];
        var nmy = new double[panelCount];
        var len = new double[panelCount];

        for (int i = 0; i < panelCount; i++)
        {
            double dx = nx[i + 1] - nx[i];
            double dy = ny[i + 1] - ny[i];
            double L = System.Math.Sqrt(dx * dx + dy * dy);
            if (L <= 0.0) throw new System.ArgumentException(
                $"degenerate panel {i}: zero length", nameof(geometry));
            len[i] = L;
            tx[i] = dx / L;
            ty[i] = dy / L;
            // Normal = 90° CCW rotation of tangent.
            nmx[i] = -ty[i];
            nmy[i] = tx[i];
            mx[i] = 0.5 * (nx[i] + nx[i + 1]);
            my[i] = 0.5 * (ny[i] + ny[i + 1]);
        }

        return new PanelizedGeometry(
            panelCount, nx, ny, mx, my, tx, ty, nmx, nmy, len);
    }

    /// <summary>
    /// Result of a pure-inviscid solve.
    /// </summary>
    public readonly record struct InviscidResult(
        double[] Gamma,         // N+1 node-γ strengths
        double[] CpMidpoint,    // N panel-midpoint Cp (incompressible)
        double LiftCoefficient, // CL via Kutta-Joukowski
        double Circulation);    // Γ = ∮ γ ds

    /// <summary>
    /// Solves the inviscid linear-vortex system for γ using the
    /// flow-tangency boundary condition at each collocation point
    /// (tangential velocity along the surface = -V∞·tangent) plus
    /// the sharp-TE Kutta condition γ_0 + γ_N = 0.
    ///
    /// P1.3 scope: no sources, no Karman-Tsien (P1.4), no wake
    /// continuation. Pure inviscid closed BIE. Blunt-TE handling
    /// and proper Kutta variants come in P3.
    /// </summary>
    /// <param name="pg">Panelized geometry from <see cref="DiscretizePanels"/>.</param>
    /// <param name="freestreamSpeed">U∞.</param>
    /// <param name="alphaRadians">Angle of attack.</param>
    /// <param name="chord">Reference chord for CL normalization.</param>
    public static InviscidResult SolveInviscid(
        PanelizedGeometry pg,
        double freestreamSpeed,
        double alphaRadians,
        double chord,
        double machNumber = 0.0)
    {
        if (chord <= 0.0) throw new System.ArgumentException("chord must be > 0", nameof(chord));
        int n = pg.PanelCount;

        // BC matrix uses NORMAL-velocity influence (flow-tangency:
        // V_normal_total = 0 at each collocation point).
        var aNormal = BuildVortexNormalInfluenceMatrix(pg);
        // Post-processing uses TANGENT-velocity influence to
        // recover Ue (and hence Cp) at the midpoints.
        var aTangent = BuildVortexInfluenceMatrix(pg);

        var mat = new double[n + 1, n + 1];
        var rhs = new double[n + 1];
        double vx = freestreamSpeed * System.Math.Cos(alphaRadians);
        double vy = freestreamSpeed * System.Math.Sin(alphaRadians);
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < n + 1; k++) mat[i, k] = aNormal[i, k];
            // Flow tangency: V_normal_induced + V∞·normal = 0.
            // Whether our normal is inward or outward doesn't
            // affect the algebra — both sides of the equation
            // use the same convention. We use the (-ty, tx) normal
            // defined in DiscretizePanels.
            rhs[i] = -(vx * pg.NormalX[i] + vy * pg.NormalY[i]);
        }
        // Kutta row (sharp TE). Panels walk TE→upper→LE→lower→TE so
        // panel 0 starts at upper-TE node (γ_0), panel N-1 ends at
        // lower-TE node (γ_N). Tangents are OPPOSITE at TE; for
        // smooth shedding, tangential velocities must match (same
        // magnitude, opposite sign in body-tangent convention, which
        // in vortex-sheet strength notation is γ_0 = -γ_N ⇒
        // γ_0 + γ_N = 0).
        mat[n, 0] = 1.0;
        mat[n, n] = 1.0;
        rhs[n] = 0.0;

        var gamma = SolveLinearSystem(mat, rhs);

        // Surface Ue (signed, along tangent) at each midpoint =
        // V∞·tangent + Σ_k aTangent[i,k]·γ[k]. Cp = 1 - (Ue/U∞)².
        // Apply Karman-Tsien compressibility correction to Cp when
        // M > 0 (XFoil's CPCALC form):
        //     β = √(1-M²), Bfac = M² / (2·(1+β))
        //     Cp_KT = Cp_inc / (β + Bfac · Cp_inc)
        // Valid up to the critical Mach; we stay subcritical here.
        double machClamped = System.Math.Max(0.0, System.Math.Min(machNumber, 0.95));
        double m2 = machClamped * machClamped;
        double beta = System.Math.Sqrt(System.Math.Max(1.0 - m2, 0.0));
        double bfac = beta > 0 ? 0.5 * m2 / (1.0 + beta) : 0.0;

        var cp = new double[n];
        var ueMid = new double[n];
        double U2 = freestreamSpeed * freestreamSpeed;
        for (int i = 0; i < n; i++)
        {
            double ue = vx * pg.TangentX[i] + vy * pg.TangentY[i];
            for (int k = 0; k < n + 1; k++) ue += aTangent[i, k] * gamma[k];
            ueMid[i] = ue;
            double cpInc = U2 > 0 ? 1.0 - (ue * ue) / U2 : 0.0;
            cp[i] = m2 > 0
                ? cpInc / (beta + bfac * cpInc)
                : cpInc;
        }

        // Contour integral ∮_CCW V_outside · dl taken along the
        // airfoil contour (TE→upper→LE→lower→TE is CCW when viewed
        // in the standard +x-right/+y-up orientation). Midpoint-rule
        // integration of the signed surface speed:
        //   ∮_CCW V · dl = Σ_j Ue_j · L_j
        //
        // Kutta-Joukowski: L' = ρ·V∞·Γ where Γ is the CW circulation
        // (positive CW → positive lift). So Γ_KJ = -∮_CCW V·dl, and
        //   CL = 2·Γ_KJ / (V∞·c) = -2·∮_CCW V·dl / (V∞·c).
        double contourIntegral = 0.0;
        for (int j = 0; j < n; j++)
        {
            contourIntegral += ueMid[j] * pg.Length[j];
        }
        double circulation = -contourIntegral;
        double clIncomp = freestreamSpeed > 0
            ? 2.0 * circulation / (freestreamSpeed * chord) : 0.0;
        // Prandtl-Glauert compressibility correction on CL. Cp is
        // already Karman-Tsien corrected above; applying P-G to CL
        // rather than re-integrating the K-T Cp keeps things
        // simple and agrees with K-T integration to within a few
        // percent up to M ≈ 0.3 (our designed operating envelope).
        double cl = m2 > 0 ? clIncomp / beta : clIncomp;

        return new InviscidResult(gamma, cp, cl, circulation);
    }

    /// <summary>
    /// Solves A·x = b by Gaussian elimination with partial pivoting.
    /// Size N; works in-place on <paramref name="a"/> and <paramref name="b"/>.
    /// Returns x.
    /// </summary>
    public static double[] SolveLinearSystem(double[,] aIn, double[] bIn)
    {
        int n = aIn.GetLength(0);
        if (aIn.GetLength(1) != n) throw new System.ArgumentException("matrix must be square");
        if (bIn.Length != n) throw new System.ArgumentException("rhs length mismatch");
        // Work on copies so caller isn't mutated.
        var a = new double[n, n];
        var b = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < n; k++) a[i, k] = aIn[i, k];
            b[i] = bIn[i];
        }
        for (int k = 0; k < n; k++)
        {
            // Partial pivot.
            int piv = k;
            double maxAbs = System.Math.Abs(a[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                double v = System.Math.Abs(a[i, k]);
                if (v > maxAbs) { maxAbs = v; piv = i; }
            }
            if (maxAbs < 1e-300)
                throw new System.InvalidOperationException(
                    $"singular matrix at column {k}");
            if (piv != k)
            {
                for (int j = 0; j < n; j++)
                    (a[k, j], a[piv, j]) = (a[piv, j], a[k, j]);
                (b[k], b[piv]) = (b[piv], b[k]);
            }
            // Eliminate below.
            for (int i = k + 1; i < n; i++)
            {
                double f = a[i, k] / a[k, k];
                if (f == 0.0) continue;
                for (int j = k; j < n; j++) a[i, j] -= f * a[k, j];
                b[i] -= f * b[k];
            }
        }
        // Back-substitute.
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = b[i];
            for (int j = i + 1; j < n; j++) s -= a[i, j] * x[j];
            x[i] = s / a[i, i];
        }
        return x;
    }

    /// <summary>
    /// Builds the linear-vortex tangent-velocity influence matrix
    /// A[i, k] where row i is a collocation point (panel midpoint)
    /// and column k is a node γ strength. A·γ gives the tangential
    /// velocity induced at each collocation point by the full
    /// γ-distribution.
    ///
    /// Size: N × (N+1) where N = panel count (N+1 node γ unknowns).
    /// Each panel j contributes to columns j and j+1 (linear γ
    /// variation from γ_j to γ_{j+1}).
    /// </summary>
    public static double[,] BuildVortexInfluenceMatrix(PanelizedGeometry pg)
        => BuildVortexInfluenceMatrixInternal(pg, normal: false);

    /// <summary>
    /// Normal-velocity version used as the flow-tangency BC
    /// matrix. A_normal[i, k] is the normal velocity at collocation
    /// point i induced by unit γ at node k.
    /// </summary>
    public static double[,] BuildVortexNormalInfluenceMatrix(PanelizedGeometry pg)
        => BuildVortexInfluenceMatrixInternal(pg, normal: true);

    private static double[,] BuildVortexInfluenceMatrixInternal(
        PanelizedGeometry pg, bool normal)
    {
        int n = pg.PanelCount;
        var a = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            double px = pg.MidX[i];
            double py = pg.MidY[i];
            double projX = normal ? pg.NormalX[i] : pg.TangentX[i];
            double projY = normal ? pg.NormalY[i] : pg.TangentY[i];
            for (int j = 0; j < n; j++)
            {
                var (uA, vA, uB, vB) = LinearVortexPanelContribution(
                    px, py, pg.NodeX[j], pg.NodeY[j],
                    pg.NodeX[j + 1], pg.NodeY[j + 1],
                    pg.TangentX[j], pg.TangentY[j], pg.Length[j],
                    selfPanel: i == j);
                a[i, j]     += uA * projX + vA * projY;
                a[i, j + 1] += uB * projX + vB * projY;
            }
        }
        return a;
    }

    /// <summary>
    /// Computes the velocity in GLOBAL coordinates at a collocation
    /// point (px, py) induced by linear γ on a source panel from
    /// (ax, ay) to (bx, by) with tangent (tx, ty) and length L.
    /// Returns the per-unit-γ coefficients for the A endpoint
    /// (shape function 1-ξ/L) and the B endpoint (shape function
    /// ξ/L).
    ///
    /// Formulas: Katz &amp; Plotkin §11.4. Local panel frame has the
    /// panel along the ξ-axis from 0 to L, with η the CCW normal.
    /// </summary>
    public static (double uA, double vA, double uB, double vB)
        LinearVortexPanelContribution(
            double px, double py,
            double ax, double ay, double bx, double by,
            double tx, double ty, double L,
            bool selfPanel)
    {
        // Transform P to panel-local: ξ along tangent, η along normal.
        double dx = px - ax;
        double dy = py - ay;
        double xi = dx * tx + dy * ty;
        // Normal = (-ty, tx)  — 90° CCW rotation.
        double eta = dx * (-ty) + dy * tx;

        double uLocal_A, vLocal_A, uLocal_B, vLocal_B;

        if (selfPanel)
        {
            // Closed-form limit at own midpoint (ξ=L/2, η→0⁺).
            // Above-sheet branch. The below-sheet branch would flip
            // v by sign; we use the above-sheet convention which
            // matches "fluid on the normal-outward side".
            // Constant-γ self: u_const = 1/2, v_const = 0.
            // Linear-shape self: u_lin = 1/4, v_lin = -1/(2π).
            double uConst = 0.5;
            double vConst = 0.0;
            double uLin = 0.25;
            double vLin = -1.0 / (2.0 * System.Math.PI);
            uLocal_A = uConst - uLin;
            vLocal_A = vConst - vLin;
            uLocal_B = uLin;
            vLocal_B = vLin;
        }
        else
        {
            // θ1 = atan2(η, ξ) is the angle at P to panel's start
            // node; θ2 = atan2(η, ξ-L) is the angle to the end node.
            // The closed-form integrals use β := π/2 − θ, so
            // β1 − β2 = θ2 − θ1. Call this dTheta21.
            double r1sq = xi * xi + eta * eta;
            double r2sq = (xi - L) * (xi - L) + eta * eta;
            double r1 = System.Math.Sqrt(r1sq);
            double r2 = System.Math.Sqrt(r2sq);
            double theta1 = System.Math.Atan2(eta, xi);
            double theta2 = System.Math.Atan2(eta, xi - L);
            double dTheta21 = theta2 - theta1;   // β1 − β2
            double lnR = r1 > 0.0 && r2 > 0.0
                ? System.Math.Log(r1 / r2) : 0.0;

            // Induced-velocity closed forms (CCW-positive Γ):
            //   u = −(1/2π) ∫ γ·η/r² dξ,  v = +(1/2π) ∫ γ·(ξ−ξ')/r² dξ
            // Constant γ = 1:
            //   u_const = −(β1−β2)/(2π) = −dTheta21/(2π)
            //   v_const = ln(r1/r2)/(2π)
            // Linear γ = ξ/L:
            //   u_lin = (1/(2πL))·[ξ_P·(β1−β2) − η·ln(r1/r2)]·(−1)
            //         = (1/(2πL))·[−ξ_P·dTheta21 + η·lnR]
            //   Wait: let me redo from ∫ξ dξ/r² = ξ_P·(β1−β2)/η − ln(r1/r2)
            //         u_lin = −(η/(2πL))·[ξ_P·dTheta21/η − lnR]
            //               = −(1/(2πL))·[ξ_P·dTheta21 − η·lnR]
            //               = (−ξ_P·dTheta21 + η·lnR)/(2πL)
            //   v_lin = (1/(2πL))·[ξ_P·ln(r1/r2) − L + η·(β1−β2)]
            //         = (ξ_P·lnR − L + η·(−dTheta21))/(2πL)
            //         = (ξ_P·lnR − L − η·dTheta21)/(2πL)
            //   Hmm — (β1−β2) = -dTheta21? Let me double-check.
            //   β_k := atan(ξ_P-endpoint_k / η_P) = π/2 - θ_k.
            //   β1 − β2 = (π/2 - θ1) - (π/2 - θ2) = θ2 - θ1 = dTheta21. ✓
            double twoPi = 2.0 * System.Math.PI;
            double uConst = -dTheta21 / twoPi;
            double vConst = lnR / twoPi;
            double uLin = (-xi * dTheta21 + eta * lnR) / (twoPi * L);
            double vLin = (xi * lnR - L + eta * dTheta21) / (twoPi * L);

            uLocal_A = uConst - uLin;
            vLocal_A = vConst - vLin;
            uLocal_B = uLin;
            vLocal_B = vLin;
        }

        // Rotate back to global: local ξ maps to (tx, ty); local η
        // maps to (-ty, tx).
        double uA_g = uLocal_A * tx + vLocal_A * (-ty);
        double vA_g = uLocal_A * ty + vLocal_A * tx;
        double uB_g = uLocal_B * tx + vLocal_B * (-ty);
        double vB_g = uLocal_B * ty + vLocal_B * tx;
        return (uA_g, vA_g, uB_g, vB_g);
    }
}

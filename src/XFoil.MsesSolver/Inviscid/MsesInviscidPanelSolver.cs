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
    {
        int n = pg.PanelCount;
        var a = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            double px = pg.MidX[i];
            double py = pg.MidY[i];
            double tix = pg.TangentX[i];
            double tiy = pg.TangentY[i];
            for (int j = 0; j < n; j++)
            {
                // Linear-vortex induced velocity at (px, py) from unit-γ
                // shape functions (1-ξ/L) at node j and (ξ/L) at node j+1
                // on panel j.
                var (uA, vA, uB, vB) = LinearVortexPanelContribution(
                    px, py, pg.NodeX[j], pg.NodeY[j],
                    pg.NodeX[j + 1], pg.NodeY[j + 1],
                    pg.TangentX[j], pg.TangentY[j], pg.Length[j],
                    selfPanel: i == j);
                // Tangential component on collocation i's tangent.
                a[i, j]     += uA * tix + vA * tiy;
                a[i, j + 1] += uB * tix + vB * tiy;
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
            double r1sq = xi * xi + eta * eta;
            double r2sq = (xi - L) * (xi - L) + eta * eta;
            double r1 = System.Math.Sqrt(r1sq);
            double r2 = System.Math.Sqrt(r2sq);
            double beta1 = System.Math.Atan2(eta, xi);
            double beta2 = System.Math.Atan2(eta, xi - L);
            double dBeta = beta2 - beta1;  // subtended angle convention
            double lnR = r1 > 0.0 && r2 > 0.0
                ? System.Math.Log(r1 / r2) : 0.0;

            double twoPi = 2.0 * System.Math.PI;
            double uConst = dBeta / twoPi;
            double vConst = lnR / twoPi;
            double uLin = (xi * dBeta + eta * lnR) / (twoPi * L);
            double vLin = (xi * lnR - L - eta * dBeta) / (twoPi * L);

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

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
}

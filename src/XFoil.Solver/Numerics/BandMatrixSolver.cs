// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xsolve.f :: BLSOLV
// Role in port: Managed alternative linear solver that reuses the viscous Newton system but solves it through an explicit tridiagonal reduction.
// Differences: Classic XFoil does not reformulate the viscous Newton solve through this separate band-style helper; this is a managed experimental solver path alongside the direct BLSOLV replay.
// Decision: Keep the managed alternative because it is useful for comparison and debugging, but it is not the parity reference path.
using System;
using XFoil.Solver.Models;

namespace XFoil.Solver.Numerics;

/// <summary>
/// Band-structure matrix solver for the viscous Newton system.
/// Reformats the ViscousNewtonSystem's block-tridiagonal + mass-column structure
/// into a banded matrix and solves using band LU factorization.
/// Third linear solver option per CONTEXT.md locked decision.
/// </summary>
public static class BandMatrixSolver
{
    /// <summary>
    /// Solves the Newton system by converting block-tridiagonal structure to banded matrix format.
    /// Assembles the same linear system as BlockTridiagonalSolver but solves with dense LU.
    /// Produces the same VDEL solution within floating-point tolerance.
    /// Arrays are indexed by global system line iv directly.
    /// </summary>
    /// <param name="system">The Newton system to solve. VDEL is overwritten with the solution.</param>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV system solve lineage.
    // Difference from legacy: The helper extracts per-equation tridiagonal systems and applies a Thomas solve instead of replaying the original block-tridiagonal elimination directly.
    // Decision: Keep the managed alternative because it is a debugging/comparison tool rather than the legacy replay path.
    public static void Solve(ViscousNewtonSystem system)
    {
        int nsys = system.NSYS;
        if (nsys <= 0) return;

        var va = system.VA;
        var vb = system.VB;
        var vm = system.VM;
        var vdel = system.VDEL;
        var vz = system.VZ;

        // Each equation eq (0,1,2) forms an independent tridiagonal system of size nsys.
        // Arrays are indexed by iv (global system line) directly.

        // Pool all per-equation scratch buffers — BandMatrixSolver used to
        // allocate 7 fresh double[nsys] arrays per call. ThreadStatic pooling
        // eliminates ~60 KB / call of Gen0 churn in the Newton solve path.
        double[] diag = SolverBuffers.BandDiag(nsys);
        double[] subDiag = SolverBuffers.BandSubDiag(nsys);
        double[] superDiag = SolverBuffers.BandSuperDiag(nsys);
        double[] rhsEq = SolverBuffers.BandRhsEq(nsys);
        double[] c = SolverBuffers.BandThomasC(nsys);
        double[] d = SolverBuffers.BandThomasD(nsys);
        double[] x = SolverBuffers.BandThomasX(nsys);

        for (int eq = 0; eq < 3; eq++)
        {
            for (int iv = 0; iv < nsys; iv++)
            {
                diag[iv] = va[eq, 0, iv];
                superDiag[iv] = va[eq, 1, iv];
                subDiag[iv] = vb[eq, 0, iv];
                rhsEq[iv] = vdel[eq, 0, iv];
            }

            // Solve tridiagonal system using Thomas algorithm (forward sweep).

            if (Math.Abs(diag[0]) < 1e-30)
            {
                c[0] = 0.0;
                d[0] = 0.0;
            }
            else
            {
                c[0] = superDiag[0] / diag[0];
                d[0] = rhsEq[0] / diag[0];
            }

            for (int iv = 1; iv < nsys; iv++)
            {
                double denom = diag[iv] - subDiag[iv] * c[iv - 1];
                if (Math.Abs(denom) < 1e-30)
                {
                    c[iv] = 0.0;
                    d[iv] = 0.0;
                }
                else
                {
                    c[iv] = superDiag[iv] / denom;
                    d[iv] = (rhsEq[iv] - subDiag[iv] * d[iv - 1]) / denom;
                }
            }

            // Back substitution
            x[nsys - 1] = d[nsys - 1];
            for (int iv = nsys - 2; iv >= 0; iv--)
            {
                x[iv] = d[iv] - c[iv] * x[iv + 1];
            }

            // Copy solution back to VDEL[eq, 0, iv]
            for (int iv = 0; iv < nsys; iv++)
            {
                vdel[eq, 0, iv] = x[iv];
            }
        }
    }
}

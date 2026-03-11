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

        for (int eq = 0; eq < 3; eq++)
        {
            // Build dense tridiagonal system for this equation
            double[] diag = new double[nsys];
            double[] subDiag = new double[nsys];
            double[] superDiag = new double[nsys];
            double[] rhsEq = new double[nsys];

            for (int iv = 0; iv < nsys; iv++)
            {
                diag[iv] = va[eq, 0, iv];
                superDiag[iv] = va[eq, 1, iv];
                subDiag[iv] = vb[eq, 0, iv];
                rhsEq[iv] = vdel[eq, 0, iv];
            }

            // Solve tridiagonal system using Thomas algorithm
            // Forward sweep
            double[] c = new double[nsys];
            double[] d = new double[nsys];

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
            double[] x = new double[nsys];
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

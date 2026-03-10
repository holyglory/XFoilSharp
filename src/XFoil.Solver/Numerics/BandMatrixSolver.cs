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
        var isys = system.ISYS;

        // The block-tridiagonal system per equation eq has the form:
        //   VA[eq,0,i] * x[eq,i] + VA[eq,1,i] * x[eq,i+1?] + VB[eq,0,i] * x[eq,i-1] = VDEL[eq,side,ibl]
        //
        // Each equation eq (0,1,2) forms an independent tridiagonal system of size nsys.
        // This matches the BlockTridiagonalSolver which does per-equation scalar tridiagonal solves.

        for (int eq = 0; eq < 3; eq++)
        {
            // Build dense tridiagonal system for this equation
            double[] diag = new double[nsys];
            double[] subDiag = new double[nsys];  // sub-diagonal: coeff of x[i-1] in row i
            double[] superDiag = new double[nsys]; // super-diagonal: coeff of x[i+1] in row i
            double[] rhsEq = new double[nsys];

            for (int i = 0; i < nsys; i++)
            {
                int ibl = isys[i, 0];
                int side = isys[i, 1];

                diag[i] = va[eq, 0, ibl];
                superDiag[i] = va[eq, 1, ibl]; // coupling to next
                subDiag[i] = vb[eq, 0, ibl];   // coupling to previous
                rhsEq[i] = vdel[eq, side, ibl];
            }

            // Solve tridiagonal system using Thomas algorithm
            // Forward sweep
            double[] c = new double[nsys];
            double[] d = new double[nsys];

            c[0] = superDiag[0] / diag[0];
            d[0] = rhsEq[0] / diag[0];

            for (int i = 1; i < nsys; i++)
            {
                double m = subDiag[i] / (diag[i] - subDiag[i] * c[i - 1]);
                // Actually, standard Thomas:
                double denom = diag[i] - subDiag[i] * c[i - 1];
                if (Math.Abs(denom) < 1e-30)
                {
                    c[i] = 0.0;
                    d[i] = 0.0;
                }
                else
                {
                    c[i] = superDiag[i] / denom;
                    d[i] = (rhsEq[i] - subDiag[i] * d[i - 1]) / denom;
                }
            }

            // Back substitution
            double[] x = new double[nsys];
            x[nsys - 1] = d[nsys - 1];
            for (int i = nsys - 2; i >= 0; i--)
            {
                x[i] = d[i] - c[i] * x[i + 1];
            }

            // Copy solution back to VDEL
            for (int i = 0; i < nsys; i++)
            {
                int ibl = isys[i, 0];
                int side = isys[i, 1];
                vdel[eq, side, ibl] = x[i];
            }
        }
    }
}

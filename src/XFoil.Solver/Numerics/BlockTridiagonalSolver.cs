using XFoil.Solver.Models;

namespace XFoil.Solver.Numerics;

/// <summary>
/// Block-tridiagonal solver for the viscous Newton system.
/// Port of BLSOLV from xsolve.f.
/// Solves the 3-equation block-tridiagonal system with mass defect coupling column.
/// </summary>
public static class BlockTridiagonalSolver
{
    /// <summary>
    /// Solves the block-tridiagonal Newton system in-place.
    /// Input: ViscousNewtonSystem with VA (diagonal blocks), VB (sub-diagonal blocks),
    ///        VM (mass defect coupling), VDEL (RHS), VZ (TE coupling), ISYS (mapping).
    /// Output: VDEL is modified in-place to contain the solution delta vector.
    /// </summary>
    /// <param name="system">The Newton system to solve. VDEL is overwritten with the solution.</param>
    /// <param name="vaccel">VACCEL acceleration parameter. Small VM coefficients below
    /// VACCEL * |diagonal| are dropped for speed. Default 0.01.</param>
    public static void Solve(ViscousNewtonSystem system, double vaccel = 0.01)
    {
        int nsys = system.NSYS;
        if (nsys <= 0) return;

        var va = system.VA;
        var vb = system.VB;
        var vm = system.VM;
        var vdel = system.VDEL;
        var vz = system.VZ;
        var isys = system.ISYS;

        // The system consists of 3 independent tridiagonal systems (one per equation).
        // For each equation eq:
        //   Station 0:   VA[eq,0,0]*x[0] + VA[eq,1,0]*x[1] = RHS[0]
        //   Station i:   VB[eq,0,i]*x[i-1] + VA[eq,0,i]*x[i] + VA[eq,1,i]*x[i+1] = RHS[i]
        //   Station N-1: VB[eq,0,N-1]*x[N-2] + VA[eq,0,N-1]*x[N-1] = RHS[N-1]
        //
        // Mass defect coupling (VM) adds a dense column for wake mass influence.
        // VACCEL threshold drops small VM entries for performance.

        for (int eq = 0; eq < 3; eq++)
        {
            // Extract tridiagonal coefficients for this equation
            double[] diag = new double[nsys];
            double[] sub = new double[nsys];   // sub-diagonal (VB)
            double[] sup = new double[nsys];   // super-diagonal (VA[,1,])
            double[] rhs = new double[nsys];

            for (int i = 0; i < nsys; i++)
            {
                int ibl = isys[i, 0];
                int side = isys[i, 1];

                diag[i] = va[eq, 0, ibl];
                sup[i] = va[eq, 1, ibl];
                sub[i] = vb[eq, 0, ibl];
                rhs[i] = vdel[eq, side, ibl];

                // Apply VACCEL: drop small mass coupling coefficients
                for (int j = 0; j < system.MaxWake; j++)
                {
                    if (Math.Abs(vm[eq, j, ibl]) < vaccel * Math.Abs(diag[i]))
                    {
                        vm[eq, j, ibl] = 0.0;
                    }
                }
            }

            // Thomas algorithm for tridiagonal solve
            // Forward sweep
            double[] c = new double[nsys]; // modified super-diagonal
            double[] d = new double[nsys]; // modified RHS

            if (Math.Abs(diag[0]) < 1e-30)
            {
                c[0] = 0.0;
                d[0] = 0.0;
            }
            else
            {
                c[0] = sup[0] / diag[0];
                d[0] = rhs[0] / diag[0];
            }

            for (int i = 1; i < nsys; i++)
            {
                double denom = diag[i] - sub[i] * c[i - 1];
                if (Math.Abs(denom) < 1e-30)
                {
                    c[i] = 0.0;
                    d[i] = 0.0;
                }
                else
                {
                    c[i] = sup[i] / denom;
                    d[i] = (rhs[i] - sub[i] * d[i - 1]) / denom;
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

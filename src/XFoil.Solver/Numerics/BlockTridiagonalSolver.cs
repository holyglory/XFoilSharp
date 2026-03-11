using System;
using System.Globalization;
using System.IO;
using XFoil.Solver.Models;

namespace XFoil.Solver.Numerics;

/// <summary>
/// Block-tridiagonal solver for the viscous Newton system.
/// Port of BLSOLV from xsolve.f.
/// Solves the 3-equation block-tridiagonal system with mass defect coupling column.
/// Iterates by global system line iv (0..NSYS-1) directly.
/// </summary>
public static class BlockTridiagonalSolver
{
    /// <summary>
    /// Solves the block-tridiagonal Newton system in-place.
    /// Input: ViscousNewtonSystem with VA (diagonal blocks), VB (sub-diagonal blocks),
    ///        VM (mass defect coupling), VDEL (RHS), VZ (TE coupling), ISYS (mapping).
    /// Output: VDEL is modified in-place to contain the solution delta vector.
    /// After the re-indexing, arrays are indexed directly by iv (global system line).
    /// </summary>
    /// <param name="system">The Newton system to solve. VDEL is overwritten with the solution.</param>
    /// <param name="vaccel">VACCEL acceleration parameter. Small VM coefficients below
    /// VACCEL * |diagonal| are dropped for speed. Default 0.01.</param>
    public static void Solve(ViscousNewtonSystem system, double vaccel = 0.01, TextWriter? debugWriter = null)
    {
        int nsys = system.NSYS;
        if (nsys <= 0) return;

        var va = system.VA;
        var vb = system.VB;
        var vm = system.VM;
        var vdel = system.VDEL;
        var vz = system.VZ;

        // The system consists of 3 independent tridiagonal systems (one per equation).
        // For each equation eq:
        //   iv=0:     VA[eq,0,0]*x[0] + VA[eq,1,0]*x[1] = VDEL[eq,0,0]
        //   iv=i:     VB[eq,0,i]*x[i-1] + VA[eq,0,i]*x[i] + VA[eq,1,i]*x[i+1] = VDEL[eq,0,i]
        //   iv=N-1:   VB[eq,0,N-1]*x[N-2] + VA[eq,0,N-1]*x[N-1] = VDEL[eq,0,N-1]
        //
        // All arrays are now indexed by iv directly (global system line).
        // RHS is in VDEL[eq, 0, iv] (slot 0).

        for (int eq = 0; eq < 3; eq++)
        {
            // Extract tridiagonal coefficients directly by iv
            double[] diag = new double[nsys];
            double[] sub = new double[nsys];   // sub-diagonal (VB)
            double[] sup = new double[nsys];   // super-diagonal (VA[,1,])
            double[] rhs = new double[nsys];

            for (int iv = 0; iv < nsys; iv++)
            {
                diag[iv] = va[eq, 0, iv];
                sup[iv] = va[eq, 1, iv];
                sub[iv] = vb[eq, 0, iv];
                rhs[iv] = vdel[eq, 0, iv];

                // Apply VACCEL: drop small mass coupling coefficients
                int maxWakeIdx = Math.Min(system.MaxWake, vm.GetLength(1));
                for (int j = 0; j < maxWakeIdx; j++)
                {
                    if (Math.Abs(vm[eq, j, iv]) < vaccel * Math.Abs(diag[iv]))
                    {
                        vm[eq, j, iv] = 0.0;
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

            for (int iv = 1; iv < nsys; iv++)
            {
                double denom = diag[iv] - sub[iv] * c[iv - 1];
                if (Math.Abs(denom) < 1e-30)
                {
                    c[iv] = 0.0;
                    d[iv] = 0.0;
                }
                else
                {
                    c[iv] = sup[iv] / denom;
                    d[iv] = (rhs[iv] - sub[iv] * d[iv - 1]) / denom;
                }
            }

            // Diagnostic: log VDEL after forward sweep for first 5 entries
            if (debugWriter != null)
            {
                debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "BLSOLV_POST_FORWARD EQ={0}", eq));
                int logCount = Math.Min(5, nsys);
                for (int j = 0; j < logCount; j++)
                {
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VDEL_FWD IV={0,4}{1,15:E8}", j + 1, d[j]));
                }
            }

            // Back substitution
            double[] x = new double[nsys];
            x[nsys - 1] = d[nsys - 1];
            for (int iv = nsys - 2; iv >= 0; iv--)
            {
                x[iv] = d[iv] - c[iv] * x[iv + 1];
            }

            // Diagnostic: log VDEL solution for first 5 entries
            if (debugWriter != null)
            {
                int logCount = Math.Min(5, nsys);
                for (int j = 0; j < logCount; j++)
                {
                    debugWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "VDEL_SOL IV={0,4}{1,15:E8}", j + 1, x[j]));
                }
            }

            // Write solution back to VDEL[eq, 0, iv]
            for (int iv = 0; iv < nsys; iv++)
            {
                vdel[eq, 0, iv] = x[iv];
            }
        }
    }
}

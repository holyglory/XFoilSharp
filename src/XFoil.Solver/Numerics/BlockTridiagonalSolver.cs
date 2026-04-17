using System;
using System.Numerics;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: BLSOLV
// Secondary legacy source(s): none
// Role in port: Coupled block-tridiagonal Newton solve for the viscous system.
// Differences: The C# version splits the legacy monolith into a generic core and a parity-only float workspace; the Fortran routine is a single REAL kernel.
// Decision: Keep the managed structure but preserve the float workspace path when parity mode needs the legacy REAL solve.

namespace XFoil.Solver.Numerics;

/// <summary>
/// Custom block solver for the coupled viscous/inviscid Newton system.
/// Port of BLSOLV from xsolve.f.
/// </summary>
public static class BlockTridiagonalSolver
{
    private const double PivotFloor = 1e-30;

    /// <summary>
    /// Solves the coupled Newton system in place. The factored solution overwrites
    /// <see cref="ViscousNewtonSystem.VDEL"/>.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV
    // Difference from legacy: The top-level entry point is managed-only structure around the same elimination algorithm, with an explicit parity switch that chooses a float workspace instead of mutating one shared REAL array set.
    // Decision: Keep the managed split so default execution stays readable while parity mode can still replay the legacy kernel.
    public static void Solve(
        ViscousNewtonSystem system,
        double vaccel = 0.01,
        bool useLegacyPrecision = false)
    {
        int nsys = system.NSYS;
        if (nsys <= 0)
        {
            return;
        }

        // Legacy block: xsolve.f BLSOLV REAL workspace path.
        // Difference from legacy: The Fortran routine always runs in REAL; the managed solver keeps double as the default and enters this branch only for parity replay.
        // Decision: Keep the explicit parity branch because it isolates the legacy arithmetic without degrading the default solver.
        if (useLegacyPrecision)
        {
            // Classic XFoil's BLSOLV runs in REAL. Keep the parity path on a
            // dedicated float workspace so the control flow stays identical to
            // the default solver while the arithmetic matches the legacy kernel.
            // NOTE: VA/VB/VDEL are [3, 2, nsysMax] but VM is [3, nsysMax, nsysMax].
            // Each buffer must be sized from its own source's dims.
            float[,,] va = CopyToSingleInto(
                SolverBuffers.BtVaFloat(system.VA.GetLength(0), system.VA.GetLength(1), system.VA.GetLength(2)),
                system.VA, nsys);
            float[,,] vb = CopyToSingleInto(
                SolverBuffers.BtVbFloat(system.VB.GetLength(0), system.VB.GetLength(1), system.VB.GetLength(2)),
                system.VB, nsys);
            float[,,] vm = CopyToSingleInto(
                SolverBuffers.BtVmFloat(system.VM.GetLength(0), system.VM.GetLength(1), system.VM.GetLength(2)),
                system.VM, nsys);
            float[,,] vdel = CopyToSingleInto(
                SolverBuffers.BtVdelFloat(system.VDEL.GetLength(0), system.VDEL.GetLength(1), system.VDEL.GetLength(2)),
                system.VDEL, nsys);
            float[,] vz = CopyToSingleInto(
                SolverBuffers.BtVzFloat(system.VZ.GetLength(0), system.VZ.GetLength(1)),
                system.VZ);

            SolveCore(
                va,
                vb,
                vm,
                vdel,
                vz,
                nsys,
                system.UpperTeLine,
                system.FirstWakeLine,
                system.ArcLengthSpan,
                (float)vaccel);

            CopySolutionToDouble(vdel, system.VDEL, nsys);
        }
        else
        {
            SolveCore(
                system.VA,
                system.VB,
                system.VM,
                system.VDEL,
                system.VZ,
                nsys,
                system.UpperTeLine,
                system.FirstWakeLine,
                system.ArcLengthSpan,
                vaccel);
        }
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV
    // Difference from legacy: This is the same elimination and back-substitution algorithm expressed as a generic core so the managed solver can share control flow across double and parity-float execution.
    // Decision: Keep the generic core and audit parity through the arithmetic type and helper selection rather than duplicating the whole solver.
    private static void SolveCore<T>(
        T[,,] va,
        T[,,] vb,
        T[,,] vm,
        T[,,] vdel,
        T[,] vz,
        int nsys,
        int ivte1,
        int ivz,
        double arcLengthSpan,
        T vaccel)
        where T : struct, IFloatingPointIeee754<T>
    {
        T span = T.Max(T.CreateChecked(arcLengthSpan), T.CreateChecked(1e-12));
        T vacc1 = vaccel;
        T vacc2 = (vaccel * T.CreateChecked(2.0)) / span;
        T vacc3 = (vaccel * T.CreateChecked(2.0)) / span;

        // Legacy block: xsolve.f BLSOLV forward elimination sweep.
        for (int iv = 0; iv < nsys; iv++)
        {
            int ivp = iv + 1;

            T pivot = SafeReciprocal(va[0, 0, iv]);
            va[0, 1, iv] *= pivot;
            for (int l = iv; l < nsys; l++)
            {
                vm[0, l, iv] *= pivot;
            }

            vdel[0, 0, iv] *= pivot;
            vdel[0, 1, iv] *= pivot;

            for (int k = 1; k < 3; k++)
            {
                T vtmp = va[k, 0, iv];
                va[k, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, va[0, 1, iv], va[k, 1, iv]);
                for (int l = iv; l < nsys; l++)
                {
                    vm[k, l, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vm[0, l, iv], vm[k, l, iv]);
                }

                vdel[k, 0, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[0, 0, iv], vdel[k, 0, iv]);
                vdel[k, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[0, 1, iv], vdel[k, 1, iv]);
            }

            pivot = SafeReciprocal(va[1, 1, iv]);
            for (int l = iv; l < nsys; l++)
            {
                vm[1, l, iv] *= pivot;
            }

            vdel[1, 0, iv] *= pivot;
            vdel[1, 1, iv] *= pivot;

            {
                T vtmp = va[2, 1, iv];
                for (int l = iv; l < nsys; l++)
                {
                    vm[2, l, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vm[1, l, iv], vm[2, l, iv]);
                }

                vdel[2, 0, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[1, 0, iv], vdel[2, 0, iv]);
                vdel[2, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[1, 1, iv], vdel[2, 1, iv]);
            }

            pivot = SafeReciprocal(vm[2, iv, iv]);
            for (int l = ivp; l < nsys; l++)
            {
                vm[2, l, iv] *= pivot;
            }

            vdel[2, 0, iv] *= pivot;
            vdel[2, 1, iv] *= pivot;

            {
                T vtmp1 = vm[0, iv, iv];
                T vtmp2 = vm[1, iv, iv];
                for (int l = ivp; l < nsys; l++)
                {
                    vm[0, l, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vm[2, l, iv], vm[0, l, iv]);
                    vm[1, l, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vm[2, l, iv], vm[1, l, iv]);
                }

                vdel[0, 0, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 0, iv], vdel[0, 0, iv]);
                vdel[1, 0, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 0, iv], vdel[1, 0, iv]);
                vdel[0, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 1, iv], vdel[0, 1, iv]);
                vdel[1, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 1, iv], vdel[1, 1, iv]);
            }

            {
                T vtmp = va[0, 1, iv];
                for (int l = ivp; l < nsys; l++)
                {
                    vm[0, l, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vm[1, l, iv], vm[0, l, iv]);
                }

                vdel[0, 0, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[1, 0, iv], vdel[0, 0, iv]);
                vdel[0, 1, iv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp, vdel[1, 1, iv], vdel[0, 1, iv]);
            }

            if (iv == nsys - 1)
            {
                continue;
            }

            for (int k = 0; k < 3; k++)
            {
                T vtmp1 = vb[k, 0, ivp];
                T vtmp2 = vb[k, 1, ivp];
                T vtmp3 = vm[k, iv, ivp];
                for (int l = ivp; l < nsys; l++)
                {
                    T reduction = LegacyPrecisionMath.SeparateSumOfProducts(
                        vtmp1, vm[0, l, iv],
                        vtmp2, vm[1, l, iv],
                        vtmp3, vm[2, l, iv]);
                    vm[k, l, ivp] -= reduction;
                }

                T delta0 = LegacyPrecisionMath.SeparateSumOfProducts(
                    vtmp1, vdel[0, 0, iv],
                    vtmp2, vdel[1, 0, iv],
                    vtmp3, vdel[2, 0, iv]);
                T delta1 = LegacyPrecisionMath.SeparateSumOfProducts(
                    vtmp1, vdel[0, 1, iv],
                    vtmp2, vdel[1, 1, iv],
                    vtmp3, vdel[2, 1, iv]);

                vdel[k, 0, ivp] -= delta0;
                vdel[k, 1, ivp] -= delta1;
            }

            if (iv == ivte1 && ivz >= 0 && ivz < nsys)
            {
                for (int k = 0; k < 3; k++)
                {
                    T vtmp1 = vz[k, 0];
                    T vtmp2 = vz[k, 1];
                    for (int l = ivp; l < nsys; l++)
                    {
                        T reduction = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vm[0, l, iv], vtmp2, vm[1, l, iv]);
                        vm[k, l, ivz] -= reduction;
                    }

                    T delta0 = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vdel[0, 0, iv], vtmp2, vdel[1, 0, iv]);
                    T delta1 = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vdel[0, 1, iv], vtmp2, vdel[1, 1, iv]);
                    vdel[k, 0, ivz] -= delta0;
                    vdel[k, 1, ivz] -= delta1;
                }
            }

            if (ivp >= nsys - 1)
            {
                continue;
            }

            for (int kv = iv + 2; kv < nsys; kv++)
            {
                T vtmp1 = vm[0, iv, kv];
                T vtmp2 = vm[1, iv, kv];
                T vtmp3 = vm[2, iv, kv];

                if (T.Abs(vtmp1) > vacc1)
                {
                    for (int l = ivp; l < nsys; l++)
                    {
                        vm[0, l, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vm[2, l, iv], vm[0, l, kv]);
                    }

                    vdel[0, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 0, iv], vdel[0, 0, kv]);
                    vdel[0, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 1, iv], vdel[0, 1, kv]);
                }

                if (T.Abs(vtmp2) > vacc2)
                {
                    for (int l = ivp; l < nsys; l++)
                    {
                        vm[1, l, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vm[2, l, iv], vm[1, l, kv]);
                    }

                    vdel[1, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 0, iv], vdel[1, 0, kv]);
                    vdel[1, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 1, iv], vdel[1, 1, kv]);
                }

                if (T.Abs(vtmp3) > vacc3)
                {
                    for (int l = ivp; l < nsys; l++)
                    {
                        vm[2, l, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp3, vm[2, l, iv], vm[2, l, kv]);
                    }

                    vdel[2, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp3, vdel[2, 0, iv], vdel[2, 0, kv]);
                    vdel[2, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp3, vdel[2, 1, iv], vdel[2, 1, kv]);
                }
            }
        }

        // Legacy block: xsolve.f BLSOLV back substitution.
        // Difference from legacy: Same reverse sweep using helper-based multiply-subtract updates instead of inlined REAL statements.
        // Decision: Keep the reverse sweep structure and helper calls because they make parity mismatches auditable without changing the algorithm.
        for (int iv = nsys - 1; iv >= 1; iv--)
        {
            T vtmp = vdel[2, 0, iv];
            for (int kv = iv - 1; kv >= 0; kv--)
            {
                vdel[0, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[0, iv, kv], vtmp, vdel[0, 0, kv]);
                vdel[1, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[1, iv, kv], vtmp, vdel[1, 0, kv]);
                vdel[2, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[2, iv, kv], vtmp, vdel[2, 0, kv]);
            }

            vtmp = vdel[2, 1, iv];
            for (int kv = iv - 1; kv >= 0; kv--)
            {
                vdel[0, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[0, iv, kv], vtmp, vdel[0, 1, kv]);
                vdel[1, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[1, iv, kv], vtmp, vdel[1, 1, kv]);
                vdel[2, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vm[2, iv, kv], vtmp, vdel[2, 1, kv]);
            }
        }
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV pivot handling
    // Difference from legacy: The managed solver makes the pivot clamp explicit instead of relying on raw reciprocal behavior when a pivot approaches zero.
    // Decision: Keep the explicit guard because it improves robustness without changing valid-pivot behavior.
    private static T SafeReciprocal<T>(T value)
        where T : struct, IFloatingPointIeee754<T>
    {
        T pivotFloor = T.CreateChecked(PivotFloor);
        if (T.IsFinite(value) && T.Abs(value) >= pivotFloor)
        {
            return T.One / value;
        }

        T signValue = value == T.Zero || T.IsNaN(value) ? T.One : value;
        T safeValue = T.CopySign(pivotFloor, signValue);
        return T.One / safeValue;
    }

    // ThreadStatic buffer pool for the parity float workspace. The per-solve
    // allocation of 4×float[,,] + 1×float[,] was ~5 MB of GC pressure per
    // Newton iteration on 160-panel cases; routing through SolverBuffers
    // removes that from the hot path while preserving the exact copy
    // semantics (only indices [*,*,0..nsys) are consumed by the solver).
    private static float[,,] CopyToSingleInto(float[,,] result, double[,,] source, int nsys)
    {
        for (int i = 0; i < source.GetLength(0); i++)
        {
            for (int j = 0; j < source.GetLength(1); j++)
            {
                for (int k = 0; k < nsys; k++)
                {
                    result[i, j, k] = (float)source[i, j, k];
                }
            }
        }
        return result;
    }

    private static float[,] CopyToSingleInto(float[,] result, double[,] source)
    {
        for (int i = 0; i < source.GetLength(0); i++)
        {
            for (int j = 0; j < source.GetLength(1); j++)
            {
                result[i, j] = (float)source[i, j];
            }
        }
        return result;
    }

    // Legacy mapping: none
    // Difference from legacy: Managed-only copy-back helper that writes the parity float solution into the default double storage expected by the rest of the managed solver.
    // Decision: Keep the helper because it keeps the parity path isolated while leaving the public solver state in double precision.
    private static void CopySolutionToDouble(float[,,] source, double[,,] destination, int nsys)
    {
        for (int eq = 0; eq < source.GetLength(0); eq++)
        {
            for (int slot = 0; slot < source.GetLength(1); slot++)
            {
                for (int iv = 0; iv < nsys; iv++)
                {
                    destination[eq, slot, iv] = source[eq, slot, iv];
                }
            }
        }
    }
}

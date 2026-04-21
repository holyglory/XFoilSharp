using System;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: BLSOLV
// Secondary legacy source(s): none
// Role in port: Coupled block-tridiagonal Newton solve for the viscous system.
// Differences: Float-only after the Phase 1 strip — the legacy REAL workspace
// is the only path. The doubled tree (auto-generated *.Double.cs twin via
// gen-double.py) holds the algorithmic-parity double-precision mirror.
// Decision: Match the Fortran REAL kernel arithmetic exactly; let gen-double
// produce the wider-precision twin without hand-edit.

namespace XFoil.Solver.Numerics;

/// <summary>
/// Custom block solver for the coupled viscous/inviscid Newton system.
/// Port of BLSOLV from xsolve.f.
/// </summary>
public static class BlockTridiagonalSolver
{
    private const float PivotFloor = 1e-30f;

    /// <summary>
    /// Solves the coupled Newton system in place. The factored solution overwrites
    /// <see cref="ViscousNewtonSystem.VDEL"/>.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV
    // Difference from legacy: Float workspace mirrors the Fortran REAL kernel
    // exactly. The doubled tree drops the float casts and runs the same
    // algorithm in double.
    public static void Solve(
        ViscousNewtonSystem system,
        double vaccel = 0.01,
        bool useLegacyPrecision = true)
    {
        int nsys = system.NSYS;
        if (nsys <= 0)
        {
            return;
        }

        // Classic XFoil's BLSOLV runs in REAL. Keep the workspace on float so
        // arithmetic matches the legacy kernel. NOTE: VA/VB/VDEL are
        // [3, 2, nsysMax] but VM is [3, nsysMax, nsysMax]. Each buffer must
        // be sized from its own source's dims.
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

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV
    // Difference from legacy: Same elimination and back-substitution algorithm
    // expressed as a non-generic float-only core. Replaces the prior
    // SolveCore<T> generic; the doubled tree gets its own SolveCore via
    // gen-double.py textual substitution.
    private static void SolveCore(
        float[,,] va,
        float[,,] vb,
        float[,,] vm,
        float[,,] vdel,
        float[,] vz,
        int nsys,
        int ivte1,
        int ivz,
        double arcLengthSpan,
        float vaccel)
    {
        float span = MathF.Max((float)arcLengthSpan, 1e-12f);
        float vacc1 = vaccel;
        float vacc2 = (vaccel * 2f) / span;
        float vacc3 = (vaccel * 2f) / span;

        // Legacy block: xsolve.f BLSOLV forward elimination sweep.
        for (int iv = 0; iv < nsys; iv++)
        {
            int ivp = iv + 1;

            float pivot = SafeReciprocal(va[0, 0, iv]);
            va[0, 1, iv] *= pivot;
            for (int l = iv; l < nsys; l++)
            {
                vm[0, l, iv] *= pivot;
            }

            vdel[0, 0, iv] *= pivot;
            vdel[0, 1, iv] *= pivot;

            for (int k = 1; k < 3; k++)
            {
                float vtmp = va[k, 0, iv];
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
                float vtmp = va[2, 1, iv];
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
                float vtmp1 = vm[0, iv, iv];
                float vtmp2 = vm[1, iv, iv];
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
                float vtmp = va[0, 1, iv];
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
                float vtmp1 = vb[k, 0, ivp];
                float vtmp2 = vb[k, 1, ivp];
                float vtmp3 = vm[k, iv, ivp];
                for (int l = ivp; l < nsys; l++)
                {
                    float reduction = LegacyPrecisionMath.SeparateSumOfProducts(
                        vtmp1, vm[0, l, iv],
                        vtmp2, vm[1, l, iv],
                        vtmp3, vm[2, l, iv]);
                    vm[k, l, ivp] -= reduction;
                }

                float delta0 = LegacyPrecisionMath.SeparateSumOfProducts(
                    vtmp1, vdel[0, 0, iv],
                    vtmp2, vdel[1, 0, iv],
                    vtmp3, vdel[2, 0, iv]);
                float delta1 = LegacyPrecisionMath.SeparateSumOfProducts(
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
                    float vtmp1 = vz[k, 0];
                    float vtmp2 = vz[k, 1];
                    for (int l = ivp; l < nsys; l++)
                    {
                        float reduction = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vm[0, l, iv], vtmp2, vm[1, l, iv]);
                        vm[k, l, ivz] -= reduction;
                    }

                    float delta0 = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vdel[0, 0, iv], vtmp2, vdel[1, 0, iv]);
                    float delta1 = LegacyPrecisionMath.SeparateSumOfProducts(vtmp1, vdel[0, 1, iv], vtmp2, vdel[1, 1, iv]);
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
                float vtmp1 = vm[0, iv, kv];
                float vtmp2 = vm[1, iv, kv];
                float vtmp3 = vm[2, iv, kv];

                if (MathF.Abs(vtmp1) > vacc1)
                {
                    for (int l = ivp; l < nsys; l++)
                    {
                        vm[0, l, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vm[2, l, iv], vm[0, l, kv]);
                    }

                    vdel[0, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 0, iv], vdel[0, 0, kv]);
                    vdel[0, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp1, vdel[2, 1, iv], vdel[0, 1, kv]);
                }

                if (MathF.Abs(vtmp2) > vacc2)
                {
                    for (int l = ivp; l < nsys; l++)
                    {
                        vm[1, l, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vm[2, l, iv], vm[1, l, kv]);
                    }

                    vdel[1, 0, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 0, iv], vdel[1, 0, kv]);
                    vdel[1, 1, kv] = LegacyPrecisionMath.SeparateMultiplySubtract(vtmp2, vdel[2, 1, iv], vdel[1, 1, kv]);
                }

                if (MathF.Abs(vtmp3) > vacc3)
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
        for (int iv = nsys - 1; iv >= 1; iv--)
        {
            float vtmp = vdel[2, 0, iv];
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
    // Difference from legacy: Explicit pivot clamp instead of relying on raw
    // reciprocal behavior near zero.
    private static float SafeReciprocal(float value)
    {
        if (float.IsFinite(value) && MathF.Abs(value) >= PivotFloor)
        {
            return 1f / value;
        }

        float signValue = value == 0f || float.IsNaN(value) ? 1f : value;
        float safeValue = MathF.CopySign(PivotFloor, signValue);
        return 1f / safeValue;
    }

    // ThreadStatic buffer pool for the float workspace. The per-solve allocation
    // of 4×float[,,] + 1×float[,] was ~5 MB of GC pressure per Newton iteration
    // on 160-panel cases; routing through SolverBuffers removes that from the
    // hot path while preserving the exact copy semantics (only indices
    // [*,*,0..nsys) are consumed by the solver).
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
    // Difference from legacy: Managed-only copy-back helper that writes the
    // float solution into the public double storage expected by the rest of
    // the managed solver.
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

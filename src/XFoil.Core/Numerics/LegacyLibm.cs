using System;
using System.Runtime.InteropServices;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/naca.f :: NACA4 exponentiation sites
// Role in port: Managed parity helper that routes selected single-precision operations through the platform libm entry points used by the native Fortran runtime.
// Differences: Legacy XFoil relies on the host Fortran runtime's REAL math intrinsics implicitly; this file makes that dependency explicit through P/Invoke.
// Decision: Keep the managed libm bridge because it is the cleanest way to reproduce legacy single-precision behavior where binary parity depends on it.
namespace XFoil.Core.Numerics;

public static class LegacyLibm
{
    // The parity branch must use the same single-precision libm entry points as
    // the native Fortran runtime whenever possible. MathF.Pow is close, but it
    // is not bitwise identical to powf on all targets.
    // Legacy mapping: none; this is a managed parity-support wrapper around the host C runtime `powf`.
    // Difference from legacy: The bridge chooses the platform DLL explicitly and falls back to `MathF.Pow` only when the native symbol cannot be loaded.
    // Decision: Keep the bridge because parity-sensitive geometry and solver paths need access to the same single-precision libm behavior as the legacy runtime.
    public static float Pow(float value, float exponent)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return PowfMac(value, exponent);
            }

            if (OperatingSystem.IsLinux())
            {
                return PowfLinux(value, exponent);
            }

            if (OperatingSystem.IsWindows())
            {
                return PowfWindows(value, exponent);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return MathF.Pow(value, exponent);
    }

    // Legacy mapping: none; this is a managed parity-support wrapper around the host C runtime `logf`.
    // Difference from legacy: The bridge picks the platform `logf` entry point explicitly instead of inheriting it transitively from the Fortran runtime.
    // Decision: Keep the bridge because separated-branch boundary-layer kernels and panel singularity formulas can be one ULP apart when `MathF.Log` differs from the native `logf`.
    public static float Log(float value)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return LogfMac(value);
            }

            if (OperatingSystem.IsLinux())
            {
                return LogfLinux(value);
            }

            if (OperatingSystem.IsWindows())
            {
                return LogfWindows(value);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return MathF.Log(value);
    }

    // Legacy mapping: none; this is a managed parity-support wrapper around the host C runtime `sqrtf`.
    // Difference from legacy: The bridge picks the platform `sqrtf` entry point explicitly instead of relying on .NET's `MathF.Sqrt`.
    // Decision: Keep the bridge because parity-sensitive single-precision geometry lengths can differ by one ULP when `MathF.Sqrt` does not mirror the native Fortran runtime.
    public static float Sqrt(float value)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return SqrtfMac(value);
            }

            if (OperatingSystem.IsLinux())
            {
                return SqrtfLinux(value);
            }

            if (OperatingSystem.IsWindows())
            {
                return SqrtfWindows(value);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return MathF.Sqrt(value);
    }

    // Legacy mapping: none; this is a managed parity-support wrapper around the host C runtime `tanhf`.
    // Difference from legacy: The bridge picks the platform `tanhf` entry point explicitly instead of inheriting it transitively from the Fortran runtime.
    // Decision: Keep the bridge because several boundary-layer kernels depend on native single-precision hyperbolic tangents to stay on the Fortran bit path.
    public static float Tanh(float value)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return TanhfMac(value);
            }

            if (OperatingSystem.IsLinux())
            {
                return TanhfLinux(value);
            }

            if (OperatingSystem.IsWindows())
            {
                return TanhfWindows(value);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return MathF.Tanh(value);
    }

    // Legacy mapping: none; this extern binds the macOS `powf` symbol used to emulate legacy runtime behavior.
    // Difference from legacy: The symbol binding is explicit in .NET rather than implicit through the Fortran toolchain.
    // Decision: Keep the explicit binding because it makes the parity dependency visible and testable.
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfMac(float value, float exponent);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfMac(float value);

    // Legacy mapping: none; this extern binds the Linux `powf` symbol used to emulate legacy runtime behavior.
    // Difference from legacy: The symbol binding is explicit in .NET rather than implicit through the Fortran toolchain.
    // Decision: Keep the explicit binding because it makes the parity dependency visible and testable.
    [DllImport("libm.so.6", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfLinux(float value, float exponent);

    [DllImport("libm.so.6", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfLinux(float value);

    // Legacy mapping: none; this extern binds the Windows `powf` symbol used to emulate legacy runtime behavior.
    // Difference from legacy: The symbol binding is explicit in .NET rather than implicit through the Fortran toolchain.
    // Decision: Keep the explicit binding because it makes the parity dependency visible and testable.
    [DllImport("ucrtbase.dll", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfWindows(float value, float exponent);

    [DllImport("ucrtbase.dll", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfWindows(float value);
}

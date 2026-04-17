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
    // Per-call `OperatingSystem.IsLinux()` / `IsMacOS()` / `IsWindows()` checks
    // are cheap in isolation but add up inside the hot path where these
    // wrappers fire millions of times per sweep. Resolve the platform once
    // at static init and dispatch through cached delegates.
    private static readonly Func<float, float, float> s_pow;
    private static readonly Func<float, float> s_log;
    private static readonly Func<float, float> s_log10;
    private static readonly Func<float, float> s_sin;
    private static readonly Func<float, float> s_cos;
    private static readonly Func<float, float> s_sqrt;
    private static readonly Func<float, float> s_tanh;
    private static readonly Func<float, float> s_exp;
    private static readonly Func<float, float, float> s_atan2;

    static LegacyLibm()
    {
        s_pow = ResolvePow();
        s_log = Resolve(MathF.Log, LogfMac, LogfLinux, LogfWindows);
        s_log10 = Resolve(MathF.Log10, Log10fMac, Log10fLinux, Log10fWindows);
        s_sin = ResolveLinuxOnly(MathF.Sin, SinfLinux);
        s_cos = ResolveLinuxOnly(MathF.Cos, CosfLinux);
        s_sqrt = Resolve(MathF.Sqrt, SqrtfMac, SqrtfLinux, SqrtfWindows);
        s_tanh = Resolve(MathF.Tanh, TanhfMac, TanhfLinux, TanhfWindows);
        s_exp = Resolve(MathF.Exp, ExpfMac, ExpfLinux, ExpfWindows);
        s_atan2 = ResolveBinary(MathF.Atan2, Atan2fMac, Atan2fLinux, Atan2fWindows);
    }

    private static Func<float, float, float> ResolvePow()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) { _ = PowfMac(1f, 1f); return PowfMac; }
            if (OperatingSystem.IsLinux()) { _ = PowfLinux(1f, 1f); return PowfLinux; }
            if (OperatingSystem.IsWindows()) { _ = PowfWindows(1f, 1f); return PowfWindows; }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return MathF.Pow;
    }

    private static Func<float, float> Resolve(
        Func<float, float> fallback,
        Func<float, float> mac,
        Func<float, float> linux,
        Func<float, float> windows)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) { _ = mac(1f); return mac; }
            if (OperatingSystem.IsLinux()) { _ = linux(1f); return linux; }
            if (OperatingSystem.IsWindows()) { _ = windows(1f); return windows; }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return fallback;
    }

    private static Func<float, float> ResolveLinuxOnly(
        Func<float, float> fallback,
        Func<float, float> linux)
    {
        try
        {
            if (OperatingSystem.IsLinux()) { _ = linux(1f); return linux; }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return fallback;
    }

    private static Func<float, float, float> ResolveBinary(
        Func<float, float, float> fallback,
        Func<float, float, float> mac,
        Func<float, float, float> linux,
        Func<float, float, float> windows)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) { _ = mac(1f, 1f); return mac; }
            if (OperatingSystem.IsLinux()) { _ = linux(1f, 1f); return linux; }
            if (OperatingSystem.IsWindows()) { _ = windows(1f, 1f); return windows; }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return fallback;
    }

    // The parity branch must use the same single-precision libm entry points as
    // the native Fortran runtime whenever possible. MathF.Pow is close, but it
    // is not bitwise identical to powf on all targets.
    public static float Pow(float value, float exponent) => s_pow(value, exponent);

    public static float Log(float value) => s_log(value);
    public static float Log10(float value) => s_log10(value);
    public static float Sin(float value) => s_sin(value);
    public static float Cos(float value) => s_cos(value);
    public static float Sqrt(float value) => s_sqrt(value);
    public static float Tanh(float value) => s_tanh(value);
    public static float Exp(float value) => s_exp(value);
    public static float Atan2(float y, float x) => s_atan2(y, x);

    // Legacy mapping: none; this extern binds the macOS `powf` symbol used to emulate legacy runtime behavior.
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfMac(float value, float exponent);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "log10f", ExactSpelling = true)]
    private static extern float Log10fMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "expf", ExactSpelling = true)]
    private static extern float ExpfMac(float value);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "atan2f", ExactSpelling = true)]
    private static extern float Atan2fMac(float y, float x);

    // Legacy mapping: none; this extern binds the Linux `powf` symbol used to emulate legacy runtime behavior.
    [DllImport("libm.so.6", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfLinux(float value, float exponent);

    [DllImport("libm.so.6", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "log10f", ExactSpelling = true)]
    private static extern float Log10fLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "sinf", ExactSpelling = true)]
    private static extern float SinfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "cosf", ExactSpelling = true)]
    private static extern float CosfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "expf", ExactSpelling = true)]
    private static extern float ExpfLinux(float value);

    [DllImport("libm.so.6", EntryPoint = "atan2f", ExactSpelling = true)]
    private static extern float Atan2fLinux(float y, float x);

    // Legacy mapping: none; this extern binds the Windows `powf` symbol used to emulate legacy runtime behavior.
    [DllImport("ucrtbase.dll", EntryPoint = "powf", ExactSpelling = true)]
    private static extern float PowfWindows(float value, float exponent);

    [DllImport("ucrtbase.dll", EntryPoint = "logf", ExactSpelling = true)]
    private static extern float LogfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "log10f", ExactSpelling = true)]
    private static extern float Log10fWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "sqrtf", ExactSpelling = true)]
    private static extern float SqrtfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "tanhf", ExactSpelling = true)]
    private static extern float TanhfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "expf", ExactSpelling = true)]
    private static extern float ExpfWindows(float value);

    [DllImport("ucrtbase.dll", EntryPoint = "atan2f", ExactSpelling = true)]
    private static extern float Atan2fWindows(float y, float x);
}

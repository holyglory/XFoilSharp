// Legacy audit:
// Primary legacy source: f_xfoil/src/XFOIL.INC :: REAL arithmetic lineage, f_xfoil/src/xblsys.f :: BLKIN/BLVAR/BLDIF scalar staging, f_xfoil/src/xsolve.f :: GAUSS/LUDCMP/BAKSUB contraction-sensitive updates
// Secondary legacy source: f_xfoil/src/xpanel.f :: streamfunction and panel-kernel REAL staging
// Role in port: Central parity-math helper that makes classic XFoil REAL rounding, source-ordered products, and explicitly fused helper families visible in managed code.
// Differences: The legacy code relies on source-level REAL declarations and the native build's contraction behavior, while the managed port spells those arithmetic choices out as named helpers and keeps the default runtime on double.
// Decision: Keep the shared helper surface because it gives the audit one central place to enforce parity arithmetic policy without degrading the default managed path.
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using XFoil.Core.Numerics;
using XFoil.Solver.Diagnostics;

namespace XFoil.Solver.Numerics;

internal static class LegacyPrecisionMath
{
    /// <summary>
    /// When true, all FMA helpers use separate multiply-then-add with RoundBarrier
    /// instead of hardware FMA. This matches Fortran compiled with -O0 -ffp-contract=off
    /// and makes any remaining mismatch a real algorithmic bug, not an FMA artifact.
    /// </summary>
    internal static bool DisableFma { get; set; }
        = DebugFlags.DisableFma;

    /// <summary>
    /// FMA or separate multiply-add depending on DisableFma flag.
    /// When DisableFma is true: returns REAL-style a*b + c with separate
    /// product and sum roundings.
    /// When false: returns Fma(a, b, c) (single rounding).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Fma(float a, float b, float c)
        => DisableFma
            ? RoundBarrier(RoundBarrier(a * b) + c)
            : MathF.FusedMultiplyAdd(a, b, c);

    // Phase 1 doubled-tree counterpart. Same DisableFma-aware routing as the
    // float Fma above. Auto-generated *.Double.cs twins call this via
    // overload resolution when their args become double.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Fma(double a, double b, double c)
        => DisableFma
            ? RoundBarrier(RoundBarrier(a * b) + c)
            : Math.FusedMultiplyAdd(a, b, c);

    // Legacy mapping: f_xfoil/src/XFOIL.INC :: GAMMA/GAMM1 REAL staging and the general BLKIN/BLVAR scalar REAL path.
    // Difference from legacy: The managed port centralizes classic REAL rounding and scalar operator selection into explicit helpers instead of relying on ambient type declarations.
    // Decision: Keep this scalar helper family because it provides the audit's canonical legacy-float template.
    // Classic XFoil stores GAMMA as REAL and then derives GAMM1 = GAMMA - 1.0
    // in REAL as well. The parity path must preserve that staging instead of
    // using the double literal 0.4, or every compressibility derivative starts
    // one ULP away before BLKIN/BLVAR even run.
    internal static double GammaMinusOne(bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)1.4f - 1.0f) : 0.4;

    internal static double RoundToSingle(double value) => (float)value;

    internal static double RoundToSingle(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)value : value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float RoundBarrier(float value)
        => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));

    // Phase 1 doubled-tree counterpart. Round-trips through Int64Bits so the JIT
    // can't keep wider precision in registers across the call. Auto-generated
    // *.Double.cs twins reach this via gen-double's `RoundBarrier(<doubleArg>)`
    // overload resolution.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static double RoundBarrier(double value)
        => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value));


    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float AddRounded(float left, float right)
    {
        float roundedLeft = RoundBarrier(left);
        float roundedRight = RoundBarrier(right);
        return RoundBarrier(roundedLeft + roundedRight);
    }

    // Phase 1 doubled-tree counterpart. Same shape as the float AddRounded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static double AddRounded(double left, double right)
    {
        double roundedLeft = RoundBarrier(left);
        double roundedRight = RoundBarrier(right);
        return RoundBarrier(roundedLeft + roundedRight);
    }

    internal static double Multiply(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)left * (float)right) : left * right;

    // -----------------------------------------------------------------
    // Float-native overloads for the legacy-precision branch. These do
    // not take a useLegacyPrecision flag; they are pure float arithmetic
    // matching what the `(float)((float)left * (float)right)` pattern
    // produces above but without the double↔float roundtrip noise.
    // Callers that can hold float locals throughout a parity block should
    // prefer these to avoid repeatedly converting back to double between
    // operations.
    // -----------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MultiplyF(float left, float right) => left * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MultiplyF(float left, float middle, float right) => left * middle * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float AddF(float left, float right) => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SubtractF(float left, float right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float NegateF(float value) => -value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float DivideF(float numerator, float denominator) => numerator / denominator;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float AverageF(float left, float right) => 0.5f * (left + right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SquareF(float value) => value * value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SqrtF(float value) => MathF.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float PowF(float value, float exponent) => LegacyLibm.Pow(value, exponent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float ExpF(float value) => LegacyLibm.Exp(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float LogF(float value) => LegacyLibm.Log(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Log10F(float value) => LegacyLibm.Log10(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float TanhF(float value) => LegacyLibm.Tanh(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SinF(float value) => MathF.Sin(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float CosF(float value) => MathF.Cos(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Atan2F(float y, float x) => LegacyLibm.Atan2(y, x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float AbsF(float value) => MathF.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MaxF(float left, float right) => MathF.Max(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MinF(float left, float right) => MathF.Min(left, right);

    // -----------------------------------------------------------------
    // Phase 1 doubled-tree counterparts. Same shape as the *F helpers
    // above but using double arithmetic — no float casts. Doubled callers
    // (auto-generated *.Double.cs twins via gen-double.py) reach these
    // via the gen-double `LegacyPrecisionMath.*F` → `LegacyPrecisionMath.*D`
    // substitution.
    // -----------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MultiplyD(double left, double right) => left * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MultiplyD(double left, double middle, double right) => left * middle * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double AddD(double left, double right) => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SubtractD(double left, double right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double NegateD(double value) => -value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DivideD(double numerator, double denominator) => numerator / denominator;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double AverageD(double left, double right) => 0.5d * (left + right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SquareD(double value) => value * value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SqrtD(double value) => Math.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double PowD(double value, double exponent) => Math.Pow(value, exponent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ExpD(double value) => Math.Exp(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double LogD(double value) => Math.Log(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Log10D(double value) => Math.Log10(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double TanhD(double value) => Math.Tanh(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SinD(double value) => Math.Sin(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double CosD(double value) => Math.Cos(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Atan2D(double y, double x) => Math.Atan2(y, x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double AbsD(double value) => Math.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MaxD(double left, double right) => Math.Max(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MinD(double left, double right) => Math.Min(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MultiplyAddD(double left, double right, double addend) => (left * right) + addend;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double MultiplySubtractD(double left, double right, double minuend) => minuend - (left * right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ProductThenAddD(double left, double right, double addend) => (left * right) + addend;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ProductThenSubtractD(double left, double right, double subtrahend) => (left * right) - subtrahend;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SumOfProductsD(double left1, double right1, double left2, double right2)
        => (left1 * right1) + (left2 * right2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double SumOfProductsD(double left1, double right1, double left2, double right2, double left3, double right3)
        => (left1 * right1) + (left2 * right2) + (left3 * right3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MultiplyAddF(float left, float right, float addend) => Fma(left, right, addend);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float MultiplySubtractF(float left, float right, float minuend) => Fma(-left, right, minuend);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float ProductThenAddF(float left, float right, float addend) => (left * right) + addend;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float ProductThenSubtractF(float left, float right, float subtrahend) => (left * right) - subtrahend;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SumOfProductsF(float left1, float right1, float left2, float right2)
        => Fma(left1, right1, left2 * right2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SumOfProductsF(float left1, float right1, float left2, float right2, float left3, float right3)
        => Fma(left1, right1, Fma(left2, right2, left3 * right3));

    internal static double Multiply(double left, double middle, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)(((float)left * (float)middle) * (float)right) : left * middle * right;

    internal static double Add(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)left + (float)right) : left + right;

    internal static double Subtract(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)left - (float)right) : left - right;

    internal static double Negate(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? -(float)value : -value;

    internal static double Divide(double numerator, double denominator, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)numerator / (float)denominator) : numerator / denominator;

    internal static double Average(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision
            ? (float)(0.5f * ((float)left + (float)right))
            : 0.5 * (left + right);

    internal static double Square(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)value * (float)value) : value * value;

    internal static double Sqrt(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Sqrt((float)value) : Math.Sqrt(value);

    internal static double Pow(double value, double exponent, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Pow((float)value, (float)exponent) : Math.Pow(value, exponent);

    internal static double Exp(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Exp((float)value) : Math.Exp(value);

    internal static double Log(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Log((float)value) : Math.Log(value);

    internal static double Log10(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Log10((float)value) : Math.Log10(value);

    internal static double Tanh(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Tanh((float)value) : Math.Tanh(value);

    internal static double Sin(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Sin((float)value) : Math.Sin(value);

    internal static double Cos(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Cos((float)value) : Math.Cos(value);

    internal static double Atan2(double y, double x, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Atan2((float)y, (float)x) : Math.Atan2(y, x);

    internal static double Abs(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Abs((float)value) : Math.Abs(value);

    internal static double Max(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Max((float)left, (float)right) : Math.Max(left, right);

    internal static double Min(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Min((float)left, (float)right) : Math.Min(left, right);

    // Legacy mapping: f_xfoil/src/xbl.f :: UPDATE-style REAL writes and f_xfoil/src/xblsys.f :: traced fused transport/update sites.
    // Difference from legacy: The managed port distinguishes explicit fused helpers from separately-rounded product/add helpers so parity work does not depend on JIT contraction luck.
    // Decision: Keep this update helper family because it makes the parity choice explicit at each call site.
    // Many legacy update paths are literally "value + relax * delta" in REAL.
    // Using an explicit fused update for the parity branch keeps those writes on
    // the same last-bit path as the classic solver without affecting doubles.
    internal static double AddScaled(double value, double scale, double delta, bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma((float)scale, (float)delta, (float)value)
            : value + (scale * delta);

    // Use the contracted helpers only for legacy REAL blocks that have been
    // traced and shown to behave like fused multiply-add in the native build.
    // Plain Fortran product/add source trees should use ProductThenAdd or
    // ProductThenSubtract so the intermediate product rounds separately.
    internal static double MultiplyAdd(double left, double right, double addend, bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma((float)left, (float)right, (float)addend)
            : (left * right) + addend;

    internal static double MultiplySubtract(double left, double right, double minuend, bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma(-(float)left, (float)right, (float)minuend)
            : minuend - (left * right);

    internal static double ProductThenAdd(double left, double right, double addend, bool useLegacyPrecision)
    {
        float product = (float)left * (float)right;
        return product + (float)addend;
    }

    internal static double ProductThenSubtract(double left, double right, double subtrahend, bool useLegacyPrecision)
    {
        float product = (float)left * (float)right;
        return product - (float)subtrahend;
    }

    // Legacy mapping: f_xfoil/src/xblsys.f :: multi-term row sums and transport expressions in BLDIF/BLVAR/TRDIF.
    // Difference from legacy: The managed port provides both contracted and explicitly regrouped multi-product helpers so traced sites can state which legacy arithmetic family they need.
    // Decision: Keep this helper family because repeated multi-product parity fixes should be centralized instead of reimplemented ad hoc.
    internal static double SumOfProducts(
        double left1,
        double right1,
        double left2,
        double right2,
        bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma(
                (float)left1,
                (float)right1,
                (float)((float)left2 * (float)right2))
            : (left1 * right1) + (left2 * right2);

    internal static double SumOfProducts(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma(
                (float)left3,
                (float)right3,
                Fma(
                    (float)left1,
                    (float)right1,
                    (float)((float)left2 * (float)right2)))
            : (left1 * right1) + (left2 * right2) + (left3 * right3);

    internal static double SumOfProducts(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        double left4,
        double right4,
        bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma(
                (float)left4,
                (float)right4,
                Fma(
                    (float)left3,
                    (float)right3,
                    Fma(
                        (float)left1,
                        (float)right1,
                (float)((float)left2 * (float)right2))))
            : (left1 * right1) + (left2 * right2) + (left3 * right3) + (left4 * right4);

    internal static double SumOfProducts(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        double left4,
        double right4,
        double left5,
        double right5,
        double left6,
        double right6,
        bool useLegacyPrecision)
        => useLegacyPrecision
            ? Fma(
                (float)left6,
                (float)right6,
                Fma(
                    (float)left5,
                    (float)right5,
                    Fma(
                        (float)left4,
                        (float)right4,
                        Fma(
                            (float)left3,
                            (float)right3,
                            Fma(
                                (float)left2,
                                (float)right2,
                                (float)((float)left1 * (float)right1))))))
            : (left1 * right1) + (left2 * right2) + (left3 * right3)
            + (left4 * right4) + (left5 * right5) + (left6 * right6);

    // Use the source-ordered helpers for plain Fortran expressions such as
    // "A*B + C*D + E*F [+ G]". The generic SumOfProducts helpers are kept for
    // sites that have been explicitly traced and shown to need contracted or
    // regrouped behavior; they are not a safe default for every legacy row sum.
    // Legacy mapping: f_xfoil/src/xblsys.f :: plain left-associated REAL source rows.
    // Difference from legacy: The managed port names the source-ordered addition family explicitly so non-contracted legacy rows do not silently drift toward fused behavior.
    // Decision: Keep the source-ordered helper family because it is the safe default for plain translated Fortran expressions.
    internal static double SourceOrderedProductSum(
        double left1,
        double right1,
        double left2,
        double right2,
        bool useLegacyPrecision)
    {
        float sum = (float)left1 * (float)right1;
        sum = (float)(sum + ((float)left2 * (float)right2));
        return sum;
    }

    internal static double SourceOrderedProductSum(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        bool useLegacyPrecision)
    {
        float sum = (float)left1 * (float)right1;
        sum = (float)(sum + ((float)left2 * (float)right2));
        sum = (float)(sum + ((float)left3 * (float)right3));
        return sum;
    }

    internal static double SourceOrderedProductSumAdd(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        double addend,
        bool useLegacyPrecision)
    {
        // Products-first, addend-last matches the -O2 Fortran best.
        // Source order (addend first) gives 138/180 diffs vs 46/180.
        float sum = (float)left1 * (float)right1;
        sum = (float)(sum + ((float)left2 * (float)right2));
        sum = (float)(sum + ((float)left3 * (float)right3));
        sum = (float)(sum + (float)addend);
        return sum;
    }

    // Legacy mapping: traced native REAL expression sites where the compiled
    // `left1*right1 + left2*right2` source tree behaves like
    // `fma(left1, right1, round(left2*right2))`.
    // Difference from legacy: The managed port names this native-expression
    // replay explicitly instead of relying on ambient contraction and register
    // staging from the native toolchain.
    // Decision: Keep this helper narrowly scoped to traced sites; it is not a
    // blanket replacement for the stricter source-ordered helpers.
    internal static double NativeFloatExpressionProductSum(
        double left1,
        double right1,
        double left2,
        double right2,
        bool useLegacyPrecision)
    {
        float addend = (float)((float)left2 * (float)right2);
        return Fma((float)left1, (float)right1, addend);
    }

    internal static double NativeFloatExpressionProductSumAdd(
        double left1,
        double right1,
        double left2,
        double right2,
        double left3,
        double right3,
        double addend,
        bool useLegacyPrecision)
    {
        float sum = Fma((float)left1, (float)right1, (float)addend);
        sum = Fma((float)left2, (float)right2, sum);
        sum = Fma((float)left3, (float)right3, sum);
        return sum;
    }

    // Legacy mapping: f_xfoil/src/xblsys.f and f_xfoil/src/xsolve.f :: traced native contraction-sensitive REAL sites.
    // Difference from legacy: The managed port factors the generic fused and difference-of-products helpers into one place instead of repeating type checks and FMA calls at every parity-sensitive site.
    // Decision: Keep this generic helper family because it standardizes parity-only fused behavior across solver kernels.
    // The classic single-precision kernels often behave like a left-associative
    // chain of fused multiply-adds. Exposing that explicitly keeps the parity
    // path readable and avoids hand-inlining the same contraction pattern.
    internal static T FusedMultiplyAdd<T>(T left, T right, T addend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float result = Fma(
                float.CreateChecked(left),
                float.CreateChecked(right),
                float.CreateChecked(addend));
            return T.CreateChecked(result);
        }

        return (left * right) + addend;
    }

    internal static T SumOfProducts<T>(T left1, T right1, T left2, T right2)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Barrier the addend so JIT can't keep it in extended precision
            float addend = RoundBarrier(float.CreateChecked(left2) * float.CreateChecked(right2));
            float result = Fma(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                addend);
            return T.CreateChecked(result);
        }

        return (left1 * right1) + (left2 * right2);
    }

    internal static T SumOfProducts<T>(T left1, T right1, T left2, T right2, T left3, T right3)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Fortran: FMAF_REAL(left3, right3, FMAF_REAL(left1, right1, left2*right2))
            // The inner FMAF_REAL receives left2*right2 as the addend, which is already
            // a REAL value. Barrier it so the JIT can't keep it in extended precision.
            float innerAddend = RoundBarrier(float.CreateChecked(left2) * float.CreateChecked(right2));
            float productChain = Fma(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                innerAddend);
            float result = Fma(
                float.CreateChecked(left3),
                float.CreateChecked(right3),
                productChain);
            return T.CreateChecked(result);
        }

        return (left1 * right1) + (left2 * right2) + (left3 * right3);
    }

    // Some legacy REAL rows behave like a fused three-product bundle plus a
    // final transport/scalar term, rather than a plain four-term float sum.
    // Keeping that as a shared helper makes the parity branch consistent and
    // gives the audit a single pattern to enforce.
    internal static T SumOfProductsAndAdd<T>(
        T left1,
        T right1,
        T left2,
        T right2,
        T left3,
        T right3,
        T addend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float productChain = Fma(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                float.CreateChecked(left2) * float.CreateChecked(right2));
            float productSum = Fma(
                float.CreateChecked(left3),
                float.CreateChecked(right3),
                productChain);
            return T.CreateChecked(productSum + float.CreateChecked(addend));
        }

        return (left1 * right1) + (left2 * right2) + (left3 * right3) + addend;
    }

    internal static T WeightedProductBlend<T>(
        T leftWeight,
        T leftValue,
        T leftScale,
        T rightWeight,
        T rightValue,
        T rightScale)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Some classic REAL transport blends match native XFoil only when
            // the left weighted product is fused with the rounded right term.
            float weightedLeft = float.CreateChecked(leftWeight) * float.CreateChecked(leftValue);
            float weightedRight = float.CreateChecked(rightWeight) * float.CreateChecked(rightValue);
            float result = Fma(
                weightedLeft,
                float.CreateChecked(leftScale),
                weightedRight * float.CreateChecked(rightScale));
            return T.CreateChecked(result);
        }

        return (leftWeight * leftValue * leftScale) + (rightWeight * rightValue * rightScale);
    }

    internal static T DifferenceOfProducts<T>(
        T leftValue,
        T leftScale,
        T rightValue,
        T rightScale)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Legacy REAL kernels can contract a*b - c*d as a fused multiply-add
            // with the right product already rounded. The parity path spells that
            // out so the managed result stops depending on JIT contraction luck.
            float roundedRightProduct = float.CreateChecked(rightValue) * float.CreateChecked(rightScale);
            float result = Fma(
                float.CreateChecked(leftValue),
                float.CreateChecked(leftScale),
                -roundedRightProduct);
            return T.CreateChecked(result);
        }

        return (leftValue * leftScale) - (rightValue * rightScale);
    }

    internal static T FusedMultiplySubtract<T>(T left, T right, T minuend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float result = Fma(
                -float.CreateChecked(left),
                float.CreateChecked(right),
                float.CreateChecked(minuend));
            return T.CreateChecked(result);
        }

        return minuend - (left * right);
    }

    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL back substitution D(K)=D(K)-C(K)*D(K+1).
    // Difference from legacy: This helper preserves the proved single-round float-expression replay
    // even when the global "disable FMA" diagnostics mode is enabled for other kernels.
    // Decision: Keep this separate from SeparateMultiplySubtract because standalone/full-run traces
    // show TRISOL back substitution matches a contracted single-round result while GAUSS does not.
    internal static T ContractedMultiplySubtract<T>(T left, T right, T minuend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Fortran with -mno-fma uses separate mulss;subss.
            // Respect DisableFma to match.
            float result = Fma(
                -float.CreateChecked(left),
                float.CreateChecked(right),
                float.CreateChecked(minuend));
            return T.CreateChecked(result);
        }

        return minuend - (left * right);
    }

    // Legacy mapping: f_xfoil/src/xblsys.f :: CFL/CFT REAL product-then-add staging.
    // Difference from legacy: Preserves the non-contracted "product, then add constant"
    // pattern used in CFL where the addend is a literal constant that gfortran does NOT
    // contract into FMA at -O2 -march=native.
    // Decision: Do NOT use FMA here — the Fortran reference keeps these as two separate
    // operations (multiply rounded, then add/subtract rounded).
    internal static T ProductThenAdd<T>(T left, T right, T addend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float product = float.CreateChecked(left) * float.CreateChecked(right);
            return T.CreateChecked(product + float.CreateChecked(addend));
        }

        return (left * right) + addend;
    }

    internal static T ProductThenSubtract<T>(T left, T right, T subtrahend)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float product = float.CreateChecked(left) * float.CreateChecked(right);
            return T.CreateChecked(product - float.CreateChecked(subtrahend));
        }

        return (left * right) - subtrahend;
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: GAUSS elimination updates written as separate REAL multiply then subtract steps.
    // Difference from legacy: The managed port makes the two-rounding path explicit instead of letting optimization accidentally contract it.
    // Decision: Keep this helper because the parity seed and solver ports rely on that exact non-fused recurrence.
    internal static T SeparateMultiplySubtract<T>(T left, T right, T minuend)
        where T : struct, IFloatingPointIeee754<T>
    {
        // XFoil's GAUSS elimination updates are written as two distinct REAL
        // operations, PRODUCT then MINUEND-PRODUCT. The parity ports of those
        // solver kernels must preserve that double-rounding path instead of
        // contracting to a fused multiply-subtract.
        if (typeof(T) == typeof(float))
        {
            float product = RoundBarrier(float.CreateChecked(left) * float.CreateChecked(right));
            float result = RoundBarrier(float.CreateChecked(minuend) - product);
            return T.CreateChecked(result);
        }

        T genericProduct = left * right;
        return minuend - genericProduct;
    }

    // Non-generic float overload of SeparateMultiplySubtract. Matches the
    // typeof(T)==typeof(float) branch above bit-exactly. Added for the
    // float-tree strip (Phase 1 of the float→double tree split): callers in
    // the float tree should prefer this non-generic form so the doubled tree
    // gets a proper non-generic double overload from gen-double.py.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SeparateMultiplySubtract(float left, float right, float minuend)
    {
        float product = RoundBarrier(left * right);
        return RoundBarrier(minuend - product);
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV/GAUSS product-sum updates.
    // Difference from legacy: The managed port uses FMA chains for float to match
    // the contraction behavior that gfortran emits for REAL multiply-add patterns.
    // Decision: Keep FMA for float and plain arithmetic for double, matching the
    // SumOfProducts(... useLegacyPrecision) family used elsewhere.
    internal static T SeparateSumOfProducts<T>(T left1, T right1, T left2, T right2)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float result = Fma(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                float.CreateChecked(left2) * float.CreateChecked(right2));
            return T.CreateChecked(result);
        }

        return (left1 * right1) + (left2 * right2);
    }

    // Non-generic float overload of SeparateSumOfProducts(2-pair). Bit-exact
    // mirror of the float branch above. See SeparateMultiplySubtract overload
    // notes for the float-tree strip rationale.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SeparateSumOfProducts(float left1, float right1, float left2, float right2)
        => Fma(left1, right1, left2 * right2);

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV three-term elimination sums.
    // Difference from legacy: Uses FMA chains for float to match gfortran contraction.
    // Decision: Keep this aligned with the SumOfProducts family for consistency.
    internal static T SeparateSumOfProducts<T>(
        T left1,
        T right1,
        T left2,
        T right2,
        T left3,
        T right3)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Fortran: (VTMP1*V1 + VTMP2*V2 + VTMP3*V3) — left-to-right REAL
            // Must match: ((p1 + p2) + p3) with each product and sum round-barriered
            float p1 = RoundBarrier(float.CreateChecked(left1) * float.CreateChecked(right1));
            float p2 = RoundBarrier(float.CreateChecked(left2) * float.CreateChecked(right2));
            float p3 = RoundBarrier(float.CreateChecked(left3) * float.CreateChecked(right3));
            float s1 = RoundBarrier(p1 + p2);
            return T.CreateChecked(RoundBarrier(s1 + p3));
        }

        return ((left1 * right1) + (left2 * right2)) + (left3 * right3);
    }

    // Non-generic float overload of SeparateSumOfProducts(3-pair). Bit-exact
    // mirror of the float branch above. See SeparateMultiplySubtract overload
    // notes for the float-tree strip rationale.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SeparateSumOfProducts(
        float left1, float right1,
        float left2, float right2,
        float left3, float right3)
    {
        float p1 = RoundBarrier(left1 * right1);
        float p2 = RoundBarrier(left2 * right2);
        float p3 = RoundBarrier(left3 * right3);
        float s1 = RoundBarrier(p1 + p2);
        return RoundBarrier(s1 + p3);
    }
}

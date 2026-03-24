// Legacy audit:
// Primary legacy source: f_xfoil/src/XFOIL.INC :: REAL arithmetic lineage, f_xfoil/src/xblsys.f :: BLKIN/BLVAR/BLDIF scalar staging, f_xfoil/src/xsolve.f :: GAUSS/LUDCMP/BAKSUB contraction-sensitive updates
// Secondary legacy source: f_xfoil/src/xpanel.f :: streamfunction and panel-kernel REAL staging
// Role in port: Central parity-math helper that makes classic XFoil REAL rounding, source-ordered products, and explicitly fused helper families visible in managed code.
// Differences: The legacy code relies on source-level REAL declarations and the native build's contraction behavior, while the managed port spells those arithmetic choices out as named helpers and keeps the default runtime on double.
// Decision: Keep the shared helper surface because it gives the audit one central place to enforce parity arithmetic policy without degrading the default managed path.
using System.Numerics;
using System.Runtime.CompilerServices;
using XFoil.Core.Numerics;

namespace XFoil.Solver.Numerics;

internal static class LegacyPrecisionMath
{
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float AddRounded(float left, float right)
    {
        float roundedLeft = RoundBarrier(left);
        float roundedRight = RoundBarrier(right);
        return RoundBarrier(roundedLeft + roundedRight);
    }

    internal static double Multiply(double left, double right, bool useLegacyPrecision)
        => useLegacyPrecision ? (float)((float)left * (float)right) : left * right;

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
        => useLegacyPrecision ? MathF.Exp((float)value) : Math.Exp(value);

    internal static double Log(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Log((float)value) : Math.Log(value);

    internal static double Log10(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Log10((float)value) : Math.Log10(value);

    internal static double Tanh(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? LegacyLibm.Tanh((float)value) : Math.Tanh(value);

    internal static double Sin(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Sin((float)value) : Math.Sin(value);

    internal static double Cos(double value, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Cos((float)value) : Math.Cos(value);

    internal static double Atan2(double y, double x, bool useLegacyPrecision)
        => useLegacyPrecision ? MathF.Atan2((float)y, (float)x) : Math.Atan2(y, x);

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
            ? MathF.FusedMultiplyAdd((float)scale, (float)delta, (float)value)
            : value + (scale * delta);

    // Use the contracted helpers only for legacy REAL blocks that have been
    // traced and shown to behave like fused multiply-add in the native build.
    // Plain Fortran product/add source trees should use ProductThenAdd or
    // ProductThenSubtract so the intermediate product rounds separately.
    internal static double MultiplyAdd(double left, double right, double addend, bool useLegacyPrecision)
        => useLegacyPrecision
            ? MathF.FusedMultiplyAdd((float)left, (float)right, (float)addend)
            : (left * right) + addend;

    internal static double MultiplySubtract(double left, double right, double minuend, bool useLegacyPrecision)
        => useLegacyPrecision
            ? MathF.FusedMultiplyAdd(-(float)left, (float)right, (float)minuend)
            : minuend - (left * right);

    internal static double ProductThenAdd(double left, double right, double addend, bool useLegacyPrecision)
    {
        if (!useLegacyPrecision)
        {
            return (left * right) + addend;
        }

        float product = (float)left * (float)right;
        return product + (float)addend;
    }

    internal static double ProductThenSubtract(double left, double right, double subtrahend, bool useLegacyPrecision)
    {
        if (!useLegacyPrecision)
        {
            return (left * right) - subtrahend;
        }

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
            ? MathF.FusedMultiplyAdd(
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
            ? MathF.FusedMultiplyAdd(
                (float)left3,
                (float)right3,
                MathF.FusedMultiplyAdd(
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
            ? MathF.FusedMultiplyAdd(
                (float)left4,
                (float)right4,
                MathF.FusedMultiplyAdd(
                    (float)left3,
                    (float)right3,
                    MathF.FusedMultiplyAdd(
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
            ? MathF.FusedMultiplyAdd(
                (float)left6,
                (float)right6,
                MathF.FusedMultiplyAdd(
                    (float)left5,
                    (float)right5,
                    MathF.FusedMultiplyAdd(
                        (float)left4,
                        (float)right4,
                        MathF.FusedMultiplyAdd(
                            (float)left3,
                            (float)right3,
                            MathF.FusedMultiplyAdd(
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
        if (!useLegacyPrecision)
        {
            return (left1 * right1) + (left2 * right2);
        }

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
        if (!useLegacyPrecision)
        {
            return (left1 * right1) + (left2 * right2) + (left3 * right3);
        }

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
        if (!useLegacyPrecision)
        {
            return (left1 * right1) + (left2 * right2) + (left3 * right3) + addend;
        }

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
        if (!useLegacyPrecision)
        {
            return (left1 * right1) + (left2 * right2);
        }

        float addend = (float)((float)left2 * (float)right2);
        return MathF.FusedMultiplyAdd((float)left1, (float)right1, addend);
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
        if (!useLegacyPrecision)
        {
            return (left1 * right1) + (left2 * right2) + (left3 * right3) + addend;
        }

        float sum = MathF.FusedMultiplyAdd((float)left1, (float)right1, (float)addend);
        sum = MathF.FusedMultiplyAdd((float)left2, (float)right2, sum);
        sum = MathF.FusedMultiplyAdd((float)left3, (float)right3, sum);
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
            float result = MathF.FusedMultiplyAdd(
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
            float result = MathF.FusedMultiplyAdd(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                float.CreateChecked(left2) * float.CreateChecked(right2));
            return T.CreateChecked(result);
        }

        return (left1 * right1) + (left2 * right2);
    }

    internal static T SumOfProducts<T>(T left1, T right1, T left2, T right2, T left3, T right3)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float productChain = MathF.FusedMultiplyAdd(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                float.CreateChecked(left2) * float.CreateChecked(right2));
            float result = MathF.FusedMultiplyAdd(
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
            float productChain = MathF.FusedMultiplyAdd(
                float.CreateChecked(left1),
                float.CreateChecked(right1),
                float.CreateChecked(left2) * float.CreateChecked(right2));
            float productSum = MathF.FusedMultiplyAdd(
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
            float result = MathF.FusedMultiplyAdd(
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
            float result = MathF.FusedMultiplyAdd(
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
            float result = MathF.FusedMultiplyAdd(
                -float.CreateChecked(left),
                float.CreateChecked(right),
                float.CreateChecked(minuend));
            return T.CreateChecked(result);
        }

        return minuend - (left * right);
    }

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

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV/GAUSS product-sum updates written as separate REAL products and left-associated additions.
    // Difference from legacy: The helper makes the non-fused multi-product recurrence explicit so solver ports do not accidentally reuse contracted SumOfProducts helpers.
    // Decision: Keep this helper because repeated solver parity fixes should centralize the plain REAL accumulation shape.
    internal static T SeparateSumOfProducts<T>(T left1, T right1, T left2, T right2)
        where T : struct, IFloatingPointIeee754<T>
    {
        T product1 = left1 * right1;
        T product2 = left2 * right2;
        return product1 + product2;
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: BLSOLV three-term elimination sums.
    // Difference from legacy: The helper preserves the source-order "p1 + p2 + p3" REAL accumulation instead of contracting the products through FMA-style helpers.
    // Decision: Keep this helper because it captures the legacy solver arithmetic family directly.
    internal static T SeparateSumOfProducts<T>(
        T left1,
        T right1,
        T left2,
        T right2,
        T left3,
        T right3)
        where T : struct, IFloatingPointIeee754<T>
    {
        T product1 = left1 * right1;
        T product2 = left2 * right2;
        T product3 = left3 * right3;
        return (product1 + product2) + product3;
    }
}

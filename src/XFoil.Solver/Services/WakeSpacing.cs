using System.Numerics;
using XFoil.Solver.Diagnostics;
using XFoil.Core.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xutils.f :: SETEXP
// Secondary legacy source: f_xfoil/src/xpanel.f :: XYWAKE call site for wake-node spacing
// Role in port: Replays the geometric wake-spacing law used to place wake stations downstream of the trailing edge.
// Differences: The core ratio solve follows SETEXP, but the managed version exposes typed overloads and returns a ready-to-use array rather than filling an existing Fortran workspace in place.
// Decision: Keep the typed managed wrapper and preserve the SETEXP ratio solve because it is the right parity reference for wake spacing.
namespace XFoil.Solver.Services;

internal static class WakeSpacing
{
    // Legacy mapping: f_xfoil/src/xutils.f :: SETEXP convenience overload.
    // Difference from legacy: The managed double overload is a thin wrapper around the generic core instead of a standalone REAL routine.
    // Decision: Keep the overload because it makes the default wake builder simpler to call.
    public static double[] BuildStretchedDistances(double firstSpacing, double maxDistance, int pointCount)
        => BuildStretchedDistancesCore(firstSpacing, maxDistance, pointCount);

    // Legacy mapping: f_xfoil/src/xutils.f :: SETEXP typed wrapper.
    // Difference from legacy: The original implementation is fixed to REAL arrays, while the managed port can execute the same algorithm for either double or parity float types.
    // Decision: Keep the generic wrapper because it is the cleanest way to support both runtime and parity paths.
    public static T[] BuildStretchedDistances<T>(T firstSpacing, T maxDistance, int pointCount)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float[] legacy = BuildStretchedDistancesLegacyFloat(
                float.CreateChecked(firstSpacing),
                float.CreateChecked(maxDistance),
                pointCount);
            var typed = new T[legacy.Length];
            for (int i = 0; i < legacy.Length; i++)
            {
                typed[i] = T.CreateChecked(legacy[i]);
            }

            return typed;
        }

        return BuildStretchedDistancesCore(firstSpacing, maxDistance, pointCount);
    }

    // Legacy mapping: f_xfoil/src/xutils.f :: SETEXP array construction.
    // Difference from legacy: The array fill is the same geometric march, but the managed code allocates and returns the distances directly.
    // Decision: Keep the return-array shape because it fits managed call sites naturally while preserving the spacing law.
    private static T[] BuildStretchedDistancesCore<T>(T firstSpacing, T maxDistance, int pointCount)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (pointCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), "Wake point count must be at least 2.");
        }

        var result = new T[pointCount];
        result[0] = T.Zero;
        if (pointCount == 2)
        {
            result[1] = maxDistance;
            return result;
        }

        int segmentCount = pointCount - 1;
        T ratio = SolveGeometricRatio(firstSpacing, maxDistance, segmentCount);
        T step = firstSpacing;
        for (int index = 1; index < pointCount; index++)
        {
            result[index] = result[index - 1] + step;
            step *= ratio;
        }

        return result;
    }

    private static float[] BuildStretchedDistancesLegacyFloat(float firstSpacing, float maxDistance, int pointCount)
    {
        if (pointCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), "Wake point count must be at least 2.");
        }

        var result = new float[pointCount];
        result[0] = 0.0f;
        if (pointCount == 2)
        {
            result[1] = maxDistance;
            return result;
        }

        int segmentCount = pointCount - 1;
        float ratio = SolveGeometricRatioLegacyFloat(firstSpacing, maxDistance, segmentCount);
        
        float step = firstSpacing;
        for (int index = 1; index < pointCount; index++)
        {
            result[index] = result[index - 1] + step;
            
            step *= ratio;
        }

        return result;
    }

    // Legacy mapping: f_xfoil/src/xutils.f :: SETEXP.
    // Difference from legacy: The quadratic estimate and Newton iteration follow SETEXP closely, but the managed port names each intermediate term and lets generic numeric types drive the arithmetic.
    // Decision: Keep the clearer decomposition and preserve the SETEXP solve as the canonical wake-spacing algorithm.
    private static T SolveGeometricRatio<T>(T firstSpacing, T maxDistance, int segmentCount)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (segmentCount <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount), "Wake segment count must be at least 2.");
        }

        T one = T.One;
        T two = T.CreateChecked(2.0);
        T four = T.CreateChecked(4.0);
        T six = T.CreateChecked(6.0);
        T tolerance = T.CreateChecked(1.0e-5);

        T sigma = maxDistance / firstSpacing;
        T rnex = T.CreateChecked(segmentCount);
        T rni = one / rnex;

        // Direct port of XFoil SETEXP:
        // initial quadratic estimate, followed by Newton iteration on
        // SIGMAN^(1/NEX) - SIGMA^(1/NEX).
        T aaa = rnex * (rnex - one) * (rnex - two) / six;
        T bbb = rnex * (rnex - one) / two;
        T ccc = rnex - sigma;
        T disc = T.Max(T.Zero, (bbb * bbb) - (four * aaa * ccc));

        T ratio;
        if (segmentCount == 2)
        {
            ratio = (-ccc / bbb) + one;
        }
        else
        {
            ratio = ((-bbb + T.Sqrt(disc)) / (two * aaa)) + one;
        }

        if (ratio == one)
        {
            return one;
        }

        for (int iteration = 0; iteration < 100; iteration++)
        {
            T ratioPowN = T.Pow(ratio, rnex);
            T sigmaN = (ratioPowN - one) / (ratio - one);
            T sigmaNRoot = T.Pow(sigmaN, rni);
            T residual = sigmaNRoot - T.Pow(sigma, rni);
            T derivative =
                rni *
                sigmaNRoot *
                ((rnex * T.Pow(ratio, T.CreateChecked(segmentCount - 1))) - sigmaN) /
                (ratioPowN - one);

            T delta = -residual / derivative;
            ratio += delta;
            if (T.Abs(delta) < tolerance)
            {
                return ratio;
            }
        }

        return ratio;
    }

    private static float SolveGeometricRatioLegacyFloat(float firstSpacing, float maxDistance, int segmentCount)
    {
        if (segmentCount <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount), "Wake segment count must be at least 2.");
        }

        const float One = 1.0f;
        const float Two = 2.0f;
        const float Four = 4.0f;
        const float Six = 6.0f;
        const float Tolerance = 1.0e-5f;

        float sigma = maxDistance / firstSpacing;
        float rnex = segmentCount;
        float rni = One / rnex;

        float aaa = rnex * (rnex - One) * (rnex - Two) / Six;
        float bbb = rnex * (rnex - One) / Two;
        float ccc = rnex - sigma;
        float disc = MathF.Max(0.0f, (bbb * bbb) - (Four * aaa * ccc));

        float ratio = segmentCount == 2
            ? (-ccc / bbb) + One
            : ((-bbb + LegacyLibm.Sqrt(disc)) / (Two * aaa)) + One;

        if (ratio == One)
        {
            return One;
        }

        float sigmaRoot = LegacyLibm.Pow(sigma, rni);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            float ratioPowN = PowIntegerLegacyFloat(ratio, segmentCount);
            float sigmaN = (ratioPowN - One) / (ratio - One);
            float sigmaNRoot = LegacyLibm.Pow(sigmaN, rni);
            float residual = sigmaNRoot - sigmaRoot;
            float ratioPowNm1 = PowIntegerLegacyFloat(ratio, segmentCount - 1);
            float derivative = rni * sigmaNRoot * ((rnex * ratioPowNm1) - sigmaN) / (ratioPowN - One);

            float delta = -residual / derivative;
            ratio += delta;
            if (MathF.Abs(delta) < Tolerance)
            {
                return ratio;
            }
        }

        return ratio;
    }

    // Mirrors libgfortran's pow_r4_i4 (binary exponentiation with squaring)
    // so RATIO**NEX in SETEXP reproduces the classic Fortran bit pattern.
    private static float PowIntegerLegacyFloat(float value, int exponent)
    {
        if (exponent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), "Exponent must be non-negative.");
        }

        float result = 1.0f;
        float basePow = value;
        int n = exponent;
        while (n != 0)
        {
            if ((n & 1) != 0)
            {
                result *= basePow;
            }
            n >>= 1;
            if (n != 0)
            {
                basePow *= basePow;
            }
        }

        return result;
    }
}

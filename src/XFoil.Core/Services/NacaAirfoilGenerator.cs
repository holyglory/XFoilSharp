using System.Globalization;
using System.Numerics;
using XFoil.Core.Numerics;
using XFoil.Core.Models;
// Legacy audit:
// Primary legacy source: f_xfoil/src/naca.f :: NACA4
// Secondary legacy source: f_xfoil/src/xfoil.f :: NACA command wrapper
// Role in port: Generates managed NACA airfoil geometry, including a classic XFoil-compatible replay mode for parity work.
// Differences: The managed port exposes explicit classic versus improved geometry modes, uses generic floating-point helpers and trace hooks, and isolates the legacy single-precision staging instead of relying on one monolithic REAL routine.
// Decision: Keep both paths: the clearer managed generator for default use and the classic legacy-replay path for parity-sensitive comparisons.
namespace XFoil.Core.Services;

public sealed class NacaAirfoilGenerator
{
    private const double TrailingEdgeBunchingExponent = 1.5;
    private const double FiniteTrailingEdgeThicknessCoefficient = -0.10150;

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4 entry path.
    // Difference from legacy: The default managed entry deliberately uses the improved normal-offset geometry rather than the classic ordinate-only construction.
    // Decision: Keep the improved default because it produces the better modern geometry while the classic path remains available separately.
    public AirfoilGeometry Generate4Digit(string designation, int pointCount = 161)
        => Generate4DigitCore<double>(designation, pointCount, useClassicXFoilGeometry: false);

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4.
    // Difference from legacy: This entry point exposes the classic XFoil-compatible construction and optional legacy single-precision replay explicitly to the caller.
    // Decision: Keep this dedicated classic entry because it is the right parity reference surface for geometry debugging.
    public AirfoilGeometry Generate4DigitClassic(
        string designation,
        int pointCount = 161,
        bool useLegacyPrecision = true)
        => useLegacyPrecision
            ? Generate4DigitCore<float>(designation, pointCount, useClassicXFoilGeometry: true)
            : Generate4DigitCore<double>(designation, pointCount, useClassicXFoilGeometry: true);

    private static AirfoilGeometry Generate4DigitCore<T>(
        string designation,
        int pointCount,
        bool useClassicXFoilGeometry)
        where T : struct, IFloatingPointIeee754<T>
    {
        string precision = typeof(T) == typeof(float) ? "Single" : "Double";

        if (string.IsNullOrWhiteSpace(designation))
        {
            throw new ArgumentException("A NACA designation is required.", nameof(designation));
        }

        if (designation.Length != 4 || !designation.All(char.IsDigit))
        {
            throw new ArgumentException("Only NACA 4-digit airfoils are supported in the initial managed port.", nameof(designation));
        }

        if (pointCount < 21 || pointCount % 2 == 0)
        {
            throw new ArgumentException("Point count must be an odd number greater than or equal to 21.", nameof(pointCount));
        }

        T maxCamber = T.CreateChecked(designation[0] - '0') / T.CreateChecked(100.0);
        T maxCamberPosition = T.CreateChecked(designation[1] - '0') / T.CreateChecked(10.0);
        T thickness = T.CreateChecked(int.Parse(designation[2..], CultureInfo.InvariantCulture)) / T.CreateChecked(100.0);
        var surfacePointCount = (pointCount + 1) / 2;

        var upper = new List<AirfoilPoint>(surfacePointCount);
        var lower = new List<AirfoilPoint>(surfacePointCount);

        T exponent = T.CreateChecked(TrailingEdgeBunchingExponent);
        T exponentPlusOne = exponent + T.One;
        T pointTwo = T.CreateChecked(0.20);
        T two = T.CreateChecked(2.0);
        T oneMinusP = T.One - maxCamberPosition;
        T oneMinusPSquared = oneMinusP * oneMinusP;


        // Legacy block: NACA4 surface-node generation loop.
        // Difference: The classic branch preserves the legacy bunching, thickness, and camber formulas, while the default managed branch replaces the final ordinate construction with a normal-offset formulation and adds explicit trace state.
        // Decision: Keep both within one audited core so the managed improvement and the parity replay stay aligned.
        for (var index = 0; index < surfacePointCount; index++)
        {
            T fraction = T.CreateChecked(index) / T.CreateChecked(surfacePointCount - 1);
            T oneMinusFraction = T.One - fraction;
            T sqrtOneMinusFraction = T.Zero;
            T oneMinusFractionPowAn = T.Zero;
            T oneMinusFractionPowAnp = T.Zero;
            T xLeadingTerm = T.Zero;
            T xTrailingTerm = T.Zero;
            T x;
            if (index == surfacePointCount - 1)
            {
                x = T.One;
            }
            else
            {
                (sqrtOneMinusFraction, oneMinusFractionPowAn, oneMinusFractionPowAnp, xLeadingTerm, xTrailingTerm, x) =
                    ComputeTrailingEdgeBunchedCoordinate(fraction, oneMinusFraction, exponent, exponentPlusOne);
            }

            (T x2, T x3, T x4) = ComputeThicknessPowers(x);
            T yt = ((T.CreateChecked(0.29690) * T.Sqrt(x))
                    - (T.CreateChecked(0.12600) * x)
                    - (T.CreateChecked(0.35160) * x2)
                    + (T.CreateChecked(0.28430) * x3)
                    + (T.CreateChecked(FiniteTrailingEdgeThicknessCoefficient) * x4)) * thickness / pointTwo;

            T yc;
            T dycDx;
            if (useClassicXFoilGeometry)
            {
                if (x < maxCamberPosition)
                {
                    T pSquared = maxCamberPosition * maxCamberPosition;
                    yc = maxCamber / pSquared * ((two * maxCamberPosition * x) - x2);
                    dycDx = two * maxCamber / pSquared * (maxCamberPosition - x);
                }
                else
                {
                    yc = maxCamber / oneMinusPSquared * ((T.One - (two * maxCamberPosition)) + (two * maxCamberPosition * x) - x2);
                    dycDx = two * maxCamber / oneMinusPSquared * (maxCamberPosition - x);
                }
            }
            else
            {
                (yc, dycDx) = MeanCamber(maxCamber, maxCamberPosition, x);
            }

            if (useClassicXFoilGeometry)
            {
                upper.Add(new AirfoilPoint(
                    double.CreateChecked(x),
                    double.CreateChecked(yc + yt)));

                lower.Add(new AirfoilPoint(
                    double.CreateChecked(x),
                    double.CreateChecked(yc - yt)));
            }
            else
            {
                T theta = T.Atan(dycDx);
                upper.Add(new AirfoilPoint(
                    double.CreateChecked(x - (yt * T.Sin(theta))),
                    double.CreateChecked(yc + (yt * T.Cos(theta)))));

                lower.Add(new AirfoilPoint(
                    double.CreateChecked(x + (yt * T.Sin(theta))),
                    double.CreateChecked(yc - (yt * T.Cos(theta)))));
            }

        }

        // Legacy block: NACA4 upper/lower surface assembly into the final airfoil contour.
        // Difference: The managed code uses LINQ to compose the final contour instead of the legacy indexed `XB/YB` fill loops, but the resulting ordering is the same.
        // Decision: Keep the managed assembly because it is clearer and not itself a parity-risk boundary.
        var points = upper
            .AsEnumerable()
            .Reverse()
            .Concat(lower.Skip(1))
            .ToArray();

        return new AirfoilGeometry($"NACA {designation}", points, AirfoilFormat.PlainCoordinates);
    }

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4 trailing-edge bunching coordinate law.
    // Difference from legacy: The method factors the law into a reusable helper and adds an explicit float replay branch so parity mode preserves `powf`-style staging and multiply order.
    // Decision: Keep the helper split because it isolates the real parity-sensitive part of the coordinate generation cleanly.
    private static (T SqrtOneMinusFraction, T PowerAn, T PowerAnp, T LeadingTerm, T TrailingTerm, T X)
        ComputeTrailingEdgeBunchedCoordinate<T>(T fraction, T oneMinusFraction, T exponent, T exponentPlusOne)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float fractionSingle = float.CreateChecked(fraction);
            float oneMinusFractionSingle = float.CreateChecked(oneMinusFraction);
            float exponentSingle = float.CreateChecked(exponent);
            float exponentPlusOneSingle = float.CreateChecked(exponentPlusOne);

            // Classic XFoil evaluates the bunching law in single precision via
            // powf-like exponentiation. The algebraically equivalent sqrt-based
            // rewrite drifts by one or more ULPs, which becomes the first raw
            // geometry mismatch in parity mode.
            float sqrtOneMinusFractionSingle = MathF.Sqrt(oneMinusFractionSingle);
            float powerAnSingle = LegacyLibm.Pow(oneMinusFractionSingle, exponentSingle);
            float powerAnpSingle = LegacyLibm.Pow(oneMinusFractionSingle, exponentPlusOneSingle);

            // Keep the original multiplication order as well. Classic XFoil's
            // left-associated multiply lands on a different ULP than
            // exponentPlusOne * (fraction * powerAn).
            float leadingTermSingle = (exponentPlusOneSingle * fractionSingle) * powerAnSingle;
            float xSingle = (1.0f - leadingTermSingle) - powerAnpSingle;

            return (
                T.CreateChecked(sqrtOneMinusFractionSingle),
                T.CreateChecked(powerAnSingle),
                T.CreateChecked(powerAnpSingle),
                T.CreateChecked(leadingTermSingle),
                T.CreateChecked(powerAnpSingle),
                T.CreateChecked(xSingle));
        }

        T sqrtOneMinusFraction = T.Sqrt(oneMinusFraction);
        T powerAn = oneMinusFraction * sqrtOneMinusFraction;
        T powerAnp = oneMinusFraction * powerAn;
        T leadingTerm = exponentPlusOne * fraction * powerAn;
        T x = T.One - leadingTerm - powerAnp;
        return (sqrtOneMinusFraction, powerAn, powerAnp, leadingTerm, powerAnp, x);
    }

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4 thickness-polynomial powers.
    // Difference from legacy: The helper makes the repeated powers explicit and preserves the single-precision `x^4` staging needed by the legacy replay path.
    // Decision: Keep the helper because it documents and localizes the parity-sensitive power chain.
    private static (T X2, T X3, T X4) ComputeThicknessPowers<T>(T x)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            float xSingle = float.CreateChecked(x);
            float x2Single = xSingle * xSingle;
            float x3Single = x2Single * xSingle;

            // The classic single-precision path behaves like a squared x^2 term
            // for x^4. Using x^3 * x shifts the TE-adjacent thickness by one ULP.
            float x4Single = x2Single * x2Single;
            return (T.CreateChecked(x2Single), T.CreateChecked(x3Single), T.CreateChecked(x4Single));
        }

        T x2 = x * x;
        T x3 = x2 * x;
        T x4 = x3 * x;
        return (x2, x3, x4);
    }

    // Legacy mapping: f_xfoil/src/naca.f :: NACA4 camber-line formula.
    // Difference from legacy: The default managed path factors the fore/aft camber equations into a reusable helper so the improved normal-offset geometry can share them.
    // Decision: Keep the helper because it improves structure without changing the classic camber equations it evaluates.
    private static (T Camber, T Slope) MeanCamber<T>(T m, T p, T x)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (m <= T.Zero || p <= T.Zero)
        {
            return (T.Zero, T.Zero);
        }

        if (x < p)
        {
            T camber = (m / (p * p)) * ((T.CreateChecked(2.0) * p * x) - (x * x));
            T slope = (T.CreateChecked(2.0) * m / (p * p)) * (p - x);
            return (camber, slope);
        }

        T oneMinusP = T.One - p;
        T denominator = oneMinusP * oneMinusP;
        T aftCamber = (m / denominator) * ((T.One - (T.CreateChecked(2.0) * p)) + (T.CreateChecked(2.0) * p * x) - (x * x));
        T aftSlope = (T.CreateChecked(2.0) * m / denominator) * (p - x);
        return (aftCamber, aftSlope);
    }
}

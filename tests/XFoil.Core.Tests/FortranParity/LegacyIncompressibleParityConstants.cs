using XFoil.Solver.Numerics;

namespace XFoil.Core.Tests.FortranParity;

internal static class LegacyIncompressibleParityConstants
{
    internal const double Tkbl = 0.0;
    internal const double Qinfbl = 1.0;
    internal const double TkblMs = 0.25;
    internal const double Hstinv = 0.0;
    internal const double Rstbl = 1.0;
    internal const double RstblMs = 0.5;
    internal const double Hvrat = 0.0;
    internal const double ReyblRe = 1.0;

    internal static double Gm1Bl => LegacyPrecisionMath.GammaMinusOne(true);

    internal static double HstinvMs => (float)Gm1Bl;

    internal static double Reybl => (double)(float)1_000_000.0f;

    internal static double ReyblMs
    {
        get
        {
            float qinfblf = 1.0f;
            float hvratf = 0.0f;
            float heratf = 1.0f;
            float hstinvMsf = (float)HstinvMs;
            float heratMsf = -0.5f * qinfblf * qinfblf * hstinvMsf;
            float reyblf = (float)Reybl;
            return reyblf * ((1.5f / heratf) - (1.0f / (heratf + hvratf))) * heratMsf;
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using XFoil.Solver.Numerics;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSWLIN half-panel downstream derivative chain
// Secondary legacy source: tools/fortran-debug/pswlin_half_parity_driver.f90 standalone half-panel oracle
// Role in port: Proves the managed downstream PSWLIN half-panel replay against a tiny Fortran micro-driver so
// `pdx*` / `pdni` / `dqJo*` mismatches can be debugged without perturbing the full solver trace.
// Decision: Keep the micro-driver because heavy in-solver trace hooks were shown to perturb the wake-source path.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class PswlinHalfFormulaParityTests
{
    [Fact]
    public void Alpha10_P80_Row43_Half1_DownstreamReplay_MatchesFortranMicroDriver()
    {
        FortranPswlinHalfCase @case = BuildAlpha10P80Row43Half1Case();
        FortranPswlinHalfResult fortran = FortranPswlinHalfDriver.RunCase(@case);
        FortranPswlinHalfResult managed = RunManagedReplay(@case);

        Assert.Equal(fortran.X0, managed.X0);
        Assert.Equal(fortran.Psum, managed.Psum);
        Assert.Equal(fortran.Pdif, managed.Pdif);
        Assert.Equal(fortran.Psx0, managed.Psx0);
        Assert.Equal(fortran.Psx1, managed.Psx1);
        Assert.Equal(fortran.Psyy, managed.Psyy);
        Assert.Equal(fortran.Pdx0Term1, managed.Pdx0Term1);
        Assert.Equal(fortran.Pdx0Term2, managed.Pdx0Term2);
        Assert.Equal(fortran.Pdx0Term3, managed.Pdx0Term3);
        Assert.Equal(fortran.Pdx0Accum1, managed.Pdx0Accum1);
        Assert.Equal(fortran.Pdx0Accum2, managed.Pdx0Accum2);
        Assert.Equal(fortran.Pdx0Numerator, managed.Pdx0Numerator);
        Assert.Equal(fortran.Pdx0Split, managed.Pdx0Split);
        Assert.Equal(fortran.Pdx0Direct, managed.Pdx0Direct);
        Assert.Equal(fortran.Pdx1Term1, managed.Pdx1Term1);
        Assert.Equal(fortran.Pdx1Term2, managed.Pdx1Term2);
        Assert.Equal(fortran.Pdx1Term3, managed.Pdx1Term3);
        Assert.Equal(fortran.Pdx1Accum1, managed.Pdx1Accum1);
        Assert.Equal(fortran.Pdx1Accum2, managed.Pdx1Accum2);
        Assert.Equal(fortran.Pdx1Numerator, managed.Pdx1Numerator);
        Assert.Equal(fortran.Pdx1Split, managed.Pdx1Split);
        Assert.Equal(fortran.Pdx1Direct, managed.Pdx1Direct);
        Assert.Equal(fortran.Pdyy, managed.Pdyy);
        Assert.Equal(fortran.Psni, managed.Psni);
        Assert.Equal(fortran.Pdni, managed.Pdni);
        Assert.Equal(fortran.DqJoLeft, managed.DqJoLeft);
        Assert.Equal(fortran.DqJoRight, managed.DqJoRight);
        Assert.Equal(fortran.DqJoInner, managed.DqJoInner);
        Assert.Equal(fortran.DqJo, managed.DqJo);
    }

    [Fact]
    public void Alpha10_P80_Row43_Half2_Pdx2Replay_MatchesReferenceTrace()
    {
        using JsonDocument document = JsonDocument.Parse($"[{string.Join(",", File.ReadLines(GetLatestRow43TracePath()))}]");
        JsonElement.ArrayEnumerator records = document.RootElement.EnumerateArray();

        JsonElement half = default;
        JsonElement segment = default;
        foreach (JsonElement record in records)
        {
            string kind = record.GetProperty("kind").GetString()!;
            JsonElement data = record.GetProperty("data");
            if (!data.TryGetProperty("wakeSegment", out JsonElement wakeSegment) ||
                !data.TryGetProperty("half", out JsonElement halfElement) ||
                wakeSegment.GetInt32() != 1 ||
                halfElement.GetInt32() != 2)
            {
                continue;
            }

            switch (kind)
            {
                case "pswlin_half_terms":
                    half = data;
                    break;
                case "pswlin_segment":
                    segment = data;
                    break;
            }
        }

        float x0 = ReadSingle(half, "x0");
        float x2 = ReadSingle(segment, "x2");
        float psx2 = ReadSingle(segment, "psx2");
        float psum = ReadSingle(half, "psum");
        float pdif = ReadSingle(half, "pdif");
        float dxInv = ReadSingle(segment, "dxInv");

        float pdx2 = (((x0 + x2) * psx2) + psum - (2f * x2 * psx2) + pdif) * dxInv;

        Assert.Equal(ToHex(ReadSingle(segment, "pdx2")), ToHex(pdx2));
    }

    private static FortranPswlinHalfCase BuildAlpha10P80Row43Half1Case()
    {
        string path = GetLatestRow43TracePath();

        using JsonDocument document = JsonDocument.Parse($"[{string.Join(",", File.ReadLines(path))}]");
        JsonElement.ArrayEnumerator records = document.RootElement.EnumerateArray();

        JsonElement segment = default;
        JsonElement half = default;
        JsonElement recurrence = default;
        foreach (JsonElement record in records)
        {
            string kind = record.GetProperty("kind").GetString()!;
            JsonElement data = record.GetProperty("data");
            if (!data.TryGetProperty("wakeSegment", out JsonElement wakeSegment) ||
                !data.TryGetProperty("half", out JsonElement halfElement) ||
                wakeSegment.GetInt32() != 1 ||
                halfElement.GetInt32() != 1)
            {
                continue;
            }

            switch (kind)
            {
                case "pswlin_segment":
                    segment = data;
                    break;
                case "pswlin_half_terms":
                    half = data;
                    break;
                case "pswlin_recurrence":
                    recurrence = data;
                    break;
            }
        }

        return new FortranPswlinHalfCase(
            X1: ReadSingle(segment, "x1"),
            X2: ReadSingle(segment, "x2"),
            Yy: ReadSingle(segment, "yy"),
            X1I: ReadSingle(segment, "x1i"),
            X2I: ReadSingle(segment, "x2i"),
            YyI: ReadSingle(segment, "yyi"),
            X0: ReadSingle(half, "x0"),
            Psum: ReadSingle(half, "psum"),
            Pdif: ReadSingle(half, "pdif"),
            Psx0: ReadSingle(segment, "psx0"),
            Psx1: ReadSingle(segment, "psx1"),
            Psyy: ReadSingle(segment, "psyy"),
            Dsio: ReadSingle(segment, "dsio"),
            Dsim: ReadSingle(segment, "dsim"),
            DxInv: ReadSingle(segment, "dxInv"),
            Qopi: ReadSingle(recurrence, "qopi"));
    }

    private static FortranPswlinHalfResult RunManagedReplay(FortranPswlinHalfCase @case)
    {
        const float half = 0.5f;
        const float two = 2.0f;

        float pdx0Term1 = (@case.X1 + @case.X0) * @case.Psx0;
        float pdx0Term2 = @case.Psum;
        float pdx0Term3 = -two * @case.X0 * @case.Psx0;
        float pdx0Accum1 = pdx0Term1 + pdx0Term2;
        float pdx0Accum2 = pdx0Accum1 + pdx0Term3;
        float pdx0Numerator = pdx0Accum2 + @case.Pdif;
        float pdx0Split = pdx0Numerator * @case.DxInv;
        float pdx0Direct = ((@case.X1 + @case.X0) * @case.Psx0 + @case.Psum + pdx0Term3 + @case.Pdif) * @case.DxInv;

        float pdx1Term1 = (@case.X1 + @case.X0) * @case.Psx1;
        float pdx1Term2 = @case.Psum;
        float pdx1Term3 = -two * @case.X1 * @case.Psx1;
        float pdx1Accum1 = pdx1Term1 + pdx1Term2;
        float pdx1Accum2 = pdx1Accum1 + pdx1Term3;
        float pdx1Numerator = pdx1Accum2 - @case.Pdif;
        float pdx1Split = pdx1Numerator * @case.DxInv;
        float pdx1Direct = ((@case.X1 + @case.X0) * @case.Psx1 + @case.Psum + pdx1Term3 - @case.Pdif) * @case.DxInv;

        float pdyy = LegacyPrecisionMath.FusedMultiplyAdd(
            @case.X1 + @case.X0,
            @case.Psyy,
            two * (@case.X0 - @case.X1 - (@case.Yy * (@case.Psx1 + @case.Psx0)))) * @case.DxInv;

        float psni = LegacyPrecisionMath.SumOfProducts(
            @case.Psx1,
            @case.X1I,
            @case.Psx0,
            (@case.X1I + @case.X2I) * half,
            @case.Psyy,
            @case.YyI);
        float pdni = LegacyPrecisionMath.SumOfProducts(
            pdx1Direct,
            @case.X1I,
            pdx0Direct,
            (@case.X1I + @case.X2I) * half,
            pdyy,
            @case.YyI);
        float dqJoLeft = -psni * @case.Dsio;
        float dqJoRight = pdni * @case.Dsio;
        float dqJoInner = dqJoLeft - dqJoRight;
        float dqJo = @case.Qopi * dqJoInner;

        return new FortranPswlinHalfResult(
            ToHex(@case.X0),
            ToHex(@case.Psum),
            ToHex(@case.Pdif),
            ToHex(@case.Psx0),
            ToHex(@case.Psx1),
            ToHex(@case.Psyy),
            ToHex(pdx0Term1),
            ToHex(pdx0Term2),
            ToHex(pdx0Term3),
            ToHex(pdx0Accum1),
            ToHex(pdx0Accum2),
            ToHex(pdx0Numerator),
            ToHex(pdx0Split),
            ToHex(pdx0Direct),
            ToHex(pdx1Term1),
            ToHex(pdx1Term2),
            ToHex(pdx1Term3),
            ToHex(pdx1Accum1),
            ToHex(pdx1Accum2),
            ToHex(pdx1Numerator),
            ToHex(pdx1Split),
            ToHex(pdx1Direct),
            ToHex(pdyy),
            ToHex(psni),
            ToHex(pdni),
            ToHex(dqJoLeft),
            ToHex(dqJoRight),
            ToHex(dqJoInner),
            ToHex(dqJo));
    }

    private static float ReadSingle(JsonElement element, string name)
        => (float)element.GetProperty(name).GetDouble();

    private static string GetLatestRow43TracePath()
        => FortranParityArtifactLocator.GetLatestReferenceTracePath(
            Path.Combine(
                FortranReferenceCases.GetFortranDebugDirectory(),
                "reference",
                "alpha10_p80_pswlin_row43_ref"));

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);
}

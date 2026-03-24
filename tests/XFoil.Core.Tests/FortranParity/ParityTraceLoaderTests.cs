using System;
using System.Text.Json;
using Xunit;

namespace XFoil.Core.Tests.FortranParity;

public sealed class ParityTraceLoaderTests
{
    [Fact]
    public void DeserializeLine_ReplacesLoneLowSurrogateEscapesInsideStringFields()
    {
        const string jsonLine =
            "{\"sequence\":20646,\"runtime\":\"fortran\",\"kind\":\"psilin_source_segment\",\"scope\":\"PSILIN source_half1 te_correction \\udcff\\udcff PSWLIN\",\"name\":null,\"data\":{\"fieldIndex\":1},\"values\":null,\"tags\":null,\"timestampUtc\":null}";

        ParityTraceRecord record = Assert.IsType<ParityTraceRecord>(ParityTraceLoader.DeserializeLine(jsonLine));

        Assert.Equal("psilin_source_segment", record.Kind);
        Assert.Equal("fortran", record.Runtime);
        Assert.Equal(2, Count(record.Scope, '\uFFFD'));
        Assert.DoesNotContain("\\udcff", record.Scope, StringComparison.OrdinalIgnoreCase);
        Assert.True(record.TryGetDataField("fieldIndex", out JsonElement fieldIndex));
        Assert.Equal(1, fieldIndex.GetInt32());
    }

    [Fact]
    public void DeserializeLine_PreservesValidSurrogatePairs()
    {
        const string jsonLine =
            "{\"sequence\":1,\"runtime\":\"fortran\",\"kind\":\"trace_event\",\"scope\":\"valid pair \\ud83d\\ude00\",\"name\":null,\"data\":{},\"values\":null,\"tags\":null,\"timestampUtc\":null}";

        ParityTraceRecord record = Assert.IsType<ParityTraceRecord>(ParityTraceLoader.DeserializeLine(jsonLine));

        Assert.Equal("valid pair \U0001F600", record.Scope);
    }

    private static int Count(string value, char expected)
    {
        int count = 0;
        foreach (char character in value)
        {
            if (character == expected)
            {
                count++;
            }
        }

        return count;
    }
}

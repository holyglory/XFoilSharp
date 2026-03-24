using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/json_trace.f JSONL event schema
// Secondary legacy source: src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs
// Role in port: Managed-only streaming loader for the Fortran/C# parity trace artifacts.
// Differences: Classic XFoil did not consume its own trace output as typed records; this loader exists only to make the cross-runtime artifact comparison cheap and deterministic.
// Decision: Keep the managed-only streaming loader because the reference traces are too large for ad hoc test parsing.
namespace XFoil.Core.Tests.FortranParity;

public sealed class ParityTraceRecord
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("runtime")]
    public string Runtime { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    [JsonPropertyName("values")]
    public double[]? Values { get; init; }

    [JsonPropertyName("dataBits")]
    public Dictionary<string, Dictionary<string, string>>? DataBits { get; init; }

    [JsonPropertyName("valuesBits")]
    public List<Dictionary<string, string>>? ValuesBits { get; init; }

    [JsonPropertyName("tags")]
    public Dictionary<string, JsonElement>? Tags { get; init; }

    [JsonPropertyName("tagsBits")]
    public Dictionary<string, Dictionary<string, string>>? TagsBits { get; init; }

    [JsonPropertyName("timestampUtc")]
    public DateTime? TimestampUtc { get; init; }

    public bool TryGetDataField(string path, out JsonElement value)
    {
        return TryGetElement(Data, path, out value);
    }

    public bool TryGetTag(string tagName, out JsonElement value)
    {
        value = default;
        return Tags is not null && Tags.TryGetValue(tagName, out value);
    }

    public bool TryGetDataBits(string path, out IReadOnlyDictionary<string, string>? bits)
    {
        bits = null;
        if (DataBits is null || !DataBits.TryGetValue(path, out Dictionary<string, string>? value))
        {
            return false;
        }

        bits = value;
        return true;
    }

    public bool TryGetTagBits(string tagName, out IReadOnlyDictionary<string, string>? bits)
    {
        bits = null;
        if (TagsBits is null || !TagsBits.TryGetValue(tagName, out Dictionary<string, string>? value))
        {
            return false;
        }

        bits = value;
        return true;
    }

    public bool TryGetValueBits(int index, out IReadOnlyDictionary<string, string>? bits)
    {
        bits = null;
        if (ValuesBits is null || index < 0 || index >= ValuesBits.Count)
        {
            return false;
        }

        bits = ValuesBits[index];
        return true;
    }

    private static bool TryGetElement(JsonElement element, string path, out JsonElement value)
    {
        value = element;
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }
}

public static class ParityTraceLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private static readonly ConcurrentDictionary<string, CachedTraceFile> Cache = new(StringComparer.Ordinal);

    public static ParityTraceRecord FindSingle(string path, Func<ParityTraceRecord, bool> predicate, string description)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        ParityTraceRecord? match = null;
        foreach (ParityTraceRecord record in ReadMatching(path, predicate))
        {
            if (match is not null)
            {
                throw new InvalidOperationException($"Expected a single trace record for {description} in {path}, but found multiple matches.");
            }

            match = record;
        }

        return match ?? throw new InvalidOperationException($"Trace record not found for {description} in {path}.");
    }

    public static IReadOnlyList<ParityTraceRecord> ReadMatching(string path, Func<ParityTraceRecord, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matches = new List<ParityTraceRecord>();
        foreach (ParityTraceRecord record in ReadAll(path))
        {
            if (predicate(record))
            {
                matches.Add(record);
            }
        }

        return matches;
    }

    public static IReadOnlyList<ParityTraceRecord> ReadAll(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Trace file not found.", path);
        }

        FileInfo fileInfo = new(path);
        CachedTraceFile cached = Cache.AddOrUpdate(
            path,
            static (cachePath, state) => LoadTraceFile(cachePath, state.LastWriteTimeUtc, state.Length),
            static (cachePath, existing, state) =>
                existing.IsCurrent(state.LastWriteTimeUtc, state.Length)
                    ? existing
                    : LoadTraceFile(cachePath, state.LastWriteTimeUtc, state.Length),
            (fileInfo.LastWriteTimeUtc, fileInfo.Length));

        return cached.Records;
    }

    public static ParityTraceRecord? DeserializeLine(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ParityTraceRecord>(jsonLine, JsonOptions);
        }
        catch (JsonException) when (TrySanitizeMalformedUnicodeEscapes(jsonLine, out string sanitizedJsonLine))
        {
            return JsonSerializer.Deserialize<ParityTraceRecord>(sanitizedJsonLine, JsonOptions);
        }
    }

    private static CachedTraceFile LoadTraceFile(string path, DateTime lastWriteTimeUtc, long length)
    {
        var records = new List<ParityTraceRecord>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ParityTraceRecord? record = DeserializeLine(line);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return new CachedTraceFile(lastWriteTimeUtc, length, records);
    }

    private sealed record CachedTraceFile(DateTime LastWriteTimeUtc, long Length, IReadOnlyList<ParityTraceRecord> Records)
    {
        public bool IsCurrent(DateTime lastWriteTimeUtc, long length)
        {
            return LastWriteTimeUtc == lastWriteTimeUtc && Length == length;
        }
    }

    private static bool TrySanitizeMalformedUnicodeEscapes(string jsonLine, out string sanitizedJsonLine)
    {
        var builder = new StringBuilder(jsonLine.Length);
        bool changed = false;
        bool insideString = false;

        for (int index = 0; index < jsonLine.Length; index++)
        {
            char current = jsonLine[index];
            if (!insideString)
            {
                builder.Append(current);
                if (current == '"')
                {
                    insideString = true;
                }

                continue;
            }

            if (current == '"')
            {
                builder.Append(current);
                insideString = false;
                continue;
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (!TryReadUnicodeEscape(jsonLine, index, out int codeUnit))
            {
                builder.Append(current);
                if (index + 1 < jsonLine.Length)
                {
                    builder.Append(jsonLine[index + 1]);
                    index++;
                }

                continue;
            }

            if (IsHighSurrogate(codeUnit))
            {
                if (TryReadUnicodeEscape(jsonLine, index + 6, out int lowCodeUnit) && IsLowSurrogate(lowCodeUnit))
                {
                    builder.Append(jsonLine, index, 12);
                    index += 11;
                    continue;
                }

                builder.Append("\\uFFFD");
                changed = true;
                index += 5;
                continue;
            }

            if (IsLowSurrogate(codeUnit))
            {
                builder.Append("\\uFFFD");
                changed = true;
                index += 5;
                continue;
            }

            builder.Append(jsonLine, index, 6);
            index += 5;
        }

        sanitizedJsonLine = builder.ToString();
        return changed;
    }

    private static bool TryReadUnicodeEscape(string text, int slashIndex, out int codeUnit)
    {
        codeUnit = 0;
        if (slashIndex + 5 >= text.Length ||
            text[slashIndex] != '\\' ||
            text[slashIndex + 1] != 'u')
        {
            return false;
        }

        for (int offset = 2; offset < 6; offset++)
        {
            int hexValue = GetHexValue(text[slashIndex + offset]);
            if (hexValue < 0)
            {
                return false;
            }

            codeUnit = (codeUnit << 4) | hexValue;
        }

        return true;
    }

    private static bool IsHighSurrogate(int codeUnit)
    {
        return codeUnit is >= 0xD800 and <= 0xDBFF;
    }

    private static bool IsLowSurrogate(int codeUnit)
    {
        return codeUnit is >= 0xDC00 and <= 0xDFFF;
    }

    private static int GetHexValue(char value)
    {
        return value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'A' and <= 'F' => value - 'A' + 10,
            >= 'a' and <= 'f' => value - 'a' + 10,
            _ => -1
        };
    }
}

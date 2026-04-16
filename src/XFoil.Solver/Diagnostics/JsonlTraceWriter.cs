// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: plain-text WRITE diagnostics, tools/fortran-debug/json_trace.f :: parity trace instrumentation
// Role in port: Managed-only JSONL diagnostics and ambient trace routing for parity debugging.
// Differences: Classic XFoil emits plain-text records and ad hoc debug writes; this file centralizes structured trace emission, float-preserving JSON formatting, and multiplexed writer plumbing for the managed port.
// Decision: Keep the managed implementation because diagnostics, trace scoping, and JSON serialization are .NET-specific infrastructure around the legacy solver.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace XFoil.Solver.Diagnostics;

public sealed class TracePersistenceLimitExceededException : InvalidOperationException
{
    public TracePersistenceLimitExceededException(long limitBytes, long projectedBytes, string kind, string scope)
        : base(
            string.Format(
                CultureInfo.InvariantCulture,
                "Persisted JSON trace exceeded the configured byte limit before writing kind={0} scope={1}. Limit={2} bytes projected={3} bytes.",
                kind,
                scope,
                limitBytes,
                projectedBytes))
    {
        LimitBytes = limitBytes;
        ProjectedBytes = projectedBytes;
        Kind = kind;
        Scope = scope;
    }

    public long LimitBytes { get; }

    public long ProjectedBytes { get; }

    public string Kind { get; }

    public string Scope { get; }
}

/// <summary>
/// Streams structured diagnostic events as newline-delimited JSON.
/// </summary>
public sealed class JsonlTraceWriter : TextWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new PreciseSingleJsonConverter() }
    };

    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Action<string>? _serializedRecordObserver;
    private readonly TraceFilterSettings _filterSettings;
    private readonly Queue<TraceRecord> _pendingRecords;
    private readonly object _sync = new();
    private readonly long? _persistedByteLimit;
    private long _sequence;
    private long _persistedBytes;
    private bool _disposed;
    private bool _triggered;
    private int _triggerMatchCount;
    private int _postTriggerRecordCount;

    // Legacy mapping: none; this is a managed-only diagnostics entry point around solver traces derived from xfoil.f and xblsys.f call sites.
    // Difference from legacy: Classic XFoil opens files and writes text directly, while the managed port creates a structured JSONL writer with explicit runtime/session metadata.
    // Decision: Keep the managed constructor because session-scoped JSON tracing has no direct Fortran analogue.
    public JsonlTraceWriter(string path, string runtime, object? session = null, Action<string>? serializedRecordObserver = null)
        : this(CreateWriter(path), runtime, ownsWriter: true, session, serializedRecordObserver)
    {
    }

    // Legacy mapping: none; this constructor wires the managed trace sink used by parity-instrumented solver code.
    // Difference from legacy: The writer ownership and runtime label are explicit instead of being implicit in OPEN/WRITE statements and COMMON state.
    // Decision: Keep the managed constructor because it is infrastructure, not a solver-fidelity path.
    public JsonlTraceWriter(
        TextWriter writer,
        string runtime,
        bool ownsWriter = false,
        object? session = null,
        Action<string>? serializedRecordObserver = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
        _serializedRecordObserver = serializedRecordObserver;
        _filterSettings = TraceFilterSettings.FromEnvironment();
        _persistedByteLimit = TraceFilterSettings.ReadMaxTraceBytesFromEnvironment();
        _pendingRecords = new Queue<TraceRecord>(Math.Max(_filterSettings.RingBufferSize, 1));
        _triggered = !_filterSettings.HasTrigger;
        Runtime = string.IsNullOrWhiteSpace(runtime) ? "unknown" : runtime;

        if (session != null)
        {
            WriteEvent("session_start", "session", session);
        }
    }

    public string Runtime { get; }

    public override Encoding Encoding => Encoding.UTF8;

    // Legacy mapping: none; partial character writes are a managed concern only.
    // Difference from legacy: The method intentionally discards partial writes so JSONL framing stays valid, whereas legacy diagnostics were free-form text.
    // Decision: Keep the managed behavior because preserving JSONL integrity is more important than emulating arbitrary character streaming.
    public override void Write(char value)
    {
        // Legacy text diagnostics use WriteLine; ignore partial writes to keep JSONL valid.
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: diagnostic WRITE statements.
    // Difference from legacy: Plain text fragments are wrapped into structured JSON events instead of being written verbatim to the console or file.
    // Decision: Keep the managed event wrapper because trace consumers rely on structured records.
    public override void Write(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            WriteEvent("legacy_fragment", "legacy", new { message = value });
        }
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: diagnostic WRITE statements.
    // Difference from legacy: Legacy line writes become explicit JSONL `legacy_line` events with sequencing and runtime metadata.
    // Decision: Keep the managed line wrapper because it preserves diagnostics while remaining machine-readable.
    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            WriteEvent("legacy_line", "legacy", new { message = value });
        }
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: enter/exit instrumentation pattern.
    // Difference from legacy: Scope lifetime is represented by `IDisposable` instead of paired subroutine calls and manual write statements.
    // Decision: Keep the managed scope helper because it makes trace nesting explicit and exception-safe.
    public IDisposable Scope(string scope, object? inputs = null)
    {
        WriteEvent("call_enter", scope, inputs);
        return new TraceScope(this, scope);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: diagnostic WRITE, tools/fortran-debug/json_trace.f :: scalar/array event emission.
    // Difference from legacy: This method centralizes sequencing, timestamps, JSON serialization, and optional tags instead of scattering format strings across solver routines.
    // Decision: Keep the managed event hub because it is the canonical trace bridge for the port.
    public void WriteEvent(
        string kind,
        string scope,
        object? data = null,
        string? name = null,
        IReadOnlyList<double>? values = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var record = new TraceRecord(
            Sequence: Interlocked.Increment(ref _sequence),
            Runtime: Runtime,
            Kind: kind,
            Scope: scope,
            Name: name,
            Data: data,
            Values: values,
            Tags: tags,
            TimestampUtc: DateTime.UtcNow);

        WriteRecord(record, force: kind is "session_start" or "session_end");
    }

    private void WriteRecord(in TraceRecord record, bool force)
    {
        lock (_sync)
        {
            ObserveSerialized(record);

            if (force)
            {
                WriteSerialized(record);
                return;
            }

            bool capture = _filterSettings.MatchesCapture(record);
            if (!_filterSettings.HasTrigger)
            {
                if (capture)
                {
                    WriteSerialized(record);
                }

                return;
            }

            bool isTriggerSelectorMatch = _filterSettings.IsTrigger(record);
            bool isTrigger = false;
            if (isTriggerSelectorMatch)
            {
                _triggerMatchCount++;
                isTrigger = _filterSettings.IsTriggerOccurrence(_triggerMatchCount);
            }

            if (_triggered)
            {
                if (capture || isTriggerSelectorMatch)
                {
                    TryWritePostTriggerTail(record);
                }

                return;
            }

            if (isTrigger)
            {
                FlushPendingRecords();
                WriteSerialized(record);
                _triggered = true;
                return;
            }

            if (capture && _filterSettings.RingBufferSize > 0)
            {
                EnqueuePending(record);
            }
        }
    }

    private void EnqueuePending(in TraceRecord record)
    {
        while (_pendingRecords.Count >= _filterSettings.RingBufferSize && _pendingRecords.Count > 0)
        {
            _pendingRecords.Dequeue();
        }

        _pendingRecords.Enqueue(record);
    }

    private void FlushPendingRecords()
    {
        while (_pendingRecords.Count > 0)
        {
            WriteSerialized(_pendingRecords.Dequeue());
        }
    }

    private void TryWritePostTriggerTail(in TraceRecord record)
    {
        int? postTriggerLimit = _filterSettings.PostTriggerLimit;
        if (postTriggerLimit is not null &&
            _postTriggerRecordCount >= postTriggerLimit.Value)
        {
            return;
        }

        WriteSerialized(record);
        _postTriggerRecordCount++;
    }

    private void WriteSerialized(in TraceRecord record)
    {
        string json = SerializeRecord(record);
        EnforcePersistedByteLimit(json, record);
        _writer.WriteLine(json);
        _writer.Flush();
        _persistedBytes += GetPersistedByteCount(json);
    }

    private void ObserveSerialized(in TraceRecord record)
    {
        if (_serializedRecordObserver is null)
        {
            return;
        }

        _serializedRecordObserver(SerializeRecord(record));
    }

    private static string SerializeRecord(in TraceRecord record)
    {
        var outputRecord = TraceBitMetadata.Augment(record);
        return JsonSerializer.Serialize(outputRecord, JsonOptions);
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: array dump helpers.
    // Difference from legacy: Array logging is normalized through the structured event API rather than being emitted by bespoke format loops.
    // Decision: Keep the managed helper because it reduces repeated trace plumbing in solver code.
    public void WriteArray(
        string scope,
        string name,
        IEnumerable<double> values,
        object? data = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        WriteEvent("array", scope, data, name, values.ToArray(), tags);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: CLOSE/FLUSH-style shutdown behavior for diagnostics.
    // Difference from legacy: Disposal emits a managed `session_end` record and honors writer ownership instead of depending on process teardown and file units.
    // Decision: Keep the managed shutdown path because it guarantees a clean trace terminator without affecting solver parity.
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            try
            {
                WriteEvent("session_end", "session");
            }
            catch
            {
                // Best-effort shutdown; diagnostics must not throw during dispose.
            }

            if (_ownsWriter)
            {
                _writer.Dispose();
            }
            else
            {
                _writer.Flush();
            }
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    // Legacy mapping: none; file-backed TextWriter creation is managed infrastructure.
    // Difference from legacy: StreamWriter creation makes UTF-8 and autoflush explicit instead of relying on compiler/runtime defaults for file units.
    // Decision: Keep the managed helper because deterministic encoding matters for tooling.
    private static StreamWriter CreateWriter(string path)
    {
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.AutoFlush = true;
        return writer;
    }

    private void EnforcePersistedByteLimit(string json, in TraceRecord record)
    {
        if (_persistedByteLimit is null || _persistedByteLimit <= 0)
        {
            return;
        }

        long projectedBytes = _persistedBytes + GetPersistedByteCount(json);
        if (projectedBytes <= _persistedByteLimit.Value)
        {
            return;
        }

        throw new TracePersistenceLimitExceededException(
            _persistedByteLimit.Value,
            projectedBytes,
            record.Kind,
            record.Scope);
    }

    private int GetPersistedByteCount(string json)
    {
        int lineBytes = Encoding.UTF8.GetByteCount(json);
        int newlineBytes = Encoding.UTF8.GetByteCount(_writer.NewLine);
        return checked(lineBytes + newlineBytes);
    }

    private readonly record struct TraceRecord(
        long Sequence,
        string Runtime,
        string Kind,
        string Scope,
        string? Name,
        object? Data,
        IReadOnlyList<double>? Values,
        IReadOnlyDictionary<string, object?>? Tags,
        DateTime TimestampUtc);

    private readonly record struct TraceOutputRecord(
        long Sequence,
        string Runtime,
        string Kind,
        string Scope,
        string? Name,
        object? Data,
        IReadOnlyList<double>? Values,
        object? Tags,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? DataBits,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? ValuesBits,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? TagsBits,
        DateTime TimestampUtc);

    private static class TraceBitMetadata
    {
        public static TraceOutputRecord Augment(in TraceRecord record)
        {
            object? normalizedData = Normalize(record.Data);
            object? normalizedTags = Normalize(record.Tags);
            var valuesBits = BuildValuesBits(record.Values);

            return new TraceOutputRecord(
                record.Sequence,
                record.Runtime,
                record.Kind,
                record.Scope,
                record.Name,
                normalizedData,
                record.Values,
                normalizedTags,
                BuildObjectBits(record.Data, normalizedData),
                valuesBits,
                BuildObjectBits(record.Tags, normalizedTags),
                record.TimestampUtc);
        }

        private static object? Normalize(object? value)
        {
            if (value is null)
            {
                return null;
            }

            return value is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(value, JsonOptions);
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? BuildObjectBits(
            object? rawValue,
            object? normalizedValue)
        {
            if (normalizedValue is not JsonElement element)
            {
                return null;
            }

            var bits = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            AppendTypedBits(rawValue, element, prefix: null, bits);
            return bits.Count == 0 ? null : bits;
        }

        private static IReadOnlyList<IReadOnlyDictionary<string, string>>? BuildValuesBits(IReadOnlyList<double>? values)
        {
            if (values is null)
            {
                return null;
            }

            var bits = new List<IReadOnlyDictionary<string, string>>(values.Count);
            foreach (double value in values)
            {
                bits.Add(DescribeFloatingBits(value));
            }

            return bits;
        }

        private static void AppendTypedBits(
            object? rawValue,
            JsonElement element,
            string? prefix,
            IDictionary<string, IReadOnlyDictionary<string, string>> destination)
        {
            if (rawValue is JsonElement rawElement)
            {
                AppendElementBits(rawElement, prefix, destination);
                return;
            }

            if (rawValue is JsonDocument rawDocument)
            {
                AppendElementBits(rawDocument.RootElement, prefix, destination);
                return;
            }

            if (TryDescribeRawNumberBits(rawValue, out IReadOnlyDictionary<string, string>? rawBits))
            {
                if (!string.IsNullOrEmpty(prefix))
                {
                    destination[prefix] = rawBits!;
                }

                return;
            }

            if (element.ValueKind == JsonValueKind.Object &&
                TryEnumerateNamedMembers(rawValue, element, out IReadOnlyList<(string Name, object? RawValue, JsonElement Element)> members))
            {
                foreach ((string name, object? childRawValue, JsonElement childElement) in members)
                {
                    string childPrefix = string.IsNullOrEmpty(prefix)
                        ? name
                        : $"{prefix}.{name}";
                    AppendTypedBits(childRawValue, childElement, childPrefix, destination);
                }

                return;
            }

            if (element.ValueKind == JsonValueKind.Array &&
                rawValue is IEnumerable enumerable &&
                rawValue is not string)
            {
                int index = 0;
                IEnumerator enumerator = enumerable.GetEnumerator();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    object? childRawValue = enumerator.MoveNext() ? enumerator.Current : null;
                    string childPrefix = string.IsNullOrEmpty(prefix)
                        ? $"[{index}]"
                        : $"{prefix}[{index}]";
                    AppendTypedBits(childRawValue, item, childPrefix, destination);
                    index++;
                }

                return;
            }

            AppendElementBits(element, prefix, destination);
        }

        private static void AppendElementBits(
            JsonElement element,
            string? prefix,
            IDictionary<string, IReadOnlyDictionary<string, string>> destination)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        string childPrefix = string.IsNullOrEmpty(prefix)
                            ? property.Name
                            : $"{prefix}.{property.Name}";
                        AppendElementBits(property.Value, childPrefix, destination);
                    }

                    break;

                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        string childPrefix = string.IsNullOrEmpty(prefix)
                            ? $"[{index}]"
                            : $"{prefix}[{index}]";
                        AppendElementBits(item, childPrefix, destination);
                        index++;
                    }

                    break;

                case JsonValueKind.Number:
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        destination[prefix] = DescribeNumberBits(element);
                    }

                    break;
            }
        }

        private static IReadOnlyDictionary<string, string> DescribeNumberBits(JsonElement element)
        {
            string raw = element.GetRawText();
            if (element.TryGetInt64(out long integer) && !ShouldTreatAsFloatingPoint(raw))
            {
                var bits = new Dictionary<string, string>(capacity: 2, comparer: StringComparer.Ordinal);
                if (integer >= int.MinValue && integer <= int.MaxValue)
                {
                    bits["i32"] = $"0x{unchecked((uint)(int)integer):X8}";
                }

                bits["i64"] = $"0x{unchecked((ulong)integer):X16}";
                return bits;
            }

            return DescribeFloatingBits(element.GetDouble());
        }

        private static bool TryDescribeRawNumberBits(object? rawValue, out IReadOnlyDictionary<string, string>? bits)
        {
            bits = null;
            switch (rawValue)
            {
                case float single:
                    bits = DescribeSingleBits(single);
                    return true;

                case double dbl:
                    bits = DescribeFloatingBits(dbl);
                    return true;

                case decimal dec:
                    bits = DescribeFloatingBits((double)dec);
                    return true;

                case byte value:
                    bits = DescribeUnsignedIntegerBits(value);
                    return true;

                case sbyte value:
                    bits = DescribeSignedIntegerBits(value);
                    return true;

                case short value:
                    bits = DescribeSignedIntegerBits(value);
                    return true;

                case ushort value:
                    bits = DescribeUnsignedIntegerBits(value);
                    return true;

                case int value:
                    bits = DescribeSignedIntegerBits(value);
                    return true;

                case uint value:
                    bits = DescribeUnsignedIntegerBits(value);
                    return true;

                case long value:
                    bits = DescribeSignedIntegerBits(value);
                    return true;

                case ulong value:
                    bits = DescribeUnsignedIntegerBits(value);
                    return true;
            }

            if (rawValue is not null)
            {
                Type rawType = rawValue.GetType();
                if (rawType.IsEnum)
                {
                    bits = IsUnsignedEnum(rawType)
                        ? DescribeUnsignedIntegerBits(Convert.ToUInt64(rawValue, CultureInfo.InvariantCulture))
                        : DescribeSignedIntegerBits(Convert.ToInt64(rawValue, CultureInfo.InvariantCulture));
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyDictionary<string, string> DescribeFloatingBits(double value)
        {
            if (!double.IsFinite(value))
            {
                return new Dictionary<string, string>(capacity: 1, comparer: StringComparer.Ordinal)
                {
                    ["special"] = value.ToString("G17", CultureInfo.InvariantCulture)
                };
            }

            return new Dictionary<string, string>(capacity: 2, comparer: StringComparer.Ordinal)
            {
                ["f32"] = $"0x{unchecked((uint)BitConverter.SingleToInt32Bits((float)value)):X8}",
                ["f64"] = $"0x{unchecked((ulong)BitConverter.DoubleToInt64Bits(value)):X16}"
            };
        }

        private static IReadOnlyDictionary<string, string> DescribeSingleBits(float value)
        {
            if (!float.IsFinite(value))
            {
                return new Dictionary<string, string>(capacity: 1, comparer: StringComparer.Ordinal)
                {
                    ["special"] = value.ToString("G9", CultureInfo.InvariantCulture)
                };
            }

            return new Dictionary<string, string>(capacity: 2, comparer: StringComparer.Ordinal)
            {
                ["f32"] = $"0x{unchecked((uint)BitConverter.SingleToInt32Bits(value)):X8}",
                ["f64"] = $"0x{unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)):X16}"
            };
        }

        private static IReadOnlyDictionary<string, string> DescribeSignedIntegerBits(long value)
        {
            var bits = new Dictionary<string, string>(capacity: 2, comparer: StringComparer.Ordinal)
            {
                ["i64"] = $"0x{unchecked((ulong)value):X16}"
            };

            if (value >= int.MinValue && value <= int.MaxValue)
            {
                bits["i32"] = $"0x{unchecked((uint)(int)value):X8}";
            }

            return bits;
        }

        private static IReadOnlyDictionary<string, string> DescribeUnsignedIntegerBits(ulong value)
        {
            var bits = new Dictionary<string, string>(capacity: 2, comparer: StringComparer.Ordinal)
            {
                ["i64"] = $"0x{value:X16}"
            };

            if (value <= int.MaxValue)
            {
                bits["i32"] = $"0x{(uint)value:X8}";
            }

            return bits;
        }

        private static bool IsUnsignedEnum(Type enumType)
        {
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            return underlyingType == typeof(byte) ||
                   underlyingType == typeof(ushort) ||
                   underlyingType == typeof(uint) ||
                   underlyingType == typeof(ulong);
        }

        private static bool ShouldTreatAsFloatingPoint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw.Contains('.', StringComparison.Ordinal) ||
                   raw.Contains('e', StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw, "-0", StringComparison.Ordinal);
        }

        private static bool TryEnumerateNamedMembers(
            object? rawValue,
            JsonElement element,
            out IReadOnlyList<(string Name, object? RawValue, JsonElement Element)> members)
        {
            var collected = new List<(string Name, object? RawValue, JsonElement Element)>();

            if (rawValue is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            {
                foreach ((string key, object? value) in readOnlyDictionary)
                {
                    if (element.TryGetProperty(key, out JsonElement childElement))
                    {
                        collected.Add((key, value, childElement));
                    }
                }

                members = collected;
                return true;
            }

            if (rawValue is IDictionary<string, object?> dictionary)
            {
                foreach ((string key, object? value) in dictionary)
                {
                    if (element.TryGetProperty(key, out JsonElement childElement))
                    {
                        collected.Add((key, value, childElement));
                    }
                }

                members = collected;
                return true;
            }

            if (rawValue is IDictionary nonGenericDictionary)
            {
                foreach (DictionaryEntry entry in nonGenericDictionary)
                {
                    if (entry.Key is string key && element.TryGetProperty(key, out JsonElement childElement))
                    {
                        collected.Add((key, entry.Value, childElement));
                    }
                }

                members = collected;
                return true;
            }

            if (rawValue is null)
            {
                members = Array.Empty<(string Name, object? RawValue, JsonElement Element)>();
                return false;
            }

            foreach (PropertyInfo property in rawValue.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string propertyName = GetSerializedPropertyName(property);
                if (!element.TryGetProperty(propertyName, out JsonElement childElement))
                {
                    continue;
                }

                collected.Add((propertyName, property.GetValue(rawValue), childElement));
            }

            members = collected;
            return collected.Count > 0;
        }

        private static string GetSerializedPropertyName(PropertyInfo property)
        {
            JsonPropertyNameAttribute? attribute = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attribute is not null)
            {
                return attribute.Name;
            }

            return JsonOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
        }
    }

    private sealed class TraceFilterSettings
    {
        public static readonly TraceFilterSettings None = new(null, null, null, 0, null);

        private TraceFilterSettings(TraceSelector? captureSelector, TraceSelector? triggerSelector, int? triggerOccurrence, int ringBufferSize, int? postTriggerLimit)
        {
            CaptureSelector = captureSelector;
            TriggerSelector = triggerSelector;
            TriggerOccurrence = triggerOccurrence is > 0 ? triggerOccurrence : null;
            RingBufferSize = Math.Max(ringBufferSize, 0);
            PostTriggerLimit = postTriggerLimit is not null && postTriggerLimit.Value >= 0
                ? postTriggerLimit
                : null;
        }

        public TraceSelector? CaptureSelector { get; }

        public TraceSelector? TriggerSelector { get; }

        public int? TriggerOccurrence { get; }

        public int RingBufferSize { get; }

        public int? PostTriggerLimit { get; }

        public bool HasTrigger => TriggerSelector is not null;

        public bool MatchesCapture(in TraceRecord record)
            => CaptureSelector?.Matches(record) ?? true;

        public bool IsTrigger(in TraceRecord record)
            => TriggerSelector?.Matches(record) ?? false;

        public bool IsTriggerOccurrence(int triggerMatchCount)
            => TriggerOccurrence is null || triggerMatchCount == TriggerOccurrence.Value;

        public static TraceFilterSettings FromEnvironment()
        {
            TraceSelector? capture = TraceSelector.FromEnvironment(
                kindVar: "XFOIL_TRACE_KIND_ALLOW",
                scopeVar: "XFOIL_TRACE_SCOPE_ALLOW",
                nameVar: "XFOIL_TRACE_NAME_ALLOW",
                dataMatchVar: "XFOIL_TRACE_DATA_MATCH",
                sideVar: "XFOIL_TRACE_SIDE",
                stationVar: "XFOIL_TRACE_STATION",
                iterationVar: "XFOIL_TRACE_ITERATION",
                iterationMinVar: "XFOIL_TRACE_ITER_MIN",
                iterationMaxVar: "XFOIL_TRACE_ITER_MAX",
                modeVar: "XFOIL_TRACE_MODE");

            TraceSelector? trigger = TraceSelector.FromEnvironment(
                kindVar: "XFOIL_TRACE_TRIGGER_KIND",
                scopeVar: "XFOIL_TRACE_TRIGGER_SCOPE",
                nameVar: "XFOIL_TRACE_TRIGGER_NAME_ALLOW",
                dataMatchVar: "XFOIL_TRACE_TRIGGER_DATA_MATCH",
                sideVar: "XFOIL_TRACE_TRIGGER_SIDE",
                stationVar: "XFOIL_TRACE_TRIGGER_STATION",
                iterationVar: "XFOIL_TRACE_TRIGGER_ITERATION",
                iterationMinVar: "XFOIL_TRACE_TRIGGER_ITER_MIN",
                iterationMaxVar: "XFOIL_TRACE_TRIGGER_ITER_MAX",
                modeVar: "XFOIL_TRACE_TRIGGER_MODE");

            int? triggerOccurrence = ParseInt("XFOIL_TRACE_TRIGGER_OCCURRENCE");
            int ringBufferSize = ParseInt("XFOIL_TRACE_RING_BUFFER") ?? 0;
            int? postTriggerLimit = ParseInt("XFOIL_TRACE_POST_LIMIT");
            if (capture is null && trigger is null && triggerOccurrence is null && ringBufferSize <= 0)
            {
                return None;
            }

            return new TraceFilterSettings(capture, trigger, triggerOccurrence, ringBufferSize, postTriggerLimit);
        }

        public static long? ReadMaxTraceBytesFromEnvironment()
        {
            int? maxTraceMegabytes = ParseInt("XFOIL_MAX_TRACE_MB");
            if (maxTraceMegabytes is null || maxTraceMegabytes <= 0)
            {
                return null;
            }

            return checked((long)maxTraceMegabytes.Value * 1024L * 1024L);
        }

        private static int? ParseInt(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }
    }

    private sealed class TraceSelector
    {
        private TraceSelector(
            HashSet<string>? allowedKinds,
            HashSet<string>? allowedScopes,
            HashSet<string>? allowedNames,
            IReadOnlyDictionary<string, string>? requiredData,
            int? side,
            int? station,
            int? iteration,
            int? iterationMin,
            int? iterationMax,
            string? mode)
        {
            AllowedKinds = allowedKinds;
            AllowedScopes = allowedScopes;
            AllowedNames = allowedNames;
            RequiredData = requiredData;
            Side = side;
            Station = station;
            Iteration = iteration;
            IterationMin = iterationMin;
            IterationMax = iterationMax;
            Mode = mode;
        }

        public HashSet<string>? AllowedKinds { get; }

        public HashSet<string>? AllowedScopes { get; }

        public HashSet<string>? AllowedNames { get; }

        public IReadOnlyDictionary<string, string>? RequiredData { get; }

        public int? Side { get; }

        public int? Station { get; }

        public int? Iteration { get; }

        public int? IterationMin { get; }

        public int? IterationMax { get; }

        public string? Mode { get; }

        public static TraceSelector? FromEnvironment(
            string kindVar,
            string scopeVar,
            string nameVar,
            string dataMatchVar,
            string sideVar,
            string stationVar,
            string iterationVar,
            string iterationMinVar,
            string iterationMaxVar,
            string modeVar)
        {
            HashSet<string>? kinds = ParseSet(kindVar);
            HashSet<string>? scopes = ParseSet(scopeVar);
            HashSet<string>? names = ParseSet(nameVar);
            IReadOnlyDictionary<string, string>? requiredData = ParseKeyValuePairs(dataMatchVar);
            int? side = ParseInt(sideVar);
            int? station = ParseInt(stationVar);
            int? iteration = ParseInt(iterationVar);
            int? iterationMin = ParseInt(iterationMinVar);
            int? iterationMax = ParseInt(iterationMaxVar);
            string? mode = ParseString(modeVar);

            if (kinds is null &&
                scopes is null &&
                names is null &&
                requiredData is null &&
                side is null &&
                station is null &&
                iteration is null &&
                iterationMin is null &&
                iterationMax is null &&
                mode is null)
            {
                return null;
            }

            return new TraceSelector(kinds, scopes, names, requiredData, side, station, iteration, iterationMin, iterationMax, mode);
        }

        public bool Matches(in TraceRecord record)
        {
            if (AllowedKinds is not null && !AllowedKinds.Contains(record.Kind))
            {
                return false;
            }

            if (AllowedScopes is not null && !AllowedScopes.Contains(record.Scope))
            {
                return false;
            }

            if (AllowedNames is not null)
            {
                if (string.IsNullOrWhiteSpace(record.Name) || !AllowedNames.Contains(record.Name))
                {
                    return false;
                }
            }

            if (RequiredData is not null)
            {
                foreach ((string key, string expectedValue) in RequiredData)
                {
                    if (!TryGetValue(record.Data, key, out object? raw) ||
                        !TryFormatValue(raw, out string? actualValue) ||
                        !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            if (Side is not null && (!TryGetInt(record.Data, "side", out int side) || side != Side.Value))
            {
                return false;
            }

            if (Station is not null && (!TryGetInt(record.Data, "station", out int station) || station != Station.Value))
            {
                return false;
            }

            if (Iteration is not null && (!TryGetInt(record.Data, "iteration", out int iteration) || iteration != Iteration.Value))
            {
                return false;
            }

            if (IterationMin is not null && (!TryGetInt(record.Data, "iteration", out int iterationMinValue) || iterationMinValue < IterationMin.Value))
            {
                return false;
            }

            if (IterationMax is not null && (!TryGetInt(record.Data, "iteration", out int iterationMaxValue) || iterationMaxValue > IterationMax.Value))
            {
                return false;
            }

            if (Mode is not null && (!TryGetString(record.Data, "mode", out string? mode) || !string.Equals(mode, Mode, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        private static HashSet<string>? ParseSet(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var values = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);

            return values.Count == 0 ? null : values;
        }

        private static int? ParseInt(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }

        private static string? ParseString(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        private static IReadOnlyDictionary<string, string>? ParseKeyValuePairs(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] tokens = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string token in tokens)
            {
                int separatorIndex = token.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
                {
                    continue;
                }

                string key = token[..separatorIndex].Trim();
                string value = token[(separatorIndex + 1)..].Trim();
                if (key.Length == 0 || value.Length == 0)
                {
                    continue;
                }

                pairs[key] = value;
            }

            return pairs.Count == 0 ? null : pairs;
        }

        private static bool TryGetInt(object? source, string name, out int value)
        {
            if (!TryGetValue(source, name, out object? raw) || raw is null)
            {
                value = 0;
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static bool TryGetString(object? source, string name, out string? value)
        {
            if (!TryGetValue(source, name, out object? raw) || raw is null)
            {
                value = null;
                return false;
            }

            value = raw as string ?? raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryFormatValue(object? raw, out string? value)
        {
            if (raw is null)
            {
                value = null;
                return false;
            }

            value = raw switch
            {
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => raw.ToString()
            };

            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetValue(object? source, string name, out object? value)
        {
            if (source is IReadOnlyDictionary<string, object?> readonlyDictionary &&
                readonlyDictionary.TryGetValue(name, out value))
            {
                return true;
            }

            if (source is IDictionary<string, object?> dictionary &&
                dictionary.TryGetValue(name, out value))
            {
                return true;
            }

            if (source is null)
            {
                value = null;
                return false;
            }

            PropertyInfo? property = source.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property is null)
            {
                value = null;
                return false;
            }

            value = property.GetValue(source);
            return true;
        }
    }

    private sealed class TraceScope : IDisposable
    {
        private readonly JsonlTraceWriter _writer;
        private readonly string _scope;
        private int _disposed;

        // Legacy mapping: tools/fortran-debug/json_trace.f :: paired enter/exit instrumentation.
        // Difference from legacy: Scope exit state is stored in a disposable object rather than in the caller's control flow.
        // Decision: Keep the managed helper because it guarantees balanced trace scopes.
        public TraceScope(JsonlTraceWriter writer, string scope)
        {
            _writer = writer;
            _scope = scope;
        }

        // Legacy mapping: tools/fortran-debug/json_trace.f :: call-exit write.
        // Difference from legacy: Idempotent disposal replaces manual one-shot exit writes.
        // Decision: Keep the managed guard because repeated dispose calls should not duplicate trace records.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _writer.WriteEvent("call_exit", _scope);
        }
    }

    private sealed class PreciseSingleJsonConverter : JsonConverter<float>
    {
        // Legacy mapping: none; JSON parsing is managed-only infrastructure around parity traces.
        // Difference from legacy: The converter reads native JSON floats instead of parsing Fortran-formatted text records.
        // Decision: Keep the managed converter because the trace format is JSONL.
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetSingle();
        }

        // Legacy mapping: tools/fortran-debug/json_trace.f :: REAL value logging for parity.
        // Difference from legacy: The managed trace promotes floats to double text so reparsing reproduces the exact classic REAL value instead of shortest-roundtrip float text.
        // Decision: Keep this parity-preserving write path because trace comparison depends on it.
        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsNaN(value))
            {
                writer.WriteStringValue("NaN");
                return;
            }

            if (float.IsPositiveInfinity(value))
            {
                writer.WriteStringValue("Infinity");
                return;
            }

            if (float.IsNegativeInfinity(value))
            {
                writer.WriteStringValue("-Infinity");
                return;
            }

            // Parity traces are compared after JSON parsing on the C# side. Writing
            // float values through the float overload emits the shortest float text
            // (for example 0.39999998), which parses back to a different double than
            // classic XFoil's REAL output. Promote to double so the JSON carries the
            // exact legacy REAL value.
            writer.WriteNumberValue((double)value);
        }
    }
}

/// <summary>
/// Routes plain-text writes to multiple sinks while allowing trace extraction.
/// </summary>
public sealed class MultiplexTextWriter : TextWriter
{
    private readonly TextWriter[] _writers;

    // Legacy mapping: none; multiplexed TextWriter composition is managed-only infrastructure.
    // Difference from legacy: The CLI and solver can tee one write stream into multiple sinks, which classic XFoil did not abstract explicitly.
    // Decision: Keep the managed wrapper because it simplifies concurrent human and machine diagnostics.
    public MultiplexTextWriter(params TextWriter[] writers)
    {
        _writers = writers.Where(writer => writer != null).ToArray()!;
    }

    public override Encoding Encoding => _writers.FirstOrDefault()?.Encoding ?? Encoding.UTF8;

    // Legacy mapping: none; multi-sink character writes are a managed concern.
    // Difference from legacy: Each sink receives the character explicitly rather than relying on one active file unit.
    // Decision: Keep the managed fan-out helper because it is infrastructure only.
    public override void Write(char value)
    {
        foreach (TextWriter writer in _writers)
        {
            writer.Write(value);
        }
    }

    // Legacy mapping: none; multi-sink string writes are a managed concern.
    // Difference from legacy: The same string is replicated to every sink explicitly.
    // Decision: Keep the managed fan-out helper because it keeps trace capture and console output aligned.
    public override void Write(string? value)
    {
        foreach (TextWriter writer in _writers)
        {
            writer.Write(value);
        }
    }

    // Legacy mapping: none; multi-sink line writes are a managed concern.
    // Difference from legacy: The line is broadcast to each sink instead of being written to a single active unit.
    // Decision: Keep the managed behavior because it is the simplest way to mirror diagnostics.
    public override void WriteLine(string? value)
    {
        foreach (TextWriter writer in _writers)
        {
            writer.WriteLine(value);
        }
    }

    // Legacy mapping: none; flushing multiple writers is managed-only orchestration.
    // Difference from legacy: Flush fan-out is explicit per sink instead of implicit in a single file unit.
    // Decision: Keep the managed helper because it avoids stale diagnostics across sinks.
    public override void Flush()
    {
        foreach (TextWriter writer in _writers)
        {
            writer.Flush();
        }
    }

    // Legacy mapping: none; ownership/disposal of multiple managed writers has no direct Fortran analogue.
    // Difference from legacy: Disposal walks all sinks explicitly.
    // Decision: Keep the managed disposal behavior because it guarantees resource cleanup for trace infrastructure.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (TextWriter writer in _writers)
            {
                writer.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    // Legacy mapping: none; recursive sink lookup is managed-only infrastructure.
    // Difference from legacy: The method can search nested multiplex writers for a specific trace sink type.
    // Decision: Keep the helper because it lets ambient tracing recover the JSON writer without coupling callers to writer topology.
    public bool TryGetWriter<TWriter>(out TWriter? writer)
        where TWriter : class
    {
        foreach (TextWriter inner in _writers)
        {
            if (inner is TWriter matched)
            {
                writer = matched;
                return true;
            }

            if (inner is MultiplexTextWriter multiplex && multiplex.TryGetWriter(out writer))
            {
                return true;
            }
        }

        writer = null;
        return false;
    }
}

/// <summary>
/// Ambient trace context for parity diagnostics.
/// </summary>
public static class SolverTrace
{
    private static readonly AsyncLocal<JsonlTraceWriter?> CurrentWriter = new();

    /// <summary>
    /// Fast-path indicator. When false, trace events are skipped without
    /// AsyncLocal lookup or anonymous-object allocation. Set to true only
    /// when at least one writer has been activated this process.
    /// </summary>
    public static volatile bool IsActive;

    public static JsonlTraceWriter? Current => IsActive ? CurrentWriter.Value : null;

    // Legacy mapping: none; ambient managed trace lookup wraps parity-instrumented solver writes.
    // Difference from legacy: The active trace writer is discovered from a TextWriter graph instead of global file units.
    // Decision: Keep the managed adapter because it isolates tracing from solver call signatures.
    public static IDisposable Begin(TextWriter? writer)
    {
        if (writer is JsonlTraceWriter jsonWriter)
        {
            return Begin(jsonWriter);
        }

        if (writer is MultiplexTextWriter multiplex && multiplex.TryGetWriter<JsonlTraceWriter>(out JsonlTraceWriter? nestedWriter))
        {
            return Begin(nestedWriter);
        }

        return EmptyScope.Instance;
    }

    // Legacy mapping: none; managed ambient scope setup has no direct Fortran equivalent.
    // Difference from legacy: Async-local state tracks the active trace sink across nested calls instead of implicit unit numbers.
    // Decision: Keep the managed scope activation because it is the least intrusive way to thread tracing through the port.
    public static IDisposable Begin(JsonlTraceWriter? writer)
    {
        if (writer == null)
        {
            return EmptyScope.Instance;
        }

        JsonlTraceWriter? previous = CurrentWriter.Value;
        CurrentWriter.Value = writer;
        IsActive = true;  // process-global fast-path flag
        return new RestoreScope(previous);
    }

    // Legacy mapping: none; temporarily muting a managed ambient trace scope is infrastructure-only behavior.
    // Difference from legacy: Trace suspension is explicit and reversible instead of being achieved by omitting writes ad hoc.
    // Decision: Keep the helper because it avoids recursive self-tracing in diagnostic code.
    public static IDisposable Suspend()
    {
        JsonlTraceWriter? previous = CurrentWriter.Value;
        CurrentWriter.Value = null;
        return new RestoreScope(previous);
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: scoped call tracing.
    // Difference from legacy: A null-object disposable replaces branchy call-site checks.
    // Decision: Keep the managed helper because it keeps solver instrumentation concise.
    public static IDisposable Scope(string scope, object? inputs = null)
    {
        if (!IsActive) return EmptyScope.Instance;
        return Current?.Scope(scope, inputs) ?? EmptyScope.Instance;
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: scalar/object event writes.
    // Difference from legacy: Event emission routes through the ambient writer rather than explicit writer parameters at every call site.
    // Decision: Keep the managed facade because it standardizes trace calls across the solver.
    public static void Event(
        string kind,
        string scope,
        object? data = null,
        string? name = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        if (!IsActive) return;  // fast-path when no writer is active
        Current?.WriteEvent(kind, scope, data, name, values: null, tags);
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: array event writes.
    // Difference from legacy: Arrays are routed through the ambient writer and null-object scope instead of manual sink plumbing.
    // Decision: Keep the managed facade because it reduces instrumentation noise.
    public static void Array(
        string scope,
        string name,
        IEnumerable<double> values,
        object? data = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        if (!IsActive) return;
        Current?.WriteArray(scope, name, values, data, tags);
    }

    // Legacy mapping: tools/fortran-debug/json_trace.f :: scalar event writes.
    // Difference from legacy: The value is wrapped into a one-element JSON array for consistent schema handling.
    // Decision: Keep the managed scalar helper because downstream trace tooling expects the unified record shape.
    public static void Point(
        string scope,
        string name,
        double value,
        object? data = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        if (!IsActive) return;
        Current?.WriteEvent("scalar", scope, data, name, new[] { value }, tags);
    }

    // Legacy mapping: none; managed scope naming is an instrumentation convenience.
    // Difference from legacy: Type and caller-member names are composed automatically instead of being handwritten at each call site.
    // Decision: Keep the helper because it reduces drift in trace scope labels.
    public static string ScopeName(Type owner, [CallerMemberName] string memberName = "")
    {
        return string.IsNullOrWhiteSpace(memberName)
            ? owner.Name
            : $"{owner.Name}.{memberName}";
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly JsonlTraceWriter? _previous;
        private int _disposed;

        // Legacy mapping: none; restoring the ambient writer is a managed scoping concern.
        // Difference from legacy: Prior trace state is captured as an object and restored on dispose.
        // Decision: Keep the managed helper because it makes nested tracing exception-safe.
        public RestoreScope(JsonlTraceWriter? previous)
        {
            _previous = previous;
        }

        // Legacy mapping: none; restore-on-dispose is a managed scoping concern.
        // Difference from legacy: Idempotent disposal replaces paired manual state reset.
        // Decision: Keep the guard because nested instrumentation should be robust to repeated cleanup.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CurrentWriter.Value = _previous;
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        // Legacy mapping: none; null-object disposal is purely managed infrastructure.
        // Difference from legacy: No-op cleanup avoids null checks at trace call sites.
        // Decision: Keep the helper because it keeps instrumentation code straight-line.
        public void Dispose()
        {
        }
    }
}

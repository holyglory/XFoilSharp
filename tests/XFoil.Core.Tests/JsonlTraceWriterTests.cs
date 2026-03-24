using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using XFoil.Solver.Diagnostics;

namespace XFoil.Core.Tests;

[Collection("TraceEnvironment")]
public sealed class JsonlTraceWriterTests
{
    [Fact]
    public void KindFilter_KeepsOnlySelectedEventsAndSessionMarkers()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "keep_kind"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "kind-filter" }))
        {
            writer.WriteEvent("drop_kind", "test_scope", new { station = 7 });
            writer.WriteEvent("keep_kind", "test_scope", new { station = 29 });
        }

        string[] lines = GetLines(sink);
        Assert.Contains(lines, line => line.Contains("\"kind\":\"session_start\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"keep_kind\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\"kind\":\"drop_kind\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"session_end\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TriggerRingBuffer_FlushesBufferedContextWhenTriggerArrives()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_TRIGGER_KIND"] = "trigger_kind",
            ["XFOIL_TRACE_RING_BUFFER"] = "2"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "ring-buffer" }))
        {
            writer.WriteEvent("pre0", "test_scope", new { station = 1 });
            writer.WriteEvent("pre1", "test_scope", new { station = 2 });
            writer.WriteEvent("pre2", "test_scope", new { station = 3 });
            writer.WriteEvent("trigger_kind", "test_scope", new { station = 29 });
            writer.WriteEvent("post", "test_scope", new { station = 30 });
        }

        string[] lines = GetLines(sink);
        Assert.DoesNotContain(lines, line => line.Contains("\"kind\":\"pre0\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"pre1\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"pre2\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"trigger_kind\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"post\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TriggerPostLimit_TruncatesCapturedTailAfterTrigger()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "pre,trigger_kind,post1,post2",
            ["XFOIL_TRACE_TRIGGER_KIND"] = "trigger_kind",
            ["XFOIL_TRACE_RING_BUFFER"] = "1",
            ["XFOIL_TRACE_POST_LIMIT"] = "1"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "post-limit" }))
        {
            writer.WriteEvent("pre", "test_scope", new { station = 21 });
            writer.WriteEvent("trigger_kind", "test_scope", new { station = 22 });
            writer.WriteEvent("post1", "test_scope", new { station = 23 });
            writer.WriteEvent("post2", "test_scope", new { station = 24 });
        }

        string[] lines = GetLines(sink);
        Assert.Contains(lines, line => line.Contains("\"kind\":\"pre\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"trigger_kind\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"post1\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\"kind\":\"post2\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"session_end\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TriggerOccurrence_FiresOnlyOnNthMatchingEvent()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "pre,trigger_kind,post",
            ["XFOIL_TRACE_TRIGGER_KIND"] = "trigger_kind",
            ["XFOIL_TRACE_TRIGGER_OCCURRENCE"] = "2",
            ["XFOIL_TRACE_RING_BUFFER"] = "1",
            ["XFOIL_TRACE_POST_LIMIT"] = "1"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "trigger-occurrence" }))
        {
            writer.WriteEvent("pre", "test_scope", new { iteration = 1 });
            writer.WriteEvent("trigger_kind", "test_scope", new { iteration = 1 });
            writer.WriteEvent("pre", "test_scope", new { iteration = 2 });
            writer.WriteEvent("trigger_kind", "test_scope", new { iteration = 2 });
            writer.WriteEvent("post", "test_scope", new { iteration = 2 });
        }

        string[] lines = GetLines(sink);
        Assert.DoesNotContain(lines, line => line.Contains("\"iteration\":1", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"pre\"", StringComparison.Ordinal) &&
                                       line.Contains("\"iteration\":2", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"trigger_kind\"", StringComparison.Ordinal) &&
                                       line.Contains("\"iteration\":2", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"post\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TriggerPostLimitZero_PersistsNoTailAfterTrigger()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "pre,trigger_kind,post",
            ["XFOIL_TRACE_TRIGGER_KIND"] = "trigger_kind",
            ["XFOIL_TRACE_RING_BUFFER"] = "1",
            ["XFOIL_TRACE_POST_LIMIT"] = "0"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "post-limit-zero" }))
        {
            writer.WriteEvent("pre", "test_scope", new { station = 41 });
            writer.WriteEvent("trigger_kind", "test_scope", new { station = 42 });
            writer.WriteEvent("post", "test_scope", new { station = 43 });
        }

        string[] lines = GetLines(sink);
        Assert.Contains(lines, line => line.Contains("\"kind\":\"pre\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"trigger_kind\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\"kind\":\"post\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"session_end\"", StringComparison.Ordinal));
    }

    [Fact]
    public void NameAndDataFilters_KeepOnlyMatchingRecords()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "lu_back_substitute_term",
            ["XFOIL_TRACE_NAME_ALLOW"] = "basis_gamma_alpha0",
            ["XFOIL_TRACE_DATA_MATCH"] = "context=basis_gamma_alpha0_single;phase=forward;row=31"
        });

        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "data-match" }))
        {
            writer.WriteEvent(
                "lu_back_substitute_term",
                "test_scope",
                new { context = "basis_gamma_alpha0_single", phase = "forward", row = 31, column = 30 },
                name: "basis_gamma_alpha0");
            writer.WriteEvent(
                "lu_back_substitute_term",
                "test_scope",
                new { context = "basis_gamma_alpha0_single", phase = "backward", row = 31, column = 30 },
                name: "basis_gamma_alpha0");
            writer.WriteEvent(
                "lu_back_substitute_term",
                "test_scope",
                new { context = "basis_gamma_alpha90_single", phase = "forward", row = 31, column = 30 },
                name: "basis_gamma_alpha90");
        }

        string[] lines = GetLines(sink);
        Assert.Contains(lines, line => line.Contains("\"kind\":\"lu_back_substitute_term\"", StringComparison.Ordinal) &&
                                       line.Contains("\"name\":\"basis_gamma_alpha0\"", StringComparison.Ordinal) &&
                                       line.Contains("\"row\":31", StringComparison.Ordinal) &&
                                       line.Contains("\"phase\":\"forward\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\"phase\":\"backward\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\"name\":\"basis_gamma_alpha90\"", StringComparison.Ordinal));
    }

    [Fact]
    public void NumericPayloads_IncludeBitMetadata()
    {
        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "bits" }))
        {
            writer.WriteEvent("bit_test", "test_scope", new
            {
                station = 29,
                zUpw = 0.00015099591109901667
            });
        }

        string traceLine = GetLines(sink).Single(line => line.Contains("\"kind\":\"bit_test\"", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(traceLine);
        JsonElement dataBits = document.RootElement.GetProperty("dataBits");

        Assert.Equal("0x0000001D", dataBits.GetProperty("station").GetProperty("i32").GetString());
        Assert.Equal("0x391E54A8", dataBits.GetProperty("zUpw").GetProperty("f32").GetString());
        Assert.Equal("0x3F23CA9500000000", dataBits.GetProperty("zUpw").GetProperty("f64").GetString());
    }

    [Fact]
    public void WholeNumberFloatingPayloads_PreserveFloatingPointBitMetadata()
    {
        using var sink = new StringWriter();
        using (var writer = new JsonlTraceWriter(sink, runtime: "csharp", session: new { caseName = "whole-floats" }))
        {
            writer.WriteEvent("bit_test", "test_scope", new
            {
                station = 29,
                row23 = 82099.0f,
                hk2T2 = -68043.0
            });
        }

        string traceLine = GetLines(sink).Single(line => line.Contains("\"kind\":\"bit_test\"", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(traceLine);
        JsonElement dataBits = document.RootElement.GetProperty("dataBits");

        JsonElement row23Bits = dataBits.GetProperty("row23");
        Assert.False(row23Bits.TryGetProperty("i32", out _));
        Assert.Equal("0x47A05980", row23Bits.GetProperty("f32").GetString());
        Assert.Equal("0x40F40B3000000000", row23Bits.GetProperty("f64").GetString());

        JsonElement hk2T2Bits = dataBits.GetProperty("hk2T2");
        Assert.False(hk2T2Bits.TryGetProperty("i32", out _));
        Assert.Equal("0xC784E580", hk2T2Bits.GetProperty("f32").GetString());
        Assert.Equal("0xC0F09CB000000000", hk2T2Bits.GetProperty("f64").GetString());

        Assert.Equal("0x0000001D", dataBits.GetProperty("station").GetProperty("i32").GetString());
    }

    [Fact]
    public void SerializedRecordObserver_ReceivesComparableOutput()
    {
        using var sink = new StringWriter();
        string? observed = null;
        using (var writer = new JsonlTraceWriter(
                   sink,
                   runtime: "csharp",
                   session: new { caseName = "observer" },
                   serializedRecordObserver: json => observed ??= json))
        {
            writer.WriteEvent("observer_kind", "test_scope", new { station = 3 });
        }

        Assert.NotNull(observed);
        Assert.Contains("\"kind\":\"session_start\"", observed, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializedRecordObserver_SeesFilteredEventsWithoutPersistingThem()
    {
        using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "keep_kind"
        });

        using var sink = new StringWriter();
        var observed = new List<string>();
        using (var writer = new JsonlTraceWriter(
                   sink,
                   runtime: "csharp",
                   session: new { caseName = "observer-filtered" },
                   serializedRecordObserver: observed.Add))
        {
            writer.WriteEvent("drop_kind", "test_scope", new { station = 7 });
            writer.WriteEvent("keep_kind", "test_scope", new { station = 29 });
        }

        string[] lines = GetLines(sink);
        Assert.DoesNotContain(lines, line => line.Contains("\"kind\":\"drop_kind\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"kind\":\"keep_kind\"", StringComparison.Ordinal));
        Assert.Contains(observed, line => line.Contains("\"kind\":\"drop_kind\"", StringComparison.Ordinal));
        Assert.Contains(observed, line => line.Contains("\"kind\":\"keep_kind\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MaxTraceSize_ThrowsBeforePersistedTraceCanGrowPastLimit()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-trace-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string tracePath = Path.Combine(tempDir, "trace.jsonl");
            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_MAX_TRACE_MB"] = "1"
            });

            using var writer = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "limit" });
            string payload = new string('x', 350_000);

            Exception? failure = null;
            try
            {
                while (true)
                {
                    writer.WriteEvent("large_payload", "test_scope", new { payload });
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            TracePersistenceLimitExceededException exception = Assert.IsType<TracePersistenceLimitExceededException>(failure);
            Assert.Contains("large_payload", exception.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(tracePath), $"Expected partial trace to exist at {tracePath}");
            long actualBytes = new FileInfo(tracePath).Length;
            Assert.True(actualBytes < 1024L * 1024L, $"Trace should stay below the configured limit, but was {actualBytes} bytes.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string[] GetLines(StringWriter sink)
        => sink.ToString()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

    private sealed class TraceEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public TraceEnvironmentScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach ((string key, string? value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

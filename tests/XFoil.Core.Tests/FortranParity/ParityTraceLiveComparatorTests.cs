using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using XFoil.Solver.Diagnostics;

namespace XFoil.Core.Tests.FortranParity;

[Collection("TraceEnvironment")]
public sealed class ParityTraceLiveComparatorTests
{
    [Fact]
    public void ObserveSerializedRecord_ThrowsOnFirstMismatch()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("bit_test", "SETBL", new { side = 1, station = 2, value = 1.25f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            string managedTracePath = Path.Combine(tempDir, "managed.jsonl");

            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(
                () => managedWriter.WriteEvent("bit_test", "ViscousNewtonAssembler.BuildNewtonSystem", new { side = 1, station = 2, value = 1.5f }));

            Assert.Contains("Live parity mismatch", exception.Message);
            Assert.Contains("side=1", exception.Message);
            Assert.Contains("station=2", exception.Message);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_AllowsEquivalentIntegralFloatFormatting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { index = 1, ny = -1.0f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            Exception? exception = Record.Exception(
                () => managedWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { index = 1, ny = -1.0 }));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_AllowsEquivalentSinglePayloadsWhenOnlyManagedBitsArePresent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            File.WriteAllText(
                referenceTracePath,
                "{\"sequence\":1,\"runtime\":\"fortran\",\"kind\":\"pangen_newton_state\",\"scope\":\"PANGEN\",\"name\":null,\"data\":{\"iteration\":1,\"index\":293,\"rez\":-2.9802322387695312e-08},\"values\":null,\"tags\":null,\"timestampUtc\":null}\n");

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent(
                "pangen_newton_state",
                "CurvatureAdaptivePanelDistributor.DistributeCore",
                new
                {
                    iteration = 1,
                    index = 293,
                    rez = BitConverter.Int32BitsToSingle(unchecked((int)0xB3000000))
                });

            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_AllowsBooleanishNumberAndBoolPayloads()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("predicted_edge_velocity_term", "UESET", new { side = 1, station = 2, isWakeSource = 0 });
                referenceWriter.WriteEvent("predicted_edge_velocity_term", "UESET", new { side = 1, station = 3, isWakeSource = 1 });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("predicted_edge_velocity_term", "ViscousNewtonAssembler.ComputePredictedEdgeVelocities", new { side = 1, station = 2, isWakeSource = false });
            managedWriter.WriteEvent("predicted_edge_velocity_term", "ViscousNewtonAssembler.ComputePredictedEdgeVelocities", new { side = 1, station = 3, isWakeSource = true });

            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_AllowsNamedFloatingPointValuePayloads()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteArray("PSILIN", "freestream_terms", new[] { double.NaN, double.PositiveInfinity, -0.0 });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            Exception? exception = Record.Exception(
                () => managedWriter.WriteArray("PSILIN", "freestream_terms", new[] { double.NaN, double.PositiveInfinity, -0.0 }));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_DetailedReportIncludesMatchedContext()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(
                () => managedWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.49999997f }));

            string report = exception.ToDetailedReport();
            Assert.Contains("Comparable events matched before abort: 1", report);
            Assert.Contains("Last matched comparable events:", report);
            Assert.Contains("kind=wake_panel_state", report);
            Assert.Contains("Reference mismatch event: kind=wake_node", report);
            Assert.Contains("Managed mismatch event: kind=wake_node", report);
            Assert.Contains("Boundary hint: the comparator localized the divergence inside this trace event", report);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AssertCompleted_ThrowsWhenReferenceHasTrailingComparableEvents()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("matched_event", "PSILIN", new { side = 1, station = 2, fieldIndex = 61, value = 1.25f });
                referenceWriter.WriteEvent("trailing_event", "PSILIN", new { side = 1, station = 2, fieldIndex = 62, value = 3.25f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("matched_event", "PSILIN", new { side = 1, station = 2, fieldIndex = 61, value = 1.25f });

            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(comparator.AssertCompleted);
            Assert.Contains("still has comparable events", exception.Message);
            Assert.Contains("Comparable events matched before abort: 1", exception.ToDetailedReport());
            Assert.Contains("Reference mismatch event: kind=trailing_event", exception.ToDetailedReport());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_DetailedReportIncludesFocusedRerunRecipe()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(
                () => managedWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.49999997f }));

            string report = exception.ToDetailedReport();
            Assert.Contains("Focused rerun recipe:", report);
            Assert.Contains("export XFOIL_TRACE_KIND_ALLOW='wake_panel_state,wake_node'", report);
            Assert.Contains("export XFOIL_TRACE_TRIGGER_KIND='wake_node'", report);
            Assert.Contains("export XFOIL_TRACE_TRIGGER_SCOPE='InfluenceMatrixBuilder'", report);
            Assert.Contains("export XFOIL_TRACE_TRIGGER_DATA_MATCH='iteration=1;index=3'", report);
            Assert.Contains("reuse the full reference trace", report);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_FocusedRecipeOmitsManagedOnlyIdentityFields()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("simi_precombine_rows", "BLSYS", new { side = 1, station = 2, eq2Vs1_22 = -1.0f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(
                () => managedWriter.WriteEvent("simi_precombine_rows", "BoundaryLayerSystemAssembler.AssembleStationSystem", new { iteration = 1, side = 1, station = 2, eq2Vs1_22 = -1.5f }));

            string report = exception.ToDetailedReport();
            Assert.Contains("export XFOIL_TRACE_TRIGGER_DATA_MATCH='side=1;station=2'", report);
            Assert.DoesNotContain("iteration=1;side=1;station=2", report);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_DetailedReportIncludesManagedCallStackHints()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_node", "BoundaryLayerSystemAssembler.ComputeKinematicParameters", new { iteration = 1, side = 1, station = 2, index = 3, y = -0.5f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            using var outerScope = managedWriter.Scope("BoundaryLayerSystemAssembler.AssembleStationSystem");
            using var innerScope = managedWriter.Scope("BoundaryLayerSystemAssembler.ComputeKinematicParameters");

            LiveParityTraceMismatchException exception = Assert.Throws<LiveParityTraceMismatchException>(
                () => managedWriter.WriteEvent("wake_node", "BoundaryLayerSystemAssembler.ComputeKinematicParameters", new { iteration = 1, side = 1, station = 2, index = 3, y = -0.49f }));

            string report = exception.ToDetailedReport();
            Assert.Contains("Managed owning scope hint: BoundaryLayerSystemAssembler.ComputeKinematicParameters", report);
            Assert.Contains("Managed parent scope hint: BoundaryLayerSystemAssembler.AssembleStationSystem", report);
            Assert.Contains("Managed active call stack:", report);
            Assert.Contains("Recent managed event tail:", report);
            Assert.Contains("kind=call_enter scope=BoundaryLayerSystemAssembler.AssembleStationSystem", report);
            Assert.Contains("kind=call_enter scope=BoundaryLayerSystemAssembler.ComputeKinematicParameters", report);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_SkipsUnselectedReferenceEvents()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("drop_kind", "InfluenceMatrixBuilder", new { iteration = 1, index = 1, y = 0.1f });
                referenceWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
                referenceWriter.WriteEvent("drop_kind", "InfluenceMatrixBuilder", new { iteration = 1, index = 4, y = 0.2f });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "wake_panel_state,wake_node"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            Assert.NotNull(captureSelector);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("wake_panel_state", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, fieldIndex = 62, y = 0.125f });
            managedWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_IgnoresNullNumericIdentityFields()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("bldif_eq2_input_bundle", "BLDIF", new { side = 1, station = 2, ityp = 0, x1 = 0.5f });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "bldif_eq2_input_bundle",
                ["XFOIL_TRACE_SIDE"] = "1",
                ["XFOIL_TRACE_STATION"] = "2"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            Assert.NotNull(captureSelector);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("bldif_eq2_input_bundle", "BoundaryLayerSystemAssembler.ComputeFiniteDifferences", new { side = (int?)null, station = (int?)null, ityp = 0, x1 = 0.5f });

            comparator.AssertCompleted();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_UsesTriggerSelectorAsReferenceAnchor()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { iteration = 1, index = 2, dx = -0.2f });
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { iteration = 2, index = 2, dx = -0.0019f });
                referenceWriter.WriteEvent("spline_eval", "ParametricSpline", new { iteration = 2, index = 2, lowerIndex = 1 });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "arc_length_step,spline_eval",
                ["XFOIL_TRACE_TRIGGER_KIND"] = "arc_length_step",
                ["XFOIL_TRACE_TRIGGER_SCOPE"] = "ParametricSpline",
                ["XFOIL_TRACE_TRIGGER_DATA_MATCH"] = "iteration=2;index=2"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            ParityTraceFocusSelector? triggerSelector = ParityTraceFocusSelector.FromTriggerEnvironment();
            Assert.NotNull(captureSelector);
            Assert.NotNull(triggerSelector);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector, triggerSelector: triggerSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("arc_length_step", "ParametricSpline", new { iteration = 2, index = 2, dx = -0.0019f });
            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new { iteration = 2, index = 2, lowerIndex = 1 });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_UsesNthTriggerOccurrenceAsReferenceAnchor()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.2f });
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 3, dx = -0.1f });
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.0019f });
                referenceWriter.WriteEvent("spline_eval", "ParametricSpline", new { index = 2, lowerIndex = 1 });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "arc_length_step,spline_eval",
                ["XFOIL_TRACE_TRIGGER_KIND"] = "arc_length_step",
                ["XFOIL_TRACE_TRIGGER_SCOPE"] = "ParametricSpline",
                ["XFOIL_TRACE_TRIGGER_DATA_MATCH"] = "index=2",
                ["XFOIL_TRACE_TRIGGER_OCCURRENCE"] = "2"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            ParityTraceFocusSelector? triggerSelector = ParityTraceFocusSelector.FromTriggerEnvironment();
            Assert.NotNull(captureSelector);
            Assert.NotNull(triggerSelector);
            Assert.Equal(2, triggerSelector!.Occurrence);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector, triggerSelector: triggerSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.2f });
            managedWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.0019f });
            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new { index = 2, lowerIndex = 1 });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DefaultComparator_SkipsManagedComparablePreambleUntilFirstBootstrap()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.2f });
                referenceWriter.WriteEvent("pangen_snew_node", "PANGEN", new { stage = "initial", iteration = 0, index = 1 });
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.2f });
                referenceWriter.WriteEvent("pangen_snew_node", "PANGEN", new { stage = "initial", iteration = 0, index = 1 });
                referenceWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.0019f });
                referenceWriter.WriteEvent("spline_eval", "ParametricSpline", new { index = 2, lowerIndex = 1 });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.2f });
            managedWriter.WriteEvent("pangen_snew_node", "CurvatureAdaptivePanelDistributor.DistributeCore", new { stage = "initial", iteration = 0, index = 1 });
            managedWriter.WriteEvent("arc_length_step", "ParametricSpline", new { index = 2, dx = -0.0019f });
            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new { index = 2, lowerIndex = 1 });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_AlignsRepeatedComparableKindsBySharedIdentityFields()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("seed_probe", "SETBL", new { iteration = 4, index = 1 });
                referenceWriter.WriteEvent("blsys_interval_inputs", "BLSYS", new { side = 1, station = 2, index = 1, x2 = 0.01f });
                referenceWriter.WriteEvent("blsys_interval_inputs", "BLSYS", new { side = 1, station = 3, index = 1, x2 = 0.02f });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "seed_probe,blsys_interval_inputs",
                ["XFOIL_TRACE_TRIGGER_KIND"] = "seed_probe",
                ["XFOIL_TRACE_TRIGGER_DATA_MATCH"] = "iteration=4;index=1"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            ParityTraceFocusSelector? triggerSelector = ParityTraceFocusSelector.FromTriggerEnvironment();
            Assert.NotNull(captureSelector);
            Assert.NotNull(triggerSelector);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector, triggerSelector: triggerSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("seed_probe", "ViscousNewtonAssembler.BuildNewtonSystem", new { iteration = 4, index = 1 });
            managedWriter.WriteEvent("blsys_interval_inputs", "BoundaryLayerSystemAssembler.AssembleStationSystem", new { side = 1, station = 3, index = 1, x2 = 0.02f });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FocusedComparatorReuse_IgnoresManagedComparableEventsBeforeTrigger()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("pangen_newton_state", "PANGEN", new { iteration = 1, index = 12 });
                referenceWriter.WriteEvent("spline_eval", "ParametricSpline", new { lowerIndex = 47, accumulator = -8.679932079758146e-07f });
            }

            using var env = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "pangen_newton_state,spline_eval",
                ["XFOIL_TRACE_TRIGGER_KIND"] = "pangen_newton_state",
                ["XFOIL_TRACE_TRIGGER_DATA_MATCH"] = "iteration=1;index=12"
            });

            ParityTraceFocusSelector? captureSelector = ParityTraceFocusSelector.FromEnvironment();
            ParityTraceFocusSelector? triggerSelector = ParityTraceFocusSelector.FromTriggerEnvironment();
            Assert.NotNull(captureSelector);
            Assert.NotNull(triggerSelector);

            var comparator = new ParityTraceLiveComparator(referenceTracePath, captureSelector: captureSelector, triggerSelector: triggerSelector);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new { lowerIndex = 120, accumulator = 0.0f });
            managedWriter.WriteEvent("pangen_newton_state", "CurvatureAdaptivePanelDistributor.DistributeCore", new { iteration = 1, index = 12 });
            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new { lowerIndex = 47, accumulator = -8.679932079758146e-07f });

            Exception? exception = Record.Exception(comparator.AssertCompleted);
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_IgnoresLegacyTextMirrorEvents()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("kinematic_result", "WakeGeometryGenerator", new { index = 1, ds = 0.25f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteLine("debug text should not participate in live compare");
            Exception? exception = Record.Exception(
                () => managedWriter.WriteEvent("kinematic_result", "WakeGeometryGenerator", new { index = 1, ds = 0.25f }));

            Assert.Null(exception);
            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_IgnoresManagedOnlyComparableSignatures()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("naca4_config", "NacaAirfoilGenerator.Generate4DigitCore", new { ides = 12, pointCount = 239 });
                referenceWriter.WriteArray(
                    "ReferenceOnlyScope",
                    "reference_only_array",
                    new[] { 1.0, 2.0 });
                referenceWriter.WriteEvent("kinematic_result", "WakeGeometryGenerator", new { index = 1, ds = 0.25f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteArray(
                "ViscousSolverEngine.TraceBufferGeometry",
                "buffer_geometry_x",
                new[] { 0.0, 1.0 });
            Exception? exception = Record.Exception(
                () => managedWriter.WriteEvent("kinematic_result", "WakeGeometryGenerator", new { index = 1, ds = 0.25f }));

            Assert.Null(exception);
            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_IgnoresCrossPhasePanelNodeDiagnostics()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("matrix_entry", "GGCALC", new { matrix = "aij", row = 1, col = 1, value = 1.25f });
                referenceWriter.WriteEvent("panel_node", "QDCALC", new { index = 1, x = 0.0f, y = 0.0f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("panel_node", "LinearVortexInviscidSolver.AssembleAndFactorSystem", new { index = 1, x = 0.0f, y = 0.0f });
            managedWriter.WriteEvent("matrix_entry", "LinearVortexInviscidSolver.AssembleAndFactorSystem", new { matrix = "aij", row = 1, col = 1, value = 1.25f });

            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ObserveSerializedRecord_IgnoresManagedOnlyDiagnosticDataFields()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("spline_eval", "ParametricSpline", new { index = 2, lowerIndex = 1, accumulator = -8.679932079758146e-07f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            using var managedSink = new StringWriter();
            using var managedWriter = new JsonlTraceWriter(
                managedSink,
                runtime: "csharp",
                session: new { caseId = "managed" },
                serializedRecordObserver: comparator.ObserveSerializedRecord);

            managedWriter.WriteEvent("spline_eval", "ParametricSpline", new
            {
                index = 2,
                lowerIndex = 1,
                accumulator = -8.679932079758146e-07f,
                operand1 = 0.125f,
                operand2 = -0.0625f,
                operandCombined = 0.0625f
            });

            Assert.Null(Record.Exception(comparator.AssertCompleted));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

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

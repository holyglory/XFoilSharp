using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using XFoil.Solver.Diagnostics;

namespace XFoil.Core.Tests.FortranParity;

[Collection("TraceEnvironment")]
public sealed class FortranReferenceCasesTests
{
    [Fact]
    public void GetImplicitLiveCompareCaptureOverrides_DefaultsToSummaryMarkersOnly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-overrides-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = null,
                ["XFOIL_TRACE_SCOPE_ALLOW"] = null,
                ["XFOIL_TRACE_NAME_ALLOW"] = null,
                ["XFOIL_TRACE_DATA_MATCH"] = null,
                ["XFOIL_TRACE_SIDE"] = null,
                ["XFOIL_TRACE_STATION"] = null,
                ["XFOIL_TRACE_ITERATION"] = null,
                ["XFOIL_TRACE_ITER_MIN"] = null,
                ["XFOIL_TRACE_ITER_MAX"] = null,
                ["XFOIL_TRACE_MODE"] = null,
                ["XFOIL_TRACE_TRIGGER_KIND"] = null,
                ["XFOIL_TRACE_TRIGGER_SCOPE"] = null,
                ["XFOIL_TRACE_TRIGGER_NAME_ALLOW"] = null,
                ["XFOIL_TRACE_TRIGGER_DATA_MATCH"] = null,
                ["XFOIL_TRACE_TRIGGER_OCCURRENCE"] = null,
                ["XFOIL_TRACE_TRIGGER_SIDE"] = null,
                ["XFOIL_TRACE_TRIGGER_STATION"] = null,
                ["XFOIL_TRACE_TRIGGER_ITERATION"] = null,
                ["XFOIL_TRACE_TRIGGER_ITER_MIN"] = null,
                ["XFOIL_TRACE_TRIGGER_ITER_MAX"] = null,
                ["XFOIL_TRACE_TRIGGER_MODE"] = null,
                ["XFOIL_TRACE_RING_BUFFER"] = null,
                ["XFOIL_TRACE_POST_LIMIT"] = null,
                ["XFOIL_TRACE_FULL"] = null
            });

            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
            }

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            IReadOnlyDictionary<string, string?>? overrides = FortranReferenceCases.GetImplicitLiveCompareCaptureOverrides(comparator);

            Assert.NotNull(overrides);
            Assert.True(overrides!.TryGetValue("XFOIL_TRACE_KIND_ALLOW", out string? kindAllow));
            Assert.Equal("__summary_none__", kindAllow);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetImplicitLiveCompareCaptureOverrides_PreservesExplicitFocusSelection()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-overrides-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
            }

            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_KIND_ALLOW"] = "wake_node"
            });

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            IReadOnlyDictionary<string, string?>? overrides = FortranReferenceCases.GetImplicitLiveCompareCaptureOverrides(comparator);

            Assert.Null(overrides);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetImplicitLiveCompareCaptureOverrides_HonorsExplicitFullTraceMode()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-livecompare-overrides-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string referenceTracePath = Path.Combine(tempDir, "reference.jsonl");
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 3, y = -0.5f });
            }

            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_TRACE_FULL"] = "1"
            });

            var comparator = new ParityTraceLiveComparator(referenceTracePath);
            IReadOnlyDictionary<string, string?>? overrides = FortranReferenceCases.GetImplicitLiveCompareCaptureOverrides(comparator);

            Assert.Null(overrides);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetManagedDirectory_ResolvesRelativeOverrideAgainstRepositoryRoot()
    {
        string repoRoot = FortranReferenceCases.FindRepositoryRoot();
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-relpath-managed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string previousDirectory = Environment.CurrentDirectory;

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_CASE_OUTPUT_DIR"] = "tools/fortran-debug/csharp/relative-managed-output"
            });

            string managedDirectory = FortranReferenceCases.GetManagedDirectory("n0012_re1e6_a0");
            Assert.Equal(
                Path.Combine(repoRoot, "tools", "fortran-debug", "csharp", "relative-managed-output"),
                managedDirectory);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateLiveComparatorIfEnabled_ResolvesRelativeReferenceTraceAgainstRepositoryRoot()
    {
        string repoRoot = FortranReferenceCases.FindRepositoryRoot();
        string relativeDirectory = Path.Combine("tools", "fortran-debug", "reference", $"livecompare-rel-{Guid.NewGuid():N}");
        string absoluteDirectory = Path.Combine(repoRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);
        string referenceTracePath = Path.Combine(absoluteDirectory, "reference_trace.1.jsonl");
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-relpath-livecompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string previousDirectory = Environment.CurrentDirectory;

        try
        {
            using (var referenceWriter = new JsonlTraceWriter(referenceTracePath, runtime: "fortran", session: new { caseId = "ref" }))
            {
                referenceWriter.WriteEvent("wake_panel_state", "QDCALC", new { fieldIndex = 81, index = 1, psiX = -0.1f });
            }

            Directory.SetCurrentDirectory(tempDir);
            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_LIVE_COMPARE_REFERENCE_TRACE"] = Path.Combine(relativeDirectory, "reference_trace.1.jsonl")
            });

            MethodInfo factory = typeof(FortranReferenceCases).GetMethod(
                "CreateLiveComparatorIfEnabled",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("CreateLiveComparatorIfEnabled not found.");

            object? comparator = factory.Invoke(null, new object[] { "n0012_re1e6_a0" });
            Assert.NotNull(comparator);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(tempDir, recursive: true);
            Directory.Delete(absoluteDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetManagedTracePath_SkipsSessionOnlyNewestVersionedTrace()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-managed-trace-select-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string reusableTracePath = Path.Combine(tempDir, "csharp_trace.10.jsonl");
            string sessionOnlyTracePath = Path.Combine(tempDir, "csharp_trace.11.jsonl");
            WriteReusableTrace(reusableTracePath);
            WriteSessionOnlyTrace(sessionOnlyTracePath);

            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_CASE_OUTPUT_DIR"] = tempDir
            });

            string selectedPath = FortranReferenceCases.GetManagedTracePath("ignored_case");
            Assert.Equal(reusableTracePath, selectedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetManagedTracePath_FallsBackToReusableCanonicalTraceWhenVersionedArtifactsAreSessionOnly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-managed-trace-select-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string canonicalTracePath = Path.Combine(tempDir, "csharp_trace.jsonl");
            string sessionOnlyTracePath = Path.Combine(tempDir, "csharp_trace.11.jsonl");
            WriteReusableTrace(canonicalTracePath);
            WriteSessionOnlyTrace(sessionOnlyTracePath);

            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_CASE_OUTPUT_DIR"] = tempDir
            });

            string selectedPath = FortranReferenceCases.GetManagedTracePath("ignored_case");
            Assert.Equal(canonicalTracePath, selectedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void EnsureManagedArtifacts_ReusesReusableVersionedTraceInsteadOfRefreshingUnknownCase()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"xfoilsharp-managed-trace-select-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string reusableTracePath = Path.Combine(tempDir, "csharp_trace.10.jsonl");
            string canonicalTracePath = Path.Combine(tempDir, "csharp_trace.jsonl");
            string dumpPath = Path.Combine(tempDir, "csharp_dump.txt");
            WriteReusableTrace(reusableTracePath);
            WriteSessionOnlyTrace(canonicalTracePath);
            File.WriteAllText(dumpPath, "dump");

            using var environment = new TraceEnvironmentScope(new Dictionary<string, string?>
            {
                ["XFOIL_CASE_OUTPUT_DIR"] = tempDir
            });

            FortranReferenceCases.EnsureManagedArtifacts("ignored_case");

            string selectedPath = FortranReferenceCases.GetManagedTracePath("ignored_case");
            Assert.Equal(reusableTracePath, selectedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void WriteReusableTrace(string path)
    {
        using var writer = new JsonlTraceWriter(path, runtime: "csharp", session: new { caseId = "managed" });
        writer.WriteEvent("wake_node", "InfluenceMatrixBuilder", new { iteration = 1, index = 2, y = -0.25f });
    }

    private static void WriteSessionOnlyTrace(string path)
    {
        using var writer = new JsonlTraceWriter(path, runtime: "csharp", session: new { caseId = "managed" });
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

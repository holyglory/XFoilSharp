using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: PANGEN final S / panel-node output
// Secondary legacy source: tools/fortran-debug/json_trace.f PANGEN node tracing
// Role in port: Guards the reduced 12-panel PANGEN parity case without reopening broad solver traces.
// Differences: The managed test deduplicates the current Fortran windowed reference by node index before comparison because the reference artifact can contain repeated final-node records from the same run.
// Decision: Keep the focused case because it protects the proved PANGEN fix at the smallest useful whole-paneling boundary.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class PangenParityTests
{
    private const string CaseId = "n0012_re1e6_a0_p12_n9_pangen";
    private const string NewtonSplineCaseId = "n0012_re1e6_a0_p12_n9_pangen_spline";
    private const string NewtonRowsCaseId = "n0012_re1e6_a0_p12_n9_pangen_rows";
    private const string NewtonRowsWideCaseId = "n0012_re1e6_a0_p80_pangen_rows";
    private static readonly string[] NewtonRowFamilyCaseIds =
    {
        NewtonRowsCaseId,
        NewtonRowsWideCaseId,
        "n2412_re3e6_a3_p80_pangen_rows",
        "n4415_re6e6_a2_p80_pangen_rows"
    };

    [Fact]
    public void Naca0012_Re1e6_A0_FinalSNodes_MatchFortran()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);

        var selector = new TraceEventSelector(
            Kind: "pangen_snew_node",
            TagFilters: new Dictionary<string, object?>
            {
                ["stage"] = "final"
            });

        IReadOnlyDictionary<int, ParityTraceRecord> reference = LoadUniqueByIndex(referencePath, selector);
        IReadOnlyDictionary<int, ParityTraceRecord> managed = LoadUniqueByIndex(managedPath, selector);

        Assert.Equal(12, reference.Count);
        Assert.Equal(reference.Count, managed.Count);

        foreach (int index in reference.Keys.OrderBy(static value => value))
        {
            Assert.True(managed.ContainsKey(index), $"Managed final PANGEN S node {index} missing.");
            FortranParityAssert.AssertInputsThenOutputs(
                reference[index],
                managed[index],
                inputFields: new[]
                {
                    new FieldExpectation("data.stage"),
                    new FieldExpectation("data.iteration", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble)
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.value")
                },
                blockDescription: $"PANGEN final S node index={index}");
        }
    }

    [Fact]
    public void Naca0012_Re1e6_A0_FinalPanelNodes_MatchFortran()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);

        var selector = new TraceEventSelector("pangen_panel_node");
        IReadOnlyDictionary<int, ParityTraceRecord> reference = LoadUniqueByIndex(referencePath, selector);
        IReadOnlyDictionary<int, ParityTraceRecord> managed = LoadUniqueByIndex(managedPath, selector);

        Assert.Equal(12, reference.Count);
        Assert.Equal(reference.Count, managed.Count);

        foreach (int index in reference.Keys.OrderBy(static value => value))
        {
            Assert.True(managed.ContainsKey(index), $"Managed PANGEN panel node {index} missing.");
            FortranParityAssert.AssertInputsThenOutputs(
                reference[index],
                managed[index],
                inputFields: new[]
                {
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble)
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.x"),
                    new FieldExpectation("data.y"),
                    new FieldExpectation("data.arcLength"),
                    new FieldExpectation("data.xp"),
                    new FieldExpectation("data.yp")
                },
                blockDescription: $"PANGEN final panel node index={index}");
        }
    }

    [Fact]
    public void Naca0012_Re1e6_A0_NewtonIteration1Index12SplineEvalAccumulator_MatchesFortran()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(NewtonSplineCaseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(NewtonSplineCaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(NewtonSplineCaseId);

        ParityTraceRecord reference = SelectNewtonSplineEval(referencePath);
        ParityTraceRecord managed = SelectNewtonSplineEval(managedPath);

        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.routine"),
                new FieldExpectation("data.lowerIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.upperIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.parameter"),
                new FieldExpectation("data.ds"),
                new FieldExpectation("data.t")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.valueLow"),
                new FieldExpectation("data.valueHigh"),
                new FieldExpectation("data.derivativeLow"),
                new FieldExpectation("data.derivativeHigh"),
                new FieldExpectation("data.cx1"),
                new FieldExpectation("data.cx2"),
                new FieldExpectation("data.delta"),
                new FieldExpectation("data.factor1"),
                new FieldExpectation("data.factor2"),
                new FieldExpectation("data.product1"),
                new FieldExpectation("data.product2"),
                new FieldExpectation("data.accumulator"),
                new FieldExpectation("data.value")
            },
            blockDescription: "PANGEN Newton iteration=1 index=12 SEVAL lowerIndex=47");
    }

    [Fact]
    public void Naca0012_Re1e6_A0_NewtonRows_MatchFortran()
    {
        AssertNewtonRowsMatch(NewtonRowsCaseId);
    }

    [Fact]
    public void Naca0012_Re1e6_A0_P80_NewtonRows_MatchFortran()
    {
        AssertNewtonRowsMatch(NewtonRowsWideCaseId, minimumExpectedVectorCount: 1000);
    }

    [Fact]
    public void PangenNewtonRows_Family_MatchFortran()
    {
        foreach (string caseId in NewtonRowFamilyCaseIds)
        {
            AssertNewtonRowsMatch(caseId);
        }
    }

    private static void AssertNewtonRowsMatch(string caseId, int? minimumExpectedVectorCount = null)
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(caseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(caseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(caseId);

        var selector = new TraceEventSelector("pangen_newton_row");
        IReadOnlyDictionary<(int Iteration, int Index), ParityTraceRecord> reference = LoadUniqueByIterationAndIndex(referencePath, selector);
        IReadOnlyDictionary<(int Iteration, int Index), ParityTraceRecord> managed = LoadUniqueByIterationAndIndex(managedPath, selector);

        if (minimumExpectedVectorCount.HasValue)
        {
            Assert.True(
                reference.Count >= minimumExpectedVectorCount.Value,
                $"Focused PANGEN Newton row case '{caseId}' should provide at least {minimumExpectedVectorCount.Value} real vectors, found {reference.Count}.");
        }

        Assert.Equal(reference.Count, managed.Count);

        foreach ((int iteration, int index) in reference.Keys.OrderBy(static value => value.Iteration).ThenBy(static value => value.Index))
        {
            Assert.True(managed.ContainsKey((iteration, index)), $"Managed PANGEN Newton row iteration={iteration} index={index} missing.");
            FortranParityAssert.AssertInputsThenOutputs(
                reference[(iteration, index)],
                managed[(iteration, index)],
                inputFields: new[]
                {
                    new FieldExpectation("data.iteration", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.arcLength")
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.lower"),
                    new FieldExpectation("data.diagonal"),
                    new FieldExpectation("data.upper"),
                    new FieldExpectation("data.residual")
                },
                blockDescription: $"PANGEN Newton row iteration={iteration} index={index}");
        }
    }

    private static IReadOnlyDictionary<int, ParityTraceRecord> LoadUniqueByIndex(string path, TraceEventSelector selector)
    {
        var result = new Dictionary<int, ParityTraceRecord>();
        foreach (ParityTraceRecord record in ParityTraceLoader.ReadMatching(path, record => ParityTraceAligner.Matches(record, selector)))
        {
            if (!record.TryGetDataField("index", out JsonElement indexElement))
            {
                throw new InvalidDataException($"Trace record {record.Kind}/{record.Name ?? "*"} in {path} is missing data.index.");
            }

            int index = indexElement.GetInt32();
            result.TryAdd(index, record);
        }

        return result;
    }

    private static ParityTraceRecord SelectNewtonSplineEval(string path)
    {
        IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(path);
        int triggerIndex = records
            .Select((record, index) => (record, index))
            .Where(tuple =>
                tuple.record.Kind == "pangen_newton_state" &&
                TryGetIntDataField(tuple.record, "iteration") == 1 &&
                TryGetIntDataField(tuple.record, "index") == 12)
            .Last()
            .index;

        return records
            .Skip(triggerIndex + 1)
            .First(record =>
                record.Kind == "spline_eval" &&
                TryGetStringDataField(record, "routine") == "SEVAL" &&
                TryGetIntDataField(record, "lowerIndex") == 47);
    }

    private static int? TryGetIntDataField(ParityTraceRecord record, string fieldName)
    {
        return record.TryGetDataField(fieldName, out JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : null;
    }

    private static IReadOnlyDictionary<(int Iteration, int Index), ParityTraceRecord> LoadUniqueByIterationAndIndex(string path, TraceEventSelector selector)
    {
        var result = new Dictionary<(int Iteration, int Index), ParityTraceRecord>();
        foreach (ParityTraceRecord record in ParityTraceLoader.ReadMatching(path, record => ParityTraceAligner.Matches(record, selector)))
        {
            if (!record.TryGetDataField("iteration", out JsonElement iterationElement) || !iterationElement.TryGetInt32(out int iteration))
            {
                throw new InvalidDataException($"Trace record {record.Kind}/{record.Name ?? "*"} in {path} is missing data.iteration.");
            }

            if (!record.TryGetDataField("index", out JsonElement indexElement) || !indexElement.TryGetInt32(out int index))
            {
                throw new InvalidDataException($"Trace record {record.Kind}/{record.Name ?? "*"} in {path} is missing data.index.");
            }

            result.TryAdd((iteration, index), record);
        }

        return result;
    }

    private static string? TryGetStringDataField(ParityTraceRecord record, string fieldName)
    {
        return record.TryGetDataField(fieldName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Numerics;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: GAUSS
// Secondary legacy source: tools/fortran-debug/xsolve_debug.f :: GAUSS phase trace
// Role in port: Exercises the managed float GAUSS parity path against a standalone Fortran micro-driver instead of whole-solver traces.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for every traced GAUSS phase, not decimal summaries.
// Decision: Keep the micro-driver because it shrinks dense-solve parity checks to a sub-second kernel test.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class DenseLinearSystemFortranParityTests
{
    [Fact]
    public void FloatGaussBatch_BitwiseMatchesFortranGaussDriver()
    {
        IReadOnlyList<FortranGaussCase> cases = BuildCases();
        IReadOnlyList<IReadOnlyList<FortranGaussSnapshot>> fortranSnapshots = FortranGaussDriver.RunBatch(cases);
        IReadOnlyList<IReadOnlyList<FortranGaussSnapshot>> managedSnapshots = RunManagedBatch(cases);

        Assert.Equal(fortranSnapshots.Count, managedSnapshots.Count);

        for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            IReadOnlyList<FortranGaussSnapshot> expected = fortranSnapshots[caseIndex];
            IReadOnlyList<FortranGaussSnapshot> actual = managedSnapshots[caseIndex];
            Assert.Equal(expected.Count, actual.Count);

            for (int snapshotIndex = 0; snapshotIndex < expected.Count; snapshotIndex++)
            {
                AssertSnapshotEqual(expected[snapshotIndex], actual[snapshotIndex], caseIndex, snapshotIndex);
            }
        }
    }

    private static IReadOnlyList<FortranGaussCase> BuildCases()
    {
        var cases = new List<FortranGaussCase>
        {
            new(
                new float[,]
                {
                    { 0.125f, 1.0f, -0.5f, 0.25f },
                    { 3.0f, -0.25f, 0.75f, -1.5f },
                    { -0.625f, 0.5f, 2.25f, 0.125f },
                    { 1.5f, -0.75f, 0.25f, 0.875f }
                },
                new[] { 0.25f, -1.0f, 1.5f, 0.75f }),
            new(
                new float[,]
                {
                    { 1.0f, 0.0f, 0.0f, 0.0f },
                    { 0.0f, -213984.609375f, 238064.78125f, 4.608588218688965f },
                    { -0.0f, 283944.21875f, -172167.40625f, -1.4107953310012817f },
                    { 0.0f, 0.0f, 0.0f, 1.0f }
                },
                new[] { -0.0f, 0.7762913703918457f, -0.32335805892944336f, 0.0f })
        };

        var random = new Random(20260317);
        for (int caseIndex = 0; caseIndex < 1024; caseIndex++)
        {
            var matrix = new float[4, 4];
            var rhs = new float[4];

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    matrix[row, column] = ((float)random.NextDouble() * 6.0f) - 3.0f;
                }

                matrix[row, row] += row switch
                {
                    0 => 5.0f,
                    1 => -6.0f,
                    2 => 7.0f,
                    _ => -8.0f
                };

                rhs[row] = ((float)random.NextDouble() * 8.0f) - 4.0f;
            }

            cases.Add(new FortranGaussCase(matrix, rhs));
        }

        return cases;
    }

    private static IReadOnlyList<IReadOnlyList<FortranGaussSnapshot>> RunManagedBatch(IReadOnlyList<FortranGaussCase> cases)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-gauss-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "gauss-micro" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            var solver = new DenseLinearSystemSolver();
            foreach (FortranGaussCase @case in cases)
            {
                solver.Solve(CloneMatrix(@case.Matrix), (float[])@case.RightHandSide.Clone());
            }

            IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadMatching(
                tracePath,
                record => record.Kind == "gauss_state");

            var grouped = new List<IReadOnlyList<FortranGaussSnapshot>>(cases.Count);
            int recordIndex = 0;
            for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
            {
                var snapshots = new List<FortranGaussSnapshot>(14);
                for (int snapshotIndex = 0; snapshotIndex < 14; snapshotIndex++)
                {
                    snapshots.Add(ParseManagedSnapshot(records[recordIndex++]));
                }

                grouped.Add(snapshots);
            }

            return grouped;
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static FortranGaussSnapshot ParseManagedSnapshot(ParityTraceRecord record)
    {
        string phase = record.Data.GetProperty("phase").GetString() ?? string.Empty;
        int pivotIndex = record.Data.GetProperty("pivotIndex").GetInt32();
        int rowIndex = record.Data.GetProperty("rowIndex").GetInt32();

        var matrix = new float[4, 4];
        matrix[0, 0] = ReadTraceFloat(record, "row11");
        matrix[0, 1] = ReadTraceFloat(record, "row12");
        matrix[0, 2] = ReadTraceFloat(record, "row13");
        matrix[0, 3] = ReadTraceFloat(record, "row14");
        matrix[1, 0] = ReadTraceFloat(record, "row21");
        matrix[1, 1] = ReadTraceFloat(record, "row22");
        matrix[1, 2] = ReadTraceFloat(record, "row23");
        matrix[1, 3] = ReadTraceFloat(record, "row24");
        matrix[2, 0] = ReadTraceFloat(record, "row31");
        matrix[2, 1] = ReadTraceFloat(record, "row32");
        matrix[2, 2] = ReadTraceFloat(record, "row33");
        matrix[2, 3] = ReadTraceFloat(record, "row34");
        matrix[3, 0] = ReadTraceFloat(record, "row41");
        matrix[3, 1] = ReadTraceFloat(record, "row42");
        matrix[3, 2] = ReadTraceFloat(record, "row43");
        matrix[3, 3] = ReadTraceFloat(record, "row44");

        float[] rhs =
        {
            ReadTraceFloat(record, "rhs1"),
            ReadTraceFloat(record, "rhs2"),
            ReadTraceFloat(record, "rhs3"),
            ReadTraceFloat(record, "rhs4")
        };

        return new FortranGaussSnapshot(phase, pivotIndex, rowIndex, matrix, rhs);
    }

    private static float ReadTraceFloat(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? singleBits))
            {
                uint singleValue = uint.Parse(singleBits[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return BitConverter.Int32BitsToSingle(unchecked((int)singleValue));
            }

            if (bits.TryGetValue("f64", out string? doubleBits))
            {
                ulong doubleValue = ulong.Parse(doubleBits[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                double asDouble = BitConverter.Int64BitsToDouble(unchecked((long)doubleValue));
                return (float)asDouble;
            }
        }

        Assert.True(record.TryGetDataField(fieldName, out System.Text.Json.JsonElement value));
        return (float)value.GetDouble();
    }

    private static float[,] CloneMatrix(float[,] matrix)
    {
        var clone = new float[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                clone[row, column] = matrix[row, column];
            }
        }

        return clone;
    }

    private static void AssertSnapshotEqual(FortranGaussSnapshot expected, FortranGaussSnapshot actual, int caseIndex, int snapshotIndex)
    {
        Assert.Equal(expected.Phase, actual.Phase);
        Assert.Equal(expected.PivotIndex, actual.PivotIndex);
        Assert.Equal(expected.RowIndex, actual.RowIndex);

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                AssertBitsEqual(
                    expected.Matrix[row, column],
                    actual.Matrix[row, column],
                    $"case={caseIndex} snapshot={snapshotIndex} matrix[{row},{column}] phase={expected.Phase}");
            }
        }

        for (int row = 0; row < 4; row++)
        {
            AssertBitsEqual(
                expected.RightHandSide[row],
                actual.RightHandSide[row],
                $"case={caseIndex} snapshot={snapshotIndex} rhs[{row}] phase={expected.Phase}");
        }
    }

    private static void AssertBitsEqual(float expected, float actual, string context)
    {
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actual);
        Assert.True(
            expectedBits == actualBits,
            $"{context}: Fortran={FormatFloat(expected)} [0x{expectedBits:X8}] Managed={FormatFloat(actual)} [0x{actualBits:X8}]");
    }

    private static string FormatFloat(float value)
        => value.ToString("G9", CultureInfo.InvariantCulture);
}

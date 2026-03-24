using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using XFoil.IO.Models;

// Legacy audit:
// Primary legacy source: none
// Role in port: Managed importer for the legacy binary polar-dump file format emitted by XFoil/ISES workflows.
// Differences: No direct Fortran analogue exists because this code reconstructs the legacy unformatted record layout into typed DTOs rather than participating in the original record-writing path.
// Decision: Keep the managed importer because it is the right compatibility layer for existing dump files.
namespace XFoil.IO.Services;

public sealed class LegacyPolarDumpImporter
{
    // Legacy mapping: none; managed-only parser for the legacy binary polar-dump format.
    // Difference from legacy: The original runtime wrote these records, while the port reads them back into immutable DTOs with explicit validation.
    // Decision: Keep the managed importer because it is a compatibility feature, not a parity-execution path.
    public LegacyPolarDumpFile Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A polar dump path is required.", nameof(path));
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        var headerRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump is missing the header record.");
        var airfoilName = ReadFixedString(headerRecord, 0, 32);
        var sourceCode = ReadFixedString(headerRecord, 32, 8);
        var version = ReadSingle(headerRecord, 40);

        var parameterRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump is missing the parameter record.");
        var referenceMach = ReadSingle(parameterRecord, 0);
        var referenceReynoldsMillions = ReadSingle(parameterRecord, 4);
        var criticalAmplification = ReadSingle(parameterRecord, 8);

        var typeRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump is missing the sweep-type record.");
        var machVariationType = (LegacyMachVariationType)ReadInt32(typeRecord, 0);
        var reynoldsVariationType = (LegacyReynoldsVariationType)ReadInt32(typeRecord, 4);

        var indexRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump is missing the geometry-index record.");
        var iitot = ReadInt32(indexRecord, 0);
        var iletot = ReadInt32(indexRecord, 4);
        var itetot = ReadInt32(indexRecord, 8);
        var geometryCount = ReadInt32(indexRecord, 12);
        var isIsesPolar = iitot != 0;
        var isMachSweep = referenceMach == 0d && isIsesPolar;

        var geometryRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump is missing the geometry record.");
        var geometry = ReadGeometry(geometryRecord, geometryCount);

        var operatingPoints = new List<LegacyPolarDumpOperatingPoint>();
        // Legacy block: Managed-only pass over the unformatted record stream to rebuild each dumped operating point and its two side records.
        // Difference: The original runtime produced these records sequentially, while the importer validates and reconstructs them into nested DTOs.
        // Decision: Keep the managed parser because it makes old dump files reusable.
        while (true)
        {
            var forceRecord = ReadRecord(reader);
            if (forceRecord is null)
            {
                break;
            }

            var forceValues = ReadSingles(forceRecord);
            double alpha;
            double cl;
            double cd;
            double cdi;
            double cm;
            double topTransition;
            double bottomTransition;
            double mach;
            int[] sideCounts;
            int[] leadingEdges;
            int[] trailingEdges;

            if (isIsesPolar)
            {
                if (isMachSweep)
                {
                    if (forceValues.Length < 8)
                    {
                        throw new InvalidOperationException("ISES Mach-sweep dump record is incomplete.");
                    }

                    alpha = forceValues[0];
                    cl = forceValues[1];
                    cd = forceValues[2];
                    cdi = forceValues[3];
                    cm = forceValues[4];
                    topTransition = forceValues[5];
                    bottomTransition = forceValues[6];
                    mach = forceValues[7];
                }
                else
                {
                    if (forceValues.Length < 7)
                    {
                        throw new InvalidOperationException("ISES dump record is incomplete.");
                    }

                    alpha = forceValues[0];
                    cl = forceValues[1];
                    cd = forceValues[2];
                    cdi = forceValues[3];
                    cm = forceValues[4];
                    topTransition = forceValues[5];
                    bottomTransition = forceValues[6];
                    mach = ResolveMach(referenceMach, machVariationType, cl);
                }

                sideCounts = new[] { iitot, iitot };
                leadingEdges = new[] { iletot, iletot };
                trailingEdges = new[] { itetot, itetot };
            }
            else
            {
                if (forceValues.Length < 7)
                {
                    throw new InvalidOperationException("XFOIL dump force record is incomplete.");
                }

                alpha = forceValues[0];
                cl = forceValues[1];
                cd = forceValues[2];
                cdi = forceValues[3];
                cm = forceValues[4];
                topTransition = forceValues[5];
                bottomTransition = forceValues[6];
                mach = ResolveMach(referenceMach, machVariationType, cl);

                var sideIndexRecord = ReadRecord(reader) ?? throw new InvalidOperationException("XFOIL dump is missing the side-index record.");
                sideCounts = new[] { ReadInt32(sideIndexRecord, 0), ReadInt32(sideIndexRecord, 4) };
                leadingEdges = new[] { 1, 1 };
                trailingEdges = new[] { ReadInt32(sideIndexRecord, 8), ReadInt32(sideIndexRecord, 12) };
            }

            var sides = new List<LegacyPolarDumpSide>(2);
            for (var sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                var sideRecord = ReadRecord(reader) ?? throw new InvalidOperationException("Polar dump ended while reading side data.");
                var samples = ReadSideSamples(sideRecord, sideCounts[sideIndex]);
                sides.Add(new LegacyPolarDumpSide(sideIndex + 1, leadingEdges[sideIndex], trailingEdges[sideIndex], samples));
            }

            operatingPoints.Add(new LegacyPolarDumpOperatingPoint(alpha, cl, cd, cdi, cm, topTransition, bottomTransition, mach, sides));
        }

        return new LegacyPolarDumpFile(
            airfoilName,
            sourceCode,
            version,
            referenceMach,
            referenceReynoldsMillions * 1_000_000d,
            criticalAmplification,
            machVariationType,
            reynoldsVariationType,
            isIsesPolar,
            isMachSweep,
            geometry,
            operatingPoints);
    }

    // Legacy mapping: none; managed-only geometry-record parser for the legacy dump format.
    // Difference from legacy: The original code wrote this unformatted record; the port decodes it into typed points.
    // Decision: Keep the managed parser helper.
    private static IReadOnlyList<LegacyPolarDumpGeometryPoint> ReadGeometry(byte[] record, int pointCount)
    {
        var floats = ReadSingles(record);
        if (floats.Length < pointCount * 2)
        {
            throw new InvalidOperationException("Polar dump geometry record is incomplete.");
        }

        var geometry = new List<LegacyPolarDumpGeometryPoint>(pointCount);
        // Legacy block: Managed-only decode of one geometry record into point objects.
        // Difference: The importer materializes the binary floats into DTOs rather than leaving them in raw arrays.
        // Decision: Keep the managed loop because it is the intended import representation.
        for (var index = 0; index < pointCount; index++)
        {
            geometry.Add(new LegacyPolarDumpGeometryPoint(floats[2 * index], floats[(2 * index) + 1]));
        }

        return geometry;
    }

    // Legacy mapping: none; managed-only side-sample parser for the legacy dump format.
    // Difference from legacy: The original code wrote these binary side rows, while the importer decodes them into named sample objects.
    // Decision: Keep the managed parser helper.
    private static IReadOnlyList<LegacyPolarDumpSideSample> ReadSideSamples(byte[] record, int sampleCount)
    {
        var floats = ReadSingles(record);
        if (floats.Length < sampleCount * 6)
        {
            throw new InvalidOperationException("Polar dump side-data record is incomplete.");
        }

        var samples = new List<LegacyPolarDumpSideSample>(sampleCount);
        // Legacy block: Managed-only decode of one side-data record into sample DTOs.
        // Difference: The importer materializes the binary record instead of leaving it in packed float arrays.
        // Decision: Keep the managed loop because it improves usability of imported dump data.
        for (var index = 0; index < sampleCount; index++)
        {
            var offset = index * 6;
            samples.Add(new LegacyPolarDumpSideSample(
                floats[offset],
                floats[offset + 1],
                floats[offset + 2],
                floats[offset + 3],
                floats[offset + 4],
                floats[offset + 5]));
        }

        return samples;
    }

    // Legacy mapping: none; managed-only interpretation of the Mach-variation mode encoded in the dump header.
    // Difference from legacy: The original runtime generated Mach values from current session state, while the importer reconstructs them from stored metadata.
    // Decision: Keep the managed helper because it makes dump imports self-contained.
    private static double ResolveMach(double referenceMach, LegacyMachVariationType variationType, double cl)
    {
        var safeCl = Math.Max(cl, 0.001d);
        return variationType switch
        {
            LegacyMachVariationType.Fixed => referenceMach,
            LegacyMachVariationType.InverseSqrtCl => referenceMach / Math.Sqrt(safeCl),
            LegacyMachVariationType.InverseCl => referenceMach / safeCl,
            _ => referenceMach,
        };
    }

    // Legacy mapping: none; managed-only unformatted-record decoding helper.
    // Difference from legacy: The original runtime wrote binary floats directly, while the importer reconstructs them into a managed array.
    // Decision: Keep the managed helper because it localizes the binary-layout logic.
    private static float[] ReadSingles(byte[] record)
    {
        if (record.Length % 4 != 0)
        {
            throw new InvalidOperationException("Fortran record byte count is not aligned to 4-byte floats.");
        }

        var values = new float[record.Length / 4];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(index * 4, 4)));
        }

        return values;
    }

    // Legacy mapping: none; managed-only binary-layout helper.
    // Difference from legacy: This helper decodes one `INT32` field from a record produced by the old runtime.
    // Decision: Keep the managed helper.
    private static int ReadInt32(byte[] record, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(offset, 4));
    }

    // Legacy mapping: none; managed-only binary-layout helper.
    // Difference from legacy: This helper decodes one single-precision value from the record stream.
    // Decision: Keep the managed helper.
    private static float ReadSingle(byte[] record, int offset)
    {
        return BitConverter.Int32BitsToSingle(ReadInt32(record, offset));
    }

    // Legacy mapping: none; managed-only binary-layout helper.
    // Difference from legacy: Fixed-width strings are reconstructed explicitly during import instead of being read into legacy character buffers.
    // Decision: Keep the managed helper because it isolates the old record-layout rules.
    private static string ReadFixedString(byte[] record, int offset, int length)
    {
        return Encoding.ASCII.GetString(record, offset, length).TrimEnd('\0', ' ');
    }

    // Legacy mapping: none; managed-only Fortran-record reader.
    // Difference from legacy: The importer must read record-length sentinels explicitly, while the original runtime wrote them implicitly through unformatted I/O.
    // Decision: Keep the managed helper because it is the core compatibility shim for the binary format.
    private static byte[]? ReadRecord(BinaryReader reader)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            return null;
        }

        var leadingLength = reader.ReadInt32();
        var data = reader.ReadBytes(leadingLength);
        if (data.Length != leadingLength)
        {
            throw new InvalidOperationException("Unexpected end of file while reading a Fortran record.");
        }

        var trailingLength = reader.ReadInt32();
        if (trailingLength != leadingLength)
        {
            throw new InvalidOperationException("Fortran record length markers do not match.");
        }

        return data;
    }
}

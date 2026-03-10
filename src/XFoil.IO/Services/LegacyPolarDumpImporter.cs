using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using XFoil.IO.Models;

namespace XFoil.IO.Services;

public sealed class LegacyPolarDumpImporter
{
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

    private static IReadOnlyList<LegacyPolarDumpGeometryPoint> ReadGeometry(byte[] record, int pointCount)
    {
        var floats = ReadSingles(record);
        if (floats.Length < pointCount * 2)
        {
            throw new InvalidOperationException("Polar dump geometry record is incomplete.");
        }

        var geometry = new List<LegacyPolarDumpGeometryPoint>(pointCount);
        for (var index = 0; index < pointCount; index++)
        {
            geometry.Add(new LegacyPolarDumpGeometryPoint(floats[2 * index], floats[(2 * index) + 1]));
        }

        return geometry;
    }

    private static IReadOnlyList<LegacyPolarDumpSideSample> ReadSideSamples(byte[] record, int sampleCount)
    {
        var floats = ReadSingles(record);
        if (floats.Length < sampleCount * 6)
        {
            throw new InvalidOperationException("Polar dump side-data record is incomplete.");
        }

        var samples = new List<LegacyPolarDumpSideSample>(sampleCount);
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

    private static int ReadInt32(byte[] record, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(offset, 4));
    }

    private static float ReadSingle(byte[] record, int offset)
    {
        return BitConverter.Int32BitsToSingle(ReadInt32(record, offset));
    }

    private static string ReadFixedString(byte[] record, int offset, int length)
    {
        return Encoding.ASCII.GetString(record, offset, length).TrimEnd('\0', ' ');
    }

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

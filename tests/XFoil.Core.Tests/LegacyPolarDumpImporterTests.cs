using System.Buffers.Binary;
using System.Text;
using XFoil.IO.Models;
using XFoil.IO.Services;

namespace XFoil.Core.Tests;

public sealed class LegacyPolarDumpImporterTests
{
    [Fact]
    public void Import_ParsesSyntheticXFoilDump_AndArchiveWriterEmitsArtifacts()
    {
        var importer = new LegacyPolarDumpImporter();
        var writer = new LegacyPolarDumpArchiveWriter();
        var root = Path.Combine(Path.GetTempPath(), $"xfoil-dump-{Guid.NewGuid():N}");
        var dumpPath = Path.Combine(root, "synthetic.dump");
        var summaryPath = Path.Combine(root, "synthetic-summary.csv");

        try
        {
            Directory.CreateDirectory(root);
            WriteSyntheticXFoilDump(dumpPath);

            var dump = importer.Import(dumpPath);

            Assert.Equal("Synthetic Dump", dump.AirfoilName);
            Assert.Equal("XFOIL", dump.SourceCode);
            Assert.Equal(6.99d, dump.Version, 2);
            Assert.False(dump.IsIsesPolar);
            Assert.False(dump.IsMachSweep);
            Assert.Equal(0.15d, dump.ReferenceMachNumber, 6);
            Assert.Equal(200_000d, dump.ReferenceReynoldsNumber, 0);
            Assert.Equal(3, dump.Geometry.Count);
            Assert.Single(dump.OperatingPoints);
            Assert.Equal(2d, dump.OperatingPoints[0].AngleOfAttackDegrees, 6);
            Assert.Equal(0.5d, dump.OperatingPoints[0].LiftCoefficient, 6);
            Assert.Equal(2, dump.OperatingPoints[0].Sides[0].Samples.Count);
            Assert.Equal(2, dump.OperatingPoints[0].Sides[1].Samples.Count);

            var export = writer.Export(summaryPath, dump);

            Assert.True(File.Exists(export.SummaryPath));
            Assert.True(File.Exists(export.GeometryPath));
            Assert.Equal(2, export.SidePaths.Count);
            Assert.All(export.SidePaths, path => Assert.True(File.Exists(path)));

            var summary = File.ReadAllText(export.SummaryPath);
            var geometry = File.ReadAllText(export.GeometryPath);
            var side = File.ReadAllText(export.SidePaths[0]);
            Assert.Contains("# XFoil.CSharp Polar Dump Export", summary);
            Assert.Contains("PointIndex,AngleOfAttackDegrees,LiftCoefficient", summary);
            Assert.Contains("PointIndex,X,Y", geometry);
            Assert.Contains("SampleIndex,X,PressureCoefficient", side);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteSyntheticXFoilDump(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

        WriteRecord(writer, bytes =>
        {
            WriteFixedString(bytes, "Synthetic Dump", 32);
            WriteFixedString(bytes, "XFOIL", 8);
            WriteSingle(bytes, 6.99f);
        });

        WriteRecord(writer, bytes =>
        {
            WriteSingle(bytes, 0.15f);
            WriteSingle(bytes, 0.2f);
            WriteSingle(bytes, 9.0f);
        });

        WriteRecord(writer, bytes =>
        {
            WriteInt32(bytes, 1);
            WriteInt32(bytes, 1);
        });

        WriteRecord(writer, bytes =>
        {
            WriteInt32(bytes, 0);
            WriteInt32(bytes, 0);
            WriteInt32(bytes, 0);
            WriteInt32(bytes, 3);
        });

        WriteRecord(writer, bytes =>
        {
            WriteSingle(bytes, 1.0f); WriteSingle(bytes, 0.0f);
            WriteSingle(bytes, 0.5f); WriteSingle(bytes, 0.1f);
            WriteSingle(bytes, 0.0f); WriteSingle(bytes, 0.0f);
        });

        WriteRecord(writer, bytes =>
        {
            WriteSingle(bytes, 2.0f);
            WriteSingle(bytes, 0.5f);
            WriteSingle(bytes, 0.02f);
            WriteSingle(bytes, 0.0f);
            WriteSingle(bytes, -0.03f);
            WriteSingle(bytes, 0.8f);
            WriteSingle(bytes, 0.9f);
        });

        WriteRecord(writer, bytes =>
        {
            WriteInt32(bytes, 2);
            WriteInt32(bytes, 2);
            WriteInt32(bytes, 1);
            WriteInt32(bytes, 1);
        });

        WriteRecord(writer, bytes =>
        {
            WriteSideSample(bytes, 0.1f, -0.5f, 0.001f, 0.002f, 0.003f, 0.004f);
            WriteSideSample(bytes, 0.3f, -0.4f, 0.005f, 0.006f, 0.007f, 0.008f);
        });

        WriteRecord(writer, bytes =>
        {
            WriteSideSample(bytes, 0.1f, -0.6f, 0.011f, 0.012f, 0.013f, 0.014f);
            WriteSideSample(bytes, 0.4f, -0.2f, 0.015f, 0.016f, 0.017f, 0.018f);
        });
    }

    private static void WriteSideSample(List<byte> bytes, float x, float cp, float theta, float dstar, float cf, float ctau)
    {
        WriteSingle(bytes, x);
        WriteSingle(bytes, cp);
        WriteSingle(bytes, theta);
        WriteSingle(bytes, dstar);
        WriteSingle(bytes, cf);
        WriteSingle(bytes, ctau);
    }

    private static void WriteRecord(BinaryWriter writer, Action<List<byte>> fill)
    {
        var bytes = new List<byte>();
        fill(bytes);
        writer.Write(bytes.Count);
        writer.Write(bytes.ToArray());
        writer.Write(bytes.Count);
    }

    private static void WriteInt32(List<byte> bytes, int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void WriteSingle(List<byte> bytes, float value)
    {
        WriteInt32(bytes, BitConverter.SingleToInt32Bits(value));
    }

    private static void WriteFixedString(List<byte> bytes, string value, int width)
    {
        var buffer = new byte[width];
        var encoded = Encoding.ASCII.GetBytes(value);
        Array.Copy(encoded, buffer, Math.Min(encoded.Length, buffer.Length));
        bytes.AddRange(buffer);
    }
}

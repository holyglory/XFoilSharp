namespace XFoil.Solver.Numerics;

/// <summary>
/// ThreadStatic scratch buffers for the hot Newton-solve path. Preallocated
/// once per thread so the per-station / per-iteration 4x4 solves do not hit
/// the GC. Callers are responsible for re-populating the buffers before use;
/// no cross-call state is preserved.
/// </summary>
internal static class SolverBuffers
{
    [ThreadStatic] private static double[,]? _matrix4x4Double;
    [ThreadStatic] private static double[]? _vector4Double;
    [ThreadStatic] private static double[,]? _matrix4x4DoubleSecondary;
    [ThreadStatic] private static double[]? _vector4DoubleSecondary;
    [ThreadStatic] private static float[,]? _matrix4x4Float;
    [ThreadStatic] private static float[]? _vector4Float;

    [ThreadStatic] private static double[,]? _denseScratchMatrixDouble;
    [ThreadStatic] private static double[]? _denseScratchVectorDouble;
    [ThreadStatic] private static float[,]? _denseScratchMatrixFloat;
    [ThreadStatic] private static float[]? _denseScratchVectorFloat;

    internal static double[,] Matrix4x4Double => _matrix4x4Double ??= new double[4, 4];
    internal static double[] Vector4Double => _vector4Double ??= new double[4];
    internal static double[,] Matrix4x4DoubleSecondary => _matrix4x4DoubleSecondary ??= new double[4, 4];
    internal static double[] Vector4DoubleSecondary => _vector4DoubleSecondary ??= new double[4];
    internal static float[,] Matrix4x4Float => _matrix4x4Float ??= new float[4, 4];
    internal static float[] Vector4Float => _vector4Float ??= new float[4];

    internal static double[,] DenseScratchMatrixDouble(int rowCount, int columnCount)
    {
        var buffer = _denseScratchMatrixDouble;
        if (buffer is null
            || buffer.GetLength(0) < rowCount
            || buffer.GetLength(1) < columnCount)
        {
            buffer = new double[rowCount, columnCount];
            _denseScratchMatrixDouble = buffer;
        }
        return buffer;
    }

    internal static double[] DenseScratchVectorDouble(int rowCount)
    {
        var buffer = _denseScratchVectorDouble;
        if (buffer is null || buffer.Length < rowCount)
        {
            buffer = new double[rowCount];
            _denseScratchVectorDouble = buffer;
        }
        return buffer;
    }

    internal static float[,] DenseScratchMatrixFloat(int rowCount, int columnCount)
    {
        var buffer = _denseScratchMatrixFloat;
        if (buffer is null
            || buffer.GetLength(0) < rowCount
            || buffer.GetLength(1) < columnCount)
        {
            buffer = new float[rowCount, columnCount];
            _denseScratchMatrixFloat = buffer;
        }
        return buffer;
    }

    internal static float[] DenseScratchVectorFloat(int rowCount)
    {
        var buffer = _denseScratchVectorFloat;
        if (buffer is null || buffer.Length < rowCount)
        {
            buffer = new float[rowCount];
            _denseScratchVectorFloat = buffer;
        }
        return buffer;
    }
}

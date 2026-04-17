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

    // Per-Newton-iter scratch sized by station count (grow-only).
    [ThreadStatic] private static double[,]? _usavScratch;
    // Per-iter coupling arrays in ViscousNewtonUpdater (uNew/uAc/qNew/qAc, etc.).
    [ThreadStatic] private static double[]? _couplingVector1;
    [ThreadStatic] private static double[]? _couplingVector2;
    [ThreadStatic] private static double[]? _couplingVector3;
    [ThreadStatic] private static double[]? _couplingVector4;
    [ThreadStatic] private static double[,]? _couplingMatrix2D;
    [ThreadStatic] private static double[,]? _couplingMatrix2DSecondary;

    // Trust-region snapshot buffers for ApplyTrustRegionUpdate rollback.
    [ThreadStatic] private static double[,]? _snapThet;
    [ThreadStatic] private static double[,]? _snapDstr;
    [ThreadStatic] private static double[,]? _snapCtau;
    [ThreadStatic] private static double[,]? _snapUedg;
    [ThreadStatic] private static double[,]? _snapMass;

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

    /// <summary>
    /// [stationCount, 2] scratch for ComputePredictedEdgeVelocities (usav).
    /// Zero-initialized on every call since the algorithm expects fresh state.
    /// </summary>
    internal static double[,] UsavScratch(int stationCount)
    {
        var buffer = _usavScratch;
        if (buffer is null
            || buffer.GetLength(0) < stationCount
            || buffer.GetLength(1) < 2)
        {
            buffer = new double[stationCount, 2];
            _usavScratch = buffer;
        }
        else
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
        return buffer;
    }

    internal static double[] CouplingVector1(int count) => EnsureVector(ref _couplingVector1, count);
    internal static double[] CouplingVector2(int count) => EnsureVector(ref _couplingVector2, count);
    internal static double[] CouplingVector3(int count) => EnsureVector(ref _couplingVector3, count);
    internal static double[] CouplingVector4(int count) => EnsureVector(ref _couplingVector4, count);

    internal static double[,] CouplingMatrix2D(int rows, int cols) => EnsureMatrix(ref _couplingMatrix2D, rows, cols);
    internal static double[,] CouplingMatrix2DSecondary(int rows, int cols) => EnsureMatrix(ref _couplingMatrix2DSecondary, rows, cols);

    /// <summary>
    /// Returns a snapshot buffer sized to match <paramref name="source"/> and
    /// copies its contents. Used by trust-region rollback so every iteration
    /// avoids `(double[,])arr.Clone()` heap allocation.
    /// </summary>
    internal static double[,] SnapshotThet(double[,] source) => SnapshotInto(ref _snapThet, source);
    internal static double[,] SnapshotDstr(double[,] source) => SnapshotInto(ref _snapDstr, source);
    internal static double[,] SnapshotCtau(double[,] source) => SnapshotInto(ref _snapCtau, source);
    internal static double[,] SnapshotUedg(double[,] source) => SnapshotInto(ref _snapUedg, source);
    internal static double[,] SnapshotMass(double[,] source) => SnapshotInto(ref _snapMass, source);

    private static double[,] SnapshotInto(ref double[,]? slot, double[,] source)
    {
        int rows = source.GetLength(0);
        int cols = source.GetLength(1);
        var buffer = slot;
        if (buffer is null
            || buffer.GetLength(0) != rows
            || buffer.GetLength(1) != cols)
        {
            buffer = new double[rows, cols];
            slot = buffer;
        }
        Array.Copy(source, buffer, source.Length);
        return buffer;
    }

    private static double[,] EnsureMatrix(ref double[,]? slot, int rows, int cols)
    {
        var buffer = slot;
        if (buffer is null
            || buffer.GetLength(0) < rows
            || buffer.GetLength(1) < cols)
        {
            buffer = new double[rows, cols];
            slot = buffer;
        }
        else
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
        return buffer;
    }

    private static double[] EnsureVector(ref double[]? slot, int count)
    {
        var buffer = slot;
        if (buffer is null || buffer.Length < count)
        {
            buffer = new double[count];
            slot = buffer;
        }
        else
        {
            Array.Clear(buffer, 0, count);
        }
        return buffer;
    }
}

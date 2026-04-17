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

    // StreamfunctionInfluenceCalculator per-field-node scratch (dzdg/dzdm/
    // dqdg/dqdm). Called ~(n+1) times per inviscid assembly; each call
    // formerly allocated 4 fresh float[n] arrays. Pooled here as zero-
    // cleared ThreadStatic buffers.
    [ThreadStatic] private static float[]? _sfDzdg;
    [ThreadStatic] private static float[]? _sfDzdm;
    [ThreadStatic] private static float[]? _sfDqdg;
    [ThreadStatic] private static float[]? _sfDqdm;

    // Panel-sized double[] scratch slots for per-Newton-iter reuse inside
    // BuildViscousPanelSpeeds / ConvertUedgToSpeeds / Compute*CL/CM. A Newton
    // iteration may have up to ~5 of these alive concurrently (preStmoveQvis,
    // currentSpeeds, qvis, cp_CL, cp_CM, gamma), so we give each callsite its
    // own dedicated slot.
    [ThreadStatic] private static double[]? _panelScratch1;
    [ThreadStatic] private static double[]? _panelScratch2;
    [ThreadStatic] private static double[]? _panelScratch3;
    [ThreadStatic] private static double[]? _panelScratch4;
    [ThreadStatic] private static double[]? _panelScratch5;
    [ThreadStatic] private static double[]? _panelScratch6;

    // InfluenceMatrixBuilder.CreateLegacyWakeSolveContext — per-case float[size,size]
    // (~103KB LOH) + int[size] pivot clone.
    [ThreadStatic] private static float[,]? _legacyWakeLuFactors;
    [ThreadStatic] private static int[]? _legacyWakePivots;

    // LinearVortexInviscidSolver.SolveBasisRightHandSides — 2×double[systemSize]
    // + 2×float[systemSize] per inviscid solve (once per AnalyzeViscous).
    [ThreadStatic] private static double[]? _basisRhs0;
    [ThreadStatic] private static double[]? _basisRhs1;
    [ThreadStatic] private static float[]? _basisRhs0Single;
    [ThreadStatic] private static float[]? _basisRhs1Single;
    // LinearVortexInviscidSolver.IntegratePressureForces CP arrays.
    [ThreadStatic] private static double[]? _cpInviscid;
    [ThreadStatic] private static double[]? _cpAlpha;
    [ThreadStatic] private static double[]? _cpM2;

    // InfluenceMatrixBuilder.BuildAnalyticalDIJ main dij array. Per-case LOH
    // allocation (~460KB for typical 240-panel systems); pooling eliminates
    // the dominant remaining LOH contribution.
    [ThreadStatic] private static double[,]? _dijScratch;
    // wakeSurfaceInfluence = new double[n, nWake] — smaller but also per case.
    [ThreadStatic] private static double[,]? _wakeSurfaceInfluenceScratch;

    // InfluenceMatrixBuilder.BuildInfluenceMatrix wake-column RHS + single-prec
    // variant. One allocation each per case for rhs; rhsSingle is per wake
    // column (×~150) so the single-prec slot sees heavy reuse per case.
    [ThreadStatic] private static double[]? _wakeDijRhs;
    [ThreadStatic] private static float[]? _wakeDijRhsSingle;

    // InfluenceMatrixBuilder.ComputeWakeSourceSensitivitiesAt* output buffers.
    // These are the `out double[] dzdm, out double[] dqdm` arrays the wake
    // kernel returns to the caller. Each call currently allocates two fresh
    // double[nWake] arrays; called ~n times per case = ~240 × 5000 cases in
    // a sweep. The internal float[] scratch arrays used by the single-prec
    // variant are pooled alongside.
    [ThreadStatic] private static double[]? _wakeSrcDzdmDouble;
    [ThreadStatic] private static double[]? _wakeSrcDqdmDouble;
    [ThreadStatic] private static float[]? _wakeSrcDzdmSingle;
    [ThreadStatic] private static float[]? _wakeSrcDqdmSingle;

    // TRDIF (transition interval) per-call scratch arrays. 8× double[5] per
    // transition station per Newton iter; pooled as ThreadStatic to avoid GC.
    [ThreadStatic] private static double[]? _trdifTt1;
    [ThreadStatic] private static double[]? _trdifTt2;
    [ThreadStatic] private static double[]? _trdifDt1;
    [ThreadStatic] private static double[]? _trdifDt2;
    [ThreadStatic] private static double[]? _trdifUt1;
    [ThreadStatic] private static double[]? _trdifUt2;
    [ThreadStatic] private static double[]? _trdifSt1;
    [ThreadStatic] private static double[]? _trdifSt2;

    // BlockTridiagonalSolver parity float scratch (VA, VB, VM, VDEL, VZ).
    [ThreadStatic] private static float[,,]? _btVaFloat;
    [ThreadStatic] private static float[,,]? _btVbFloat;
    [ThreadStatic] private static float[,,]? _btVmFloat;
    [ThreadStatic] private static float[,,]? _btVdelFloat;
    [ThreadStatic] private static float[,]? _btVzFloat;

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

    internal static float[,] LegacyWakeLuFactors(int size) => EnsureFloat2D(ref _legacyWakeLuFactors, size, size);
    internal static int[] LegacyWakePivots(int size)
    {
        var buffer = _legacyWakePivots;
        if (buffer is null || buffer.Length < size)
        {
            int n = buffer is null ? size : Math.Max(buffer.Length, size);
            buffer = new int[n];
            _legacyWakePivots = buffer;
        }
        return buffer;
    }

    internal static double[] BasisRhs0(int n) => EnsureVector(ref _basisRhs0, n);
    internal static double[] BasisRhs1(int n) => EnsureVector(ref _basisRhs1, n);
    internal static float[] BasisRhs0Single(int n) => EnsureFloatVector(ref _basisRhs0Single, n);
    internal static float[] BasisRhs1Single(int n) => EnsureFloatVector(ref _basisRhs1Single, n);
    internal static double[] CpInviscid(int n) => EnsureVector(ref _cpInviscid, n);
    internal static double[] CpAlpha(int n) => EnsureVector(ref _cpAlpha, n);
    internal static double[] CpM2(int n) => EnsureVector(ref _cpM2, n);

    internal static float[] SfDzdg(int n) => EnsureFloatVector(ref _sfDzdg, n);
    internal static float[] SfDzdm(int n) => EnsureFloatVector(ref _sfDzdm, n);
    internal static float[] SfDqdg(int n) => EnsureFloatVector(ref _sfDqdg, n);
    internal static float[] SfDqdm(int n) => EnsureFloatVector(ref _sfDqdm, n);

    private static float[] EnsureFloatVector(ref float[]? slot, int count)
    {
        var buffer = slot;
        if (buffer is null || buffer.Length < count)
        {
            buffer = new float[count];
            slot = buffer;
        }
        else
        {
            Array.Clear(buffer, 0, count);
        }
        return buffer;
    }

    internal static double[] PanelScratch1(int n) => EnsureVector(ref _panelScratch1, n);
    internal static double[] PanelScratch2(int n) => EnsureVector(ref _panelScratch2, n);
    internal static double[] PanelScratch3(int n) => EnsureVector(ref _panelScratch3, n);
    internal static double[] PanelScratch4(int n) => EnsureVector(ref _panelScratch4, n);
    internal static double[] PanelScratch5(int n) => EnsureVector(ref _panelScratch5, n);
    internal static double[] PanelScratch6(int n) => EnsureVector(ref _panelScratch6, n);

    internal static double[] WakeDijRhs(int n) => EnsureVector(ref _wakeDijRhs, n);
    internal static float[] WakeDijRhsSingle(int n) => EnsureFloatVector(ref _wakeDijRhsSingle, n);
    internal static double[] WakeSrcDzdmDouble(int n) => EnsureVector(ref _wakeSrcDzdmDouble, n);
    internal static double[] WakeSrcDqdmDouble(int n) => EnsureVector(ref _wakeSrcDqdmDouble, n);
    internal static float[] WakeSrcDzdmSingle(int n) => EnsureFloatVector(ref _wakeSrcDzdmSingle, n);
    internal static float[] WakeSrcDqdmSingle(int n) => EnsureFloatVector(ref _wakeSrcDqdmSingle, n);

    internal static double[,] DijScratch(int rows, int cols)
    {
        var buffer = _dijScratch;
        if (buffer is null
            || buffer.GetLength(0) < rows
            || buffer.GetLength(1) < cols)
        {
            int nr = buffer is null ? rows : Math.Max(buffer.GetLength(0), rows);
            int nc = buffer is null ? cols : Math.Max(buffer.GetLength(1), cols);
            buffer = new double[nr, nc];
            _dijScratch = buffer;
        }
        else
        {
            // The caller writes every cell it cares about, but the array may
            // have stale rows/cols beyond the current (n, totalSize). Zero
            // those to be safe — clearing the full buffer is cheap relative
            // to the N^2 work that follows.
            Array.Clear(buffer, 0, buffer.Length);
        }
        return buffer;
    }

    internal static double[,] WakeSurfaceInfluenceScratch(int rows, int cols)
    {
        var buffer = _wakeSurfaceInfluenceScratch;
        if (buffer is null
            || buffer.GetLength(0) < rows
            || buffer.GetLength(1) < cols)
        {
            int nr = buffer is null ? rows : Math.Max(buffer.GetLength(0), rows);
            int nc = buffer is null ? cols : Math.Max(buffer.GetLength(1), cols);
            buffer = new double[nr, nc];
            _wakeSurfaceInfluenceScratch = buffer;
        }
        else
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
        return buffer;
    }

    // TRDIF per-call scratch accessors (all are size 5, zero-cleared on reuse).
    internal static double[] TrdifTt1 => _trdifTt1 ??= new double[5];
    internal static double[] TrdifTt2 => _trdifTt2 ??= new double[5];
    internal static double[] TrdifDt1 => _trdifDt1 ??= new double[5];
    internal static double[] TrdifDt2 => _trdifDt2 ??= new double[5];
    internal static double[] TrdifUt1 => _trdifUt1 ??= new double[5];
    internal static double[] TrdifUt2 => _trdifUt2 ??= new double[5];
    internal static double[] TrdifSt1 => _trdifSt1 ??= new double[5];
    internal static double[] TrdifSt2 => _trdifSt2 ??= new double[5];

    internal static double[,] DenseScratchMatrixDouble(int rowCount, int columnCount)
    {
        var buffer = _denseScratchMatrixDouble;
        if (buffer is null
            || buffer.GetLength(0) < rowCount
            || buffer.GetLength(1) < columnCount)
        {
            int nr = buffer is null ? rowCount : Math.Max(buffer.GetLength(0), rowCount);
            int nc = buffer is null ? columnCount : Math.Max(buffer.GetLength(1), columnCount);
            buffer = new double[nr, nc];
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
            int nr = buffer is null ? rowCount : Math.Max(buffer.GetLength(0), rowCount);
            int nc = buffer is null ? columnCount : Math.Max(buffer.GetLength(1), columnCount);
            buffer = new float[nr, nc];
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
            int nr = buffer is null ? stationCount : Math.Max(buffer.GetLength(0), stationCount);
            int nc = buffer is null ? 2 : Math.Max(buffer.GetLength(1), 2);
            buffer = new double[nr, nc];
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

    internal static float[,,] BtVaFloat(int d0, int d1, int d2) => EnsureFloat3D(ref _btVaFloat, d0, d1, d2);
    internal static float[,,] BtVbFloat(int d0, int d1, int d2) => EnsureFloat3D(ref _btVbFloat, d0, d1, d2);
    internal static float[,,] BtVmFloat(int d0, int d1, int d2) => EnsureFloat3D(ref _btVmFloat, d0, d1, d2);
    internal static float[,,] BtVdelFloat(int d0, int d1, int d2) => EnsureFloat3D(ref _btVdelFloat, d0, d1, d2);
    internal static float[,] BtVzFloat(int d0, int d1) => EnsureFloat2D(ref _btVzFloat, d0, d1);

    private static float[,,] EnsureFloat3D(ref float[,,]? slot, int d0, int d1, int d2)
    {
        var buffer = slot;
        if (buffer is null
            || buffer.GetLength(0) < d0
            || buffer.GetLength(1) < d1
            || buffer.GetLength(2) < d2)
        {
            // Grow to max(existing, requested) in every dimension so subsequent
            // callers with smaller requests never shrink the buffer below what
            // an earlier larger call populated.
            int nd0 = buffer is null ? d0 : Math.Max(buffer.GetLength(0), d0);
            int nd1 = buffer is null ? d1 : Math.Max(buffer.GetLength(1), d1);
            int nd2 = buffer is null ? d2 : Math.Max(buffer.GetLength(2), d2);
            buffer = new float[nd0, nd1, nd2];
            slot = buffer;
        }
        return buffer;
    }

    private static float[,] EnsureFloat2D(ref float[,]? slot, int d0, int d1)
    {
        var buffer = slot;
        if (buffer is null
            || buffer.GetLength(0) < d0
            || buffer.GetLength(1) < d1)
        {
            int nd0 = buffer is null ? d0 : Math.Max(buffer.GetLength(0), d0);
            int nd1 = buffer is null ? d1 : Math.Max(buffer.GetLength(1), d1);
            buffer = new float[nd0, nd1];
            slot = buffer;
        }
        return buffer;
    }

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
            int nr = buffer is null ? rows : Math.Max(buffer.GetLength(0), rows);
            int nc = buffer is null ? cols : Math.Max(buffer.GetLength(1), cols);
            buffer = new double[nr, nc];
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

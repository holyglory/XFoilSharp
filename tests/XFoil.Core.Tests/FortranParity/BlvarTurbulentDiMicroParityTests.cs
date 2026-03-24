using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR turbulent DI packet and derivative chain
// Secondary legacy source: tools/fortran-debug/di_wall_parity_driver.f90, di_dfac_parity_driver.f90, di_outer_parity_driver.f90, di_turbulent_parity_driver.f90,
// and tools/fortran-debug/reference/alpha10_p80_blvar_di_ref/reference_trace.*.jsonl
// Role in port: Provides a small matrix-facing owner ladder for the turbulent BLVAR DI chain without loading the giant historical alpha-10 managed solver trace.
// Differences: The old wrapper delegated into a broad alpha-10 station-29 trace selector that now materializes a 5.8 GB managed trace. The current wrapper routes the quick path
// through the already-split packet micro-drivers (wall, DFAC, outer-layer, full chain) and keeps the station-15 direct-seed trace replay as the representative trace-backed proof.
// Decision: Keep this wrapper because it gives the matrix one fast BLVAR DI family surface while preserving the stronger station-15 replay as the full-mode witness.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class BlvarTurbulentDiMicroParityTests
{
    [Fact]
    public void Alpha10_BlvarTurbulentDiTerms_Station29SeedProducer_MatchFortran()
    {
        // Compatibility alias for the historical quick rig name. The packet-level
        // DI chain micro-driver is now the truthful owner surface for this row.
        new TurbulentDiChainFortranParityTests()
            .TurbulentDiChainBatch_BitwiseMatchesFortranDriver();
    }

    [Fact]
    public void TurbulentWallDiBatch_BitwiseMatchesFortranDriver()
    {
        new TurbulentWallDiFortranParityTests()
            .TurbulentWallDiBatch_BitwiseMatchesFortranDriver();
    }

    [Fact]
    public void TurbulentDiDfacBatch_BitwiseMatchesFortranDriver()
    {
        new TurbulentDiDfacFortranParityTests()
            .TurbulentDiDfacBatch_BitwiseMatchesFortranDriver();
    }

    [Fact]
    public void TurbulentOuterDiBatch_BitwiseMatchesFortranDriver()
    {
        new TurbulentOuterDiFortranParityTests()
            .TurbulentOuterDiBatch_BitwiseMatchesFortranDriver();
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BlvarTurbulentDiTerms_BitwiseMatchFortranTrace()
    {
        new DirectSeedStation15SystemMicroParityTests()
            .Alpha10_P80_DirectSeedStation15_BlvarTurbulentDiTerms_BitwiseMatchFortranTrace();
    }
}

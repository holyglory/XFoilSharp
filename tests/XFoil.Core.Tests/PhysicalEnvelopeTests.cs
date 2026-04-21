using XFoil.Solver.Models;
using Xunit;

namespace XFoil.Core.Tests;

public class PhysicalEnvelopeTests
{
    private static ViscousAnalysisResult Make(double cl, double cd, bool converged = true)
        => new()
        {
            LiftCoefficient = cl,
            DragDecomposition = new DragDecomposition { CD = cd },
            Converged = converged,
        };

    [Fact]
    public void Null_IsNotPhysical()
        => Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(null));

    [Fact]
    public void NotConverged_IsNotPhysical()
        => Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: 0.01d, converged: false)));

    [Fact]
    public void TypicalPhysicalResult_IsPhysical()
        => Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 0.7, cd: 0.008)));

    [Fact]
    public void NaN_IsNotPhysical()
    {
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: double.NaN, cd: 0.01)));
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: double.NaN)));
    }

    [Fact]
    public void Infinity_IsNotPhysical()
    {
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: double.PositiveInfinity, cd: 0.01)));
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: double.PositiveInfinity)));
    }

    [Fact]
    public void NegativeCD_IsNotPhysical()
        => Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: -1e-6)));

    [Fact]
    public void CL_AtBoundary_IsPhysical()
    {
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: PhysicalEnvelope.MaxAbsoluteLiftCoefficient, cd: 0.01)));
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: -PhysicalEnvelope.MaxAbsoluteLiftCoefficient, cd: 0.01)));
    }

    [Fact]
    public void CL_JustOverBoundary_IsNotPhysical()
        => Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(
            Make(cl: PhysicalEnvelope.MaxAbsoluteLiftCoefficient + 1e-9, cd: 0.01)));

    [Fact]
    public void CD_AtBoundary_IsPhysical()
    {
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: 0d)));
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 1d, cd: PhysicalEnvelope.MaxDragCoefficient)));
    }

    [Fact]
    public void CD_JustOverBoundary_IsNotPhysical()
        => Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(
            Make(cl: 1d, cd: PhysicalEnvelope.MaxDragCoefficient + 1e-9)));

    [Fact]
    public void NonPhysicalAttractor_IsRejected()
    {
        // Mirrors the actual iter-37 finding: CD blew up to 1e60+, |CL|>20.
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 20d, cd: 1e60)));
        Assert.False(PhysicalEnvelope.IsAirfoilResultPhysical(Make(cl: 46.7, cd: 0.01)));
    }
}

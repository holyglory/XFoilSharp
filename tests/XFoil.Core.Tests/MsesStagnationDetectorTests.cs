using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.1 — stagnation-point detector tests.
/// </summary>
public class MsesStagnationDetectorTests
{
    [Fact]
    public void Detect_SyntheticSignChange_LocatesZeroBetweenPanels()
    {
        // Synthetic Ue array: negative, negative, +ε, +big. Sign
        // change happens between index 1 and 2. Linear interp
        // should give α close to 1 (mostly in panel 2).
        var ue = new[] { -1.0, -0.5, 0.1, 0.5 };
        var s = StagnationDetector.Detect(ue);
        Assert.Equal(2, s.PanelIndex);
        // α = -0.5 / (-0.5 - 0.1) = 0.833
        Assert.InRange(s.InterpolationFraction, 0.8, 0.9);
    }

    [Fact]
    public void Detect_AllNegative_ReturnsMidFallback()
    {
        var ue = new[] { -1.0, -0.8, -0.5, -0.2 };
        var s = StagnationDetector.Detect(ue);
        Assert.Equal(2, s.PanelIndex);  // n/2 = 4/2 = 2
    }

    [Fact]
    public void Detect_ExactZero_SkipsAndContinues()
    {
        // If Ue[i] is exactly 0, we don't count that as a sign change
        // crossing (it's the zero itself, not a crossing). Falls
        // through to mid fallback when no real crossing.
        var ue = new[] { -1.0, 0.0, 1.0 };
        var s = StagnationDetector.Detect(ue);
        // Fallback n/2 = 1.
        Assert.Equal(1, s.PanelIndex);
    }

    [Fact]
    public void DetectFromGeometry_Naca0012_Alpha0_LocatesNearLE()
    {
        // Symmetric airfoil at α=0°: stagnation is exactly at LE.
        // Panel index near middle of the 160-panel grid (where x=0).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var s = StagnationDetector.DetectFromGeometry(pg, 1.0, 0.0);
        // LE is at panel index ~80 (160 panels / 2). Allow ±3 for
        // numerical imprecision.
        Assert.InRange(s.PanelIndex, 77, 83);
    }

    [Fact]
    public void DetectFromGeometry_Naca0012_Alpha4_MovesStagBelowLE()
    {
        // At positive α, stagnation moves to the lower surface
        // (below the LE) because the flow approaches from below.
        // With our CCW panel walk, "below LE" corresponds to a
        // higher panel index than LE.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var s0 = StagnationDetector.DetectFromGeometry(pg, 1.0, 0.0);
        var s4 = StagnationDetector.DetectFromGeometry(
            pg, 1.0, 4.0 * System.Math.PI / 180.0);
        Assert.True(s4.PanelIndex > s0.PanelIndex,
            $"At α=4°, stag panel {s4.PanelIndex} should be > α=0° stag panel {s0.PanelIndex}");
    }

    [Fact]
    public void Detect_RejectsShortInput()
    {
        Assert.Throws<System.ArgumentException>(
            () => StagnationDetector.Detect(new[] { 1.0 }));
    }
}

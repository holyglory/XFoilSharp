using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Models;
using XFoil.Design.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ConformalMapgenServiceTests
{
    [Fact]
    public void Execute_ReturnsConformalGeometryAndCoefficients()
    {
        var mapgenService = new ConformalMapgenService();
        var qspecService = new QSpecDesignService();
        var profile = CreateProfile();
        var edited = qspecService.Modify(
            profile,
            new[]
            {
                new AirfoilPoint(profile.Points[1].PlotCoordinate, profile.Points[1].SpeedRatio),
                new AirfoilPoint(profile.Points[2].PlotCoordinate, 1.20d),
                new AirfoilPoint(profile.Points[4].PlotCoordinate, profile.Points[4].SpeedRatio),
            },
            true).Profile;
        var geometry = CreateGeometry(profile);

        var result = mapgenService.Execute(geometry, profile, edited, 17, 6);

        Assert.Equal(17, result.CirclePointCount);
        Assert.Equal(17, result.Geometry.Points.Count);
        Assert.NotEmpty(result.Coefficients);
        Assert.True(result.IterationCount >= 0);
        Assert.True(double.IsFinite(result.MaxCoefficientCorrection));
        Assert.All(result.Geometry.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Fact]
    public void Execute_RequiresOddCirclePointCount()
    {
        var service = new ConformalMapgenService();
        var geometry = CreateGeometry(CreateProfile());

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Execute(geometry, CreateProfile(), CreateProfile(), 16));
    }

    [Fact]
    public void Execute_LargerCirclePointCountStillReturnsFiniteGeometry()
    {
        var service = new ConformalMapgenService();
        var geometry = CreateGeometry(CreateProfile());

        var profile = CreateProfile();
        var result = service.Execute(geometry, profile, profile, 33, 8);

        Assert.Equal(33, result.CirclePointCount);
        Assert.All(result.Geometry.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Fact]
    public void Execute_FileBasedAirfoilCaseConverges()
    {
        var parser = new AirfoilParser();
        var analysisService = new AirfoilAnalysisService();
        var qSpecDesignService = new QSpecDesignService();
        var conformalMapgenService = new ConformalMapgenService();
        var geometry = parser.ParseFile(GetFixturePath("dae11.dat"));
        var analysis = analysisService.AnalyzeInviscid(geometry, 2d, new AnalysisSettings(120));
        var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
        var targetProfile = qSpecDesignService.Modify(
            baselineProfile,
            new[]
            {
                new AirfoilPoint(baselineProfile.Points[10].PlotCoordinate, baselineProfile.Points[10].SpeedRatio),
                new AirfoilPoint(baselineProfile.Points[30].PlotCoordinate, baselineProfile.Points[30].SpeedRatio * 0.94d),
                new AirfoilPoint(baselineProfile.Points[70].PlotCoordinate, baselineProfile.Points[70].SpeedRatio * 1.02d),
                new AirfoilPoint(baselineProfile.Points[100].PlotCoordinate, baselineProfile.Points[100].SpeedRatio),
            },
            true).Profile;

        var result = conformalMapgenService.Execute(geometry, baselineProfile, targetProfile, 129, 8);

        Assert.True(result.Converged);
        Assert.True(result.FinalTrailingEdgeResidual < 1e-2d);
    }

    [Fact]
    public void Execute_AllowsExplicitTrailingEdgeGapTarget()
    {
        var service = new ConformalMapgenService();
        var profile = CreateProfile();
        var geometry = CreateGeometry(profile);
        var targetGap = new AirfoilPoint(0.02d, -0.01d);

        var result = service.Execute(geometry, profile, profile, 33, 8, 5e-5d, targetGap);

        Assert.Equal(targetGap.X, result.TargetTrailingEdgeGap.X, 9);
        Assert.Equal(targetGap.Y, result.TargetTrailingEdgeGap.Y, 9);
        Assert.True(double.IsFinite(result.AchievedTrailingEdgeGap.X));
        Assert.True(double.IsFinite(result.AchievedTrailingEdgeGap.Y));
    }

    [Fact]
    public void Execute_AllowsExplicitTrailingEdgeAngleTarget()
    {
        var service = new ConformalMapgenService();
        var profile = CreateProfile();
        var geometry = CreateGeometry(profile);

        var result = service.Execute(geometry, profile, profile, 33, 8, 5e-5d, null, 4d);

        Assert.Equal(4d, result.TargetTrailingEdgeAngleDegrees, 9);
        Assert.True(double.IsFinite(result.AchievedTrailingEdgeAngleDegrees));
    }

    [Fact]
    public void Execute_FilterExponentAttenuatesUpperHarmonics()
    {
        var service = new ConformalMapgenService();
        var qspecService = new QSpecDesignService();
        var profile = CreateProfile();
        var targetProfile = qspecService.Modify(
            profile,
            new[]
            {
                new AirfoilPoint(profile.Points[1].PlotCoordinate, profile.Points[1].SpeedRatio),
                new AirfoilPoint(profile.Points[2].PlotCoordinate, profile.Points[2].SpeedRatio * 1.15d),
                new AirfoilPoint(profile.Points[3].PlotCoordinate, profile.Points[3].SpeedRatio * 0.88d),
                new AirfoilPoint(profile.Points[4].PlotCoordinate, profile.Points[4].SpeedRatio),
            },
            true).Profile;
        var geometry = CreateGeometry(profile);

        var unfiltered = service.Execute(geometry, profile, targetProfile, 33, 8);
        var filtered = service.Execute(geometry, profile, targetProfile, 33, 8, 5e-5d, null, null, 2d);

        var unfilteredHighMode = unfiltered.Coefficients[^1];
        var filteredHighMode = filtered.Coefficients[^1];
        var unfilteredMagnitude = Math.Sqrt((unfilteredHighMode.RealPart * unfilteredHighMode.RealPart) + (unfilteredHighMode.ImaginaryPart * unfilteredHighMode.ImaginaryPart));
        var filteredMagnitude = Math.Sqrt((filteredHighMode.RealPart * filteredHighMode.RealPart) + (filteredHighMode.ImaginaryPart * filteredHighMode.ImaginaryPart));

        Assert.True(filteredMagnitude < unfilteredMagnitude);
    }

    [Fact]
    public void Execute_TracksExplicitTrailingEdgeAngleTargetForRepresentativeNacaCase()
    {
        var generator = new NacaAirfoilGenerator();
        var analysisService = new AirfoilAnalysisService();
        var qSpecDesignService = new QSpecDesignService();
        var conformalMapgenService = new ConformalMapgenService();

        var geometry = generator.Generate4Digit("2412");
        var analysis = analysisService.AnalyzeInviscid(geometry, 3d, new AnalysisSettings(120));
        var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
        var targetProfile = qSpecDesignService.Modify(
            baselineProfile,
            new[]
            {
                new AirfoilPoint(baselineProfile.Points[10].PlotCoordinate, baselineProfile.Points[10].SpeedRatio),
                new AirfoilPoint(baselineProfile.Points[30].PlotCoordinate, baselineProfile.Points[30].SpeedRatio * 0.92d),
                new AirfoilPoint(baselineProfile.Points[60].PlotCoordinate, baselineProfile.Points[60].SpeedRatio * 1.03d),
                new AirfoilPoint(baselineProfile.Points[90].PlotCoordinate, baselineProfile.Points[90].SpeedRatio),
            },
            true).Profile;

        var result = conformalMapgenService.Execute(geometry, baselineProfile, targetProfile, 129, 8, 5e-5d, null, 4d);

        Assert.True(result.Converged);
        Assert.InRange(Math.Abs(result.AchievedTrailingEdgeAngleDegrees - 4d), 0d, 0.5d);
    }

    [Theory]
    [InlineData("0012", 0d)]
    [InlineData("0012", 4d)]
    [InlineData("2412", 0d)]
    [InlineData("2412", 3d)]
    public void Execute_ConvergesForRepresentativeNacaCases(string designation, double alphaDegrees)
    {
        var generator = new NacaAirfoilGenerator();
        var analysisService = new AirfoilAnalysisService();
        var qSpecDesignService = new QSpecDesignService();
        var conformalMapgenService = new ConformalMapgenService();

        var geometry = generator.Generate4Digit(designation);
        var analysis = analysisService.AnalyzeInviscid(geometry, alphaDegrees, new AnalysisSettings(120));
        var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
        var targetProfile = qSpecDesignService.Modify(
            baselineProfile,
            new[]
            {
                new AirfoilPoint(baselineProfile.Points[10].PlotCoordinate, baselineProfile.Points[10].SpeedRatio),
                new AirfoilPoint(baselineProfile.Points[30].PlotCoordinate, baselineProfile.Points[30].SpeedRatio * 0.92d),
                new AirfoilPoint(baselineProfile.Points[60].PlotCoordinate, baselineProfile.Points[60].SpeedRatio * 1.03d),
                new AirfoilPoint(baselineProfile.Points[90].PlotCoordinate, baselineProfile.Points[90].SpeedRatio),
            },
            true).Profile;

        var result = conformalMapgenService.Execute(geometry, baselineProfile, targetProfile, 129, 8);

        Assert.True(result.Converged);
        Assert.True(result.FinalTrailingEdgeResidual <= result.InitialTrailingEdgeResidual);
        Assert.True(result.FinalTrailingEdgeResidual < 1e-2d);
        Assert.All(result.Geometry.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Theory]
    [InlineData("dae11.dat", 2d)]
    [InlineData("dae21.dat", 2d)]
    [InlineData("dae31.dat", 3d)]
    [InlineData("dae51.dat", 2d)]
    [InlineData("e387.dat", 3d)]
    [InlineData("la203.dat", 2d)]
    public void Execute_ConvergesForRepresentativeLegacyAirfoilFiles(string fileName, double alphaDegrees)
    {
        var parser = new AirfoilParser();
        var analysisService = new AirfoilAnalysisService();
        var qSpecDesignService = new QSpecDesignService();
        var conformalMapgenService = new ConformalMapgenService();
        var geometry = parser.ParseFile(GetFixturePath(fileName));
        var analysis = analysisService.AnalyzeInviscid(geometry, alphaDegrees, new AnalysisSettings(140));
        var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
        var targetProfile = BuildModeratelyEditedProfile(qSpecDesignService, baselineProfile);

        var result = conformalMapgenService.Execute(geometry, baselineProfile, targetProfile, 129, 10);
        var geometryTrailingEdgeAngle = ComputeTrailingEdgeAngleDegrees(geometry.Points);

        Assert.True(result.Converged);
        Assert.True(result.FinalTrailingEdgeResidual <= result.InitialTrailingEdgeResidual);
        Assert.True(result.FinalTrailingEdgeResidual < 1e-2d);
        Assert.InRange(Math.Abs(result.AchievedTrailingEdgeAngleDegrees - geometryTrailingEdgeAngle), 0d, 0.75d);
        Assert.All(result.Geometry.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    private static AirfoilGeometry CreateGeometry(QSpecProfile profile)
    {
        return new AirfoilGeometry(
            "ProfileGeom",
            profile.Points.Select(point => point.Location).ToArray(),
            AirfoilFormat.PlainCoordinates);
    }

    private static string GetFixturePath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "runs", fileName));
    }

    private static QSpecProfile CreateProfile()
    {
        return new QSpecProfile(
            "Profile",
            3d,
            0d,
            new[]
            {
                new QSpecPoint(0, 0d, 1d, new AirfoilPoint(1d, 0d), 0.8d, 0.36d, 0.36d),
                new QSpecPoint(1, 0.2d, 0.8d, new AirfoilPoint(0.8d, 0.04d), 1.0d, 0.0d, 0.0d),
                new QSpecPoint(2, 0.4d, 0.6d, new AirfoilPoint(0.6d, 0.08d), 1.5d, -1.25d, -1.20d),
                new QSpecPoint(3, 0.6d, 0.4d, new AirfoilPoint(0.4d, 0.02d), 1.1d, -0.21d, -0.20d),
                new QSpecPoint(4, 0.8d, 0.2d, new AirfoilPoint(0.2d, -0.03d), 0.7d, 0.51d, 0.50d),
                new QSpecPoint(5, 1d, 0d, new AirfoilPoint(0d, 0d), 0.5d, 0.75d, 0.72d),
            });
    }

    private static QSpecProfile BuildModeratelyEditedProfile(QSpecDesignService service, QSpecProfile baselineProfile)
    {
        var points = baselineProfile.Points;
        var count = points.Count;
        return service.Modify(
            baselineProfile,
            new[]
            {
                CreateControlPoint(points, (int)Math.Round(0.10d * (count - 1)), 1.00d),
                CreateControlPoint(points, (int)Math.Round(0.32d * (count - 1)), 0.94d),
                CreateControlPoint(points, (int)Math.Round(0.66d * (count - 1)), 1.02d),
                CreateControlPoint(points, (int)Math.Round(0.90d * (count - 1)), 1.00d),
            },
            true).Profile;
    }

    private static AirfoilPoint CreateControlPoint(IReadOnlyList<QSpecPoint> points, int index, double speedRatioScale)
    {
        var clampedIndex = Math.Clamp(index, 0, points.Count - 1);
        return new AirfoilPoint(points[clampedIndex].PlotCoordinate, points[clampedIndex].SpeedRatio * speedRatioScale);
    }

    private static double ComputeTrailingEdgeAngleDegrees(IReadOnlyList<AirfoilPoint> points)
    {
        var firstDerivative = new AirfoilPoint(points[1].X - points[0].X, points[1].Y - points[0].Y);
        var lastDerivative = new AirfoilPoint(points[^1].X - points[^2].X, points[^1].Y - points[^2].Y);
        return
            ((Math.Atan2(lastDerivative.X, -lastDerivative.Y) - Math.Atan2(firstDerivative.X, -firstDerivative.Y)) / Math.PI - 1d)
            * 180d;
    }
}

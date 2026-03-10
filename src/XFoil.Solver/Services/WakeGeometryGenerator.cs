using XFoil.Core.Models;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class WakeGeometryGenerator
{
    private const double TwoPi = 2d * Math.PI;

    public WakeGeometry Generate(
        PanelMesh mesh,
        IReadOnlyList<double> sourceStrengths,
        double vortexStrength,
        double freestreamVelocity,
        double angleOfAttackDegrees,
        int? wakePointCount = null,
        double? wakeLength = null)
    {
        if (mesh is null)
        {
            throw new ArgumentNullException(nameof(mesh));
        }

        if (sourceStrengths is null)
        {
            throw new ArgumentNullException(nameof(sourceStrengths));
        }

        if (sourceStrengths.Count != mesh.Panels.Count)
        {
            throw new ArgumentException("Source strengths must match the panel count.", nameof(sourceStrengths));
        }

        if (freestreamVelocity <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(freestreamVelocity), "Freestream velocity must be positive.");
        }

        var count = wakePointCount ?? Math.Max(6, (mesh.Panels.Count / 8) + 2);
        var totalWakeLength = wakeLength ?? 1.0d;

        var firstPanel = mesh.Panels[0];
        var lastPanel = mesh.Panels[^1];
        var trailingEdge = new AirfoilPoint(
            0.5d * (mesh.Nodes[0].X + mesh.Nodes[^2].X),
            0.5d * (mesh.Nodes[0].Y + mesh.Nodes[^2].Y));

        var freestreamAngle = angleOfAttackDegrees * Math.PI / 180d;
        var freestreamX = Math.Cos(freestreamAngle);
        var freestreamY = -Math.Sin(freestreamAngle);

        var baseDirectionX = 0.5d * (firstPanel.TangentX + lastPanel.TangentX) + freestreamX;
        var baseDirectionY = 0.5d * (firstPanel.TangentY + lastPanel.TangentY) + freestreamY;
        var magnitude = Math.Sqrt((baseDirectionX * baseDirectionX) + (baseDirectionY * baseDirectionY));
        if (magnitude <= 1e-12)
        {
            baseDirectionX = 1d;
            baseDirectionY = 0d;
            magnitude = 1d;
        }

        var tangentX = baseDirectionX / magnitude;
        var tangentY = baseDirectionY / magnitude;
        if (tangentX < 0d)
        {
            tangentX = -tangentX;
            tangentY = -tangentY;
        }

        var firstSpacing = 0.5d * (firstPanel.Length + lastPanel.Length);
        var stretchedDistances = BuildStretchedDistances(firstSpacing, totalWakeLength, count);
        var points = new List<WakePoint>(count);
        var seedOffset = Math.Max(1e-4, 0.1d * firstSpacing);
        var seedPoint = new AirfoilPoint(
            trailingEdge.X + (seedOffset * tangentX),
            trailingEdge.Y + (seedOffset * tangentY));
        var initialWakeState = EvaluateWakeState(
            mesh,
            sourceStrengths,
            vortexStrength,
            freestreamX,
            freestreamY,
            seedPoint,
            tangentX,
            tangentY);

        tangentX = initialWakeState.TangentX;
        tangentY = initialWakeState.TangentY;

        points.Add(new WakePoint(trailingEdge, tangentX, tangentY, 0d, initialWakeState.Speed));

        for (var index = 1; index < count; index++)
        {
            var step = stretchedDistances[index] - stretchedDistances[index - 1];
            var previousPoint = points[^1].Location;
            var nextLocation = new AirfoilPoint(
                previousPoint.X + (step * tangentX),
                previousPoint.Y + (step * tangentY));
            var samplePoint = new AirfoilPoint(
                previousPoint.X + (0.5d * step * tangentX),
                previousPoint.Y + (0.5d * step * tangentY));
            var nextWakeState = index < count - 1
                ? EvaluateWakeState(
                    mesh,
                    sourceStrengths,
                    vortexStrength,
                    freestreamX,
                    freestreamY,
                    samplePoint,
                    tangentX,
                    tangentY)
                : new WakeState(tangentX, tangentY, points[^1].VelocityMagnitude);

            tangentX = nextWakeState.TangentX;
            tangentY = nextWakeState.TangentY;
            points.Add(new WakePoint(nextLocation, tangentX, tangentY, stretchedDistances[index], nextWakeState.Speed));
        }

        return new WakeGeometry(points);
    }

    private static WakeState EvaluateWakeState(
        PanelMesh mesh,
        IReadOnlyList<double> sourceStrengths,
        double vortexStrength,
        double freestreamX,
        double freestreamY,
        AirfoilPoint location,
        double fallbackTangentX,
        double fallbackTangentY)
    {
        var velocityX = freestreamX;
        var velocityY = freestreamY;

        for (var panelIndex = 0; panelIndex < mesh.Panels.Count; panelIndex++)
        {
            var panel = mesh.Panels[panelIndex];
            var influence = ComputePointVelocityInfluence(location, panel);
            velocityX += sourceStrengths[panelIndex] * influence.SourceVelocityX;
            velocityY += sourceStrengths[panelIndex] * influence.SourceVelocityY;
            velocityX += vortexStrength * influence.VortexVelocityX;
            velocityY += vortexStrength * influence.VortexVelocityY;
        }

        var magnitude = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        if (magnitude <= 1e-12)
        {
            var fallback = NormalizeFallback(fallbackTangentX, fallbackTangentY);
            return new WakeState(fallback.X, fallback.Y, Math.Sqrt((freestreamX * freestreamX) + (freestreamY * freestreamY)));
        }

        var tangentX = velocityX / magnitude;
        var tangentY = velocityY / magnitude;
        var alignment = (tangentX * fallbackTangentX) + (tangentY * fallbackTangentY);
        if (alignment < 0d)
        {
            tangentX = -tangentX;
            tangentY = -tangentY;
        }

        if (tangentX < -0.05d)
        {
            var fallback = NormalizeFallback(fallbackTangentX, fallbackTangentY);
            return new WakeState(fallback.X, fallback.Y, magnitude);
        }

        return new WakeState(tangentX, tangentY, magnitude);
    }

    private static AirfoilPoint NormalizeFallback(double tangentX, double tangentY)
    {
        var magnitude = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
        if (magnitude <= 1e-12)
        {
            return new AirfoilPoint(1d, 0d);
        }

        return new AirfoilPoint(tangentX / magnitude, tangentY / magnitude);
    }

    private static PanelVelocityInfluence ComputePointVelocityInfluence(AirfoilPoint point, Panel panel)
    {
        var dx = point.X - panel.Start.X;
        var dy = point.Y - panel.Start.Y;
        var localX = (dx * panel.TangentX) + (dy * panel.TangentY);
        var localY = (dx * panel.NormalX) + (dy * panel.NormalY);
        var r1Squared = Math.Max((localX * localX) + (localY * localY), 1e-16);
        var localX2 = localX - panel.Length;
        var r2Squared = Math.Max((localX2 * localX2) + (localY * localY), 1e-16);
        var theta1 = Math.Atan2(localY, localX);
        var theta2 = Math.Atan2(localY, localX2);

        var uSource = Math.Log(r1Squared / r2Squared) / (4d * Math.PI);
        var vSource = (theta2 - theta1) / TwoPi;
        var uVortex = vSource;
        var vVortex = -uSource;

        return new PanelVelocityInfluence(
            (uSource * panel.TangentX) + (vSource * panel.NormalX),
            (uSource * panel.TangentY) + (vSource * panel.NormalY),
            (uVortex * panel.TangentX) + (vVortex * panel.NormalX),
            (uVortex * panel.TangentY) + (vVortex * panel.NormalY));
    }

    private static double[] BuildStretchedDistances(double firstSpacing, double maxDistance, int pointCount)
    {
        if (pointCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), "Wake point count must be at least 2.");
        }

        var result = new double[pointCount];
        result[0] = 0d;
        if (pointCount == 2)
        {
            result[1] = maxDistance;
            return result;
        }

        var segmentCount = pointCount - 1;
        var ratio = SolveGeometricRatio(firstSpacing, maxDistance, segmentCount);
        var step = firstSpacing;
        for (var index = 1; index < pointCount; index++)
        {
            result[index] = result[index - 1] + step;
            step *= ratio;
        }

        result[^1] = maxDistance;
        return result;
    }

    private static double SolveGeometricRatio(double firstSpacing, double maxDistance, int segmentCount)
    {
        if (segmentCount <= 1)
        {
            return 1d;
        }

        var sigma = maxDistance / Math.Max(firstSpacing, 1e-9);
        var ratio = 1.05d;

        for (var iteration = 0; iteration < 100; iteration++)
        {
            if (Math.Abs(ratio - 1d) < 1e-9)
            {
                ratio = 1.0001d;
            }

            var numerator = Math.Pow(ratio, segmentCount) - 1d;
            var denominator = ratio - 1d;
            var geometricSum = numerator / denominator;
            var residual = geometricSum - sigma;
            if (Math.Abs(residual) < 1e-8)
            {
                return ratio;
            }

            var derivative =
                ((segmentCount * Math.Pow(ratio, segmentCount - 1d)) * denominator - numerator)
                / (denominator * denominator);

            ratio -= residual / derivative;
            ratio = Math.Max(1.00001d, ratio);
        }

        return ratio;
    }

    private readonly record struct PanelVelocityInfluence(
        double SourceVelocityX,
        double SourceVelocityY,
        double VortexVelocityX,
        double VortexVelocityY);

    private readonly record struct WakeState(
        double TangentX,
        double TangentY,
        double Speed);
}

using XFoil.Core.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: XYWAKE
// Secondary legacy source: f_xfoil/src/xutils.f :: SETEXP
// Role in port: Generates the managed wake geometry used by the Hess-Smith and diagnostic paths.
// Differences: The wake march is derived from XYWAKE, but the managed version evaluates wake direction from explicit panel velocity influence summation and stores the result as immutable WakePoint records instead of mutating the legacy wake arrays in place.
// Decision: Keep the managed wake builder because it is easier to inspect and test. Preserve the SETEXP-derived spacing law and the legacy wake-gap behavior where parity-sensitive seed construction depends on them.
namespace XFoil.Solver.Services;

public sealed class WakeGeometryGenerator
{
    private const double TwoPi = 2d * Math.PI;

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE (managed-derived wake march).
    // Difference from legacy: The method consumes an already-solved panel mesh and influence strengths, then constructs a managed WakeGeometry object through explicit stepping instead of updating XFoil COMMON arrays.
    // Decision: Keep the managed object-building flow because it matches the public solver API, while the spacing and tangent-march ideas remain aligned with XYWAKE.
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
        var stretchedDistances = WakeSpacing.BuildStretchedDistances(firstSpacing, totalWakeLength, count);
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

        // Legacy block: XYWAKE downstream wake march.
        // Difference: The managed code advances WakePoint records explicitly and samples the midpoint flow state for the next tangent, rather than updating the legacy wake arrays in-place.
        // Decision: Keep the explicit march because it is easier to debug while retaining the same high-level wake continuation behavior.
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

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE local wake-direction update.
    // Difference from legacy: The managed port sums source and vortex point-velocity influences explicitly rather than calling through the original panel solver workspace.
    // Decision: Keep the explicit influence sum because it matches the managed Hess-Smith data flow.
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

    // Legacy mapping: managed-only fallback normalization helper with no standalone Fortran analogue.
    // Difference from legacy: XFoil folds this safeguard into surrounding wake logic; the port factors it out to keep the wake-state update readable.
    // Decision: Keep the helper because it makes the managed wake march easier to reason about.
    private static AirfoilPoint NormalizeFallback(double tangentX, double tangentY)
    {
        var magnitude = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
        if (magnitude <= 1e-12)
        {
            return new AirfoilPoint(1d, 0d);
        }

        return new AirfoilPoint(tangentX / magnitude, tangentY / magnitude);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE / induced-velocity evaluation lineage.
    // Difference from legacy: The underlying source/vortex influence formulas are classical panel-method relations evaluated directly here instead of through the full XFoil influence workspace and wake/source coupling tables.
    // Decision: Keep the local helper because it matches the managed Hess-Smith solver architecture, while the comment records that it is a managed derivation rather than a literal workspace port.
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

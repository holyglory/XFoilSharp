using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

public sealed class HessSmithInviscidSolver
{
    private const double TwoPi = 2d * Math.PI;
    private readonly DenseLinearSystemSolver linearSystemSolver = new();
    private readonly WakeGeometryGenerator wakeGeometryGenerator = new();

    public PreparedInviscidSystem Prepare(PanelMesh mesh)
    {
        if (mesh is null)
        {
            throw new ArgumentNullException(nameof(mesh));
        }

        var panelCount = mesh.Panels.Count;
        var systemMatrix = new double[panelCount + 1, panelCount + 1];
        var sourceTangentialInfluence = new double[panelCount, panelCount];
        var vortexTangentialInfluence = new double[panelCount, panelCount];

        for (var controlIndex = 0; controlIndex < panelCount; controlIndex++)
        {
            var controlPanel = mesh.Panels[controlIndex];

            for (var panelIndex = 0; panelIndex < panelCount; panelIndex++)
            {
                var influence = ComputeInfluence(controlPanel, mesh.Panels[panelIndex], controlIndex == panelIndex);
                systemMatrix[controlIndex, panelIndex] = influence.SourceNormal;
                sourceTangentialInfluence[controlIndex, panelIndex] = influence.SourceTangential;
                vortexTangentialInfluence[controlIndex, panelIndex] = influence.VortexTangential;
                systemMatrix[controlIndex, panelCount] += influence.VortexNormal;
            }
        }

        var firstPanel = mesh.Panels[0];
        var lastPanel = mesh.Panels[^1];

        for (var panelIndex = 0; panelIndex < panelCount; panelIndex++)
        {
            systemMatrix[panelCount, panelIndex] =
                sourceTangentialInfluence[0, panelIndex]
                + sourceTangentialInfluence[panelCount - 1, panelIndex];

            systemMatrix[panelCount, panelCount] +=
                vortexTangentialInfluence[0, panelIndex]
                + vortexTangentialInfluence[panelCount - 1, panelIndex];
        }

        var unitFreestreamXRhs = BuildUnitFreestreamRightHandSide(mesh, firstPanel, lastPanel, 1d, 0d);
        var unitFreestreamYRhs = BuildUnitFreestreamRightHandSide(mesh, firstPanel, lastPanel, 0d, 1d);
        var unitFreestreamXSolution = linearSystemSolver.Solve((double[,])systemMatrix.Clone(), unitFreestreamXRhs);
        var unitFreestreamYSolution = linearSystemSolver.Solve((double[,])systemMatrix.Clone(), unitFreestreamYRhs);

        return new PreparedInviscidSystem(
            mesh,
            sourceTangentialInfluence,
            vortexTangentialInfluence,
            unitFreestreamXSolution.Take(panelCount).ToArray(),
            unitFreestreamXSolution[panelCount],
            unitFreestreamYSolution.Take(panelCount).ToArray(),
            unitFreestreamYSolution[panelCount]);
    }

    public InviscidAnalysisResult Analyze(
        PanelMesh mesh,
        double angleOfAttackDegrees,
        double freestreamVelocity = 1d,
        double machNumber = 0d)
    {
        return Analyze(Prepare(mesh), angleOfAttackDegrees, freestreamVelocity, machNumber);
    }

    public InviscidAnalysisResult Analyze(
        PreparedInviscidSystem preparedSystem,
        double angleOfAttackDegrees,
        double freestreamVelocity = 1d,
        double machNumber = 0d)
    {
        if (preparedSystem is null)
        {
            throw new ArgumentNullException(nameof(preparedSystem));
        }

        if (freestreamVelocity <= 0d)
        {
            throw new ArgumentException("Freestream velocity must be positive.", nameof(freestreamVelocity));
        }

        if (machNumber < 0d || machNumber >= 1d)
        {
            throw new ArgumentException("Mach number must be in the range [0, 1).", nameof(machNumber));
        }

        var mesh = preparedSystem.Mesh;
        var panelCount = mesh.Panels.Count;

        var angleOfAttackRadians = DegreesToRadians(angleOfAttackDegrees);
        var freestreamX = freestreamVelocity * Math.Cos(angleOfAttackRadians);
        var freestreamY = -freestreamVelocity * Math.Sin(angleOfAttackRadians);
        var sourceStrengths = CombineBasisVectors(
            preparedSystem.UnitFreestreamXSourceStrengths,
            preparedSystem.UnitFreestreamYSourceStrengths,
            freestreamX,
            freestreamY);
        var vortexStrength =
            (freestreamX * preparedSystem.UnitFreestreamXVortexStrength)
            + (freestreamY * preparedSystem.UnitFreestreamYVortexStrength);

        var pressureSamples = new List<PressureCoefficientSample>(panelCount);
        var totalLength = 0d;
        var panelPressureCoefficients = new double[panelCount];
        var correctedPanelPressureCoefficients = new double[panelCount];

        for (var controlIndex = 0; controlIndex < panelCount; controlIndex++)
        {
            var controlPanel = mesh.Panels[controlIndex];
            var tangentialVelocity =
                (freestreamX * controlPanel.TangentX)
                + (freestreamY * controlPanel.TangentY);

            for (var panelIndex = 0; panelIndex < panelCount; panelIndex++)
            {
                tangentialVelocity += sourceStrengths[panelIndex] * preparedSystem.SourceTangentialInfluence[controlIndex, panelIndex];
                tangentialVelocity += vortexStrength * preparedSystem.VortexTangentialInfluence[controlIndex, panelIndex];
            }

            var pressureCoefficient = ComputeIncompressiblePressureCoefficient(tangentialVelocity, freestreamVelocity);
            var correctedPressureCoefficient = ApplyCompressibilityCorrection(pressureCoefficient, machNumber);
            panelPressureCoefficients[controlIndex] = pressureCoefficient;
            correctedPanelPressureCoefficients[controlIndex] = correctedPressureCoefficient;
            pressureSamples.Add(new PressureCoefficientSample(
                controlPanel.ControlPoint,
                tangentialVelocity,
                pressureCoefficient,
                correctedPressureCoefficient));
            totalLength += controlPanel.Length;
        }

        var circulation = vortexStrength * totalLength;
        var (pressureLiftCoefficient, pressureDragCoefficient, momentQuarterChord) = IntegratePressureForces(
            mesh,
            panelPressureCoefficients,
            angleOfAttackRadians);
        var (correctedPressureLiftCoefficient, correctedPressureDragCoefficient, correctedMomentQuarterChord) = IntegratePressureForces(
            mesh,
            correctedPanelPressureCoefficients,
            angleOfAttackRadians);
        var camberIndicator = mesh.Nodes.Take(mesh.Nodes.Count - 1).Average(point => point.Y);
        var effectiveIncidence = angleOfAttackRadians + (8d * camberIndicator);
        var liftSign = Math.Sign(effectiveIncidence);
        if (liftSign == 0)
        {
            liftSign = Math.Sign(vortexStrength);
        }

        var circulationLiftCoefficient = liftSign * (2d * Math.Abs(circulation) / freestreamVelocity);
        var pressureLiftAgreement =
            Math.Abs(correctedPressureLiftCoefficient - circulationLiftCoefficient)
            <= Math.Max(0.05d, 0.2d * Math.Abs(circulationLiftCoefficient));
        var usePressureLift = pressureLiftAgreement;
        var liftCoefficient = usePressureLift ? correctedPressureLiftCoefficient : circulationLiftCoefficient;
        var dragCoefficient = usePressureLift ? correctedPressureDragCoefficient : 0d;
        var momentCoefficient = machNumber > 0d ? correctedMomentQuarterChord : momentQuarterChord;

        return new InviscidAnalysisResult(
            mesh,
            angleOfAttackDegrees,
            machNumber,
            circulation,
            liftCoefficient,
            dragCoefficient,
            correctedPressureLiftCoefficient,
            correctedPressureDragCoefficient,
            pressureLiftCoefficient,
            pressureDragCoefficient,
            momentCoefficient,
            sourceStrengths,
            vortexStrength,
            pressureSamples,
            wakeGeometryGenerator.Generate(
                mesh,
                sourceStrengths,
                vortexStrength,
                freestreamVelocity,
                angleOfAttackDegrees));
    }

    private static double[] BuildUnitFreestreamRightHandSide(
        PanelMesh mesh,
        Panel firstPanel,
        Panel lastPanel,
        double freestreamX,
        double freestreamY)
    {
        var panelCount = mesh.Panels.Count;
        var rightHandSide = new double[panelCount + 1];

        for (var controlIndex = 0; controlIndex < panelCount; controlIndex++)
        {
            var controlPanel = mesh.Panels[controlIndex];
            rightHandSide[controlIndex] =
                -((freestreamX * controlPanel.NormalX) + (freestreamY * controlPanel.NormalY));
        }

        rightHandSide[panelCount] =
            -((freestreamX * firstPanel.TangentX) + (freestreamY * firstPanel.TangentY))
            -((freestreamX * lastPanel.TangentX) + (freestreamY * lastPanel.TangentY));

        return rightHandSide;
    }

    private static double[] CombineBasisVectors(
        IReadOnlyList<double> xBasis,
        IReadOnlyList<double> yBasis,
        double freestreamX,
        double freestreamY)
    {
        var result = new double[xBasis.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (freestreamX * xBasis[index]) + (freestreamY * yBasis[index]);
        }

        return result;
    }

    private static PanelInfluence ComputeInfluence(Panel controlPanel, Panel sourcePanel, bool isSelf)
    {
        if (isSelf)
        {
            return new PanelInfluence(0.5d, 0d, 0d, -0.5d);
        }

        var dx = controlPanel.ControlPoint.X - sourcePanel.Start.X;
        var dy = controlPanel.ControlPoint.Y - sourcePanel.Start.Y;
        var localX = (dx * sourcePanel.TangentX) + (dy * sourcePanel.TangentY);
        var localY = (dx * sourcePanel.NormalX) + (dy * sourcePanel.NormalY);
        var r1Squared = Math.Max((localX * localX) + (localY * localY), 1e-16);
        var localX2 = localX - sourcePanel.Length;
        var r2Squared = Math.Max((localX2 * localX2) + (localY * localY), 1e-16);
        var theta1 = Math.Atan2(localY, localX);
        var theta2 = Math.Atan2(localY, localX2);

        var uSource = Math.Log(r1Squared / r2Squared) / (4d * Math.PI);
        var vSource = (theta2 - theta1) / TwoPi;
        var uVortex = vSource;
        var vVortex = -uSource;

        var sourceVelocityX = (uSource * sourcePanel.TangentX) + (vSource * sourcePanel.NormalX);
        var sourceVelocityY = (uSource * sourcePanel.TangentY) + (vSource * sourcePanel.NormalY);
        var vortexVelocityX = (uVortex * sourcePanel.TangentX) + (vVortex * sourcePanel.NormalX);
        var vortexVelocityY = (uVortex * sourcePanel.TangentY) + (vVortex * sourcePanel.NormalY);

        return new PanelInfluence(
            (sourceVelocityX * controlPanel.NormalX) + (sourceVelocityY * controlPanel.NormalY),
            (sourceVelocityX * controlPanel.TangentX) + (sourceVelocityY * controlPanel.TangentY),
            (vortexVelocityX * controlPanel.NormalX) + (vortexVelocityY * controlPanel.NormalY),
            (vortexVelocityX * controlPanel.TangentX) + (vortexVelocityY * controlPanel.TangentY));
    }

    private static (double LiftCoefficient, double DragCoefficient, double MomentCoefficientQuarterChord) IntegratePressureForces(
        PanelMesh mesh,
        IReadOnlyList<double> pressureCoefficients,
        double angleOfAttackRadians)
    {
        var dragDirectionX = Math.Cos(angleOfAttackRadians);
        var dragDirectionY = -Math.Sin(angleOfAttackRadians);
        var liftDirectionX = Math.Sin(angleOfAttackRadians);
        var liftDirectionY = Math.Cos(angleOfAttackRadians);
        var forceX = 0d;
        var forceY = 0d;
        var momentCoefficient = 0d;

        for (var panelIndex = 0; panelIndex < mesh.Panels.Count; panelIndex++)
        {
            var panel = mesh.Panels[panelIndex];
            var pressureCoefficient = pressureCoefficients[panelIndex];
            var panelForceX = -pressureCoefficient * panel.NormalX * panel.Length;
            var panelForceY = -pressureCoefficient * panel.NormalY * panel.Length;
            forceX += panelForceX;
            forceY += panelForceY;

            var armX = panel.ControlPoint.X - 0.25d;
            var armY = panel.ControlPoint.Y;
            momentCoefficient += (armX * panelForceY) - (armY * panelForceX);
        }

        var dragCoefficient = (forceX * dragDirectionX) + (forceY * dragDirectionY);
        var liftCoefficient = (forceX * liftDirectionX) + (forceY * liftDirectionY);
        return (liftCoefficient, dragCoefficient, momentCoefficient);
    }

    private static double ComputeIncompressiblePressureCoefficient(double tangentialVelocity, double freestreamVelocity)
    {
        return 1d - Math.Pow(tangentialVelocity / freestreamVelocity, 2d);
    }

    private static double ApplyCompressibilityCorrection(double pressureCoefficientIncompressible, double machNumber)
    {
        if (machNumber <= 0d)
        {
            return pressureCoefficientIncompressible;
        }

        var beta = Math.Sqrt(1d - (machNumber * machNumber));
        var bFactor = 0.5d * machNumber * machNumber / (1d + beta);
        var denominator = beta + (bFactor * pressureCoefficientIncompressible);
        if (denominator <= 1e-12)
        {
            return pressureCoefficientIncompressible;
        }

        return pressureCoefficientIncompressible / denominator;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private readonly record struct PanelInfluence(
        double SourceNormal,
        double SourceTangential,
        double VortexNormal,
        double VortexTangential);
}

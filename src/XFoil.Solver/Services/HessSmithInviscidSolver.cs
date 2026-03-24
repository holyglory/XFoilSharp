using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSILIN/QISET/QWCALC influence-lineage
// Secondary legacy source: f_xfoil/src/xfoil.f :: COMSET
// Role in port: Provides the current managed Hess-Smith inviscid analysis path.
// Differences: This file is not a direct port of XFoil's inviscid core; it reuses classical panel-method influence formulas, precomputes basis solutions for freestream directions, and packages the result into managed objects and wake geometry helpers.
// Decision: Keep the managed Hess-Smith implementation because it is the active inviscid runtime path, but record clearly that it is legacy-derived rather than a literal workspace port.
namespace XFoil.Solver.Services;

public sealed class HessSmithInviscidSolver
{
    private const double TwoPi = 2d * Math.PI;
    private readonly DenseLinearSystemSolver linearSystemSolver = new();
    private readonly WakeGeometryGenerator wakeGeometryGenerator = new();

    // Legacy mapping: f_xfoil/src/xpanel.f :: influence-matrix assembly lineage (managed-derived).
    // Difference from legacy: The method assembles and solves a Hess-Smith basis system once for unit freestream directions instead of using XFoil's original panel workspace and gamma solve path directly.
    // Decision: Keep the prepared-system abstraction because it makes repeated analyses cheap and does not pretend to be a direct parity port.
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

    // Legacy mapping: managed-only convenience overload around the prepared Hess-Smith system.
    // Difference from legacy: The original XFoil flow does not separate prepare/analyze in this way; the port adds it to support repeated inviscid solves on the same mesh.
    // Decision: Keep the overload because it is a useful API wrapper over the managed solver path.
    public InviscidAnalysisResult Analyze(
        PanelMesh mesh,
        double angleOfAttackDegrees,
        double freestreamVelocity = 1d,
        double machNumber = 0d)
    {
        return Analyze(Prepare(mesh), angleOfAttackDegrees, freestreamVelocity, machNumber);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: inviscid solve and force-recovery lineage; f_xfoil/src/xfoil.f :: COMSET compressibility correction.
    // Difference from legacy: The method combines precomputed basis solutions, managed pressure integration, and wake generation rather than replaying XFoil's original inviscid state arrays and solver sequencing.
    // Decision: Keep the managed analysis flow because it is the current runtime solver, while continuing to document where it diverges from the original implementation.
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

    // Legacy mapping: f_xfoil/src/xpanel.f :: freestream right-hand-side assembly lineage.
    // Difference from legacy: The managed solver forms explicit unit-X and unit-Y RHS vectors for reusable basis solves instead of rebuilding a single RHS per angle-of-attack solve.
    // Decision: Keep the basis-solve formulation because it is a deliberate managed performance improvement.
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

    // Legacy mapping: managed-only basis-combination helper with no standalone Fortran analogue.
    // Difference from legacy: XFoil does not combine pre-solved freestream basis vectors because it solves the inviscid system directly for each case.
    // Decision: Keep the helper because it is central to the managed prepared-system design.
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

    // Legacy mapping: f_xfoil/src/xpanel.f :: source/vortex panel influence evaluation lineage.
    // Difference from legacy: The formulas are standard and related to XFoil's panel kernels, but this method evaluates them in a standalone Hess-Smith form rather than through the original PSILIN/QISET workspace flow.
    // Decision: Keep the explicit influence helper because it matches the managed solver architecture.
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

    // Legacy mapping: f_xfoil/src/xfoil.f :: pressure-force recovery lineage.
    // Difference from legacy: The managed code integrates directly over Panel records and returns lift/drag/moment together instead of updating shared result scalars.
    // Decision: Keep the explicit integration helper because it fits the managed result model cleanly.
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

    // Legacy mapping: f_xfoil/src/xfoil.f :: incompressible Cp relation lineage.
    // Difference from legacy: The formula is the same Bernoulli-style relation, but it is exposed as a standalone managed helper.
    // Decision: Keep the helper because it makes the pressure recovery stage clear.
    private static double ComputeIncompressiblePressureCoefficient(double tangentialVelocity, double freestreamVelocity)
    {
        return 1d - Math.Pow(tangentialVelocity / freestreamVelocity, 2d);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: COMSET/Karman-Tsien correction lineage.
    // Difference from legacy: The correction is applied directly to Cp values in the managed inviscid result flow rather than through the original shared compressibility state.
    // Decision: Keep the helper because it makes the managed compressibility step explicit.
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

    // Legacy mapping: managed-only angle conversion helper with no direct Fortran analogue.
    // Difference from legacy: XFoil stores and updates angles in mixed degree/radian command state instead of centralizing the conversion in one helper.
    // Decision: Keep the helper because it makes the managed API boundary cleaner.
    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private readonly record struct PanelInfluence(
        double SourceNormal,
        double SourceTangential,
        double VortexNormal,
        double VortexTangential);
}

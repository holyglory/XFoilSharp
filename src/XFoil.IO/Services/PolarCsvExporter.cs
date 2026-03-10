using System.Globalization;
using System.Text;
using XFoil.IO.Models;
using XFoil.Solver.Models;

namespace XFoil.IO.Services;

public sealed class PolarCsvExporter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Format(PolarSweepResult sweep)
    {
        if (sweep is null)
        {
            throw new ArgumentNullException(nameof(sweep));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: InviscidAlphaSweep",
            $"# Geometry: {sweep.Geometry.Name}",
            $"# PanelCount: {FormatInteger(sweep.Settings.PanelCount)}",
            $"# MachNumber: {FormatDouble(sweep.Settings.MachNumber)}",
            $"# FreestreamVelocity: {FormatDouble(sweep.Settings.FreestreamVelocity)}",
            "AngleOfAttackDegrees,LiftCoefficient,DragCoefficient,CorrectedPressureIntegratedLiftCoefficient,CorrectedPressureIntegratedDragCoefficient,MomentCoefficientQuarterChord,Circulation,PressureIntegratedLiftCoefficient,PressureIntegratedDragCoefficient",
        };

        foreach (var point in sweep.Points)
        {
            lines.Add(string.Join(
                ',',
                FormatDouble(point.AngleOfAttackDegrees),
                FormatDouble(point.LiftCoefficient),
                FormatDouble(point.DragCoefficient),
                FormatDouble(point.CorrectedPressureIntegratedLiftCoefficient),
                FormatDouble(point.CorrectedPressureIntegratedDragCoefficient),
                FormatDouble(point.MomentCoefficientQuarterChord),
                FormatDouble(point.Circulation),
                FormatDouble(point.PressureIntegratedLiftCoefficient),
                FormatDouble(point.PressureIntegratedDragCoefficient)));
        }

        return string.Join('\n', lines) + "\n";
    }

    public string Format(ViscousPolarSweepResult sweep)
    {
        if (sweep is null)
        {
            throw new ArgumentNullException(nameof(sweep));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: ViscousAlphaSweep",
            $"# Geometry: {sweep.Geometry.Name}",
            $"# PanelCount: {FormatInteger(sweep.Settings.PanelCount)}",
            $"# MachNumber: {FormatDouble(sweep.Settings.MachNumber)}",
            $"# ReynoldsNumber: {FormatDouble(sweep.Settings.ReynoldsNumber)}",
            $"# TransitionReynoldsTheta: {FormatDouble(sweep.Settings.TransitionReynoldsTheta)}",
            $"# CriticalAmplificationFactor: {FormatDouble(sweep.Settings.CriticalAmplificationFactor)}",
            "AngleOfAttackDegrees,LiftCoefficient,EstimatedProfileDragCoefficient,MomentCoefficientQuarterChord,FinalSurfaceResidual,FinalTransitionResidual,FinalWakeResidual,OuterConverged,InnerInteractionConverged,FinalDisplacementRelaxation,FinalSeedEdgeVelocityChange",
        };

        foreach (var point in sweep.Points)
        {
            lines.Add(string.Join(
                ',',
                FormatDouble(point.AngleOfAttackDegrees),
                FormatDouble(point.LiftCoefficient),
                FormatDouble(point.EstimatedProfileDragCoefficient),
                FormatDouble(point.MomentCoefficientQuarterChord),
                FormatDouble(point.FinalSurfaceResidual),
                FormatDouble(point.FinalTransitionResidual),
                FormatDouble(point.FinalWakeResidual),
                FormatBoolean(point.OuterConverged),
                FormatBoolean(point.InnerInteractionConverged),
                FormatDouble(point.FinalDisplacementRelaxation),
                FormatDouble(point.FinalSeedEdgeVelocityChange)));
        }

        return string.Join('\n', lines) + "\n";
    }

    public string Format(InviscidLiftSweepResult sweep)
    {
        if (sweep is null)
        {
            throw new ArgumentNullException(nameof(sweep));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: InviscidLiftSweep",
            $"# Geometry: {sweep.Geometry.Name}",
            $"# PanelCount: {FormatInteger(sweep.Settings.PanelCount)}",
            $"# MachNumber: {FormatDouble(sweep.Settings.MachNumber)}",
            $"# FreestreamVelocity: {FormatDouble(sweep.Settings.FreestreamVelocity)}",
            "TargetLiftCoefficient,SolvedAngleOfAttackDegrees,LiftCoefficient,DragCoefficient,CorrectedPressureIntegratedLiftCoefficient,CorrectedPressureIntegratedDragCoefficient,MomentCoefficientQuarterChord,Circulation,PressureIntegratedLiftCoefficient,PressureIntegratedDragCoefficient",
        };

        foreach (var point in sweep.Points)
        {
            lines.Add(string.Join(
                ',',
                FormatDouble(point.TargetLiftCoefficient),
                FormatDouble(point.OperatingPoint.AngleOfAttackDegrees),
                FormatDouble(point.OperatingPoint.LiftCoefficient),
                FormatDouble(point.OperatingPoint.DragCoefficient),
                FormatDouble(point.OperatingPoint.CorrectedPressureIntegratedLiftCoefficient),
                FormatDouble(point.OperatingPoint.CorrectedPressureIntegratedDragCoefficient),
                FormatDouble(point.OperatingPoint.MomentCoefficientQuarterChord),
                FormatDouble(point.OperatingPoint.Circulation),
                FormatDouble(point.OperatingPoint.PressureIntegratedLiftCoefficient),
                FormatDouble(point.OperatingPoint.PressureIntegratedDragCoefficient)));
        }

        return string.Join('\n', lines) + "\n";
    }

    public string Format(ViscousLiftSweepResult sweep)
    {
        if (sweep is null)
        {
            throw new ArgumentNullException(nameof(sweep));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: ViscousLiftSweep",
            $"# Geometry: {sweep.Geometry.Name}",
            $"# PanelCount: {FormatInteger(sweep.Settings.PanelCount)}",
            $"# MachNumber: {FormatDouble(sweep.Settings.MachNumber)}",
            $"# ReynoldsNumber: {FormatDouble(sweep.Settings.ReynoldsNumber)}",
            $"# TransitionReynoldsTheta: {FormatDouble(sweep.Settings.TransitionReynoldsTheta)}",
            $"# CriticalAmplificationFactor: {FormatDouble(sweep.Settings.CriticalAmplificationFactor)}",
            "TargetLiftCoefficient,SolvedAngleOfAttackDegrees,LiftCoefficient,EstimatedProfileDragCoefficient,MomentCoefficientQuarterChord,FinalSurfaceResidual,FinalTransitionResidual,FinalWakeResidual,OuterConverged,InnerInteractionConverged,FinalDisplacementRelaxation,FinalSeedEdgeVelocityChange",
        };

        foreach (var point in sweep.Points)
        {
            lines.Add(string.Join(
                ',',
                FormatDouble(point.TargetLiftCoefficient),
                FormatDouble(point.SolvedAngleOfAttackDegrees),
                FormatDouble(point.OperatingPoint.FinalAnalysis.LiftCoefficient),
                FormatDouble(point.OperatingPoint.EstimatedProfileDragCoefficient),
                FormatDouble(point.OperatingPoint.FinalAnalysis.MomentCoefficientQuarterChord),
                FormatDouble(point.OperatingPoint.FinalSolveResult.FinalSurfaceResidual),
                FormatDouble(point.OperatingPoint.FinalSolveResult.FinalTransitionResidual),
                FormatDouble(point.OperatingPoint.FinalSolveResult.FinalWakeResidual),
                FormatBoolean(point.OperatingPoint.Converged),
                FormatBoolean(point.OperatingPoint.InnerInteractionConverged),
                FormatDouble(point.OperatingPoint.FinalDisplacementRelaxation),
                FormatDouble(point.OperatingPoint.FinalSeedEdgeVelocityChange)));
        }

        return string.Join('\n', lines) + "\n";
    }

    public void Export(string path, PolarSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    public void Export(string path, ViscousPolarSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    public void Export(string path, InviscidLiftSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    public void Export(string path, ViscousLiftSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    public string Format(LegacyPolarFile polar)
    {
        if (polar is null)
        {
            throw new ArgumentNullException(nameof(polar));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: LegacySavedPolarImport",
            $"# Geometry: {polar.AirfoilName}",
            $"# SourceCode: {polar.SourceCode}",
            $"# ElementCount: {FormatInteger(polar.ElementCount)}",
        };

        if (polar.Version.HasValue)
        {
            lines.Add($"# Version: {FormatDouble(polar.Version.Value)}");
        }

        lines.Add($"# ReynoldsVariationType: {polar.ReynoldsVariationType}");
        lines.Add($"# MachVariationType: {polar.MachVariationType}");

        if (polar.ReferenceMachNumber.HasValue)
        {
            lines.Add($"# ReferenceMachNumber: {FormatDouble(polar.ReferenceMachNumber.Value)}");
        }

        if (polar.ReferenceReynoldsNumber.HasValue)
        {
            lines.Add($"# ReferenceReynoldsNumber: {FormatDouble(polar.ReferenceReynoldsNumber.Value)}");
        }

        if (polar.CriticalAmplificationFactor.HasValue)
        {
            lines.Add($"# CriticalAmplificationFactor: {FormatDouble(polar.CriticalAmplificationFactor.Value)}");
        }

        if (polar.PressureRatio.HasValue)
        {
            lines.Add($"# PressureRatio: {FormatDouble(polar.PressureRatio.Value)}");
        }

        if (polar.ThermalEfficiency.HasValue)
        {
            lines.Add($"# ThermalEfficiency: {FormatDouble(polar.ThermalEfficiency.Value)}");
        }

        foreach (var trip in polar.TripSettings.OrderBy(setting => setting.ElementIndex))
        {
            lines.Add(
                $"# TripElement{trip.ElementIndex}: Top={FormatDouble(trip.TopTrip)},Bottom={FormatDouble(trip.BottomTrip)}");
        }

        lines.Add(string.Join(',', polar.Columns.Select(column => column.Key)));

        foreach (var record in polar.Records)
        {
            lines.Add(string.Join(
                ',',
                polar.Columns.Select(column => FormatDouble(record.Values[column.Key]))));
        }

        return string.Join('\n', lines) + "\n";
    }

    public void Export(string path, LegacyPolarFile polar)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(polar));
    }

    public string Format(LegacyReferencePolarFile polar)
    {
        if (polar is null)
        {
            throw new ArgumentNullException(nameof(polar));
        }

        var lines = new List<string>
        {
            "# XFoil.CSharp Polar Export",
            "# Kind: LegacyReferencePolarImport",
        };

        if (!string.IsNullOrWhiteSpace(polar.Label))
        {
            lines.Add($"# Label: {polar.Label}");
        }

        lines.Add("BlockKind,XValue,YValue");
        foreach (var block in polar.Blocks)
        {
            foreach (var point in block.Points)
            {
                lines.Add(string.Join(
                    ',',
                    block.Kind,
                    FormatDouble(point.X),
                    FormatDouble(point.Y)));
            }
        }

        return string.Join('\n', lines) + "\n";
    }

    public void Export(string path, LegacyReferencePolarFile polar)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(polar));
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static void WriteFile(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content, Utf8WithoutBom);
    }
}

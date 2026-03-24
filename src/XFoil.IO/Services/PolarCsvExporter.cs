using System.Globalization;
using System.Text;
using XFoil.IO.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: PACC/PWRT/BLDUMP/CPDUMP saved-output workflow
// Role in port: Managed exporter that writes solver sweeps and imported legacy polar/reference data to stable CSV text.
// Differences: The exporter provides deterministic CSV projections of managed solver results and imported legacy formats rather than replaying the original interactive save-file writers exactly.
// Decision: Keep the managed exporter because it is an intentional IO layer for automation and tests, not a parity-execution path.
namespace XFoil.IO.Services;

public sealed class PolarCsvExporter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    // Legacy mapping: none directly; managed CSV export of an inviscid alpha sweep.
    // Difference from legacy: The original operating workflow accumulated polar files through interactive commands, while the port exposes a reusable CSV formatter over managed result objects.
    // Decision: Keep the managed formatter because it is the right API for automation.
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

        // Legacy block: Managed-only CSV row emission for the inviscid alpha sweep.
        // Difference: The exporter writes normalized managed result objects instead of the original interactive save-file format.
        // Decision: Keep the managed loop because it produces a stable automation-friendly export.
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

    // Legacy mapping: none directly; managed CSV export of a viscous alpha sweep.
    // Difference from legacy: The exported columns are tailored to the managed viscous result model rather than to the original polar save-file writer.
    // Decision: Keep the managed formatter because it exposes the managed solver outputs clearly.
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

        // Legacy block: Managed-only CSV row emission for the viscous alpha sweep.
        // Difference: The managed export includes Newton/interaction convergence diagnostics that are not laid out like the legacy save-file text.
        // Decision: Keep the managed loop because it reflects the managed result contract.
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

    // Legacy mapping: none directly; managed CSV export of an inviscid lift-target sweep.
    // Difference from legacy: The formatter projects managed lift-target results into CSV instead of replaying the legacy interactive save path.
    // Decision: Keep the managed formatter.
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

        // Legacy block: Managed-only CSV row emission for the inviscid lift-target sweep.
        // Difference: The export is derived from managed result objects rather than the original interactive save-file buffers.
        // Decision: Keep the managed loop because it is deterministic and automation-friendly.
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

    // Legacy mapping: none directly; managed CSV export of a viscous lift-target sweep.
    // Difference from legacy: The exported fields are aligned with the managed viscous lift result model rather than the old save-file layout.
    // Decision: Keep the managed formatter.
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

        // Legacy block: Managed-only CSV row emission for the viscous lift-target sweep.
        // Difference: The export reflects managed solver result objects instead of the original interactive save-file state.
        // Decision: Keep the managed loop because it is stable and explicit.
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

    // Legacy mapping: none; managed file writer for inviscid alpha sweep CSV output.
    // Difference from legacy: The original runtime did not expose this reusable file-writing API.
    // Decision: Keep the managed wrapper.
    public void Export(string path, PolarSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    // Legacy mapping: none; managed file writer for viscous alpha sweep CSV output.
    // Difference from legacy: File export is explicit and reusable instead of command-driven.
    // Decision: Keep the managed wrapper.
    public void Export(string path, ViscousPolarSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    // Legacy mapping: none; managed file writer for inviscid lift-target sweep CSV output.
    // Difference from legacy: File export is explicit and reusable instead of command-driven.
    // Decision: Keep the managed wrapper.
    public void Export(string path, InviscidLiftSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    // Legacy mapping: none; managed file writer for viscous lift-target sweep CSV output.
    // Difference from legacy: File export is explicit and reusable instead of command-driven.
    // Decision: Keep the managed wrapper.
    public void Export(string path, ViscousLiftSweepResult sweep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(sweep));
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: PACC/PWRT saved-polar text output lineage.
    // Difference from legacy: The exporter rewrites imported legacy polar data into normalized CSV with explicit metadata headers rather than reproducing the original save-file format verbatim.
    // Decision: Keep the managed formatter because the goal here is stable CSV export, not byte-for-byte legacy replay.
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

        // Legacy block: Managed-only metadata and trip-setting projection from the imported legacy polar object.
        // Difference: The original file already encoded this information in its own legacy text format; the exporter rewrites it into normalized CSV comments.
        // Decision: Keep the managed loop because it makes the imported metadata explicit.
        foreach (var trip in polar.TripSettings.OrderBy(setting => setting.ElementIndex))
        {
            lines.Add(
                $"# TripElement{trip.ElementIndex}: Top={FormatDouble(trip.TopTrip)},Bottom={FormatDouble(trip.BottomTrip)}");
        }

        lines.Add(string.Join(',', polar.Columns.Select(column => column.Key)));

        // Legacy block: Managed-only CSV row emission for imported legacy polar records.
        // Difference: The exporter rewrites the parsed record dictionaries into normalized CSV rather than replaying the legacy spacing and header layout.
        // Decision: Keep the managed loop because it is the purpose of this exporter.
        foreach (var record in polar.Records)
        {
            lines.Add(string.Join(
                ',',
                polar.Columns.Select(column => FormatDouble(record.Values[column.Key]))));
        }

        return string.Join('\n', lines) + "\n";
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: PWRT saved-polar output lineage through imported legacy polar data.
    // Difference from legacy: The port writes normalized CSV instead of the original saved-polar text format.
    // Decision: Keep the managed file writer because stable CSV is the intended output here.
    public void Export(string path, LegacyPolarFile polar)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(polar));
    }

    // Legacy mapping: none; managed CSV export of imported reference-polar comparison data.
    // Difference from legacy: This comparison-file format belongs to the managed tooling layer rather than the legacy runtime.
    // Decision: Keep the managed formatter.
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
        // Legacy block: Managed-only CSV row emission for imported reference-polar comparison blocks.
        // Difference: The reference-polar format is a managed tooling concern, not a legacy runtime writer.
        // Decision: Keep the managed nested loops because they make comparison data portable.
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

    // Legacy mapping: none; managed file writer for imported reference-polar comparison data.
    // Difference from legacy: This export path belongs to the managed tooling layer.
    // Decision: Keep the managed wrapper.
    public void Export(string path, LegacyReferencePolarFile polar)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        WriteFile(path, Format(polar));
    }

    // Legacy mapping: none; managed CSV-formatting helper.
    // Difference from legacy: The exporter normalizes numeric formatting for all CSV outputs through one helper.
    // Decision: Keep the managed helper.
    private static string FormatDouble(double value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    // Legacy mapping: none; managed CSV-formatting helper.
    // Difference from legacy: Integer formatting is centralized for exporter consistency.
    // Decision: Keep the managed helper.
    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // Legacy mapping: none; managed CSV-formatting helper.
    // Difference from legacy: Boolean formatting is normalized for exporter consistency.
    // Decision: Keep the managed helper.
    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    // Legacy mapping: none; managed file writer shared by all CSV export overloads.
    // Difference from legacy: Directory creation and UTF-8 file writing are centralized here instead of being woven into command handlers.
    // Decision: Keep the managed helper because it makes exporter behavior consistent.
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

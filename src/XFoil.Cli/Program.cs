// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xfoil.f :: ALFA/CLI/ASEQ/CSEQ, f_xfoil/src/xoper.f :: PACC/PWRT/DUMP, f_xfoil/src/xgdes.f :: GDES command family, f_xfoil/src/xqdes.f :: QDES command family, f_xfoil/src/xmdes.f :: MDES/MAPGEN command family
// Role in port: Headless CLI orchestration over the managed services that port XFoil analysis, design, IO, and session workflows.
// Differences: Classic XFoil is an interactive command interpreter with mutable COMMON state, while this file exposes deterministic argument parsing, command dispatch, and export formatting for the managed service layer.
// Decision: Keep the managed CLI because it is .NET-specific orchestration, not a direct solver-parity target. Legacy lineage is documented per helper where commands delegate into ported workflows.
using System.Globalization;
using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Design.Models;
using XFoil.Design.Services;
using XFoil.IO.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

var parser = new AirfoilParser();
var normalizer = new AirfoilNormalizer();
var metricsCalculator = new AirfoilMetricsCalculator();
var nacaGenerator = new NacaAirfoilGenerator();
var analysisService = new AirfoilAnalysisService();
var polarExporter = new PolarCsvExporter();
var sessionRunner = new AnalysisSessionRunner();
var legacyPolarImporter = new LegacyPolarImporter();
var legacyReferencePolarImporter = new LegacyReferencePolarImporter();
var legacyPolarDumpImporter = new LegacyPolarDumpImporter();
var legacyPolarDumpArchiveWriter = new LegacyPolarDumpArchiveWriter();
var flapDeflectionService = new FlapDeflectionService();
var trailingEdgeGapService = new TrailingEdgeGapService();
var leadingEdgeRadiusService = new LeadingEdgeRadiusService();
var geometryScalingService = new GeometryScalingService();
var basicGeometryTransformService = new BasicGeometryTransformService();
var contourEditService = new ContourEditService();
var contourModificationService = new ContourModificationService();
var qSpecDesignService = new QSpecDesignService();
var modalInverseDesignService = new ModalInverseDesignService();
var conformalMapgenService = new ConformalMapgenService();
var airfoilDatExporter = new AirfoilDatExporter();

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "summarize":
            if (args.Length < 2)
            {
                throw new ArgumentException("The summarize command requires a file path.");
            }

            var parsed = parser.ParseFile(args[1]);
            WriteSummary(parsed, normalizer, metricsCalculator);
            return 0;

        case "naca":
            if (args.Length < 2)
            {
                throw new ArgumentException("The naca command requires a 4-digit designation.");
            }

            var generated = nacaGenerator.Generate4Digit(args[1]);
            WriteSummary(generated, normalizer, metricsCalculator);
            return 0;

        case "inviscid-file":
            if (args.Length < 3)
            {
                throw new ArgumentException("The inviscid-file command requires a file path and angle of attack.");
            }

            var airfoilFromFile = parser.ParseFile(args[1]);
            var fileAlpha = ParseDouble(args[2], "angle of attack");
            var filePanels = args.Length >= 4 ? ParseInteger(args[3], "panel count") : 120;
            var fileMach = args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d;
            WriteInviscidSummary(airfoilFromFile, fileAlpha, filePanels, fileMach, analysisService);
            return 0;

        case "inviscid-naca":
            if (args.Length < 3)
            {
                throw new ArgumentException("The inviscid-naca command requires a 4-digit designation and angle of attack.");
            }

            var airfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            var nacaAlpha = ParseDouble(args[2], "angle of attack");
            var nacaPanels = args.Length >= 4 ? ParseInteger(args[3], "panel count") : 120;
            var nacaMach = args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d;
            WriteInviscidSummary(airfoilFromNaca, nacaAlpha, nacaPanels, nacaMach, analysisService);
            return 0;

        case "polar-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The polar-file command requires a file path, alpha start, alpha end, and alpha step.");
            }

            var polarAirfoilFromFile = parser.ParseFile(args[1]);
            WritePolarSummary(
                polarAirfoilFromFile,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                analysisService);
            return 0;

        case "polar-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The polar-naca command requires a 4-digit designation, alpha start, alpha end, and alpha step.");
            }

            var polarAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            WritePolarSummary(
                polarAirfoilFromNaca,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                analysisService);
            return 0;

        case "export-polar-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-polar-file command requires a file path, output path, alpha start, alpha end, and alpha step.");
            }

            var exportPolarAirfoilFromFile = parser.ParseFile(args[1]);
            ExportPolarCsv(
                exportPolarAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "alpha start"),
                ParseDouble(args[4], "alpha end"),
                ParseDouble(args[5], "alpha step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                polarExporter);
            return 0;

        case "export-polar-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-polar-naca command requires a 4-digit designation, output path, alpha start, alpha end, and alpha step.");
            }

            var exportPolarAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            ExportPolarCsv(
                exportPolarAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "alpha start"),
                ParseDouble(args[4], "alpha end"),
                ParseDouble(args[5], "alpha step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                polarExporter);
            return 0;

        case "export-polar-cl-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-polar-cl-file command requires a file path, output path, CL start, CL end, and CL step.");
            }

            var exportPolarClAirfoilFromFile = parser.ParseFile(args[1]);
            ExportLiftSweepCsv(
                exportPolarClAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "CL start"),
                ParseDouble(args[4], "CL end"),
                ParseDouble(args[5], "CL step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                polarExporter);
            return 0;

        case "export-polar-cl-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-polar-cl-naca command requires a 4-digit designation, output path, CL start, CL end, and CL step.");
            }

            var exportPolarClAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            ExportLiftSweepCsv(
                exportPolarClAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "CL start"),
                ParseDouble(args[4], "CL end"),
                ParseDouble(args[5], "CL step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                polarExporter);
            return 0;

        case "import-legacy-polar":
            if (args.Length < 3)
            {
                throw new ArgumentException("The import-legacy-polar command requires an input path and output CSV path.");
            }

            ImportLegacyPolar(args[1], args[2], legacyPolarImporter, polarExporter);
            return 0;

        case "show-legacy-polar":
            if (args.Length < 2)
            {
                throw new ArgumentException("The show-legacy-polar command requires an input path.");
            }

            WriteLegacyPolarSummary(args[1], legacyPolarImporter);
            return 0;

        case "import-legacy-reference-polar":
            if (args.Length < 3)
            {
                throw new ArgumentException("The import-legacy-reference-polar command requires an input path and output CSV path.");
            }

            ImportLegacyReferencePolar(args[1], args[2], legacyReferencePolarImporter, polarExporter);
            return 0;

        case "show-legacy-reference-polar":
            if (args.Length < 2)
            {
                throw new ArgumentException("The show-legacy-reference-polar command requires an input path.");
            }

            WriteLegacyReferencePolarSummary(args[1], legacyReferencePolarImporter);
            return 0;

        case "import-legacy-polar-dump":
            if (args.Length < 3)
            {
                throw new ArgumentException("The import-legacy-polar-dump command requires an input path and output summary CSV path.");
            }

            ImportLegacyPolarDump(args[1], args[2], legacyPolarDumpImporter, legacyPolarDumpArchiveWriter);
            return 0;

        case "show-legacy-polar-dump":
            if (args.Length < 2)
            {
                throw new ArgumentException("The show-legacy-polar-dump command requires an input path.");
            }

            WriteLegacyPolarDumpSummary(args[1], legacyPolarDumpImporter);
            return 0;

        case "design-flap-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The design-flap-file command requires an input path, output path, hinge X, hinge Y, and deflection angle.");
            }

            var flapAirfoilFromFile = parser.ParseFile(args[1]);
            ExportFlapGeometry(
                flapAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "hinge X"),
                ParseDouble(args[4], "hinge Y"),
                ParseDouble(args[5], "deflection angle"),
                flapDeflectionService,
                airfoilDatExporter);
            return 0;

        case "design-flap-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The design-flap-naca command requires a 4-digit designation, output path, hinge X, hinge Y, and deflection angle.");
            }

            var flapAirfoilFromNaca = nacaGenerator.Generate4Digit(
                args[1],
                args.Length >= 7 ? ParseInteger(args[6], "point count") : 161);
            ExportFlapGeometry(
                flapAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "hinge X"),
                ParseDouble(args[4], "hinge Y"),
                ParseDouble(args[5], "deflection angle"),
                flapDeflectionService,
                airfoilDatExporter);
            return 0;

        case "set-te-gap-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The set-te-gap-file command requires an input path, output path, and target gap.");
            }

            var teGapAirfoilFromFile = parser.ParseFile(args[1]);
            ExportTrailingEdgeGapGeometry(
                teGapAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "target gap"),
                args.Length >= 5 ? ParseDouble(args[4], "blend distance chord fraction") : 1d,
                trailingEdgeGapService,
                airfoilDatExporter);
            return 0;

        case "set-te-gap-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The set-te-gap-naca command requires a 4-digit designation, output path, and target gap.");
            }

            var teGapAirfoilFromNaca = nacaGenerator.Generate4Digit(
                args[1],
                args.Length >= 6 ? ParseInteger(args[5], "point count") : 161);
            ExportTrailingEdgeGapGeometry(
                teGapAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "target gap"),
                args.Length >= 5 ? ParseDouble(args[4], "blend distance chord fraction") : 1d,
                trailingEdgeGapService,
                airfoilDatExporter);
            return 0;

        case "addp-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The addp-file command requires an input path, output path, insert index, x, and y.");
            }

            ExportContourEditGeometry(
                contourEditService.AddPoint(
                    parser.ParseFile(args[1]),
                    ParseInteger(args[3], "insert index"),
                    new AirfoilPoint(
                        ParseDouble(args[4], "x"),
                        ParseDouble(args[5], "y"))),
                args[2],
                airfoilDatExporter);
            return 0;

        case "addp-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The addp-naca command requires a 4-digit designation, output path, insert index, x, and y.");
            }

            ExportContourEditGeometry(
                contourEditService.AddPoint(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 7 ? ParseInteger(args[6], "point count") : 161),
                    ParseInteger(args[3], "insert index"),
                    new AirfoilPoint(
                        ParseDouble(args[4], "x"),
                        ParseDouble(args[5], "y"))),
                args[2],
                airfoilDatExporter);
            return 0;

        case "movp-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The movp-file command requires an input path, output path, point index, x, and y.");
            }

            ExportContourEditGeometry(
                contourEditService.MovePoint(
                    parser.ParseFile(args[1]),
                    ParseInteger(args[3], "point index"),
                    new AirfoilPoint(
                        ParseDouble(args[4], "x"),
                        ParseDouble(args[5], "y"))),
                args[2],
                airfoilDatExporter);
            return 0;

        case "movp-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The movp-naca command requires a 4-digit designation, output path, point index, x, and y.");
            }

            ExportContourEditGeometry(
                contourEditService.MovePoint(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 7 ? ParseInteger(args[6], "point count") : 161),
                    ParseInteger(args[3], "point index"),
                    new AirfoilPoint(
                        ParseDouble(args[4], "x"),
                        ParseDouble(args[5], "y"))),
                args[2],
                airfoilDatExporter);
            return 0;

        case "delp-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The delp-file command requires an input path, output path, and point index.");
            }

            ExportContourEditGeometry(
                contourEditService.DeletePoint(
                    parser.ParseFile(args[1]),
                    ParseInteger(args[3], "point index")),
                args[2],
                airfoilDatExporter);
            return 0;

        case "delp-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The delp-naca command requires a 4-digit designation, output path, and point index.");
            }

            ExportContourEditGeometry(
                contourEditService.DeletePoint(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 5 ? ParseInteger(args[4], "point count") : 161),
                    ParseInteger(args[3], "point index")),
                args[2],
                airfoilDatExporter);
            return 0;

        case "corn-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The corn-file command requires an input path, output path, and point index.");
            }

            ExportContourEditGeometry(
                contourEditService.DoublePoint(
                    parser.ParseFile(args[1]),
                    ParseInteger(args[3], "point index")),
                args[2],
                airfoilDatExporter);
            return 0;

        case "corn-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The corn-naca command requires a 4-digit designation, output path, and point index.");
            }

            ExportContourEditGeometry(
                contourEditService.DoublePoint(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 5 ? ParseInteger(args[4], "point count") : 161),
                    ParseInteger(args[3], "point index")),
                args[2],
                airfoilDatExporter);
            return 0;

        case "cadd-file":
            if (args.Length < 4 || args.Length == 6 || args.Length > 7)
            {
                throw new ArgumentException("The cadd-file command requires an input path, output path, a corner angle threshold, an optional parameter mode, and an optional x-range.");
            }

            ExportContourEditGeometry(
                contourEditService.RefineCorners(
                    parser.ParseFile(args[1]),
                    ParseDouble(args[3], "corner angle threshold"),
                    args.Length >= 5 ? ParseCornerRefinementMode(args[4]) : CornerRefinementParameterMode.ArcLength,
                    args.Length >= 7 ? ParseDouble(args[5], "minimum x") : null,
                    args.Length >= 7 ? ParseDouble(args[6], "maximum x") : null),
                args[2],
                airfoilDatExporter);
            return 0;

        case "cadd-naca":
            if (args.Length < 4 || args.Length > 8)
            {
                throw new ArgumentException("The cadd-naca command requires a 4-digit designation, output path, a corner angle threshold, an optional parameter mode, an optional x-range, and an optional point count.");
            }

            var caddNacaMode = CornerRefinementParameterMode.ArcLength;
            var caddNacaPointCount = 161;
            double? caddNacaMinimumX = null;
            double? caddNacaMaximumX = null;
            if (args.Length >= 5)
            {
                if (TryParseCornerRefinementMode(args[4], out var parsedMode))
                {
                    caddNacaMode = parsedMode;
                    if (args.Length == 6)
                    {
                        caddNacaPointCount = ParseInteger(args[5], "point count");
                    }
                    else if (args.Length >= 7)
                    {
                        caddNacaMinimumX = ParseDouble(args[5], "minimum x");
                        caddNacaMaximumX = ParseDouble(args[6], "maximum x");
                        if (args.Length == 8)
                        {
                            caddNacaPointCount = ParseInteger(args[7], "point count");
                        }
                    }
                }
                else
                {
                    if (args.Length != 5)
                    {
                        throw new ArgumentException("The cadd-naca command only allows pointCount without a parameter mode when no x-range is specified.");
                    }

                    caddNacaPointCount = ParseInteger(args[4], "point count");
                }
            }

            ExportContourEditGeometry(
                contourEditService.RefineCorners(
                    nacaGenerator.Generate4Digit(args[1], caddNacaPointCount),
                    ParseDouble(args[3], "corner angle threshold"),
                    caddNacaMode,
                    caddNacaMinimumX,
                    caddNacaMaximumX),
                args[2],
                airfoilDatExporter);
            return 0;

        case "modi-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The modi-file command requires an input path, output path, and control-points path.");
            }

            ExportContourModificationGeometry(
                contourModificationService.ModifyContour(
                    parser.ParseFile(args[1]),
                    ParseControlPointsFile(args[3]),
                    args.Length >= 5 ? ParseBooleanFlag(args[4], "match endpoint slope") : true),
                args[2],
                airfoilDatExporter);
            return 0;

        case "modi-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The modi-naca command requires a 4-digit designation, output path, and control-points path.");
            }

            var modiNacaMatchSlope = args.Length >= 5 ? ParseBooleanFlag(args[4], "match endpoint slope") : true;
            var modiNacaPointCount = args.Length >= 6 ? ParseInteger(args[5], "point count") : 161;
            ExportContourModificationGeometry(
                contourModificationService.ModifyContour(
                    nacaGenerator.Generate4Digit(args[1], modiNacaPointCount),
                    ParseControlPointsFile(args[3]),
                    modiNacaMatchSlope),
                args[2],
                airfoilDatExporter);
            return 0;

        case "qdes-profile-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The qdes-profile-file command requires an input path, output CSV path, and angle of attack.");
            }

            ExportQSpecProfile(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-profile-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The qdes-profile-naca command requires a 4-digit designation, output CSV path, and angle of attack.");
            }

            ExportQSpecProfile(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 7 ? ParseInteger(args[6], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-symm-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The qdes-symm-file command requires an input path, output CSV path, and angle of attack.");
            }

            ExportSymmetricQSpecProfile(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-symm-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The qdes-symm-naca command requires a 4-digit designation, output CSV path, and angle of attack.");
            }

            ExportSymmetricQSpecProfile(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 7 ? ParseInteger(args[6], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-aq-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-aq-file command requires an input path, output CSV path, panel count, Mach number, and at least one alpha.");
            }

            ExportQSpecProfileSetForAngles(
                parser.ParseFile(args[1]),
                args[2],
                ParseInteger(args[3], "panel count"),
                ParseDouble(args[4], "Mach number"),
                ParseRemainingDoubles(args, 5, "alpha"),
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-aq-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-aq-naca command requires a 4-digit designation, output CSV path, panel count, Mach number, and at least one alpha.");
            }

            ExportQSpecProfileSetForAngles(
                nacaGenerator.Generate4Digit(args[1]),
                args[2],
                ParseInteger(args[3], "panel count"),
                ParseDouble(args[4], "Mach number"),
                ParseRemainingDoubles(args, 5, "alpha"),
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-cq-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-cq-file command requires an input path, output CSV path, panel count, Mach number, and at least one CL target.");
            }

            ExportQSpecProfileSetForLiftCoefficients(
                parser.ParseFile(args[1]),
                args[2],
                ParseInteger(args[3], "panel count"),
                ParseDouble(args[4], "Mach number"),
                ParseRemainingDoubles(args, 5, "target CL"),
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-cq-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-cq-naca command requires a 4-digit designation, output CSV path, panel count, Mach number, and at least one CL target.");
            }

            ExportQSpecProfileSetForLiftCoefficients(
                nacaGenerator.Generate4Digit(args[1]),
                args[2],
                ParseInteger(args[3], "panel count"),
                ParseDouble(args[4], "Mach number"),
                ParseRemainingDoubles(args, 5, "target CL"),
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-modi-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The qdes-modi-file command requires an input path, output CSV path, angle of attack, and control-points path.");
            }

            ExportModifiedQSpecProfile(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-modi-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The qdes-modi-naca command requires a 4-digit designation, output CSV path, angle of attack, and control-points path.");
            }

            ExportModifiedQSpecProfile(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 9 ? ParseInteger(args[8], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-smoo-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-smoo-file command requires an input path, output CSV path, angle of attack, plot x1, and plot x2.");
            }

            ExportSmoothedQSpecProfile(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseDouble(args[4], "plot x1"),
                ParseDouble(args[5], "plot x2"),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseDouble(args[7], "smoothing length factor") : 0.002d,
                args.Length >= 9 ? ParseInteger(args[8], "panel count") : 120,
                args.Length >= 10 ? ParseDouble(args[9], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-smoo-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The qdes-smoo-naca command requires a 4-digit designation, output CSV path, angle of attack, plot x1, and plot x2.");
            }

            ExportSmoothedQSpecProfile(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 11 ? ParseInteger(args[10], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseDouble(args[4], "plot x1"),
                ParseDouble(args[5], "plot x2"),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseDouble(args[7], "smoothing length factor") : 0.002d,
                args.Length >= 9 ? ParseInteger(args[8], "panel count") : 120,
                args.Length >= 10 ? ParseDouble(args[9], "Mach number") : 0d,
                analysisService,
                qSpecDesignService);
            return 0;

        case "qdes-exec-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The qdes-exec-file command requires an input path, output DAT path, angle of attack, and control-points path.");
            }

            ExportExecutedQSpecGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                airfoilDatExporter);
            return 0;

        case "qdes-exec-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The qdes-exec-naca command requires a 4-digit designation, output DAT path, angle of attack, and control-points path.");
            }

            ExportExecutedQSpecGeometry(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 10 ? ParseInteger(args[9], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                airfoilDatExporter);
            return 0;

        case "mdes-spec-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The mdes-spec-file command requires an input path, output CSV path, and angle of attack.");
            }

            ExportModalSpectrum(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                args.Length >= 7 ? ParseInteger(args[6], "mode count") : 12,
                args.Length >= 8 ? ParseDouble(args[7], "filter strength") : 0.15d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService);
            return 0;

        case "mdes-spec-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The mdes-spec-naca command requires a 4-digit designation, output CSV path, and angle of attack.");
            }

            ExportModalSpectrum(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 9 ? ParseInteger(args[8], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 120,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                args.Length >= 7 ? ParseInteger(args[6], "mode count") : 12,
                args.Length >= 8 ? ParseDouble(args[7], "filter strength") : 0.15d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService);
            return 0;

        case "mdes-exec-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mdes-exec-file command requires an input path, output DAT path, angle of attack, and control-points path.");
            }

            ExportModalExecutedGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "mode count") : 12,
                args.Length >= 10 ? ParseDouble(args[9], "filter strength") : 0.15d,
                args.Length >= 11 ? ParseDouble(args[10], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService,
                airfoilDatExporter);
            return 0;

        case "mdes-exec-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mdes-exec-naca command requires a 4-digit designation, output DAT path, angle of attack, and control-points path.");
            }

            ExportModalExecutedGeometry(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 12 ? ParseInteger(args[11], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "mode count") : 12,
                args.Length >= 10 ? ParseDouble(args[9], "filter strength") : 0.15d,
                args.Length >= 11 ? ParseDouble(args[10], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService,
                airfoilDatExporter);
            return 0;

        case "mdes-pert-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mdes-pert-file command requires an input path, output DAT path, angle of attack, mode index, and coefficient delta.");
            }

            ExportPerturbedModalGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseInteger(args[4], "mode index"),
                ParseDouble(args[5], "coefficient delta"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "mode count") : 12,
                args.Length >= 10 ? ParseDouble(args[9], "filter strength") : 0.15d,
                args.Length >= 11 ? ParseDouble(args[10], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService,
                airfoilDatExporter);
            return 0;

        case "mdes-pert-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mdes-pert-naca command requires a 4-digit designation, output DAT path, angle of attack, mode index, and coefficient delta.");
            }

            ExportPerturbedModalGeometry(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 12 ? ParseInteger(args[11], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseInteger(args[4], "mode index"),
                ParseDouble(args[5], "coefficient delta"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "mode count") : 12,
                args.Length >= 10 ? ParseDouble(args[9], "filter strength") : 0.15d,
                args.Length >= 11 ? ParseDouble(args[10], "max displacement fraction") : 0.02d,
                analysisService,
                qSpecDesignService,
                modalInverseDesignService,
                airfoilDatExporter);
            return 0;

        case "mapgen-exec-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mapgen-exec-file command requires an input path, output DAT path, angle of attack, and control-points path.");
            }

            ExportConformalMapgenGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "circle point count") : 129,
                args.Length >= 10 ? ParseInteger(args[9], "max Newton iterations") : 10,
                args.Length >= 12
                    ? new AirfoilPoint(ParseDouble(args[10], "target TE gap dx"), ParseDouble(args[11], "target TE gap dy"))
                    : (AirfoilPoint?)null,
                null,
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "mapgen-exec-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mapgen-exec-naca command requires a 4-digit designation, output DAT path, angle of attack, and control-points path.");
            }

            var mapgenExecNacaPointCount =
                args.Length == 11 ? ParseInteger(args[10], "point count")
                : args.Length >= 13 ? ParseInteger(args[12], "point count")
                : 161;
            AirfoilPoint? mapgenExecNacaTargetGap =
                args.Length == 12 || args.Length >= 13
                    ? new AirfoilPoint(ParseDouble(args[10], "target TE gap dx"), ParseDouble(args[11], "target TE gap dy"))
                    : (AirfoilPoint?)null;

            ExportConformalMapgenGeometry(
                nacaGenerator.Generate4Digit(args[1], mapgenExecNacaPointCount),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "circle point count") : 129,
                args.Length >= 10 ? ParseInteger(args[9], "max Newton iterations") : 10,
                mapgenExecNacaTargetGap,
                null,
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "mapgen-spec-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mapgen-spec-file command requires an input path, output CSV path, angle of attack, and control-points path.");
            }

            ExportConformalMapgenSpectrum(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "circle point count") : 129,
                args.Length >= 10 ? ParseInteger(args[9], "max Newton iterations") : 10,
                args.Length >= 12
                    ? new AirfoilPoint(ParseDouble(args[10], "target TE gap dx"), ParseDouble(args[11], "target TE gap dy"))
                    : (AirfoilPoint?)null,
                null,
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService);
            return 0;

        case "mapgen-spec-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The mapgen-spec-naca command requires a 4-digit designation, output CSV path, angle of attack, and control-points path.");
            }

            var mapgenSpecNacaPointCount =
                args.Length == 11 ? ParseInteger(args[10], "point count")
                : args.Length >= 13 ? ParseInteger(args[12], "point count")
                : 161;
            AirfoilPoint? mapgenSpecNacaTargetGap =
                args.Length == 12 || args.Length >= 13
                    ? new AirfoilPoint(ParseDouble(args[10], "target TE gap dx"), ParseDouble(args[11], "target TE gap dy"))
                    : (AirfoilPoint?)null;

            ExportConformalMapgenSpectrum(
                nacaGenerator.Generate4Digit(args[1], mapgenSpecNacaPointCount),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 6 ? ParseBooleanFlag(args[5], "match endpoint slope") : true,
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseInteger(args[8], "circle point count") : 129,
                args.Length >= 10 ? ParseInteger(args[9], "max Newton iterations") : 10,
                mapgenSpecNacaTargetGap,
                null,
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService);
            return 0;

        case "mapgen-filt-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-filt-file command requires an input path, output DAT path, angle of attack, control-points path, and filter exponent.");
            }

            ExportConformalMapgenGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                null,
                ParseDouble(args[5], "filter exponent"),
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "mapgen-filt-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-filt-naca command requires a 4-digit designation, output DAT path, angle of attack, control-points path, and filter exponent.");
            }

            ExportConformalMapgenGeometry(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 12 ? ParseInteger(args[11], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                null,
                ParseDouble(args[5], "filter exponent"),
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "mapgen-filt-spec-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-filt-spec-file command requires an input path, output CSV path, angle of attack, control-points path, and filter exponent.");
            }

            ExportConformalMapgenSpectrum(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                null,
                ParseDouble(args[5], "filter exponent"),
                analysisService,
                qSpecDesignService,
                conformalMapgenService);
            return 0;

        case "mapgen-filt-spec-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-filt-spec-naca command requires a 4-digit designation, output CSV path, angle of attack, control-points path, and filter exponent.");
            }

            ExportConformalMapgenSpectrum(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 12 ? ParseInteger(args[11], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                null,
                ParseDouble(args[5], "filter exponent"),
                analysisService,
                qSpecDesignService,
                conformalMapgenService);
            return 0;

        case "mapgen-tang-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-tang-file command requires an input path, output DAT path, angle of attack, control-points path, and target TE angle in degrees.");
            }

            ExportConformalMapgenGeometry(
                parser.ParseFile(args[1]),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                ParseDouble(args[5], "target TE angle"),
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "mapgen-tang-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The mapgen-tang-naca command requires a 4-digit designation, output DAT path, angle of attack, control-points path, and target TE angle in degrees.");
            }

            ExportConformalMapgenGeometry(
                nacaGenerator.Generate4Digit(args[1], args.Length >= 12 ? ParseInteger(args[11], "point count") : 161),
                args[2],
                ParseDouble(args[3], "angle of attack"),
                ParseControlPointsFile(args[4]),
                args.Length >= 7 ? ParseBooleanFlag(args[6], "match endpoint slope") : true,
                args.Length >= 8 ? ParseInteger(args[7], "panel count") : 120,
                args.Length >= 9 ? ParseDouble(args[8], "Mach number") : 0d,
                args.Length >= 10 ? ParseInteger(args[9], "circle point count") : 129,
                args.Length >= 11 ? ParseInteger(args[10], "max Newton iterations") : 10,
                null,
                ParseDouble(args[5], "target TE angle"),
                0d,
                analysisService,
                qSpecDesignService,
                conformalMapgenService,
                airfoilDatExporter);
            return 0;

        case "adeg-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The adeg-file command requires an input path, output path, and angle in degrees.");
            }

            ExportGeometry(
                basicGeometryTransformService.RotateDegrees(parser.ParseFile(args[1]), ParseDouble(args[3], "angle in degrees")),
                args[2],
                "RotateDegrees",
                airfoilDatExporter);
            return 0;

        case "adeg-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The adeg-naca command requires a 4-digit designation, output path, and angle in degrees.");
            }

            ExportGeometry(
                basicGeometryTransformService.RotateDegrees(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 5 ? ParseInteger(args[4], "point count") : 161),
                    ParseDouble(args[3], "angle in degrees")),
                args[2],
                "RotateDegrees",
                airfoilDatExporter);
            return 0;

        case "arad-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The arad-file command requires an input path, output path, and angle in radians.");
            }

            ExportGeometry(
                basicGeometryTransformService.RotateRadians(parser.ParseFile(args[1]), ParseDouble(args[3], "angle in radians")),
                args[2],
                "RotateRadians",
                airfoilDatExporter);
            return 0;

        case "arad-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The arad-naca command requires a 4-digit designation, output path, and angle in radians.");
            }

            ExportGeometry(
                basicGeometryTransformService.RotateRadians(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 5 ? ParseInteger(args[4], "point count") : 161),
                    ParseDouble(args[3], "angle in radians")),
                args[2],
                "RotateRadians",
                airfoilDatExporter);
            return 0;

        case "tran-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The tran-file command requires an input path, output path, delta x, and delta y.");
            }

            ExportGeometry(
                basicGeometryTransformService.Translate(
                    parser.ParseFile(args[1]),
                    ParseDouble(args[3], "delta x"),
                    ParseDouble(args[4], "delta y")),
                args[2],
                "Translate",
                airfoilDatExporter);
            return 0;

        case "tran-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The tran-naca command requires a 4-digit designation, output path, delta x, and delta y.");
            }

            ExportGeometry(
                basicGeometryTransformService.Translate(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 6 ? ParseInteger(args[5], "point count") : 161),
                    ParseDouble(args[3], "delta x"),
                    ParseDouble(args[4], "delta y")),
                args[2],
                "Translate",
                airfoilDatExporter);
            return 0;

        case "scal-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The scal-file command requires an input path, output path, and scale factor.");
            }

            ExportGeometry(
                basicGeometryTransformService.ScaleAboutOrigin(
                    parser.ParseFile(args[1]),
                    ParseDouble(args[3], "x scale factor"),
                    args.Length >= 5 ? ParseDouble(args[4], "y scale factor") : ParseDouble(args[3], "x scale factor")),
                args[2],
                "ScaleAboutOrigin",
                airfoilDatExporter);
            return 0;

        case "scal-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The scal-naca command requires a 4-digit designation, output path, and scale factor.");
            }

            ExportGeometry(
                basicGeometryTransformService.ScaleAboutOrigin(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 6 ? ParseInteger(args[5], "point count") : 161),
                    ParseDouble(args[3], "x scale factor"),
                    args.Length >= 5 ? ParseDouble(args[4], "y scale factor") : ParseDouble(args[3], "x scale factor")),
                args[2],
                "ScaleAboutOrigin",
                airfoilDatExporter);
            return 0;

        case "lins-file":
            if (args.Length < 7)
            {
                throw new ArgumentException("The lins-file command requires an input path, output path, x/c1, y-scale1, x/c2, and y-scale2.");
            }

            ExportGeometry(
                basicGeometryTransformService.ScaleYLinearly(
                    parser.ParseFile(args[1]),
                    ParseDouble(args[3], "x/c 1"),
                    ParseDouble(args[4], "y scale 1"),
                    ParseDouble(args[5], "x/c 2"),
                    ParseDouble(args[6], "y scale 2")),
                args[2],
                "LinearYScale",
                airfoilDatExporter);
            return 0;

        case "lins-naca":
            if (args.Length < 7)
            {
                throw new ArgumentException("The lins-naca command requires a 4-digit designation, output path, x/c1, y-scale1, x/c2, and y-scale2.");
            }

            ExportGeometry(
                basicGeometryTransformService.ScaleYLinearly(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 8 ? ParseInteger(args[7], "point count") : 161),
                    ParseDouble(args[3], "x/c 1"),
                    ParseDouble(args[4], "y scale 1"),
                    ParseDouble(args[5], "x/c 2"),
                    ParseDouble(args[6], "y scale 2")),
                args[2],
                "LinearYScale",
                airfoilDatExporter);
            return 0;

        case "dero-file":
            if (args.Length < 3)
            {
                throw new ArgumentException("The dero-file command requires an input path and output path.");
            }

            ExportGeometry(
                basicGeometryTransformService.Derotate(parser.ParseFile(args[1])),
                args[2],
                "Derotate",
                airfoilDatExporter);
            return 0;

        case "dero-naca":
            if (args.Length < 3)
            {
                throw new ArgumentException("The dero-naca command requires a 4-digit designation and output path.");
            }

            ExportGeometry(
                basicGeometryTransformService.Derotate(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 4 ? ParseInteger(args[3], "point count") : 161)),
                args[2],
                "Derotate",
                airfoilDatExporter);
            return 0;

        case "unit-file":
            if (args.Length < 3)
            {
                throw new ArgumentException("The unit-file command requires an input path and output path.");
            }

            ExportGeometry(
                basicGeometryTransformService.NormalizeUnitChord(parser.ParseFile(args[1])),
                args[2],
                "NormalizeUnitChord",
                airfoilDatExporter);
            return 0;

        case "unit-naca":
            if (args.Length < 3)
            {
                throw new ArgumentException("The unit-naca command requires a 4-digit designation and output path.");
            }

            ExportGeometry(
                basicGeometryTransformService.NormalizeUnitChord(
                    nacaGenerator.Generate4Digit(args[1], args.Length >= 4 ? ParseInteger(args[3], "point count") : 161)),
                args[2],
                "NormalizeUnitChord",
                airfoilDatExporter);
            return 0;

        case "set-le-radius-file":
            if (args.Length < 4)
            {
                throw new ArgumentException("The set-le-radius-file command requires an input path, output path, and LE radius scale factor.");
            }

            var leRadiusAirfoilFromFile = parser.ParseFile(args[1]);
            ExportLeadingEdgeRadiusGeometry(
                leRadiusAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "LE radius scale factor"),
                args.Length >= 5 ? ParseDouble(args[4], "blend distance chord fraction") : 1d,
                leadingEdgeRadiusService,
                airfoilDatExporter);
            return 0;

        case "set-le-radius-naca":
            if (args.Length < 4)
            {
                throw new ArgumentException("The set-le-radius-naca command requires a 4-digit designation, output path, and LE radius scale factor.");
            }

            var leRadiusAirfoilFromNaca = nacaGenerator.Generate4Digit(
                args[1],
                args.Length >= 6 ? ParseInteger(args[5], "point count") : 161);
            ExportLeadingEdgeRadiusGeometry(
                leRadiusAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "LE radius scale factor"),
                args.Length >= 5 ? ParseDouble(args[4], "blend distance chord fraction") : 1d,
                leadingEdgeRadiusService,
                airfoilDatExporter);
            return 0;

        case "scale-geometry-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The scale-geometry-file command requires an input path, output path, scale factor, and origin kind.");
            }

            var scaledAirfoilFromFile = parser.ParseFile(args[1]);
            ExportScaledGeometry(
                scaledAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "scale factor"),
                ParseScaleOrigin(args[4]),
                args.Length >= 7
                    ? new AirfoilPoint(ParseDouble(args[5], "origin X"), ParseDouble(args[6], "origin Y"))
                    : null,
                geometryScalingService,
                airfoilDatExporter);
            return 0;

        case "scale-geometry-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The scale-geometry-naca command requires a 4-digit designation, output path, scale factor, and origin kind.");
            }

            var scaledAirfoilFromNaca = nacaGenerator.Generate4Digit(
                args[1],
                args.Length >= 8 ? ParseInteger(args[7], "point count") : 161);
            ExportScaledGeometry(
                scaledAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "scale factor"),
                ParseScaleOrigin(args[4]),
                args.Length >= 7
                    ? new AirfoilPoint(ParseDouble(args[5], "origin X"), ParseDouble(args[6], "origin Y"))
                    : null,
                geometryScalingService,
                airfoilDatExporter);
            return 0;

        case "viscous-polar-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-file command requires a file path, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarAirfoilFromFile = parser.ParseFile(args[1]);
            WriteViscousPolarSummary(
                viscousPolarAirfoilFromFile,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseInteger(args[8], "coupling iterations") : 2,
                args.Length >= 10 ? ParseInteger(args[9], "viscous iterations") : 8,
                args.Length >= 11 ? ParseDouble(args[10], "residual tolerance") : 0.3d,
                args.Length >= 12 ? ParseDouble(args[11], "displacement relaxation") : 0.5d,
                args.Length >= 13 ? ParseDouble(args[12], "transition Reynolds-theta") : 320d,
                args.Length >= 14 ? ParseDouble(args[13], "critical amplification factor") : 9d,
                analysisService);
            return 0;

        case "viscous-polar-file-double":
            // Phase 2 iter 61: doubled-tree variant for arbitrary .dat files.
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-file-double command requires a file path, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarAirfoilFromFileDouble = parser.ParseFile(args[1]);
            WriteViscousPolarSummaryDouble(
                viscousPolarAirfoilFromFileDouble,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 160,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseDouble(args[8], "transition Reynolds-theta") : 320d,
                args.Length >= 10 ? ParseDouble(args[9], "critical amplification factor") : 9d);
            return 0;

        case "viscous-polar-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-naca command requires a 4-digit designation, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarAirfoilFromNaca = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousPolarSummary(
                viscousPolarAirfoilFromNaca,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseInteger(args[8], "coupling iterations") : 2,
                args.Length >= 10 ? ParseInteger(args[9], "viscous iterations") : 8,
                args.Length >= 11 ? ParseDouble(args[10], "residual tolerance") : 0.3d,
                args.Length >= 12 ? ParseDouble(args[11], "displacement relaxation") : 0.5d,
                args.Length >= 13 ? ParseDouble(args[12], "transition Reynolds-theta") : 320d,
                args.Length >= 14 ? ParseDouble(args[13], "critical amplification factor") : 9d,
                analysisService);
            return 0;

        case "viscous-polar-naca-double":
            // Phase 2: doubled-tree (native double-precision) viscous polar.
            // Same arg layout as viscous-polar-naca for parity. Routes through
            // XFoil.Solver.Double.Services.AirfoilAnalysisService with the
            // Phase-2-standardized settings (panels default 160, useExtendedWake=true,
            // maxIter=200, tol=1e-5, TrustRegion default).
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-naca-double command requires a 4-digit designation, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarAirfoilFromNacaDouble = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousPolarSummaryDouble(
                viscousPolarAirfoilFromNacaDouble,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 160,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseDouble(args[8], "transition Reynolds-theta") : 320d,
                args.Length >= 10 ? ParseDouble(args[9], "critical amplification factor") : 9d);
            return 0;

        case "viscous-point-mses":
            // MSES-thesis single-point viscous (Phase-5 stub).
            // Inviscid CL + Squire-Young CD from composite lam→turb
            // marcher. No viscous Ue feedback yet; outputs finite
            // CL/CD suitable for benchmarking against Modern.
            if (args.Length < 3)
            {
                throw new ArgumentException("The viscous-point-mses command requires a 4-digit designation and alpha (degrees).");
            }
            var msesAirfoil = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousSinglePointMses(
                msesAirfoil,
                ParseDouble(args[2], "alpha"),
                args.Length >= 4 ? ParseInteger(args[3], "panel count") : 160,
                args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d,
                args.Length >= 6 ? ParseDouble(args[5], "Reynolds number") : 1_000_000d,
                args.Length >= 7 ? ParseDouble(args[6], "critical amplification factor") : 9d);
            return 0;

        case "viscous-polar-mses":
            // MSES polar sweep. Runs viscous-point-mses at each α in
            // [start, end] with given step.
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-mses command requires a 4-digit designation, alpha start, alpha end, alpha step.");
            }
            var polarMsesNaca = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousPolarMses(
                polarMsesNaca,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 160,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseDouble(args[8], "critical amplification factor") : 9d,
                outputCsvPath: null);
            return 0;

        case "export-profile-mses":
            // MSES per-station BL profile dump to CSV.
            if (args.Length < 4)
            {
                throw new ArgumentException("The export-profile-mses command requires a 4-digit designation, alpha, and CSV path.");
            }
            var profileMsesAirfoil = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteMsesProfileDump(
                profileMsesAirfoil,
                ParseDouble(args[2], "alpha"),
                args[3],
                args.Length >= 5 ? ParseInteger(args[4], "panel count") : 160,
                args.Length >= 6 ? ParseDouble(args[5], "Mach number") : 0d,
                args.Length >= 7 ? ParseDouble(args[6], "Reynolds number") : 1_000_000d,
                args.Length >= 8 ? ParseDouble(args[7], "critical amplification factor") : 9d);
            return 0;

        case "export-polar-mses":
            // MSES polar sweep → CSV.
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-polar-mses command requires a 4-digit designation, CSV path, alpha start, alpha end, alpha step.");
            }
            var exportMsesNaca = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousPolarMses(
                exportMsesNaca,
                ParseDouble(args[3], "alpha start"),
                ParseDouble(args[4], "alpha end"),
                ParseDouble(args[5], "alpha step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 160,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "Reynolds number") : 1_000_000d,
                args.Length >= 10 ? ParseDouble(args[9], "critical amplification factor") : 9d,
                outputCsvPath: args[2]);
            return 0;

        case "viscous-point-mses-file":
            // MSES-thesis single-point viscous from an airfoil file.
            // Accepts Selig/XFoil .dat format via AirfoilParser.
            if (args.Length < 3)
            {
                throw new ArgumentException("The viscous-point-mses-file command requires a file path and alpha (degrees).");
            }
            var msesFileAirfoil = parser.ParseFile(args[1]);
            WriteViscousSinglePointMses(
                msesFileAirfoil,
                ParseDouble(args[2], "alpha"),
                args.Length >= 4 ? ParseInteger(args[3], "panel count") : 160,
                args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d,
                args.Length >= 6 ? ParseDouble(args[5], "Reynolds number") : 1_000_000d,
                args.Length >= 7 ? ParseDouble(args[6], "critical amplification factor") : 9d);
            return 0;

        case "viscous-point-modern":
            // Phase 3: single-alpha viscous analysis on Modern (#3) tree.
            // Unlike viscous-polar-naca-modern which uses SweepViscousAlpha
            // (currently identical to Doubled per B1 v6 deferred), this
            // command goes through Modern.AnalyzeViscous — the path where
            // Tier A1 multi-start AND Tier B1 solution-adaptive paneling
            // BOTH actually apply. At coarse N (≤ 100) B1 biases panel
            // distribution by Cp gradient; A1 retries failed physical
            // convergence with jittered alphas. Best for single-point
            // queries where Modern's algorithmic improvements are
            // demonstrably used.
            if (args.Length < 3)
            {
                throw new ArgumentException("The viscous-point-modern command requires a 4-digit designation and alpha (degrees).");
            }

            var pointAirfoilModern = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousSinglePointModern(
                pointAirfoilModern,
                ParseDouble(args[2], "alpha"),
                args.Length >= 4 ? ParseInteger(args[3], "panel count") : 160,
                args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d,
                args.Length >= 6 ? ParseDouble(args[5], "Reynolds number") : 1_000_000d,
                args.Length >= 7 ? ParseDouble(args[6], "transition Reynolds-theta") : 320d,
                args.Length >= 8 ? ParseDouble(args[7], "critical amplification factor") : 9d);
            return 0;

        case "viscous-polar-naca-modern":
            // Phase 3: Modern (#3) viscous polar — inherits from the doubled tree,
            // adds Tier A1 multi-start retry and Tier A2 fresh-state Type-3 sweeps.
            // Same args + settings as viscous-polar-naca-double.
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-naca-modern command requires a 4-digit designation, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarAirfoilFromNacaModern = nacaGenerator.Generate4DigitClassic(args[1], pointCount: 239);
            WriteViscousPolarSummaryModern(
                viscousPolarAirfoilFromNacaModern,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 160,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseDouble(args[8], "transition Reynolds-theta") : 320d,
                args.Length >= 10 ? ParseDouble(args[9], "critical amplification factor") : 9d);
            return 0;

        case "viscous-polar-file-modern":
            // Phase 3: Modern tree polar sweep from an airfoil coordinate file.
            // Same ergonomics as viscous-polar-file-double but routes through
            // the Modern facade so v7 auto-ramp rescues apply to suspicious
            // stall-region points. Useful for Selig-database airfoils where
            // the NACA 4-digit generator path doesn't apply.
            if (args.Length < 5)
            {
                throw new ArgumentException("The viscous-polar-file-modern command requires a file path, alpha start, alpha end, and alpha step.");
            }

            var viscousPolarFromFileModern = parser.ParseFile(args[1]);
            WriteViscousPolarSummaryModern(
                viscousPolarFromFileModern,
                ParseDouble(args[2], "alpha start"),
                ParseDouble(args[3], "alpha end"),
                ParseDouble(args[4], "alpha step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 160,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                args.Length >= 8 ? ParseDouble(args[7], "Reynolds number") : 1_000_000d,
                args.Length >= 9 ? ParseDouble(args[8], "transition Reynolds-theta") : 320d,
                args.Length >= 10 ? ParseDouble(args[9], "critical amplification factor") : 9d);
            return 0;

        case "export-viscous-polar-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-viscous-polar-file command requires a file path, output path, alpha start, alpha end, and alpha step.");
            }

            var exportViscousPolarAirfoilFromFile = parser.ParseFile(args[1]);
            ExportViscousPolarCsv(
                exportViscousPolarAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "alpha start"),
                ParseDouble(args[4], "alpha end"),
                ParseDouble(args[5], "alpha step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "Reynolds number") : 1_000_000d,
                args.Length >= 10 ? ParseInteger(args[9], "coupling iterations") : 2,
                args.Length >= 11 ? ParseInteger(args[10], "viscous iterations") : 8,
                args.Length >= 12 ? ParseDouble(args[11], "residual tolerance") : 0.3d,
                args.Length >= 13 ? ParseDouble(args[12], "displacement relaxation") : 0.5d,
                args.Length >= 14 ? ParseDouble(args[13], "transition Reynolds-theta") : 320d,
                args.Length >= 15 ? ParseDouble(args[14], "critical amplification factor") : 9d,
                analysisService,
                polarExporter);
            return 0;

        case "export-viscous-polar-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-viscous-polar-naca command requires a 4-digit designation, output path, alpha start, alpha end, and alpha step.");
            }

            var exportViscousPolarAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            ExportViscousPolarCsv(
                exportViscousPolarAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "alpha start"),
                ParseDouble(args[4], "alpha end"),
                ParseDouble(args[5], "alpha step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "Reynolds number") : 1_000_000d,
                args.Length >= 10 ? ParseInteger(args[9], "coupling iterations") : 2,
                args.Length >= 11 ? ParseInteger(args[10], "viscous iterations") : 8,
                args.Length >= 12 ? ParseDouble(args[11], "residual tolerance") : 0.3d,
                args.Length >= 13 ? ParseDouble(args[12], "displacement relaxation") : 0.5d,
                args.Length >= 14 ? ParseDouble(args[13], "transition Reynolds-theta") : 320d,
                args.Length >= 15 ? ParseDouble(args[14], "critical amplification factor") : 9d,
                analysisService,
                polarExporter);
            return 0;

        case "export-viscous-polar-cl-file":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-viscous-polar-cl-file command requires a file path, output path, CL start, CL end, and CL step.");
            }

            var exportViscousPolarClAirfoilFromFile = parser.ParseFile(args[1]);
            ExportViscousLiftSweepCsv(
                exportViscousPolarClAirfoilFromFile,
                args[2],
                ParseDouble(args[3], "CL start"),
                ParseDouble(args[4], "CL end"),
                ParseDouble(args[5], "CL step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "Reynolds number") : 1_000_000d,
                args.Length >= 10 ? ParseInteger(args[9], "coupling iterations") : 2,
                args.Length >= 11 ? ParseInteger(args[10], "viscous iterations") : 8,
                args.Length >= 12 ? ParseDouble(args[11], "residual tolerance") : 0.3d,
                args.Length >= 13 ? ParseDouble(args[12], "displacement relaxation") : 0.5d,
                args.Length >= 14 ? ParseDouble(args[13], "transition Reynolds-theta") : 320d,
                args.Length >= 15 ? ParseDouble(args[14], "critical amplification factor") : 9d,
                analysisService,
                polarExporter);
            return 0;

        case "export-viscous-polar-cl-naca":
            if (args.Length < 6)
            {
                throw new ArgumentException("The export-viscous-polar-cl-naca command requires a 4-digit designation, output path, CL start, CL end, and CL step.");
            }

            var exportViscousPolarClAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            ExportViscousLiftSweepCsv(
                exportViscousPolarClAirfoilFromNaca,
                args[2],
                ParseDouble(args[3], "CL start"),
                ParseDouble(args[4], "CL end"),
                ParseDouble(args[5], "CL step"),
                args.Length >= 7 ? ParseInteger(args[6], "panel count") : 120,
                args.Length >= 8 ? ParseDouble(args[7], "Mach number") : 0d,
                args.Length >= 9 ? ParseDouble(args[8], "Reynolds number") : 1_000_000d,
                args.Length >= 10 ? ParseInteger(args[9], "coupling iterations") : 2,
                args.Length >= 11 ? ParseInteger(args[10], "viscous iterations") : 8,
                args.Length >= 12 ? ParseDouble(args[11], "residual tolerance") : 0.3d,
                args.Length >= 13 ? ParseDouble(args[12], "displacement relaxation") : 0.5d,
                args.Length >= 14 ? ParseDouble(args[13], "transition Reynolds-theta") : 320d,
                args.Length >= 15 ? ParseDouble(args[14], "critical amplification factor") : 9d,
                analysisService,
                polarExporter);
            return 0;

        case "solve-cl-file":
            if (args.Length < 3)
            {
                throw new ArgumentException("The solve-cl-file command requires a file path and target lift coefficient.");
            }

            var clAirfoilFromFile = parser.ParseFile(args[1]);
            WriteTargetLiftSummary(
                clAirfoilFromFile,
                ParseDouble(args[2], "target lift coefficient"),
                args.Length >= 4 ? ParseInteger(args[3], "panel count") : 120,
                args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d,
                analysisService);
            return 0;

        case "solve-cl-naca":
            if (args.Length < 3)
            {
                throw new ArgumentException("The solve-cl-naca command requires a 4-digit designation and target lift coefficient.");
            }

            var clAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            WriteTargetLiftSummary(
                clAirfoilFromNaca,
                ParseDouble(args[2], "target lift coefficient"),
                args.Length >= 4 ? ParseInteger(args[3], "panel count") : 120,
                args.Length >= 5 ? ParseDouble(args[4], "Mach number") : 0d,
                analysisService);
            return 0;

        case "polar-cl-file":
            if (args.Length < 5)
            {
                throw new ArgumentException("The polar-cl-file command requires a file path, CL start, CL end, and CL step.");
            }

            var polarClAirfoilFromFile = parser.ParseFile(args[1]);
            WriteLiftSweepSummary(
                polarClAirfoilFromFile,
                ParseDouble(args[2], "CL start"),
                ParseDouble(args[3], "CL end"),
                ParseDouble(args[4], "CL step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                analysisService);
            return 0;

        case "polar-cl-naca":
            if (args.Length < 5)
            {
                throw new ArgumentException("The polar-cl-naca command requires a 4-digit designation, CL start, CL end, and CL step.");
            }

            var polarClAirfoilFromNaca = nacaGenerator.Generate4Digit(args[1]);
            WriteLiftSweepSummary(
                polarClAirfoilFromNaca,
                ParseDouble(args[2], "CL start"),
                ParseDouble(args[3], "CL end"),
                ParseDouble(args[4], "CL step"),
                args.Length >= 6 ? ParseInteger(args[5], "panel count") : 120,
                args.Length >= 7 ? ParseDouble(args[6], "Mach number") : 0d,
                analysisService);
            return 0;

        case "run-session":
            if (args.Length < 3)
            {
                throw new ArgumentException("The run-session command requires a manifest path and output directory.");
            }

            RunSession(args[1], args[2], sessionRunner);
            return 0;

        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

// Legacy mapping: f_xfoil/src/aread.f :: AREAD, f_xfoil/src/naca.f :: NACA4/NACA5, f_xfoil/src/xgeom.f :: NORM/GEOPAR.
// Difference from legacy: This helper prints a deterministic managed summary instead of driving the interactive prompt and mutable geometry buffers.
// Decision: Keep the managed summary projection because it is CLI presentation over existing ported services.
static void WriteSummary(
    AirfoilGeometry geometry,
    AirfoilNormalizer normalizer,
    AirfoilMetricsCalculator metricsCalculator)
{
    var normalized = normalizer.Normalize(geometry);
    var metrics = metricsCalculator.Calculate(normalized);

    Console.WriteLine($"Name: {normalized.Name}");
    Console.WriteLine($"Format: {normalized.Format}");
    Console.WriteLine($"Points: {normalized.Points.Count}");
    Console.WriteLine($"Chord: {metrics.Chord.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"ArcLength: {metrics.TotalArcLength.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"MaxThickness: {metrics.MaxThickness.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"MaxCamber: {metrics.MaxCamber.ToString("F6", CultureInfo.InvariantCulture)}");
}

// Legacy mapping: none; classic XFoil exposes commands through an interactive REPL rather than a headless usage banner.
// Difference from legacy: The managed CLI publishes a static command catalog and argument contract for scripted use.
// Decision: Keep the managed usage banner because the headless command surface has no direct Fortran analogue.
static void PrintUsage()
{
    Console.WriteLine("XFoil.Cli");
    Console.WriteLine("  summarize <path>   Parse and summarize an airfoil file.");
    Console.WriteLine("  naca <####>        Generate and summarize a NACA 4-digit airfoil.");
    Console.WriteLine("  inviscid-file <path> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  inviscid-naca <####> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  polar-file <path> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach]");
    Console.WriteLine("  polar-naca <####> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach]");
    Console.WriteLine("  export-polar-file <path> <outputCsvPath> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach]");
    Console.WriteLine("  export-polar-naca <####> <outputCsvPath> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach]");
    Console.WriteLine("  export-polar-cl-file <path> <outputCsvPath> <clStart> <clEnd> <clStep> [panels] [mach]");
    Console.WriteLine("  export-polar-cl-naca <####> <outputCsvPath> <clStart> <clEnd> <clStep> [panels] [mach]");
    Console.WriteLine("  import-legacy-polar <inputPolarPath> <outputCsvPath>");
    Console.WriteLine("  show-legacy-polar <inputPolarPath>");
    Console.WriteLine("  import-legacy-reference-polar <inputRefPath> <outputCsvPath>");
    Console.WriteLine("  show-legacy-reference-polar <inputRefPath>");
    Console.WriteLine("  import-legacy-polar-dump <inputDumpPath> <outputSummaryCsvPath>");
    Console.WriteLine("  show-legacy-polar-dump <inputDumpPath>");
    Console.WriteLine("  design-flap-file <inputPath> <outputDatPath> <hingeX> <hingeY> <deflectionDeg>");
    Console.WriteLine("  design-flap-naca <####> <outputDatPath> <hingeX> <hingeY> <deflectionDeg> [pointCount]");
    Console.WriteLine("  set-te-gap-file <inputPath> <outputDatPath> <targetGap> [blendDistanceChordFraction]");
    Console.WriteLine("  set-te-gap-naca <####> <outputDatPath> <targetGap> [blendDistanceChordFraction] [pointCount]");
    Console.WriteLine("  addp-file <inputPath> <outputDatPath> <insertIndex> <x> <y>");
    Console.WriteLine("  addp-naca <####> <outputDatPath> <insertIndex> <x> <y> [pointCount]");
    Console.WriteLine("  movp-file <inputPath> <outputDatPath> <pointIndex> <x> <y>");
    Console.WriteLine("  movp-naca <####> <outputDatPath> <pointIndex> <x> <y> [pointCount]");
    Console.WriteLine("  delp-file <inputPath> <outputDatPath> <pointIndex>");
    Console.WriteLine("  delp-naca <####> <outputDatPath> <pointIndex> [pointCount]");
    Console.WriteLine("  corn-file <inputPath> <outputDatPath> <pointIndex>");
    Console.WriteLine("  corn-naca <####> <outputDatPath> <pointIndex> [pointCount]");
    Console.WriteLine("  cadd-file <inputPath> <outputDatPath> <angleDeg> [uniform|arclength] [xMin xMax]");
    Console.WriteLine("  cadd-naca <####> <outputDatPath> <angleDeg> [uniform|arclength] [pointCount | xMin xMax [pointCount]]");
    Console.WriteLine("  modi-file <inputPath> <outputDatPath> <controlPointsPath> [matchSlope]");
    Console.WriteLine("  modi-naca <####> <outputDatPath> <controlPointsPath> [matchSlope] [pointCount]");
    Console.WriteLine("  qdes-profile-file <inputPath> <outputCsvPath> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  qdes-profile-naca <####> <outputCsvPath> <alphaDeg> [panels] [mach] [pointCount]");
    Console.WriteLine("  qdes-symm-file <inputPath> <outputCsvPath> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  qdes-symm-naca <####> <outputCsvPath> <alphaDeg> [panels] [mach] [pointCount]");
    Console.WriteLine("  qdes-aq-file <inputPath> <outputCsvPath> <panels> <mach> <alpha1> [alpha2 ...]");
    Console.WriteLine("  qdes-aq-naca <####> <outputCsvPath> <panels> <mach> <alpha1> [alpha2 ...]");
    Console.WriteLine("  qdes-cq-file <inputPath> <outputCsvPath> <panels> <mach> <cl1> [cl2 ...]");
    Console.WriteLine("  qdes-cq-naca <####> <outputCsvPath> <panels> <mach> <cl1> [cl2 ...]");
    Console.WriteLine("  qdes-modi-file <inputPath> <outputCsvPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach]");
    Console.WriteLine("  qdes-modi-naca <####> <outputCsvPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [pointCount]");
    Console.WriteLine("  qdes-smoo-file <inputPath> <outputCsvPath> <alphaDeg> <plotX1> <plotX2> [matchSlope] [smoothFactor] [panels] [mach]");
    Console.WriteLine("  qdes-smoo-naca <####> <outputCsvPath> <alphaDeg> <plotX1> <plotX2> [matchSlope] [smoothFactor] [panels] [mach] [pointCount]");
    Console.WriteLine("  qdes-exec-file <inputPath> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [maxDispFrac]");
    Console.WriteLine("  qdes-exec-naca <####> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [maxDispFrac] [pointCount]");
    Console.WriteLine("  mdes-spec-file <inputPath> <outputCsvPath> <alphaDeg> [panels] [mach] [modeCount] [filterStrength]");
    Console.WriteLine("  mdes-spec-naca <####> <outputCsvPath> <alphaDeg> [panels] [mach] [modeCount] [filterStrength] [pointCount]");
    Console.WriteLine("  mdes-exec-file <inputPath> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [modeCount] [filterStrength] [maxDispFrac]");
    Console.WriteLine("  mdes-exec-naca <####> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [modeCount] [filterStrength] [maxDispFrac] [pointCount]");
    Console.WriteLine("  mdes-pert-file <inputPath> <outputDatPath> <alphaDeg> <modeIndex> <coeffDelta> [panels] [mach] [modeCount] [filterStrength] [maxDispFrac]");
    Console.WriteLine("  mdes-pert-naca <####> <outputDatPath> <alphaDeg> <modeIndex> <coeffDelta> [panels] [mach] [modeCount] [filterStrength] [maxDispFrac] [pointCount]");
    Console.WriteLine("  mapgen-exec-file <inputPath> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [targetGapDx targetGapDy]");
    Console.WriteLine("  mapgen-exec-naca <####> <outputDatPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [pointCount | targetGapDx targetGapDy | targetGapDx targetGapDy pointCount]");
    Console.WriteLine("  mapgen-spec-file <inputPath> <outputCsvPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [targetGapDx targetGapDy]");
    Console.WriteLine("  mapgen-spec-naca <####> <outputCsvPath> <alphaDeg> <controlPointsPath> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [pointCount | targetGapDx targetGapDy | targetGapDx targetGapDy pointCount]");
    Console.WriteLine("  mapgen-filt-file <inputPath> <outputDatPath> <alphaDeg> <controlPointsPath> <filterExponent> [matchSlope] [panels] [mach] [circlePoints] [maxNewton]");
    Console.WriteLine("  mapgen-filt-naca <####> <outputDatPath> <alphaDeg> <controlPointsPath> <filterExponent> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [pointCount]");
    Console.WriteLine("  mapgen-filt-spec-file <inputPath> <outputCsvPath> <alphaDeg> <controlPointsPath> <filterExponent> [matchSlope] [panels] [mach] [circlePoints] [maxNewton]");
    Console.WriteLine("  mapgen-filt-spec-naca <####> <outputCsvPath> <alphaDeg> <controlPointsPath> <filterExponent> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [pointCount]");
    Console.WriteLine("  mapgen-tang-file <inputPath> <outputDatPath> <alphaDeg> <controlPointsPath> <targetTeAngleDeg> [matchSlope] [panels] [mach] [circlePoints] [maxNewton]");
    Console.WriteLine("  mapgen-tang-naca <####> <outputDatPath> <alphaDeg> <controlPointsPath> <targetTeAngleDeg> [matchSlope] [panels] [mach] [circlePoints] [maxNewton] [pointCount]");
    Console.WriteLine("  adeg-file <inputPath> <outputDatPath> <angleDeg>");
    Console.WriteLine("  adeg-naca <####> <outputDatPath> <angleDeg> [pointCount]");
    Console.WriteLine("  arad-file <inputPath> <outputDatPath> <angleRad>");
    Console.WriteLine("  arad-naca <####> <outputDatPath> <angleRad> [pointCount]");
    Console.WriteLine("  tran-file <inputPath> <outputDatPath> <deltaX> <deltaY>");
    Console.WriteLine("  tran-naca <####> <outputDatPath> <deltaX> <deltaY> [pointCount]");
    Console.WriteLine("  scal-file <inputPath> <outputDatPath> <xScale> [yScale]");
    Console.WriteLine("  scal-naca <####> <outputDatPath> <xScale> [yScale] [pointCount]");
    Console.WriteLine("  lins-file <inputPath> <outputDatPath> <xoc1> <yScale1> <xoc2> <yScale2>");
    Console.WriteLine("  lins-naca <####> <outputDatPath> <xoc1> <yScale1> <xoc2> <yScale2> [pointCount]");
    Console.WriteLine("  dero-file <inputPath> <outputDatPath>");
    Console.WriteLine("  dero-naca <####> <outputDatPath> [pointCount]");
    Console.WriteLine("  unit-file <inputPath> <outputDatPath>");
    Console.WriteLine("  unit-naca <####> <outputDatPath> [pointCount]");
    Console.WriteLine("  set-le-radius-file <inputPath> <outputDatPath> <radiusScaleFactor> [blendDistanceChordFraction]");
    Console.WriteLine("  set-le-radius-naca <####> <outputDatPath> <radiusScaleFactor> [blendDistanceChordFraction] [pointCount]");
    Console.WriteLine("  scale-geometry-file <inputPath> <outputDatPath> <scaleFactor> <LE|TE|POINT> [originX originY]");
    Console.WriteLine("  scale-geometry-naca <####> <outputDatPath> <scaleFactor> <LE|TE|POINT> [originX originY] [pointCount]");
    Console.WriteLine("  viscous-polar-file <path> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-polar-naca <####> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-polar-naca-double <####> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [transitionReTheta] [criticalN]   (Phase 2: native double-precision tree)");
    Console.WriteLine("  viscous-polar-naca-modern <####> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [transitionReTheta] [criticalN]   (Phase 3: modern tree, multi-start retry on non-physical results)");
    Console.WriteLine("  viscous-point-modern <####> <alpha> [panels=160] [mach] [reynolds] [transitionReTheta] [criticalN]   (Phase 3: single-alpha modern analysis — A1 multi-start for non-physical results)");
    Console.WriteLine("  viscous-point-mses <####> <alpha> [panels=160] [mach] [reynolds] [criticalN]   (MSES-thesis closure, Phase-5 stub — inviscid CL + Squire-Young CD)");
    Console.WriteLine("  viscous-point-mses-file <path> <alpha> [panels=160] [mach] [reynolds] [criticalN]   (MSES single-point from arbitrary airfoil .dat)");
    Console.WriteLine("  viscous-polar-mses <####> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [criticalN]   (MSES polar sweep)");
    Console.WriteLine("  export-polar-mses <####> <outputCsvPath> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [criticalN]   (MSES polar sweep → CSV)");
    Console.WriteLine("  export-profile-mses <####> <alpha> <outputCsvPath> [panels=160] [mach] [reynolds] [criticalN]   (MSES per-station BL profile → CSV)");
    Console.WriteLine("    (set XFOIL_MSES_THESIS_EXACT=1 to use the Phase-2e implicit-Newton turbulent marcher instead of the Clauser-placeholder)");
    Console.WriteLine("    (set XFOIL_MSES_WAKE=1 to integrate Squire-Young at the wake far-field via the Phase-2f wake marcher)");
    Console.WriteLine("    (set XFOIL_MSES_THESIS_LAMINAR=1 to use the implicit-Newton ThesisExactLaminarMarcher for the pre-transition leg)");
    Console.WriteLine("  viscous-polar-file-double <path> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [transitionReTheta] [criticalN]   (Phase 2: doubled tree, arbitrary .dat)");
    Console.WriteLine("  viscous-polar-file-modern <path> <alphaStart> <alphaEnd> <alphaStep> [panels=160] [mach] [reynolds] [transitionReTheta] [criticalN]   (Phase 3: modern tree from .dat, v7 auto-ramp for stall rescue)");
    Console.WriteLine("  export-viscous-polar-file <path> <outputCsvPath> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  export-viscous-polar-naca <####> <outputCsvPath> <alphaStart> <alphaEnd> <alphaStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  export-viscous-polar-cl-file <path> <outputCsvPath> <clStart> <clEnd> <clStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  export-viscous-polar-cl-naca <####> <outputCsvPath> <clStart> <clEnd> <clStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  solve-cl-file <path> <targetCL> [panels] [mach]");
    Console.WriteLine("  solve-cl-naca <####> <targetCL> [panels] [mach]");
    Console.WriteLine("  viscous-solve-cl-file <path> <targetCL> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-solve-cl-naca <####> <targetCL> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  polar-cl-file <path> <clStart> <clEnd> <clStep> [panels] [mach]");
    Console.WriteLine("  polar-cl-naca <####> <clStart> <clEnd> <clStep> [panels] [mach]");
    Console.WriteLine("  viscous-polar-cl-file <path> <clStart> <clEnd> <clStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-polar-cl-naca <####> <clStart> <clEnd> <clStep> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  topology-file <path> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  topology-naca <####> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  viscous-seed-file <path> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  viscous-seed-naca <####> <alphaDeg> [panels] [mach]");
    Console.WriteLine("  viscous-init-file <path> <alphaDeg> [panels] [mach] [reynolds] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-init-naca <####> <alphaDeg> [panels] [mach] [reynolds] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-interval-file <path> <alphaDeg> [panels] [mach] [reynolds] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-interval-naca <####> <alphaDeg> [panels] [mach] [reynolds] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-correct-file <path> <alphaDeg> [panels] [mach] [reynolds] [iterations] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-correct-naca <####> <alphaDeg> [panels] [mach] [reynolds] [iterations] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-solve-file <path> <alphaDeg> [panels] [mach] [reynolds] [maxIterations] [residualTolerance] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-solve-naca <####> <alphaDeg> [panels] [mach] [reynolds] [maxIterations] [residualTolerance] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-interact-file <path> <alphaDeg> [panels] [mach] [reynolds] [interactionIterations] [couplingFactor] [viscousIterations] [residualTolerance] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-interact-naca <####> <alphaDeg> [panels] [mach] [reynolds] [interactionIterations] [couplingFactor] [viscousIterations] [residualTolerance] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-coupled-file <path> <alphaDeg> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  viscous-coupled-naca <####> <alphaDeg> [panels] [mach] [reynolds] [couplingIterations] [viscousIterations] [residualTolerance] [displacementRelaxation] [transitionReTheta] [criticalN]");
    Console.WriteLine("  run-session <manifestPath> <outputDirectory>");
}

// Legacy mapping: f_xfoil/src/xfoil.f :: ALFA and f_xfoil/src/xoper.f :: CPWRIT/PWRT reporting lineage.
// Difference from legacy: The managed helper delegates the analysis to services and formats a compact console summary instead of mutating global operating-point state and optional plot buffers.
// Decision: Keep the managed summary wrapper because it is presentation logic around the ported inviscid solve.
static void WriteInviscidSummary(
    AirfoilGeometry geometry,
    double angleOfAttackDegrees,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));

    Console.WriteLine($"Name: {geometry.Name}");
    Console.WriteLine($"Panels: {analysis.PanelCount}");
    Console.WriteLine($"AlphaDeg: {analysis.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Mach: {analysis.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CL: {analysis.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD: {analysis.DragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CL_pressure_corr: {analysis.CorrectedPressureIntegratedLiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD_pressure_corr: {analysis.CorrectedPressureIntegratedDragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CL_pressure: {analysis.PressureIntegratedLiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD_pressure: {analysis.PressureIntegratedDragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CMc/4: {analysis.MomentCoefficientQuarterChord.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Gamma: {analysis.Circulation.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"WakePoints: {analysis.Wake.Points.Count}");
    Console.WriteLine($"WakeLength: {analysis.Wake.Points[^1].DistanceFromTrailingEdge.ToString("F6", CultureInfo.InvariantCulture)}");
}

// Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ and f_xfoil/src/xoper.f :: PACC/PWRT.
// Difference from legacy: The sweep is executed through managed services and printed directly to stdout instead of being staged through the interactive polar accumulator.
// Decision: Keep the managed summary wrapper because it provides deterministic batch output for the same operating-point lineage.
static void WritePolarSummary(
    AirfoilGeometry geometry,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService)
{
    var sweep = analysisService.SweepInviscidAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        new AnalysisSettings(panelCount, machNumber: machNumber));

    Console.WriteLine($"Name: {geometry.Name}");
    Console.WriteLine($"Panels: {sweep.Settings.PanelCount}");
    Console.WriteLine($"Mach: {sweep.Settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine("AlphaDeg\tCL\tCD\tCLcorr\tCDcorr\tCMc/4\tGamma");

    foreach (var point in sweep.Points)
    {
        Console.WriteLine(
            $"{point.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"{point.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{point.DragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{point.CorrectedPressureIntegratedLiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{point.CorrectedPressureIntegratedDragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{point.MomentCoefficientQuarterChord.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{point.Circulation.ToString("F6", CultureInfo.InvariantCulture)}");
    }
}

// Legacy mapping: f_xfoil/src/xfoil.f :: ASEQ/VISC and f_xfoil/src/xoper.f :: PACC/PWRT.
// Difference from legacy: The helper formats managed viscous sweep results directly rather than replaying the interactive polar-buffer workflow.
// Decision: Keep the managed summary wrapper because it is a CLI view over the viscous solver service.
static void WriteViscousPolarSummary(
    AirfoilGeometry geometry,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    int couplingIterations,
    int viscousIterations,
    double residualTolerance,
    double displacementRelaxation,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor,
    AirfoilAnalysisService analysisService)
{
    var settings = CreateViscousSettings(panelCount, machNumber, reynoldsNumber, transitionReynoldsTheta, criticalAmplificationFactor);
    var results = analysisService.SweepViscousAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        settings);

    Console.WriteLine($"Name: {geometry.Name}");
    Console.WriteLine($"Panels: {settings.PanelCount}");
    Console.WriteLine($"Mach: {settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Re: {settings.ReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TransitionReTheta: {transitionReynoldsTheta.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {criticalAmplificationFactor.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine("AlphaDeg\tCL\tCD\tCM\tConverged\tIterations");

    foreach (var r in results)
    {
        // Same physicality classification as the doubled-tree CLI — float
        // facade also produces "Converged: True" non-physical attractors at
        // extreme α/Re. Mark them so downstream consumers don't treat them
        // as engineering data.
        string qualityTag = r.Converged
            ? (PhysicalEnvelope.IsAirfoilResultPhysical(r) ? "PHYSICAL" : "NON-PHYSICAL")
            : "DIVERGED";
        Console.WriteLine(
            $"{r.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"{r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.Converged}\t" +
            $"{r.Iterations}\t" +
            $"CDf={r.DragDecomposition.CDF.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"CDp={r.DragDecomposition.CDP.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_U={r.UpperTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_L={r.LowerTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Quality={qualityTag}");
    }
}

// Phase 2: doubled-tree (native double precision) viscous polar summary.
// Mirrors WriteViscousPolarSummary but routes through the doubled-tree
// facade. Uses Phase-2-standardized settings: useExtendedWake=true,
// maxIter=200, tol=1e-5, TrustRegion default.
static void WriteViscousPolarSummaryDouble(
    AirfoilGeometry geometry,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        transitionReynoldsTheta: transitionReynoldsTheta,
        criticalAmplificationFactor: criticalAmplificationFactor,

        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: 1e-5);

    var doubleService = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
    var results = doubleService.SweepViscousAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        settings);

    Console.WriteLine($"Name: {geometry.Name} (doubled tree)");
    Console.WriteLine($"Panels: {settings.PanelCount}");
    Console.WriteLine($"Mach: {settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Re: {settings.ReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TransitionReTheta: {transitionReynoldsTheta.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {criticalAmplificationFactor.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine("AlphaDeg\tCL\tCD\tCM\tConverged\tIterations");

    foreach (var r in results)
    {
        // PhysicalEnvelope flags converged results outside the realistic 2D
        // envelope (|CL|≤5, CD∈[0,1]) — those are non-physical attractors
        // even though Newton reports converged. POST-STALL tags non-
        // converged-but-physically-bounded results (Modern v7 auto-ramp
        // or Viterna extrapolation), distinct from true DIVERGED.
        string plausibleTag;
        if (r.Converged && PhysicalEnvelope.IsAirfoilResultPhysical(r))
            plausibleTag = "PHYSICAL";
        else if (!r.Converged && PhysicalEnvelope.IsAirfoilResultPhysicalPostStall(r))
            plausibleTag = "POST-STALL";
        else if (r.Converged)
            plausibleTag = "NON-PHYSICAL";
        else
            plausibleTag = "DIVERGED";
        Console.WriteLine(
            $"{r.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"{r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.Converged}\t" +
            $"{r.Iterations}\t" +
            $"CDf={r.DragDecomposition.CDF.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"CDp={r.DragDecomposition.CDP.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_U={r.UpperTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_L={r.LowerTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Quality={plausibleTag}");
    }
}

// MSES Phase-2e opt-in: setting XFOIL_MSES_THESIS_EXACT=1 switches
// all MSES CLI commands to run the implicit-Newton turbulent marcher
// (thesis eq. 6.10) instead of the Clauser-placeholder lag marcher.
static bool UseThesisExactTurbulentFromEnv()
{
    string? v = Environment.GetEnvironmentVariable("XFOIL_MSES_THESIS_EXACT");
    if (string.IsNullOrEmpty(v)) return false;
    return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
}

// MSES Phase-2f opt-in: setting XFOIL_MSES_WAKE=1 switches the
// Squire-Young CD integration from the airfoil TE to the wake
// far-field (half-chord downstream), using the wake marcher to
// relax the merged-TE state.
static bool UseWakeMarcherFromEnv()
{
    string? v = Environment.GetEnvironmentVariable("XFOIL_MSES_WAKE");
    if (string.IsNullOrEmpty(v)) return false;
    return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
}

// MSES Phase-2e laminar opt-in: setting XFOIL_MSES_THESIS_LAMINAR=1
// drives the pre-transition (θ, H) through the implicit-Newton
// ThesisExactLaminarMarcher instead of the Thwaites-λ marcher.
// Ñ tracking uses the same envelope e^N logic regardless.
static bool UseThesisExactLaminarFromEnv()
{
    string? v = Environment.GetEnvironmentVariable("XFOIL_MSES_THESIS_LAMINAR");
    if (string.IsNullOrEmpty(v)) return false;
    return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
}

static void WriteMsesProfileDump(
    AirfoilGeometry geometry,
    double alphaDegrees,
    string csvPath,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double criticalAmplificationFactor)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        nCritUpper: criticalAmplificationFactor,
        nCritLower: criticalAmplificationFactor);
    var mses = new XFoil.MsesSolver.Services.MsesAnalysisService(
        useThesisExactTurbulent: UseThesisExactTurbulentFromEnv(),
        useWakeMarcher: UseWakeMarcherFromEnv(),
        useThesisExactLaminar: UseThesisExactLaminarFromEnv());
    var r = mses.AnalyzeViscous(geometry, alphaDegrees, settings);

    using var writer = new System.IO.StreamWriter(csvPath);
    writer.WriteLine("# MSES BL profile dump");
    writer.WriteLine($"# airfoil={geometry.Name}, alpha={alphaDegrees}, panels={panelCount}, mach={machNumber}, re={reynoldsNumber}, nCrit={criticalAmplificationFactor}");
    writer.WriteLine($"# CL={r.LiftCoefficient:F6}, CD={r.DragDecomposition.CD:F6}, CM={r.MomentCoefficient:F6}, converged={r.Converged}");
    writer.WriteLine($"# Xtr_U={r.UpperTransition.XTransition:F6}, Xtr_L={r.LowerTransition.XTransition:F6}");
    writer.WriteLine("surface,station,theta,DStar,H,Cf,Ctau,Ue,Namp");
    for (int i = 0; i < r.UpperProfiles.Length; i++)
    {
        var p = r.UpperProfiles[i];
        writer.WriteLine($"upper,{i},{p.Theta:F9},{p.DStar:F9},{p.Hk:F6},{p.Cf:F9},{p.Ctau:F9},{p.EdgeVelocity:F6},{p.AmplificationFactor:F4}");
    }
    for (int i = 0; i < r.LowerProfiles.Length; i++)
    {
        var p = r.LowerProfiles[i];
        writer.WriteLine($"lower,{i},{p.Theta:F9},{p.DStar:F9},{p.Hk:F6},{p.Cf:F9},{p.Ctau:F9},{p.EdgeVelocity:F6},{p.AmplificationFactor:F4}");
    }
    for (int i = 0; i < r.WakeProfiles.Length; i++)
    {
        var p = r.WakeProfiles[i];
        writer.WriteLine($"wake,{i},{p.Theta:F9},{p.DStar:F9},{p.Hk:F6},{p.Cf:F9},{p.Ctau:F9},{p.EdgeVelocity:F6},{p.AmplificationFactor:F4}");
    }
    Console.WriteLine($"Wrote MSES profile to {csvPath}");
    Console.WriteLine($"Upper stations: {r.UpperProfiles.Length}  Lower stations: {r.LowerProfiles.Length}  Wake stations: {r.WakeProfiles.Length}");
    Console.WriteLine($"CL={r.LiftCoefficient:F4} CD={r.DragDecomposition.CD:F6} Xtr_U={r.UpperTransition.XTransition:F4} Xtr_L={r.LowerTransition.XTransition:F4}");
}

static void WriteViscousPolarMses(
    AirfoilGeometry geometry,
    double alphaStart,
    double alphaEnd,
    double alphaStep,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double criticalAmplificationFactor,
    string? outputCsvPath)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        nCritUpper: criticalAmplificationFactor,
        nCritLower: criticalAmplificationFactor);
    var mses = new XFoil.MsesSolver.Services.MsesAnalysisService(
        useThesisExactTurbulent: UseThesisExactTurbulentFromEnv(),
        useWakeMarcher: UseWakeMarcherFromEnv(),
        useThesisExactLaminar: UseThesisExactLaminarFromEnv());
    using var writer = outputCsvPath is null ? null : new System.IO.StreamWriter(outputCsvPath);

    Action<string> emit = line =>
    {
        if (writer is null) Console.WriteLine(line);
        else writer.WriteLine(line);
    };

    if (writer is null)
    {
        Console.WriteLine($"Name: {geometry.Name} (MSES polar — Phase 5 stub)");
        Console.WriteLine($"Panels: {panelCount}  Mach: {machNumber:F4}  Re: {reynoldsNumber:F0}  nCrit: {criticalAmplificationFactor:F3}");
        Console.WriteLine();
        Console.WriteLine("AlphaDeg\tCL\t\tCD\t\tCM\t\tConverged");
    }
    else
    {
        writer.WriteLine("# MSES polar export");
        writer.WriteLine($"# airfoil={geometry.Name}, panels={panelCount}, mach={machNumber}, re={reynoldsNumber}, nCrit={criticalAmplificationFactor}");
        writer.WriteLine("alpha_deg,CL,CD,CM,converged");
    }

    double eps = 1e-9;
    if (alphaStep <= 0) alphaStep = 0.5;
    for (double a = alphaStart; a <= alphaEnd + eps; a += alphaStep)
    {
        var r = mses.AnalyzeViscous(geometry, a, settings);
        string line = writer is null
            ? $"{a.ToString("F4", CultureInfo.InvariantCulture)}\t"
              + $"{r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t"
              + $"{r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}\t"
              + $"{r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t"
              + (r.Converged ? "Y" : "N")
            : $"{a.ToString("F4", CultureInfo.InvariantCulture)},"
              + $"{r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)},"
              + $"{r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)},"
              + $"{r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)},"
              + (r.Converged ? "True" : "False");
        emit(line);
    }

    if (writer is not null)
    {
        Console.WriteLine($"Wrote MSES polar to {outputCsvPath}");
    }
}

static void WriteViscousSinglePointMses(
    AirfoilGeometry geometry,
    double alphaDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double criticalAmplificationFactor)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        nCritUpper: criticalAmplificationFactor,
        nCritLower: criticalAmplificationFactor);
    var mses = new XFoil.MsesSolver.Services.MsesAnalysisService(
        useThesisExactTurbulent: UseThesisExactTurbulentFromEnv(),
        useWakeMarcher: UseWakeMarcherFromEnv(),
        useThesisExactLaminar: UseThesisExactLaminarFromEnv());
    var r = mses.AnalyzeViscous(geometry, alphaDegrees, settings);
    Console.WriteLine($"Name: {geometry.Name} (MSES single-point — Phase 5 stub)");
    Console.WriteLine($"Panels: {settings.PanelCount}");
    Console.WriteLine($"Mach: {settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Re: {settings.ReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {criticalAmplificationFactor.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Alpha: {alphaDegrees.ToString("F4", CultureInfo.InvariantCulture)}°");
    Console.WriteLine("Pipeline: inviscid (Modern) → composite laminar→transition→turbulent marcher");
    Console.WriteLine("          → Squire-Young far-field CD. No viscous Ue feedback (Phase 5 TODO).");
    Console.WriteLine();
    Console.WriteLine($"CL:        {r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD:        {r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CM:        {r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    string xtrU = r.UpperTransition.StationIndex > 0
        ? r.UpperTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)
        : "laminar-at-TE";
    string xtrL = r.LowerTransition.StationIndex > 0
        ? r.LowerTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)
        : "laminar-at-TE";
    Console.WriteLine($"Xtr_U:     {xtrU}");
    Console.WriteLine($"Xtr_L:     {xtrL}");
    Console.WriteLine($"Converged: {r.Converged}");
}

static void WriteViscousSinglePointModern(
    AirfoilGeometry geometry,
    double alphaDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        transitionReynoldsTheta: transitionReynoldsTheta,
        criticalAmplificationFactor: criticalAmplificationFactor,
        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: 1e-5);

    var modernService = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var r = modernService.AnalyzeViscous(geometry, alphaDegrees, settings);

    Console.WriteLine($"Name: {geometry.Name} (modern single-point)");
    Console.WriteLine($"Panels: {settings.PanelCount}");
    Console.WriteLine($"Mach: {settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Re: {settings.ReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TransitionReTheta: {transitionReynoldsTheta.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {criticalAmplificationFactor.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Alpha: {alphaDegrees.ToString("F4", CultureInfo.InvariantCulture)}°");
    Console.WriteLine($"B1 (inviscid bias): applied per Cp-gradient sensor via base.AnalyzeInviscid. Viscous B1 is disabled as of v11 — see Phase3TierBMetrics.md for Re-sensitivity findings.");
    Console.WriteLine($"A1 multi-start: active (fires only on non-physical primary results).");
    // Quality tag now distinguishes POST-STALL (v7 auto-ramp or Viterna
    // result — Converged=False but CL/CD pass the relaxed post-stall
    // envelope |CL|≤2.2). The old "DIVERGED" label was misleading for
    // cases where the Modern facade produced a physical result via
    // fallbacks even though Newton didn't formally converge.
    string plausibleTag;
    if (r.Converged && PhysicalEnvelope.IsAirfoilResultPhysical(r))
        plausibleTag = "PHYSICAL";
    else if (!r.Converged && PhysicalEnvelope.IsAirfoilResultPhysicalPostStall(r))
        plausibleTag = "POST-STALL";
    else if (r.Converged)
        plausibleTag = "NON-PHYSICAL";
    else
        plausibleTag = "DIVERGED";

    Console.WriteLine();
    Console.WriteLine($"CL:        {r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD:        {r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CDf:       {r.DragDecomposition.CDF.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CDp:       {r.DragDecomposition.CDP.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CM:        {r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Xtr_U:     {r.UpperTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Xtr_L:     {r.LowerTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Converged: {r.Converged}");
    Console.WriteLine($"Iters:     {r.Iterations}");
    Console.WriteLine($"Quality:   {plausibleTag}");
}

static void WriteViscousPolarSummaryModern(
    AirfoilGeometry geometry,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor)
{
    var settings = new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        transitionReynoldsTheta: transitionReynoldsTheta,
        criticalAmplificationFactor: criticalAmplificationFactor,
        useExtendedWake: true,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyPanelingPrecision: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        useModernTransitionCorrections: false,
        maxViscousIterations: 200,
        viscousConvergenceTolerance: 1e-5);

    var modernService = new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    var results = modernService.SweepViscousAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        settings);

    Console.WriteLine($"Name: {geometry.Name} (modern tree)");
    Console.WriteLine($"Panels: {settings.PanelCount}");
    Console.WriteLine($"Mach: {settings.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Re: {settings.ReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TransitionReTheta: {transitionReynoldsTheta.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {criticalAmplificationFactor.ToString("F3", CultureInfo.InvariantCulture)}");
    Console.WriteLine("AlphaDeg\tCL\tCD\tCM\tConverged\tIterations");

    foreach (var r in results)
    {
        string plausibleTag;
        if (r.Converged && PhysicalEnvelope.IsAirfoilResultPhysical(r))
            plausibleTag = "PHYSICAL";
        else if (!r.Converged && PhysicalEnvelope.IsAirfoilResultPhysicalPostStall(r))
            plausibleTag = "POST-STALL";
        else if (r.Converged)
            plausibleTag = "NON-PHYSICAL";
        else
            plausibleTag = "DIVERGED";
        Console.WriteLine(
            $"{r.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"{r.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.DragDecomposition.CD.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.MomentCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{r.Converged}\t" +
            $"{r.Iterations}\t" +
            $"CDf={r.DragDecomposition.CDF.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"CDp={r.DragDecomposition.CDP.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_U={r.UpperTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Xtr_L={r.LowerTransition.XTransition.ToString("F4", CultureInfo.InvariantCulture)}\t" +
            $"Quality={plausibleTag}");
    }
}

// Legacy mapping: f_xfoil/src/xoper.f :: PACC/PWRT.
// Difference from legacy: Classic XFoil writes saved polars in its own text format, while this helper exports the managed sweep through a dedicated CSV exporter.
// Decision: Keep the managed CSV path because stable machine-readable export is an intentional CLI improvement.
static void ExportPolarCsv(
    AirfoilGeometry geometry,
    string outputPath,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    PolarCsvExporter polarExporter)
{
    var sweep = analysisService.SweepInviscidAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        new AnalysisSettings(panelCount, machNumber: machNumber));

    polarExporter.Export(outputPath, sweep);
    WriteExportSummary("InviscidAlphaSweep", outputPath, sweep.Points.Count);
}

// Legacy mapping: f_xfoil/src/xoper.f :: PACC/PWRT for viscous polar persistence.
// Difference from legacy: The managed CLI emits plain CSV lines from the viscous sweep instead of the legacy saved-polar file format.
// Decision: Keep the managed CSV export because it is easier to automate while preserving the same operating-point lineage.
static void ExportViscousPolarCsv(
    AirfoilGeometry geometry,
    string outputPath,
    double alphaStartDegrees,
    double alphaEndDegrees,
    double alphaStepDegrees,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    int couplingIterations,
    int viscousIterations,
    double residualTolerance,
    double displacementRelaxation,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor,
    AirfoilAnalysisService analysisService,
    PolarCsvExporter polarExporter)
{
    var settings = CreateViscousSettings(panelCount, machNumber, reynoldsNumber, transitionReynoldsTheta, criticalAmplificationFactor);
    var results = analysisService.SweepViscousAlpha(
        geometry,
        alphaStartDegrees,
        alphaEndDegrees,
        alphaStepDegrees,
        settings);

    var lines = new List<string> { "alpha,CL,CD,CM,converged" };
    foreach (var r in results)
    {
        lines.Add($"{r.AngleOfAttackDegrees:F4},{r.LiftCoefficient:F6},{r.DragDecomposition.CD:F6},{r.MomentCoefficient:F6},{r.Converged}");
    }
    System.IO.File.WriteAllLines(outputPath, lines);
    WriteExportSummary("ViscousAlphaSweep", outputPath, results.Count);
}

// Legacy mapping: f_xfoil/src/xfoil.f :: CLI/CSEQ and f_xfoil/src/xoper.f :: PACC/PWRT.
// Difference from legacy: The lift sweep is delegated to managed services and exported as CSV instead of entering the interactive polar buffer.
// Decision: Keep the managed CSV export because it is a batch-friendly wrapper over the legacy operating-point lineage.
static void ExportLiftSweepCsv(
    AirfoilGeometry geometry,
    string outputPath,
    double liftStart,
    double liftEnd,
    double liftStep,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    PolarCsvExporter polarExporter)
{
    var sweep = analysisService.SweepInviscidLiftCoefficient(
        geometry,
        liftStart,
        liftEnd,
        liftStep,
        new AnalysisSettings(panelCount, machNumber: machNumber));

    polarExporter.Export(outputPath, sweep);
    WriteExportSummary("InviscidLiftSweep", outputPath, sweep.Points.Count);
}

// Legacy mapping: f_xfoil/src/xoper.f :: PWRT saved-polar format lineage.
// Difference from legacy: The managed CLI imports an existing legacy polar and immediately re-emits it through the managed CSV exporter.
// Decision: Keep the managed import/export bridge because it is an interoperability tool, not a direct runtime parity path.
static void ImportLegacyPolar(
    string inputPath,
    string outputPath,
    LegacyPolarImporter legacyPolarImporter,
    PolarCsvExporter polarExporter)
{
    var polar = legacyPolarImporter.Import(inputPath);
    polarExporter.Export(outputPath, polar);
    WriteExportSummary("LegacySavedPolarImport", outputPath, polar.Records.Count);
}

// Legacy mapping: f_xfoil/src/xoper.f :: PWRT saved-polar format lineage.
// Difference from legacy: This helper parses the saved-polar file into managed records and prints metadata rather than reloading it into interactive XFoil session state.
// Decision: Keep the managed summary view because it is a diagnostic wrapper around the importer.
static void WriteLegacyPolarSummary(string inputPath, LegacyPolarImporter legacyPolarImporter)
{
    var polar = legacyPolarImporter.Import(inputPath);
    Console.WriteLine($"SourcePath: {Path.GetFullPath(inputPath)}");
    Console.WriteLine($"SourceCode: {polar.SourceCode}");
    Console.WriteLine($"Version: {polar.Version?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a"}");
    Console.WriteLine($"Name: {polar.AirfoilName}");
    Console.WriteLine($"Elements: {polar.ElementCount}");
    Console.WriteLine($"ReynoldsVariation: {polar.ReynoldsVariationType}");
    Console.WriteLine($"MachVariation: {polar.MachVariationType}");
    Console.WriteLine($"ReferenceMach: {polar.ReferenceMachNumber?.ToString("F6", CultureInfo.InvariantCulture) ?? "n/a"}");
    Console.WriteLine($"ReferenceRe: {polar.ReferenceReynoldsNumber?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a"}");
    Console.WriteLine($"CriticalN: {polar.CriticalAmplificationFactor?.ToString("F6", CultureInfo.InvariantCulture) ?? "n/a"}");
    Console.WriteLine($"Columns: {string.Join(", ", polar.Columns.Select(column => column.Key))}");
    Console.WriteLine($"PointCount: {polar.Records.Count}");
    if (polar.Records.Count > 0)
    {
        var firstRecord = polar.Records[0];
        var preview = string.Join(
            ", ",
            polar.Columns.Take(Math.Min(4, polar.Columns.Count))
                .Select(column => $"{column.Key}={firstRecord.Values[column.Key].ToString("F6", CultureInfo.InvariantCulture)}"));
        Console.WriteLine($"FirstPoint: {preview}");
    }
}

// Legacy mapping: none; reference polar bundles are managed regression artifacts rather than an interactive XFoil command product.
// Difference from legacy: The CLI bridges a managed reference-polar importer directly into CSV export.
// Decision: Keep the managed import/export wrapper because there is no direct Fortran analogue to preserve.
static void ImportLegacyReferencePolar(
    string inputPath,
    string outputPath,
    LegacyReferencePolarImporter legacyReferencePolarImporter,
    PolarCsvExporter polarExporter)
{
    var polar = legacyReferencePolarImporter.Import(inputPath);
    polarExporter.Export(outputPath, polar);
    WriteExportSummary("LegacyReferencePolarImport", outputPath, polar.Blocks.Sum(block => block.Points.Count));
}

// Legacy mapping: none; reference polar block summaries are managed-only regression tooling.
// Difference from legacy: The helper reports imported block sizes and labels instead of interacting with legacy runtime state.
// Decision: Keep the managed summary helper because it serves repository diagnostics only.
static void WriteLegacyReferencePolarSummary(string inputPath, LegacyReferencePolarImporter legacyReferencePolarImporter)
{
    var polar = legacyReferencePolarImporter.Import(inputPath);
    Console.WriteLine($"SourcePath: {Path.GetFullPath(inputPath)}");
    Console.WriteLine($"Label: {polar.Label}");
    foreach (var block in polar.Blocks)
    {
        Console.WriteLine($"{block.Kind}Points: {block.Points.Count}");
    }
}

// Legacy mapping: f_xfoil/src/xoper.f :: DUMP/CPDUMP/BLDUMP file lineage.
// Difference from legacy: The managed CLI parses a legacy dump and emits a normalized archive layout instead of reusing the original monolithic text file.
// Decision: Keep the managed archive export because it improves tooling without changing the imported data content.
static void ImportLegacyPolarDump(
    string inputPath,
    string outputPath,
    LegacyPolarDumpImporter legacyPolarDumpImporter,
    LegacyPolarDumpArchiveWriter legacyPolarDumpArchiveWriter)
{
    var dump = legacyPolarDumpImporter.Import(inputPath);
    var export = legacyPolarDumpArchiveWriter.Export(outputPath, dump);
    Console.WriteLine("ExportKind: LegacyPolarDumpImport");
    Console.WriteLine($"SummaryPath: {export.SummaryPath}");
    Console.WriteLine($"GeometryPath: {export.GeometryPath}");
    Console.WriteLine($"SideFileCount: {export.SidePaths.Count}");
    Console.WriteLine($"PointCount: {dump.OperatingPoints.Count}");
}

// Legacy mapping: f_xfoil/src/xoper.f :: DUMP/CPDUMP/BLDUMP file lineage.
// Difference from legacy: The helper renders the imported dump as concise metadata instead of replaying XFoil's interactive dump inspection workflow.
// Decision: Keep the managed summary wrapper because it is repository tooling around imported legacy artifacts.
static void WriteLegacyPolarDumpSummary(string inputPath, LegacyPolarDumpImporter legacyPolarDumpImporter)
{
    var dump = legacyPolarDumpImporter.Import(inputPath);
    Console.WriteLine($"SourcePath: {Path.GetFullPath(inputPath)}");
    Console.WriteLine($"SourceCode: {dump.SourceCode}");
    Console.WriteLine($"Version: {dump.Version.ToString("F2", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Name: {dump.AirfoilName}");
    Console.WriteLine($"IsIsesPolar: {dump.IsIsesPolar}");
    Console.WriteLine($"IsMachSweep: {dump.IsMachSweep}");
    Console.WriteLine($"ReferenceMach: {dump.ReferenceMachNumber.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"ReferenceRe: {dump.ReferenceReynoldsNumber.ToString("F0", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CriticalN: {dump.CriticalAmplificationFactor.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"GeometryPoints: {dump.Geometry.Count}");
    Console.WriteLine($"PointCount: {dump.OperatingPoints.Count}");
    if (dump.OperatingPoints.Count > 0)
    {
        var first = dump.OperatingPoints[0];
        Console.WriteLine(
            $"FirstPoint: Alpha={first.AngleOfAttackDegrees.ToString("F6", CultureInfo.InvariantCulture)}, " +
            $"CL={first.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}, " +
            $"Mach={first.MachNumber.ToString("F6", CultureInfo.InvariantCulture)}, " +
            $"UpperSamples={first.Sides[0].Samples.Count}, LowerSamples={first.Sides[1].Samples.Count}");
    }
}

// Legacy mapping: f_xfoil/src/xfoil.f :: CSEQ/VISC and f_xfoil/src/xoper.f :: PACC/PWRT.
// Difference from legacy: The managed CLI exports viscous lift-target sweeps as CSV rather than the legacy saved-polar text layout.
// Decision: Keep the managed CSV export because it is a deliberate automation-oriented wrapper around the viscous sweep lineage.
static void ExportViscousLiftSweepCsv(
    AirfoilGeometry geometry,
    string outputPath,
    double liftStart,
    double liftEnd,
    double liftStep,
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    int couplingIterations,
    int viscousIterations,
    double residualTolerance,
    double displacementRelaxation,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor,
    AirfoilAnalysisService analysisService,
    PolarCsvExporter polarExporter)
{
    var settings = CreateViscousSettings(panelCount, machNumber, reynoldsNumber, transitionReynoldsTheta, criticalAmplificationFactor);
    var results = analysisService.SweepViscousCL(
        geometry,
        liftStart,
        liftEnd,
        liftStep,
        settings);

    var lines = new List<string> { "alpha,CL,CD,CM,converged" };
    foreach (var r in results)
    {
        lines.Add($"{r.AngleOfAttackDegrees:F4},{r.LiftCoefficient:F6},{r.DragDecomposition.CD:F6},{r.MomentCoefficient:F6},{r.Converged}");
    }
    System.IO.File.WriteAllLines(outputPath, lines);
    WriteExportSummary("ViscousLiftSweep", outputPath, results.Count);
}

// Legacy mapping: f_xfoil/src/xfoil.f :: CLI.
// Difference from legacy: The helper prints the managed solution for a target lift coefficient directly instead of updating the interactive session display and stored operating-point buffers.
// Decision: Keep the managed console projection because it is presentation logic over the inviscid target-lift solve.
static void WriteTargetLiftSummary(
    AirfoilGeometry geometry,
    double targetLiftCoefficient,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService)
{
    var analysis = analysisService.AnalyzeInviscidForLiftCoefficient(
        geometry,
        targetLiftCoefficient,
        new AnalysisSettings(panelCount, machNumber: machNumber));

    Console.WriteLine($"Name: {geometry.Name}");
    Console.WriteLine($"TargetCL: {targetLiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"SolvedAlphaDeg: {analysis.AngleOfAttackDegrees.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Mach: {analysis.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CL: {analysis.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CD: {analysis.DragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CMc/4: {analysis.MomentCoefficientQuarterChord.ToString("F6", CultureInfo.InvariantCulture)}");
}

static void WriteLiftSweepSummary(
    AirfoilGeometry geometry,
    double clStart,
    double clEnd,
    double clStep,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService)
{
    var sweep = analysisService.SweepInviscidLiftCoefficient(
        geometry,
        clStart,
        clEnd,
        clStep,
        new AnalysisSettings(panelCount, machNumber: machNumber));

    Console.WriteLine($"Name: {geometry.Name}");
    Console.WriteLine($"Mach: {machNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine("TargetCL\tSolvedAlphaDeg\tCL\tCD\tCMc/4");
    foreach (var point in sweep.Points)
    {
        var op = point.OperatingPoint;
        Console.WriteLine(
            $"{point.TargetLiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{op.AngleOfAttackDegrees.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{op.LiftCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{op.DragCoefficient.ToString("F6", CultureInfo.InvariantCulture)}\t" +
            $"{op.MomentCoefficientQuarterChord.ToString("F6", CultureInfo.InvariantCulture)}");
    }
}

static AnalysisSettings CreateViscousSettings(
    int panelCount,
    double machNumber,
    double reynoldsNumber,
    double transitionReynoldsTheta,
    double criticalAmplificationFactor)
{
    return new AnalysisSettings(
        panelCount,
        machNumber: machNumber,
        reynoldsNumber: reynoldsNumber,
        transitionReynoldsTheta: transitionReynoldsTheta,
        criticalAmplificationFactor: criticalAmplificationFactor,
        useExtendedWake: true,
        maxViscousIterations: 200,
        viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
        useModernTransitionCorrections: false,
        useLegacyBoundaryLayerInitialization: true,
        useLegacyStreamfunctionKernelPrecision: true,
        useLegacyPanelingPrecision: true,
        useLegacyWakeSourceKernelPrecision: true,
        viscousConvergenceTolerance: 1e-4); // Fortran EPS1
}

// Legacy mapping: none; export-summary formatting is managed-only CLI behavior.
// Difference from legacy: The helper prints normalized export metadata after service execution rather than relying on implicit terminal feedback.
// Decision: Keep the helper because it standardizes CLI output across commands.
static void WriteExportSummary(string kind, string outputPath, int pointCount)
{
    Console.WriteLine($"ExportKind: {kind}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"PointCount: {pointCount}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: FLAP.
// Difference from legacy: The helper delegates flap geometry changes to a managed service and emits deterministic export metadata instead of driving the interactive GDES session.
// Decision: Keep the managed wrapper because it preserves the FLAP lineage while fitting the headless CLI.
static void ExportFlapGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double hingeX,
    double hingeY,
    double deflectionDegrees,
    FlapDeflectionService flapDeflectionService,
    AirfoilDatExporter airfoilDatExporter)
{
    var result = flapDeflectionService.DeflectTrailingEdge(
        geometry,
        new AirfoilPoint(hingeX, hingeY),
        deflectionDegrees);

    airfoilDatExporter.Export(outputPath, result.Geometry);

    Console.WriteLine("ExportKind: FlapGeometry");
    Console.WriteLine($"InputName: {geometry.Name}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"HingeX: {result.HingePoint.X.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"HingeY: {result.HingePoint.Y.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"DeflectionDeg: {result.DeflectionDegrees.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"AffectedPointCount: {result.AffectedPointCount}");
    Console.WriteLine($"InsertedPointCount: {result.InsertedPointCount}");
    Console.WriteLine($"RemovedPointCount: {result.RemovedPointCount}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: TGAP.
// Difference from legacy: The helper wraps the managed trailing-edge-gap service and prints explicit result metadata instead of mutating geometry in-place inside GDES.
// Decision: Keep the managed wrapper because it is the right headless interface for the TGAP workflow.
static void ExportTrailingEdgeGapGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double targetGap,
    double blendDistanceChordFraction,
    TrailingEdgeGapService trailingEdgeGapService,
    AirfoilDatExporter airfoilDatExporter)
{
    var result = trailingEdgeGapService.SetTrailingEdgeGap(
        geometry,
        targetGap,
        blendDistanceChordFraction);

    airfoilDatExporter.Export(outputPath, result.Geometry);

    Console.WriteLine("ExportKind: TrailingEdgeGapGeometry");
    Console.WriteLine($"InputName: {geometry.Name}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OriginalGap: {result.OriginalGap.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetGap: {result.TargetGap.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"FinalGap: {result.FinalGap.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"BlendDistanceChordFraction: {result.BlendDistanceChordFraction.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: LERAD.
// Difference from legacy: The helper delegates the leading-edge-radius edit to a managed service and reports explicit before/after values rather than using the interactive design buffer.
// Decision: Keep the managed wrapper because it makes the LERAD workflow scriptable.
static void ExportLeadingEdgeRadiusGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double radiusScaleFactor,
    double blendDistanceChordFraction,
    LeadingEdgeRadiusService leadingEdgeRadiusService,
    AirfoilDatExporter airfoilDatExporter)
{
    var result = leadingEdgeRadiusService.ScaleLeadingEdgeRadius(
        geometry,
        radiusScaleFactor,
        blendDistanceChordFraction);

    airfoilDatExporter.Export(outputPath, result.Geometry);

    Console.WriteLine("ExportKind: LeadingEdgeRadiusGeometry");
    Console.WriteLine($"InputName: {geometry.Name}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OriginalLeadingEdgeRadius: {result.OriginalRadius.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"RadiusScaleFactor: {result.RadiusScaleFactor.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"FinalLeadingEdgeRadius: {result.FinalRadius.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"BlendDistanceChordFraction: {result.BlendDistanceChordFraction.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: SCAL and related geometry-transform commands.
// Difference from legacy: Scaling is represented as an explicit managed result object with origin metadata instead of an in-place edit on global geometry arrays.
// Decision: Keep the managed wrapper because it is clearer and better suited to scripted use.
static void ExportScaledGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double scaleFactor,
    GeometryScaleOrigin originKind,
    AirfoilPoint? originPoint,
    GeometryScalingService geometryScalingService,
    AirfoilDatExporter airfoilDatExporter)
{
    var result = geometryScalingService.Scale(
        geometry,
        scaleFactor,
        originKind,
        originPoint);

    airfoilDatExporter.Export(outputPath, result.Geometry);

    Console.WriteLine("ExportKind: GeometryScale");
    Console.WriteLine($"InputName: {geometry.Name}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OriginKind: {result.OriginKind}");
    Console.WriteLine($"OriginX: {result.OriginPoint.X.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"OriginY: {result.OriginPoint.Y.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"ScaleFactor: {result.ScaleFactor.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: ADEG/ARAD/TRAN/LINS/DERO/UNIT command-family output lineage.
// Difference from legacy: This helper only handles deterministic DAT export and summary printing after the actual transform has already been performed by managed services.
// Decision: Keep the managed export wrapper because it decouples file writing from the transform implementations.
static void ExportGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    string kind,
    AirfoilDatExporter airfoilDatExporter)
{
    airfoilDatExporter.Export(outputPath, geometry);
    Console.WriteLine($"ExportKind: {kind}");
    Console.WriteLine($"OutputName: {geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"PointCount: {geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xgdes.f :: ADDP/MOVP/DELP/CORN/CADD.
// Difference from legacy: The contour edit result is surfaced as an explicit managed object and then exported, rather than being left in the mutable GDES buffer.
// Decision: Keep the managed wrapper because it makes contour edits auditable and scriptable.
static void ExportContourEditGeometry(
    ContourEditResult result,
    string outputPath,
    AirfoilDatExporter airfoilDatExporter)
{
    airfoilDatExporter.Export(outputPath, result.Geometry);
    Console.WriteLine("ExportKind: ContourEdit");
    Console.WriteLine($"Operation: {result.Operation}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"PrimaryIndex: {result.PrimaryIndex}");
    Console.WriteLine($"InsertedPointCount: {result.InsertedPointCount}");
    Console.WriteLine($"RemovedPointCount: {result.RemovedPointCount}");
    Console.WriteLine($"RefinedCornerCount: {result.RefinedCornerCount}");
    Console.WriteLine($"MaxCornerAngleDeg: {result.MaxCornerAngleDegrees.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"MaxCornerAngleIndex: {result.MaxCornerAngleIndex}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: MODI.
// Difference from legacy: The modified contour arrives as a managed result object with explicit metadata instead of an implicit in-memory QDES state update.
// Decision: Keep the managed wrapper because it is a cleaner headless interface for the MODI lineage.
static void ExportContourModificationGeometry(
    ContourModificationResult result,
    string outputPath,
    AirfoilDatExporter airfoilDatExporter)
{
    airfoilDatExporter.Export(outputPath, result.Geometry);
    Console.WriteLine("ExportKind: ContourModification");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"ModifiedStartIndex: {result.ModifiedStartIndex}");
    Console.WriteLine($"ModifiedEndIndex: {result.ModifiedEndIndex}");
    Console.WriteLine($"ControlPointCount: {result.ControlPointCount}");
    Console.WriteLine($"MatchedEndpointSlope: {result.MatchedEndpointSlope}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: QDES profile generation.
// Difference from legacy: The helper uses managed analysis and QSpec services, then exports a stable CSV rather than entering the interactive QDES buffer/plot flow.
// Decision: Keep the managed export wrapper because it is better suited to automation while preserving the same design lineage.
static void ExportQSpecProfile(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var profile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    WriteQSpecCsv(outputPath, profile);

    Console.WriteLine("ExportKind: QSpecProfile");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Name: {profile.Name}");
    Console.WriteLine($"AlphaDeg: {profile.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Mach: {profile.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {profile.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: QDES symmetry-style editing lineage.
// Difference from legacy: Symmetry is enforced through a managed profile transformation and CSV export instead of interactive buffer edits.
// Decision: Keep the managed wrapper because it makes a common QDES workflow reproducible in batch mode.
static void ExportSymmetricQSpecProfile(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var profile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var symmetricProfile = qSpecDesignService.ForceSymmetry(profile);
    WriteQSpecCsv(outputPath, symmetricProfile);

    Console.WriteLine("ExportKind: QSpecSymmetry");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Name: {symmetricProfile.Name}");
    Console.WriteLine($"AlphaDeg: {symmetricProfile.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Mach: {symmetricProfile.MachNumber.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {symmetricProfile.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: AQ.
// Difference from legacy: The helper batches multiple managed QSpec profiles into one CSV set instead of stepping through an interactive alpha-driven design loop.
// Decision: Keep the managed set export because it is a deliberate batch-oriented improvement over the legacy workflow.
static void ExportQSpecProfileSetForAngles(
    AirfoilGeometry geometry,
    string outputPath,
    int panelCount,
    double machNumber,
    IReadOnlyList<double> anglesOfAttackDegrees,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var profiles = anglesOfAttackDegrees
        .Select(angle =>
        {
            var analysis = analysisService.AnalyzeInviscid(geometry, angle, new AnalysisSettings(panelCount, machNumber: machNumber));
            return qSpecDesignService.CreateFromInviscidAnalysis(
                $"{geometry.Name} aq {analysis.AngleOfAttackDegrees.ToString("0.###", CultureInfo.InvariantCulture)}",
                analysis);
        })
        .ToArray();

    WriteQSpecSetCsv(outputPath, profiles);

    Console.WriteLine("ExportKind: QSpecAlphaSet");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"ProfileCount: {profiles.Length}");
    Console.WriteLine($"AnglesDeg: {string.Join(", ", profiles.Select(profile => profile.AngleOfAttackDegrees.ToString("F4", CultureInfo.InvariantCulture)))}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: CQ.
// Difference from legacy: The helper walks target lift coefficients through managed target-lift solves and exports the resulting QSpec profiles as one CSV set.
// Decision: Keep the managed batching because it is clearer and more automatable than the interactive CQ loop.
static void ExportQSpecProfileSetForLiftCoefficients(
    AirfoilGeometry geometry,
    string outputPath,
    int panelCount,
    double machNumber,
    IReadOnlyList<double> targetLiftCoefficients,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var settings = new AnalysisSettings(panelCount, machNumber: machNumber);
    var profiles = new List<QSpecProfile>(targetLiftCoefficients.Count);
    var alphaGuess = 0d;
    foreach (var targetLiftCoefficient in targetLiftCoefficients)
    {
        var analysis = analysisService.AnalyzeInviscidForLiftCoefficient(geometry, targetLiftCoefficient, settings, alphaGuess);
        alphaGuess = analysis.AngleOfAttackDegrees;
        profiles.Add(qSpecDesignService.CreateFromInviscidAnalysis(
            $"{geometry.Name} cq {targetLiftCoefficient.ToString("0.###", CultureInfo.InvariantCulture)}",
            analysis));
    }

    WriteQSpecSetCsv(outputPath, profiles);

    Console.WriteLine("ExportKind: QSpecLiftSet");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"ProfileCount: {profiles.Count}");
    Console.WriteLine($"TargetCL: {string.Join(", ", targetLiftCoefficients.Select(target => target.ToString("F4", CultureInfo.InvariantCulture)))}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: MODI.
// Difference from legacy: The helper composes managed inviscid analysis, profile construction, and QSpec modification before emitting a CSV snapshot.
// Decision: Keep the managed wrapper because it makes the MODI lineage explicit and scriptable.
static void ExportModifiedQSpecProfile(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    IReadOnlyList<AirfoilPoint> controlPoints,
    bool matchEndpointSlope,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var profile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var result = qSpecDesignService.Modify(profile, controlPoints, matchEndpointSlope);
    WriteQSpecCsv(outputPath, result.Profile);

    Console.WriteLine("ExportKind: QSpecModify");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Name: {result.Profile.Name}");
    Console.WriteLine($"ModifiedStartIndex: {result.ModifiedStartIndex}");
    Console.WriteLine($"ModifiedEndIndex: {result.ModifiedEndIndex}");
    Console.WriteLine($"MatchedEndpointSlope: {result.MatchedEndpointSlope}");
    Console.WriteLine($"PointCount: {result.Profile.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: SMOOQ.
// Difference from legacy: The managed helper smooths an explicit QSpec profile object and exports the result directly instead of relying on interactive design buffers and plots.
// Decision: Keep the managed wrapper because it exposes SMOOQ-style editing cleanly in batch mode.
static void ExportSmoothedQSpecProfile(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    double startPlotCoordinate,
    double endPlotCoordinate,
    bool matchEndpointSlope,
    double smoothingLengthFactor,
    int panelCount,
    double machNumber,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var profile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var result = qSpecDesignService.Smooth(profile, startPlotCoordinate, endPlotCoordinate, matchEndpointSlope, smoothingLengthFactor);
    WriteQSpecCsv(outputPath, result.Profile);

    Console.WriteLine("ExportKind: QSpecSmooth");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Name: {result.Profile.Name}");
    Console.WriteLine($"ModifiedStartIndex: {result.ModifiedStartIndex}");
    Console.WriteLine($"ModifiedEndIndex: {result.ModifiedEndIndex}");
    Console.WriteLine($"MatchedEndpointSlope: {result.MatchedEndpointSlope}");
    Console.WriteLine($"SmoothingLength: {result.SmoothingLength.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {result.Profile.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: EXEC.
// Difference from legacy: The helper runs the managed inverse-design execution path and exports a DAT file directly instead of mutating the interactive geometry buffer.
// Decision: Keep the managed wrapper because it provides a deterministic headless interface for the EXEC lineage.
static void ExportExecutedQSpecGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    IReadOnlyList<AirfoilPoint> controlPoints,
    bool matchEndpointSlope,
    int panelCount,
    double machNumber,
    double maxDisplacementFraction,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    AirfoilDatExporter airfoilDatExporter)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var modifiedProfile = qSpecDesignService.Modify(baselineProfile, controlPoints, matchEndpointSlope);
    var execution = qSpecDesignService.ExecuteInverse(geometry, baselineProfile, modifiedProfile.Profile, maxDisplacementFraction);
    airfoilDatExporter.Export(outputPath, execution.Geometry);

    Console.WriteLine("ExportKind: QSpecExecute");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OutputName: {execution.Geometry.Name}");
    Console.WriteLine($"MatchedEndpointSlope: {modifiedProfile.MatchedEndpointSlope}");
    Console.WriteLine($"MaxSpeedRatioDelta: {execution.MaxSpeedRatioDelta.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"MaxNormalDisplacement: {execution.MaxNormalDisplacement.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"RmsNormalDisplacement: {execution.RmsNormalDisplacement.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {execution.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xmdes.f :: MDES.
// Difference from legacy: The helper builds the modal spectrum through managed services and writes a CSV snapshot instead of relying on the interactive MDES spectrum view.
// Decision: Keep the managed export because it makes modal data available to external tooling.
static void ExportModalSpectrum(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    int panelCount,
    double machNumber,
    int modeCount,
    double filterStrength,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    ModalInverseDesignService modalInverseDesignService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var profile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var spectrum = modalInverseDesignService.CreateSpectrum($"{geometry.Name} mdes", profile, modeCount, filterStrength);
    WriteModalSpectrumCsv(outputPath, spectrum);

    Console.WriteLine("ExportKind: MdesSpectrum");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Name: {spectrum.Name}");
    Console.WriteLine($"ModeCount: {spectrum.Coefficients.Count}");
}

// Legacy mapping: f_xfoil/src/xmdes.f :: EXEC.
// Difference from legacy: The helper composes managed baseline/profile modification work with modal inverse execution and DAT export instead of editing the design buffer interactively.
// Decision: Keep the managed wrapper because it is the correct headless surface for the MDES execution path.
static void ExportModalExecutedGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    IReadOnlyList<AirfoilPoint> controlPoints,
    bool matchEndpointSlope,
    int panelCount,
    double machNumber,
    int modeCount,
    double filterStrength,
    double maxDisplacementFraction,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    ModalInverseDesignService modalInverseDesignService,
    AirfoilDatExporter airfoilDatExporter)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var modifiedProfile = qSpecDesignService.Modify(baselineProfile, controlPoints, matchEndpointSlope);
    var execution = modalInverseDesignService.Execute(
        geometry,
        baselineProfile,
        modifiedProfile.Profile,
        modeCount,
        filterStrength,
        maxDisplacementFraction);
    airfoilDatExporter.Export(outputPath, execution.Geometry);

    Console.WriteLine("ExportKind: MdesExecute");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OutputName: {execution.Geometry.Name}");
    Console.WriteLine($"ModeCount: {execution.Spectrum.Coefficients.Count}");
    Console.WriteLine($"MaxNormalDisplacement: {execution.MaxNormalDisplacement.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"RmsNormalDisplacement: {execution.RmsNormalDisplacement.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {execution.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xmdes.f :: PERT.
// Difference from legacy: The helper applies the modal perturbation through explicit managed services and exports the resulting geometry directly.
// Decision: Keep the managed wrapper because it makes the PERT workflow reproducible in scripts and tests.
static void ExportPerturbedModalGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    int modeIndex,
    double coefficientDelta,
    int panelCount,
    double machNumber,
    int modeCount,
    double filterStrength,
    double maxDisplacementFraction,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    ModalInverseDesignService modalInverseDesignService,
    AirfoilDatExporter airfoilDatExporter)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var execution = modalInverseDesignService.PerturbMode(
        geometry,
        baselineProfile,
        modeIndex,
        coefficientDelta,
        modeCount,
        filterStrength,
        maxDisplacementFraction);
    airfoilDatExporter.Export(outputPath, execution.Geometry);

    Console.WriteLine("ExportKind: MdesPerturb");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OutputName: {execution.Geometry.Name}");
    Console.WriteLine($"ModeIndex: {modeIndex}");
    Console.WriteLine($"CoefficientDelta: {coefficientDelta.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"MaxNormalDisplacement: {execution.MaxNormalDisplacement.ToString("F6", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {execution.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN.
// Difference from legacy: The helper drives the managed conformal-map execution service and prints explicit convergence metrics instead of relying on interactive MDES state and plots.
// Decision: Keep the managed wrapper because the headless MAPGEN workflow benefits from explicit result metadata.
static void ExportConformalMapgenGeometry(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    IReadOnlyList<AirfoilPoint> controlPoints,
    bool matchEndpointSlope,
    int panelCount,
    double machNumber,
    int circlePointCount,
    int maxNewtonIterations,
    AirfoilPoint? targetTrailingEdgeGap,
    double? targetTrailingEdgeAngleDegrees,
    double filterExponent,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    ConformalMapgenService conformalMapgenService,
    AirfoilDatExporter airfoilDatExporter)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var modifiedProfile = qSpecDesignService.Modify(baselineProfile, controlPoints, matchEndpointSlope);
    var result = conformalMapgenService.Execute(
        geometry,
        baselineProfile,
        modifiedProfile.Profile,
        circlePointCount,
        maxNewtonIterations,
        5e-5d,
        targetTrailingEdgeGap,
        targetTrailingEdgeAngleDegrees,
        filterExponent);
    airfoilDatExporter.Export(outputPath, result.Geometry);

    Console.WriteLine("ExportKind: MapgenExecute");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"OutputName: {result.Geometry.Name}");
    Console.WriteLine($"CirclePointCount: {result.CirclePointCount}");
    Console.WriteLine($"IterationCount: {result.IterationCount}");
    Console.WriteLine($"Converged: {result.Converged}");
    Console.WriteLine($"MaxCoefficientCorrection: {result.MaxCoefficientCorrection.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"InitialTrailingEdgeResidual: {result.InitialTrailingEdgeResidual.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"FinalTrailingEdgeResidual: {result.FinalTrailingEdgeResidual.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeGapX: {result.TargetTrailingEdgeGap.X.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeGapY: {result.TargetTrailingEdgeGap.Y.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"AchievedTrailingEdgeGapX: {result.AchievedTrailingEdgeGap.X.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"AchievedTrailingEdgeGapY: {result.AchievedTrailingEdgeGap.Y.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeAngleDeg: {result.TargetTrailingEdgeAngleDegrees.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"AchievedTrailingEdgeAngleDeg: {result.AchievedTrailingEdgeAngleDegrees.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"PointCount: {result.Geometry.Points.Count}");
}

// Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN.
// Difference from legacy: The helper exports the solved conformal coefficients as CSV instead of keeping them only in an interactive MDES session.
// Decision: Keep the managed wrapper because batch access to MAPGEN coefficients is an intentional CLI improvement.
static void ExportConformalMapgenSpectrum(
    AirfoilGeometry geometry,
    string outputPath,
    double angleOfAttackDegrees,
    IReadOnlyList<AirfoilPoint> controlPoints,
    bool matchEndpointSlope,
    int panelCount,
    double machNumber,
    int circlePointCount,
    int maxNewtonIterations,
    AirfoilPoint? targetTrailingEdgeGap,
    double? targetTrailingEdgeAngleDegrees,
    double filterExponent,
    AirfoilAnalysisService analysisService,
    QSpecDesignService qSpecDesignService,
    ConformalMapgenService conformalMapgenService)
{
    var analysis = analysisService.AnalyzeInviscid(geometry, angleOfAttackDegrees, new AnalysisSettings(panelCount, machNumber: machNumber));
    var baselineProfile = qSpecDesignService.CreateFromInviscidAnalysis(geometry.Name, analysis);
    var modifiedProfile = qSpecDesignService.Modify(baselineProfile, controlPoints, matchEndpointSlope);
    var result = conformalMapgenService.Execute(
        geometry,
        baselineProfile,
        modifiedProfile.Profile,
        circlePointCount,
        maxNewtonIterations,
        5e-5d,
        targetTrailingEdgeGap,
        targetTrailingEdgeAngleDegrees,
        filterExponent);
    WriteConformalCoefficientCsv(outputPath, result.Coefficients);

    Console.WriteLine("ExportKind: MapgenSpectrum");
    Console.WriteLine($"OutputPath: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"CirclePointCount: {result.CirclePointCount}");
    Console.WriteLine($"CoefficientCount: {result.Coefficients.Count}");
    Console.WriteLine($"Converged: {result.Converged}");
    Console.WriteLine($"InitialTrailingEdgeResidual: {result.InitialTrailingEdgeResidual.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"FinalTrailingEdgeResidual: {result.FinalTrailingEdgeResidual.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeGapX: {result.TargetTrailingEdgeGap.X.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeGapY: {result.TargetTrailingEdgeGap.Y.ToString("F8", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"TargetTrailingEdgeAngleDeg: {result.TargetTrailingEdgeAngleDegrees.ToString("F8", CultureInfo.InvariantCulture)}");
}

// Legacy mapping: f_xfoil/src/xqdes.f :: QDES profile storage/inspection lineage.
// Difference from legacy: The managed helper serializes an explicit QSpec profile into stable CSV rather than writing plot-oriented text from interactive buffers.
// Decision: Keep the managed CSV writer because external tooling depends on a stable tabular format.
static void WriteQSpecCsv(string outputPath, QSpecProfile profile)
{
    var lines = new List<string>(profile.Points.Count + 1)
    {
        "index,s,plot_x,x,y,q_over_qinf,cp,cp_corr",
    };

    foreach (var point in profile.Points)
    {
        lines.Add(string.Join(
            ",",
            point.Index.ToString(CultureInfo.InvariantCulture),
            point.SurfaceCoordinate.ToString("F8", CultureInfo.InvariantCulture),
            point.PlotCoordinate.ToString("F8", CultureInfo.InvariantCulture),
            point.Location.X.ToString("F8", CultureInfo.InvariantCulture),
            point.Location.Y.ToString("F8", CultureInfo.InvariantCulture),
            point.SpeedRatio.ToString("F8", CultureInfo.InvariantCulture),
            point.PressureCoefficient.ToString("F8", CultureInfo.InvariantCulture),
            point.CorrectedPressureCoefficient.ToString("F8", CultureInfo.InvariantCulture)));
    }

    File.WriteAllLines(outputPath, lines);
}

// Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN coefficient lineage.
// Difference from legacy: Conformal coefficients are exported as a managed CSV table instead of being viewed only through interactive MDES output.
// Decision: Keep the managed CSV writer because it exposes the MAPGEN result to automation.
static void WriteConformalCoefficientCsv(string outputPath, IReadOnlyList<ConformalMappingCoefficient> coefficients)
{
    var lines = new List<string>(coefficients.Count + 1)
    {
        "mode,real,imag",
    };

    foreach (var coefficient in coefficients)
    {
        lines.Add(string.Join(
            ",",
            coefficient.ModeIndex.ToString(CultureInfo.InvariantCulture),
            coefficient.RealPart.ToString("F10", CultureInfo.InvariantCulture),
            coefficient.ImaginaryPart.ToString("F10", CultureInfo.InvariantCulture)));
    }

    File.WriteAllLines(outputPath, lines);
}

// Legacy mapping: f_xfoil/src/xmdes.f :: MDES spectrum lineage.
// Difference from legacy: The helper writes the modal coefficients into a stable CSV table instead of relying on interactive spectrum display.
// Decision: Keep the managed CSV writer because it is the cleanest interchange format for modal diagnostics.
static void WriteModalSpectrumCsv(string outputPath, ModalSpectrum spectrum)
{
    var lines = new List<string>(spectrum.Coefficients.Count + 1)
    {
        "mode,coefficient,filtered_coefficient",
    };

    foreach (var coefficient in spectrum.Coefficients)
    {
        lines.Add(string.Join(
            ",",
            coefficient.ModeIndex.ToString(CultureInfo.InvariantCulture),
            coefficient.Coefficient.ToString("F8", CultureInfo.InvariantCulture),
            coefficient.FilteredCoefficient.ToString("F8", CultureInfo.InvariantCulture)));
    }

    File.WriteAllLines(outputPath, lines);
}

// Legacy mapping: f_xfoil/src/xqdes.f :: AQ/CQ profile-set lineage.
// Difference from legacy: Multiple QSpec profiles are flattened into one managed CSV file instead of being iterated one by one through the interactive design session.
// Decision: Keep the managed batch writer because it makes multi-profile analysis easy to automate.
static void WriteQSpecSetCsv(string outputPath, IReadOnlyList<QSpecProfile> profiles)
{
    var estimatedRowCount = profiles.Sum(profile => profile.Points.Count) + 1;
    var lines = new List<string>(estimatedRowCount)
    {
        "profile_name,index,s,plot_x,x,y,q_over_qinf,cp,cp_corr,alpha_deg,mach",
    };

    foreach (var profile in profiles)
    {
        foreach (var point in profile.Points)
        {
            lines.Add(string.Join(
                ",",
                EscapeCsv(profile.Name),
                point.Index.ToString(CultureInfo.InvariantCulture),
                point.SurfaceCoordinate.ToString("F8", CultureInfo.InvariantCulture),
                point.PlotCoordinate.ToString("F8", CultureInfo.InvariantCulture),
                point.Location.X.ToString("F8", CultureInfo.InvariantCulture),
                point.Location.Y.ToString("F8", CultureInfo.InvariantCulture),
                point.SpeedRatio.ToString("F8", CultureInfo.InvariantCulture),
                point.PressureCoefficient.ToString("F8", CultureInfo.InvariantCulture),
                point.CorrectedPressureCoefficient.ToString("F8", CultureInfo.InvariantCulture),
                profile.AngleOfAttackDegrees.ToString("F8", CultureInfo.InvariantCulture),
                profile.MachNumber.ToString("F8", CultureInfo.InvariantCulture)));
        }
    }

    File.WriteAllLines(outputPath, lines);
}

// Legacy mapping: none; session-manifest execution is a managed-only automation layer over ported workflows.
// Difference from legacy: A declarative manifest drives repeated analyses and artifact capture, which classic XFoil did not provide as a first-class workflow.
// Decision: Keep the managed session runner because it is repository infrastructure, not legacy solver behavior.
static void RunSession(string manifestPath, string outputDirectory, AnalysisSessionRunner sessionRunner)
{
    var result = sessionRunner.Run(manifestPath, outputDirectory);
    Console.WriteLine($"SessionName: {result.SessionName}");
    Console.WriteLine($"Geometry: {result.GeometryName}");
    Console.WriteLine($"OutputDirectory: {result.OutputDirectory}");
    Console.WriteLine($"SummaryPath: {result.SummaryPath}");
    Console.WriteLine("Artifacts:");
    foreach (var artifact in result.Artifacts)
    {
        Console.WriteLine($"{artifact.Name}\t{artifact.Kind}\t{artifact.PointCount}\t{artifact.OutputPath}");
    }
}

// Legacy mapping: none; command-line argument parsing is managed-only infrastructure.
// Difference from legacy: Numeric parsing is explicit, culture-invariant, and exception-based instead of being driven by interactive READ statements.
// Decision: Keep the managed parser because deterministic CLI input handling is required for scripting.
static double ParseDouble(string raw, string label)
{
    if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
    {
        throw new ArgumentException($"Could not parse {label} '{raw}'.");
    }

    return value;
}

// Legacy mapping: none; list-style CLI argument parsing has no direct interactive Fortran analogue.
// Difference from legacy: Remaining values are parsed eagerly into a managed list instead of being consumed incrementally from the terminal.
// Decision: Keep the helper because it simplifies batch command handling.
static IReadOnlyList<double> ParseRemainingDoubles(string[] args, int startIndex, string label)
{
    if (startIndex >= args.Length)
    {
        throw new ArgumentException($"At least one {label} value is required.");
    }

    var values = new double[args.Length - startIndex];
    for (var index = startIndex; index < args.Length; index++)
    {
        values[index - startIndex] = ParseDouble(args[index], label);
    }

    return values;
}

// Legacy mapping: none; command-line integer parsing is managed-only infrastructure.
// Difference from legacy: Parsing is culture-invariant and exception-based instead of using interactive READ semantics.
// Decision: Keep the managed parser because it provides deterministic error handling for the CLI.
static int ParseInteger(string raw, string label)
{
    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
    {
        throw new ArgumentException($"Could not parse {label} '{raw}'.");
    }

    return value;
}

// Legacy mapping: none; control-point file parsing is a managed CLI convenience around design workflows.
// Difference from legacy: The helper reads whitespace-delimited files with comment skipping and converts them into explicit point records.
// Decision: Keep the managed parser because external design workflows need a simple batch input format.
static IReadOnlyList<AirfoilPoint> ParseControlPointsFile(string path)
{
    var lines = File.ReadAllLines(path);
    var points = new List<AirfoilPoint>();
    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
        {
            continue;
        }

        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new ArgumentException($"Control-point line '{line}' does not contain an x y pair.");
        }

        points.Add(new AirfoilPoint(
            ParseDouble(parts[0], "control point x"),
            ParseDouble(parts[1], "control point y")));
    }

    return points;
}

// Legacy mapping: none; boolean flag normalization is managed-only CLI behavior.
// Difference from legacy: The helper accepts a wider batch-friendly vocabulary than the interactive prompt ever normalized centrally.
// Decision: Keep the managed parser because it improves command ergonomics without affecting solver behavior.
static bool ParseBooleanFlag(string raw, string label)
{
    return raw.ToUpperInvariant() switch
    {
        "1" or "TRUE" or "T" or "YES" or "Y" or "ON" => true,
        "0" or "FALSE" or "F" or "NO" or "N" or "OFF" => false,
        _ => throw new ArgumentException($"Could not parse {label} '{raw}'. Use true/false."),
    };
}

// Legacy mapping: f_xfoil/src/xgdes.f :: CADD parameter-mode lineage.
// Difference from legacy: The mode is normalized into a managed enum instead of being carried as a character or integer flag in the design session.
// Decision: Keep the managed parser because the enum makes the CLI contract explicit.
static CornerRefinementParameterMode ParseCornerRefinementMode(string raw)
{
    return raw.ToUpperInvariant() switch
    {
        "1" or "U" or "UNIFORM" => CornerRefinementParameterMode.Uniform,
        "2" or "S" or "ARCLENGTH" or "ARC" => CornerRefinementParameterMode.ArcLength,
        _ => throw new ArgumentException($"Could not parse corner refinement mode '{raw}'. Use UNIFORM or ARCLENGTH."),
    };
}

// Legacy mapping: f_xfoil/src/xgdes.f :: CADD parameter-mode lineage.
// Difference from legacy: The helper offers tolerant probe-style parsing so the CLI can disambiguate between optional mode and point-count arguments.
// Decision: Keep the managed parser because this ambiguity resolution is specific to the headless command surface.
static bool TryParseCornerRefinementMode(string raw, out CornerRefinementParameterMode mode)
{
    switch (raw.ToUpperInvariant())
    {
        case "1":
        case "U":
        case "UNIFORM":
            mode = CornerRefinementParameterMode.Uniform;
            return true;
        case "2":
        case "S":
        case "ARCLENGTH":
        case "ARC":
            mode = CornerRefinementParameterMode.ArcLength;
            return true;
        default:
            mode = default;
            return false;
    }
}

// Legacy mapping: f_xfoil/src/xgdes.f :: scale-origin command lineage.
// Difference from legacy: The origin choice is normalized into a managed enum instead of being interpreted from interactive prompt state.
// Decision: Keep the managed parser because it makes scale-command inputs explicit and type-safe.
static GeometryScaleOrigin ParseScaleOrigin(string raw)
{
    return raw.ToUpperInvariant() switch
    {
        "L" or "LE" => GeometryScaleOrigin.LeadingEdge,
        "T" or "TE" => GeometryScaleOrigin.TrailingEdge,
        "P" or "POINT" => GeometryScaleOrigin.Point,
        _ => throw new ArgumentException($"Could not parse scale origin '{raw}'. Use LE, TE, or POINT."),
    };
}

// Legacy mapping: none; CSV escaping is managed-only export infrastructure.
// Difference from legacy: The helper emits RFC-style escaped fields for modern CSV consumers instead of relying on legacy free-form text output.
// Decision: Keep the managed helper because stable CSV export is an intentional repository feature.
static string EscapeCsv(string value)
{
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    return value;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: QDCALC wake-column assembly and BAKSUB replay
// Secondary legacy source: tools/fortran-debug/reference/n0012_re1e6_a10_p80/reference_dump.*.txt authoritative reduced-panel dump
// Role in port: Proves the managed first wake-column RHS/solution against the numbered P80 Fortran dump so wake-coupling bugs can be isolated before they contaminate viscous marching.
// Differences: Classic XFoil had no managed unit-test harness; this test constructs the same analytical wake column directly from the managed solver and compares it to the recorded Fortran column.
// Decision: Keep the focused wake-column oracle because it localizes wake-coupling regressions to a few milliseconds instead of a full viscous run.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class WakeColumnFortranParityTests
{
    private const string CaseId = "n0012_re1e6_a10_p80";
    private const int ClassicXFoilNacaPointCount = 239;
    private const double FreestreamSpeed = 1.0;
    private static readonly string WakePanelReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_wakepanel_row1_ref2");
    private static readonly string WakePanelRow2ReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_wakepanel_row2_ref1");
    private static readonly string BasisGammaReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_basis_gamma_ref");
    private static readonly string PswlinRow43ReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_pswlin_row43_ref");
    private static readonly string PswlinRow55ReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_pswlin_row55_ref");
    private static readonly string PswlinRow55FullReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_pswlin_row55_full_ref");
    private static readonly string PswlinRow55NiReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_pswlin_row55_ni_ref");
    private static readonly string WakeGeometryReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.FindRepositoryRoot(),
            "tools",
            "fortran-debug",
            "reference",
            "alpha10_p80_wake_geometry_ref");
    private static readonly FortranReferenceCase ManagedWake81TraceCase = new(
        "n0012_re1e6_a10_p80_wake81psilin",
        "0012",
        1_000_000.0,
        10.0,
        "NACA 0012 Re=1e6 alpha=10 panel=80 wake81 PSILIN live trace",
        PanelCount: 80,
        MaxViscousIterations: 200,
        TraceKindAllowList: "psilin_field,psilin_vortex_segment,psilin_te_correction,psilin_accum_state,psilin_result_terms,psilin_result");

    private static readonly string[] PsilinVortexFields =
    {
        "x1", "x2", "yy", "rs1", "rs2", "g1", "g2", "t1", "t2", "dxInv",
        "psisTerm1", "psisTerm2", "psisTerm3", "psisTerm4", "psis",
        "psidTerm1", "psidTerm2", "psidTerm3", "psidTerm4", "psidTerm5", "psidHalfTerm", "psid",
        "psx1", "psx2", "psyy",
        "pdxSum", "pdx1Mul", "pdx1PanelTerm", "pdx1Accum1", "pdx1Accum2", "pdx1Numerator", "pdx1",
        "pdx2Mul", "pdx2PanelTerm", "pdx2Accum1", "pdx2Accum2", "pdx2Numerator", "pdx2", "pdyy",
        "gammaJo", "gammaJp", "gsum", "gdif", "psni", "pdni", "psiDelta", "psiNiDelta", "dzJo", "dzJp", "dqJo", "dqJp"
    };

    private static readonly string[] PsilinTeFields =
    {
        "psig", "pgam", "psigni", "pgamni", "sigte", "gamte", "scs", "sds",
        "dzJoTeSig", "dzJpTeSig", "dzJoTeGam", "dzJpTeGam",
        "dqJoTeSigHalf", "dqJoTeSigTerm", "dqJoTeGamHalf", "dqJoTeGamTerm",
        "dqTeInner", "dqJoTe", "dqJpTe"
    };

    private static readonly string[] PsilinAccumFields =
    {
        "psiBefore", "psiNormalBefore", "psi", "psiNormalDerivative"
    };

    private static readonly string[] PsilinResultTermFields =
    {
        "psiBeforeFreestream", "psiNormalBeforeFreestream", "psiFreestreamDelta", "psiNormalFreestreamDelta"
    };

    private static readonly string[] PsilinResultFields =
    {
        "psi", "psiNormalDerivative"
    };

    private static readonly string[] PswlinFieldFields =
    {
        "fieldX", "fieldY", "fieldNormalX", "fieldNormalY"
    };

    private static readonly string[] WakeStepFields =
    {
        "ds", "previousX", "previousY", "normalX", "normalY", "x", "y"
    };

    private static readonly string[] PswlinHalfTermFields =
    {
        "x0", "psumTerm1", "psumTerm2", "psumTerm3", "psumAccum", "psum",
        "pdifTerm1", "pdifTerm2", "pdifTerm3", "pdifTerm4", "pdifBase", "pdifAccum", "pdifNumerator", "pdif"
    };

    private static readonly string[] PswlinGeometryFields =
    {
        "xJo", "yJo", "xJp", "yJp",
        "dxPanel", "dyPanel", "dso", "dsio",
        "sx", "sy", "rx1", "ry1", "rx2", "ry2"
    };

    private static readonly string[] WakeSourceAccumFields =
    {
        "delta", "total"
    };

    private static readonly string[] PswlinRecurrenceFields =
    {
        "dzJoLeft", "dzJoRight", "dzJoInner", "dzJo",
        "dqJoLeft", "dqJoRight", "dqJoInner", "dqJo", "qopi"
    };

    private static readonly string[] PswlinRow43TraceKinds =
    {
        "pswlin_geometry",
        "pswlin_half_terms",
        "pswlin_recurrence",
        "pswlin_segment"
    };

    private static readonly string[] PswlinRow55FullTraceKinds =
    {
        "pswlin_field",
        "pswlin_geometry",
        "pswlin_half_terms",
        "pswlin_recurrence",
        "pswlin_segment",
        "wake_source_accum",
        "wake_source_entry"
    };

    private static readonly string[] PswlinNiTraceKinds =
    {
        "pswlin_ni_terms"
    };

    private static readonly string[] PswlinPdx0TermFields =
    {
        "pdx0Term1", "pdx0Term2", "pdx0Term3",
        "pdx0Accum1", "pdx0Accum2", "pdx0Numerator", "pdx0"
    };

    private static readonly string[] PswlinSegmentFields =
    {
        "jm", "jo", "jp", "jq",
        "x1", "x2", "yy", "sgn", "panelAngle",
        "x1i", "x2i", "yyi",
        "rs0", "rs1", "rs2", "g0", "g1", "g2", "t0", "t1", "t2",
        "dso", "dsio", "dsm", "dsim", "dsp", "dsip", "dxInv",
        "ssum", "sdif", "psum", "pdif",
        "psx0", "psx1", "psx2", "psyy",
        "pdx0", "pdx1", "pdx2", "pdyy",
        "psni", "pdni",
        "dzJm", "dzJo", "dzJp", "dzJq",
        "dqJm", "dqJo", "dqJp", "dqJq"
    };

    private static readonly string[] PswlinSegmentInputFields =
    {
        "jm", "jo", "jp", "jq",
        "x1", "x2", "yy", "sgn", "panelAngle",
        "x1i", "x2i", "yyi",
        "rs0", "rs1", "rs2", "g0", "g1", "g2", "t0", "t1", "t2",
        "dso", "dsio", "dsm", "dsim", "dsp", "dsip", "dxInv"
    };

    private static readonly string[] PswlinHalf1SegmentFields =
    {
        "x1", "x2", "yy", "sgn", "panelAngle",
        "x1i", "x2i", "yyi",
        "rs0", "rs1", "g0", "g1", "t0", "t1",
        "dso", "dsio", "dsm", "dsim", "dxInv",
        "psum", "pdif",
        "psx0", "psx1", "psyy",
        "pdx0", "pdx1", "pdyy",
        "psni", "pdni"
    };

    private static readonly string[] PswlinHalf2SegmentFields =
    {
        "x1", "x2", "yy", "sgn", "panelAngle",
        "x1i", "x2i", "yyi",
        "rs0", "rs2", "g0", "g2", "t0", "t2",
        "dso", "dsio", "dsp", "dsip", "dxInv",
        "psum", "pdif",
        "psx0", "psx2", "psyy",
        "pdx0", "pdx2", "pdyy",
        "psni", "pdni"
    };

    private static readonly string[] PswlinNiTermFields =
    {
        "xSum", "xHalf",
        "psLeadRaw", "psLeadScaled", "psTerm1", "psTerm2", "psTerm3", "psAccum12", "psni",
        "pdLeadRaw", "pdLeadScaled", "pdTerm1", "pdTerm2", "pdTerm3", "pdAccum12", "pdni"
    };

    private static readonly string[] WakeSourceEntryFields =
    {
        "dzdm", "dqdm"
    };

    [Fact]
    public void Alpha10_P80_WakeGeometry_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        double[] managedX = GetWakeArray(context.WakeGeometry, "X");
        double[] managedY = GetWakeArray(context.WakeGeometry, "Y");
        double[] managedNx = GetWakeArray(context.WakeGeometry, "NormalX");
        double[] managedNy = GetWakeArray(context.WakeGeometry, "NormalY");
        double[] managedPanelAngle = GetWakeArray(context.WakeGeometry, "PanelAngle");

        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        Assert.Equal(reference.Count, managedX.Length);

        int worstIndex = -1;
        string worstField = string.Empty;
        double worstDelta = double.NegativeInfinity;
        double worstReference = 0.0;
        double worstManaged = 0.0;

        void Track(int index, string field, double referenceValue, double managedValue)
        {
            double delta = Math.Abs(referenceValue - managedValue);
            if (delta > worstDelta)
            {
                worstDelta = delta;
                worstIndex = index;
                worstField = field;
                worstReference = referenceValue;
                worstManaged = managedValue;
            }
        }

        for (int i = 0; i < reference.Count; i++)
        {
            ReferenceWakeNode node = reference[i];
            Track(i, "x", node.X, managedX[i]);
            Track(i, "y", node.Y, managedY[i]);
            Track(i, "nx", node.NormalX, managedNx[i]);
            Track(i, "ny", node.NormalY, managedNy[i]);
            if (i < managedPanelAngle.Length)
            {
                Track(i, "panelAngle", node.PanelAngle, managedPanelAngle[i]);
            }
        }

        Assert.True(
            worstDelta <= 2.0e-6,
            string.Format(
                CultureInfo.InvariantCulture,
                "wake geometry mismatch at node {0} field {1}: abs={2:E8}",
                worstIndex + 1,
                worstField,
                worstDelta) +
            string.Format(
                CultureInfo.InvariantCulture,
                " Fortran={0:E8} Managed={1:E8}",
                worstReference,
                worstManaged));
    }

    [Fact]
    public void Alpha10_P80_WakeGeometry_Bitwise_WithTraceGammaSeed_MatchesReferenceDump()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        double[] managedX = GetWakeArray(context.WakeGeometry, "X");
        double[] managedY = GetWakeArray(context.WakeGeometry, "Y");
        double[] managedNx = GetWakeArray(context.WakeGeometry, "NormalX");
        double[] managedNy = GetWakeArray(context.WakeGeometry, "NormalY");
        double[] managedPanelAngle = GetWakeArray(context.WakeGeometry, "PanelAngle");

        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        Assert.Equal(reference.Count, managedX.Length);

        for (int i = 0; i < reference.Count; i++)
        {
            ReferenceWakeNode node = reference[i];
            AssertExactSingleMatch($"seeded wake geometry x node {i + 1}", node.X, managedX[i]);
            AssertExactSingleMatch($"seeded wake geometry y node {i + 1}", node.Y, managedY[i]);
            AssertExactSingleMatch($"seeded wake geometry nx node {i + 1}", node.NormalX, managedNx[i]);
            AssertExactSingleMatch($"seeded wake geometry ny node {i + 1}", node.NormalY, managedNy[i]);
            if (i < managedPanelAngle.Length)
            {
                AssertExactSingleMatch($"seeded wake geometry panelAngle node {i + 1}", node.PanelAngle, managedPanelAngle[i]);
            }
        }
    }

    [Fact]
    public void Alpha10_P80_PanelGeometry_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferencePanelNode> reference = ReadReferencePanelNodes();
        Assert.Equal(reference.Count, context.Panel.NodeCount);

        int worstIndex = -1;
        string worstField = string.Empty;
        double worstDelta = double.NegativeInfinity;
        double worstReference = 0.0;
        double worstManaged = 0.0;

        void Track(int index, string field, double referenceValue, double managedValue)
        {
            double delta = Math.Abs(referenceValue - managedValue);
            if (delta > worstDelta)
            {
                worstDelta = delta;
                worstIndex = index;
                worstField = field;
                worstReference = referenceValue;
                worstManaged = managedValue;
            }
        }

        for (int i = 0; i < reference.Count; i++)
        {
            ReferencePanelNode node = reference[i];
            Track(i, "x", node.X, context.Panel.X[i]);
            Track(i, "y", node.Y, context.Panel.Y[i]);
            Track(i, "nx", node.NormalX, context.Panel.NormalX[i]);
            Track(i, "ny", node.NormalY, context.Panel.NormalY[i]);
            Track(i, "panelAngle", node.PanelAngle, context.Panel.PanelAngle[i]);
        }

        Assert.True(
            worstDelta <= 2.0e-6,
            string.Format(
                CultureInfo.InvariantCulture,
                "panel geometry mismatch at node {0} field {1}: abs={2:E8}",
                worstIndex + 1,
                worstField,
                worstDelta) +
            string.Format(
                CultureInfo.InvariantCulture,
                " Fortran={0:E8} Managed={1:E8}",
                worstReference,
                worstManaged));
    }

    [Fact]
    public void Alpha10_P80_FirstWakeSeed_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        ReferenceWakeNode reference = ReadReferenceWakeNodes()[0];

        (double seedNormalX, double seedNormalY) = InvokeComputeTrailingEdgeWakeNormal(context.Panel);
        float teX = (float)context.Panel.TrailingEdgeX;
        float teY = (float)context.Panel.TrailingEdgeY;
        float wakeOffset = 1.0e-4f;
        float seedX = teX - (wakeOffset * (float)seedNormalY);
        float seedY = teY + (wakeOffset * (float)seedNormalX);

        AssertExactSingleMatch("first wake seed x", reference.X, seedX);
        AssertExactSingleMatch("first wake seed y", reference.Y, seedY);
        AssertExactSingleMatch("first wake seed nx", reference.NormalX, seedNormalX);
        AssertExactSingleMatch("first wake seed ny", reference.NormalY, seedNormalY);
    }

    [Fact]
    public void Alpha10_P80_BasisGamma_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferenceBasisGammaRow> reference = ReadReferenceBasisGammaRows();

        Assert.Equal(reference.Count, context.State.NodeCount + 1);

        for (int i = 0; i < reference.Count; i++)
        {
            AssertExactSingleMatch($"basis gamma alpha0 row {i + 1}", reference[i].Alpha0, context.State.BasisVortexStrength[i, 0]);
            AssertExactSingleMatch($"basis gamma alpha90 row {i + 1}", reference[i].Alpha90, context.State.BasisVortexStrength[i, 1]);
        }
    }

    [Fact]
    public void Alpha10_P80_Aij_MatchesReferenceDump()
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(CaseId);
        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(definition.AirfoilCode, ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        var panel = new LinearVortexPanelState(definition.PanelCount + 40);
        CosineClusteringPanelDistributor.Distribute(
            x,
            y,
            x.Length,
            panel,
            desiredNodeCount: definition.PanelCount,
            useLegacyPrecision: true);

        var state = new InviscidSolverState(panel.MaxNodes);
        state.InitializeForNodeCount(panel.NodeCount);
        state.UseLegacyKernelPrecision = true;
        state.UseLegacyPanelingPrecision = true;

        int systemSize = InvokeAssembleSystem(panel, state);
        IReadOnlyDictionary<(int row, int col), double> reference = ReadReferenceAijEntries();

        int worstRow = -1;
        int worstCol = -1;
        double worstDelta = double.NegativeInfinity;
        double worstReference = 0.0;
        double worstManaged = 0.0;

        for (int row = 0; row < systemSize; row++)
        {
            for (int col = 0; col < systemSize; col++)
            {
                double referenceValue = reference[(row + 1, col + 1)];
                double managedValue = state.StreamfunctionInfluence[row, col];
                double delta = Math.Abs(referenceValue - managedValue);
                if (delta > worstDelta)
                {
                    worstDelta = delta;
                    worstRow = row + 1;
                    worstCol = col + 1;
                    worstReference = referenceValue;
                    worstManaged = managedValue;
                }
            }
        }

        Assert.True(
            worstDelta <= 2.0e-6,
            string.Format(
                CultureInfo.InvariantCulture,
                "AIJ mismatch at row {0} col {1}: Fortran={2:E8} Managed={3:E8} abs={4:E8}",
                worstRow,
                worstCol,
                worstReference,
                worstManaged,
                worstDelta));
    }

    [Fact]
    public void Alpha10_P80_VortexStrengthAlphaSuperposition_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferenceBasisGammaRow> reference = ReadReferenceBasisGammaRows();
        float cosa = (float)Math.Cos(context.AlphaRadians);
        float sina = (float)Math.Sin(context.AlphaRadians);

        for (int i = 0; i < context.State.NodeCount; i++)
        {
            float referenceGamma = (float)(((double)cosa * (float)reference[i].Alpha0) + ((double)sina * (float)reference[i].Alpha90));
            AssertExactSingleMatch($"vortex strength row {i + 1}", referenceGamma, context.State.VortexStrength[i]);
        }
    }

    [Fact]
    public void Alpha10_P80_BasisGamma_MatchesReferenceTrace()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ParityTraceRecord> reference = ReadBasisGammaReferenceTrace();

        var alpha0Rows = reference
            .Where(record => record.Kind == "basis_entry" &&
                             string.Equals(record.Name, "basis_gamma_alpha0", StringComparison.Ordinal))
            .OrderBy(record => record.Data.GetProperty("index").GetInt32())
            .ToArray();
        var alpha90Rows = reference
            .Where(record => record.Kind == "basis_entry" &&
                             string.Equals(record.Name, "basis_gamma_alpha90", StringComparison.Ordinal))
            .OrderBy(record => record.Data.GetProperty("index").GetInt32())
            .ToArray();

        Assert.Equal(context.State.NodeCount + 1, alpha0Rows.Length);
        Assert.Equal(context.State.NodeCount + 1, alpha90Rows.Length);

        for (int i = 0; i < alpha0Rows.Length; i++)
        {
            AssertExactSingleMatch($"basis gamma alpha0 trace row {i + 1}", ReadTraceSingle(alpha0Rows[i], "value"), context.State.BasisVortexStrength[i, 0]);
            AssertExactSingleMatch($"basis gamma alpha90 trace row {i + 1}", ReadTraceSingle(alpha90Rows[i], "value"), context.State.BasisVortexStrength[i, 1]);
        }
    }

    [Fact]
    public void Alpha10_P80_VortexStrength_FirstWakeTraceGammaInputs_MatchReferenceTrace()
    {
        ParityTraceRecord[] vortexSegments = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f)
            .Where(record => record.Kind == "psilin_vortex_segment")
            .ToArray();
        ParityTraceRecord[] managedSegments = ExtractPsilinCallByFieldNormal(ReadManagedWake81Trace(), 1.0f, 0.0f)
            .Where(record => record.Kind == "psilin_vortex_segment")
            .ToArray();

        Assert.NotEmpty(vortexSegments);
        Assert.Equal(vortexSegments.Length, managedSegments.Length);

        int firstJo = vortexSegments[0].Data.GetProperty("jo").GetInt32();
        int firstJp = vortexSegments[0].Data.GetProperty("jp").GetInt32();
        Assert.Equal(1, firstJo);
        Assert.Equal(2, firstJp);

        for (int index = 0; index < vortexSegments.Length; index++)
        {
            ParityTraceRecord vortexSegment = vortexSegments[index];
            ParityTraceRecord managedSegment = managedSegments[index];
            int jo = vortexSegment.Data.GetProperty("jo").GetInt32();
            int jp = vortexSegment.Data.GetProperty("jp").GetInt32();
            Assert.Equal(jo, managedSegment.Data.GetProperty("jo").GetInt32());
            Assert.Equal(jp, managedSegment.Data.GetProperty("jp").GetInt32());
            Assert.Equal(
                ReadTraceBits(vortexSegment, "gammaJo"),
                ReadTraceBits(managedSegment, "gammaJo"));
            Assert.Equal(
                ReadTraceBits(vortexSegment, "gammaJp"),
                ReadTraceBits(managedSegment, "gammaJp"));
        }
    }

    [Fact]
    public void Alpha10_P80_FirstWakePsiX_LiveGammaOwner_Segment16To17_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> expected = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        IReadOnlyList<ParityTraceRecord> actual = ExtractPsilinCallByFieldNormal(ReadManagedWake81Trace(), 1.0f, 0.0f);

        ParityTraceRecord expectedSegment = SelectPsilinPairRecord(expected, "psilin_vortex_segment", jo: 16, jp: 17);
        ParityTraceRecord actualSegment = SelectPsilinPairRecord(actual, "psilin_vortex_segment", jo: 16, jp: 17);

        foreach (string field in new[] { "gammaJo", "gammaJp", "gsum", "gdif" })
        {
            Assert.True(
                string.Equals(ReadTraceBits(expectedSegment, field), ReadTraceBits(actualSegment, field), StringComparison.Ordinal),
                $"psilin_vortex_segment owner jo=16 jp=17 field={field} Fortran=0x{ReadTraceBits(expectedSegment, field)} Managed=0x{ReadTraceBits(actualSegment, field)}");
        }
    }

    [Fact]
    public void Alpha10_P80_FirstWakePanelState_WithReferenceSeed_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        ReferenceWakeNode seedNode = reference[0];
        ReferenceWakeNode nextNode = reference[1];

        (double panelAngle, double nextNormalX, double nextNormalY) = InvokeComputeWakePanelState(
            context.Panel,
            context.State,
            wakeNodeIndex: 0,
            x: seedNode.X,
            y: seedNode.Y,
            fallbackNormalX: seedNode.NormalX,
            fallbackNormalY: seedNode.NormalY,
            context.AlphaRadians);

        AssertExactSingleMatch("first wake panel angle", seedNode.PanelAngle, panelAngle);
        AssertExactSingleMatch("first wake next normal x", nextNode.NormalX, nextNormalX);
        AssertExactSingleMatch("first wake next normal y", nextNode.NormalY, nextNormalY);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePanelState_Trace_MatchesReferenceTrace()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        ReferenceWakeNode seedNode = reference[0];

        ParityTraceRecord expected = ReadLatestWakePanelStateReferenceTrace();
        ParityTraceRecord actual = CaptureWakePanelStateTrace(
            context.Panel,
            context.State,
            wakeNodeIndex: 0,
            x: seedNode.X,
            y: seedNode.Y,
            fallbackNormalX: seedNode.NormalX,
            fallbackNormalY: seedNode.NormalY,
            context.AlphaRadians);

        string[] fields =
        {
            "x", "y", "psiX", "psiY", "magnitude", "panelAngle",
            "currentNormalX", "currentNormalY", "nextNormalX", "nextNormalY"
        };

        foreach (string field in fields)
        {
            Assert.True(
                string.Equals(ReadTraceBits(expected, field), ReadTraceBits(actual, field), StringComparison.Ordinal),
                $"wake_panel_state field={field} Fortran=0x{ReadTraceBits(expected, field)} Managed=0x{ReadTraceBits(actual, field)}");
        }
    }

    [Fact]
    public void Alpha10_P80_FirstWakePanelState_Trace_WithTraceGammaSeed_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        ReferenceWakeNode seedNode = reference[0];

        ParityTraceRecord expected = ReadLatestWakePanelStateReferenceTrace();
        ParityTraceRecord actual = CaptureWakePanelStateTrace(
            context.Panel,
            context.State,
            wakeNodeIndex: 0,
            x: seedNode.X,
            y: seedNode.Y,
            fallbackNormalX: seedNode.NormalX,
            fallbackNormalY: seedNode.NormalY,
            context.AlphaRadians);

        string[] fields =
        {
            "x", "y", "psiX", "psiY", "magnitude", "panelAngle",
            "currentNormalX", "currentNormalY", "nextNormalX", "nextNormalY"
        };

        foreach (string field in fields)
        {
            Assert.True(
                string.Equals(ReadTraceBits(expected, field), ReadTraceBits(actual, field), StringComparison.Ordinal),
                $"wake_panel_state seeded field={field} Fortran=0x{ReadTraceBits(expected, field)} Managed=0x{ReadTraceBits(actual, field)}");
        }
    }

    [Fact]
    public void Alpha10_P80_SecondWakePanelState_Trace_WithTraceGammaSeed_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        IReadOnlyList<ParityTraceRecord> trace = CaptureWakeGeometryTraceRecords(context.Panel, context.State, context.AlphaRadians);

        ParityTraceRecord expected = ReadWakePanelStateReferenceTrace(WakePanelRow2ReferenceDirectory, index: 2);
        ParityTraceRecord actual = trace.Single(record => record.Kind == "wake_panel_state" &&
                                                          record.Data.GetProperty("index").GetInt32() == 2);

        string[] fields =
        {
            "x", "y", "psiX", "psiY", "magnitude", "panelAngle",
            "currentNormalX", "currentNormalY", "nextNormalX", "nextNormalY"
        };

        foreach (string field in fields)
        {
            Assert.True(
                string.Equals(ReadTraceBits(expected, field), ReadTraceBits(actual, field), StringComparison.Ordinal),
                $"wake_panel_state index=2 field={field} Fortran=0x{ReadTraceBits(expected, field)} Managed=0x{ReadTraceBits(actual, field)}");
        }
    }

    [Fact]
    public void Alpha10_P80_SecondWakePanelState_Trace_WithManagedState_MatchesReferenceTrace()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ParityTraceRecord> trace = CaptureWakeGeometryTraceRecords(context.Panel, context.State, context.AlphaRadians);

        ParityTraceRecord expected = ReadWakePanelStateReferenceTrace(WakePanelRow2ReferenceDirectory, index: 2);
        ParityTraceRecord actual = trace.Single(record => record.Kind == "wake_panel_state" &&
                                                          record.Data.GetProperty("index").GetInt32() == 2);

        string[] fields =
        {
            "x", "y", "psiX", "psiY", "magnitude", "panelAngle",
            "currentNormalX", "currentNormalY", "nextNormalX", "nextNormalY"
        };

        foreach (string field in fields)
        {
            Assert.True(
                string.Equals(ReadTraceBits(expected, field), ReadTraceBits(actual, field), StringComparison.Ordinal),
                $"wake_panel_state index=2 managed-state field={field} Fortran=0x{ReadTraceBits(expected, field)} Managed=0x{ReadTraceBits(actual, field)}");
        }
    }

    [Fact]
    public void Alpha10_P80_ThirdWakeStep_Trace_WithTraceGammaSeed_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        IReadOnlyList<ParityTraceRecord> trace = CaptureWakeGeometryTraceRecords(context.Panel, context.State, context.AlphaRadians);

        ParityTraceRecord expected = ReadWakeStepReferenceTrace(WakePanelRow2ReferenceDirectory, index: 3);
        ParityTraceRecord actual = trace.Single(record => record.Kind == "wake_step_terms" &&
                                                          record.Data.GetProperty("index").GetInt32() == 3);

        AssertSingleTraceRecordFieldsEqual(expected, actual, WakeStepFields, "wake_step_terms index=3");
    }

    [Fact]
    public void Alpha10_P80_FourthWakeStep_Trace_WithTraceGammaSeed_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        IReadOnlyList<ParityTraceRecord> trace = CaptureWakeGeometryTraceRecords(context.Panel, context.State, context.AlphaRadians);

        ParityTraceRecord expected = ReadWakeStepReferenceTrace(WakePanelRow2ReferenceDirectory, index: 4);
        ParityTraceRecord actual = trace.Single(record => record.Kind == "wake_step_terms" &&
                                                          record.Data.GetProperty("index").GetInt32() == 4);

        AssertSingleTraceRecordFieldsEqual(expected, actual, WakeStepFields, "wake_step_terms index=4");
    }

    [Fact]
    public void Alpha10_P80_FirstWakePsiX_PsilinTrace_MatchesReferenceTrace()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        IReadOnlyList<ReferenceWakeNode> reference = ReadReferenceWakeNodes();
        ReferenceWakeNode seedNode = reference[0];

        IReadOnlyList<ParityTraceRecord> expected = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        SeedVortexStrengthFromPsilinTrace(context.State, expected);
        IReadOnlyList<ParityTraceRecord> actual = ExtractPsilinCallByFieldNormal(
            CaptureWakePanelStateTraceRecords(
                context.Panel,
                context.State,
                wakeNodeIndex: 0,
                x: seedNode.X,
                y: seedNode.Y,
                fallbackNormalX: seedNode.NormalX,
                fallbackNormalY: seedNode.NormalY,
                context.AlphaRadians),
            1.0f,
            0.0f);

        AssertPairTraceRecordsEqual(
            ReadPairTraceRecords(expected, "psilin_vortex_segment", PsilinVortexFields),
            ReadPairTraceRecords(actual, "psilin_vortex_segment", PsilinVortexFields),
            "psilin_vortex_segment",
            PsilinVortexFields);
        AssertPairTraceRecordsEqual(
            ReadPairTraceRecords(expected, "psilin_te_correction", PsilinTeFields),
            ReadPairTraceRecords(actual, "psilin_te_correction", PsilinTeFields),
            "psilin_te_correction",
            PsilinTeFields);
        AssertAccumTraceRecordsEqual(
            ReadAccumTraceRecords(expected),
            ReadAccumTraceRecords(actual),
            PsilinAccumFields);
        AssertResultTraceRecordsEqual(
            ReadResultTraceRecords(expected, "psilin_result_terms", PsilinResultTermFields),
            ReadResultTraceRecords(actual, "psilin_result_terms", PsilinResultTermFields),
            "psilin_result_terms",
            PsilinResultTermFields);
        AssertResultTraceRecordsEqual(
            ReadResultTraceRecords(expected, "psilin_result", PsilinResultFields),
            ReadResultTraceRecords(actual, "psilin_result", PsilinResultFields),
            "psilin_result",
            PsilinResultFields);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePsiX_PsilinTrace_WithManagedState_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> expected = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        IReadOnlyList<ParityTraceRecord> actual = ExtractPsilinCallByFieldNormal(ReadManagedWake81Trace(), 1.0f, 0.0f);

        AssertPairTraceRecordsEqual(
            ReadPairTraceRecords(expected, "psilin_vortex_segment", PsilinVortexFields),
            ReadPairTraceRecords(actual, "psilin_vortex_segment", PsilinVortexFields),
            "psilin_vortex_segment",
            PsilinVortexFields);
        AssertPairTraceRecordsEqual(
            ReadPairTraceRecords(expected, "psilin_te_correction", PsilinTeFields),
            ReadPairTraceRecords(actual, "psilin_te_correction", PsilinTeFields),
            "psilin_te_correction",
            PsilinTeFields);
        AssertAccumTraceRecordsEqual(
            ReadAccumTraceRecords(expected),
            ReadAccumTraceRecords(actual),
            PsilinAccumFields);
        AssertResultTraceRecordsEqual(
            ReadResultTraceRecords(expected, "psilin_result_terms", PsilinResultTermFields),
            ReadResultTraceRecords(actual, "psilin_result_terms", PsilinResultTermFields),
            "psilin_result_terms",
            PsilinResultTermFields);
        AssertResultTraceRecordsEqual(
            ReadResultTraceRecords(expected, "psilin_result", PsilinResultFields),
            ReadResultTraceRecords(actual, "psilin_result", PsilinResultFields),
            "psilin_result",
            PsilinResultFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow43_Trace_WithReferenceInputs_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[42];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow43ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 43,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        AssertTraceRecordSequenceEqual(expected, actual);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow43_SegmentInputs_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[42];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow43ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 43,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        AssertTraceRecordsOfKindEqual(expected, actual, "pswlin_geometry", PswlinGeometryFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow43_LocalKernelInputs_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[42];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow43ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 43,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        AssertTraceRecordsOfKindEqual(expected, actual, "pswlin_segment", PswlinSegmentInputFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow43_Half1SegmentChain_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[42];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow43ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 43,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        ParityTraceRecord expectedRecord = expected.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 1);
        ParityTraceRecord actualRecord = actual.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 1);

        AssertSingleTraceRecordFieldsEqual(expectedRecord, actualRecord, PswlinHalf1SegmentFields, "pswlin_segment seg=1 half=1");
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow43_Half2SegmentChain_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[42];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow43ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 43,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        ParityTraceRecord expectedRecord = expected.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 2);
        ParityTraceRecord actualRecord = actual.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 2);

        AssertSingleTraceRecordFieldsEqual(expectedRecord, actualRecord, PswlinHalf2SegmentFields, "pswlin_segment seg=1 half=2");
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_Trace_WithReferenceInputs_MatchesReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        AssertTraceRecordSequenceEqual(expected, actual);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_LocalKernelInputs_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        AssertTraceRecordsOfKindEqual(expected, actual, "pswlin_segment", PswlinSegmentInputFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_Half2SegmentChain_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55ReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1);

        ParityTraceRecord expectedRecord = expected.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 2);
        ParityTraceRecord actualRecord = actual.Single(
            record => record.Kind == "pswlin_segment" &&
                      record.Data.GetProperty("wakeSegment").GetInt32() == 1 &&
                      record.Data.GetProperty("half").GetInt32() == 2);

        AssertSingleTraceRecordFieldsEqual(expectedRecord, actualRecord, PswlinHalf2SegmentFields, "pswlin_segment seg=1 half=2 row55");
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_FinalEntries_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55FullReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1,
            wakeSegment: null,
            kinds: PswlinRow55FullTraceKinds);

        AssertTraceRecordsOfKindEqual(expected, actual, "wake_source_entry", WakeSourceEntryFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_Accum_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55FullReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1,
            wakeSegment: null,
            kinds: PswlinRow55FullTraceKinds);

        AssertTraceRecordsOfKindEqual(expected, actual, "wake_source_accum", WakeSourceAccumFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_AllSegments_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55FullReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1,
            wakeSegment: null,
            kinds: PswlinRow55FullTraceKinds);

        AssertTraceRecordsOfKindEqual(expected, actual, "pswlin_segment", PswlinSegmentFields);
    }

    [Fact]
    public void Alpha10_P80_WakeSourceRow55_NiTerms_WithReferenceInputs_MatchReferenceTrace()
    {
        IReadOnlyList<ParityTraceRecord> wakeGeometryTrace = ReadWakeGeometryReferenceTrace();
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromTrace(wakeGeometryTrace);
        ReferencePanelNode fieldNode = referencePanelNodes[54];

        IReadOnlyList<ParityTraceRecord> expected = ReadPswlinRow55NiReferenceTrace();
        IReadOnlyList<ParityTraceRecord> actual = CaptureWakeSourceSensitivityTrace(
            referenceWakeGeometry,
            fieldNodeIndex: 55,
            fieldX: fieldNode.X,
            fieldY: fieldNode.Y,
            fieldNormalX: fieldNode.NormalX,
            fieldNormalY: fieldNode.NormalY,
            fieldWakeIndex: -1,
            wakeSegment: null,
            kinds: PswlinNiTraceKinds);

        AssertTraceRecordsOfKindEqual(expected, actual, "pswlin_ni_terms", PswlinNiTermFields);
    }

    [Fact]
    public void Alpha10_P80_FirstWakeRhsColumn_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        double[] managed = BuildFirstWakeColumnRightHandSide(context.Panel, context.WakeGeometry);
        double[] reference = ReadColumnValues("WAKE_RHS_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-6, "first wake RHS column");
    }

    [Fact]
    public void Alpha10_P80_FirstWakeRhsColumn_WithReferenceWakeGeometry_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        object referenceWakeGeometry = BuildWakeGeometryFromReference(ReadReferenceWakeNodes());
        double[] managed = BuildFirstWakeColumnRightHandSide(context.Panel, referenceWakeGeometry);
        double[] reference = ReadColumnValues("WAKE_RHS_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-6, "first wake RHS column with reference wake geometry");
    }

    [Fact]
    public void Alpha10_P80_FirstWakeRhsColumn_WithReferenceFieldAndWakeGeometry_MatchesReferenceDump()
    {
        IReadOnlyList<ReferencePanelNode> referencePanelNodes = ReadReferencePanelNodes();
        object referenceWakeGeometry = BuildWakeGeometryFromReference(ReadReferenceWakeNodes());
        double[] managed = BuildFirstWakeColumnRightHandSide(referencePanelNodes, referenceWakeGeometry);
        double[] reference = ReadColumnValues("WAKE_RHS_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-6, "first wake RHS column with reference field and wake geometry");
    }

    [Fact]
    public void Alpha10_P80_FirstWakeSolvedColumn_MatchesReferenceDump()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        double[] rhs = BuildFirstWakeColumnRightHandSide(context.Panel, context.WakeGeometry);
        double[] managed = SolveWakeColumn(context.State, rhs);
        double[] reference = ReadColumnValues("WAKE_SOL_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-5, "first wake solved column");
    }

    [Fact]
    public void Alpha10_P80_FirstWakeRhsColumn_WithTraceGammaSeed_MatchesReferenceDump()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        double[] managed = BuildFirstWakeColumnRightHandSide(context.Panel, context.WakeGeometry);
        double[] reference = ReadColumnValues("WAKE_RHS_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-6, "first wake RHS column with trace gamma seed");
    }

    [Fact]
    public void Alpha10_P80_FirstWakeSolvedColumn_WithTraceGammaSeed_MatchesReferenceDump()
    {
        IReadOnlyList<ParityTraceRecord> gammaTrace = ExtractPsilinCallByFieldNormal(ReadPsilinWake81ReferenceTrace(), 1.0f, 0.0f);
        ManagedWakeColumnContext context = BuildManagedContext(gammaTrace);
        double[] rhs = BuildFirstWakeColumnRightHandSide(context.Panel, context.WakeGeometry);
        double[] managed = SolveWakeColumn(context.State, rhs);
        double[] reference = ReadColumnValues("WAKE_SOL_COL");

        AssertColumnMatches(reference, managed, tolerance: 2.0e-5, "first wake solved column with trace gamma seed");
    }

    [Fact]
    public void Alpha10_P80_AnalyticalDij_FirstWakeColumn_MatchesDirectReplay()
    {
        ManagedWakeColumnContext context = BuildManagedContext();
        double[] rhs = BuildFirstWakeColumnRightHandSide(context.Panel, context.WakeGeometry);
        double[] replay = SolveWakeColumn(context.State, rhs);
        double[,] analytical = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            context.State,
            context.Panel,
            Math.Max((context.Panel.NodeCount / 8) + 2, 4),
            FreestreamSpeed,
            context.AlphaRadians,
            useLegacyWakeSourceKernelPrecision: true);

        int n = context.Panel.NodeCount;
        var managed = new double[n];
        for (int row = 0; row < n; row++)
        {
            managed[row] = analytical[row, n];
        }

        AssertColumnMatches(replay.Take(n).ToArray(), managed, tolerance: 1.0e-6, "analytical DIJ first wake column");
    }

    private static ManagedWakeColumnContext BuildManagedContext(IReadOnlyList<ParityTraceRecord>? gammaTrace = null)
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(CaseId);
        AnalysisSettings settings = new(
            panelCount: definition.PanelCount,
            reynoldsNumber: definition.ReynoldsNumber,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: definition.MaxViscousIterations,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: definition.CriticalAmplificationFactor,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);
        double alphaRadians = definition.AlphaDegrees * Math.PI / 180.0;

        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(definition.AirfoilCode, ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var state = new InviscidSolverState(maxNodes);

        CosineClusteringPanelDistributor.Distribute(
            x,
            y,
            x.Length,
            panel,
            settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        state.InitializeForNodeCount(panel.NodeCount);
        state.UseLegacyKernelPrecision = settings.UseLegacyStreamfunctionKernelPrecision;
        state.UseLegacyPanelingPrecision = settings.UseLegacyPanelingPrecision;

        _ = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians,
            panel,
            state,
            settings.FreestreamVelocity,
            settings.MachNumber);

        if (gammaTrace is not null)
        {
            SeedVortexStrengthFromPsilinTrace(state, gammaTrace);
        }

        int nWake = Math.Max((panel.NodeCount / 8) + 2, 4);
        object wakeGeometry = InvokeBuildWakeGeometry(panel, state, nWake, FreestreamSpeed, alphaRadians);

        return new ManagedWakeColumnContext(panel, state, wakeGeometry, alphaRadians);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadManagedWake81Trace()
    {
        // This owner rig exists to validate the current live solver state against a
        // focused full-run PSILIN capture. Always regenerate the managed artifact so
        // the comparison cannot be pinned to a stale trace from an earlier build.
        FortranReferenceCases.RefreshManagedArtifacts(ManagedWake81TraceCase);
        string tracePath = FortranReferenceCases.GetManagedTracePath(ManagedWake81TraceCase.CaseId);

        return ParityTraceLoader.ReadAll(tracePath);
    }

    private static object InvokeBuildWakeGeometry(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        int nWake,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        MethodInfo method = typeof(InfluenceMatrixBuilder).GetMethod(
            "BuildWakeGeometry",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildWakeGeometry method not found.");

        return method.Invoke(null, new object[] { panel, state, nWake, freestreamSpeed, angleOfAttackRadians })!;
    }

    private static int InvokeAssembleSystem(
        LinearVortexPanelState panel,
        InviscidSolverState state)
    {
        MethodInfo method = typeof(LinearVortexInviscidSolver).GetMethod(
            "AssembleSystem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("AssembleSystem method not found.");

        return (int)method.Invoke(null, new object[] { panel, state, FreestreamSpeed })!;
    }

    private static (double normalX, double normalY) InvokeComputeTrailingEdgeWakeNormal(LinearVortexPanelState panel)
    {
        MethodInfo method = typeof(InfluenceMatrixBuilder).GetMethod(
            "ComputeTrailingEdgeWakeNormal",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeTrailingEdgeWakeNormal method not found.");

        MethodInfo genericMethod = method.MakeGenericMethod(typeof(float));
        object?[] args = { panel, null, null };
        genericMethod.Invoke(null, args);
        return ((float)args[1]!, (float)args[2]!);
    }

    private static (double panelAngle, double nextNormalX, double nextNormalY) InvokeComputeWakePanelState(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        int wakeNodeIndex,
        double x,
        double y,
        double fallbackNormalX,
        double fallbackNormalY,
        double angleOfAttackRadians)
    {
        MethodInfo method = typeof(InfluenceMatrixBuilder).GetMethod(
            "ComputeWakePanelState",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeWakePanelState method not found.");

        MethodInfo genericMethod = method.MakeGenericMethod(typeof(float));
        object?[] args =
        {
            panel,
            state,
            wakeNodeIndex,
            (float)x,
            (float)y,
            (float)fallbackNormalX,
            (float)fallbackNormalY,
            FreestreamSpeed,
            angleOfAttackRadians,
            null,
            null,
            null
        };

        genericMethod.Invoke(null, args);
        return ((float)args[9]!, (float)args[10]!, (float)args[11]!);
    }

    private static (double[] dzdm, double[] dqdm) InvokeComputeWakeSourceSensitivitiesAt(
        object wakeGeometry,
        int fieldNodeIndex,
        double fieldX,
        double fieldY,
        double fieldNormalX,
        double fieldNormalY,
        int fieldWakeIndex,
        bool useLegacyPrecision)
    {
        MethodInfo method = typeof(InfluenceMatrixBuilder).GetMethod(
            useLegacyPrecision
                ? "ComputeWakeSourceSensitivitiesAtLegacyPrecision"
                : "ComputeWakeSourceSensitivitiesAt",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeWakeSourceSensitivitiesAt method not found.");

        object?[] args =
        {
            wakeGeometry,
            fieldNodeIndex,
            fieldX,
            fieldY,
            fieldNormalX,
            fieldNormalY,
            fieldWakeIndex,
            null,
            null
        };

        method.Invoke(null, args);
        return ((double[])args[7]!, (double[])args[8]!);
    }

    private static double[] BuildFirstWakeColumnRightHandSide(
        LinearVortexPanelState panel,
        object wakeGeometry)
    {
        int n = panel.NodeCount;
        var rhs = new double[n + 1];

        for (int row = 0; row < n; row++)
        {
            (double[] dzdm, _) = InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex: row + 1,
                fieldX: panel.X[row],
                fieldY: panel.Y[row],
                fieldNormalX: panel.NormalX[row],
                fieldNormalY: panel.NormalY[row],
                fieldWakeIndex: -1,
                useLegacyPrecision: true);

            rhs[row] = -dzdm[0];
        }

        rhs[n] = 0.0;
        return rhs;
    }

    private static double[] BuildFirstWakeColumnRightHandSide(
        IReadOnlyList<ReferencePanelNode> panelNodes,
        object wakeGeometry)
    {
        int n = panelNodes.Count;
        var rhs = new double[n + 1];

        for (int row = 0; row < n; row++)
        {
            ReferencePanelNode node = panelNodes[row];
            (double[] dzdm, _) = InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex: row + 1,
                fieldX: node.X,
                fieldY: node.Y,
                fieldNormalX: node.NormalX,
                fieldNormalY: node.NormalY,
                fieldWakeIndex: -1,
                useLegacyPrecision: true);

            rhs[row] = -dzdm[0];
        }

        rhs[n] = 0.0;
        return rhs;
    }

    private static double[] SolveWakeColumn(InviscidSolverState state, double[] rhs)
    {
        int size = state.NodeCount + 1;
        var rhsSingle = new float[size];
        for (int i = 0; i < size; i++)
        {
            rhsSingle[i] = (float)rhs[i];
        }

        ScaledPivotLuSolver.BackSubstitute(
            state.LegacyStreamfunctionInfluenceFactors,
            state.LegacyPivotIndices,
            rhsSingle,
            size,
            traceContext: null);

        var solved = new double[size];
        for (int i = 0; i < size; i++)
        {
            solved[i] = rhsSingle[i];
        }

        return solved;
    }

    private static double[] ReadColumnValues(string prefix)
    {
        string dumpPath = FortranReferenceCases.GetReferenceDumpPath(CaseId);
        Assert.True(File.Exists(dumpPath), $"Reference dump missing for case {CaseId}: {dumpPath}");

        var values = new SortedDictionary<int, double>();
        var regex = new Regex(
            $"^{Regex.Escape(prefix)} I=\\s*(\\d+) BIJ=\\s*([+-]?\\d\\.\\d+E[+-]\\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double value = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            values[index] = value;
        }

        Assert.NotEmpty(values);
        int expectedCount = values.Keys.Max();
        return Enumerable.Range(1, expectedCount)
            .Select(index => values[index])
            .ToArray();
    }

    private static IReadOnlyList<ReferenceWakeNode> ReadReferenceWakeNodes()
    {
        string dumpPath = FortranReferenceCases.GetReferenceDumpPath(CaseId);
        Assert.True(File.Exists(dumpPath), $"Reference dump missing for case {CaseId}: {dumpPath}");

        var nodes = new SortedDictionary<int, ReferenceWakeNode>();
        var regex = new Regex(
            @"^WAKE_NODE IW=\s*(\d+) X=\s*([+-]?\d\.\d+E[+-]\d+) Y=\s*([+-]?\d\.\d+E[+-]\d+) NX=\s*([+-]?\d\.\d+E[+-]\d+) NY=\s*([+-]?\d\.\d+E[+-]\d+) APAN=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            nodes[index] = new ReferenceWakeNode(
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture));
        }

        Assert.NotEmpty(nodes);
        return Enumerable.Range(1, nodes.Keys.Max())
            .Select(index => nodes[index])
            .ToArray();
    }

    private static IReadOnlyList<ReferencePanelNode> ReadReferencePanelNodes()
    {
        string dumpPath = FortranReferenceCases.GetReferenceDumpPath(CaseId);
        Assert.True(File.Exists(dumpPath), $"Reference dump missing for case {CaseId}: {dumpPath}");

        var nodes = new SortedDictionary<int, ReferencePanelNode>();
        var regex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)\s+NX=\s*([+-]?\d\.\d+E[+-]\d+)\s+NY=\s*([+-]?\d\.\d+E[+-]\d+)\s+APAN=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            nodes[index] = new ReferencePanelNode(
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture));
        }

        Assert.NotEmpty(nodes);
        return Enumerable.Range(1, nodes.Keys.Max())
            .Select(index => nodes[index])
            .ToArray();
    }

    private static IReadOnlyList<ReferenceBasisGammaRow> ReadReferenceBasisGammaRows()
    {
        string dumpPath = FortranReferenceCases.GetReferenceDumpPath(CaseId);
        Assert.True(File.Exists(dumpPath), $"Reference dump missing for case {CaseId}: {dumpPath}");

        var rows = new SortedDictionary<int, ReferenceBasisGammaRow>();
        var regex = new Regex(
            @"^GAMU_ROW I=\s*(\d+)\s+U1=\s*([+-]?\d\.\d+E[+-]\d+)\s+U2=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            rows[index] = new ReferenceBasisGammaRow(
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        Assert.NotEmpty(rows);
        return Enumerable.Range(1, rows.Keys.Max())
            .Select(index => rows[index])
            .ToArray();
    }

    private static IReadOnlyDictionary<(int row, int col), double> ReadReferenceAijEntries()
    {
        string dumpPath = FortranReferenceCases.GetReferenceDumpPath(CaseId);
        Assert.True(File.Exists(dumpPath), $"Reference dump missing for case {CaseId}: {dumpPath}");

        var entries = new Dictionary<(int row, int col), double>();
        var regex = new Regex(
            @"^AIJ_ROW I=\s*(\d+)\s+J=\s*(\d+)\s+VAL=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int row = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int col = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            entries[(row, col)] = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        }

        Assert.NotEmpty(entries);
        return entries;
    }

    private static object BuildWakeGeometryFromReference(IReadOnlyList<ReferenceWakeNode> referenceWakeNodes)
    {
        int count = referenceWakeNodes.Count;
        var x = new double[count];
        var y = new double[count];
        var nx = new double[count];
        var ny = new double[count];
        var panelAngle = new double[Math.Max(count - 1, 1)];

        for (int i = 0; i < count; i++)
        {
            ReferenceWakeNode node = referenceWakeNodes[i];
            x[i] = node.X;
            y[i] = node.Y;
            nx[i] = node.NormalX;
            ny[i] = node.NormalY;
            if (i < count - 1)
            {
                panelAngle[i] = node.PanelAngle;
            }
        }

        if (count == 1)
        {
            panelAngle[0] = referenceWakeNodes[0].PanelAngle;
        }

        Type wakeGeometryType = typeof(InfluenceMatrixBuilder).GetNestedType(
            "WakeGeometryData",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WakeGeometryData type not found.");

        return Activator.CreateInstance(
            wakeGeometryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { x, y, nx, ny, panelAngle },
            culture: CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("WakeGeometryData construction failed.");
    }

    private static object BuildWakeGeometryFromTrace(IReadOnlyList<ParityTraceRecord> traceRecords)
    {
        double[] x = ReadTraceBasisEntries(traceRecords, "wake_geometry_x");
        double[] y = ReadTraceBasisEntries(traceRecords, "wake_geometry_y");
        double[] nx = ReadTraceBasisEntries(traceRecords, "wake_geometry_nx");
        double[] ny = ReadTraceBasisEntries(traceRecords, "wake_geometry_ny");
        double[] panelAngle = ReadTraceBasisEntries(traceRecords, "wake_geometry_panel_angle");

        Type wakeGeometryType = typeof(InfluenceMatrixBuilder).GetNestedType(
            "WakeGeometryData",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WakeGeometryData type not found.");

        return Activator.CreateInstance(
            wakeGeometryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { x, y, nx, ny, panelAngle },
            culture: CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("WakeGeometryData construction failed.");
    }

    private static double[] ReadTraceBasisEntries(IReadOnlyList<ParityTraceRecord> records, string name)
    {
        ParityTraceRecord[] entries = records
            .Where(record => record.Kind == "basis_entry" &&
                             string.Equals(record.Name, name, StringComparison.Ordinal))
            .OrderBy(record => record.Data.GetProperty("index").GetInt32())
            .ToArray();

        Assert.NotEmpty(entries);
        return entries
            .Select(entry => (double)ReadTraceSingle(entry, "value"))
            .ToArray();
    }

    private static double[] GetWakeArray(object wakeGeometry, string propertyName)
    {
        PropertyInfo property = wakeGeometry.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{propertyName} property not found on wake geometry.");

        return (double[])property.GetValue(wakeGeometry)!;
    }

    private static ParityTraceRecord ReadLatestWakePanelStateReferenceTrace()
        => ReadWakePanelStateReferenceTrace(WakePanelReferenceDirectory, index: 1);

    private static ParityTraceRecord ReadWakePanelStateReferenceTrace(string directory, int index)
    {
        string path = GetLatestVersionedReferenceTracePath(directory);

        return ParityTraceLoader.ReadAll(path)
            .Single(record => record.Kind == "wake_panel_state" &&
                              record.Data.GetProperty("index").GetInt32() == index);
    }

    private static ParityTraceRecord ReadWakeStepReferenceTrace(string directory, int index)
    {
        string path = GetLatestVersionedReferenceTracePath(directory);

        return ParityTraceLoader.ReadAll(path)
            .Single(record => record.Kind == "wake_step_terms" &&
                              record.Data.GetProperty("index").GetInt32() == index);
    }

    private static ParityTraceRecord CaptureWakePanelStateTrace(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        int wakeNodeIndex,
        double x,
        double y,
        double fallbackNormalX,
        double fallbackNormalY,
        double angleOfAttackRadians)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-wake-panel-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var envScope = TraceEnvironmentIsolation.Clear();
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "wake-panel-state" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            InvokeComputeWakePanelState(
                panel,
                state,
                wakeNodeIndex,
                x,
                y,
                fallbackNormalX,
                fallbackNormalY,
                angleOfAttackRadians);

            return ParityTraceLoader.ReadAll(tracePath)
                .Single(record => record.Kind == "wake_panel_state");
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static IReadOnlyList<ParityTraceRecord> ReadPsilinWake81ReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(WakePanelReferenceDirectory);

        return ParityTraceLoader.ReadAll(path);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadBasisGammaReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(BasisGammaReferenceDirectory);

        return ParityTraceLoader.ReadAll(path);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadPswlinRow43ReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(PswlinRow43ReferenceDirectory);

        return FilterWakeSourceTraceRecords(ParityTraceLoader.ReadAll(path), fieldIndex: 43, wakeSegment: 1, PswlinRow43TraceKinds);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadPswlinRow55ReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(PswlinRow55ReferenceDirectory);

        return FilterWakeSourceTraceRecords(ParityTraceLoader.ReadAll(path), fieldIndex: 55, wakeSegment: 1, PswlinRow43TraceKinds);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadPswlinRow55FullReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(PswlinRow55FullReferenceDirectory);

        return FilterWakeSourceTraceRecords(
            ParityTraceLoader.ReadAll(path),
            fieldIndex: 55,
            wakeSegment: null,
            PswlinRow55FullTraceKinds);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadPswlinRow55NiReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(PswlinRow55NiReferenceDirectory);

        return FilterWakeSourceTraceRecords(
            ParityTraceLoader.ReadAll(path),
            fieldIndex: 55,
            wakeSegment: null,
            PswlinNiTraceKinds);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadWakeGeometryReferenceTrace()
    {
        string path = GetLatestVersionedReferenceTracePath(WakeGeometryReferenceDirectory);

        return FilterParityTraceRecords(ParityTraceLoader.ReadAll(path));
    }

    private static string GetLatestVersionedReferenceTracePath(string directory)
    {
        Assert.True(Directory.Exists(directory), $"Reference trace directory missing: {directory}");

        string? latest = Directory.EnumerateFiles(directory, "reference_trace.*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Counter = TryParseVersionedArtifactCounter(Path.GetFileName(path), "reference_trace.", ".jsonl")
            })
            .Where(entry => entry.Counter is not null)
            .OrderByDescending(entry => entry.Counter)
            .Select(entry => entry.Path)
            .FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(latest), $"No numbered reference trace found in {directory}");
        return latest!;
    }

    private static long? TryParseVersionedArtifactCounter(string fileName, string prefix, string suffix)
    {
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        string middle = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        return long.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out long counter)
            ? counter
            : null;
    }

    private static IReadOnlyList<ParityTraceRecord> CaptureWakePanelStateTraceRecords(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        int wakeNodeIndex,
        double x,
        double y,
        double fallbackNormalX,
        double fallbackNormalY,
        double angleOfAttackRadians)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-wake-panel-fulltrace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var envScope = TraceEnvironmentIsolation.Clear();
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "wake-panel-state-full" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            InvokeComputeWakePanelState(
                panel,
                state,
                wakeNodeIndex,
                x,
                y,
                fallbackNormalX,
                fallbackNormalY,
                angleOfAttackRadians);

            return ParityTraceLoader.ReadAll(tracePath);
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static IReadOnlyList<ParityTraceRecord> CaptureWakeGeometryTraceRecords(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double angleOfAttackRadians)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-wake-geometry-fulltrace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var envScope = TraceEnvironmentIsolation.Clear();
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "wake-geometry-full" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            int nWake = Math.Max((panel.NodeCount / 8) + 2, 3);
            _ = InvokeBuildWakeGeometry(panel, state, nWake, FreestreamSpeed, angleOfAttackRadians);

            return ParityTraceLoader.ReadAll(tracePath);
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static IReadOnlyList<ParityTraceRecord> CaptureWakeSourceSensitivityTrace(
        object wakeGeometry,
        int fieldNodeIndex,
        double fieldX,
        double fieldY,
        double fieldNormalX,
        double fieldNormalY,
        int fieldWakeIndex,
        int? wakeSegment = 1,
        IReadOnlyList<string>? kinds = null)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-wake-source-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var envScope = TraceEnvironmentIsolation.Clear();
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "wake-source-row" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            _ = InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex,
                fieldX,
                fieldY,
                fieldNormalX,
                fieldNormalY,
                fieldWakeIndex,
                useLegacyPrecision: true);

            return FilterWakeSourceTraceRecords(
                ParityTraceLoader.ReadAll(tracePath),
                fieldNodeIndex,
                wakeSegment,
                kinds ?? PswlinRow43TraceKinds);
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static IReadOnlyList<ParityTraceRecord> ExtractFirstPsilinCall(IReadOnlyList<ParityTraceRecord> records)
    {
        var call = new List<ParityTraceRecord>();
        bool inside = false;

        foreach (ParityTraceRecord record in records)
        {
            if (record.Kind == "psilin_field")
            {
                if (inside)
                {
                    break;
                }

                inside = true;
            }

            if (inside && record.Kind.StartsWith("psilin_", StringComparison.Ordinal))
            {
                call.Add(record);
            }
        }

        return call;
    }

    private static IReadOnlyList<ParityTraceRecord> ExtractPsilinCallByFieldNormal(
        IReadOnlyList<ParityTraceRecord> records,
        float fieldNormalX,
        float fieldNormalY)
    {
        var call = new List<ParityTraceRecord>();
        bool inside = false;

        foreach (ParityTraceRecord record in records)
        {
            if (record.Kind == "psilin_field")
            {
                if (inside)
                {
                    break;
                }

                if (!TraceFieldMatchesNormal(record, fieldNormalX, fieldNormalY))
                {
                    continue;
                }

                inside = true;
            }

            if (inside && record.Kind.StartsWith("psilin_", StringComparison.Ordinal))
            {
                call.Add(record);
            }
        }

        Assert.NotEmpty(call);
        return call;
    }

    private static IReadOnlyList<ParityTraceRecord> FilterParityTraceRecords(IReadOnlyList<ParityTraceRecord> records)
        => records
            .Where(record => record.Kind is not ("session_start" or "session_end"))
            .ToArray();

    private static IReadOnlyList<ParityTraceRecord> FilterWakeSourceTraceRecords(IReadOnlyList<ParityTraceRecord> records)
        => FilterParityTraceRecords(records)
            .Where(record => record.Kind is
                "pswlin_field" or
                "pswlin_geometry" or
                "pswlin_half_terms" or
                "wake_source_accum" or
                "pswlin_recurrence" or
                "pswlin_segment" or
                "wake_source_entry")
            .ToArray();

    private static IReadOnlyList<ParityTraceRecord> FilterWakeSourceTraceRecords(
        IReadOnlyList<ParityTraceRecord> records,
        int fieldIndex,
        int? wakeSegment,
        IReadOnlyList<string> kinds)
        => FilterParityTraceRecords(records)
            .Where(record => kinds.Contains(record.Kind, StringComparer.Ordinal))
            .Where(record => record.Data.TryGetProperty("fieldIndex", out JsonElement fieldIndexElement) &&
                             fieldIndexElement.GetInt32() == fieldIndex)
            .Where(record => !wakeSegment.HasValue ||
                             !record.Data.TryGetProperty("wakeSegment", out JsonElement wakeSegmentElement) ||
                             wakeSegmentElement.GetInt32() == wakeSegment.Value)
            .ToArray();

    private static void SeedVortexStrengthFromPsilinTrace(InviscidSolverState state, IReadOnlyList<ParityTraceRecord> records)
    {
        foreach (ParityTraceRecord record in records.Where(record => record.Kind == "psilin_vortex_segment"))
        {
            int jo = record.Data.GetProperty("jo").GetInt32();
            int jp = record.Data.GetProperty("jp").GetInt32();
            state.VortexStrength[jo - 1] = ReadTraceSingle(record, "gammaJo");
            state.VortexStrength[jp - 1] = ReadTraceSingle(record, "gammaJp");
        }
    }

    private static void AssertTraceRecordSequenceEqual(IReadOnlyList<ParityTraceRecord> expected, IReadOnlyList<ParityTraceRecord> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Kind, actual[i].Kind);

            string[] fields = expected[i].Kind switch
            {
                "pswlin_field" => PswlinFieldFields,
                "pswlin_geometry" => PswlinGeometryFields,
                "pswlin_half_terms" => PswlinHalfTermFields,
                "pswlin_pdx0_terms" => PswlinPdx0TermFields,
                "wake_source_accum" => WakeSourceAccumFields,
                "pswlin_recurrence" => PswlinRecurrenceFields,
                "pswlin_segment" => PswlinSegmentFields,
                "wake_source_entry" => WakeSourceEntryFields,
                _ => Array.Empty<string>()
            };

            string location = BuildTraceLocation(expected[i], i);
            foreach (string field in fields)
            {
                string expectedValue = ReadTraceComparableValue(expected[i], field);
                string actualValue = ReadTraceComparableValue(actual[i], field);
                Assert.True(
                    string.Equals(expectedValue, actualValue, StringComparison.Ordinal),
                    $"{expected[i].Kind} {location} field={field} Fortran={expectedValue} Managed={actualValue}");
            }
        }
    }

    private static void AssertTraceRecordsOfKindEqual(
        IReadOnlyList<ParityTraceRecord> expected,
        IReadOnlyList<ParityTraceRecord> actual,
        string kind,
        IReadOnlyList<string> fields)
    {
        ParityTraceRecord[] expectedRecords = expected.Where(record => record.Kind == kind).ToArray();
        ParityTraceRecord[] actualRecords = actual.Where(record => record.Kind == kind).ToArray();

        Assert.Equal(expectedRecords.Length, actualRecords.Length);

        for (int i = 0; i < expectedRecords.Length; i++)
        {
            string location = BuildTraceLocation(expectedRecords[i], i);
            foreach (string field in fields)
            {
                string expectedValue = ReadTraceComparableValue(expectedRecords[i], field);
                string actualValue = ReadTraceComparableValue(actualRecords[i], field);
                Assert.True(
                    string.Equals(expectedValue, actualValue, StringComparison.Ordinal),
                    $"{kind} {location} field={field} Fortran={expectedValue} Managed={actualValue}");
            }
        }
    }

    private static void AssertSingleTraceRecordFieldsEqual(
        ParityTraceRecord expected,
        ParityTraceRecord actual,
        IReadOnlyList<string> fields,
        string description)
    {
        foreach (string field in fields)
        {
            string expectedValue = ReadTraceComparableValue(expected, field);
            string actualValue = ReadTraceComparableValue(actual, field);
            Assert.True(
                string.Equals(expectedValue, actualValue, StringComparison.Ordinal),
                $"{description} field={field} Fortran={expectedValue} Managed={actualValue}");
        }
    }

    private static string BuildTraceLocation(ParityTraceRecord record, int index)
    {
        string wakeSegment = record.Data.TryGetProperty("wakeSegment", out JsonElement wakeSegmentElement)
            ? $" seg={wakeSegmentElement.GetInt32()}"
            : string.Empty;
        string half = record.Data.TryGetProperty("half", out JsonElement halfElement)
            ? $" half={halfElement.GetInt32()}"
            : string.Empty;
        string entry = record.Data.TryGetProperty("index", out JsonElement indexElement)
            ? $" index={indexElement.GetInt32()}"
            : string.Empty;
        string term = record.Data.TryGetProperty("term", out JsonElement termElement)
            ? $" term={termElement.GetString()}"
            : string.Empty;

        return $"record={index}{wakeSegment}{half}{entry}{term}";
    }

    private static string ReadTraceComparableValue(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? f32))
            {
                return f32;
            }

            if (bits.TryGetValue("f64", out string? f64))
            {
                return f64;
            }

            if (bits.TryGetValue("i32", out string? i32))
            {
                return i32;
            }

            if (bits.TryGetValue("i64", out string? i64))
            {
                return i64;
            }
        }

        if (!record.Data.TryGetProperty(fieldName, out JsonElement property))
        {
            return "<missing>";
        }

        return property.ValueKind switch
        {
            JsonValueKind.Null => "<null>",
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number when property.TryGetInt64(out long integer) => integer.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Number when record.Data.TryGetProperty("precision", out JsonElement precisionElement) &&
                                      precisionElement.ValueKind == JsonValueKind.String &&
                                      string.Equals(precisionElement.GetString(), nameof(Single), StringComparison.Ordinal)
                => $"0x{unchecked((uint)BitConverter.SingleToInt32Bits((float)property.GetDouble())):X8}",
            JsonValueKind.Number => BitConverter.DoubleToInt64Bits(property.GetDouble()).ToString("X16", CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => property.ToString()
        };
    }

    private static IReadOnlyList<TracePairRecord> ReadPairTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new TracePairRecord(
                record.Data.GetProperty("jo").GetInt32(),
                record.Data.GetProperty("jp").GetInt32(),
                fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static ParityTraceRecord SelectPsilinPairRecord(
        IReadOnlyList<ParityTraceRecord> records,
        string kind,
        int jo,
        int jp)
    {
        return records.Single(
            record => record.Kind == kind &&
                      record.Data.GetProperty("jo").GetInt32() == jo &&
                      record.Data.GetProperty("jp").GetInt32() == jp);
    }

    private static IReadOnlyList<TraceAccumRecord> ReadAccumTraceRecords(IReadOnlyList<ParityTraceRecord> records)
    {
        return records
            .Where(record => record.Kind == "psilin_accum_state")
            .Select(record => new TraceAccumRecord(
                record.Data.GetProperty("stage").GetString() ?? string.Empty,
                record.Data.GetProperty("jo").GetInt32(),
                record.Data.GetProperty("jp").GetInt32(),
                PsilinAccumFields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<TraceResultRecord> ReadResultTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new TraceResultRecord(fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static void AssertPairTraceRecordsEqual(
        IReadOnlyList<TracePairRecord> expected,
        IReadOnlyList<TracePairRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].Jo, actual[recordIndex].Jo);
            Assert.Equal(expected[recordIndex].Jp, actual[recordIndex].Jp);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"{kind} record={recordIndex} jo={expected[recordIndex].Jo} jp={expected[recordIndex].Jp} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertAccumTraceRecordsEqual(
        IReadOnlyList<TraceAccumRecord> expected,
        IReadOnlyList<TraceAccumRecord> actual,
        IReadOnlyList<string> fieldNames)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].Stage, actual[recordIndex].Stage);
            Assert.Equal(expected[recordIndex].Jo, actual[recordIndex].Jo);
            Assert.Equal(expected[recordIndex].Jp, actual[recordIndex].Jp);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"psilin_accum_state record={recordIndex} stage={expected[recordIndex].Stage} jo={expected[recordIndex].Jo} jp={expected[recordIndex].Jp} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertResultTraceRecordsEqual(
        IReadOnlyList<TraceResultRecord> expected,
        IReadOnlyList<TraceResultRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"{kind} record={recordIndex} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static string ReadTraceBits(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? single))
            {
                return single[2..];
            }

            if (bits.TryGetValue("f64", out string? dbl))
            {
                ulong raw = ulong.Parse(dbl[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return $"{BitConverter.SingleToInt32Bits((float)BitConverter.Int64BitsToDouble(unchecked((long)raw))):X8}";
            }
        }

        return $"{BitConverter.SingleToInt32Bits((float)record.Data.GetProperty(fieldName).GetDouble()):X8}";
    }

    private static float ReadTraceSingle(ParityTraceRecord record, string fieldName)
        => BitConverter.Int32BitsToSingle(
            int.Parse(ReadTraceBits(record, fieldName), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static bool TraceFieldMatchesNormal(ParityTraceRecord record, float fieldNormalX, float fieldNormalY)
    {
        if (record.Kind != "psilin_field")
        {
            return false;
        }

        return BitConverter.SingleToInt32Bits((float)record.Data.GetProperty("fieldNormalX").GetDouble()) == BitConverter.SingleToInt32Bits(fieldNormalX) &&
               BitConverter.SingleToInt32Bits((float)record.Data.GetProperty("fieldNormalY").GetDouble()) == BitConverter.SingleToInt32Bits(fieldNormalY);
    }

    private sealed record TracePairRecord(int Jo, int Jp, IReadOnlyList<string> Values);

    private sealed record TraceAccumRecord(string Stage, int Jo, int Jp, IReadOnlyList<string> Values);

    private sealed record TraceResultRecord(IReadOnlyList<string> Values);

    private static void AssertColumnMatches(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> managed,
        double tolerance,
        string description)
    {
        Assert.Equal(reference.Count, managed.Count);

        int maxIndex = -1;
        double maxDelta = double.NegativeInfinity;
        double referenceAtMax = 0.0;
        double managedAtMax = 0.0;

        for (int i = 0; i < reference.Count; i++)
        {
            double delta = Math.Abs(reference[i] - managed[i]);
            if (delta > maxDelta)
            {
                maxDelta = delta;
                maxIndex = i;
                referenceAtMax = reference[i];
                managedAtMax = managed[i];
            }
        }

        Assert.True(
            maxDelta <= tolerance,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} mismatch at row {1}: Fortran={2:E8} Managed={3:E8} abs={4:E8} tol={5:E8}",
                description,
                maxIndex + 1,
                referenceAtMax,
                managedAtMax,
                maxDelta,
                tolerance));
    }

    private static void AssertExactSingleMatch(string description, double referenceValue, double managedValue)
    {
        int referenceBits = BitConverter.SingleToInt32Bits((float)referenceValue);
        int managedBits = BitConverter.SingleToInt32Bits((float)managedValue);
        Assert.True(
            referenceBits == managedBits,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} mismatch: Fortran={1:E8} [{2}] Managed={3:E8} [{4}]",
                description,
                referenceValue,
                FormatFloatBits(referenceBits),
                managedValue,
                FormatFloatBits(managedBits)));
    }

    private static void AssertWakeTraceGammaMatchesManagedState(
        ManagedWakeColumnContext context,
        ParityTraceRecord vortexSegment,
        int stateIndex,
        string fieldName,
        string description)
    {
        float referenceValue = ReadTraceSingle(vortexSegment, fieldName);
        float managedValue = (float)context.State.VortexStrength[stateIndex];

        if (BitConverter.SingleToInt32Bits(referenceValue) == BitConverter.SingleToInt32Bits(managedValue))
        {
            return;
        }

        float basisAlpha0 = (float)context.State.BasisVortexStrength[stateIndex, 0];
        float basisAlpha90 = (float)context.State.BasisVortexStrength[stateIndex, 1];
        string candidateReport = DescribeSuperpositionCandidates(context.AlphaRadians, basisAlpha0, basisAlpha90);

        Assert.Fail(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} mismatch: Fortran={1:E8} [{2}] Managed={3:E8} [{4}] alpha0={5:E8} [{6}] alpha90={7:E8} [{8}] candidates: {9}",
                description,
                referenceValue,
                FormatFloatBits(BitConverter.SingleToInt32Bits(referenceValue)),
                managedValue,
                FormatFloatBits(BitConverter.SingleToInt32Bits(managedValue)),
                basisAlpha0,
                FormatFloatBits(BitConverter.SingleToInt32Bits(basisAlpha0)),
                basisAlpha90,
                FormatFloatBits(BitConverter.SingleToInt32Bits(basisAlpha90)),
                candidateReport));
    }

    private static string DescribeSuperpositionCandidates(double alphaRadians, float basisAlpha0, float basisAlpha90)
    {
        float alphaSingle = (float)alphaRadians;

        static string FormatCandidate(string name, float value)
            => string.Format(
                CultureInfo.InvariantCulture,
                "{0}={1:E8}[{2}]",
                name,
                value,
                FormatFloatBits(BitConverter.SingleToInt32Bits(value)));

        float mathfFloatAlpha = (MathF.Cos(alphaSingle) * basisAlpha0) + (MathF.Sin(alphaSingle) * basisAlpha90);
        float mathDoubleAlphaCast = ((float)Math.Cos(alphaRadians) * basisAlpha0) + ((float)Math.Sin(alphaRadians) * basisAlpha90);
        float mathFloatAlphaCast = ((float)Math.Cos(alphaSingle) * basisAlpha0) + ((float)Math.Sin(alphaSingle) * basisAlpha90);
        float mathfFloatAlphaCastEnd = (float)(((double)MathF.Cos(alphaSingle) * basisAlpha0) + ((double)MathF.Sin(alphaSingle) * basisAlpha90));
        float mathDoubleAlphaCastEnd = (float)(((double)(float)Math.Cos(alphaRadians) * basisAlpha0) + ((double)(float)Math.Sin(alphaRadians) * basisAlpha90));

        return string.Join(
            "; ",
            new[]
            {
                FormatCandidate("mathf_float_alpha", mathfFloatAlpha),
                FormatCandidate("math_double_alpha_cast", mathDoubleAlphaCast),
                FormatCandidate("math_float_alpha_cast", mathFloatAlphaCast),
                FormatCandidate("mathf_float_alpha_cast_end", mathfFloatAlphaCastEnd),
                FormatCandidate("math_double_alpha_cast_end", mathDoubleAlphaCastEnd)
            });
    }

    private static string FormatFloatBits(int bits)
    {
        return $"0x{unchecked((uint)bits):X8}";
    }

    private sealed record ManagedWakeColumnContext(
        LinearVortexPanelState Panel,
        InviscidSolverState State,
        object WakeGeometry,
        double AlphaRadians);

    private sealed record ReferenceWakeNode(
        double X,
        double Y,
        double NormalX,
        double NormalY,
        double PanelAngle);

    private sealed record ReferencePanelNode(
        double X,
        double Y,
        double NormalX,
        double NormalY,
        double PanelAngle);

    private sealed record ReferenceBasisGammaRow(
        double Alpha0,
        double Alpha90);
}

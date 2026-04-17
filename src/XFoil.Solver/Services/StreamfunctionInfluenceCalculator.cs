using XFoil.Core.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSILIN
// Secondary legacy source: f_xfoil/src/xpanel.f :: GGCALC/QDCALC call sites
// Role in port: Computes the streamfunction and tangential-velocity sensitivity kernels that drive the linear-vortex inviscid solve and viscous coupling.
// Differences: The PSILIN lineage is direct, but the managed implementation splits the double path, the parity single-precision replay path, the source/vortex/TE subkernels, and the structured trace hooks into separate methods so binary mismatches can be isolated precisely.
// Decision: Keep the decomposed managed structure and preserve the legacy source/vortex arithmetic order in the parity path because this file is one of the main solver-fidelity boundaries.
namespace XFoil.Solver.Services;

/// <summary>
/// Computes streamfunction influence coefficients for the linear-vorticity panel method.
/// This is the computational heart of the formulation -- a direct port of XFoil's PSILIN
/// algorithm (xpanel.f lines 87-476) in clean idiomatic C# with 0-based indexing.
///
/// For each field point, computes PSI (streamfunction) and all sensitivity arrays
/// due to all panels, the trailing-edge panel, and the freestream.
/// </summary>
public static class StreamfunctionInfluenceCalculator
{
    private const string traceScope = nameof(StreamfunctionInfluenceCalculator);

    /// <summary>
    /// Computes the streamfunction PSI and all sensitivity arrays at a single field point
    /// due to all panels, the TE panel, and the freestream.
    ///
    /// Writes to state workspace arrays:
    /// - StreamfunctionVortexSensitivity[j] = dPSI/dGamma_j (DZDG in XFoil)
    /// - StreamfunctionSourceSensitivity[j] = dPSI/dSigma_j (DZDM in XFoil)
    /// - VelocityVortexSensitivity[j] = dQ_tangential/dGamma_j (DQDG in XFoil)
    /// - VelocitySourceSensitivity[j] = dQ_tangential/dSigma_j (DQDM in XFoil)
    /// </summary>
    /// <param name="fieldNodeIndex">Node index of the field point (0-based). Use -1 for off-body points.</param>
    /// <param name="fieldX">Field point X coordinate.</param>
    /// <param name="fieldY">Field point Y coordinate.</param>
    /// <param name="fieldNormalX">Outward normal X component at the field point.</param>
    /// <param name="fieldNormalY">Outward normal Y component at the field point.</param>
    /// <param name="computeGeometricSensitivities">If true, compute DZDN geometric sensitivities (for inverse design).</param>
    /// <param name="includeSourceTerms">If true, compute source influence (needed for GGCALC assembly).</param>
    /// <param name="panel">Panel geometry state.</param>
    /// <param name="state">Inviscid solver state (workspace arrays are written).</param>
    /// <param name="freestreamSpeed">Freestream speed magnitude (QINF).</param>
    /// <param name="angleOfAttackRadians">Angle of attack in radians (ALFA).</param>
    /// <returns>Tuple of (psi, psiNormalDerivative).</returns>
    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN.
    // Difference from legacy: The managed entry point routes either to the precise double kernel or the explicit single-precision replay kernel, while keeping the PSILIN call signature readable for other managed services.
    // Decision: Keep the entry point split because it makes parity intent explicit without changing the underlying kernel responsibilities.
    public static (double psi, double psiNormalDerivative) ComputeInfluenceAt(
        int fieldNodeIndex,
        double fieldX, double fieldY,
        double fieldNormalX, double fieldNormalY,
        bool computeGeometricSensitivities,
        bool includeSourceTerms,
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        int n = panel.NodeCount;

        if (state.UseLegacyKernelPrecision && !computeGeometricSensitivities)
        {
            return ComputeInfluenceAtLegacyPrecision(
                fieldNodeIndex,
                fieldX,
                fieldY,
                fieldNormalX,
                fieldNormalY,
                includeSourceTerms,
                panel,
                state,
                freestreamSpeed,
                angleOfAttackRadians);
        }

        TracePsilinField(
            traceScope,
            fieldNodeIndex + 1,
            fieldX,
            fieldY,
            fieldNormalX,
            fieldNormalY,
            computeGeometricSensitivities,
            includeSourceTerms,
            "Double");

        // Scaling constants
        const double quarterOverPi = 1.0 / (4.0 * Math.PI);  // QOPI
        const double halfOverPi = 1.0 / (2.0 * Math.PI);      // HOPI

        // Distance tolerance for degenerate panel detection
        // Fortran: SEPS = (S(N)-S(1)) * 1.0E-5
        double seps = (panel.ArcLength[n - 1] - panel.ArcLength[0]) * 1.0e-5;

        // Map field node index to 1-based for comparison with Fortran convention
        // In Fortran: IO = I where I is 1-based. Airfoil nodes: 1 <= IO <= N.
        // In C#: fieldNodeIndex is 0-based. Airfoil nodes: 0 <= fieldNodeIndex < n.
        // Use io as the 1-based equivalent for the SGN reflection logic.
        int io = fieldNodeIndex + 1; // -1 becomes 0 (off-body), 0 becomes 1, etc.

        double cosa = Math.Cos(angleOfAttackRadians);
        double sina = Math.Sin(angleOfAttackRadians);

        // Zero sensitivity arrays
        for (int j = 0; j < n; j++)
        {
            state.StreamfunctionVortexSensitivity[j] = 0.0;
            state.StreamfunctionNormalSensitivity[j] = 0.0;
            state.VelocityVortexSensitivity[j] = 0.0;
            state.StreamfunctionSourceSensitivity[j] = 0.0;
            state.VelocitySourceSensitivity[j] = 0.0;
        }

        double psi = 0.0;
        double psiNi = 0.0;

        // TE bisector ratios
        double scs, sds;
        if (state.IsSharpTrailingEdge)
        {
            scs = 1.0;
            sds = 0.0;
        }
        else
        {
            scs = state.TrailingEdgeAngleNormal / state.TrailingEdgeGap;
            sds = state.TrailingEdgeAngleStreamwise / state.TrailingEdgeGap;
        }

        // Local variables that persist across the last-panel TE treatment
        double x1 = 0.0, x2 = 0.0, yy = 0.0;
        double g1 = 0.0, g2 = 0.0, t1 = 0.0, t2 = 0.0;
        double rs1 = 0.0, rs2 = 0.0;
        double x1i = 0.0, x2i = 0.0, yyi = 0.0;
        double apan = 0.0;

        // Track the last valid jo/jp for TE panel treatment
        int lastJo = n - 1;
        int lastJp = 0;
        bool skipTEPanel = false;

        // Main panel loop: jo = 0 to n-1 (Fortran: JO = 1 to N)
        for (int jo = 0; jo < n; jo++)
        {
            int jp = jo + 1;
            int jm = jo - 1;
            int jq = jp + 1;

            // Boundary clamping (Fortran: JO=1 -> JM=JO, JO=N-1 -> JQ=JP, JO=N -> JP=1)
            if (jo == 0)
            {
                jm = jo;
            }
            else if (jo == n - 2)
            {
                jq = jp;
            }
            else if (jo == n - 1)
            {
                jp = 0;
                // Check if TE panel is degenerate (sharp TE with coincident endpoints)
                double dxte = panel.X[jo] - panel.X[jp];
                double dyte = panel.Y[jo] - panel.Y[jp];
                if (dxte * dxte + dyte * dyte < seps * seps)
                {
                    // Skip to TE panel treatment (label 12 in Fortran)
                    skipTEPanel = true;
                    break;
                }
            }

            double dso = Math.Sqrt(
                (panel.X[jo] - panel.X[jp]) * (panel.X[jo] - panel.X[jp]) +
                (panel.Y[jo] - panel.Y[jp]) * (panel.Y[jo] - panel.Y[jp]));

            // Skip null panel
            if (dso == 0.0)
            {
                continue;
            }

            double dsio = 1.0 / dso;

            apan = panel.PanelAngle[jo];

            // Vectors from panel nodes to field point
            double rx1 = fieldX - panel.X[jo];
            double ry1 = fieldY - panel.Y[jo];
            double rx2 = fieldX - panel.X[jp];
            double ry2 = fieldY - panel.Y[jp];

            // Panel unit tangent
            double sx = (panel.X[jp] - panel.X[jo]) * dsio;
            double sy = (panel.Y[jp] - panel.Y[jo]) * dsio;

            // Transform field point to panel-local coordinates
            x1 = sx * rx1 + sy * ry1;
            x2 = sx * rx2 + sy * ry2;
            yy = sx * ry1 - sy * rx1;

            // Squared distances
            rs1 = rx1 * rx1 + ry1 * ry1;
            rs2 = rx2 * rx2 + ry2 * ry2;

            // Reflection flag SGN to avoid branch problems with arctan
            // Fortran: IO >= 1 and IO <= N means airfoil surface -> SGN=1
            // Otherwise (wake/off-body): SGN = sign(1, YY)
            double sgn;
            if (io >= 1 && io <= n)
            {
                sgn = 1.0;
            }
            else
            {
                sgn = yy >= 0.0 ? 1.0 : -1.0;
            }

            // Log(r^2) and atan2(x/y) terms with self-influence protection
            // Fortran: IO != JO (1-based) -> fieldNodeIndex != jo (0-based)
            if (fieldNodeIndex != jo && rs1 > 0.0)
            {
                g1 = Math.Log(rs1);
                t1 = Math.Atan2(sgn * x1, sgn * yy) + (0.5 - 0.5 * sgn) * Math.PI;
            }
            else
            {
                g1 = 0.0;
                t1 = 0.0;
            }

            // Fortran: IO != JP (1-based) -> fieldNodeIndex != jp (0-based)
            if (fieldNodeIndex != jp && rs2 > 0.0)
            {
                g2 = Math.Log(rs2);
                t2 = Math.Atan2(sgn * x2, sgn * yy) + (0.5 - 0.5 * sgn) * Math.PI;
            }
            else
            {
                g2 = 0.0;
                t2 = 0.0;
            }

            // Normal-direction projections for tangential velocity computation
            x1i = sx * fieldNormalX + sy * fieldNormalY;
            x2i = sx * fieldNormalX + sy * fieldNormalY;
            yyi = sx * fieldNormalY - sy * fieldNormalX;

            TracePsilinPanel(
                traceScope,
                fieldIndex: io,
                panelIndex: jo + 1,
                jm: jm + 1,
                jo: jo + 1,
                jp: jp + 1,
                jq: jq + 1,
                computeGeometricSensitivities,
                includeSourceTerms,
                precision: "Double",
                panelXJo: panel.X[jo],
                panelYJo: panel.Y[jo],
                panelXJp: panel.X[jp],
                panelYJp: panel.Y[jp],
                panelDx: panel.X[jo] - panel.X[jp],
                panelDy: panel.Y[jo] - panel.Y[jp],
                dso,
                dsio,
                apan,
                rx1,
                ry1,
                rx2,
                ry2,
                sx,
                sy,
                x1,
                x2,
                yy,
                rs1,
                rs2,
                sgn,
                g1,
                g2,
                t1,
                t2,
                x1i,
                x2i,
                yyi);

            

            // Track last panel for TE treatment
            lastJo = jo;
            lastJp = jp;

            // If this is the closing TE panel (jo == n-1), skip source and vortex
            // main-panel contribution but proceed to TE treatment (label 11 in Fortran)
            if (jo == n - 1)
            {
                // Skip the regular panel contributions, go directly to TE panel code
                break;
            }

            // ---- Source contribution (if requested) ----
            if (includeSourceTerms)
            {
                ComputeSourceContribution(
                    traceScope,
                    io,
                    jo, jp, jm, jq, n,
                    x1, x2, yy, sgn, g1, g2, t1, t2, rs1, rs2,
                    x1i, x2i, yyi, apan,
                    dsio, panel, state, quarterOverPi, "Double",
                    ref psi, ref psiNi);
            }

            // ---- Vortex panel contribution to PSI ----
            ComputeVortexContribution(
                traceScope,
                io,
                jo, jp, n,
                x1, x2, yy, g1, g2, t1, t2, rs1, rs2,
                x1i, x2i, yyi,
                panel, state, quarterOverPi, "Double",
                ref psi, ref psiNi);
        }

        // ---- TE panel treatment (labels 11/12 in Fortran) ----
        // After the main loop, compute TE panel contribution from the last panel's geometry
        if (!skipTEPanel)
        {
            ComputeTEPanelContribution(
                lastJo, lastJp, n,
                x1, x2, yy, g1, g2, t1, t2,
                x1i, x2i, yyi, apan,
                scs, sds,
                panel, state, halfOverPi,
                ref psi, ref psiNi);
        }

        // ---- Freestream terms ----
        psi += freestreamSpeed * (cosa * fieldY - sina * fieldX);
        psiNi += freestreamSpeed * (cosa * fieldNormalY - sina * fieldNormalX);

        TracePsilinResult(traceScope, io, psi, psiNi, "Double");

        return (psi, psiNi);
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN legacy REAL replay.
    // Difference from legacy: The managed code reproduces the single-precision path explicitly with floats and selected fused operations instead of relying on the runtime to approximate the original REAL build.
    // Decision: Keep the dedicated parity kernel because this is the exact PSILIN replay path used for binary comparisons.
    private static (double psi, double psiNormalDerivative) ComputeInfluenceAtLegacyPrecision(
        int fieldNodeIndex,
        double fieldX,
        double fieldY,
        double fieldNormalX,
        double fieldNormalY,
        bool includeSourceTerms,
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        int n = panel.NodeCount;
        const float qopi = 1f / (4f * MathF.PI);
        const float hopi = 1f / (2f * MathF.PI);

        TracePsilinField(
            traceScope,
            fieldNodeIndex + 1,
            fieldX,
            fieldY,
            fieldNormalX,
            fieldNormalY,
            computeGeometricSensitivities: false,
            includeSourceTerms,
            precision: "Single");

        float seps = (float)((panel.ArcLength[n - 1] - panel.ArcLength[0]) * 1.0e-5);
        int io = fieldNodeIndex + 1;

        

        float fieldXf = (float)fieldX;
        float fieldYf = (float)fieldY;
        float fieldNormalXf = (float)fieldNormalX;
        float fieldNormalYf = (float)fieldNormalY;
        float cosa = (float)Math.Cos(angleOfAttackRadians);
        float sina = (float)Math.Sin(angleOfAttackRadians);
        

        var dzdg = new float[n];
        var dzdm = new float[n];
        var dqdg = new float[n];
        var dqdm = new float[n];

        float psi = 0f;
        float psiNi = 0f;

        float scs;
        float sds;
        if (state.IsSharpTrailingEdge)
        {
            scs = 1f;
            sds = 0f;
        }
        else
        {
            // Fortran: SCS = ANTE/DSTE in REAL (float) precision.
            // C# previously did the division in double then cast to float,
            // which can produce 1 ULP differences. Cast operands first to
            // ensure float-precision division.
            float anteF = (float)state.TrailingEdgeAngleNormal;
            float asteF = (float)state.TrailingEdgeAngleStreamwise;
            float dsteF = (float)state.TrailingEdgeGap;
            scs = anteF / dsteF;
            sds = asteF / dsteF;
        }

        float x1 = 0f;
        float x2 = 0f;
        float yy = 0f;
        float g1 = 0f;
        float g2 = 0f;
        float t1 = 0f;
        float t2 = 0f;
        float rs1 = 0f;
        float rs2 = 0f;
        float x1i = 0f;
        float x2i = 0f;
        float yyi = 0f;
        float apan = 0f;

        int lastJo = n - 1;
        int lastJp = 0;
        bool skipTEPanel = false;

        for (int jo = 0; jo < n; jo++)
        {
            int jp = jo + 1;
            int jm = jo - 1;
            int jq = jp + 1;

            if (jo == 0)
            {
                jm = jo;
            }
            else if (jo == n - 2)
            {
                jq = jp;
            }
            else if (jo == n - 1)
            {
                jp = 0;
                float xJoTe = (float)panel.X[jo];
                float yJoTe = (float)panel.Y[jo];
                float xJpTe = (float)panel.X[jp];
                float yJpTe = (float)panel.Y[jp];
                float dxte = xJoTe - xJpTe;
                float dyte = yJoTe - yJpTe;
                if (((dxte * dxte) + (dyte * dyte)) < (seps * seps))
                {
                    skipTEPanel = true;
                    break;
                }
            }

            // Legacy parity mode must form panel deltas from already-rounded single-precision
            // coordinates. Subtracting the doubles first shifts DSO/DSM/DSP by one ULP.
            float xJo = (float)panel.X[jo];
            float yJo = (float)panel.Y[jo];
            float xJp = (float)panel.X[jp];
            float yJp = (float)panel.Y[jp];
            float dxPanel = xJo - xJp;
            float dyPanel = yJo - yJp;
            // The optimized Fortran reference build contracts the off-body PSILIN
            // panel-length square-sum, so the parity path must keep the fused
            // `dx*dx + dy*dy` replay here even though the source tree is written
            // as a plain REAL sum.
            float dsoSquared = LegacyPrecisionMath.FusedMultiplyAdd(dxPanel, dxPanel, dyPanel * dyPanel);
            float dso = MathF.Sqrt(dsoSquared);
            if (dso == 0f)
            {
                continue;
            }

            float dsio = 1f / dso;
            apan = (float)panel.PanelAngle[jo];

            float rx1 = fieldXf - xJo;
            float ry1 = fieldYf - yJo;
            float rx2 = fieldXf - xJp;
            float ry2 = fieldYf - yJp;

            float sx = (xJp - xJo) * dsio;
            float sy = (yJp - yJo) * dsio;

            // The optimized Fortran wake/off-body build contracts these local
            // coordinate sums instead of keeping separately rounded products.
            x1 = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.FusedMultiplyAdd(sx, rx1, sy * ry1)
                : LegacyPrecisionMath.SumOfProducts(sx, rx1, sy, ry1);
            x2 = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.ContractedMultiplySubtract(-sx, rx2, sy * ry2)
                : LegacyPrecisionMath.SumOfProducts(sx, rx2, sy, ry2);
            yy = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.FusedMultiplyAdd(sx, ry1, -(sy * rx1))
                : LegacyPrecisionMath.FusedMultiplyAdd(sx, ry1, -(sy * rx1));

            rs1 = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.ContractedMultiplySubtract(-rx1, rx1, ry1 * ry1)
                : LegacyPrecisionMath.FusedMultiplyAdd(rx1, rx1, ry1 * ry1);
            rs2 = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.ContractedMultiplySubtract(-rx2, rx2, ry2 * ry2)
                : LegacyPrecisionMath.FusedMultiplyAdd(rx2, rx2, ry2 * ry2);

            float sgn = (io >= 1 && io <= n) ? 1f : (yy >= 0f ? 1f : -1f);

            if (fieldNodeIndex != jo && rs1 > 0f)
            {
                g1 = LegacyLibm.Log(rs1);
                t1 = LegacyLibm.Atan2(sgn * x1, sgn * yy) + ((0.5f - (0.5f * sgn)) * MathF.PI);
            }
            else
            {
                g1 = 0f;
                t1 = 0f;
            }

            if (fieldNodeIndex != jp && rs2 > 0f)
            {
                g2 = LegacyLibm.Log(rs2);
                t2 = LegacyLibm.Atan2(sgn * x2, sgn * yy) + ((0.5f - (0.5f * sgn)) * MathF.PI);
            }
            else
            {
                g2 = 0f;
                t2 = 0f;
            }

            x1i = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.ContractedMultiplySubtract(-sx, fieldNormalXf, sy * fieldNormalYf)
                : LegacyPrecisionMath.SumOfProducts(sx, fieldNormalXf, sy, fieldNormalYf);
            x2i = x1i;
            yyi = (io >= 1 && io <= n)
                ? LegacyPrecisionMath.FusedMultiplyAdd(sx, fieldNormalYf, -(sy * fieldNormalXf))
                : LegacyPrecisionMath.SeparateMultiplySubtract(sy, fieldNormalXf, sx * fieldNormalYf);

            TracePsilinPanel(
                traceScope,
                fieldIndex: io,
                panelIndex: jo + 1,
                jm: jm + 1,
                jo: jo + 1,
                jp: jp + 1,
                jq: jq + 1,
                computeGeometricSensitivities: false,
                includeSourceTerms,
                precision: "Single",
                panelXJo: xJo,
                panelYJo: yJo,
                panelXJp: xJp,
                panelYJp: yJp,
                panelDx: dxPanel,
                panelDy: dyPanel,
                dso,
                dsio,
                apan,
                rx1,
                ry1,
                rx2,
                ry2,
                sx,
                sy,
                x1,
                x2,
                yy,
                rs1,
                rs2,
                sgn,
                g1,
                g2,
                t1,
                t2,
                x1i,
                x2i,
                yyi);

            

            lastJo = jo;
            lastJp = jp;

            if (jo == n - 1)
            {
                break;
            }

            if (includeSourceTerms)
            {
                float x0 = 0.5f * (x1 + x2);
                // The corrected O2 standalone PSILIN driver and the authoritative
                // wake-point traces both keep the midpoint radius-square on the
                // contracted native REAL path. The earlier off-body split came from
                // the stale pre-O2 micro-driver build and is no longer authoritative.
                float rs0 = LegacyPrecisionMath.FusedMultiplyAdd(x0, x0, yy * yy);
                float g0 = LegacyLibm.Log(rs0);
                float t0 = LegacyLibm.Atan2(sgn * x0, sgn * yy) + ((0.5f - (0.5f * sgn)) * MathF.PI);

                {
                    float dxInv = 1f / (x1 - x0);
                    float psumTerm1 = x0 * (t0 - apan);
                    float psumTerm2 = x1 * (t1 - apan);
                    float psumTerm3 = 0.5f * yy * (g1 - g0);
                    float psumAccum = psumTerm1 - psumTerm2;
                    float psum = psumAccum + psumTerm3;
                    float pdifTerm1 = (x1 + x0) * psum;
                    float pdifTerm2 = rs1 * (t1 - apan);
                    float pdifTerm3 = rs0 * (t0 - apan);
                    float pdifTerm4 = (x0 - x1) * yy;
                    float pdifBase = pdifTerm1 + pdifTerm2;
                    float pdifAccum = pdifBase - pdifTerm3;
                    float pdifNumerator = pdifAccum + pdifTerm4;
                    float pdif = pdifNumerator * dxInv;
                    TracePsilinSourceHalfTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 1,
                        precision: "Single",
                        x0,
                        psumTerm1,
                        psumTerm2,
                        psumTerm3,
                        psumAccum,
                        psum,
                        pdifTerm1,
                        pdifTerm2,
                        pdifTerm3,
                        pdifTerm4,
                        pdifBase,
                        pdifAccum,
                        pdifNumerator,
                        pdif);
                    float psx1 = -(t1 - apan);
                    float psx0 = t0 - apan;
                    float psyy = 0.5f * (g1 - g0);
                    // The standalone PSILIN micro-driver shows the half-1 PDX numerators
                    // follow the visible Fortran source tree here rather than a contracted
                    // FMA replay. Keep the product terms and left-associated +/- tails explicit.
                    float pdx1Term1 = (x1 + x0) * psx1;
                    float pdx1Term2 = (2f * x1) * (t1 - apan);
                    float pdx1Numerator = (pdx1Term1 + psum) + pdx1Term2;
                    pdx1Numerator -= pdif;
                    float pdx1 = pdx1Numerator * dxInv;
                    float pdx0Term1 = (x1 + x0) * psx0;
                    float pdx0Term2 = (-2f * x0) * (t0 - apan);
                    float pdx0Numerator = (pdx0Term1 + psum) + pdx0Term2;
                    pdx0Numerator += pdif;
                    float pdx0 = pdx0Numerator * dxInv;
                    // The standalone PSILIN micro-driver also shows the half-1 PDYY path
                    // follows the visible REAL source tree instead of a contracted pair of
                    // FMAs: `(X1+X0)*PSYY + 2*(X0-X1 + YY*(T1-T0))`.
                    float pdyyTerm1 = (x1 + x0) * psyy;
                    float pdyyTailLinear;
                    float pdyyTailAngular;
                    float pdyyTerm2;
                    if (io >= 1 && io <= n)
                    {
                        // The focused 12-panel surface-source traces show the native REAL
                        // build materializes the doubled linear tail as two separately
                        // rounded products before the final add, not as `2*(dx + yy*dt)`.
                        pdyyTailLinear = LegacyPrecisionMath.RoundBarrier(2f * (x0 - x1));
                        pdyyTailAngular = LegacyPrecisionMath.RoundBarrier((2f * yy) * (t1 - t0));
                        pdyyTerm2 = LegacyPrecisionMath.AddRounded(pdyyTailLinear, pdyyTailAngular);
                    }
                    else
                    {
                        // Off-body (wake) path: also barrier intermediate values to
                        // prevent JIT from keeping them in extended precision.
                        pdyyTailLinear = LegacyPrecisionMath.RoundBarrier(2f * (x0 - x1));
                        pdyyTailAngular = LegacyPrecisionMath.RoundBarrier((2f * yy) * (t1 - t0));
                        pdyyTerm2 = LegacyPrecisionMath.AddRounded(pdyyTailLinear, pdyyTailAngular);
                    }
                    float pdyy;
                    if (io >= 1 && io <= n)
                    {
                        // The traced surface-source write does not consume the separately
                        // rounded PDYYTERM2/PDYYNUMERATOR variables directly. Classic XFoil
                        // Fortran: PDYY = ((X1+X0)*PSYY + 2.0*(X0-X1+YY*(T1-T0))) * DXINV
                        // All REAL (float) operations.
                        float pdyySourceTerm2 = 2.0f * ((x0 - x1) + (yy * (t1 - t0)));
                        pdyy = (pdyyTerm1 + pdyySourceTerm2) * dxInv;
                    }
                    else
                    {
                        pdyy = (pdyyTerm1 + pdyyTerm2) * dxInv;
                    }
                    float pdyyWriteDt = t1 - t0;
                    float pdyyWriteDiff = x0 - x1;
                    float pdyyWriteInner = LegacyPrecisionMath.FusedMultiplyAdd(yy, pdyyWriteDt, pdyyWriteDiff);
                    float pdyyWriteHead = (x1 + x0) * psyy;
                    float pdyyWriteTail = 2f * pdyyWriteInner;
                    float pdyyWriteSum = pdyyWriteHead + pdyyWriteTail;
                    float pdyyWriteValue = pdyyWriteSum * dxInv;
                    pdyy = pdyyWriteValue;
                    float xJm = (float)panel.X[jm];
                    float yJm = (float)panel.Y[jm];
                    float dxSm = xJp - xJm;
                    float dySm = yJp - yJm;
                    float dsmSquared = (io >= 1 && io <= n)
                        ? LegacyPrecisionMath.FusedMultiplyAdd(dxSm, dxSm, dySm * dySm)
                        : LegacyPrecisionMath.SeparateSumOfProducts(dxSm, dxSm, dySm, dySm);
                    float dsm = MathF.Sqrt(dsmSquared);
                    float dsim = 1f / dsm;

                    // Keep the two source-strength products separated so the
                    // half-panel SSUM/SDIF recurrence cannot contract into an
                    // FMA-style `a*b +/- c*d` update.
                    float sourceJoTerm = ((float)state.SourceStrength[jp] - (float)state.SourceStrength[jo]) * dsio;
                    float sourceJmTerm = ((float)state.SourceStrength[jp] - (float)state.SourceStrength[jm]) * dsim;
                    float ssum = sourceJoTerm + sourceJmTerm;
                    float sdif = sourceJoTerm - sourceJmTerm;
                    float dzJm = qopi * ((-psum * dsim) + (pdif * dsim));
                    float dzJoUnscaled = ((-psum) * dsio) - (pdif * dsio);
                    float dzJo = qopi * dzJoUnscaled;
                    float dzJpUnscaled = (psum * (dsio + dsim)) + (pdif * (dsio - dsim));
                    float dzJp = qopi * dzJpUnscaled;

                    TracePsilinSourceDzTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 1,
                        precision: "Single",
                        dzJmTerm1: (-psum) * dsim,
                        dzJmTerm2: pdif * dsim,
                        dzJmInner: ((-psum) * dsim) + (pdif * dsim),
                        dzJoTerm1: (-psum) * dsio,
                        dzJoTerm2: (-pdif) * dsio,
                        dzJoInner: dzJoUnscaled,
                        dzJpTerm1: psum * (dsio + dsim),
                        dzJpTerm2: pdif * (dsio - dsim),
                        dzJpInner: dzJpUnscaled,
                        dzJqTerm1: 0f,
                        dzJqTerm2: 0f,
                        dzJqInner: 0f);

                    float psiTerm1 = psum * ssum;
                    float psiTerm2 = pdif * sdif;
                    float psiBeforeSourceHalf1 = psi;
                    float psiNiBeforeSourceHalf1 = psiNi;
                    float psiInner = psiTerm1 + psiTerm2;
                    psi = psi + (qopi * psiInner);
                    dzdm[jm] += dzJm;
                    dzdm[jo] += dzJo;
                    dzdm[jp] += dzJp;

                    float psniTerm1 = psx1 * x1i;
                    float psniTerm2 = (psx0 * (x1i + x2i)) * 0.5f;
                    float psniTerm3 = psyy * yyi;
                    float psni = (psniTerm1 + psniTerm2) + psniTerm3;
                    float pdniTerm1 = pdx1 * x1i;
                    float pdniTerm2 = (pdx0 * (x1i + x2i)) * 0.5f;
                    float pdniTerm3 = pdyy * yyi;
                    float pdni = (pdniTerm1 + pdniTerm2) + pdniTerm3;
                    float psiNiTerm1 = psni * ssum;
                    float psiNiTerm2 = pdni * sdif;
                    float psiNiInner = psiNiTerm1 + psiNiTerm2;
                    psiNi = psiNi + (qopi * psiNiInner);
                    TracePsilinAccumState(
                        traceScope,
                        io,
                        "source_half1",
                        jo + 1,
                        jp + 1,
                        psiBeforeSourceHalf1,
                        psiNiBeforeSourceHalf1,
                        psi,
                        psiNi,
                        "Single");
                    // The half-1 dQ/dm tail keeps the two products separately rounded in the
                    // traced PSILIN path; forcing an FMA here shifts the first live mismatch.
                    float dqJm = qopi * (((-psni) * dsim) + (pdni * dsim));
                    // Keep the two JO products materialized separately before the subtraction;
                    // otherwise the JIT can fold this back toward the wrong rounded inner sum.
                    float dqJoLeft = (-psni) * dsio;
                    float dqJoRight = pdni * dsio;
                    float dqJoInner = dqJoLeft - dqJoRight;
                    float dqJo = qopi * dqJoInner;
                    // The half-1 JP derivative follows the same separately rounded sum-of-products
                    // pattern as the classic source trace; an explicit FMA is one ULP too large.
                    float dqJpLeft = psni * (dsio + dsim);
                    float dqJpRight = pdni * (dsio - dsim);
                    float dqJp = qopi * (dqJpLeft + dqJpRight);

                    TracePsilinSourceDqTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 1,
                        precision: "Single",
                        dqJmTerm1: (-psni) * dsim,
                        dqJmTerm2: pdni * dsim,
                        dqJmInner: ((-psni) * dsim) + (pdni * dsim),
                        dqJoTerm1: dqJoLeft,
                        dqJoTerm2: -dqJoRight,
                        dqJoInner,
                        dqJpTerm1: dqJpLeft,
                        dqJpTerm2: dqJpRight,
                        dqJpInner: dqJpLeft + dqJpRight,
                        dqJqTerm1: 0f,
                        dqJqTerm2: 0f,
                        dqJqInner: 0f);
                    dqdm[jm] += dqJm;
                    dqdm[jo] += dqJo;
                    dqdm[jp] += dqJp;

                    TracePsilinSourceSegment(
                        traceScope,
                        io,
                        jo + 1,
                        half: 1,
                        jm: jm + 1,
                        jo: jo + 1,
                        jp: jp + 1,
                        jq: jq + 1,
                        precision: "Single",
                        x0,
                        x1,
                        x2,
                        yy,
                        panelAngle: apan,
                        x1i,
                        x2i,
                        yyi,
                        rs0,
                        rs1,
                        rs2,
                        g0,
                        g1,
                        g2,
                        t0,
                        t1,
                        t2,
                        dso,
                        dsio,
                        dsm,
                        dsim,
                        dsp: 0f,
                        dsip: 0f,
                        dxInv,
                        sourceTermLeft: sourceJoTerm,
                        sourceTermRight: sourceJmTerm,
                        ssum,
                        sdif,
                        psum,
                        pdif,
                        psx0,
                        psx1,
                        psx2: 0f,
                        psyy,
                        pdx0Term1,
                        pdx0Term2,
                        pdx0Numerator,
                        pdx0,
                        pdx1Term1,
                        pdx1Term2,
                        pdx1Numerator,
                        pdx1,
                        pdx2Term1: 0f,
                        pdx2Term2: 0f,
                        pdx2Numerator: 0f,
                        pdx2: 0f,
                        pdyyTerm1,
                        pdyyTailLinear,
                        pdyyTailAngular,
                        pdyyTerm2,
                        pdyyNumerator: pdyyTerm1 + pdyyTerm2,
                        pdyy,
                        psniTerm1,
                        psniTerm2,
                        psniTerm3,
                        psni,
                        pdniTerm1,
                        pdniTerm2,
                        pdniTerm3,
                        pdni,
                        dzJm,
                        dzJo,
                        dzJp,
                        dzJq: 0f,
                        dqJm,
                        dqJo,
                        dqJp,
                        dqJq: 0f);
                    TracePsilinSourcePdyyWrite(
                        traceScope,
                        io,
                        jo + 1,
                        half: 1,
                        precision: "Single",
                        x0,
                        xEdge: x1,
                        yy,
                        t0,
                        tEdge: t1,
                        psyy,
                        dxInv,
                        pdyyWriteDt,
                        pdyyWriteInner,
                        pdyyWriteHead,
                        pdyyWriteTail,
                        pdyyWriteSum,
                        pdyyWriteValue,
                        pdyyTerm1,
                        pdyyTerm2,
                        pdyyNumerator: pdyyTerm1 + pdyyTerm2,
                        pdyy);
                }

                {
                    float dxInv = 1f / (x0 - x2);
                    float psumTerm1 = x2 * (t2 - apan);
                    float psumTerm2 = x0 * (t0 - apan);
                    float psumTerm3 = 0.5f * yy * (g0 - g2);
                    float psumAccum = psumTerm1 - psumTerm2;
                    float psum = psumAccum + psumTerm3;
                    float pdifTerm1 = (x0 + x2) * psum;
                    float pdifTerm2 = rs0 * (t0 - apan);
                    float pdifTerm3 = rs2 * (t2 - apan);
                    float pdifTerm4 = (x2 - x0) * yy;
                    // Legacy block: xpanel.f PSILIN half-2 source PDIF numerator.
                    // Difference from legacy: The prior managed code recombined the traced terms into one expression; parity mode needs the explicit staged accumulation order that the REAL build already logs.
                    // Decision: Keep the staged numerator so the single-precision half-panel source path cannot drift from the reference rounding sequence.
                    float pdifBase = pdifTerm1 + pdifTerm2;
                    float pdifAccum = pdifBase - pdifTerm3;
                    float pdifNumerator = pdifAccum + pdifTerm4;
                    float pdif = pdifNumerator * dxInv;
                    TracePsilinSourceHalfTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 2,
                        precision: "Single",
                        x0,
                        psumTerm1,
                        psumTerm2,
                        psumTerm3,
                        psumAccum,
                        psum,
                        pdifTerm1,
                        pdifTerm2,
                        pdifTerm3,
                        pdifTerm4,
                        pdifBase,
                        pdifAccum,
                        pdifNumerator,
                        pdif);
                    float psx0 = -(t0 - apan);
                    float psx2 = t2 - apan;
                    float psyy = 0.5f * (g0 - g2);
                    // The standalone PSILIN micro-driver shows that the half-2 PDX numerators
                    // follow the visible Fortran source tree here rather than a contracted FMA
                    // replay. Keep the two product terms and the left-associated +/- tail explicit.
                    float pdx0Term1 = (x0 + x2) * psx0;
                    float pdx0Term2 = (2f * x0) * (t0 - apan);
                    float pdx0Numerator = (pdx0Term1 + psum) + pdx0Term2;
                    pdx0Numerator -= pdif;
                    float pdx0 = pdx0Numerator * dxInv;
                    float pdx2Term1 = (x0 + x2) * psx2;
                    float pdx2Term2 = (-2f * x2) * (t2 - apan);
                    float pdx2Numerator = (pdx2Term1 + psum) + pdx2Term2;
                    pdx2Numerator += pdif;
                    float pdx2 = pdx2Numerator * dxInv;
                    // The standalone PSILIN micro-driver shows the half-2 PDYY path also
                    // follows the visible REAL source tree instead of a contracted pair of
                    // FMAs: `(X0+X2)*PSYY + 2*(X2-X0 + YY*(T0-T2))`.
                    float pdyyTerm1 = (x0 + x2) * psyy;
                    float pdyyTailLinear = 2f * (x2 - x0);
                    float pdyyTailAngular = (2f * yy) * (t0 - t2);
                    float pdyyTerm2 = LegacyPrecisionMath.AddRounded(pdyyTailLinear, pdyyTailAngular);
                    float pdyyNumerator = pdyyTerm1 + pdyyTerm2;
                    float pdyy = pdyyNumerator * dxInv;
                    float pdyyWriteDt = t0 - t2;
                    float pdyyWriteDiff = x2 - x0;
                    float pdyyWriteInner = LegacyPrecisionMath.FusedMultiplyAdd(yy, pdyyWriteDt, pdyyWriteDiff);
                    float pdyyWriteHead = (x0 + x2) * psyy;
                    float pdyyWriteTail = 2f * pdyyWriteInner;
                    float pdyyWriteSum = pdyyWriteHead + pdyyWriteTail;
                    float pdyyWriteValue = pdyyWriteSum * dxInv;
                    pdyy = pdyyWriteValue;
                    float xJq = (float)panel.X[jq];
                    float yJq = (float)panel.Y[jq];
                    float dxSp = xJq - xJo;
                    float dySp = yJq - yJo;
                    float dspSquared = (io >= 1 && io <= n)
                        ? LegacyPrecisionMath.FusedMultiplyAdd(dxSp, dxSp, dySp * dySp)
                        : LegacyPrecisionMath.SeparateSumOfProducts(dxSp, dxSp, dySp, dySp);
                    float dsp = MathF.Sqrt(dspSquared);
                    float dsip = 1f / dsp;

                    // Keep the two source-strength products separated so the
                    // half-panel SSUM/SDIF recurrence cannot contract into an
                    // FMA-style `a*b +/- c*d` update.
                    float sourceQTerm = ((float)state.SourceStrength[jq] - (float)state.SourceStrength[jo]) * dsip;
                    float sourcePTerm = ((float)state.SourceStrength[jp] - (float)state.SourceStrength[jo]) * dsio;
                    float ssum = sourceQTerm + sourcePTerm;
                    float sdif = sourceQTerm - sourcePTerm;
                    // The traced half-2 JO source update also stays on the split product path;
                    // the fused inner sum runs one ULP high against classic XFoil here.
                    float dzJoInner = ((-psum) * (dsip + dsio)) - (pdif * (dsip - dsio));
                    float dzJo = qopi * dzJoInner;
                    float dzJpUnscaled = (psum * dsio) - (pdif * dsio);
                    float dzJp = qopi * dzJpUnscaled;
                    float dzJqUnscaled = (psum * dsip) + (pdif * dsip);
                    float dzJq = qopi * dzJqUnscaled;

                    TracePsilinSourceDzTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 2,
                        precision: "Single",
                        dzJmTerm1: 0f,
                        dzJmTerm2: 0f,
                        dzJmInner: 0f,
                        dzJoTerm1: (-psum) * (dsip + dsio),
                        dzJoTerm2: (-pdif) * (dsip - dsio),
                        dzJoInner,
                        dzJpTerm1: psum * dsio,
                        dzJpTerm2: (-pdif) * dsio,
                        dzJpInner: dzJpUnscaled,
                        dzJqTerm1: psum * dsip,
                        dzJqTerm2: pdif * dsip,
                        dzJqInner: dzJqUnscaled);

                    float psiTerm1 = psum * ssum;
                    float psiTerm2 = pdif * sdif;
                    float psiBeforeSourceHalf2 = psi;
                    float psiNiBeforeSourceHalf2 = psiNi;
                    float psiInner = psiTerm1 + psiTerm2;
                    psi = psi + (qopi * psiInner);
                    dzdm[jo] += dzJo;
                    dzdm[jp] += dzJp;
                    if (jq < n)
                    {
                        dzdm[jq] += dzJq;
                    }

                    float psniTerm1 = (psx0 * (x1i + x2i)) * 0.5f;
                    float psniTerm2 = psx2 * x2i;
                    float psniTerm3 = psyy * yyi;
                    float psni = (psniTerm1 + psniTerm2) + psniTerm3;
                    float pdniTerm1 = (pdx0 * (x1i + x2i)) * 0.5f;
                    float pdniTerm2 = pdx2 * x2i;
                    float pdniTerm3 = pdyy * yyi;
                    float pdni = (pdniTerm1 + pdniTerm2) + pdniTerm3;
                    float psiNiTerm1 = psni * ssum;
                    float psiNiTerm2 = pdni * sdif;
                    float psiNiInner = psiNiTerm1 + psiNiTerm2;
                    psiNi = psiNi + (qopi * psiNiInner);
                    TracePsilinAccumState(
                        traceScope,
                        io,
                        "source_half2",
                        jo + 1,
                        jp + 1,
                        psiBeforeSourceHalf2,
                        psiNiBeforeSourceHalf2,
                        psi,
                        psiNi,
                        "Single");
                    // The half-2 JO derivative also needs the classic staged product difference.
                    float dqJoLeft = (-psni) * (dsip + dsio);
                    float dqJoRight = pdni * (dsip - dsio);
                    float dqJo = qopi * (dqJoLeft - dqJoRight);
                    // The traced half-2 JP update also keeps the product difference split
                    // before QOPI; the fused form lands one ULP away on the reference case.
                    float dqJpLeft = psni * dsio;
                    float dqJpRight = pdni * dsio;
                    float dqJpInner = dqJpLeft - dqJpRight;
                    float dqJp = qopi * dqJpInner;
                    // The traced JQ tail keeps both DSIP products separately rounded too.
                    float dqJqLeft = psni * dsip;
                    float dqJqRight = pdni * dsip;
                    float dqJq = qopi * (dqJqLeft + dqJqRight);

                    TracePsilinSourceDqTerms(
                        traceScope,
                        io,
                        jo + 1,
                        half: 2,
                        precision: "Single",
                        dqJmTerm1: 0f,
                        dqJmTerm2: 0f,
                        dqJmInner: 0f,
                        dqJoTerm1: (-psni) * (dsip + dsio),
                        dqJoTerm2: (-pdni) * (dsip - dsio),
                        dqJoInner: ((-psni) * (dsip + dsio)) - (pdni * (dsip - dsio)),
                        dqJpTerm1: dqJpLeft,
                        dqJpTerm2: -dqJpRight,
                        dqJpInner,
                        dqJqTerm1: dqJqLeft,
                        dqJqTerm2: dqJqRight,
                        dqJqInner: dqJqLeft + dqJqRight);
                    dqdm[jo] += dqJo;
                    dqdm[jp] += dqJp;
                    if (jq < n)
                    {
                        dqdm[jq] += dqJq;
                    }

                    TracePsilinSourceSegment(
                        traceScope,
                        io,
                        jo + 1,
                        half: 2,
                        jm: jm + 1,
                        jo: jo + 1,
                        jp: jp + 1,
                        jq: jq + 1,
                        precision: "Single",
                        x0,
                        x1,
                        x2,
                        yy,
                        panelAngle: apan,
                        x1i,
                        x2i,
                        yyi,
                        rs0,
                        rs1,
                        rs2,
                        g0,
                        g1,
                        g2,
                        t0,
                        t1,
                        t2,
                        dso,
                        dsio,
                        dsm: 0f,
                        dsim: 0f,
                        dsp,
                        dsip,
                        dxInv,
                        sourceTermLeft: sourceQTerm,
                        sourceTermRight: sourcePTerm,
                        ssum,
                        sdif,
                        psum,
                        pdif,
                        psx0,
                        psx1: 0f,
                        psx2,
                        psyy,
                        pdx0Term1,
                        pdx0Term2,
                        pdx0Numerator,
                        pdx0,
                        pdx1Term1: 0f,
                        pdx1Term2: 0f,
                        pdx1Numerator: 0f,
                        pdx1: 0f,
                        pdx2Term1,
                        pdx2Term2,
                        pdx2Numerator,
                        pdx2,
                        pdyyTerm1,
                        pdyyTailLinear,
                        pdyyTailAngular,
                        pdyyTerm2,
                        pdyyNumerator,
                        pdyy,
                        psniTerm1,
                        psniTerm2,
                        psniTerm3,
                        psni,
                        pdniTerm1,
                        pdniTerm2,
                        pdniTerm3,
                        pdni,
                        dzJm: 0f,
                        dzJo,
                        dzJp,
                        dzJq: jq < n ? dzJq : 0f,
                        dqJm: 0f,
                        dqJo,
                        dqJp,
                        dqJq: jq < n ? dqJq : 0f);
                    TracePsilinSourcePdyyWrite(
                        traceScope,
                        io,
                        jo + 1,
                        half: 2,
                        precision: "Single",
                        x0,
                        xEdge: x2,
                        yy,
                        t0,
                        tEdge: t2,
                        psyy,
                        dxInv,
                        pdyyWriteDt,
                        pdyyWriteInner,
                        pdyyWriteHead,
                        pdyyWriteTail,
                        pdyyWriteSum,
                        pdyyWriteValue,
                        pdyyTerm1,
                        pdyyTerm2,
                        pdyyNumerator,
                        pdyy);
                }
            }

            {
                float dxInv = 1f / (x1 - x2);
                // gfortran contracts the last term YY*(T1-T2) into FMA.
                float psis = LegacyPrecisionMath.Fma(yy, t1 - t2,
                    (0.5f * x1 * g1) - (0.5f * x2 * g2) + x2 - x1);
                float psisTerm1 = 0.5f * x1 * g1;
                float psisTerm2 = -0.5f * x2 * g2;
                float psisTerm3 = x2 - x1;
                float psisTerm4 = yy * (t1 - t2);
                float psidTerm1 = (x1 + x2) * psis;
                float psidTerm2 = rs2 * g2;
                float psidTerm3 = rs1 * g1;
                float psidTerm4 = x1 * x1;
                float psidTerm5 = x2 * x2;
                // Fortran evaluates left-to-right: ((T2-T3)+T4)-T5
                // Must match evaluation order exactly for float parity.
                float psidHalfAccum1 = psidTerm2 - psidTerm3;
                float psidHalfAccum2 = psidHalfAccum1 + psidTerm4;
                float psidHalfInner = psidHalfAccum2 - psidTerm5;
                float psidHalfTerm = 0.5f * psidHalfInner;
                float psidBase = (x1 + x2) * psis + psidHalfTerm;
                float psid = psidBase * dxInv;
                float psx1 = 0.5f * g1;
                float psx2 = -0.5f * g2;
                float psyy = t1 - t2;
                float pdxSum = x1 + x2;
                float pdx1Mul = pdxSum * psx1;
                float pdx1PanelTerm = x1 * g1;
                // The optimized off-body vortex branch also contracts the head
                // `(X1+X2)*PSX1 + PSIS` before the plain `- X1*G1 - PSID` tail.
                float pdx1Accum1 = (pdxSum * psx1) + psis;
                float pdx1Accum2 = pdx1Accum1 - pdx1PanelTerm;
                float pdx1Numerator = pdx1Accum2 - psid;
                float pdx1Head = LegacyPrecisionMath.FusedMultiplyAdd(pdxSum, psx1, psis);
                // gfortran contracts: FMA(-X1, G1, pdx1Head) for the -x1*g1 step
                float pdx1 = (LegacyPrecisionMath.Fma(-x1, g1, pdx1Head) - psid) * dxInv;
                float pdx2Mul = pdxSum * psx2;
                float pdx2PanelTerm = x2 * g2;
                float pdx2Accum1 = pdx2Mul + psis;
                float pdx2Accum2 = pdx2Accum1 + pdx2PanelTerm;
                float pdx2Numerator = pdx2Accum2 + psid;
                float pdx2Head = LegacyPrecisionMath.FusedMultiplyAdd(pdxSum, psx2, psis);
                // gfortran contracts: FMA(X2, G2, pdx2Head) for the +x2*g2 step
                float pdx2 = (LegacyPrecisionMath.Fma(x2, g2, pdx2Head) + psid) * dxInv;
                float pdyyTerm1 = (x1 + x2) * psyy;
                float pdyyTerm2 = yy * (g1 - g2);
                float pdyy = LegacyPrecisionMath.ContractedMultiplySubtract(-pdxSum, psyy, -pdyyTerm2) * dxInv;

                float gammaJp = (float)state.VortexStrength[jp];
                float gammaJo = (float)state.VortexStrength[jo];
                // Fortran: GSUM = GAM(JP) + GAM(JO) in REAL (float)
                float gsum = LegacyPrecisionMath.RoundBarrier(gammaJp + gammaJo);
                float gdif = LegacyPrecisionMath.RoundBarrier(gammaJp - gammaJo);

                float psiInner = LegacyPrecisionMath.Fma(psis, gsum, psid * gdif);
                float psiDelta = qopi * psiInner;
                float psiBefore = psi;
                psi += psiDelta;
                float dzJo = qopi * (psis - psid);
                float dzJp = qopi * (psis + psid);
                dzdg[jo] += dzJo;
                dzdg[jp] += dzJp;

                // GDB: trace DZDG[0] accumulation at field node 33 (1-indexed)
                

                

                // Fortran PSILIN vortex line 563:
                // PSNI = FMAF_REAL(PSYY, YYI, FMAF_REAL(PSX1, X1I, PSX2*X2I))
                // QVOR = QOPI*(GSUM*PSNI + GDIF*PDNI)
                float psniTerm1 = psx1 * x1i;
                float psniTerm2 = psx2 * x2i;
                float psniTerm3 = psyy * yyi;
                float psni = LegacyPrecisionMath.Fma(psyy, yyi,
                    LegacyPrecisionMath.Fma(psx1, x1i, LegacyPrecisionMath.RoundBarrier(psx2 * x2i)));
                float pdniTerm1 = pdx1 * x1i;
                float pdniTerm2 = pdx2 * x2i;
                float pdniTerm3 = pdyy * yyi;
                float pdniInner = LegacyPrecisionMath.ContractedMultiplySubtract(
                    -pdx1,
                    x1i,
                    LegacyPrecisionMath.RoundBarrier(pdx2 * x2i));
                float pdni = LegacyPrecisionMath.ContractedMultiplySubtract(-pdyy, yyi, pdniInner);
                // QVOR = QOPI*(GSUM*PSNI + GDIF*PDNI) — single Fortran REAL expression
                // Matching Fortran: t1=GSUM*PSNI (rounded), then t1+GDIF*PDNI (GDIF*PDNI
                // may stay in extended precision before adding), then QOPI*sum.
                // Use Fma to match: QOPI * (GSUM*PSNI + GDIF*PDNI)
                // = QOPI * Fma(GSUM, PSNI, GDIF*PDNI)
                float psiNiInner = LegacyPrecisionMath.Fma(gsum, psni, gdif * pdni);
                float psiNiDelta = LegacyPrecisionMath.RoundBarrier(qopi * psiNiInner);
                float psiNiBefore = psiNi;
                psiNi += psiNiDelta;
                // Parity trace: per-panel psiNi accumulation at wake node 6
                
                
                // Trace psiNi at wake node 0 psiY call (io=161)
                // Trace psiNi at wake node 0 — psiX (NX>0.5) and psiY (NY>0.5)
                
                TracePsilinAccumState(
                    traceScope,
                    io,
                    "vortex_segment",
                    jo + 1,
                    jp + 1,
                    psiBefore,
                    psiNiBefore,
                    psi,
                    psiNi,
                    "Single");

                float dqJo = qopi * (psni - pdni);
                float dqJp = qopi * (psni + pdni);
                dqdg[jo] += dqJo;
                dqdg[jp] += dqJp;

                TracePsilinVortexSegment(
                    traceScope,
                    io,
                    jo + 1,
                    jp + 1,
                    "Single",
                    x1,
                    x2,
                    yy,
                    rs1,
                    rs2,
                    g1,
                    g2,
                    t1,
                    t2,
                    dxInv,
                    psisTerm1,
                    psisTerm2,
                    psisTerm3,
                    psisTerm4,
                    psis,
                    psidTerm1,
                    psidTerm2,
                    psidTerm3,
                    psidTerm4,
                    psidTerm5,
                    psidHalfTerm,
                    psid,
                    psx1,
                    psx2,
                    psyy,
                    pdxSum,
                    pdx1Mul,
                    pdx1PanelTerm,
                    pdx1Accum1,
                    pdx1Accum2,
                    pdx1Numerator,
                    pdx1,
                    pdx2Mul,
                    pdx2PanelTerm,
                    pdx2Accum1,
                    pdx2Accum2,
                    pdx2Numerator,
                    pdx2,
                    pdyy,
                    gammaJo,
                    gammaJp,
                    gsum,
                    gdif,
                    psni,
                    pdni,
                    psiDelta,
                    psiNiDelta,
                    dzJo,
                    dzJp,
                    dqJo,
                    dqJp);

                
            }
        }

        if (!skipTEPanel)
        {
            // The optimized legacy TE path contracts the PSIG head, but the PGAM path
            // is subtler: the leading scaled-product pair is formed in widened
            // precision and rounded once to float, then the X2-X1 base is added in
            // float, and the YY*(T1-T2) tail is fused onto that rounded base. Keeping
            // PGAM on either the old fused head path, a fully stepwise float pair, or
            // a widened tail sum stays one to four ULPs off on the reduced-panel
            // full-run TE correction.
            float psigHead = LegacyPrecisionMath.FusedMultiplyAdd(0.5f * yy, g1 - g2, x2 * (t2 - apan));
            float psig = LegacyPrecisionMath.FusedMultiplyAdd(-x1, t1 - apan, psigHead);
            // Fortran: PGAM = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
            // Left-to-right: ((((0.5*X1)*G1) - ((0.5*X2)*G2)) + X2) - X1 + YY*(T1-T2)
            float pgamHalfX1 = LegacyPrecisionMath.RoundBarrier(0.5f * x1);
            float pgamTerm1 = LegacyPrecisionMath.RoundBarrier(pgamHalfX1 * g1);
            float pgamHalfX2 = LegacyPrecisionMath.RoundBarrier(0.5f * x2);
            float pgamTerm2 = LegacyPrecisionMath.RoundBarrier(pgamHalfX2 * g2);
            float pgamDiff = LegacyPrecisionMath.RoundBarrier(pgamTerm1 - pgamTerm2);
            float pgamPlusX2 = LegacyPrecisionMath.RoundBarrier(pgamDiff + x2);
            float pgamBase = LegacyPrecisionMath.RoundBarrier(pgamPlusX2 - x1);
            float pgamTailMul = LegacyPrecisionMath.RoundBarrier(yy * LegacyPrecisionMath.RoundBarrier(t1 - t2));
            float pgam = LegacyPrecisionMath.RoundBarrier(pgamBase + pgamTailMul);
            float psigx1 = -(t1 - apan);
            float psigx2 = t2 - apan;
            float psigyy = 0.5f * (g1 - g2);
            float pgamx1 = 0.5f * g1;
            float pgamx2 = -0.5f * g2;
            float pgamyy = t1 - t2;
            // Fortran PSILIN TE: plain REAL left-to-right
            float psigni = LegacyPrecisionMath.RoundBarrier(
                LegacyPrecisionMath.RoundBarrier(
                    LegacyPrecisionMath.RoundBarrier(psigx1 * x1i)
                    + LegacyPrecisionMath.RoundBarrier(psigx2 * x2i))
                + LegacyPrecisionMath.RoundBarrier(psigyy * yyi));
            float pgamni = LegacyPrecisionMath.RoundBarrier(
                LegacyPrecisionMath.RoundBarrier(
                    LegacyPrecisionMath.RoundBarrier(pgamx1 * x1i)
                    + LegacyPrecisionMath.RoundBarrier(pgamx2 * x2i))
                + LegacyPrecisionMath.RoundBarrier(pgamyy * yyi));
            float gammaLastJp = (float)state.VortexStrength[lastJp];
            float gammaLastJo = (float)state.VortexStrength[lastJo];
            // Fortran: plain REAL
            float gammaTeDelta = gammaLastJp - gammaLastJo;
            float sigte = 0.5f * scs * gammaTeDelta;
            float gamte = -0.5f * sds * gammaTeDelta;
            float dzJoTeSig = -hopi * psig * scs * 0.5f;
            float dzJpTeSig = hopi * psig * scs * 0.5f;
            float dzJoTeGam = hopi * pgam * sds * 0.5f;
            float dzJpTeGam = -hopi * pgam * sds * 0.5f;
            float dqJoTeSigHalf = psigni * 0.5f;
            float dqJoTeSigTerm = dqJoTeSigHalf * scs;
            float dqJoTeGamHalf = pgamni * 0.5f;
            float dqJoTeGamTerm = dqJoTeGamHalf * sds;
            float dqTeInner = dqJoTeSigTerm - dqJoTeGamTerm;
            float dqJoTe = -hopi * ((psigni * 0.5f * scs) - (pgamni * 0.5f * sds));
            float dqJpTe = hopi * ((psigni * 0.5f * scs) - (pgamni * 0.5f * sds));

            // Keep the TE contribution signs aligned with classic XFoil:
            // GAMTE already carries the negated SDS factor, so the PGAM branch
            // is added here rather than subtracted again.
            float psiBeforeTe = psi;
            psi += hopi * ((psig * sigte) + (pgam * gamte));
            dzdg[lastJo] += dzJoTeSig;
            dzdg[lastJp] += dzJpTeSig;
            dzdg[lastJo] += dzJoTeGam;
            dzdg[lastJp] += dzJpTeGam;

            // GDB: trace DZDG[0] after TE correction at field node 33
            

            float psiNiTeTerm1 = psigni * sigte;
            float psiNiTeTerm2 = pgamni * gamte;
            float psiNiTeInner = psiNiTeTerm1 + psiNiTeTerm2;
            // Fortran: PSI_NI = PSI_NI + HOPI*(PSIGNI*SIGTE + PGAMNI*GAMTE)
            // All REAL (float) operations — multiply rounds, then add rounds.
            float psiNiBeforeTe = psiNi;
            float psiNiTeDelta = hopi * psiNiTeInner;
            psiNi = psiNi + psiNiTeDelta;
            
            TracePsilinAccumState(
                traceScope,
                io,
                "te_correction",
                lastJo + 1,
                lastJp + 1,
                psiBeforeTe,
                psiNiBeforeTe,
                psi,
                psiNi,
                "Single");
            dqdg[lastJo] += dqJoTe;
            dqdg[lastJp] += dqJpTe;

            TracePsilinTeCorrection(
                traceScope,
                io,
                lastJo + 1,
                lastJp + 1,
                "Single",
                psig,
                pgam,
                psigni,
                pgamni,
                sigte,
                gamte,
                scs,
                sds,
                dzJoTeSig,
                dzJpTeSig,
                dzJoTeGam,
                dzJpTeGam,
                dqJoTeSigHalf,
                dqJoTeSigTerm,
                dqJoTeGamHalf,
                dqJoTeGamTerm,
                dqTeInner,
                dqJoTe,
                dqJpTe);
            TracePsilinTePgamTerms(
                traceScope,
                io,
                lastJo + 1,
                lastJp + 1,
                "Single",
                pgamTerm1,
                pgamTerm2,
                pgamDiff,
                pgamBase,
                LegacyPrecisionMath.RoundBarrier(t1 - t2),
                pgamTailMul);
        }

        float psiFreestreamDelta = (float)freestreamSpeed * ((cosa * fieldYf) - (sina * fieldXf));
        float psiNiFreestreamDelta = (float)freestreamSpeed * ((cosa * fieldNormalYf) - (sina * fieldNormalXf));
        TracePsilinResultTerms(
            traceScope,
            io,
            psi,
            psiNi,
            psiFreestreamDelta,
            psiNiFreestreamDelta,
            "Single");
        psi += psiFreestreamDelta;
        psiNi += psiNiFreestreamDelta;

        Array.Clear(state.StreamfunctionNormalSensitivity);
        for (int i = 0; i < n; i++)
        {
            state.StreamfunctionVortexSensitivity[i] = dzdg[i];
            state.StreamfunctionSourceSensitivity[i] = dzdm[i];
            state.VelocityVortexSensitivity[i] = dqdg[i];
            state.VelocitySourceSensitivity[i] = dqdm[i];
        }

        TracePsilinResult(traceScope, io, psi, psiNi, "Single");

        return (psi, psiNi);
    }

    // Legacy mapping: none; managed-only trace helper family around PSILIN diagnostics.
    // Difference from legacy: The original Fortran emits equivalent detail only in instrumented builds, while the managed port keeps structured trace helpers co-located with the kernel.
    // Decision: Keep the trace-helper block because it is essential for parity debugging and does not change the solver.
    private static void TracePsilinField(
        string scope,
        int fieldIndex,
        double fieldX,
        double fieldY,
        double fieldNormalX,
        double fieldNormalY,
        bool computeGeometricSensitivities,
        bool includeSourceTerms,
        string precision)
    {
    }

    private static void TracePsilinPanel(
        string scope,
        int fieldIndex,
        int panelIndex,
        int jm,
        int jo,
        int jp,
        int jq,
        bool computeGeometricSensitivities,
        bool includeSourceTerms,
        string precision,
        double panelXJo,
        double panelYJo,
        double panelXJp,
        double panelYJp,
        double panelDx,
        double panelDy,
        double dso,
        double dsio,
        double panelAngle,
        double rx1,
        double ry1,
        double rx2,
        double ry2,
        double sx,
        double sy,
        double x1,
        double x2,
        double yy,
        double rs1,
        double rs2,
        double sgn,
        double g1,
        double g2,
        double t1,
        double t2,
        double x1i,
        double x2i,
        double yyi)
    {
    }

    private static void TracePsilinResult(
        string scope,
        int fieldIndex,
        double psi,
        double psiNormalDerivative,
        string precision)
    {
    }

    private static void TracePsilinAccumState(
        string scope,
        int fieldIndex,
        string stage,
        int jo,
        int jp,
        double psiBefore,
        double psiNormalBefore,
        double psi,
        double psiNormalDerivative,
        string precision)
    {
    }

    private static void TracePsilinResultTerms(
        string scope,
        int fieldIndex,
        double psiBeforeFreestream,
        double psiNormalBeforeFreestream,
        double psiFreestreamDelta,
        double psiNormalFreestreamDelta,
        string precision)
    {
    }

    private static void TracePsilinSourceSegment(
        string scope,
        int fieldIndex,
        int panelIndex,
        int half,
        int jm,
        int jo,
        int jp,
        int jq,
        string precision,
        double x0,
        double x1,
        double x2,
        double yy,
        double panelAngle,
        double x1i,
        double x2i,
        double yyi,
        double rs0,
        double rs1,
        double rs2,
        double g0,
        double g1,
        double g2,
        double t0,
        double t1,
        double t2,
        double dso,
        double dsio,
        double dsm,
        double dsim,
        double dsp,
        double dsip,
        double dxInv,
        double sourceTermLeft,
        double sourceTermRight,
        double ssum,
        double sdif,
        double psum,
        double pdif,
        double psx0,
        double psx1,
        double psx2,
        double psyy,
        double pdx0Term1,
        double pdx0Term2,
        double pdx0Numerator,
        double pdx0,
        double pdx1Term1,
        double pdx1Term2,
        double pdx1Numerator,
        double pdx1,
        double pdx2Term1,
        double pdx2Term2,
        double pdx2Numerator,
        double pdx2,
        double pdyyTerm1,
        double pdyyTailLinear,
        double pdyyTailAngular,
        double pdyyTerm2,
        double pdyyNumerator,
        double pdyy,
        double psniTerm1,
        double psniTerm2,
        double psniTerm3,
        double psni,
        double pdniTerm1,
        double pdniTerm2,
        double pdniTerm3,
        double pdni,
        double dzJm,
        double dzJo,
        double dzJp,
        double dzJq,
        double dqJm,
        double dqJo,
        double dqJp,
        double dqJq)
    {
    }

    private static void TracePsilinSourcePdyyWrite(
        string scope,
        int fieldIndex,
        int panelIndex,
        int half,
        string precision,
        double x0,
        double xEdge,
        double yy,
        double t0,
        double tEdge,
        double psyy,
        double dxInv,
        double pdyyWriteDt,
        double pdyyWriteInner,
        double pdyyWriteHead,
        double pdyyWriteTail,
        double pdyyWriteSum,
        double pdyyWriteValue,
        double pdyyTerm1,
        double pdyyTerm2,
        double pdyyNumerator,
        double pdyy)
    {
    }

    private static void TracePsilinSourceDqTerms(
        string scope,
        int fieldIndex,
        int panelIndex,
        int half,
        string precision,
        double dqJmTerm1,
        double dqJmTerm2,
        double dqJmInner,
        double dqJoTerm1,
        double dqJoTerm2,
        double dqJoInner,
        double dqJpTerm1,
        double dqJpTerm2,
        double dqJpInner,
        double dqJqTerm1,
        double dqJqTerm2,
        double dqJqInner)
    {
    }

    private static void TracePsilinSourceDzTerms(
        string scope,
        int fieldIndex,
        int panelIndex,
        int half,
        string precision,
        double dzJmTerm1,
        double dzJmTerm2,
        double dzJmInner,
        double dzJoTerm1,
        double dzJoTerm2,
        double dzJoInner,
        double dzJpTerm1,
        double dzJpTerm2,
        double dzJpInner,
        double dzJqTerm1,
        double dzJqTerm2,
        double dzJqInner)
    {
    }

    private static void TracePsilinTeCorrection(
        string scope,
        int fieldIndex,
        int jo,
        int jp,
        string precision,
        double psig,
        double pgam,
        double psigni,
        double pgamni,
        double sigte,
        double gamte,
        double scs,
        double sds,
        double dzJoTeSig,
        double dzJpTeSig,
        double dzJoTeGam,
        double dzJpTeGam,
        double dqJoTeSigHalf,
        double dqJoTeSigTerm,
        double dqJoTeGamHalf,
        double dqJoTeGamTerm,
        double dqTeInner,
        double dqJoTe,
        double dqJpTe)
    {
    }

    private static void TracePsilinTePgamTerms(
        string scope,
        int fieldIndex,
        int jo,
        int jp,
        string precision,
        double pgamLeadProduct1,
        double pgamLeadProduct2,
        double pgamLeadPair,
        double pgamBase,
        double pgamDt,
        double pgamTail)
    {
    }

    private static void TracePsilinSourceHalfTerms(
        string scope,
        int fieldIndex,
        int panelIndex,
        int half,
        string precision,
        double x0,
        double psumTerm1,
        double psumTerm2,
        double psumTerm3,
        double psumAccum,
        double psum,
        double pdifTerm1,
        double pdifTerm2,
        double pdifTerm3,
        double pdifTerm4,
        double pdifAccum1,
        double pdifAccum2,
        double pdifNumerator,
        double pdif)
    {
}

    private static void TracePsilinVortexSegment(
        string scope,
        int fieldIndex,
        int jo,
        int jp,
        string precision,
        double x1,
        double x2,
        double yy,
        double rs1,
        double rs2,
        double g1,
        double g2,
        double t1,
        double t2,
        double dxInv,
        double psisTerm1,
        double psisTerm2,
        double psisTerm3,
        double psisTerm4,
        double psis,
        double psidTerm1,
        double psidTerm2,
        double psidTerm3,
        double psidTerm4,
        double psidTerm5,
        double psidHalfTerm,
        double psid,
        double psx1,
        double psx2,
        double psyy,
        double pdxSum,
        double pdx1Mul,
        double pdx1PanelTerm,
        double pdx1Accum1,
        double pdx1Accum2,
        double pdx1Numerator,
        double pdx1,
        double pdx2Mul,
        double pdx2PanelTerm,
        double pdx2Accum1,
        double pdx2Accum2,
        double pdx2Numerator,
        double pdx2,
        double pdyy,
        double gammaJo,
        double gammaJp,
        double gsum,
        double gdif,
        double psni,
        double pdni,
        double psiDelta,
        double psiNiDelta,
        double dzJo,
        double dzJp,
        double dqJo,
        double dqJp)
    {
    }

    /// <summary>
    /// Computes the source contribution to PSI for both half-panels (1-0 and 0-2).
    /// Port of Fortran lines 235-334 in PSILIN.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN source-half-panel contribution.
    // Difference from legacy: The algebra follows the same two-half source integration, but the managed code factors it into a dedicated helper and mirrors the parity-sensitive product-order choices explicitly.
    // Decision: Keep the helper because it isolates the source branch, which is one of the main parity hot spots.
    private static void ComputeSourceContribution(
        string traceScope,
        int fieldIndex,
        int jo, int jp, int jm, int jq, int n,
        double x1, double x2, double yy,
        double sgn,
        double g1, double g2, double t1, double t2,
        double rs1, double rs2,
        double x1i, double x2i, double yyi,
        double apan, double dsio,
        LinearVortexPanelState panel, InviscidSolverState state,
        double qopi,
        string precision,
        ref double psi, ref double psiNi)
    {
        // Midpoint quantities
        double x0 = 0.5 * (x1 + x2);
        double rs0 = x0 * x0 + yy * yy;
        double g0 = Math.Log(rs0);
        double t0 = Math.Atan2(sgn * x0, sgn * yy) + ((0.5 - (0.5 * sgn)) * Math.PI);

        // ---- First half-panel (1-0): from start node to midpoint ----
        {
            double dxInv = 1.0 / (x1 - x0);
            double psum = x0 * (t0 - apan) - x1 * (t1 - apan) + 0.5 * yy * (g1 - g0);
            double pdif = ((x1 + x0) * psum + rs1 * (t1 - apan) - rs0 * (t0 - apan)
                          + (x0 - x1) * yy) * dxInv;

            double psx1 = -(t1 - apan);
            double psx0 = t0 - apan;
            double psyy = 0.5 * (g1 - g0);

            double pdx1Term1 = (x1 + x0) * psx1;
            double pdx1Term2 = 2.0 * x1 * (t1 - apan);
            double pdx1Numerator = (pdx1Term1 + psum) + pdx1Term2;
            pdx1Numerator -= pdif;
            double pdx1 = ((x1 + x0) * psx1 + psum + 2.0 * x1 * (t1 - apan) - pdif) * dxInv;
            double pdx0Term1 = (x1 + x0) * psx0;
            double pdx0Term2 = -2.0 * x0 * (t0 - apan);
            double pdx0Numerator = (pdx0Term1 + psum) + pdx0Term2;
            pdx0Numerator += pdif;
            double pdx0 = ((x1 + x0) * psx0 + psum - 2.0 * x0 * (t0 - apan) + pdif) * dxInv;
            double pdyyTerm1 = (x1 + x0) * psyy;
            double pdyyTailLinear = 2.0 * (x0 - x1);
            double pdyyTailAngular = 2.0 * yy * (t1 - t0);
            double pdyyTerm2 = pdyyTailLinear + pdyyTailAngular;
            double pdyyNumerator = pdyyTerm1 + pdyyTerm2;
            double pdyy = pdyyNumerator * dxInv;

            double dsm = Math.Sqrt(
                (panel.X[jp] - panel.X[jm]) * (panel.X[jp] - panel.X[jm]) +
                (panel.Y[jp] - panel.Y[jm]) * (panel.Y[jp] - panel.Y[jm]));
            double dsim = 1.0 / dsm;

            double sourceTermLeft = (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio;
            double sourceTermRight = (state.SourceStrength[jp] - state.SourceStrength[jm]) * dsim;
            double ssum = sourceTermLeft + sourceTermRight;
            double sdif = sourceTermLeft - sourceTermRight;
            double dzJm = qopi * (-psum * dsim + pdif * dsim);
            double dzJo = qopi * (-psum * dsio - pdif * dsio);
            double dzJp = qopi * (psum * (dsio + dsim) + pdif * (dsio - dsim));

            psi += qopi * (psum * ssum + pdif * sdif);

            // dPsi/dm
            state.StreamfunctionSourceSensitivity[jm] += dzJm;
            state.StreamfunctionSourceSensitivity[jo] += dzJo;
            state.StreamfunctionSourceSensitivity[jp] += dzJp;

            // dPsi/dni
            double psni = psx1 * x1i + psx0 * (x1i + x2i) * 0.5 + psyy * yyi;
            double pdni = pdx1 * x1i + pdx0 * (x1i + x2i) * 0.5 + pdyy * yyi;
            psiNi += qopi * (psni * ssum + pdni * sdif);
            double dqJm = qopi * (-psni * dsim + pdni * dsim);
            double dqJo = qopi * (-psni * dsio - pdni * dsio);
            double dqJp = qopi * (psni * (dsio + dsim) + pdni * (dsio - dsim));

            // dQ/dm
            state.VelocitySourceSensitivity[jm] += dqJm;
            state.VelocitySourceSensitivity[jo] += dqJo;
            state.VelocitySourceSensitivity[jp] += dqJp;
            TracePsilinSourceDqTerms(
                traceScope,
                fieldIndex,
                jo + 1,
                half: 1,
                precision,
                dqJmTerm1: (-psni) * dsim,
                dqJmTerm2: pdni * dsim,
                dqJmInner: ((-psni) * dsim) + (pdni * dsim),
                dqJoTerm1: (-psni) * dsio,
                dqJoTerm2: (-pdni) * dsio,
                dqJoInner: ((-psni) * dsio) - (pdni * dsio),
                dqJpTerm1: psni * (dsio + dsim),
                dqJpTerm2: pdni * (dsio - dsim),
                dqJpInner: (psni * (dsio + dsim)) + (pdni * (dsio - dsim)),
                dqJqTerm1: 0.0,
                dqJqTerm2: 0.0,
                dqJqInner: 0.0);

            TracePsilinSourceSegment(
                traceScope,
                fieldIndex,
                jo + 1,
                half: 1,
                jm: jm + 1,
                jo: jo + 1,
                jp: jp + 1,
                jq: jq + 1,
                precision,
                x0,
                x1,
                x2,
                yy,
                panelAngle: apan,
                x1i,
                x2i,
                yyi,
                rs0,
                rs1,
                rs2,
                g0,
                g1,
                g2,
                t0,
                t1,
                t2,
                dso: 1.0 / dsio,
                dsio,
                dsm,
                dsim,
                dsp: 0.0,
                dsip: 0.0,
                dxInv,
                sourceTermLeft,
                sourceTermRight,
                ssum,
                sdif,
                psum,
                pdif,
                psx0,
                psx1,
                psx2: 0.0,
                psyy,
                pdx0Term1,
                pdx0Term2,
                pdx0Numerator,
                pdx0,
                pdx1Term1,
                pdx1Term2,
                pdx1Numerator,
                pdx1,
                pdx2Term1: 0.0,
                pdx2Term2: 0.0,
                pdx2Numerator: 0.0,
                pdx2: 0.0,
                pdyyTerm1,
                pdyyTailLinear,
                pdyyTailAngular,
                pdyyTerm2,
                pdyyNumerator,
                pdyy,
                psniTerm1: psx1 * x1i,
                psniTerm2: psx0 * (x1i + x2i) * 0.5,
                psniTerm3: psyy * yyi,
                psni,
                pdniTerm1: pdx1 * x1i,
                pdniTerm2: pdx0 * (x1i + x2i) * 0.5,
                pdniTerm3: pdyy * yyi,
                pdni,
                dzJm,
                dzJo,
                dzJp,
                dzJq: 0.0,
                dqJm,
                dqJo,
                dqJp,
                dqJq: 0.0);
        }

        // ---- Second half-panel (0-2): from midpoint to end node ----
        {
            double dxInv = 1.0 / (x0 - x2);
            double psum = x2 * (t2 - apan) - x0 * (t0 - apan) + 0.5 * yy * (g0 - g2);
            double pdif = ((x0 + x2) * psum + rs0 * (t0 - apan) - rs2 * (t2 - apan)
                          + (x2 - x0) * yy) * dxInv;

            double psx0 = -(t0 - apan);
            double psx2 = t2 - apan;
            double psyy = 0.5 * (g0 - g2);

            double pdx0Term1 = (x0 + x2) * psx0;
            double pdx0Term2 = 2.0 * x0 * (t0 - apan);
            double pdx0Numerator = (pdx0Term1 + psum) + pdx0Term2;
            pdx0Numerator -= pdif;
            double pdx0 = ((x0 + x2) * psx0 + psum + 2.0 * x0 * (t0 - apan) - pdif) * dxInv;
            double pdx2Term1 = (x0 + x2) * psx2;
            double pdx2Term2 = -2.0 * x2 * (t2 - apan);
            double pdx2Numerator = (pdx2Term1 + psum) + pdx2Term2;
            pdx2Numerator += pdif;
            double pdx2 = ((x0 + x2) * psx2 + psum - 2.0 * x2 * (t2 - apan) + pdif) * dxInv;
            double pdyyTerm1 = (x0 + x2) * psyy;
            double pdyyTailLinear = 2.0 * (x2 - x0);
            double pdyyTailAngular = 2.0 * yy * (t0 - t2);
            double pdyyTerm2 = pdyyTailLinear + pdyyTailAngular;
            double pdyyNumerator = pdyyTerm1 + pdyyTerm2;
            double pdyy = pdyyNumerator * dxInv;

            double dsp = Math.Sqrt(
                (panel.X[jq] - panel.X[jo]) * (panel.X[jq] - panel.X[jo]) +
                (panel.Y[jq] - panel.Y[jo]) * (panel.Y[jq] - panel.Y[jo]));
            double dsip = 1.0 / dsp;

            double sourceTermLeft = (state.SourceStrength[jq] - state.SourceStrength[jo]) * dsip;
            double sourceTermRight = (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio;
            double ssum = sourceTermLeft + sourceTermRight;
            double sdif = sourceTermLeft - sourceTermRight;
            double dzJo = qopi * (-psum * (dsip + dsio) - pdif * (dsip - dsio));
            double dzJp = qopi * (psum * dsio - pdif * dsio);
            double dzJq = qopi * (psum * dsip + pdif * dsip);

            psi += qopi * (psum * ssum + pdif * sdif);

            // dPsi/dm
            state.StreamfunctionSourceSensitivity[jo] += dzJo;
            state.StreamfunctionSourceSensitivity[jp] += dzJp;
            // Clamp jq to valid range (it may equal jp at the boundary)
            if (jq < n)
            {
                state.StreamfunctionSourceSensitivity[jq] += dzJq;
            }

            // dPsi/dni
            double psni = psx0 * (x1i + x2i) * 0.5 + psx2 * x2i + psyy * yyi;
            double pdni = pdx0 * (x1i + x2i) * 0.5 + pdx2 * x2i + pdyy * yyi;
            psiNi += qopi * (psni * ssum + pdni * sdif);
            double dqJo = qopi * (-psni * (dsip + dsio) - pdni * (dsip - dsio));
            double dqJp = qopi * (psni * dsio - pdni * dsio);
            double dqJq = qopi * (psni * dsip + pdni * dsip);

            // dQ/dm
            state.VelocitySourceSensitivity[jo] += dqJo;
            state.VelocitySourceSensitivity[jp] += dqJp;
            if (jq < n)
            {
                state.VelocitySourceSensitivity[jq] += dqJq;
            }
            TracePsilinSourceDqTerms(
                traceScope,
                fieldIndex,
                jo + 1,
                half: 2,
                precision,
                dqJmTerm1: 0.0,
                dqJmTerm2: 0.0,
                dqJmInner: 0.0,
                dqJoTerm1: (-psni) * (dsip + dsio),
                dqJoTerm2: (-pdni) * (dsip - dsio),
                dqJoInner: ((-psni) * (dsip + dsio)) - (pdni * (dsip - dsio)),
                dqJpTerm1: psni * dsio,
                dqJpTerm2: (-pdni) * dsio,
                dqJpInner: (psni * dsio) - (pdni * dsio),
                dqJqTerm1: psni * dsip,
                dqJqTerm2: pdni * dsip,
                dqJqInner: (psni * dsip) + (pdni * dsip));

            TracePsilinSourceSegment(
                traceScope,
                fieldIndex,
                jo + 1,
                half: 2,
                jm: jm + 1,
                jo: jo + 1,
                jp: jp + 1,
                jq: jq + 1,
                precision,
                x0,
                x1,
                x2,
                yy,
                panelAngle: apan,
                x1i,
                x2i,
                yyi,
                rs0,
                rs1,
                rs2,
                g0,
                g1,
                g2,
                t0,
                t1,
                t2,
                dso: 1.0 / dsio,
                dsio,
                dsm: 0.0,
                dsim: 0.0,
                dsp,
                dsip,
                dxInv,
                sourceTermLeft,
                sourceTermRight,
                ssum,
                sdif,
                psum,
                pdif,
                psx0,
                psx1: 0.0,
                psx2,
                psyy,
                pdx0Term1,
                pdx0Term2,
                pdx0Numerator,
                pdx0,
                pdx1Term1: 0.0,
                pdx1Term2: 0.0,
                pdx1Numerator: 0.0,
                pdx1: 0.0,
                pdx2Term1,
                pdx2Term2,
                pdx2Numerator,
                pdx2,
                pdyyTerm1,
                pdyyTailLinear,
                pdyyTailAngular,
                pdyyTerm2,
                pdyyNumerator,
                pdyy,
                psniTerm1: psx0 * (x1i + x2i) * 0.5,
                psniTerm2: psx2 * x2i,
                psniTerm3: psyy * yyi,
                psni,
                pdniTerm1: pdx0 * (x1i + x2i) * 0.5,
                pdniTerm2: pdx2 * x2i,
                pdniTerm3: pdyy * yyi,
                pdni,
                dzJm: 0.0,
                dzJo,
                dzJp,
                dzJq: jq < n ? dzJq : 0.0,
                dqJm: 0.0,
                dqJo,
                dqJp,
                dqJq: jq < n ? dqJq : 0.0);
        }
    }

    /// <summary>
    /// Computes the vortex panel contribution to PSI (linear vorticity integrals).
    /// Port of Fortran lines 336-372 in PSILIN.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN vortex contribution.
    // Difference from legacy: The same linear-vorticity kernel is packaged into a dedicated helper and paired with trace instrumentation for the managed state arrays.
    // Decision: Keep the helper because it makes the vortex branch auditable without altering the kernel.
    private static void ComputeVortexContribution(
        string traceScope,
        int fieldIndex,
        int jo, int jp, int n,
        double x1, double x2, double yy,
        double g1, double g2, double t1, double t2,
        double rs1, double rs2,
        double x1i, double x2i, double yyi,
        LinearVortexPanelState panel, InviscidSolverState state,
        double qopi,
        string precision,
        ref double psi, ref double psiNi)
    {
        double dxInv = 1.0 / (x1 - x2);

        // Sum and difference integrals for linear vorticity
        double psis = 0.5 * x1 * g1 - 0.5 * x2 * g2 + x2 - x1 + yy * (t1 - t2);
        double psisTerm1 = 0.5 * x1 * g1;
        double psisTerm2 = -0.5 * x2 * g2;
        double psisTerm3 = x2 - x1;
        double psisTerm4 = yy * (t1 - t2);
        double psidTerm1 = (x1 + x2) * psis;
        double psidTerm2 = rs2 * g2;
        double psidTerm3 = rs1 * g1;
        double psidTerm4 = x1 * x1;
        double psidTerm5 = x2 * x2;
        double psidHalfAccum1 = psidTerm2 - psidTerm3;
        double psidHalfAccum2 = psidHalfAccum1 + psidTerm4;
        double psidHalfInner = psidHalfAccum2 - psidTerm5;
        double psidHalfTerm = 0.5 * psidHalfInner;
        double psidBase = psidTerm1 + psidHalfTerm;
        double psid = psidBase * dxInv;

        // Partial derivatives for tangential velocity
        double psx1 = 0.5 * g1;
        double psx2 = -0.5 * g2;
        double psyy = t1 - t2;

        double pdxSum = x1 + x2;
        double pdx1Mul = pdxSum * psx1;
        double pdx1PanelTerm = x1 * g1;
        double pdx1Accum1 = pdx1Mul + psis;
        double pdx1Accum2 = pdx1Accum1 - pdx1PanelTerm;
        double pdx1Numerator = pdx1Accum2 - psid;
        double pdx1 = ((x1 + x2) * psx1 + psis - x1 * g1 - psid) * dxInv;
        double pdx2Mul = pdxSum * psx2;
        double pdx2PanelTerm = x2 * g2;
        double pdx2Accum1 = pdx2Mul + psis;
        double pdx2Accum2 = pdx2Accum1 + pdx2PanelTerm;
        double pdx2Numerator = pdx2Accum2 + psid;
        double pdx2 = ((x1 + x2) * psx2 + psis + x2 * g2 + psid) * dxInv;
        double pdyy = ((x1 + x2) * psyy - yy * (g1 - g2)) * dxInv;

        // Vortex strength sums/differences
        double gammaJo = state.VortexStrength[jo];
        double gammaJp = state.VortexStrength[jp];
        double gsum = gammaJp + gammaJo;
        double gdif = gammaJp - gammaJo;

        // Accumulate PSI
        double psiDelta = qopi * (psis * gsum + psid * gdif);
        psi += psiDelta;

        // dPsi/dGam
        double dzJo = qopi * (psis - psid);
        double dzJp = qopi * (psis + psid);
        state.StreamfunctionVortexSensitivity[jo] += dzJo;
        state.StreamfunctionVortexSensitivity[jp] += dzJp;

        // dPsi/dni (tangential velocity)
        double psni = psx1 * x1i + psx2 * x2i + psyy * yyi;
        double pdni = pdx1 * x1i + pdx2 * x2i + pdyy * yyi;
        double psiNiDelta = qopi * (gsum * psni + gdif * pdni);
        psiNi += psiNiDelta;

        // dQ/dGam
        double dqJo = qopi * (psni - pdni);
        double dqJp = qopi * (psni + pdni);
        state.VelocityVortexSensitivity[jo] += dqJo;
        state.VelocityVortexSensitivity[jp] += dqJp;

        TracePsilinVortexSegment(
            traceScope,
            fieldIndex,
            jo + 1,
            jp + 1,
            precision,
            x1,
            x2,
            yy,
            rs1,
            rs2,
            g1,
            g2,
            t1,
            t2,
            dxInv,
            psisTerm1,
            psisTerm2,
            psisTerm3,
            psisTerm4,
            psis,
            psidTerm1,
            psidTerm2,
            psidTerm3,
            psidTerm4,
            psidTerm5,
            psidHalfTerm,
            psid,
            psx1,
            psx2,
            psyy,
            pdxSum,
            pdx1Mul,
            pdx1PanelTerm,
            pdx1Accum1,
            pdx1Accum2,
            pdx1Numerator,
            pdx1,
            pdx2Mul,
            pdx2PanelTerm,
            pdx2Accum1,
            pdx2Accum2,
            pdx2Numerator,
            pdx2,
            pdyy,
            gammaJo,
            gammaJp,
            gsum,
            gdif,
            psni,
            pdni,
            psiDelta,
            psiNiDelta,
            dzJo,
            dzJp,
            dqJo,
            dqJp);
    }

    /// <summary>
    /// Computes the TE panel contribution to PSI and sensitivity arrays.
    /// Port of Fortran lines 395-456 in PSILIN (label 11 through label 12).
    /// The TE panel uses the last-panel geometry (g1, g2, t1, t2, etc.) to compute
    /// source-like and vortex-like contributions from the TE gap.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xpanel.f :: PSILIN trailing-edge panel correction.
    // Difference from legacy: The TE correction terms are isolated into a dedicated helper over the managed state object instead of being embedded in one long PSILIN routine.
    // Decision: Keep the helper because it separates the TE special case cleanly while preserving the legacy formula.
    private static void ComputeTEPanelContribution(
        int jo, int jp, int n,
        double x1, double x2, double yy,
        double g1, double g2, double t1, double t2,
        double x1i, double x2i, double yyi,
        double apan,
        double scs, double sds,
        LinearVortexPanelState panel, InviscidSolverState state,
        double hopi,
        ref double psi, ref double psiNi)
    {
        // TE panel source-like and vortex-like influence functions
        // PSIG = source-like streamfunction from TE gap
        // PGAM = vortex-like streamfunction from TE gap
        double psig = 0.5 * yy * (g1 - g2) + x2 * (t2 - apan) - x1 * (t1 - apan);
        double pgam = 0.5 * x1 * g1 - 0.5 * x2 * g2 + x2 - x1 + yy * (t1 - t2);

        // Normal derivatives of TE influence functions
        double psigx1 = -(t1 - apan);
        double psigx2 = t2 - apan;
        double psigyy = 0.5 * (g1 - g2);
        double pgamx1 = 0.5 * g1;
        double pgamx2 = -0.5 * g2;
        double pgamyy = t1 - t2;

        double psigni = psigx1 * x1i + psigx2 * x2i + psigyy * yyi;
        double pgamni = pgamx1 * x1i + pgamx2 * x2i + pgamyy * yyi;

        // TE panel source and vortex strengths
        // SIGTE = 0.5*SCS*(GAM(JP) - GAM(JO))
        // GAMTE = -0.5*SDS*(GAM(JP) - GAM(JO))
        // Here jo/jp are the last panel's nodes (jo=N-1, jp=0 in 0-based)
        double sigte = 0.5 * scs * (state.VortexStrength[jp] - state.VortexStrength[jo]);
        double gamte = -0.5 * sds * (state.VortexStrength[jp] - state.VortexStrength[jo]);

        // TE panel contribution to PSI
        // GAMTE already includes the negated SDS term, so the TE PGAM branch
        // stays additive here to match the original PSILIN formulation exactly.
        psi += hopi * (psig * sigte + pgam * gamte);

        // dPsi/dGam from TE panel
        state.StreamfunctionVortexSensitivity[jo] += -hopi * psig * scs * 0.5;
        state.StreamfunctionVortexSensitivity[jp] += hopi * psig * scs * 0.5;

        state.StreamfunctionVortexSensitivity[jo] += hopi * pgam * sds * 0.5;
        state.StreamfunctionVortexSensitivity[jp] += -hopi * pgam * sds * 0.5;

        // dPsi/dni from TE panel
        psiNi += hopi * (psigni * sigte + pgamni * gamte);

        // dQ/dGam from TE panel
        state.VelocityVortexSensitivity[jo] += -hopi * (psigni * 0.5 * scs - pgamni * 0.5 * sds);
        state.VelocityVortexSensitivity[jp] += hopi * (psigni * 0.5 * scs - pgamni * 0.5 * sds);
    }
}

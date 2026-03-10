using XFoil.Solver.Models;

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
                    jo, jp, jm, jq, n,
                    x1, x2, yy, g1, g2, t1, t2, rs1, rs2,
                    x1i, x2i, yyi, apan,
                    dsio, panel, state, quarterOverPi,
                    ref psi, ref psiNi);
            }

            // ---- Vortex panel contribution to PSI ----
            ComputeVortexContribution(
                jo, jp, n,
                x1, x2, yy, g1, g2, t1, t2, rs1, rs2,
                x1i, x2i, yyi,
                panel, state, quarterOverPi,
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

        return (psi, psiNi);
    }

    /// <summary>
    /// Computes the source contribution to PSI for both half-panels (1-0 and 0-2).
    /// Port of Fortran lines 235-334 in PSILIN.
    /// </summary>
    private static void ComputeSourceContribution(
        int jo, int jp, int jm, int jq, int n,
        double x1, double x2, double yy,
        double g1, double g2, double t1, double t2,
        double rs1, double rs2,
        double x1i, double x2i, double yyi,
        double apan, double dsio,
        LinearVortexPanelState panel, InviscidSolverState state,
        double qopi,
        ref double psi, ref double psiNi)
    {
        // Midpoint quantities
        double x0 = 0.5 * (x1 + x2);
        double rs0 = x0 * x0 + yy * yy;
        double g0 = Math.Log(rs0);
        // Use the same SGN logic implicitly (consistent with g1/g2 computation)
        // For airfoil surface points, SGN=1, so no correction needed for T0
        double t0 = Math.Atan2(x0, yy);

        // ---- First half-panel (1-0): from start node to midpoint ----
        {
            double dxInv = 1.0 / (x1 - x0);
            double psum = x0 * (t0 - apan) - x1 * (t1 - apan) + 0.5 * yy * (g1 - g0);
            double pdif = ((x1 + x0) * psum + rs1 * (t1 - apan) - rs0 * (t0 - apan)
                          + (x0 - x1) * yy) * dxInv;

            double psx1 = -(t1 - apan);
            double psx0 = t0 - apan;
            double psyy = 0.5 * (g1 - g0);

            double pdx1 = ((x1 + x0) * psx1 + psum + 2.0 * x1 * (t1 - apan) - pdif) * dxInv;
            double pdx0 = ((x1 + x0) * psx0 + psum - 2.0 * x0 * (t0 - apan) + pdif) * dxInv;
            double pdyy = ((x1 + x0) * psyy + 2.0 * (x0 - x1 + yy * (t1 - t0))) * dxInv;

            double dsm = Math.Sqrt(
                (panel.X[jp] - panel.X[jm]) * (panel.X[jp] - panel.X[jm]) +
                (panel.Y[jp] - panel.Y[jm]) * (panel.Y[jp] - panel.Y[jm]));
            double dsim = 1.0 / dsm;

            double ssum = (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio
                        + (state.SourceStrength[jp] - state.SourceStrength[jm]) * dsim;
            double sdif = (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio
                        - (state.SourceStrength[jp] - state.SourceStrength[jm]) * dsim;

            psi += qopi * (psum * ssum + pdif * sdif);

            // dPsi/dm
            state.StreamfunctionSourceSensitivity[jm] += qopi * (-psum * dsim + pdif * dsim);
            state.StreamfunctionSourceSensitivity[jo] += qopi * (-psum * dsio - pdif * dsio);
            state.StreamfunctionSourceSensitivity[jp] += qopi * (psum * (dsio + dsim) + pdif * (dsio - dsim));

            // dPsi/dni
            double psni = psx1 * x1i + psx0 * (x1i + x2i) * 0.5 + psyy * yyi;
            double pdni = pdx1 * x1i + pdx0 * (x1i + x2i) * 0.5 + pdyy * yyi;
            psiNi += qopi * (psni * ssum + pdni * sdif);

            // dQ/dm
            state.VelocitySourceSensitivity[jm] += qopi * (-psni * dsim + pdni * dsim);
            state.VelocitySourceSensitivity[jo] += qopi * (-psni * dsio - pdni * dsio);
            state.VelocitySourceSensitivity[jp] += qopi * (psni * (dsio + dsim) + pdni * (dsio - dsim));
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

            double pdx0 = ((x0 + x2) * psx0 + psum + 2.0 * x0 * (t0 - apan) - pdif) * dxInv;
            double pdx2 = ((x0 + x2) * psx2 + psum - 2.0 * x2 * (t2 - apan) + pdif) * dxInv;
            double pdyy = ((x0 + x2) * psyy + 2.0 * (x2 - x0 + yy * (t0 - t2))) * dxInv;

            double dsp = Math.Sqrt(
                (panel.X[jq] - panel.X[jo]) * (panel.X[jq] - panel.X[jo]) +
                (panel.Y[jq] - panel.Y[jo]) * (panel.Y[jq] - panel.Y[jo]));
            double dsip = 1.0 / dsp;

            double ssum = (state.SourceStrength[jq] - state.SourceStrength[jo]) * dsip
                        + (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio;
            double sdif = (state.SourceStrength[jq] - state.SourceStrength[jo]) * dsip
                        - (state.SourceStrength[jp] - state.SourceStrength[jo]) * dsio;

            psi += qopi * (psum * ssum + pdif * sdif);

            // dPsi/dm
            state.StreamfunctionSourceSensitivity[jo] += qopi * (-psum * (dsip + dsio) - pdif * (dsip - dsio));
            state.StreamfunctionSourceSensitivity[jp] += qopi * (psum * dsio - pdif * dsio);
            // Clamp jq to valid range (it may equal jp at the boundary)
            if (jq < n)
            {
                state.StreamfunctionSourceSensitivity[jq] += qopi * (psum * dsip + pdif * dsip);
            }

            // dPsi/dni
            double psni = psx0 * (x1i + x2i) * 0.5 + psx2 * x2i + psyy * yyi;
            double pdni = pdx0 * (x1i + x2i) * 0.5 + pdx2 * x2i + pdyy * yyi;
            psiNi += qopi * (psni * ssum + pdni * sdif);

            // dQ/dm
            state.VelocitySourceSensitivity[jo] += qopi * (-psni * (dsip + dsio) - pdni * (dsip - dsio));
            state.VelocitySourceSensitivity[jp] += qopi * (psni * dsio - pdni * dsio);
            if (jq < n)
            {
                state.VelocitySourceSensitivity[jq] += qopi * (psni * dsip + pdni * dsip);
            }
        }
    }

    /// <summary>
    /// Computes the vortex panel contribution to PSI (linear vorticity integrals).
    /// Port of Fortran lines 336-372 in PSILIN.
    /// </summary>
    private static void ComputeVortexContribution(
        int jo, int jp, int n,
        double x1, double x2, double yy,
        double g1, double g2, double t1, double t2,
        double rs1, double rs2,
        double x1i, double x2i, double yyi,
        LinearVortexPanelState panel, InviscidSolverState state,
        double qopi,
        ref double psi, ref double psiNi)
    {
        double dxInv = 1.0 / (x1 - x2);

        // Sum and difference integrals for linear vorticity
        double psis = 0.5 * x1 * g1 - 0.5 * x2 * g2 + x2 - x1 + yy * (t1 - t2);
        double psid = ((x1 + x2) * psis + 0.5 * (rs2 * g2 - rs1 * g1 + x1 * x1 - x2 * x2)) * dxInv;

        // Partial derivatives for tangential velocity
        double psx1 = 0.5 * g1;
        double psx2 = -0.5 * g2;
        double psyy = t1 - t2;

        double pdx1 = ((x1 + x2) * psx1 + psis - x1 * g1 - psid) * dxInv;
        double pdx2 = ((x1 + x2) * psx2 + psis + x2 * g2 + psid) * dxInv;
        double pdyy = ((x1 + x2) * psyy - yy * (g1 - g2)) * dxInv;

        // Vortex strength sums/differences
        double gsum = state.VortexStrength[jp] + state.VortexStrength[jo];
        double gdif = state.VortexStrength[jp] - state.VortexStrength[jo];

        // Accumulate PSI
        psi += qopi * (psis * gsum + psid * gdif);

        // dPsi/dGam
        state.StreamfunctionVortexSensitivity[jo] += qopi * (psis - psid);
        state.StreamfunctionVortexSensitivity[jp] += qopi * (psis + psid);

        // dPsi/dni (tangential velocity)
        double psni = psx1 * x1i + psx2 * x2i + psyy * yyi;
        double pdni = pdx1 * x1i + pdx2 * x2i + pdyy * yyi;
        psiNi += qopi * (gsum * psni + gdif * pdni);

        // dQ/dGam
        state.VelocityVortexSensitivity[jo] += qopi * (psni - pdni);
        state.VelocityVortexSensitivity[jp] += qopi * (psni + pdni);
    }

    /// <summary>
    /// Computes the TE panel contribution to PSI and sensitivity arrays.
    /// Port of Fortran lines 395-456 in PSILIN (label 11 through label 12).
    /// The TE panel uses the last-panel geometry (g1, g2, t1, t2, etc.) to compute
    /// source-like and vortex-like contributions from the TE gap.
    /// </summary>
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

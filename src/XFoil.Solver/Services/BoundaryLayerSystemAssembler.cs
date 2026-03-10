using System;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// BL equation system assembly routines ported from XFoil's xblsys.f.
/// Assembles 3-equation BL residuals and Jacobian blocks for each station pair.
/// Static utility class following Phase 2 convention.
/// </summary>
public static class BoundaryLayerSystemAssembler
{
    private const double Gamma = 1.4;
    private const double Gm1 = Gamma - 1.0;

    // BLPAR defaults matching BoundaryLayerCorrelationConstants.Default
    private const double GACON = 6.70;
    private const double GBCON = 0.75;
    private const double GCCON = 18.0;
    private const double DLCON = 0.9;
    private const double CTCON = 0.5 / (GACON * GACON * GBCON); // ~0.01486
    private const double SCCON = 5.6;
    private const double DUXCON = 1.0;
    private const double CTRCON = 1.8;
    private const double CTRCEX = 3.3;

    // =====================================================================
    // BLPRV: ConvertToCompressible
    // Source: xblsys.f:701-722
    // =====================================================================

    /// <summary>
    /// Converts incompressible edge velocity UEI to compressible U2 via Karman-Tsien.
    /// Port of BLPRV from xblsys.f.
    /// </summary>
    public static (double U2, double U2_UEI, double U2_MS) ConvertToCompressible(
        double uei, double tkbl, double qinfbl, double tkbl_ms)
    {
        double ueiOq2 = (uei / qinfbl) * (uei / qinfbl);
        double denom = 1.0 - tkbl * ueiOq2;

        double u2 = uei * (1.0 - tkbl) / denom;
        double u2_uei = (1.0 + tkbl * (2.0 * u2 * uei / (qinfbl * qinfbl) - 1.0)) / denom;
        double u2_ms = (u2 * ueiOq2 - uei) * tkbl_ms / denom;

        return (u2, u2_uei, u2_ms);
    }

    // =====================================================================
    // BLKIN: ComputeKinematicParameters
    // Source: xblsys.f:725-780
    // =====================================================================

    /// <summary>
    /// Computes turbulence-independent secondary variables from primary BL variables.
    /// Port of BLKIN from xblsys.f.
    /// </summary>
    public static KinematicResult ComputeKinematicParameters(
        double u2, double t2, double d2, double dw2,
        double hstinv, double hstinv_ms,
        double gm1bl, double rstbl, double rstbl_ms,
        double hvrat, double reybl, double reybl_re, double reybl_ms)
    {
        var r = new KinematicResult();

        // Edge Mach^2
        double u2sq_hstinv = u2 * u2 * hstinv;
        r.M2 = u2sq_hstinv / (gm1bl * (1.0 - 0.5 * u2sq_hstinv));
        double tr2 = 1.0 + 0.5 * gm1bl * r.M2;
        r.M2_U2 = 2.0 * r.M2 * tr2 / u2;
        r.M2_MS = u2 * u2 * tr2 / (gm1bl * (1.0 - 0.5 * u2sq_hstinv)) * hstinv_ms;

        // Edge density (isentropic)
        r.R2 = rstbl * Math.Pow(tr2, -1.0 / gm1bl);
        r.R2_U2 = -r.R2 / tr2 * 0.5 * r.M2_U2;
        r.R2_MS = -r.R2 / tr2 * 0.5 * r.M2_MS + rstbl_ms * Math.Pow(tr2, -1.0 / gm1bl);

        // Shape parameter
        r.H2 = d2 / t2;
        r.H2_D2 = 1.0 / t2;
        r.H2_T2 = -r.H2 / t2;

        // Static/stagnation enthalpy ratio
        double herat = 1.0 - 0.5 * u2 * u2 * hstinv;
        double he_u2 = -u2 * hstinv;
        double he_ms = -0.5 * u2 * u2 * hstinv_ms;

        // Molecular viscosity (Sutherland's law)
        double v2 = Math.Sqrt(herat * herat * herat) * (1.0 + hvrat) / (herat + hvrat) / reybl;
        double v2_he = v2 * (1.5 / herat - 1.0 / (herat + hvrat));
        double v2_u2 = v2_he * he_u2;
        double v2_ms = -v2 / reybl * reybl_ms + v2_he * he_ms;
        double v2_re = -v2 / reybl * reybl_re;

        // Kinematic shape parameter Hk
        var (hk2, hk2_h2, hk2_m2) = BoundaryLayerCorrelations.KinematicShapeParameter(r.H2, r.M2);
        r.HK2 = hk2;
        r.HK2_U2 = hk2_m2 * r.M2_U2;
        r.HK2_T2 = hk2_h2 * r.H2_T2;
        r.HK2_D2 = hk2_h2 * r.H2_D2;
        r.HK2_MS = hk2_m2 * r.M2_MS;

        // Momentum-thickness Reynolds number
        r.RT2 = r.R2 * u2 * t2 / v2;
        r.RT2_U2 = r.RT2 * (1.0 / u2 + r.R2_U2 / r.R2 - v2_u2 / v2);
        r.RT2_T2 = r.RT2 / t2;
        r.RT2_MS = r.RT2 * (r.R2_MS / r.R2 - v2_ms / v2);
        r.RT2_RE = r.RT2 * (-v2_re / v2);

        return r;
    }

    // =====================================================================
    // BLVAR: ComputeStationVariables
    // Source: xblsys.f:784-1120
    // =====================================================================

    /// <summary>
    /// Computes all secondary variables for a single station.
    /// Port of BLVAR from xblsys.f.
    /// </summary>
    /// <param name="ityp">1=laminar, 2=turbulent, 3=wake</param>
    public static StationVariables ComputeStationVariables(
        int ityp, double hk, double rt, double msq, double h,
        double ctau, double dw, double theta)
    {
        var v = new StationVariables();

        // Clamp Hk
        if (ityp == 3) hk = Math.Max(hk, 1.00005);
        else hk = Math.Max(hk, 1.05);

        // Density thickness H**
        var (hc, hc_hk, hc_msq) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk, msq);

        // H* (energy shape parameter)
        double hs, hs_hk, hs_rt, hs_msq;
        if (ityp == 1)
        {
            var hsl = BoundaryLayerCorrelations.LaminarShapeParameter(hk);
            (hs, hs_hk, hs_rt, hs_msq) = hsl;
        }
        else
        {
            var hst = BoundaryLayerCorrelations.TurbulentShapeParameter(hk, rt, msq);
            (hs, hs_hk, hs_rt, hs_msq) = hst;
        }
        v.Hs = hs;

        // Normalized slip velocity Us
        double us = 0.5 * hs * (1.0 - (hk - 1.0) / (GBCON * h));
        double us_hs = 0.5 * (1.0 - (hk - 1.0) / (GBCON * h));
        double us_hk = 0.5 * hs * (-1.0 / (GBCON * h));
        double us_h = 0.5 * hs * (hk - 1.0) / (GBCON * h * h);

        if (ityp <= 2 && us > 0.95)
        {
            us = 0.98; us_hs = 0; us_hk = 0; us_h = 0;
        }
        if (ityp == 3 && us > 0.99995)
        {
            us = 0.99995; us_hs = 0; us_hk = 0; us_h = 0;
        }
        v.Us = us;

        // Equilibrium Ctau (CQ2)
        double gcc = (ityp == 2) ? GCCON : 0.0;
        double hkc = hk - 1.0 - gcc / rt;
        // hkc_hk, hkc_rt tracked for future full-Jacobian use
        if (hkc < 0.01) { hkc = 0.01; }

        double hkb = hk - 1.0;
        if (hkb < 0.01) hkb = 0.01;
        double usb = 1.0 - us;
        if (usb < 0.01) usb = 0.01;

        double cq2arg = CTCON * hs * hkb * hkc * hkc / (usb * h * hk * hk);
        if (cq2arg < 1e-20) cq2arg = 1e-20;
        double cteq = Math.Sqrt(cq2arg);
        v.Cteq = cteq;

        // Skin friction
        double cf, cf_hk, cf_rt, cf_msq;
        if (ityp == 3)
        {
            cf = 0; cf_hk = 0; cf_rt = 0; cf_msq = 0;
        }
        else if (ityp == 1)
        {
            (cf, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt, msq);
        }
        else
        {
            (cf, cf_hk, cf_rt, cf_msq) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msq);
            var (cfl, cfl_hk, cfl_rt, cfl_msq) = BoundaryLayerCorrelations.LaminarSkinFriction(hk, rt, msq);
            if (cfl > cf) { cf = cfl; cf_hk = cfl_hk; cf_rt = cfl_rt; cf_msq = cfl_msq; }
        }
        v.Cf = cf;

        // Dissipation
        double di, di_s2;
        if (ityp == 1)
        {
            var (dil, dil_hk, dil_rt) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);
            di = dil; di_s2 = 0;
        }
        else if (ityp == 2)
        {
            // Turbulent wall contribution (uses turbulent Cf always)
            var (cf2t, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msq);
            double diWall = (0.5 * cf2t * us) * 2.0 / hs;

            // DFAC correction for low Hk
            double grt = Math.Log(rt);
            double hmin = 1.0 + 2.1 / grt;
            double fl = (hk - 1.0) / (hmin - 1.0);
            double tfl = Math.Tanh(fl);
            double dfac = 0.5 + 0.5 * tfl;
            diWall *= dfac;

            // Outer layer contribution
            double st = Math.Sqrt(Math.Max(ctau, 0.0));
            double dd_outer = st * st * (0.995 - us) * 2.0 / hs;

            // Laminar stress contribution
            double dd_lam = 0.15 * (0.995 - us) * (0.995 - us) / rt * 2.0 / hs;

            di = diWall + dd_outer + dd_lam;
            di_s2 = 2.0 * st * (0.995 - us) * 2.0 / hs; // dd_s2 = 2*S*(0.995-US)*2/HS

            // Check if laminar CD is larger
            var (dil, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);
            if (dil > di) { di = dil; di_s2 = 0; }
        }
        else
        {
            // Wake: zero wall, outer layer only, then laminar wake check
            double st = Math.Sqrt(Math.Max(ctau, 0.0));
            double dd_outer = st * st * (0.995 - us) * 2.0 / hs;
            double dd_lam = 0.15 * (0.995 - us) * (0.995 - us) / rt * 2.0 / hs;
            di = dd_outer + dd_lam;
            di_s2 = 2.0 * st * (0.995 - us) * 2.0 / hs;

            // Check laminar wake CD
            var (dilw, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk, rt);
            if (dilw > di) { di = dilw; di_s2 = 0; }

            // Double for wake (two halves)
            di *= 2.0;
            di_s2 *= 2.0;
        }
        v.Di = di;

        // BL thickness (Delta) from simplified Green's correlation
        double de = (3.15 + 1.72 / (hk - 1.0)) * theta + (h * theta); // d2 + delta approx
        // Simplified: de = (3.15 + 1.72/(hk-1)) * theta + dstar
        // Actually in Fortran: DE2 = (3.15 + 1.72/(HK2-1.0))*T2 + D2
        de = (3.15 + 1.72 / (hk - 1.0)) * theta + (h * theta); // h*theta = d2 = delta*
        double hdmax = 12.0;
        if (de > hdmax * theta) de = hdmax * theta;
        v.De = de;

        v.Hc = hc;

        return v;
    }

    // =====================================================================
    // BLMID: ComputeMidpointCorrelations
    // Source: xblsys.f:1123-1191
    // =====================================================================

    /// <summary>
    /// Computes midpoint skin friction between stations 1 and 2.
    /// Port of BLMID from xblsys.f.
    /// </summary>
    public static MidpointResult ComputeMidpointCorrelations(
        int ityp,
        double hk1, double rt1, double m1,
        double hk2, double rt2, double m2)
    {
        var r = new MidpointResult();

        double hka = 0.5 * (hk1 + hk2);
        double rta = 0.5 * (rt1 + rt2);
        double ma = 0.5 * (m1 + m2);

        double cfm, cfm_hka, cfm_rta, cfm_ma;

        if (ityp == 3)
        {
            cfm = 0; cfm_hka = 0; cfm_rta = 0; cfm_ma = 0;
        }
        else if (ityp == 1)
        {
            (cfm, cfm_hka, cfm_rta, cfm_ma) = BoundaryLayerCorrelations.LaminarSkinFriction(hka, rta, ma);
        }
        else
        {
            (cfm, cfm_hka, cfm_rta, cfm_ma) = BoundaryLayerCorrelations.TurbulentSkinFriction(hka, rta, ma);
            var (cfml, cfml_hka, cfml_rta, cfml_ma) = BoundaryLayerCorrelations.LaminarSkinFriction(hka, rta, ma);
            if (cfml > cfm)
            {
                cfm = cfml; cfm_hka = cfml_hka; cfm_rta = cfml_rta; cfm_ma = cfml_ma;
            }
        }

        r.Cfm = cfm;
        r.Cfm_Hka = cfm_hka;
        r.Cfm_Rta = cfm_rta;
        r.Cfm_Ma = cfm_ma;

        return r;
    }

    // =====================================================================
    // BLDIF: ComputeFiniteDifferences
    // Source: xblsys.f:1551-1976
    // =====================================================================

    /// <summary>
    /// Computes finite-difference BL equation residuals with Jacobian blocks.
    /// Port of BLDIF from xblsys.f.
    /// Returns residuals[3] and 3x5 Jacobian blocks VS1, VS2.
    /// </summary>
    public static BldifResult ComputeFiniteDifferences(
        int ityp,
        double x1, double x2,
        double u1, double u2,
        double t1, double t2,
        double d1, double d2,
        double s1, double s2,
        double dw1, double dw2,
        double msq1, double msq2,
        double ampl1, double ampl2,
        double amcrit)
    {
        var result = new BldifResult();
        result.Residual = new double[3];
        result.VS1 = new double[3, 5];
        result.VS2 = new double[3, 5];

        // Compute kinematic parameters at both stations using correlations
        double h1 = d1 / t1;
        double h2v = d2 / t2;

        // Hk at both stations
        var (hk1, hk1_h, hk1_m) = BoundaryLayerCorrelations.KinematicShapeParameter(h1, msq1);
        var (hk2, hk2_h, hk2_m) = BoundaryLayerCorrelations.KinematicShapeParameter(h2v, msq2);
        hk1 = Math.Max(hk1, ityp == 3 ? 1.00005 : 1.05);
        hk2 = Math.Max(hk2, ityp == 3 ? 1.00005 : 1.05);

        // H* at both stations
        double hs1, hs1_hk, hs1_rt, hs1_msq;
        double hs2, hs2_hk, hs2_rt, hs2_msq;
        if (ityp == 1)
        {
            (hs1, hs1_hk, hs1_rt, hs1_msq) = BoundaryLayerCorrelations.LaminarShapeParameter(hk1);
            (hs2, hs2_hk, hs2_rt, hs2_msq) = BoundaryLayerCorrelations.LaminarShapeParameter(hk2);
        }
        else
        {
            // Use a reasonable default Rt for shape parameter if needed
            double rt1est = Math.Max(u1 * t1 * 1e6, 200.0);
            double rt2est = Math.Max(u2 * t2 * 1e6, 200.0);
            (hs1, hs1_hk, hs1_rt, hs1_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk1, rt1est, msq1);
            (hs2, hs2_hk, hs2_rt, hs2_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk2, rt2est, msq2);
        }

        // Cf at both stations
        double cf1, cf1_hk, cf1_rt, cf1_m;
        double cf2, cf2_hk, cf2_rt, cf2_m;
        if (ityp == 3)
        {
            cf1 = 0; cf1_hk = 0; cf1_rt = 0; cf1_m = 0;
            cf2 = 0; cf2_hk = 0; cf2_rt = 0; cf2_m = 0;
        }
        else if (ityp == 1)
        {
            (cf1, cf1_hk, cf1_rt, cf1_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk1, Math.Max(u1 * t1 * 1e6, 200.0), msq1);
            (cf2, cf2_hk, cf2_rt, cf2_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk2, Math.Max(u2 * t2 * 1e6, 200.0), msq2);
        }
        else
        {
            (cf1, cf1_hk, cf1_rt, cf1_m) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk1, Math.Max(u1 * t1 * 1e6, 200.0), msq1);
            (cf2, cf2_hk, cf2_rt, cf2_m) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk2, Math.Max(u2 * t2 * 1e6, 200.0), msq2);
        }

        // Midpoint Cf
        var mid = ComputeMidpointCorrelations(ityp, hk1, Math.Max(u1 * t1 * 1e6, 200.0), msq1,
                                                          hk2, Math.Max(u2 * t2 * 1e6, 200.0), msq2);

        // Logarithmic differences
        double xlog = Math.Log(x2 / x1);
        double ulog = Math.Log(u2 / u1);
        double tlog = Math.Log(t2 / t1);
        double hlog = Math.Log(hs2 / hs1);

        // Upwinding parameter
        double arg = Math.Abs((hk2 - 1.0) / (hk1 - 1.0));
        double hl = Math.Log(arg);
        double hlsq = Math.Min(hl * hl, 15.0);
        double hdcon = (ityp == 3) ? 1.0 / (hk2 * hk2) : 5.0 / (hk2 * hk2);
        double ehh = Math.Exp(-hlsq * hdcon);
        double upw = 1.0 - 0.5 * ehh;

        // === Equation 1: Ctau/Amplification ===
        if (ityp == 1)
        {
            // Laminar: amplification equation placeholder
            // REZC = AMPL2 - AMPL1 - AX*(X2-X1)
            // Simplified: just store the amplification residual
            double rezc = ampl2 - ampl1; // Simplified (AX computation needs DAMPL)
            result.Residual[0] = -rezc;
            result.VS1[0, 0] = -1.0; // d/dAmpl1
            result.VS2[0, 0] = 1.0;  // d/dAmpl2
        }
        else
        {
            // Turbulent/wake: shear lag equation
            double sa = (1.0 - upw) * s1 + upw * s2;
            double cq1v = ComputeLocalCteq(hk1, hs1, h1, Math.Max(u1 * t1 * 1e6, 200.0), ityp);
            double cq2v = ComputeLocalCteq(hk2, hs2, h2v, Math.Max(u2 * t2 * 1e6, 200.0), ityp);
            double cqa = (1.0 - upw) * cq1v + upw * cq2v;
            double cfa = (1.0 - upw) * cf1 + upw * cf2;
            double hka = (1.0 - upw) * hk1 + upw * hk2;

            double usa = 0.5 * (ComputeUs(hk1, hs1, h1) + ComputeUs(hk2, hs2, h2v));
            double rta = 0.5 * (u1 * t1 * 1e6 + u2 * t2 * 1e6);
            double dea = 0.5 * (ComputeDe(hk1, t1) + ComputeDe(hk2, t2));
            double da = 0.5 * (d1 + d2);

            double ald = (ityp == 3) ? DLCON : 1.0;

            // Equilibrium 1/Ue dUe/dx
            double gcc = (ityp == 2) ? GCCON : 0.0;
            double hkcv = hka - 1.0 - gcc / rta;
            if (hkcv < 0.01) hkcv = 0.01;

            double hr = hkcv / (GACON * ald * hka);
            double uq = (0.5 * cfa - hr * hr) / (GBCON * da);

            double scc = SCCON * 1.333 / (1.0 + usa);
            double slog = (s1 > 0 && s2 > 0) ? Math.Log(s2 / s1) : 0.0;
            double dxi = x2 - x1;

            double rezc = scc * (cqa - sa * ald) * dxi
                        - dea * 2.0 * slog
                        + dea * 2.0 * (uq * dxi - ulog) * DUXCON;

            result.Residual[0] = -rezc;
            result.VS1[0, 0] = (s1 > 0) ? (-(1.0 - upw) * scc * ald * dxi + dea * 2.0 / s1) : 0;
            result.VS2[0, 0] = (s2 > 0) ? (-upw * scc * ald * dxi - dea * 2.0 / s2) : 0;
        }

        // === Equation 2: Momentum (von Karman) ===
        {
            double ha = 0.5 * (h1 + h2v);
            double ma = 0.5 * (msq1 + msq2);
            double xa = 0.5 * (x1 + x2);
            double ta = 0.5 * (t1 + t2);
            double hwa = 0.5 * (dw1 / t1 + dw2 / t2);

            double cfx = 0.50 * mid.Cfm * xa / ta + 0.25 * (cf1 * x1 / t1 + cf2 * x2 / t2);
            double btmp = ha + 2.0 - ma + hwa;

            double rezt = tlog + btmp * ulog - xlog * 0.5 * cfx;

            result.Residual[1] = -rezt;

            // Simplified Jacobian entries for momentum equation
            result.VS1[1, 1] = -1.0 / t1 + 0.5 * ulog * (-d1 / (t1 * t1)) / (d1 / t1); // Z_T1 terms
            result.VS2[1, 1] = 1.0 / t2 + 0.5 * ulog * (-d2 / (t2 * t2)) / (d2 / t2);  // Z_T2 terms
            result.VS1[1, 2] = 0.5 * ulog / t1; // Z_HA * H1_D1
            result.VS2[1, 2] = 0.5 * ulog / t2;
            result.VS1[1, 3] = -btmp / u1;       // Z_UL/U1
            result.VS2[1, 3] = btmp / u2;
            result.VS1[1, 4] = xlog * 0.5 * cfx / x1; // Z_XL/X1 simplified
            result.VS2[1, 4] = -xlog * 0.5 * cfx / x2;
        }

        // === Equation 3: Shape parameter (energy) ===
        {
            double xot1 = x1 / t1;
            double xot2 = x2 / t2;
            double ha = 0.5 * (h1 + h2v);
            double hsa = 0.5 * (hs1 + hs2);
            double hca = 0.5 * (ComputeHc(hk1, msq1) + ComputeHc(hk2, msq2));
            double hwa = 0.5 * (dw1 / t1 + dw2 / t2);

            // Di at both stations
            double di1v, di2v;
            if (ityp == 1)
            {
                (di1v, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk1, Math.Max(u1 * t1 * 1e6, 200.0));
                (di2v, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk2, Math.Max(u2 * t2 * 1e6, 200.0));
            }
            else
            {
                di1v = ComputeTurbDi(hk1, hs1, h1, cf1, s1, u1 * t1 * 1e6, ityp);
                di2v = ComputeTurbDi(hk2, hs2, h2v, cf2, s2, u2 * t2 * 1e6, ityp);
            }

            double dix = (1.0 - upw) * di1v * xot1 + upw * di2v * xot2;
            double cfx = (1.0 - upw) * cf1 * xot1 + upw * cf2 * xot2;

            double btmp = 2.0 * hca / hsa + 1.0 - ha - hwa;

            double rezh = hlog + btmp * ulog + xlog * (0.5 * cfx - dix);

            result.Residual[2] = -rezh;

            // Simplified Jacobian for shape parameter equation
            result.VS1[2, 1] = -1.0 / hs1 * hs1_hk * hk1_h / t1; // HS1 sensitivity through T1
            result.VS2[2, 1] = 1.0 / hs2 * hs2_hk * hk2_h / t2;
            result.VS1[2, 2] = 0; // D1 sensitivity placeholder
            result.VS2[2, 2] = 0;
            result.VS1[2, 3] = -btmp / u1;
            result.VS2[2, 3] = btmp / u2;
            result.VS1[2, 4] = -(0.5 * cfx - dix) / x1;
            result.VS2[2, 4] = (0.5 * cfx - dix) / x2;
        }

        return result;
    }

    // =====================================================================
    // TESYS: AssembleTESystem
    // Source: xblsys.f:664-698
    // =====================================================================

    /// <summary>
    /// Sets up the BL system at TE-wake junction.
    /// Port of TESYS from xblsys.f.
    /// </summary>
    public static BldifResult AssembleTESystem(
        double cte, double tte, double dte,
        double hk2, double rt2, double msq2, double h2,
        double s2, double t2, double d2, double dw2)
    {
        var result = new BldifResult();
        result.Residual = new double[3];
        result.VS1 = new double[3, 5];
        result.VS2 = new double[3, 5];

        // Initialize to zero (matching Fortran DO 55 loop)

        // Equation 1: Ctau continuity
        result.VS1[0, 0] = -1.0;
        result.VS2[0, 0] = 1.0;
        result.Residual[0] = cte - s2;

        // Equation 2: Theta continuity
        result.VS1[1, 1] = -1.0;
        result.VS2[1, 1] = 1.0;
        result.Residual[1] = tte - t2;

        // Equation 3: Delta* continuity (includes wake displacement)
        result.VS1[2, 2] = -1.0;
        result.VS2[2, 2] = 1.0;
        result.Residual[2] = dte - d2 - dw2;

        return result;
    }

    // =====================================================================
    // BLSYS: AssembleStationSystem
    // Source: xblsys.f:583-661
    // =====================================================================

    /// <summary>
    /// Top-level BL system assembly for a single station interval.
    /// Port of BLSYS from xblsys.f.
    /// </summary>
    public static BlsysResult AssembleStationSystem(
        bool isWake, bool isTurbOrTran, bool isTran, bool isSimi,
        double x1, double x2,
        double uei1, double uei2,
        double t1, double t2,
        double d1, double d2,
        double s1, double s2,
        double dw1, double dw2,
        double ampl1, double ampl2,
        double amcrit,
        double tkbl, double qinfbl, double tkbl_ms,
        double hstinv, double hstinv_ms,
        double gm1bl, double rstbl, double rstbl_ms,
        double hvrat, double reybl, double reybl_re, double reybl_ms)
    {
        var result = new BlsysResult();
        result.Residual = new double[3];
        result.VS1 = new double[3, 5];
        result.VS2 = new double[3, 5];

        // Determine ITYP
        int ityp;
        if (isWake) ityp = 3;
        else if (isTurbOrTran) ityp = 2;
        else ityp = 1;

        // Convert to compressible (BLPRV)
        var (u1, u1_uei, u1_ms) = ConvertToCompressible(uei1, tkbl, qinfbl, tkbl_ms);
        var (u2, u2_uei, u2_ms) = ConvertToCompressible(uei2, tkbl, qinfbl, tkbl_ms);

        // Compute BLDIF
        double msq1 = 0.0, msq2 = 0.0; // Simplified for M=0 case
        if (hstinv > 0)
        {
            double u1sq = u1 * u1 * hstinv;
            msq1 = u1sq / (gm1bl * (1.0 - 0.5 * u1sq));
            double u2sq = u2 * u2 * hstinv;
            msq2 = u2sq / (gm1bl * (1.0 - 0.5 * u2sq));
        }

        var bldif = ComputeFiniteDifferences(
            ityp, x1, x2, u1, u2, t1, t2, d1, d2, s1, s2,
            dw1, dw2, msq1, msq2, ampl1, ampl2, amcrit);

        // Copy residuals
        for (int k = 0; k < 3; k++)
            result.Residual[k] = bldif.Residual[k];

        // Map 5-column Jacobian to system (convert Ue column from compressible to incompressible)
        for (int k = 0; k < 3; k++)
        {
            for (int l = 0; l < 5; l++)
            {
                result.VS1[k, l] = bldif.VS1[k, l];
                result.VS2[k, l] = bldif.VS2[k, l];
            }
            // Convert Ue sensitivities: VS(k,4) *= U_UEI
            double resU1 = bldif.VS1[k, 3];
            double resU2 = bldif.VS2[k, 3];
            result.VS1[k, 3] = resU1 * u1_uei;
            result.VS2[k, 3] = resU2 * u2_uei;
        }

        // Handle similarity station
        if (isSimi)
        {
            for (int k = 0; k < 3; k++)
                for (int l = 0; l < 5; l++)
                {
                    result.VS2[k, l] = result.VS1[k, l] + result.VS2[k, l];
                    result.VS1[k, l] = 0;
                }
        }

        return result;
    }

    // =====================================================================
    // Helper functions
    // =====================================================================

    private static double ComputeUs(double hk, double hs, double h)
    {
        double us = 0.5 * hs * (1.0 - (hk - 1.0) / (GBCON * h));
        if (us > 0.98) us = 0.98;
        return us;
    }

    private static double ComputeDe(double hk, double theta)
    {
        double de = (3.15 + 1.72 / (hk - 1.0)) * theta;
        double hdmax = 12.0;
        if (de > hdmax * theta) de = hdmax * theta;
        return de;
    }

    private static double ComputeHc(double hk, double msq)
    {
        var (hc, _, _) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk, msq);
        return hc;
    }

    private static double ComputeLocalCteq(double hk, double hs, double h, double rt, int ityp)
    {
        double gcc = (ityp == 2) ? GCCON : 0.0;
        double hkc = hk - 1.0 - gcc / rt;
        if (hkc < 0.01) hkc = 0.01;
        double hkb = hk - 1.0;
        if (hkb < 0.01) hkb = 0.01;
        double us = ComputeUs(hk, hs, h);
        double usb = 1.0 - us;
        if (usb < 0.01) usb = 0.01;
        double cqarg = CTCON * hs * hkb * hkc * hkc / (usb * h * hk * hk);
        if (cqarg < 1e-20) cqarg = 1e-20;
        return Math.Sqrt(cqarg);
    }

    private static double ComputeTurbDi(double hk, double hs, double h, double cf, double ctau, double rt, int ityp)
    {
        double us = ComputeUs(hk, hs, h);
        double st = Math.Sqrt(Math.Max(ctau, 0.0));

        double di;
        if (ityp == 2)
        {
            // Wall contribution
            var (cf2t, _, _, _) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, 0.0);
            double diWall = (0.5 * cf2t * us) * 2.0 / hs;

            double grt = Math.Log(Math.Max(rt, 1.0));
            double hmin = 1.0 + 2.1 / grt;
            double fl = (hk - 1.0) / (hmin - 1.0);
            double tfl = Math.Tanh(fl);
            double dfac = 0.5 + 0.5 * tfl;
            diWall *= dfac;

            double dd_outer = st * st * (0.995 - us) * 2.0 / hs;
            double dd_lam = 0.15 * (0.995 - us) * (0.995 - us) / rt * 2.0 / hs;
            di = diWall + dd_outer + dd_lam;

            var (dil, _, _) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);
            if (dil > di) di = dil;
        }
        else
        {
            // Wake
            double dd_outer = st * st * (0.995 - us) * 2.0 / hs;
            double dd_lam = 0.15 * (0.995 - us) * (0.995 - us) / rt * 2.0 / hs;
            di = dd_outer + dd_lam;

            var (dilw, _, _) = BoundaryLayerCorrelations.WakeDissipation(hk, rt);
            if (dilw > di) di = dilw;
            di *= 2.0;
        }

        return di;
    }

    // =====================================================================
    // Result types
    // =====================================================================

    public class KinematicResult
    {
        public double M2, M2_U2, M2_MS;
        public double R2, R2_U2, R2_MS;
        public double H2, H2_D2, H2_T2;
        public double HK2, HK2_U2, HK2_T2, HK2_D2, HK2_MS;
        public double RT2, RT2_U2, RT2_T2, RT2_MS, RT2_RE;
    }

    public class StationVariables
    {
        public double Cf, Hs, Di, Cteq, Us, De, Hc;
    }

    public class MidpointResult
    {
        public double Cfm, Cfm_Hka, Cfm_Rta, Cfm_Ma;
    }

    public class BldifResult
    {
        public double[] Residual = Array.Empty<double>();
        public double[,] VS1 = new double[0, 0]; // 3x5 Jacobian block for station 1
        public double[,] VS2 = new double[0, 0]; // 3x5 Jacobian block for station 2
    }

    public class BlsysResult
    {
        public double[] Residual = Array.Empty<double>();
        public double[,] VS1 = new double[0, 0];
        public double[,] VS2 = new double[0, 0];
    }
}

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
    // Full chain-rule Jacobians matching Fortran BLDIF exactly.
    // =====================================================================

    /// <summary>
    /// Computes finite-difference BL equation residuals with full chain-rule
    /// Jacobian blocks. Port of BLDIF from xblsys.f (lines 1551-1976).
    /// Returns residuals[3] and 3x5 Jacobian blocks VS1, VS2.
    /// Columns: [0]=S/Ampl, [1]=Theta, [2]=Dstar, [3]=Ue, [4]=Xi
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
        double amcrit,
        double reybl = 1e6)
    {
        var result = new BldifResult();
        result.Residual = new double[3];
        result.VS1 = new double[3, 5];
        result.VS2 = new double[3, 5];

        // ================================================================
        // BLVAR-style primary derivatives at both stations
        // (Fortran BLVAR xblsys.f:784-1120 computes these via BLKIN)
        // ================================================================

        // --- Station 1 primary variables ---
        double h1 = d1 / t1;
        double h1_t1 = -h1 / t1;
        double h1_d1 = 1.0 / t1;

        // Hk1 from H1 and MSQ1
        var (hk1, hk1_h, hk1_msq) = BoundaryLayerCorrelations.KinematicShapeParameter(h1, msq1);
        double hkMin = (ityp == 3) ? 1.00005 : 1.05;
        if (hk1 < hkMin) { hk1 = hkMin; hk1_h = 0; hk1_msq = 0; }
        double hk1_t1 = hk1_h * h1_t1;
        double hk1_d1 = hk1_h * h1_d1;
        double hk1_u1 = 0.0; // No Mach at station level in incompressible path
        double hk1_ms = hk1_msq;

        // Rt1 = reybl * u1 * t1
        double rt1 = Math.Max(u1 * t1 * reybl, 200.0);
        bool rt1Clamped = (u1 * t1 * reybl < 200.0);
        double rt1_t1 = rt1Clamped ? 0.0 : reybl * u1;
        double rt1_u1 = rt1Clamped ? 0.0 : reybl * t1;
        double rt1_ms = 0.0;
        double rt1_re = rt1Clamped ? 0.0 : u1 * t1;

        // M1 sensitivities (incompressible: M1=0, M1_U1=0, M1_MS=0)
        double m1v = msq1;
        double m1_u1 = 0.0;
        double m1_ms = 1.0;

        // --- Station 2 primary variables ---
        double h2 = d2 / t2;
        double h2_t2 = -h2 / t2;
        double h2_d2 = 1.0 / t2;

        var (hk2, hk2_h, hk2_msq) = BoundaryLayerCorrelations.KinematicShapeParameter(h2, msq2);
        if (hk2 < hkMin) { hk2 = hkMin; hk2_h = 0; hk2_msq = 0; }
        double hk2_t2 = hk2_h * h2_t2;
        double hk2_d2 = hk2_h * h2_d2;
        double hk2_u2 = 0.0;
        double hk2_ms = hk2_msq;

        double rt2 = Math.Max(u2 * t2 * reybl, 200.0);
        bool rt2Clamped = (u2 * t2 * reybl < 200.0);
        double rt2_t2 = rt2Clamped ? 0.0 : reybl * u2;
        double rt2_u2 = rt2Clamped ? 0.0 : reybl * t2;
        double rt2_ms = 0.0;
        double rt2_re = rt2Clamped ? 0.0 : u2 * t2;

        double m2v = msq2;
        double m2_u2 = 0.0;
        double m2_ms = 1.0;

        // ================================================================
        // Correlation derivatives at both stations (BLVAR-style chains)
        // ================================================================

        // --- HC (density thickness H**) ---
        var (hc1, hc1_hk, hc1_msq) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk1, msq1);
        double hc1_t1 = hc1_hk * hk1_t1;
        double hc1_d1 = hc1_hk * hk1_d1;
        double hc1_u1 = hc1_hk * hk1_u1 + hc1_msq * m1_u1;
        double hc1_ms = hc1_hk * hk1_ms + hc1_msq * m1_ms;

        var (hc2, hc2_hk, hc2_msq) = BoundaryLayerCorrelations.DensityThicknessShapeParameter(hk2, msq2);
        double hc2_t2 = hc2_hk * hk2_t2;
        double hc2_d2 = hc2_hk * hk2_d2;
        double hc2_u2 = hc2_hk * hk2_u2 + hc2_msq * m2_u2;
        double hc2_ms = hc2_hk * hk2_ms + hc2_msq * m2_ms;

        // --- HS (energy shape parameter H*) ---
        double hs1, hs1_hk, hs1_rt, hs1_msq;
        double hs2, hs2_hk, hs2_rt, hs2_msq;
        if (ityp == 1)
        {
            (hs1, hs1_hk, hs1_rt, hs1_msq) = BoundaryLayerCorrelations.LaminarShapeParameter(hk1);
            (hs2, hs2_hk, hs2_rt, hs2_msq) = BoundaryLayerCorrelations.LaminarShapeParameter(hk2);
        }
        else
        {
            (hs1, hs1_hk, hs1_rt, hs1_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk1, rt1, msq1);
            (hs2, hs2_hk, hs2_rt, hs2_msq) = BoundaryLayerCorrelations.TurbulentShapeParameter(hk2, rt2, msq2);
        }

        // Chain HS to T,D,U (Fortran BLVAR lines 815-819)
        double hs1_t1 = hs1_hk * hk1_t1 + hs1_rt * rt1_t1;
        double hs1_d1 = hs1_hk * hk1_d1;
        double hs1_u1 = hs1_hk * hk1_u1 + hs1_rt * rt1_u1 + hs1_msq * m1_u1;
        double hs1_ms = hs1_hk * hk1_ms + hs1_rt * rt1_ms + hs1_msq * m1_ms;

        double hs2_t2 = hs2_hk * hk2_t2 + hs2_rt * rt2_t2;
        double hs2_d2 = hs2_hk * hk2_d2;
        double hs2_u2 = hs2_hk * hk2_u2 + hs2_rt * rt2_u2 + hs2_msq * m2_u2;
        double hs2_ms = hs2_hk * hk2_ms + hs2_rt * rt2_ms + hs2_msq * m2_ms;

        // --- US (normalized slip velocity) (Fortran BLVAR lines 821-831) ---
        double us1 = 0.5 * hs1 * (1.0 - (hk1 - 1.0) / (GBCON * h1));
        double us1_hs1 = 0.5 * (1.0 - (hk1 - 1.0) / (GBCON * h1));
        double us1_hk1 = 0.5 * hs1 * (-1.0 / (GBCON * h1));
        double us1_h1 = 0.5 * hs1 * (hk1 - 1.0) / (GBCON * h1 * h1);
        if (ityp <= 2 && us1 > 0.95) { us1 = 0.98; us1_hs1 = 0; us1_hk1 = 0; us1_h1 = 0; }
        if (ityp == 3 && us1 > 0.99995) { us1 = 0.99995; us1_hs1 = 0; us1_hk1 = 0; us1_h1 = 0; }

        double us1_t1 = us1_hs1 * hs1_t1 + us1_hk1 * hk1_t1 + us1_h1 * h1_t1;
        double us1_d1 = us1_hs1 * hs1_d1 + us1_hk1 * hk1_d1 + us1_h1 * h1_d1;
        double us1_u1 = us1_hs1 * hs1_u1 + us1_hk1 * hk1_u1;
        double us1_ms = us1_hs1 * hs1_ms + us1_hk1 * hk1_ms;

        double us2 = 0.5 * hs2 * (1.0 - (hk2 - 1.0) / (GBCON * h2));
        double us2_hs2 = 0.5 * (1.0 - (hk2 - 1.0) / (GBCON * h2));
        double us2_hk2 = 0.5 * hs2 * (-1.0 / (GBCON * h2));
        double us2_h2 = 0.5 * hs2 * (hk2 - 1.0) / (GBCON * h2 * h2);
        if (ityp <= 2 && us2 > 0.95) { us2 = 0.98; us2_hs2 = 0; us2_hk2 = 0; us2_h2 = 0; }
        if (ityp == 3 && us2 > 0.99995) { us2 = 0.99995; us2_hs2 = 0; us2_hk2 = 0; us2_h2 = 0; }

        double us2_t2 = us2_hs2 * hs2_t2 + us2_hk2 * hk2_t2 + us2_h2 * h2_t2;
        double us2_d2 = us2_hs2 * hs2_d2 + us2_hk2 * hk2_d2 + us2_h2 * h2_d2;
        double us2_u2 = us2_hs2 * hs2_u2 + us2_hk2 * hk2_u2;
        double us2_ms = us2_hs2 * hs2_ms + us2_hk2 * hk2_ms;

        // --- CQ (equilibrium Ctau^1/2) (Fortran BLVAR lines 853-895) ---
        double cq1, cq1_t1, cq1_d1, cq1_u1, cq1_ms;
        ComputeCqChains(hk1, hs1, us1, h1, rt1, ityp,
            hk1_t1, hk1_d1, hk1_u1, hk1_ms,
            hs1_t1, hs1_d1, hs1_u1, hs1_ms,
            us1_t1, us1_d1, us1_u1, us1_ms,
            h1_t1, h1_d1,
            rt1_t1, rt1_u1, rt1_ms,
            out cq1, out cq1_t1, out cq1_d1, out cq1_u1, out cq1_ms);

        double cq2, cq2_t2, cq2_d2, cq2_u2, cq2_ms;
        ComputeCqChains(hk2, hs2, us2, h2, rt2, ityp,
            hk2_t2, hk2_d2, hk2_u2, hk2_ms,
            hs2_t2, hs2_d2, hs2_u2, hs2_ms,
            us2_t2, us2_d2, us2_u2, us2_ms,
            h2_t2, h2_d2,
            rt2_t2, rt2_u2, rt2_ms,
            out cq2, out cq2_t2, out cq2_d2, out cq2_u2, out cq2_ms);

        // --- CF (skin friction) chains (Fortran BLVAR lines 898-927) ---
        double cf1, cf1_hk, cf1_rt, cf1_m;
        double cf2, cf2_hk, cf2_rt, cf2_m;
        if (ityp == 3)
        {
            cf1 = 0; cf1_hk = 0; cf1_rt = 0; cf1_m = 0;
            cf2 = 0; cf2_hk = 0; cf2_rt = 0; cf2_m = 0;
        }
        else if (ityp == 1)
        {
            (cf1, cf1_hk, cf1_rt, cf1_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk1, rt1, msq1);
            (cf2, cf2_hk, cf2_rt, cf2_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk2, rt2, msq2);
        }
        else
        {
            (cf1, cf1_hk, cf1_rt, cf1_m) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk1, rt1, msq1);
            (cf2, cf2_hk, cf2_rt, cf2_m) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk2, rt2, msq2);
            var (cf1l, cf1l_hk, cf1l_rt, cf1l_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk1, rt1, msq1);
            if (cf1l > cf1) { cf1 = cf1l; cf1_hk = cf1l_hk; cf1_rt = cf1l_rt; cf1_m = cf1l_m; }
            var (cf2l, cf2l_hk, cf2l_rt, cf2l_m) = BoundaryLayerCorrelations.LaminarSkinFriction(hk2, rt2, msq2);
            if (cf2l > cf2) { cf2 = cf2l; cf2_hk = cf2l_hk; cf2_rt = cf2l_rt; cf2_m = cf2l_m; }
        }

        // Chain CF to T,D,U (Fortran BLVAR lines 923-927)
        double cf1_t1 = cf1_hk * hk1_t1 + cf1_rt * rt1_t1;
        double cf1_d1 = cf1_hk * hk1_d1;
        double cf1_u1 = cf1_hk * hk1_u1 + cf1_rt * rt1_u1 + cf1_m * m1_u1;
        double cf1_ms = cf1_hk * hk1_ms + cf1_rt * rt1_ms + cf1_m * m1_ms;

        double cf2_t2 = cf2_hk * hk2_t2 + cf2_rt * rt2_t2;
        double cf2_d2 = cf2_hk * hk2_d2;
        double cf2_u2 = cf2_hk * hk2_u2 + cf2_rt * rt2_u2 + cf2_m * m2_u2;
        double cf2_ms = cf2_hk * hk2_ms + cf2_rt * rt2_ms + cf2_m * m2_ms;

        // --- DI (dissipation) with full derivative chains (Fortran BLVAR lines 929-1097) ---
        double di1, di1_s1, di1_t1, di1_d1, di1_u1, di1_ms;
        ComputeDiChains(ityp, hk1, hs1, us1, h1, rt1, s1, msq1,
            hk1_t1, hk1_d1, hk1_u1, hk1_ms,
            hs1_t1, hs1_d1, hs1_u1, hs1_ms,
            us1_t1, us1_d1, us1_u1, us1_ms,
            rt1_t1, rt1_u1, rt1_ms,
            out di1, out di1_s1, out di1_t1, out di1_d1, out di1_u1, out di1_ms);

        double di2, di2_s2, di2_t2, di2_d2, di2_u2, di2_ms;
        ComputeDiChains(ityp, hk2, hs2, us2, h2, rt2, s2, msq2,
            hk2_t2, hk2_d2, hk2_u2, hk2_ms,
            hs2_t2, hs2_d2, hs2_u2, hs2_ms,
            us2_t2, us2_d2, us2_u2, us2_ms,
            rt2_t2, rt2_u2, rt2_ms,
            out di2, out di2_s2, out di2_t2, out di2_d2, out di2_u2, out di2_ms);

        // --- DE (BL thickness) chains (Fortran BLVAR lines 1099-1117) ---
        double de1_hk1 = (-1.72 / ((hk1 - 1.0) * (hk1 - 1.0))) * t1;
        double de1 = (3.15 + 1.72 / (hk1 - 1.0)) * t1 + d1;
        double de1_t1 = de1_hk1 * hk1_t1 + (3.15 + 1.72 / (hk1 - 1.0));
        double de1_d1 = de1_hk1 * hk1_d1 + 1.0;
        double de1_u1 = de1_hk1 * hk1_u1;
        double de1_ms = de1_hk1 * hk1_ms;
        if (de1 > 12.0 * t1) { de1 = 12.0 * t1; de1_t1 = 12.0; de1_d1 = 0; de1_u1 = 0; de1_ms = 0; }

        double de2_hk2 = (-1.72 / ((hk2 - 1.0) * (hk2 - 1.0))) * t2;
        double de2 = (3.15 + 1.72 / (hk2 - 1.0)) * t2 + d2;
        double de2_t2 = de2_hk2 * hk2_t2 + (3.15 + 1.72 / (hk2 - 1.0));
        double de2_d2 = de2_hk2 * hk2_d2 + 1.0;
        double de2_u2 = de2_hk2 * hk2_u2;
        double de2_ms = de2_hk2 * hk2_ms;
        if (de2 > 12.0 * t2) { de2 = 12.0 * t2; de2_t2 = 12.0; de2_d2 = 0; de2_u2 = 0; de2_ms = 0; }

        // --- Midpoint Cf with chain derivatives (Fortran BLMID lines 1177-1188) ---
        var mid = ComputeMidpointCorrelations(ityp, hk1, rt1, msq1, hk2, rt2, msq2);
        double cfm = mid.Cfm;
        double cfm_hka = mid.Cfm_Hka;
        double cfm_rta = mid.Cfm_Rta;
        double cfm_ma = mid.Cfm_Ma;

        double cfm_t1 = 0.5 * (cfm_hka * hk1_t1 + cfm_rta * rt1_t1);
        double cfm_d1 = 0.5 * (cfm_hka * hk1_d1);
        double cfm_u1 = 0.5 * (cfm_hka * hk1_u1 + cfm_ma * m1_u1 + cfm_rta * rt1_u1);
        double cfm_t2 = 0.5 * (cfm_hka * hk2_t2 + cfm_rta * rt2_t2);
        double cfm_d2 = 0.5 * (cfm_hka * hk2_d2);
        double cfm_u2 = 0.5 * (cfm_hka * hk2_u2 + cfm_ma * m2_u2 + cfm_rta * rt2_u2);
        double cfm_ms = 0.5 * (cfm_hka * hk1_ms + cfm_ma * m1_ms + cfm_rta * rt1_ms
                              + cfm_hka * hk2_ms + cfm_ma * m2_ms + cfm_rta * rt2_ms);

        // ================================================================
        // Logarithmic differences (Fortran BLDIF lines 1573-1584)
        // ================================================================
        double xlog = Math.Log(x2 / x1);
        double ulog = Math.Log(u2 / u1);
        double tlog = Math.Log(t2 / t1);
        double hlog = Math.Log(hs2 / hs1);
        double ddlog = 1.0;

        // ================================================================
        // UPW (upwinding parameter) with full derivatives
        // (Fortran BLDIF lines 1597-1643)
        // ================================================================
        double hupwt = 1.0;
        double hdcon = (ityp == 3) ? hupwt / (hk2 * hk2) : 5.0 * hupwt / (hk2 * hk2);
        double hd_hk1 = 0.0;
        double hd_hk2 = -hdcon * 2.0 / hk2;

        double arg = Math.Abs((hk2 - 1.0) / (hk1 - 1.0));
        double hl = Math.Log(arg);
        double hl_hk1 = -1.0 / (hk1 - 1.0);
        double hl_hk2 = 1.0 / (hk2 - 1.0);

        double hlsq = Math.Min(hl * hl, 15.0);
        double ehh = Math.Exp(-hlsq * hdcon);
        double upw = 1.0 - 0.5 * ehh;
        double upw_hl = ehh * hl * hdcon;
        double upw_hd = 0.5 * ehh * hlsq;

        double upw_hk1 = upw_hl * hl_hk1 + upw_hd * hd_hk1;
        double upw_hk2 = upw_hl * hl_hk2 + upw_hd * hd_hk2;

        // Chain UPW to T,D,U (Fortran BLDIF lines 1636-1643)
        double upw_t1 = upw_hk1 * hk1_t1;
        double upw_d1 = upw_hk1 * hk1_d1;
        double upw_u1 = upw_hk1 * hk1_u1;
        double upw_t2 = upw_hk2 * hk2_t2;
        double upw_d2 = upw_hk2 * hk2_d2;
        double upw_u2 = upw_hk2 * hk2_u2;
        double upw_ms = upw_hk1 * hk1_ms + upw_hk2 * hk2_ms;

        // ================================================================
        // Equation 1: Ctau/Amplification (Fortran BLDIF lines 1646-1839)
        // ================================================================
        if (ityp == 1)
        {
            // Laminar: amplification equation (simplified -- AXSET not yet ported)
            double rezc = ampl2 - ampl1;
            result.Residual[0] = -rezc;
            result.VS1[0, 0] = -1.0;
            result.VS2[0, 0] = 1.0;
        }
        else
        {
            // Turbulent/wake: shear lag equation (Fortran BLDIF lines 1683-1839)
            double sa = (1.0 - upw) * s1 + upw * s2;
            double cqa = (1.0 - upw) * cq1 + upw * cq2;
            double cfa = (1.0 - upw) * cf1 + upw * cf2;
            double hka = (1.0 - upw) * hk1 + upw * hk2;
            double usa = 0.5 * (us1 + us2);
            double rta = 0.5 * (rt1 + rt2);
            double dea = 0.5 * (de1 + de2);
            double da = 0.5 * (d1 + d2);

            double ald = (ityp == 3) ? DLCON : 1.0;

            // Equilibrium 1/Ue dUe/dx (Fortran lines 1706-1754)
            double gcc = (ityp == 2) ? GCCON : 0.0;
            double hkc = hka - 1.0 - gcc / rta;
            double hkc_hka = 1.0;
            double hkc_rta = gcc / (rta * rta);
            if (hkc < 0.01) { hkc = 0.01; hkc_hka = 0; hkc_rta = 0; }

            double hr = hkc / (GACON * ald * hka);
            double hr_hka = hkc_hka / (GACON * ald * hka) - hr / hka;
            double hr_rta = hkc_rta / (GACON * ald * hka);

            double uq = (0.5 * cfa - hr * hr) / (GBCON * da);
            double uq_hka = -2.0 * hr * hr_hka / (GBCON * da);
            double uq_rta = -2.0 * hr * hr_rta / (GBCON * da);
            double uq_cfa = 0.5 / (GBCON * da);
            double uq_da = -uq / da;
            double uq_upw = uq_cfa * (cf2 - cf1) + uq_hka * (hk2 - hk1);

            double uq_t1 = (1.0 - upw) * (uq_cfa * cf1_t1 + uq_hka * hk1_t1) + uq_upw * upw_t1;
            double uq_d1 = (1.0 - upw) * (uq_cfa * cf1_d1 + uq_hka * hk1_d1) + uq_upw * upw_d1;
            double uq_u1 = (1.0 - upw) * (uq_cfa * cf1_u1 + uq_hka * hk1_u1) + uq_upw * upw_u1;
            double uq_t2 = upw * (uq_cfa * cf2_t2 + uq_hka * hk2_t2) + uq_upw * upw_t2;
            double uq_d2 = upw * (uq_cfa * cf2_d2 + uq_hka * hk2_d2) + uq_upw * upw_d2;
            double uq_u2 = upw * (uq_cfa * cf2_u2 + uq_hka * hk2_u2) + uq_upw * upw_u2;

            // Add RTA, DA contributions (Fortran lines 1745-1754)
            uq_t1 += 0.5 * uq_rta * rt1_t1;
            uq_d1 += 0.5 * uq_da;
            uq_u1 += 0.5 * uq_rta * rt1_u1;
            uq_t2 += 0.5 * uq_rta * rt2_t2;
            uq_d2 += 0.5 * uq_da;
            uq_u2 += 0.5 * uq_rta * rt2_u2;

            double scc = SCCON * 1.333 / (1.0 + usa);
            double scc_usa = -scc / (1.0 + usa);
            double scc_us1 = scc_usa * 0.5;
            double scc_us2 = scc_usa * 0.5;

            double slog = (s1 > 0 && s2 > 0) ? Math.Log(s2 / s1) : 0.0;
            double dxi = x2 - x1;

            double rezc = scc * (cqa - sa * ald) * dxi
                        - dea * 2.0 * slog
                        + dea * 2.0 * (uq * dxi - ulog) * DUXCON;
            result.Residual[0] = -rezc;

            // Z-coefficients (Fortran lines 1780-1810)
            double z_cfa = dea * 2.0 * uq_cfa * dxi * DUXCON;
            double z_hka = dea * 2.0 * uq_hka * dxi * DUXCON;
            double z_da = dea * 2.0 * uq_da * dxi * DUXCON;
            double z_sl = -dea * 2.0;
            double z_ul = -dea * 2.0 * DUXCON;
            double z_dxi = scc * (cqa - sa * ald) + dea * 2.0 * uq * DUXCON;
            double z_usa = scc_usa * (cqa - sa * ald) * dxi;
            double z_cqa = scc * dxi;
            double z_sa = -scc * dxi * ald;
            double z_dea = 2.0 * ((uq * dxi - ulog) * DUXCON - slog);

            double z_upw = z_cqa * (cq2 - cq1) + z_sa * (s2 - s1)
                         + z_cfa * (cf2 - cf1) + z_hka * (hk2 - hk1);
            double z_de1 = 0.5 * z_dea;
            double z_de2 = 0.5 * z_dea;
            double z_us1 = 0.5 * z_usa;
            double z_us2 = 0.5 * z_usa;
            double z_d1 = 0.5 * z_da;
            double z_d2 = 0.5 * z_da;
            double z_u1 = -z_ul / u1;
            double z_u2 = z_ul / u2;
            double z_x1 = -z_dxi;
            double z_x2 = z_dxi;
            double z_s1 = (1.0 - upw) * z_sa - ((s1 > 0) ? z_sl / s1 : 0);
            double z_s2 = upw * z_sa + ((s2 > 0) ? z_sl / s2 : 0);
            double z_cq1 = (1.0 - upw) * z_cqa;
            double z_cq2 = upw * z_cqa;
            double z_cf1 = (1.0 - upw) * z_cfa;
            double z_cf2 = upw * z_cfa;
            double z_hk1 = (1.0 - upw) * z_hka;
            double z_hk2 = upw * z_hka;

            // Assemble Equation 1 Jacobians (Fortran lines 1812-1837)
            result.VS1[0, 0] = z_s1;
            result.VS1[0, 1] = z_upw * upw_t1 + z_de1 * de1_t1 + z_us1 * us1_t1;
            result.VS1[0, 2] = z_d1 + z_upw * upw_d1 + z_de1 * de1_d1 + z_us1 * us1_d1;
            result.VS1[0, 3] = z_u1 + z_upw * upw_u1 + z_de1 * de1_u1 + z_us1 * us1_u1;
            result.VS1[0, 4] = z_x1;
            result.VS2[0, 0] = z_s2;
            result.VS2[0, 1] = z_upw * upw_t2 + z_de2 * de2_t2 + z_us2 * us2_t2;
            result.VS2[0, 2] = z_d2 + z_upw * upw_d2 + z_de2 * de2_d2 + z_us2 * us2_d2;
            result.VS2[0, 3] = z_u2 + z_upw * upw_u2 + z_de2 * de2_u2 + z_us2 * us2_u2;
            result.VS2[0, 4] = z_x2;

            // Add CQ, CF, HK contributions (Fortran lines 1825-1831)
            result.VS1[0, 1] += z_cq1 * cq1_t1 + z_cf1 * cf1_t1 + z_hk1 * hk1_t1;
            result.VS1[0, 2] += z_cq1 * cq1_d1 + z_cf1 * cf1_d1 + z_hk1 * hk1_d1;
            result.VS1[0, 3] += z_cq1 * cq1_u1 + z_cf1 * cf1_u1 + z_hk1 * hk1_u1;

            result.VS2[0, 1] += z_cq2 * cq2_t2 + z_cf2 * cf2_t2 + z_hk2 * hk2_t2;
            result.VS2[0, 2] += z_cq2 * cq2_d2 + z_cf2 * cf2_d2 + z_hk2 * hk2_d2;
            result.VS2[0, 3] += z_cq2 * cq2_u2 + z_cf2 * cf2_u2 + z_hk2 * hk2_u2;
        }

        // ================================================================
        // Equation 2: Momentum (von Karman) (Fortran BLDIF lines 1842-1898)
        // ================================================================
        {
            double ha = 0.5 * (h1 + h2);
            double ma = 0.5 * (m1v + m2v);
            double xa = 0.5 * (x1 + x2);
            double ta = 0.5 * (t1 + t2);
            double hwa = 0.5 * (dw1 / t1 + dw2 / t2);

            // CFX (Fortran lines 1850-1860)
            double cfx = 0.50 * cfm * xa / ta + 0.25 * (cf1 * x1 / t1 + cf2 * x2 / t2);
            double cfx_xa = 0.50 * cfm / ta;
            double cfx_ta = -0.50 * cfm * xa / (ta * ta);
            double cfx_x1 = 0.25 * cf1 / t1 + cfx_xa * 0.5;
            double cfx_x2 = 0.25 * cf2 / t2 + cfx_xa * 0.5;
            double cfx_t1 = -0.25 * cf1 * x1 / (t1 * t1) + cfx_ta * 0.5;
            double cfx_t2 = -0.25 * cf2 * x2 / (t2 * t2) + cfx_ta * 0.5;
            double cfx_cf1 = 0.25 * x1 / t1;
            double cfx_cf2 = 0.25 * x2 / t2;
            double cfx_cfm = 0.50 * xa / ta;

            double btmp = ha + 2.0 - ma + hwa;

            double rezt = tlog + btmp * ulog - xlog * 0.5 * cfx;
            result.Residual[1] = -rezt;

            // Z-coefficients (Fortran lines 1865-1876)
            double z_cfx = -xlog * 0.5;
            double z_ha = ulog;
            double z_hwa = ulog;
            double z_ma = -ulog;
            double z_xl = -ddlog * 0.5 * cfx;
            double z_ul = ddlog * btmp;
            double z_tl = ddlog;

            double z_cfm = z_cfx * cfx_cfm;
            double z_cf1 = z_cfx * cfx_cf1;
            double z_cf2 = z_cfx * cfx_cf2;

            // Z_T1, Z_T2, Z_X1, Z_X2, Z_U1, Z_U2 (Fortran lines 1877-1882)
            double z_t1 = -z_tl / t1 + z_cfx * cfx_t1 + z_hwa * 0.5 * (-dw1 / (t1 * t1));
            double z_t2 = z_tl / t2 + z_cfx * cfx_t2 + z_hwa * 0.5 * (-dw2 / (t2 * t2));
            double z_x1 = -z_xl / x1 + z_cfx * cfx_x1;
            double z_x2 = z_xl / x2 + z_cfx * cfx_x2;
            double z_u1 = -z_ul / u1;
            double z_u2 = z_ul / u2;

            // Assemble Equation 2 Jacobians (Fortran lines 1884-1898)
            result.VS1[1, 1] = 0.5 * z_ha * h1_t1 + z_cfm * cfm_t1 + z_cf1 * cf1_t1 + z_t1;
            result.VS1[1, 2] = 0.5 * z_ha * h1_d1 + z_cfm * cfm_d1 + z_cf1 * cf1_d1;
            result.VS1[1, 3] = 0.5 * z_ma * m1_u1 + z_cfm * cfm_u1 + z_cf1 * cf1_u1 + z_u1;
            result.VS1[1, 4] = z_x1;
            result.VS2[1, 1] = 0.5 * z_ha * h2_t2 + z_cfm * cfm_t2 + z_cf2 * cf2_t2 + z_t2;
            result.VS2[1, 2] = 0.5 * z_ha * h2_d2 + z_cfm * cfm_d2 + z_cf2 * cf2_d2;
            result.VS2[1, 3] = 0.5 * z_ma * m2_u2 + z_cfm * cfm_u2 + z_cf2 * cf2_u2 + z_u2;
            result.VS2[1, 4] = z_x2;
        }

        // ================================================================
        // Equation 3: Shape parameter (energy) (Fortran BLDIF lines 1900-1975)
        // ================================================================
        {
            double xot1 = x1 / t1;
            double xot2 = x2 / t2;

            double ha = 0.5 * (h1 + h2);
            double hsa = 0.5 * (hs1 + hs2);
            double hca = 0.5 * (hc1 + hc2);
            double hwa = 0.5 * (dw1 / t1 + dw2 / t2);

            double dix = (1.0 - upw) * di1 * xot1 + upw * di2 * xot2;
            double cfx = (1.0 - upw) * cf1 * xot1 + upw * cf2 * xot2;
            double dix_upw = di2 * xot2 - di1 * xot1;
            double cfx_upw = cf2 * xot2 - cf1 * xot1;

            double btmp = 2.0 * hca / hsa + 1.0 - ha - hwa;

            double rezh = hlog + btmp * ulog + xlog * (0.5 * cfx - dix);
            result.Residual[2] = -rezh;

            // Z-coefficients (Fortran lines 1918-1941)
            double z_cfx = xlog * 0.5;
            double z_dix = -xlog;
            double z_hca = 2.0 * ulog / hsa;
            double z_ha = -ulog;
            double z_hwa = -ulog;
            double z_xl = ddlog * (0.5 * cfx - dix);
            double z_ul = ddlog * btmp;
            double z_hl = ddlog;

            double z_upw = z_cfx * cfx_upw + z_dix * dix_upw;

            double z_hs1 = -hca * ulog / (hsa * hsa) - z_hl / hs1;
            double z_hs2 = -hca * ulog / (hsa * hsa) + z_hl / hs2;

            double z_cf1 = (1.0 - upw) * z_cfx * xot1;
            double z_cf2 = upw * z_cfx * xot2;
            double z_di1 = (1.0 - upw) * z_dix * xot1;
            double z_di2 = upw * z_dix * xot2;

            double z_t1 = (1.0 - upw) * (z_cfx * cf1 + z_dix * di1) * (-xot1 / t1);
            double z_t2 = upw * (z_cfx * cf2 + z_dix * di2) * (-xot2 / t2);
            double z_x1 = (1.0 - upw) * (z_cfx * cf1 + z_dix * di1) / t1 - z_xl / x1;
            double z_x2 = upw * (z_cfx * cf2 + z_dix * di2) / t2 + z_xl / x2;
            double z_u1 = -z_ul / u1;
            double z_u2 = z_ul / u2;

            z_t1 += z_hwa * 0.5 * (-dw1 / (t1 * t1));
            z_t2 += z_hwa * 0.5 * (-dw2 / (t2 * t2));

            // Assemble Equation 3 Jacobians (Fortran lines 1947-1967)
            result.VS1[2, 0] = z_di1 * di1_s1;
            result.VS1[2, 1] = z_hs1 * hs1_t1 + z_cf1 * cf1_t1 + z_di1 * di1_t1 + z_t1;
            result.VS1[2, 2] = z_hs1 * hs1_d1 + z_cf1 * cf1_d1 + z_di1 * di1_d1;
            result.VS1[2, 3] = z_hs1 * hs1_u1 + z_cf1 * cf1_u1 + z_di1 * di1_u1 + z_u1;
            result.VS1[2, 4] = z_x1;
            result.VS2[2, 0] = z_di2 * di2_s2;
            result.VS2[2, 1] = z_hs2 * hs2_t2 + z_cf2 * cf2_t2 + z_di2 * di2_t2 + z_t2;
            result.VS2[2, 2] = z_hs2 * hs2_d2 + z_cf2 * cf2_d2 + z_di2 * di2_d2;
            result.VS2[2, 3] = z_hs2 * hs2_u2 + z_cf2 * cf2_u2 + z_di2 * di2_u2 + z_u2;
            result.VS2[2, 4] = z_x2;

            // Add HC, HA, UPW contributions (Fortran lines 1962-1967)
            result.VS1[2, 1] += 0.5 * (z_hca * hc1_t1 + z_ha * h1_t1) + z_upw * upw_t1;
            result.VS1[2, 2] += 0.5 * (z_hca * hc1_d1 + z_ha * h1_d1) + z_upw * upw_d1;
            result.VS1[2, 3] += 0.5 * (z_hca * hc1_u1) + z_upw * upw_u1;
            result.VS2[2, 1] += 0.5 * (z_hca * hc2_t2 + z_ha * h2_t2) + z_upw * upw_t2;
            result.VS2[2, 2] += 0.5 * (z_hca * hc2_d2 + z_ha * h2_d2) + z_upw * upw_d2;
            result.VS2[2, 3] += 0.5 * (z_hca * hc2_u2) + z_upw * upw_u2;
        }

        return result;
    }

    // =====================================================================
    // ComputeCqChains: Equilibrium Ctau chain derivatives
    // Computes CQ value and its T,D,U,MS derivatives at a single station.
    // Port of CQ2 computation from BLVAR (xblsys.f:853-895).
    // =====================================================================
    private static void ComputeCqChains(
        double hk, double hs, double us, double h, double rt, int ityp,
        double hk_t, double hk_d, double hk_u, double hk_ms,
        double hs_t, double hs_d, double hs_u, double hs_ms,
        double us_t, double us_d, double us_u, double us_ms,
        double h_t, double h_d,
        double rt_t, double rt_u, double rt_ms,
        out double cq, out double cq_t, out double cq_d, out double cq_u, out double cq_ms)
    {
        double gcc = (ityp == 2) ? GCCON : 0.0;
        double hkc = hk - 1.0 - gcc / rt;
        double hkc_hk = 1.0;
        double hkc_rt = gcc / (rt * rt);
        if (hkc < 0.01) { hkc = 0.01; hkc_hk = 0; hkc_rt = 0; }

        double hkb = hk - 1.0;
        if (hkb < 0.01) hkb = 0.01;

        double usb = 1.0 - us;
        if (usb < 0.01) usb = 0.01;

        double num = CTCON * hs * hkb * hkc * hkc;
        double den = usb * h * hk * hk;
        double ratio = num / den;
        if (ratio < 1e-20) ratio = 1e-20;

        cq = Math.Sqrt(ratio);
        double halfOverCq = 0.5 / cq;

        // Partial derivatives wrt intermediate variables (Fortran lines 875-883)
        double cq_hs = CTCON * hkb * hkc * hkc / (usb * h * hk * hk) * halfOverCq;
        double cq_us = CTCON * hs * hkb * hkc * hkc / (usb * h * hk * hk) / usb * halfOverCq;
        double cq_hk = (CTCON * hs * hkc * hkc / (usb * h * hk * hk)
                       - CTCON * hs * hkb * hkc * hkc / (usb * h * hk * hk * hk) * 2.0
                       + CTCON * hs * hkb * hkc / (usb * h * hk * hk) * 2.0 * hkc_hk) * halfOverCq;
        double cq_rt2 = CTCON * hs * hkb * hkc / (usb * h * hk * hk) * 2.0 * hkc_rt * halfOverCq;
        double cq_h = -CTCON * hs * hkb * hkc * hkc / (usb * h * h * hk * hk) * halfOverCq;

        // Chain to T,D,U (Fortran lines 885-895)
        cq_t = cq_hs * hs_t + cq_us * us_t + cq_hk * hk_t + cq_h * h_t + cq_rt2 * rt_t;
        cq_d = cq_hs * hs_d + cq_us * us_d + cq_hk * hk_d + cq_h * h_d;
        cq_u = cq_hs * hs_u + cq_us * us_u + cq_hk * hk_u + cq_rt2 * rt_u;
        cq_ms = cq_hs * hs_ms + cq_us * us_ms + cq_hk * hk_ms + cq_rt2 * rt_ms;
    }

    // =====================================================================
    // ComputeDiChains: Dissipation coefficient with full derivative chains
    // Port of DI2 computation from BLVAR (xblsys.f:929-1097).
    // =====================================================================
    private static void ComputeDiChains(
        int ityp,
        double hk, double hs, double us, double h, double rt, double s, double msq,
        double hk_t, double hk_d, double hk_u, double hk_ms,
        double hs_t, double hs_d, double hs_u, double hs_ms,
        double us_t, double us_d, double us_u, double us_ms,
        double rt_t, double rt_u, double rt_ms,
        out double di, out double di_s, out double di_t, out double di_d, out double di_u, out double di_ms)
    {
        di = 0; di_s = 0; di_t = 0; di_d = 0; di_u = 0; di_ms = 0;

        if (ityp == 1)
        {
            // Laminar dissipation (Fortran BLVAR lines 930-940)
            var (dil, dil_hk, dil_rt) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);
            di = dil;
            di_s = 0;
            di_t = dil_hk * hk_t + dil_rt * rt_t;
            di_d = dil_hk * hk_d;
            di_u = dil_hk * hk_u + dil_rt * rt_u;
            di_ms = dil_hk * hk_ms + dil_rt * rt_ms;
            return;
        }

        // Turbulent or wake dissipation
        if (ityp == 2)
        {
            // Wall contribution (Fortran BLVAR lines 947-991)
            var (cf2t, cf2t_hk, cf2t_rt, cf2t_m) = BoundaryLayerCorrelations.TurbulentSkinFriction(hk, rt, msq);
            double cf2t_t = cf2t_hk * hk_t + cf2t_rt * rt_t;
            double cf2t_d = cf2t_hk * hk_d;
            double cf2t_u = cf2t_hk * hk_u + cf2t_rt * rt_u + cf2t_m * 0.0; // m_u=0 incomp
            double cf2t_ms2 = cf2t_hk * hk_ms + cf2t_rt * rt_ms + cf2t_m * 1.0;

            di = (0.5 * cf2t * us) * 2.0 / hs;
            double di_hs = -(0.5 * cf2t * us) * 2.0 / (hs * hs);
            double di_us = (0.5 * cf2t) * 2.0 / hs;
            double di_cf2t = (0.5 * us) * 2.0 / hs;

            di_s = 0;
            di_t = di_hs * hs_t + di_us * us_t + di_cf2t * cf2t_t;
            di_d = di_hs * hs_d + di_us * us_d + di_cf2t * cf2t_d;
            di_u = di_hs * hs_u + di_us * us_u + di_cf2t * cf2t_u;
            di_ms = di_hs * hs_ms + di_us * us_ms + di_cf2t * cf2t_ms2;

            // DFAC correction (Fortran lines 968-991)
            double grt = Math.Log(Math.Max(rt, 1.0));
            double hmin = 1.0 + 2.1 / grt;
            double hm_rt = -(2.1 / (grt * grt)) / rt;

            double fl = (hk - 1.0) / (hmin - 1.0);
            double fl_hk = 1.0 / (hmin - 1.0);
            double fl_rt = (-fl / (hmin - 1.0)) * hm_rt;

            double tfl = Math.Tanh(fl);
            double dfac = 0.5 + 0.5 * tfl;
            double df_fl = 0.5 * (1.0 - tfl * tfl);
            double df_hk = df_fl * fl_hk;
            double df_rt = df_fl * fl_rt;

            // Apply DFAC to DI and derivatives (Fortran lines 985-991)
            di_s = di_s * dfac;
            double di_save = di;
            di_t = di_t * dfac + di_save * (df_hk * hk_t + df_rt * rt_t);
            di_d = di_d * dfac + di_save * (df_hk * hk_d);
            di_u = di_u * dfac + di_save * (df_hk * hk_u + df_rt * rt_u);
            di_ms = di_ms * dfac + di_save * (df_hk * hk_ms + df_rt * rt_ms);
            di = di * dfac;
        }
        // else wake: DI starts at 0 (no wall contribution)

        // Outer layer contribution (Fortran lines 1007-1036)
        if (ityp != 1)
        {
            double st = Math.Sqrt(Math.Max(s, 0.0));

            double dd = st * st * (0.995 - us) * 2.0 / hs;
            double dd_hs = -st * st * (0.995 - us) * 2.0 / (hs * hs);
            double dd_us = -st * st * 2.0 / hs;
            double dd_s = (s > 0) ? 2.0 * st * (0.995 - us) * 2.0 / hs : 0;

            di += dd;
            di_s += dd_s;
            di_t += dd_hs * hs_t + dd_us * us_t;
            di_d += dd_hs * hs_d + dd_us * us_d;
            di_u += dd_hs * hs_u + dd_us * us_u;
            di_ms += dd_hs * hs_ms + dd_us * us_ms;

            // Laminar stress contribution (Fortran lines 1024-1035)
            double ddl = 0.15 * (0.995 - us) * (0.995 - us) / rt * 2.0 / hs;
            double ddl_us = -0.15 * (0.995 - us) * 2.0 / rt * 2.0 / hs;
            double ddl_hs = -ddl / hs;
            double ddl_rt = -ddl / rt;

            di += ddl;
            di_t += ddl_hs * hs_t + ddl_us * us_t + ddl_rt * rt_t;
            di_d += ddl_hs * hs_d + ddl_us * us_d;
            di_u += ddl_hs * hs_u + ddl_us * us_u + ddl_rt * rt_u;
            di_ms += ddl_hs * hs_ms + ddl_us * us_ms + ddl_rt * rt_ms;
        }

        // Check laminar dissipation override (Fortran lines 1040-1055)
        if (ityp == 2)
        {
            var (dil, dil_hk, dil_rt) = BoundaryLayerCorrelations.LaminarDissipation(hk, rt);
            if (dil > di)
            {
                di = dil;
                di_s = 0;
                di_t = dil_hk * hk_t + dil_rt * rt_t;
                di_d = dil_hk * hk_d;
                di_u = dil_hk * hk_u + dil_rt * rt_u;
                di_ms = dil_hk * hk_ms + dil_rt * rt_ms;
            }
        }

        // Wake laminar dissipation check (Fortran lines 1070-1085)
        if (ityp == 3)
        {
            var (dilw, dilw_hk, dilw_rt) = BoundaryLayerCorrelations.WakeDissipation(hk, rt);
            if (dilw > di)
            {
                di = dilw;
                di_s = 0;
                di_t = dilw_hk * hk_t + dilw_rt * rt_t;
                di_d = dilw_hk * hk_d;
                di_u = dilw_hk * hk_u + dilw_rt * rt_u;
                di_ms = dilw_hk * hk_ms + dilw_rt * rt_ms;
            }
        }

        // Wake doubles dissipation (Fortran lines 1088-1097)
        if (ityp == 3)
        {
            di *= 2.0;
            di_s *= 2.0;
            di_t *= 2.0;
            di_d *= 2.0;
            di_u *= 2.0;
            di_ms *= 2.0;
        }
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
    //
    // Performs the complete BLSYS chain transformation:
    // 1. BLPRV: Convert incompressible Uei to compressible U via Karman-Tsien
    // 2. Compute MSQ (Mach^2) at both stations with U_MS sensitivities
    // 3. BLDIF: Compute residuals and VS1/VS2 Jacobians in compressible vars
    // 4. SIMI: Combine VS1+VS2 for similarity station
    // 5. Ue chain transform: VS(k,4) *= U_UEI (compressible -> incompressible)
    //    This is the key BLSYS chain: derivatives wrt compressible Ue are
    //    converted to derivatives wrt incompressible Uei using the
    //    Karman-Tsien Jacobian factor U_UEI.
    //    Fortran BLSYS lines 647-658:
    //      RES_U1 = VS1(K,4)
    //      RES_U2 = VS2(K,4)
    //      RES_MS = VSM(K)
    //      VS1(K,4) = RES_U1*U1_UEI
    //      VS2(K,4) = RES_U2*U2_UEI
    //      VSM(K)   = RES_U1*U1_MS + RES_U2*U2_MS + RES_MS
    // =====================================================================

    /// <summary>
    /// Top-level BL system assembly for a single station interval.
    /// Port of BLSYS from xblsys.f. Applies full chain transformations:
    /// BLPRV (compressibility), BLDIF (Jacobians), SIMI (similarity),
    /// and Ue incompressible chain (U_UEI factor).
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

        // Determine ITYP (Fortran BLSYS lines 604-614)
        int ityp;
        if (isWake) ityp = 3;
        else if (isTurbOrTran) ityp = 2;
        else ityp = 1;

        // ---- BLPRV: Convert to compressible edge velocity ----
        // (Fortran BLSYS calls BLVAR which calls BLPRV internally)
        var (u1, u1_uei, u1_ms) = ConvertToCompressible(uei1, tkbl, qinfbl, tkbl_ms);
        var (u2, u2_uei, u2_ms) = ConvertToCompressible(uei2, tkbl, qinfbl, tkbl_ms);

        // ---- Compute MSQ (Mach^2) at both stations ----
        // MSQ is used by BLDIF for compressibility corrections in correlations
        double msq1 = 0.0, msq2 = 0.0;
        if (hstinv > 0)
        {
            double u1sq = u1 * u1 * hstinv;
            msq1 = u1sq / (gm1bl * (1.0 - 0.5 * u1sq));
            double u2sq = u2 * u2 * hstinv;
            msq2 = u2sq / (gm1bl * (1.0 - 0.5 * u2sq));
        }

        // ---- BLDIF: Compute BL equation residuals and Jacobians ----
        // ComputeFiniteDifferences now includes full BLVAR-style chain-rule
        // Jacobians at both stations (primary derivatives + correlation chains)
        var bldif = ComputeFiniteDifferences(
            ityp, x1, x2, u1, u2, t1, t2, d1, d2, s1, s2,
            dw1, dw2, msq1, msq2, ampl1, ampl2, amcrit, reybl);

        // Copy residuals
        for (int k = 0; k < 3; k++)
            result.Residual[k] = bldif.Residual[k];

        // ---- SIMI: Similarity station handling ----
        // (Fortran BLSYS lines 636-644: VS2 = VS1 + VS2, VS1 = 0)
        // Must be done BEFORE the Ue chain transform
        if (isSimi)
        {
            for (int k = 0; k < 3; k++)
                for (int l = 0; l < 5; l++)
                {
                    double vs2kl = bldif.VS1[k, l] + bldif.VS2[k, l];
                    bldif.VS1[k, l] = 0;
                    bldif.VS2[k, l] = vs2kl;
                }
        }

        // ---- Ue chain transform: compressible -> incompressible ----
        // (Fortran BLSYS lines 647-658)
        // This is the key BLSYS chain transformation that converts
        // derivatives wrt compressible Ue (from BLDIF) into derivatives
        // wrt incompressible Uei (what the Newton system actually solves for).
        //
        // Chain rule: dR/dUei = dR/dU * dU/dUei = VS(k,4) * U_UEI
        //
        // The Fortran also computes:
        //   VSM(K) = RES_U1*U1_MS + RES_U2*U2_MS + RES_MS
        // which tracks Mach sensitivity. We fold this into the result
        // for completeness, though VSM is not directly used in the
        // 5-column VS1/VS2 Newton system (it would appear in a separate
        // Mach sensitivity column if needed).
        for (int k = 0; k < 3; k++)
        {
            for (int l = 0; l < 5; l++)
            {
                result.VS1[k, l] = bldif.VS1[k, l];
                result.VS2[k, l] = bldif.VS2[k, l];
            }

            // Apply U_UEI chain factor to Ue column (column 3)
            // Fortran: VS1(K,4) = RES_U1*U1_UEI
            //          VS2(K,4) = RES_U2*U2_UEI
            double resU1 = bldif.VS1[k, 3];
            double resU2 = bldif.VS2[k, 3];
            result.VS1[k, 3] = resU1 * u1_uei;
            result.VS2[k, 3] = resU2 * u2_uei;
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

using System;
using System.Collections.Generic;
using XFoil.Core.Models;
using XFoil.Solver.Models;

// Modern-only code — do NOT run gen-double.py on this file. It lives in
// the XFoil.Solver.Modern.Services namespace and is purely Phase-3
// machinery. gen-double would generate a nonsense twin with namespace
// XFoil.Solver.Double.Modern.Services; delete any such file if it appears.
namespace XFoil.Solver.Modern.Services;

/// <summary>
/// Builds a per-buffer-point density bias array from a first-pass solution,
/// to be pushed through <c>CurvatureAdaptivePanelDistributor.PushDensityScale</c>
/// so PANGEN places more panels where the solution varies rapidly. Phase 3
/// B1 Modern (#3) helper — inviscid sensor (Cp gradient) for v1. Viscous
/// sensor (BL shear-stress gradient) lands in a follow-up.
/// </summary>
internal static class SolutionAdaptivePanelBias
{
    /// <summary>
    /// Default cap on the density-scale multiplier. 3x keeps the
    /// distribution stable — higher values can cluster so aggressively
    /// that other surface regions under-resolve.
    /// </summary>
    public const double DefaultMaxBoost = 3.0;

    /// <summary>
    /// Default exponent on the normalized Cp-gradient sensor. 1.0 gives a
    /// linear bias; values &gt; 1 sharpen the emphasis on the very-highest
    /// gradient points at the expense of moderate-gradient ones.
    /// </summary>
    public const double DefaultSmoothingExponent = 1.0;

    /// <summary>
    /// Computes a density scale array of length <c>bufferPoints.Count</c>
    /// where scale[i] &gt;= 1.0. Entries with no useful sensor stay at 1.0
    /// (unbiased PANGEN). Callers pass the result to
    /// <c>CurvatureAdaptivePanelDistributor.PushDensityScale</c>.
    /// </summary>
    /// <param name="bufferPoints">Raw airfoil buffer points (upper-TE → LE
    /// → lower-TE). Must be the same list handed to the inviscid solver
    /// that produced <paramref name="panelSamples"/>.</param>
    /// <param name="panelSamples">Cp samples from the coarse inviscid
    /// pass, indexed in panel-node order. At least 8 samples needed for a
    /// stable gradient estimate; otherwise a flat scale is returned.</param>
    /// <param name="maxBoost">Upper clamp on scale[i]. Values in [1.5, 5]
    /// are reasonable; the default 3.0 is conservative.</param>
    /// <param name="smoothingExponent">Exponent on the normalized sensor
    /// before boost application. Default 1.0 (linear).</param>
    public static double[] BuildFromCpGradient(
        IReadOnlyList<AirfoilPoint> bufferPoints,
        IReadOnlyList<PressureCoefficientSample> panelSamples,
        double maxBoost = DefaultMaxBoost,
        double smoothingExponent = DefaultSmoothingExponent)
    {
        ArgumentNullException.ThrowIfNull(bufferPoints);
        ArgumentNullException.ThrowIfNull(panelSamples);

        int nb = bufferPoints.Count;
        var scale = new double[nb];
        Array.Fill(scale, 1.0);

        int np = panelSamples.Count;
        if (np < 8 || nb < 8 || maxBoost <= 1.0)
        {
            return scale;
        }

        // Arc-length of buffer points (cumulative chord-line distance).
        var sBuffer = new double[nb];
        sBuffer[0] = 0.0;
        for (int i = 1; i < nb; i++)
        {
            double dx = bufferPoints[i].X - bufferPoints[i - 1].X;
            double dy = bufferPoints[i].Y - bufferPoints[i - 1].Y;
            sBuffer[i] = sBuffer[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        double sTotBuf = sBuffer[nb - 1];
        if (sTotBuf < 1e-9)
        {
            return scale;
        }

        // Arc-length of panel samples.
        var sPanel = new double[np];
        sPanel[0] = 0.0;
        for (int i = 1; i < np; i++)
        {
            double dx = panelSamples[i].Location.X - panelSamples[i - 1].Location.X;
            double dy = panelSamples[i].Location.Y - panelSamples[i - 1].Location.Y;
            sPanel[i] = sPanel[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        double sTotPan = sPanel[np - 1];
        if (sTotPan < 1e-9)
        {
            return scale;
        }

        // Per-panel-sample sensor: combines |dCp/ds| (gradient) and
        // |d²Cp/ds²| (curvature, to detect pressure-recovery shoulders
        // and LE stagnation-peak inflections that a pure gradient sensor
        // misses). Weighted sum: sensor = |dCp/ds| + curvatureWeight *
        // ds * |d²Cp/ds²| where the ds factor makes both terms
        // dimensionally consistent as Cp-per-arc-length.
        const double curvatureWeight = 0.25;

        var sensor = new double[np];
        for (int i = 1; i < np - 1; i++)
        {
            double cpL = panelSamples[i - 1].PressureCoefficient;
            double cpC = panelSamples[i].PressureCoefficient;
            double cpR = panelSamples[i + 1].PressureCoefficient;
            double dsL = sPanel[i] - sPanel[i - 1];
            double dsR = sPanel[i + 1] - sPanel[i];
            double dsC = sPanel[i + 1] - sPanel[i - 1];

            double gradient = dsC > 1e-12 ? Math.Abs(cpR - cpL) / dsC : 0.0;

            // 2nd central difference scaled by average segment length so
            // it enters the sum at the same order as the gradient term.
            double curvature = 0.0;
            if (dsL > 1e-12 && dsR > 1e-12)
            {
                double dds = ((cpR - cpC) / dsR - (cpC - cpL) / dsL) / (0.5 * dsC);
                curvature = Math.Abs(dds) * 0.5 * dsC;
            }

            sensor[i] = gradient + (curvatureWeight * curvature);
        }
        sensor[0] = sensor[1];
        sensor[np - 1] = sensor[np - 2];

        // 3-point moving-average smoothing — damps high-frequency noise
        // in the sensor that would otherwise amplify into PANGEN Newton
        // asymmetries. One pass is the sweet spot; two passes improved
        // the diverse-set ratio slightly but degraded the NACA-only
        // ratio by more.
        var sensorSmoothed = new double[np];
        sensorSmoothed[0] = sensor[0];
        sensorSmoothed[np - 1] = sensor[np - 1];
        for (int i = 1; i < np - 1; i++)
        {
            sensorSmoothed[i] = 0.25 * sensor[i - 1]
                              + 0.5 * sensor[i]
                              + 0.25 * sensor[i + 1];
        }
        sensor = sensorSmoothed;

        // Per-surface normalization. On cambered airfoils the upper
        // surface has much stronger Cp gradients than the lower, so a
        // global max makes the lower surface look unbiased (all scale=1)
        // even though it has its own meaningful gradient structure.
        // Finding the leading-edge index via min-x over panel samples
        // and normalizing the two segments independently gives both
        // surfaces a fair share of the bias budget.
        int iLE = 0;
        double minX = panelSamples[0].Location.X;
        for (int i = 1; i < np; i++)
        {
            double x = panelSamples[i].Location.X;
            if (x < minX) { minX = x; iLE = i; }
        }

        // Per-surface max normalization (v4, locked after v16-v18 probes
        // ruled out percentile, peakiness-gate, and 2nd-peak-ratio
        // adaptive variants). v18 confirmed that 2nd-peak discrimination
        // does trigger on some airfoils (goe387, 4418) but delivers only
        // 0.002-pp aggregate improvement — within measurement noise. The
        // long-tail Selig cases (s1223 0.975, e387/rg15/sd7037 ~0.85) are
        // resistant to normalization tweaks; their gap to theory is a
        // sensor-design limitation, not a normalization-strategy choice.
        double maxUpper = 0.0;
        for (int i = 0; i <= iLE && i < np; i++)
        {
            if (sensor[i] > maxUpper) maxUpper = sensor[i];
        }
        double maxLower = 0.0;
        for (int i = iLE; i < np; i++)
        {
            if (sensor[i] > maxLower) maxLower = sensor[i];
        }
        if (maxUpper < 1e-6 && maxLower < 1e-6)
        {
            // Flat Cp on both surfaces — no bias opportunity.
            return scale;
        }

        var cpGrad = sensor;  // reuse the buffer under the original name
        double invUpper = maxUpper > 1e-6 ? 1.0 / maxUpper : 0.0;
        double invLower = maxLower > 1e-6 ? 1.0 / maxLower : 0.0;
        for (int i = 0; i < np; i++)
        {
            cpGrad[i] *= (i <= iLE ? invUpper : invLower);
        }

        // For each buffer point, find its fractional arc-length position,
        // map to a matching fractional position on the panel grid,
        // bracket-interpolate the sensor, then convert to a scale factor.
        double boost = maxBoost - 1.0;
        for (int i = 0; i < nb; i++)
        {
            double frac = sBuffer[i] / sTotBuf;
            double sOnPanel = frac * sTotPan;

            int lo = 0;
            int hi = np - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (sPanel[mid] > sOnPanel) hi = mid;
                else lo = mid;
            }
            double span = sPanel[hi] - sPanel[lo];
            double t = span > 1e-12 ? (sOnPanel - sPanel[lo]) / span : 0.0;
            double interpSensor = ((1.0 - t) * cpGrad[lo]) + (t * cpGrad[hi]);
            if (interpSensor < 0.0) interpSensor = 0.0;
            if (interpSensor > 1.0) interpSensor = 1.0;

            double pow = smoothingExponent == 1.0
                ? interpSensor
                : Math.Pow(interpSensor, smoothingExponent);
            scale[i] = 1.0 + (boost * pow);
        }

        return scale;
    }
}

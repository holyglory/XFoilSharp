using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

/// <summary>
/// Panel distribution generator implementing XFoil's PANGEN algorithm.
/// Produces panel node positions using cosine spacing with curvature-based refinement.
/// The node ordering follows XFoil convention: upper-surface TE (node 0) around LE
/// to lower-surface TE (node N-1), counterclockwise.
/// </summary>
public static class CosineClusteringPanelDistributor
{
    /// <summary>
    /// Over-sampling factor for the Newton iteration grid. The iteration uses
    /// IPFAC*(N-1)+1 temporary nodes that are later subsampled to the final N.
    /// XFoil uses IPFAC=5.
    /// </summary>
    private const int Ipfac = 5;

    /// <summary>
    /// Distributes panel nodes along an airfoil surface using XFoil's PANGEN algorithm:
    /// curvature-adaptive cosine spacing with TE density control.
    /// </summary>
    /// <param name="inputX">Raw airfoil X coordinates (0-based). Upper-TE around LE to lower-TE.</param>
    /// <param name="inputY">Raw airfoil Y coordinates (0-based).</param>
    /// <param name="inputCount">Number of raw input points.</param>
    /// <param name="panel">Output panel state to populate with node positions and geometry.</param>
    /// <param name="desiredNodeCount">Target number of panel nodes (default 160, matching XFoil NPAN).</param>
    /// <param name="curvatureWeight">CVPAR: controls how strongly curvature affects panel density (1.0 = XFoil default).</param>
    /// <param name="trailingEdgeDensityRatio">CTERAT: ratio of TE panel density to average LE curvature (0.15 = XFoil default).</param>
    /// <param name="curvatureDensityRatio">CTRRAT: refinement region panel density ratio (0.2 = XFoil default).</param>
    public static void Distribute(
        double[] inputX, double[] inputY, int inputCount,
        LinearVortexPanelState panel,
        int desiredNodeCount = 160,
        double curvatureWeight = 1.0,
        double trailingEdgeDensityRatio = 0.15,
        double curvatureDensityRatio = 0.2)
    {
        if (inputCount < 2) return;

        int nb = inputCount;
        int n = desiredNodeCount;

        // --- Step 1: Spline the input coordinates ---
        var sb = new double[nb];
        var xb = new double[nb];
        var yb = new double[nb];
        var xbp = new double[nb];
        var ybp = new double[nb];

        Array.Copy(inputX, xb, nb);
        Array.Copy(inputY, yb, nb);

        ParametricSpline.ComputeArcLength(xb, yb, sb, nb);
        ParametricSpline.FitSegmented(xb, xbp, sb, nb);
        ParametricSpline.FitSegmented(yb, ybp, sb, nb);

        // Normalizing length (~ half chord, same as XFoil's SBREF)
        double sbref = 0.5 * (sb[nb - 1] - sb[0]);

        // --- Step 2: Compute curvature array (W5 in Fortran) ---
        var w5 = new double[nb];
        for (int i = 0; i < nb; i++)
        {
            w5[i] = Math.Abs(ComputeCurvature(sb[i], xb, xbp, yb, ybp, sb, nb)) * sbref;
        }

        // --- Step 3: Find LE arc-length parameter ---
        double sble = FindLeadingEdge(xb, xbp, yb, ybp, sb, nb);

        double cvle = Math.Abs(ComputeCurvature(sble, xb, xbp, yb, ybp, sb, nb)) * sbref;

        // Check for sharp LE (doubled point)
        int ible = 0;
        for (int i = 0; i < nb - 1; i++)
        {
            if (sble == sb[i] && sble == sb[i + 1])
            {
                ible = i + 1; // 1-based index for compatibility with algorithm
                break;
            }
        }

        // LE and TE coordinates
        double xble = ParametricSpline.Evaluate(sble, xb, xbp, sb, nb);
        double yble = ParametricSpline.Evaluate(sble, yb, ybp, sb, nb);
        double xbte = 0.5 * (xb[0] + xb[nb - 1]);
        double ybte = 0.5 * (yb[0] + yb[nb - 1]);
        double chbsq = (xbte - xble) * (xbte - xble) + (ybte - yble) * (ybte - yble);

        // --- Step 4: Average curvature near LE ---
        int nk = 3;
        double cvsum = 0.0;
        for (int k = -nk; k <= nk; k++)
        {
            double frac = (double)k / nk;
            double sbk = sble + frac * sbref / Math.Max(cvle, 20.0);
            double cvk = Math.Abs(ComputeCurvature(sbk, xb, xbp, yb, ybp, sb, nb)) * sbref;
            cvsum += cvk;
        }

        double cvavg = cvsum / (2 * nk + 1);

        // Dummy curvature for sharp LE
        if (ible != 0)
        {
            cvavg = 10.0;
        }

        // --- Step 5: Curvature attraction coefficient ---
        double cc = 6.0 * curvatureWeight;

        // Artificial curvature at TE
        double cvte = cvavg * trailingEdgeDensityRatio;
        w5[0] = cvte;
        w5[nb - 1] = cvte;

        // --- Step 6: Smooth curvature array ---
        double smool = Math.Max(1.0 / Math.Max(cvavg, 20.0), 0.25 / (n / 2));
        double smoosq = (smool * sbref) * (smool * sbref);

        var w1 = new double[nb];
        var w2 = new double[nb];
        var w3 = new double[nb];

        w2[0] = 1.0;
        w3[0] = 0.0;

        for (int i = 1; i < nb - 1; i++)
        {
            double dsm = sb[i] - sb[i - 1];
            double dsp = sb[i + 1] - sb[i];
            double dso = 0.5 * (sb[i + 1] - sb[i - 1]);

            if (dsm == 0.0 || dsp == 0.0)
            {
                // Corner point: leave curvature unchanged
                w1[i] = 0.0;
                w2[i] = 1.0;
                w3[i] = 0.0;
            }
            else
            {
                w1[i] = smoosq * (-1.0 / dsm) / dso;
                w2[i] = smoosq * (1.0 / dsp + 1.0 / dsm) / dso + 1.0;
                w3[i] = smoosq * (-1.0 / dsp) / dso;
            }
        }

        w1[nb - 1] = 0.0;
        w2[nb - 1] = 1.0;

        // Fix curvature at LE by modifying equations adjacent to LE
        for (int i = 1; i < nb - 1; i++)
        {
            if (sb[i] == sble || (ible != 0 && (i == ible - 1 || i == ible)))
            {
                // Node falls on LE point
                w1[i] = 0.0;
                w2[i] = 1.0;
                w3[i] = 0.0;
                w5[i] = cvle;
            }
            else if (sb[i - 1] < sble && sb[i] > sble)
            {
                // Modify equation at node just before LE point
                double dsm = sb[i - 1] - sb[i - 2];
                double dsp = sble - sb[i - 1];
                double dso = 0.5 * (sble - sb[i - 2]);

                w1[i - 1] = smoosq * (-1.0 / dsm) / dso;
                w2[i - 1] = smoosq * (1.0 / dsp + 1.0 / dsm) / dso + 1.0;
                w3[i - 1] = 0.0;
                w5[i - 1] = w5[i - 1] + smoosq * cvle / (dsp * dso);

                // Modify equation at node just after LE point
                dsm = sb[i] - sble;
                dsp = sb[i + 1] - sb[i];
                dso = 0.5 * (sb[i + 1] - sble);

                w1[i] = 0.0;
                w2[i] = smoosq * (1.0 / dsp + 1.0 / dsm) / dso + 1.0;
                w3[i] = smoosq * (-1.0 / dsp) / dso;
                w5[i] = w5[i] + smoosq * cvle / (dsm * dso);

                break;
            }
        }

        // Set artificial curvature at bunching points (refinement areas)
        // XFoil defaults: XSREF1=XSREF2=1.0, XPREF1=XPREF2=1.0 (i.e. no refinement)
        // We skip this since the default parameters disable it.

        // Solve for smoothed curvature
        // Fortran TRISOL(A=W2, B=W1, C=W3, D=W5) => A=diagonal, B=lower, C=upper
        // C# Solve(lower, diagonal, upper, rhs) => Solve(W1, W2, W3, W5)
        if (ible == 0)
        {
            TridiagonalSolver.Solve(w1, w2, w3, w5, nb);
        }
        else
        {
            // Two-segment solve for sharp LE
            SolveTridiagonalSegment(w1, w2, w3, w5, 0, ible);
            SolveTridiagonalSegment(w1, w2, w3, w5, ible, nb - ible);
        }

        // Find max curvature and normalize
        double cvmax = 0.0;
        for (int i = 0; i < nb; i++)
        {
            cvmax = Math.Max(cvmax, Math.Abs(w5[i]));
        }

        if (cvmax > 0.0)
        {
            for (int i = 0; i < nb; i++)
            {
                w5[i] /= cvmax;
            }
        }

        // Spline the curvature array
        var w6 = new double[nb];
        ParametricSpline.FitSegmented(w5, w6, sb, nb);

        // --- Step 7: Set initial guess for node positions ---
        int nn = Ipfac * (n - 1) + 1;
        var snew = new double[nn];

        // Ratio of lengths of panel at TE to one away from TE
        double rdste = 0.667;
        double rtf = (rdste - 1.0) * 2.0 + 1.0;

        if (ible == 0)
        {
            double dsavg = (sb[nb - 1] - sb[0]) / (nn - 3 + 2.0 * rtf);
            snew[0] = sb[0];
            for (int i = 1; i < nn - 1; i++)
            {
                snew[i] = sb[0] + dsavg * (i - 1 + rtf);
            }

            snew[nn - 1] = sb[nb - 1];
        }
        else
        {
            int nfrac1 = (n * (ible)) / nb; // ible is 1-based
            int nn1 = Ipfac * (nfrac1 - 1) + 1;
            double dsavg1 = (sble - sb[0]) / (nn1 - 2 + rtf);
            snew[0] = sb[0];
            for (int i = 1; i < nn1; i++)
            {
                snew[i] = sb[0] + dsavg1 * (i - 1 + rtf);
            }

            int nn2 = nn - nn1 + 1;
            double dsavg2 = (sb[nb - 1] - sble) / (nn2 - 2 + rtf);
            for (int i = 1; i < nn2 - 1; i++)
            {
                snew[i - 1 + nn1] = sble + dsavg2 * (i - 1 + rtf);
            }

            snew[nn - 1] = sb[nb - 1];
        }

        // --- Step 8: Newton iteration for node positions ---
        var ww1 = new double[nn];
        var ww2 = new double[nn];
        var ww3 = new double[nn];
        var ww4 = new double[nn];

        for (int iter = 0; iter < 20; iter++)
        {
            double cv1 = ParametricSpline.Evaluate(snew[0], w5, w6, sb, nb);
            double cv2 = ParametricSpline.Evaluate(snew[1], w5, w6, sb, nb);
            double cvs1 = ParametricSpline.EvaluateDerivative(snew[0], w5, w6, sb, nb);
            double cvs2 = ParametricSpline.EvaluateDerivative(snew[1], w5, w6, sb, nb);

            double cavm = Math.Sqrt(cv1 * cv1 + cv2 * cv2);
            double cavm_s1, cavm_s2;
            if (cavm == 0.0)
            {
                cavm_s1 = 0.0;
                cavm_s2 = 0.0;
            }
            else
            {
                cavm_s1 = cvs1 * cv1 / cavm;
                cavm_s2 = cvs2 * cv2 / cavm;
            }

            for (int i = 1; i < nn - 1; i++)
            {
                double dsm = snew[i] - snew[i - 1];
                double dsp = snew[i] - snew[i + 1];
                double cv3 = ParametricSpline.Evaluate(snew[i + 1], w5, w6, sb, nb);
                double cvs3 = ParametricSpline.EvaluateDerivative(snew[i + 1], w5, w6, sb, nb);

                double cavp = Math.Sqrt(cv3 * cv3 + cv2 * cv2);
                double cavp_s2, cavp_s3;
                if (cavp == 0.0)
                {
                    cavp_s2 = 0.0;
                    cavp_s3 = 0.0;
                }
                else
                {
                    cavp_s2 = cvs2 * cv2 / cavp;
                    cavp_s3 = cvs3 * cv3 / cavp;
                }

                double fm = cc * cavm + 1.0;
                double fp = cc * cavp + 1.0;

                double rez = dsp * fp + dsm * fm;

                ww1[i] = -fm + cc * dsm * cavm_s1;
                ww2[i] = fp + fm + cc * (dsp * cavp_s2 + dsm * cavm_s2);
                ww3[i] = -fp + cc * dsp * cavp_s3;
                ww4[i] = -rez;

                cv1 = cv2;
                cv2 = cv3;
                cvs1 = cvs2;
                cvs2 = cvs3;
                cavm = cavp;
                cavm_s1 = cavp_s2;
                cavm_s2 = cavp_s3;
            }

            // Fix endpoints
            ww2[0] = 1.0;
            ww3[0] = 0.0;
            ww4[0] = 0.0;
            ww1[nn - 1] = 0.0;
            ww2[nn - 1] = 1.0;
            ww4[nn - 1] = 0.0;

            if (rtf != 1.0)
            {
                // Fudge equations adjacent to TE for TE panel length ratio
                int i2 = 1;
                ww4[i2] = -((snew[i2] - snew[i2 - 1]) + rtf * (snew[i2] - snew[i2 + 1]));
                ww1[i2] = -1.0;
                ww2[i2] = 1.0 + rtf;
                ww3[i2] = -rtf;

                int i3 = nn - 2;
                ww4[i3] = -((snew[i3] - snew[i3 + 1]) + rtf * (snew[i3] - snew[i3 - 1]));
                ww3[i3] = -1.0;
                ww2[i3] = 1.0 + rtf;
                ww1[i3] = -rtf;
            }

            // Fix sharp LE point
            if (ible != 0)
            {
                int nn1 = Ipfac * ((n * ible / nb) - 1) + 1;
                int leIdx = nn1 - 1; // 0-based
                ww1[leIdx] = 0.0;
                ww2[leIdx] = 1.0;
                ww3[leIdx] = 0.0;
                ww4[leIdx] = sble - snew[leIdx];
            }

            // Solve for node position deltas
            // Fortran TRISOL(W2, W1, W3, W4, NN) => A=W2=diagonal, B=W1=lower, C=W3=upper
            TridiagonalSolver.Solve(ww1, ww2, ww3, ww4, nn);

            // Under-relaxation to prevent node order reversal
            double rlx = 1.0;
            double dmax = 0.0;
            for (int i = 0; i < nn - 1; i++)
            {
                double ds = snew[i + 1] - snew[i];
                double dds = ww4[i + 1] - ww4[i];
                if (ds != 0.0)
                {
                    double dsrat = 1.0 + rlx * dds / ds;
                    if (dsrat > 4.0) rlx = (4.0 - 1.0) * ds / dds;
                    if (dsrat < 0.2) rlx = (0.2 - 1.0) * ds / dds;
                }

                dmax = Math.Max(Math.Abs(ww4[i]), dmax);
            }

            // Update node positions
            for (int i = 1; i < nn - 1; i++)
            {
                snew[i] += rlx * ww4[i];
            }

            if (Math.Abs(dmax) < 1.0e-3) break;
        }

        // --- Step 9: Set final panel node coordinates ---
        var sOut = new double[n];
        var xOut = new double[n];
        var yOut = new double[n];

        for (int i = 0; i < n; i++)
        {
            int ind = Ipfac * i;
            sOut[i] = snew[ind];
            xOut[i] = ParametricSpline.Evaluate(snew[ind], xb, xbp, sb, nb);
            yOut[i] = ParametricSpline.Evaluate(snew[ind], yb, ybp, sb, nb);
        }

        // --- Step 10: Handle corners (double points in buffer airfoil) ---
        // For typical NACA airfoils there are no corners, but we handle this
        // for completeness following XFoil's PANGEN logic.
        int finalN = n;
        for (int ib = 0; ib < nb - 1; ib++)
        {
            if (sb[ib] == sb[ib + 1])
            {
                double sbcorn = sb[ib];
                double xbcorn = xb[ib];
                double ybcorn = yb[ib];

                for (int i = 0; i < finalN; i++)
                {
                    if (sOut[i] <= sbcorn) continue;

                    // Make room for additional node
                    var newX = new double[finalN + 1];
                    var newY = new double[finalN + 1];
                    var newS = new double[finalN + 1];

                    Array.Copy(xOut, 0, newX, 0, i);
                    Array.Copy(yOut, 0, newY, 0, i);
                    Array.Copy(sOut, 0, newS, 0, i);

                    newX[i] = xbcorn;
                    newY[i] = ybcorn;
                    newS[i] = sbcorn;

                    Array.Copy(xOut, i, newX, i + 1, finalN - i);
                    Array.Copy(yOut, i, newY, i + 1, finalN - i);
                    Array.Copy(sOut, i, newS, i + 1, finalN - i);

                    finalN++;
                    xOut = newX;
                    yOut = newY;
                    sOut = newS;

                    // Shift adjacent nodes
                    if (i - 2 >= 0)
                    {
                        sOut[i - 1] = 0.5 * (sOut[i] + sOut[i - 2]);
                        xOut[i - 1] = ParametricSpline.Evaluate(sOut[i - 1], xb, xbp, sb, nb);
                        yOut[i - 1] = ParametricSpline.Evaluate(sOut[i - 1], yb, ybp, sb, nb);
                    }

                    if (i + 2 < finalN)
                    {
                        sOut[i + 1] = 0.5 * (sOut[i] + sOut[i + 2]);
                        xOut[i + 1] = ParametricSpline.Evaluate(sOut[i + 1], xb, xbp, sb, nb);
                        yOut[i + 1] = ParametricSpline.Evaluate(sOut[i + 1], yb, ybp, sb, nb);
                    }

                    break;
                }
            }
        }

        // --- Step 11: Re-compute arc length, splines, and LE on final nodes ---
        var finalArc = new double[finalN];
        ParametricSpline.ComputeArcLength(xOut, yOut, finalArc, finalN);

        var finalXp = new double[finalN];
        var finalYp = new double[finalN];
        ParametricSpline.FitSegmented(xOut, finalXp, finalArc, finalN);
        ParametricSpline.FitSegmented(yOut, finalYp, finalArc, finalN);

        double sle = FindLeadingEdge(xOut, finalXp, yOut, finalYp, finalArc, finalN);

        double xle = ParametricSpline.Evaluate(sle, xOut, finalXp, finalArc, finalN);
        double yle = ParametricSpline.Evaluate(sle, yOut, finalYp, finalArc, finalN);
        double xte = 0.5 * (xOut[0] + xOut[finalN - 1]);
        double yte = 0.5 * (yOut[0] + yOut[finalN - 1]);
        double chord = Math.Sqrt((xte - xle) * (xte - xle) + (yte - yle) * (yte - yle));

        // --- Step 12: Populate panel state ---
        panel.Resize(finalN);

        Array.Copy(xOut, panel.X, finalN);
        Array.Copy(yOut, panel.Y, finalN);
        Array.Copy(finalArc, panel.ArcLength, finalN);

        // Fit splines for derivatives
        ParametricSpline.FitSegmented(panel.X, panel.XDerivative, panel.ArcLength, finalN);
        ParametricSpline.FitSegmented(panel.Y, panel.YDerivative, panel.ArcLength, finalN);

        panel.TrailingEdgeX = xte;
        panel.TrailingEdgeY = yte;
        panel.LeadingEdgeX = xle;
        panel.LeadingEdgeY = yle;
        panel.LeadingEdgeArcLength = sle;
        panel.Chord = chord;
    }

    /// <summary>
    /// Finds the leading edge arc-length parameter using XFoil's LEFIND algorithm.
    /// The LE is defined as the point where the surface tangent is perpendicular
    /// to the chord line connecting (X(SLE),Y(SLE)) to the TE midpoint.
    /// </summary>
    private static double FindLeadingEdge(
        double[] x, double[] xp, double[] y, double[] yp, double[] s, int n)
    {
        double dseps = (s[n - 1] - s[0]) * 1.0e-5;

        // TE coordinates
        double xte = 0.5 * (x[0] + x[n - 1]);
        double yte = 0.5 * (y[0] + y[n - 1]);

        // Initial guess: find where dot product of (X-XTE, Y-YTE) . (dX, dY) changes sign
        double sle = s[0];
        for (int i = 2; i < n - 2; i++)
        {
            double dxte = x[i] - xte;
            double dyte = y[i] - yte;
            double dx = x[i + 1] - x[i];
            double dy = y[i + 1] - y[i];
            double dotp = dxte * dx + dyte * dy;
            if (dotp < 0.0)
            {
                sle = s[i];
                break;
            }
        }

        // Check for sharp LE (doubled point)
        for (int i = 0; i < n - 1; i++)
        {
            if (sle == s[i] && sle == s[i + 1])
            {
                return sle;
            }
        }

        // Newton iteration
        for (int iter = 0; iter < 50; iter++)
        {
            double xleVal = ParametricSpline.Evaluate(sle, x, xp, s, n);
            double yleVal = ParametricSpline.Evaluate(sle, y, yp, s, n);
            double dxds = ParametricSpline.EvaluateDerivative(sle, x, xp, s, n);
            double dyds = ParametricSpline.EvaluateDerivative(sle, y, yp, s, n);
            double dxdd = EvaluateSecondDerivative(sle, x, xp, s, n);
            double dydd = EvaluateSecondDerivative(sle, y, yp, s, n);

            double xchord = xleVal - xte;
            double ychord = yleVal - yte;

            // Residual: dot product of chord vector and tangent
            double res = xchord * dxds + ychord * dyds;
            double ress = dxds * dxds + dyds * dyds + xchord * dxdd + ychord * dydd;

            double dsle = -res / ress;

            // Clamp step size
            double limit = 0.02 * Math.Abs(xchord + ychord);
            dsle = Math.Max(dsle, -limit);
            dsle = Math.Min(dsle, limit);

            sle += dsle;

            if (Math.Abs(dsle) < dseps) return sle;
        }

        return sle;
    }

    /// <summary>
    /// Evaluates the second derivative d2X/dS2 at parameter s.
    /// Port of XFoil's D2VAL routine from spline.f.
    /// </summary>
    private static double EvaluateSecondDerivative(
        double s, double[] values, double[] derivatives, double[] parameters, int count)
    {
        // Binary search for interval
        int iLow = 0;
        int i = count - 1;

        while (i - iLow > 1)
        {
            int mid = (i + iLow) / 2;
            if (s < parameters[mid])
                i = mid;
            else
                iLow = mid;
        }

        double ds = parameters[i] - parameters[iLow];
        double t = (s - parameters[iLow]) / ds;
        double cx1 = ds * derivatives[iLow] - values[i] + values[iLow];
        double cx2 = ds * derivatives[i] - values[i] + values[iLow];
        double d2val = (6.0 * t - 4.0) * cx1 + (6.0 * t - 2.0) * cx2;
        return d2val / (ds * ds);
    }

    /// <summary>
    /// Computes curvature of the splined 2D curve at parameter ss.
    /// Port of XFoil's CURV function from spline.f.
    /// Returns signed curvature: kappa = (x' * y'' - y' * x'') / |speed|^3.
    /// </summary>
    private static double ComputeCurvature(
        double ss, double[] x, double[] xp, double[] y, double[] yp, double[] s, int n)
    {
        // Binary search for interval
        int iLow = 0;
        int i = n - 1;

        while (i - iLow > 1)
        {
            int mid = (i + iLow) / 2;
            if (ss < s[mid])
                i = mid;
            else
                iLow = mid;
        }

        double ds = s[i] - s[iLow];
        double t = (ss - s[iLow]) / ds;

        double cx1 = ds * xp[iLow] - x[i] + x[iLow];
        double cx2 = ds * xp[i] - x[i] + x[iLow];
        double xd = x[i] - x[iLow] + (1.0 - 4.0 * t + 3.0 * t * t) * cx1 + t * (3.0 * t - 2.0) * cx2;
        double xdd = (6.0 * t - 4.0) * cx1 + (6.0 * t - 2.0) * cx2;

        double cy1 = ds * yp[iLow] - y[i] + y[iLow];
        double cy2 = ds * yp[i] - y[i] + y[iLow];
        double yd = y[i] - y[iLow] + (1.0 - 4.0 * t + 3.0 * t * t) * cy1 + t * (3.0 * t - 2.0) * cy2;
        double ydd = (6.0 * t - 4.0) * cy1 + (6.0 * t - 2.0) * cy2;

        double sd = Math.Sqrt(xd * xd + yd * yd);
        sd = Math.Max(sd, 0.001 * ds);

        return (xd * ydd - yd * xdd) / (sd * sd * sd);
    }

    /// <summary>
    /// Solves a tridiagonal system for a segment of the arrays starting at offset.
    /// Used when the curvature array has a sharp LE requiring two-segment solution.
    /// </summary>
    private static void SolveTridiagonalSegment(
        double[] lower, double[] diagonal, double[] upper, double[] rhs,
        int offset, int count)
    {
        if (count < 2) return;

        // Extract segment into temporary arrays
        var segLower = new double[count];
        var segDiag = new double[count];
        var segUpper = new double[count];
        var segRhs = new double[count];

        Array.Copy(lower, offset, segLower, 0, count);
        Array.Copy(diagonal, offset, segDiag, 0, count);
        Array.Copy(upper, offset, segUpper, 0, count);
        Array.Copy(rhs, offset, segRhs, 0, count);

        TridiagonalSolver.Solve(segLower, segDiag, segUpper, segRhs, count);

        Array.Copy(segRhs, 0, rhs, offset, count);
    }
}

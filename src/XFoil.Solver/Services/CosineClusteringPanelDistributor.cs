using System.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: PANGEN
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND; f_xfoil/src/spline.f :: CURV/D2VAL
// Role in port: Directly ports XFoil's curvature-adaptive panel distribution algorithm into the managed solver.
// Differences: The numerical flow stays aligned with PANGEN, but the managed code factors the monolithic routine into traceable helpers, uses generic numeric types, and routes parity-sensitive float contraction through explicit helper calls rather than implicit REAL temporaries.
// Decision: Keep the decomposed managed structure and preserve the legacy evaluation order inside the parity-sensitive path because this file is the manual parity reference for paneling.
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
    // Legacy mapping: f_xfoil/src/xfoil.f :: PANGEN public entry wrapper.
    // Difference from legacy: The managed port exposes the algorithm as a typed service entry point that writes into LinearVortexPanelState and optionally selects parity precision explicitly.
    // Decision: Keep the public wrapper because it makes the direct PANGEN port reusable across solver paths.
    public static void Distribute(
        double[] inputX,
        double[] inputY,
        int inputCount,
        LinearVortexPanelState panel,
        int desiredNodeCount = 160,
        double curvatureWeight = 1.0,
        double trailingEdgeDensityRatio = 0.15,
        double curvatureDensityRatio = 0.2,
        bool useLegacyPrecision = false)
    {
        if (useLegacyPrecision)
        {
            DistributeCore<float>(
                inputX, inputY, inputCount, panel, desiredNodeCount,
                curvatureWeight, trailingEdgeDensityRatio, curvatureDensityRatio);
        }
        else
        {
            DistributeCore<double>(
                inputX, inputY, inputCount, panel, desiredNodeCount,
                curvatureWeight, trailingEdgeDensityRatio, curvatureDensityRatio);
        }
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: PANGEN.
    // Difference from legacy: The underlying algorithm is a direct port, but the managed version breaks the original routine into named phases, explicit trace hooks, and generic numeric operations so parity-sensitive float staging can be controlled precisely.
    // Decision: Keep the structured managed decomposition and preserve the legacy order within the main redistribution flow.
    private static void DistributeCore<T>(
        double[] inputX,
        double[] inputY,
        int inputCount,
        LinearVortexPanelState panel,
        int desiredNodeCount,
        double curvatureWeight,
        double trailingEdgeDensityRatio,
        double curvatureDensityRatio)
        where T : struct, IFloatingPointIeee754<T>
    {
        string traceScope = nameof(CosineClusteringPanelDistributor);
        if (inputCount < 2)
        {
            return;
        }

        _ = curvatureDensityRatio;

        int nb = inputCount;
        int n = desiredNodeCount;

        T zero = T.Zero;
        T one = T.One;
        T two = T.CreateChecked(2.0);
        T half = T.CreateChecked(0.5);
        T quarter = T.CreateChecked(0.25);
        T pointTwo = T.CreateChecked(0.2);
        T pointZeroZeroOne = T.CreateChecked(1.0e-3);
        T six = T.CreateChecked(6.0);
        T ten = T.CreateChecked(10.0);
        T twenty = T.CreateChecked(20.0);

        var sb = PoolSb<T>(nb);
        var xb = PoolXb<T>(nb);
        var yb = PoolYb<T>(nb);
        var xbp = PoolXbp<T>(nb);
        var ybp = PoolYbp<T>(nb);

        for (int i = 0; i < nb; i++)
        {
            xb[i] = T.CreateChecked(inputX[i]);
            yb[i] = T.CreateChecked(inputY[i]);
        }

        ParametricSpline.ComputeArcLength(xb, yb, sb, nb);
        ParametricSpline.FitSegmented(xb, xbp, sb, nb);
        ParametricSpline.FitSegmented(yb, ybp, sb, nb);
        for (int i = 0; i < nb; i++)
        {
            TraceBufferSplineNode(traceScope, i + 1, xb[i], yb[i], sb[i], xbp[i], ybp[i]);
        }
        

        T sbref = half * (sb[nb - 1] - sb[0]);

        var w5 = PoolW5<T>(nb);
        for (int i = 0; i < nb; i++)
        {
            w5[i] = T.Abs(ComputeCurvature(sb[i], xb, xbp, yb, ybp, sb, nb)) * sbref;
            TracePangenCurvatureNode(traceScope, "raw", i + 1, sb[i], w5[i]);
        }

        T sble = FindLeadingEdge(xb, xbp, yb, ybp, sb, nb);
        T cvle = T.Abs(ComputeCurvature(sble, xb, xbp, yb, ybp, sb, nb)) * sbref;

        int ible = 0;
        for (int i = 0; i < nb - 1; i++)
        {
            if (sble == sb[i] && sble == sb[i + 1])
            {
                ible = i + 1;
                break;
            }
        }

        T xble = ParametricSpline.Evaluate(sble, xb, xbp, sb, nb);
        T yble = ParametricSpline.Evaluate(sble, yb, ybp, sb, nb);
        T xbte = half * (xb[0] + xb[nb - 1]);
        T ybte = half * (yb[0] + yb[nb - 1]);
        T chbsq = ((xbte - xble) * (xbte - xble)) + ((ybte - yble) * (ybte - yble));

        int nk = 3;
        T cvsum = zero;
        for (int k = -nk; k <= nk; k++)
        {
            T frac = T.CreateChecked(k) / T.CreateChecked(nk);
            T sbk = sble + (frac * sbref / Max(cvle, twenty));
            T cvk = T.Abs(ComputeCurvature(sbk, xb, xbp, yb, ybp, sb, nb)) * sbref;
            cvsum += cvk;
            TracePangenLeadingEdgeSample(traceScope, "buffer", k, frac, sbk, cvk, cvsum);
        }

        T cvavg = cvsum / T.CreateChecked((2 * nk) + 1);
        if (ible != 0)
        {
            cvavg = ten;
        }

        TracePangenLeadingEdge(traceScope, "buffer", sble, xble, yble, xbte, ybte, cvle, cvavg, ible);

        T cc = six * T.CreateChecked(curvatureWeight);
        T cvte = cvavg * T.CreateChecked(trailingEdgeDensityRatio);
        w5[0] = cvte;
        w5[nb - 1] = cvte;

        // Legacy block: PANGEN curvature smoothing system.
        // Difference: The equation set is the same as the legacy tridiagonal diffusion smoother, but the managed code exposes the coefficients as named arrays and supports segmented solves around duplicated LE nodes explicitly.
        // Decision: Keep the named-array form because it is easier to inspect while preserving the same smoother semantics.
        T smool = Max(one / Max(cvavg, twenty), quarter / T.CreateChecked(n / 2));
        T smoosq = (smool * sbref) * (smool * sbref);

        var w1 = PoolW1<T>(nb);
        var w2 = PoolW2<T>(nb);
        var w3 = PoolW3<T>(nb);

        w2[0] = one;
        w3[0] = zero;

        for (int i = 1; i < nb - 1; i++)
        {
            T dsm = sb[i] - sb[i - 1];
            T dsp = sb[i + 1] - sb[i];
            T dso = half * (sb[i + 1] - sb[i - 1]);

            if (dsm == zero || dsp == zero)
            {
                w1[i] = zero;
                w2[i] = one;
                w3[i] = zero;
            }
            else
            {
                w1[i] = smoosq * (-one / dsm) / dso;
                w2[i] = (smoosq * ((one / dsp) + (one / dsm)) / dso) + one;
                w3[i] = smoosq * (-one / dsp) / dso;
            }
        }

        w1[nb - 1] = zero;
        w2[nb - 1] = one;

        for (int i = 1; i < nb - 1; i++)
        {
            if (sb[i] == sble || (ible != 0 && (i == ible - 1 || i == ible)))
            {
                w1[i] = zero;
                w2[i] = one;
                w3[i] = zero;
                w5[i] = cvle;
            }
            else if (sb[i - 1] < sble && sb[i] > sble)
            {
                T dsm = sb[i - 1] - sb[i - 2];
                T dsp = sble - sb[i - 1];
                T dso = half * (sble - sb[i - 2]);

                w1[i - 1] = smoosq * (-one / dsm) / dso;
                w2[i - 1] = (smoosq * ((one / dsp) + (one / dsm)) / dso) + one;
                w3[i - 1] = zero;
                w5[i - 1] += smoosq * cvle / (dsp * dso);

                dsm = sb[i] - sble;
                dsp = sb[i + 1] - sb[i];
                dso = half * (sb[i + 1] - sble);

                w1[i] = zero;
                w2[i] = (smoosq * ((one / dsp) + (one / dsm)) / dso) + one;
                w3[i] = smoosq * (-one / dsp) / dso;
                w5[i] += smoosq * cvle / (dsm * dso);
                break;
            }
        }

        if (ible == 0)
        {
            TridiagonalSolver.Solve(w1, w2, w3, w5, nb);
        }
        else
        {
            SolveTridiagonalSegment(w1, w2, w3, w5, 0, ible);
            SolveTridiagonalSegment(w1, w2, w3, w5, ible, nb - ible);
        }

        T cvmax = zero;
        for (int i = 0; i < nb; i++)
        {
            T abs = T.Abs(w5[i]);
            if (abs > cvmax)
            {
                cvmax = abs;
            }
        }

        if (cvmax > zero)
        {
            for (int i = 0; i < nb; i++)
            {
                w5[i] /= cvmax;
                TracePangenCurvatureNode(traceScope, "normalized", i + 1, sb[i], w5[i]);
            }
        }

        var w6 = PoolW6<T>(nb);
        ParametricSpline.FitSegmented(w5, w6, sb, nb);

        int nn = Ipfac * (n - 1) + 1;
        var snew = PoolSnew<T>(nn);

        T rdste = T.CreateChecked(0.667);
        T rtf = ((rdste - one) * two) + one;

        if (ible == 0)
        {
            T dsavg = (sb[nb - 1] - sb[0]) / (T.CreateChecked(nn - 3) + (two * rtf));
            snew[0] = sb[0];
            for (int i = 1; i < nn - 1; i++)
            {
                snew[i] = sb[0] + (dsavg * (T.CreateChecked(i - 1) + rtf));
            }

            snew[nn - 1] = sb[nb - 1];
        }
        else
        {
            int nfrac1 = (n * ible) / nb;
            int nn1 = Ipfac * (nfrac1 - 1) + 1;
            T dsavg1 = (sble - sb[0]) / (T.CreateChecked(nn1 - 2) + rtf);
            snew[0] = sb[0];
            for (int i = 1; i < nn1; i++)
            {
                snew[i] = sb[0] + (dsavg1 * (T.CreateChecked(i - 1) + rtf));
            }

            int nn2 = nn - nn1 + 1;
            T dsavg2 = (sb[nb - 1] - sble) / (T.CreateChecked(nn2 - 2) + rtf);
            for (int i = 1; i < nn2 - 1; i++)
            {
                snew[(i - 1) + nn1] = sble + (dsavg2 * (T.CreateChecked(i - 1) + rtf));
            }

            snew[nn - 1] = sb[nb - 1];
        }

        for (int i = 0; i < nn; i++)
        {
            TracePangenSnewNode(traceScope, "initial", 0, i + 1, snew[i]);
        }

        var ww1 = PoolWw1<T>(nn);
        var ww2 = PoolWw2<T>(nn);
        var ww3 = PoolWw3<T>(nn);
        var ww4 = PoolWw4<T>(nn);

        int legacyCarryCount = Math.Min(nn, nb);
        if (legacyCarryCount > 0)
        {
            // Classic XFoil reuses the same COMMON-block work arrays for the
            // curvature smoother and the Newton redistribution rows. The
            // first lower slot and the final upper slot are ignored by the
            // solver, but their carried values still appear in the trace.
            Array.Copy(w1, ww1, legacyCarryCount);
            Array.Copy(w2, ww2, legacyCarryCount);
            Array.Copy(w3, ww3, legacyCarryCount);
            ww1[0] = one;
        }

        // Legacy block: PANGEN Newton redistribution loop.
        // Difference: The row assembly and relaxation match the original algorithm, but the managed code keeps each curvature and residual term traceable and uses explicit fused helpers where REAL contraction matters.
        // Decision: Keep the traceable decomposition and preserve the legacy arithmetic ordering inside parity mode.
        for (int iter = 0; iter < 20; iter++)
        {
            T cv1 = ParametricSpline.Evaluate(snew[0], w5, w6, sb, nb);
            T cv2 = ParametricSpline.Evaluate(snew[1], w5, w6, sb, nb);
            T cvs1 = ParametricSpline.EvaluateDerivative(snew[0], w5, w6, sb, nb);
            T cvs2 = ParametricSpline.EvaluateDerivative(snew[1], w5, w6, sb, nb);

            // The legacy float norm keeps the square-sum fused before sqrt.
            T cavm = T.Sqrt(LegacyPrecisionMath.FusedMultiplyAdd(cv1, cv1, cv2 * cv2));
            T cavmS1;
            T cavmS2;
            if (cavm == zero)
            {
                cavmS1 = zero;
                cavmS2 = zero;
            }
            else
            {
                cavmS1 = cvs1 * cv1 / cavm;
                cavmS2 = cvs2 * cv2 / cavm;
            }

            for (int i = 1; i < nn - 1; i++)
            {
                T dsm = snew[i] - snew[i - 1];
                T dsp = snew[i] - snew[i + 1];
                T cv3 = ParametricSpline.Evaluate(snew[i + 1], w5, w6, sb, nb);
                T cvs3 = ParametricSpline.EvaluateDerivative(snew[i + 1], w5, w6, sb, nb);

                T cavp = T.Sqrt(LegacyPrecisionMath.FusedMultiplyAdd(cv3, cv3, cv2 * cv2));
                T cavpS2;
                T cavpS3;
                if (cavp == zero)
                {
                    cavpS2 = zero;
                    cavpS3 = zero;
                }
                else
                {
                    cavpS2 = cvs2 * cv2 / cavp;
                    cavpS3 = cvs3 * cv3 / cavp;
                }

                // The reference float build also contracts the curvature scaling
                // offsets used in the Newton row assembly.
                T fm = LegacyPrecisionMath.FusedMultiplyAdd(cc, cavm, one);
                T fp = LegacyPrecisionMath.FusedMultiplyAdd(cc, cavp, one);
                T rez = LegacyPrecisionMath.FusedMultiplyAdd(dsp, fp, dsm * fm);

                // The legacy float build contracts this lower-diagonal term to a
                // single-rounding multiply-add. Keep that behavior in parity mode.
                ww1[i] = LegacyPrecisionMath.FusedMultiplyAdd(cc * dsm, cavmS1, -fm);
                ww2[i] = AddRoundedBaseWithWideScaledCurvatureTerms(fp, fm, cc, dsp, cavpS2, dsm, cavmS2);
                ww3[i] = LegacyPrecisionMath.FusedMultiplyAdd(cc * dsp, cavpS3, -fp);
                ww4[i] = -rez;

                TracePangenNewtonState(
                    traceScope,
                    iter + 1,
                    i + 1,
                    snew[i],
                    dsm,
                    dsp,
                    cv1,
                    cv2,
                    cv3,
                    cvs1,
                    cvs2,
                    cvs3,
                    cavm,
                    cavmS1,
                    cavmS2,
                    cavp,
                    cavpS2,
                    cavpS3,
                    fm,
                    fp,
                    rez,
                    ww1[i],
                    ww2[i],
                    ww3[i],
                    ww4[i]);

                cv1 = cv2;
                cv2 = cv3;
                cvs1 = cvs2;
                cvs2 = cvs3;
                cavm = cavp;
                cavmS1 = cavpS2;
                cavmS2 = cavpS3;
            }

            ww2[0] = one;
            ww3[0] = zero;
            ww4[0] = zero;
            ww1[nn - 1] = zero;
            ww2[nn - 1] = one;
            ww4[nn - 1] = zero;

            if (rtf != one)
            {
                int i2 = 1;
                // The TE clustering constraint keeps a denormal residual in the
                // legacy float build only when this inner sum is fused.
                ww4[i2] = -LegacyPrecisionMath.FusedMultiplyAdd(
                    rtf,
                    snew[i2] - snew[i2 + 1],
                    snew[i2] - snew[i2 - 1]);
                ww1[i2] = -one;
                ww2[i2] = one + rtf;
                ww3[i2] = -rtf;

                int i3 = nn - 2;
                ww4[i3] = -LegacyPrecisionMath.FusedMultiplyAdd(
                    rtf,
                    snew[i3] - snew[i3 - 1],
                    snew[i3] - snew[i3 + 1]);
                ww3[i3] = -one;
                ww2[i3] = one + rtf;
                ww1[i3] = -rtf;
            }

            if (ible != 0)
            {
                int nn1 = Ipfac * (((n * ible) / nb) - 1) + 1;
                int leIdx = nn1 - 1;
                ww1[leIdx] = zero;
                ww2[leIdx] = one;
                ww3[leIdx] = zero;
                ww4[leIdx] = sble - snew[leIdx];
            }

            for (int i = 0; i < nn; i++)
            {
                TracePangenNewtonRow(traceScope, iter + 1, i + 1, snew[i], ww1[i], ww2[i], ww3[i], ww4[i]);
            }
            

            TridiagonalSolver.Solve(ww1, ww2, ww3, ww4, nn);
            

            for (int i = 0; i < nn; i++)
            {
                TracePangenNewtonDelta(traceScope, iter + 1, i + 1, ww4[i]);
            }

            T rlx = one;
            T dmax = zero;
            for (int i = 0; i < nn - 1; i++)
            {
                T ds = snew[i + 1] - snew[i];
                T dds = ww4[i + 1] - ww4[i];
                T rlxBefore = rlx;
                T dmaxBefore = dmax;
                T dsrat = zero;
                if (ds != zero)
                {
                    dsrat = one + ((rlx * dds) / ds);
                    if (dsrat > T.CreateChecked(4.0))
                    {
                        rlx = (T.CreateChecked(3.0) * ds) / dds;
                    }
                    if (dsrat < T.CreateChecked(0.2))
                    {
                        rlx = (T.CreateChecked(-0.8) * ds) / dds;
                    }
                }

                T abs = T.Abs(ww4[i]);
                if (abs > dmax)
                {
                    dmax = abs;
                }

                TracePangenRelaxationStep(traceScope, iter + 1, i + 1, ds, dds, dsrat, rlxBefore, rlx, dmaxBefore, dmax);
            }

            for (int i = 1; i < nn - 1; i++)
            {
                // Updating the redistributed arc-length nodes also needs a
                // fused multiply-add to follow the legacy float rounding path.
                snew[i] = LegacyPrecisionMath.FusedMultiplyAdd(rlx, ww4[i], snew[i]);
            }

            TracePangenIteration(traceScope, iter + 1, dmax, rlx);
            for (int i = 0; i < nn; i++)
            {
                TracePangenSnewNode(traceScope, "updated", iter + 1, i + 1, snew[i]);
            }

            if (T.Abs(dmax) < pointZeroZeroOne)
            {
                break;
            }
        }

        var sOut = PoolSOut<T>(n);
        var xOut = PoolXOut<T>(n);
        var yOut = PoolYOut<T>(n);

        
        for (int i = 0; i < n; i++)
        {
            int ind = Ipfac * i;
            sOut[i] = snew[ind];
            xOut[i] = ParametricSpline.Evaluate(snew[ind], xb, xbp, sb, nb);
            yOut[i] = ParametricSpline.Evaluate(snew[ind], yb, ybp, sb, nb);
            // Match XFoil's trace order: the "final" SNEW nodes are emitted
            // immediately after subsampling, before the corner pass and before
            // SCALC/SEGSPL rebuild the current-panel spline state.
            TracePangenSnewNode(traceScope, "final", 0, i + 1, sOut[i]);
        }

        int finalN = n;
        for (int ib = 0; ib < nb - 1; ib++)
        {
            if (sb[ib] == sb[ib + 1])
            {
                T sbcorn = sb[ib];
                T xbcorn = xb[ib];
                T ybcorn = yb[ib];

                for (int i = 0; i < finalN; i++)
                {
                    if (sOut[i] <= sbcorn)
                    {
                        continue;
                    }

                    var newX = PoolNewX<T>(finalN + 1);
                    var newY = PoolNewY<T>(finalN + 1);
                    var newS = PoolNewS<T>(finalN + 1);

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

                    if (i - 2 >= 0)
                    {
                        sOut[i - 1] = half * (sOut[i] + sOut[i - 2]);
                        xOut[i - 1] = ParametricSpline.Evaluate(sOut[i - 1], xb, xbp, sb, nb);
                        yOut[i - 1] = ParametricSpline.Evaluate(sOut[i - 1], yb, ybp, sb, nb);
                    }

                    if (i + 2 < finalN)
                    {
                        sOut[i + 1] = half * (sOut[i] + sOut[i + 2]);
                        xOut[i + 1] = ParametricSpline.Evaluate(sOut[i + 1], xb, xbp, sb, nb);
                        yOut[i + 1] = ParametricSpline.Evaluate(sOut[i + 1], yb, ybp, sb, nb);
                    }

                    break;
                }
            }
        }

        var finalArc = PoolFinalArc<T>(finalN);
        ParametricSpline.ComputeArcLength(xOut, yOut, finalArc, finalN);

        var finalXp = PoolFinalXp<T>(finalN);
        var finalYp = PoolFinalYp<T>(finalN);
        ParametricSpline.FitSegmented(xOut, finalXp, finalArc, finalN);
        ParametricSpline.FitSegmented(yOut, finalYp, finalArc, finalN);
        for (int i = 0; i < finalN; i++)
        {
            TracePangenPanelNode(traceScope, i + 1, xOut[i], yOut[i], finalArc[i], finalXp[i], finalYp[i]);
        }

        T sle = FindLeadingEdge(xOut, finalXp, yOut, finalYp, finalArc, finalN);

        T xle = ParametricSpline.Evaluate(sle, xOut, finalXp, finalArc, finalN);
        T yle = ParametricSpline.Evaluate(sle, yOut, finalYp, finalArc, finalN);
        T xte = half * (xOut[0] + xOut[finalN - 1]);
        T yte = half * (yOut[0] + yOut[finalN - 1]);
        T chordT = T.Sqrt(((xte - xle) * (xte - xle)) + ((yte - yle) * (yte - yle)));

        TracePangenLeadingEdge(traceScope, "final", sle, xle, yle, xte, yte, zero, zero, 0);

        panel.Resize(finalN);
        for (int i = 0; i < finalN; i++)
        {
            panel.X[i] = double.CreateChecked(xOut[i]);
            panel.Y[i] = double.CreateChecked(yOut[i]);
            panel.ArcLength[i] = double.CreateChecked(finalArc[i]);
            panel.XDerivative[i] = double.CreateChecked(finalXp[i]);
            panel.YDerivative[i] = double.CreateChecked(finalYp[i]);
        }

        panel.TrailingEdgeX = double.CreateChecked(xte);
        panel.TrailingEdgeY = double.CreateChecked(yte);
        panel.LeadingEdgeX = double.CreateChecked(xle);
        panel.LeadingEdgeY = double.CreateChecked(yle);
        panel.LeadingEdgeArcLength = double.CreateChecked(sle);
        panel.Chord = double.CreateChecked(chordT);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: PANGEN Newton main-diagonal row W2(I).
    // Difference from legacy: The managed port needs an explicit mixed-width helper because
    // the reference REAL build rounds FP+FM to float first, then evaluates the mixed curvature
    // sum asymmetrically: DSP*CAVP_S2 stays wide while DSM*CAVM_S2 contracts back to REAL before
    // the wide scaled add and final cast to REAL. A symmetric wide-product replay lands one ULP
    // low on the diversified paneling rig, while rounding both products breaks the reduced 12-panel
    // rung that originally proved this helper.
    // Decision: Keep the localized helper here because this mixed-width staging is specific to
    // the PANGEN Newton diagonal parity path and should not leak into the default managed branch.
    private static T AddRoundedBaseWithWideScaledCurvatureTerms<T>(
        T leftBase,
        T rightBase,
        T scale,
        T left1,
        T right1,
        T left2,
        T right2)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            // Fortran (xfoil.f line ~1973): W2(I) = FP + FM + CC*(DSP*CAVP_S2 + DSM*CAVM_S2)
            // Pure float arithmetic, left-to-right with operator precedence:
            //   inner = (left1*right1) + (left2*right2)
            //   scaled = scale * inner
            //   result = (leftBase + rightBase) + scaled
            // The previous double-promoted variant drifted 1 ULP from Fortran for
            // NACA 0009+ panel sets at PANGEN iter 2.
            float lb = float.CreateChecked(leftBase);
            float rb = float.CreateChecked(rightBase);
            float sc = float.CreateChecked(scale);
            float l1 = float.CreateChecked(left1);
            float r1 = float.CreateChecked(right1);
            float l2 = float.CreateChecked(left2);
            float r2 = float.CreateChecked(right2);
            float p1 = l1 * r1;
            float p2 = l2 * r2;
            float inner = p1 + p2;
            float scaled = sc * inner;
            float baseSum = lb + rb;
            return T.CreateChecked(baseSum + scaled);
        }

        return (leftBase + rightBase) + (scale * ((left1 * right1) + (left2 * right2)));
    }

    /// <summary>
    /// Finds the leading edge arc-length parameter using XFoil's LEFIND algorithm.
    /// The LE is defined as the point where the surface tangent is perpendicular
    /// to the chord line connecting (X(SLE),Y(SLE)) to the TE midpoint.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xgeom.f :: LEFIND.
    // Difference from legacy: The Newton solve is materially the same, but the managed port packages it as a reusable generic helper and emits optional trace events through the surrounding distributor.
    // Decision: Keep the helper because it makes the PANGEN port readable without changing the LE search algorithm.
    private static T FindLeadingEdge<T>(T[] x, T[] xp, T[] y, T[] yp, T[] s, int n)
        where T : struct, IFloatingPointIeee754<T>
    {
        T dseps = (s[n - 1] - s[0]) * T.CreateChecked(1.0e-5);
        T half = T.CreateChecked(0.5);
        T pointZeroTwo = T.CreateChecked(0.02);
        T xte = half * (x[0] + x[n - 1]);
        T yte = half * (y[0] + y[n - 1]);

        T sle = s[0];
        for (int i = 2; i < n - 2; i++)
        {
            T dxte = x[i] - xte;
            T dyte = y[i] - yte;
            T dx = x[i + 1] - x[i];
            T dy = y[i + 1] - y[i];
            T dotp = (dxte * dx) + (dyte * dy);
            if (dotp < T.Zero)
            {
                sle = s[i];
                break;
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (sle == s[i] && sle == s[i + 1])
            {
                return sle;
            }
        }

        for (int iter = 0; iter < 50; iter++)
        {
            T xleVal = ParametricSpline.Evaluate(sle, x, xp, s, n);
            T yleVal = ParametricSpline.Evaluate(sle, y, yp, s, n);
            T dxds = ParametricSpline.EvaluateDerivative(sle, x, xp, s, n);
            T dyds = ParametricSpline.EvaluateDerivative(sle, y, yp, s, n);
            T dxdd = EvaluateSecondDerivative(sle, x, xp, s, n);
            T dydd = EvaluateSecondDerivative(sle, y, yp, s, n);

            T xchord = xleVal - xte;
            T ychord = yleVal - yte;
            T res = (xchord * dxds) + (ychord * dyds);
            T ress = (dxds * dxds) + (dyds * dyds) + (xchord * dxdd) + (ychord * dydd);
            T dsle = -res / ress;

            T limit = pointZeroTwo * T.Abs(xchord + ychord);
            if (dsle < -limit)
            {
                dsle = -limit;
            }
            if (dsle > limit)
            {
                dsle = limit;
            }

            sle += dsle;
            if (T.Abs(dsle) < dseps)
            {
                return sle;
            }
        }

        return sle;
    }

    /// <summary>
    /// Evaluates the second derivative d2X/dS2 at parameter s.
    /// Port of XFoil's D2VAL routine from spline.f.
    /// </summary>
    // Legacy mapping: f_xfoil/src/spline.f :: D2VAL.
    // Difference from legacy: The second-derivative reconstruction is the same spline identity, but the managed version computes it as a local helper against the managed spline arrays.
    // Decision: Keep the helper because it isolates one reusable spline primitive used by the paneling port.
    private static T EvaluateSecondDerivative<T>(T s, T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        int iLow = 0;
        int i = count - 1;

        while (i - iLow > 1)
        {
            int mid = (i + iLow) / 2;
            if (s < parameters[mid])
            {
                i = mid;
            }
            else
            {
                iLow = mid;
            }
        }

        T ds = parameters[i] - parameters[iLow];
        T t = (s - parameters[iLow]) / ds;
        T cx1 = (ds * derivatives[iLow]) - values[i] + values[iLow];
        T cx2 = (ds * derivatives[i]) - values[i] + values[iLow];
        T d2val = ((T.CreateChecked(6.0) * t) - T.CreateChecked(4.0)) * cx1
                + ((T.CreateChecked(6.0) * t) - T.CreateChecked(2.0)) * cx2;
        return d2val / (ds * ds);
    }

    /// <summary>
    /// Computes curvature of the splined 2D curve at parameter ss.
    /// Port of XFoil's CURV function from spline.f.
    /// Returns signed curvature: kappa = (x' * y'' - y' * x'') / |speed|^3.
    /// </summary>
    // Legacy mapping: f_xfoil/src/spline.f :: CURV.
    // Difference from legacy: The curvature formula is equivalent, but the managed port expresses the derivative chain through reusable spline helpers and explicit fused operations where parity requires it.
    // Decision: Keep the helper because it makes the curvature kernel auditable while preserving the legacy result.
    private static T ComputeCurvature<T>(T ss, T[] x, T[] xp, T[] y, T[] yp, T[] s, int n)
        where T : struct, IFloatingPointIeee754<T>
    {
        T one = T.One;
        T two = T.CreateChecked(2.0);
        T three = T.CreateChecked(3.0);
        T four = T.CreateChecked(4.0);
        T six = T.CreateChecked(6.0);

        int iLow = 0;
        int i = n - 1;

        while (i - iLow > 1)
        {
            int mid = (i + iLow) / 2;
            if (ss < s[mid])
            {
                i = mid;
            }
            else
            {
                iLow = mid;
            }
        }

        T ds = s[i] - s[iLow];
        T t = (ss - s[iLow]) / ds;

        // Fortran SEVAL: CX1 = DS*XP(ILOW) - X(I) + X(ILOW)  (separate mul then add/sub)
        T cx1 = (ds * xp[iLow]) - x[i] + x[iLow];
        T cx2 = (ds * xp[i]) - x[i] + x[iLow];
        // Classic XFoil evaluates this cubic coefficient in single precision with a
        // fused add-multiply on current toolchains. Keep that contraction in the
        // parity path so the legacy panel replay stays bitwise aligned, while the
        // default double path keeps normal arithmetic.
        T xFactor1 = LegacyPrecisionMath.FusedMultiplyAdd(three * t, t, one - (four * t));
        T xFactor2 = t * ((three * t) - two);
        T xDelta = x[i] - x[iLow];
        T xTerm1 = xFactor1 * cx1;
        T xTerm2 = xFactor2 * cx2;
        T xd = xDelta + xTerm1 + xTerm2;
        // Fortran SEVAL second derivative: separate operations
        T xdd = ((six * t) - four) * cx1 + ((six * t) - two) * cx2;

        // Fortran SEVAL: CY1 = DS*YP(ILOW) - Y(I) + Y(ILOW)  (separate)
        T cy1 = (ds * yp[iLow]) - y[i] + y[iLow];
        T cy2 = (ds * yp[i]) - y[i] + y[iLow];
        T yFactor1 = LegacyPrecisionMath.FusedMultiplyAdd(three * t, t, one - (four * t));
        T yFactor2 = t * ((three * t) - two);
        T yDelta = y[i] - y[iLow];
        T yTerm1 = yFactor1 * cy1;
        T yTerm2 = yFactor2 * cy2;
        T yd = yDelta + yTerm1 + yTerm2;
        // Fortran SEVAL second derivative: separate operations
        T ydd = ((six * t) - four) * cy1 + ((six * t) - two) * cy2;

        T sd = ParametricSpline.ComputeDistance(xd, yd);
        T minSd = T.CreateChecked(0.001) * ds;
        if (sd < minSd)
        {
            sd = minSd;
        }

        // Fortran: separate multiply-subtract for curvature cross product
        T curvatureNumerator = (xd * ydd) - (yd * xdd);
        T curvature = curvatureNumerator / (sd * sd * sd);
        TraceCurvatureEvaluation(
            nameof(ParametricSpline),
            "CURV",
            iLow + 1,
            i + 1,
            ss,
            ds,
            t,
            xDelta,
            yDelta,
            cx1,
            cx2,
            cy1,
            cy2,
            xFactor1,
            xFactor2,
            yFactor1,
            yFactor2,
            xTerm1,
            xTerm2,
            yTerm1,
            yTerm2,
            xd,
            xdd,
            yd,
            ydd,
            sd,
            curvature);
        return curvature;
    }

    /// <summary>
    /// Solves a tridiagonal system for a segment of the arrays starting at offset.
    /// Used when the curvature array has a sharp LE requiring two-segment solution.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xfoil.f :: PANGEN segmented tridiagonal solves around duplicated LE nodes.
    // Difference from legacy: The original code reuses a common solver on array slices; the managed port exposes the segmented solve explicitly so the LE split is easier to trace.
    // Decision: Keep the helper because it clarifies the duplicated-node branch without changing the solve order.
    private static void SolveTridiagonalSegment<T>(T[] lower, T[] diagonal, T[] upper, T[] rhs, int offset, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (count < 2)
        {
            return;
        }

        var segLower = PoolSegLower<T>(count);
        var segDiag = PoolSegDiag<T>(count);
        var segUpper = PoolSegUpper<T>(count);
        var segRhs = PoolSegRhs<T>(count);

        Array.Copy(lower, offset, segLower, 0, count);
        Array.Copy(diagonal, offset, segDiag, 0, count);
        Array.Copy(upper, offset, segUpper, 0, count);
        Array.Copy(rhs, offset, segRhs, 0, count);

        TridiagonalSolver.Solve(segLower, segDiag, segUpper, segRhs, count);

        Array.Copy(segRhs, 0, rhs, offset, count);
    }

    // Legacy mapping: none; these trace helpers are managed-only instrumentation around the direct PANGEN port.
    // Difference from legacy: The original Fortran routine writes nothing comparable unless debug tracing is compiled in, while the managed port keeps structured trace hooks available during parity work.
    // Decision: Keep the trace-helper block because it is essential for binary mismatch localization and does not change solver behavior.
    private static void TracePangenCurvatureNode<T>(string scope, string stage, int index, T arcLength, T value)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TraceBufferSplineNode<T>(
        string scope,
        int index,
        T x,
        T y,
        T arcLength,
        T xp,
        T yp)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenPanelNode<T>(
        string scope,
        int index,
        T x,
        T y,
        T arcLength,
        T xp,
        T yp)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenLeadingEdge<T>(
        string scope,
        string stage,
        T sle,
        T xle,
        T yle,
        T xte,
        T yte,
        T cvle,
        T cvavg,
        int ible)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenLeadingEdgeSample<T>(
        string scope,
        string stage,
        int sample,
        T frac,
        T parameter,
        T curvatureValue,
        T curvatureSum)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenIteration<T>(string scope, int iteration, T dmax, T rlx)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenNewtonRow<T>(
        string scope,
        int iteration,
        int index,
        T arcLength,
        T lower,
        T diagonal,
        T upper,
        T residual)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenNewtonState<T>(
        string scope,
        int iteration,
        int index,
        T arcLength,
        T dsm,
        T dsp,
        T cv1,
        T cv2,
        T cv3,
        T cvs1,
        T cvs2,
        T cvs3,
        T cavm,
        T cavmS1,
        T cavmS2,
        T cavp,
        T cavpS2,
        T cavpS3,
        T fm,
        T fp,
        T rez,
        T lower,
        T diagonal,
        T upper,
        T residual)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenNewtonDelta<T>(string scope, int iteration, int index, T delta)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenRelaxationStep<T>(
        string scope,
        int iteration,
        int index,
        T ds,
        T dds,
        T dsrat,
        T rlxBefore,
        T rlxAfter,
        T dmaxBefore,
        T dmaxAfter)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TracePangenSnewNode<T>(string scope, string stage, int iteration, int index, T value)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    private static void TraceCurvatureEvaluation<T>(
        string scope,
        string routine,
        int lowerIndex,
        int upperIndex,
        T parameter,
        T ds,
        T t,
        T xDelta,
        T yDelta,
        T cx1,
        T cx2,
        T cy1,
        T cy2,
        T xFactor1,
        T xFactor2,
        T yFactor1,
        T yFactor2,
        T xTerm1,
        T xTerm2,
        T yTerm1,
        T yTerm2,
        T xd,
        T xdd,
        T yd,
        T ydd,
        T sd,
        T curvature)
        where T : struct, IFloatingPointIeee754<T>
    {
    }

    // Legacy mapping: managed-only scalar helper corresponding to Fortran intrinsic MAX/AMAX1 usage.
    // Difference from legacy: The original routine uses the intrinsic inline, while the managed port factors the comparison into a local generic helper.
    // Decision: Keep the helper because it keeps the generic port readable.
    private static T Max<T>(T a, T b)
        where T : struct, IFloatingPointIeee754<T>
        => a > b ? a : b;

    // -----------------------------------------------------------------
    // ThreadStatic working-buffer pool for DistributeCore. Distribute is
    // called once per AnalyzeViscous — ~30 generic scratch arrays per
    // call used to allocate fresh every time. These slots keep them
    // sized-to-max across cases. Pairs of double/float fields plus a
    // typed dispatcher mirror the PanelGeometryBuilder pattern.
    // -----------------------------------------------------------------
    [ThreadStatic] private static double[]? _sbD;   [ThreadStatic] private static float[]? _sbF;
    [ThreadStatic] private static double[]? _xbD;   [ThreadStatic] private static float[]? _xbF;
    [ThreadStatic] private static double[]? _ybD;   [ThreadStatic] private static float[]? _ybF;
    [ThreadStatic] private static double[]? _xbpD;  [ThreadStatic] private static float[]? _xbpF;
    [ThreadStatic] private static double[]? _ybpD;  [ThreadStatic] private static float[]? _ybpF;
    [ThreadStatic] private static double[]? _w5D;   [ThreadStatic] private static float[]? _w5F;
    [ThreadStatic] private static double[]? _w1D;   [ThreadStatic] private static float[]? _w1F;
    [ThreadStatic] private static double[]? _w2D;   [ThreadStatic] private static float[]? _w2F;
    [ThreadStatic] private static double[]? _w3D;   [ThreadStatic] private static float[]? _w3F;
    [ThreadStatic] private static double[]? _w6D;   [ThreadStatic] private static float[]? _w6F;
    [ThreadStatic] private static double[]? _snewD; [ThreadStatic] private static float[]? _snewF;
    [ThreadStatic] private static double[]? _ww1D;  [ThreadStatic] private static float[]? _ww1F;
    [ThreadStatic] private static double[]? _ww2D;  [ThreadStatic] private static float[]? _ww2F;
    [ThreadStatic] private static double[]? _ww3D;  [ThreadStatic] private static float[]? _ww3F;
    [ThreadStatic] private static double[]? _ww4D;  [ThreadStatic] private static float[]? _ww4F;
    [ThreadStatic] private static double[]? _sOutD; [ThreadStatic] private static float[]? _sOutF;
    [ThreadStatic] private static double[]? _xOutD; [ThreadStatic] private static float[]? _xOutF;
    [ThreadStatic] private static double[]? _yOutD; [ThreadStatic] private static float[]? _yOutF;
    [ThreadStatic] private static double[]? _newXD; [ThreadStatic] private static float[]? _newXF;
    [ThreadStatic] private static double[]? _newYD; [ThreadStatic] private static float[]? _newYF;
    [ThreadStatic] private static double[]? _newSD; [ThreadStatic] private static float[]? _newSF;
    [ThreadStatic] private static double[]? _finalArcD;  [ThreadStatic] private static float[]? _finalArcF;
    [ThreadStatic] private static double[]? _finalXpD;   [ThreadStatic] private static float[]? _finalXpF;
    [ThreadStatic] private static double[]? _finalYpD;   [ThreadStatic] private static float[]? _finalYpF;
    [ThreadStatic] private static double[]? _segLowerD;  [ThreadStatic] private static float[]? _segLowerF;
    [ThreadStatic] private static double[]? _segDiagD;   [ThreadStatic] private static float[]? _segDiagF;
    [ThreadStatic] private static double[]? _segUpperD;  [ThreadStatic] private static float[]? _segUpperF;
    [ThreadStatic] private static double[]? _segRhsD;    [ThreadStatic] private static float[]? _segRhsF;

    private static T[] Pool<T>(int n, ref double[]? dSlot, ref float[]? fSlot)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(double))
        {
            var a = dSlot;
            if (a is null || a.Length < n) { a = new double[n]; dSlot = a; }
            else { Array.Clear(a, 0, n); }
            return (T[])(object)a;
        }
        else
        {
            var a = fSlot;
            if (a is null || a.Length < n) { a = new float[n]; fSlot = a; }
            else { Array.Clear(a, 0, n); }
            return (T[])(object)a;
        }
    }

    private static T[] PoolSb<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _sbD, ref _sbF);
    private static T[] PoolXb<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _xbD, ref _xbF);
    private static T[] PoolYb<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ybD, ref _ybF);
    private static T[] PoolXbp<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _xbpD, ref _xbpF);
    private static T[] PoolYbp<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ybpD, ref _ybpF);
    private static T[] PoolW5<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _w5D, ref _w5F);
    private static T[] PoolW1<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _w1D, ref _w1F);
    private static T[] PoolW2<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _w2D, ref _w2F);
    private static T[] PoolW3<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _w3D, ref _w3F);
    private static T[] PoolW6<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _w6D, ref _w6F);
    private static T[] PoolSnew<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _snewD, ref _snewF);
    private static T[] PoolWw1<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ww1D, ref _ww1F);
    private static T[] PoolWw2<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ww2D, ref _ww2F);
    private static T[] PoolWw3<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ww3D, ref _ww3F);
    private static T[] PoolWw4<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _ww4D, ref _ww4F);
    private static T[] PoolSOut<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _sOutD, ref _sOutF);
    private static T[] PoolXOut<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _xOutD, ref _xOutF);
    private static T[] PoolYOut<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _yOutD, ref _yOutF);
    private static T[] PoolNewX<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _newXD, ref _newXF);
    private static T[] PoolNewY<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _newYD, ref _newYF);
    private static T[] PoolNewS<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _newSD, ref _newSF);
    private static T[] PoolFinalArc<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _finalArcD, ref _finalArcF);
    private static T[] PoolFinalXp<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _finalXpD, ref _finalXpF);
    private static T[] PoolFinalYp<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _finalYpD, ref _finalYpF);
    private static T[] PoolSegLower<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _segLowerD, ref _segLowerF);
    private static T[] PoolSegDiag<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _segDiagD, ref _segDiagF);
    private static T[] PoolSegUpper<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _segUpperD, ref _segUpperF);
    private static T[] PoolSegRhs<T>(int n) where T : struct, IFloatingPointIeee754<T> => Pool<T>(n, ref _segRhsD, ref _segRhsF);
}

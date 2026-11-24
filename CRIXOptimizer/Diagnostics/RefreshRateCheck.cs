// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Checks the refresh rate the VR runtime reports against the frames that really arrive (no Unity dependency,
    /// unit tested). Since Gorilla Tag 1.1.150 one user's runtime (Meta Quest Link, OpenXR) reported 72 Hz for whole
    /// sessions in which the game ran at 112-117 FPS. CRIX then aimed at 72 FPS, counted hitches against a 72 Hz frame
    /// and told the user the game could not run faster than 72 FPS. A game cannot present frames faster than the display
    /// refreshes, so when the typical (median) frame time stays clearly shorter than the reported refresh interval for
    /// <see cref="ConfirmSeconds"/> seconds in a row, the real refresh rate is the standard rate those frames fit. Only
    /// ever raises the rate (frames slower than the refresh are load or reprojection, not a slower display); a change in
    /// the reported rate starts the check over.
    /// </summary>
    internal sealed class RefreshRateCheck
    {
        public const int ConfirmSeconds = 20;
        public const double FasterBy = 1.1;
        private static readonly float[] StandardRates = { 72f, 80f, 90f, 96f, 100f, 120f, 144f, 165f, 240f };

        private readonly float[] frames = new float[512];
        private int count;
        private int fastSeconds;
        private float basis;

        /// <summary>The refresh rate the frames show (0 = none: the reported rate stands).</summary>
        public float Measured { get; private set; }
        /// <summary>The reported rate <see cref="Measured"/> replaces.</summary>
        public float Reported => basis;

        public void AddFrame(float ms)
        {
            if (ms > 0f && ms < 1000f && count < frames.Length)
                frames[count++] = ms;
        }

        /// <summary>Once a second, with the rate the runtime reports now: the rate to use.</summary>
        public float EndSecond(float reportedHz)
        {
            float median = 0f;
            if (count > 0)
            {
                Array.Sort(frames, 0, count);
                median = frames[count / 2];
            }
            int frameCount = count;
            count = 0;
            if (reportedHz <= 1f)
                return reportedHz;
            if (reportedHz != basis)
            {
                basis = reportedHz;
                fastSeconds = 0;
                Measured = 0f;
            }
            if (frameCount >= 20 && median > 0f)
            {
                double fps = 1000.0 / median;
                if (fps > reportedHz * FasterBy)
                {
                    if (++fastSeconds >= ConfirmSeconds)
                    {
                        float rate = Standard(fps);
                        if (rate > Measured)
                            Measured = rate;
                    }
                }
                else
                {
                    fastSeconds = 0;
                }
            }
            return Measured > reportedHz ? Measured : reportedHz;
        }

        /// <summary>The smallest standard refresh rate that frames at this rate fit (a few percent of timing noise allowed).</summary>
        public static float Standard(double fps)
        {
            foreach (float r in StandardRates)
            {
                if (fps <= r * 1.03)
                    return r;
            }
            return (float)Math.Round(fps);
        }
    }
}

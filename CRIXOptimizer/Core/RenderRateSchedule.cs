// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Time-based "at most N renders per second" schedule for a group of cameras (no Unity dependency, unit tested).
    ///
    /// Each camera has a due time. It may render on the first frame at or after it (within half a frame); the due
    /// time then advances by exactly one period, so a camera renders at most rate x seconds + 1 times over any stretch.
    /// After a hitch it renders once and starts a new period (no catch-up burst). Per frame at most
    /// ceil(cameras x rate x frame time) cameras render, earliest due first, so cameras do not pile up in the same
    /// frame; a due camera over that budget simply renders next frame (its due time is not moved). When the game runs
    /// slower than the rate the budget covers every camera, so each renders every frame.
    /// </summary>
    internal static class RenderRateSchedule
    {
        private const double GoldenRatioFraction = 0.6180339887498949;

        /// <summary>Phase in [0, 1) for the n-th limited camera: golden-ratio spacing spreads any number of cameras
        /// over the period (0, 0.62, 0.24, 0.85, ...), whatever their instance IDs.</summary>
        public static double PhaseFor(int index)
        {
            double phase = index * GoldenRatioFraction;
            return phase - Math.Floor(phase);
        }

        /// <summary>First due time: the camera's phase within one period from now.</summary>
        public static double InitialDue(double now, double period, double phase)
        {
            return now + period * phase;
        }

        /// <summary>How many of <paramref name="cameras"/> may render in one frame of length <paramref name="frameTime"/>.</summary>
        public static int FrameBudget(int cameras, double rate, double frameTime)
        {
            if (cameras <= 0)
                return 0;
            if (rate <= 0 || frameTime <= 0)
                return 1;
            double share = cameras * rate * frameTime;
            return share >= cameras ? cameras : Math.Max(1, (int)Math.Ceiling(share - 1e-9));
        }

        /// <summary>
        /// Decides which cameras render this frame. Only entries with <paramref name="eligible"/> set take part (a
        /// camera its owner switched off is not eligible). <paramref name="frameTime"/> is the typical (averaged) frame
        /// time, not the last frame's, so one hitch frame does not let every camera render at once. Sets
        /// <paramref name="render"/>, advances the due time of each camera that renders, and returns how many render.
        /// </summary>
        public static int Plan(double[] due, bool[] eligible, bool[] render, int count, double now, double period, double frameTime)
        {
            int candidates = 0;
            for (int i = 0; i < count; i++)
            {
                render[i] = false;
                if (eligible[i])
                    candidates++;
            }
            if (candidates == 0)
                return 0;
            if (period <= 0)
            {
                for (int i = 0; i < count; i++)
                    render[i] = eligible[i];
                return candidates;
            }
            int budget = FrameBudget(candidates, 1.0 / period, frameTime);
            double slack = frameTime > 0 && frameTime < period ? frameTime * 0.5 : 0;
            int rendered = 0;
            while (rendered < budget)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (eligible[i] && !render[i] && now + slack >= due[i] && (best < 0 || due[i] < due[best]))
                        best = i;
                }
                if (best < 0)
                    break;
                render[best] = true;
                rendered++;
                due[best] += period;
                // Fell behind (hitch, pause, owner had it off): start a new period now instead of catching up.
                if (due[best] <= now)
                    due[best] = now + period;
            }
            return rendered;
        }
    }
}

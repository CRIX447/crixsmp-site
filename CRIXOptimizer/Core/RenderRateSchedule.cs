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
    /// Due times lie on a fixed grid per camera, (k + phase) x period on the absolute clock, so cameras with different
    /// phases keep apart for good. After a hitch a camera renders once and moves to its next grid point (no catch-up
    /// burst, and cameras that were overdue together do not stay locked together). Per frame at most
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

        /// <summary>First due time: the camera's first grid point at or after now.</summary>
        public static double InitialDue(double now, double period, double phase)
        {
            return NextOnGrid(now, period, phase, false);
        }

        /// <summary>The camera's grid point (k + phase) x period at or after (or, if <paramref name="strictlyAfter"/>,
        /// after) <paramref name="time"/>.</summary>
        public static double NextOnGrid(double time, double period, double phase, bool strictlyAfter)
        {
            double k = Math.Ceiling(time / period - phase);
            double t = (k + phase) * period;
            if (t < time || (strictlyAfter && t <= time))
                t += period;
            return t;
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
        /// Typical frame time for the budget: a slow average of frame times that ignores single hitch frames (more
        /// than twice the typical value) and only follows a real slowdown once it lasts ten frames.
        /// </summary>
        public static double SmoothFrameTime(double typical, ref int outliers, double frameTime)
        {
            if (frameTime <= 0)
                return typical;
            if (typical <= 0)
            {
                outliers = 0;
                return frameTime;
            }
            if (frameTime > typical * 2)
            {
                if (++outliers < 10)
                    return typical;
                outliers = 0;
                return frameTime;
            }
            outliers = 0;
            return typical + (frameTime - typical) * 0.1;
        }

        /// <summary>
        /// Decides which cameras render this frame. Only entries with <paramref name="eligible"/> set take part (a
        /// camera its owner switched off is not eligible). <paramref name="frameTime"/> is the typical frame time
        /// (SmoothFrameTime), not the last frame's, so one hitch frame does not let every camera render at once. Sets
        /// <paramref name="render"/>, advances the due time of each camera that renders, and returns how many render.
        /// </summary>
        public static int Plan(double[] due, double[] phase, bool[] eligible, bool[] render, int count, double now, double period, double frameTime)
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
            if (period <= 0 || frameTime >= period)
            {
                // No limit, or the game is slower than the limit: every camera renders every frame.
                for (int i = 0; i < count; i++)
                {
                    render[i] = eligible[i];
                    if (eligible[i] && period > 0)
                        due[i] = NextOnGrid(now, period, phase[i], true);
                }
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
                // Fell behind (hitch, pause, owner had it off): the camera's first grid point at least one period
                // away instead of catching up, so cameras that were overdue together are spread out again and no
                // camera renders twice in quick succession.
                if (due[best] <= now)
                    due[best] = NextOnGrid(now + period, period, phase[best], false);
            }
            return rendered;
        }
    }
}

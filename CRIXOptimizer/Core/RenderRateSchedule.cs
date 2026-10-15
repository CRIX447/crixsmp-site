// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Time-based "at most N renders per second" schedule for a group of cameras (no Unity dependency, unit tested).
    ///
    /// Each camera has a due time on its own grid, (k + phase) x period on the absolute clock, with the phases spread
    /// evenly over the period (i / n), so the cameras' renders stay apart. A camera may render on the first frame at or
    /// after its due time (within half a frame); the due time then advances by exactly one period, so a camera renders
    /// at most rate x seconds + 1 times over any stretch. After a hitch it renders once and moves to its next grid point
    /// at least a period away (no catch-up burst). Per frame at most about cameras x rate x frame time cameras render,
    /// earliest due first, so cameras take turns; the frame time used is the real one (long frames get more renders,
    /// keeping the rate), but at most twice the typical one, so a hitch frame renders at most about two frames' worth. A due
    /// camera over the budget renders next frame (its due time is not moved). When the game runs slower than the rate,
    /// every camera renders every frame.
    /// </summary>
    internal static class RenderRateSchedule
    {
        /// <summary>Evenly spaced phase in [0, 1) for camera <paramref name="index"/> of <paramref name="count"/>.</summary>
        public static double EvenPhase(int index, int count)
        {
            return count <= 0 ? 0 : (double)index / count;
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

        /// <summary>How many of <paramref name="cameras"/> may render in a frame of length <paramref name="frameTime"/>:
        /// the expected number rounded up, with a 0.1 tolerance so normal frame-time jitter does not add a slot.</summary>
        public static int FrameBudget(int cameras, double rate, double frameTime)
        {
            if (cameras <= 0)
                return 0;
            if (rate <= 0 || frameTime <= 0)
                return 1;
            int budget = (int)Math.Ceiling(cameras * rate * frameTime - 0.1);
            return Math.Min(cameras, Math.Max(1, budget));
        }

        /// <summary>
        /// Typical frame time: a slow average in which one frame counts for at most three typical frames, so a single
        /// hitch barely moves it and a load screen cannot make it jump; a lasting change is followed within a few
        /// dozen frames.
        /// </summary>
        public static double SmoothFrameTime(double typical, double frameTime)
        {
            if (frameTime <= 0)
                return typical;
            if (typical <= 0)
                return frameTime;
            return typical + (Math.Min(frameTime, typical * 3) - typical) * 0.1;
        }

        /// <summary>
        /// Decides which cameras render this frame. Only entries with <paramref name="eligible"/> set take part (a
        /// camera its owner switched off is not eligible). <paramref name="frameTime"/> is the frame that just passed,
        /// <paramref name="typicalFrameTime"/> the SmoothFrameTime value from before it. Sets <paramref name="render"/>, advances the
        /// due time of each camera that renders, and returns how many render.
        /// </summary>
        public static int Plan(double[] due, double[] phase, bool[] eligible, bool[] render, int count, double now, double period,
                               double frameTime, double typicalFrameTime)
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
            // The shorter of the two: right after a load the average is still high while frames are fast again, and a
            // single long frame says nothing about the next one.
            double effective = frameTime > 0 && typicalFrameTime > 0 ? Math.Min(frameTime, typicalFrameTime)
                : Math.Max(frameTime, typicalFrameTime);
            if (period <= 0 || effective >= period)
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
            double budgetTime = typicalFrameTime > 0 ? Math.Min(frameTime > 0 ? frameTime : typicalFrameTime, typicalFrameTime * 2) : frameTime;
            int budget = FrameBudget(candidates, 1.0 / period, budgetTime);
            double slack = effective > 0 ? effective * 0.5 : 0;
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

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Time-based "at most N renders per second" schedule for one camera (no Unity dependency, unit tested).
    /// A camera renders on the first frame at or after its due time (within half a frame), and the due time then
    /// advances by exactly one period, so over any stretch of time it renders at most rate x seconds + 1 times.
    /// When the game runs slower than the rate it renders every frame.
    /// </summary>
    internal static class RenderRateSchedule
    {
        /// <summary>First due time: spread over one period by the camera's hash so cameras do not render together.</summary>
        public static double InitialDue(double now, double period, int hash)
        {
            return now + period * ((hash & 0xFFFF) / 65536.0);
        }

        /// <summary>True when the camera should render this frame; advances <paramref name="due"/> if so.</summary>
        public static bool Due(ref double due, double now, double period, double frameTime)
        {
            if (period <= 0)
                return true;
            double slack = frameTime > 0 && frameTime < period ? frameTime * 0.5 : 0;
            if (now + slack < due)
                return false;
            due += period;
            // Fell far behind (hitch, pause): start a new period from now instead of rendering several frames in a row.
            if (due <= now)
                due = now + period;
            return true;
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Frame-staggered throttling.
    ///
    /// Potassium throttles per-instance updates with independent timers. Instances that start in the
    /// same frame keep running in the same frame, so the saved work arrives as periodic spikes.
    /// Here each instance gets a fixed phase (from its identity hash) inside an N-frame cycle, which
    /// spreads the remaining work evenly across frames: the average cost is the same, the per-frame
    /// peak is lower.
    /// </summary>
    public static class ThrottleMath
    {
        public const int MaxInterval = 16;

        /// <summary>
        /// Number of frames between runs so that the effective rate is never below the requested rate.
        /// Rounds down (floor), so throttling never cuts more than asked. rate &lt;= 0 means "unthrottled".
        /// </summary>
        public static int IntervalFrames(double measuredFps, double ratePerSecond)
        {
            if (ratePerSecond <= 0 || measuredFps <= 0 || double.IsNaN(measuredFps) || double.IsNaN(ratePerSecond))
                return 1;
            double frames = measuredFps / ratePerSecond;
            if (frames < 2)
                return 1;
            int interval = (int)frames;
            return interval > MaxInterval ? MaxInterval : interval;
        }

        /// <summary>True when the instance with the given hash should run on this frame.</summary>
        public static bool IsInPhase(int frame, int interval, int instanceHash)
        {
            if (interval <= 1)
                return true;
            uint i = (uint)interval;
            return (uint)frame % i == (uint)instanceHash % i;
        }
    }
}

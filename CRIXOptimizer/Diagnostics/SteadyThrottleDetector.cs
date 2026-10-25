// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Recognises frames held at a fixed slow rate (no Unity dependency, unit tested): in one user's session every
    /// frame took 100 ms (+-3 %) for 9 s once the headset was put down, which CRIX logged as some 60 hitches and fed to
    /// adaptive quality. Load never paces frames that evenly; a fixed rate is a throttle, usually the VR runtime's while
    /// the headset is off or its menu is open. <see cref="ConfirmFrames"/> frames in a row of at least
    /// <see cref="MinFrameMs"/> (15 FPS or slower, below any reprojection rate), each within <see cref="Spread"/> of the
    /// run's average, make a throttle; the first frame outside ends it.
    /// </summary>
    internal sealed class SteadyThrottleDetector
    {
        public const float MinFrameMs = 66f;
        public const int ConfirmFrames = 20;
        public const float Spread = 0.04f;

        private double runMs;
        private int run;

        public bool Throttled { get; private set; }
        /// <summary>When the current run (or throttle) began: the start of its first frame.</summary>
        public double RunStart { get; private set; }
        /// <summary>Average frame time of the current run.</summary>
        public float FrameMs => (float)runMs;
        public int Frames => run;
        /// <summary>The throttle that ended last (set when <see cref="AddFrame"/> returns -1).</summary>
        public float EndedFrameMs { get; private set; }
        public int EndedFrames { get; private set; }
        public double EndedSeconds { get; private set; }

        /// <summary>One frame of ms milliseconds that ended at now (seconds). +1 when a throttle was just recognised,
        /// -1 when one just ended (this frame is not part of it), 0 otherwise.</summary>
        public int AddFrame(float ms, double now)
        {
            if (ms >= MinFrameMs && run > 0 && Math.Abs(ms - runMs) <= runMs * Spread)
            {
                run++;
                runMs += (ms - runMs) / run;
                if (!Throttled && run >= ConfirmFrames)
                {
                    Throttled = true;
                    return 1;
                }
                return 0;
            }
            bool ended = Throttled;
            if (ended)
            {
                EndedFrameMs = (float)runMs;
                EndedFrames = run;
                EndedSeconds = now - ms / 1000.0 - RunStart;
            }
            Throttled = false;
            if (ms >= MinFrameMs && ms < 5000f)
            {
                run = 1;
                runMs = ms;
                RunStart = now - ms / 1000.0;
            }
            else
            {
                run = 0;
            }
            return ended ? -1 : 0;
        }
    }
}

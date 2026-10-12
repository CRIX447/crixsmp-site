// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Per-frame sampling of frame time (Unity's unscaled delta time, i.e. measured frame-to-frame time) and
    /// garbage-collection count. O(1) and allocation-free per frame; summaries are computed on demand.
    /// </summary>
    internal sealed class FrameTimeMonitor
    {
        private readonly FrameStatistics window;
        private int lastGcCount = -1;
        private int framesSinceReport;
        private double timeSinceReport;
        private FrameStatsSnapshot cachedSnapshot;
        private float cachedBudget = -1f;
        private bool snapshotDirty = true;

        public FrameTimeMonitor(double windowSeconds)
        {
            window = new FrameStatistics(16384, windowSeconds);
        }

        public FrameStatistics Window => window;
        public long TotalFrames { get; private set; }
        public long TotalGcCollections { get; private set; }
        /// <summary>Frames over 2x budget that coincided with a garbage collection (measured correlation).</summary>
        public long GcCoincidentHitches { get; private set; }
        public long Hitches { get; private set; }
        public double CurrentFps { get; private set; }
        public float LastFrameMs { get; private set; }
        public float BudgetMs { get; set; } = 1000f / 90f;

        public void SetWindowSeconds(double seconds) => window.WindowSeconds = seconds;

        /// <summary>Call once per frame from Update with Time.unscaledDeltaTime.</summary>
        public void Sample(float unscaledDeltaTime)
        {
            float ms = unscaledDeltaTime * 1000f;
            if (!(ms > 0f) || ms > 5000f)
                return; // pauses, loading screens and focus loss are not frames

            LastFrameMs = ms;
            window.Push(ms);
            TotalFrames++;
            framesSinceReport++;
            timeSinceReport += unscaledDeltaTime;
            snapshotDirty = true;

            int gc = GC.CollectionCount(0);
            bool collected = lastGcCount >= 0 && gc != lastGcCount;
            if (collected)
                TotalGcCollections += gc - lastGcCount;
            lastGcCount = gc;

            if (BudgetMs > 0f && ms > BudgetMs * 2f)
            {
                Hitches++;
                if (collected)
                    GcCoincidentHitches++;
            }

            if (timeSinceReport >= 0.5)
            {
                CurrentFps = framesSinceReport / timeSinceReport;
                framesSinceReport = 0;
                timeSinceReport = 0;
            }
        }

        public FrameStatsSnapshot Snapshot()
        {
            if (snapshotDirty || cachedBudget != BudgetMs)
            {
                cachedSnapshot = window.Snapshot(BudgetMs);
                cachedBudget = BudgetMs;
                snapshotDirty = false;
            }
            return cachedSnapshot;
        }

        public void ResetCounters()
        {
            window.Clear();
            Hitches = 0;
            GcCoincidentHitches = 0;
            snapshotDirty = true;
        }
    }
}

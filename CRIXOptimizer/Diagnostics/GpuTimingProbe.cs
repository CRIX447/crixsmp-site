// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Reflection;
using UnityEngine;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// GPU frame time, from the best source the running game exposes:
    ///   1. the XR runtime's app GPU time (XRDisplaySubsystem.TryGetAppGPUTimeLastFrame), or
    ///   2. Unity's FrameTimingManager (only works when the game build enables frame timing stats).
    /// If neither reports, GPU time is "unavailable" - it is never estimated. Sampling runs only while
    /// something needs it (adaptive mode, dashboard, benchmark).
    /// </summary>
    internal sealed class GpuTimingProbe
    {
        private readonly XRRuntimeInfo xr;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private bool frameTimingDisabled;
        private int frameTimingZeroFrames;
        private double sumGpu, sumCompositor;
        private int countGpu, countCompositor;
        private int droppedAtStart = -1;

        public GpuTimingProbe(XRRuntimeInfo xr) { this.xr = xr; }

        public string Source { get; private set; } = "unavailable";
        public bool HasGpuTime { get; private set; }
        public float LastGpuMs { get; private set; } = float.NaN;
        public float AverageGpuMs { get; private set; } = float.NaN;
        public float AverageCompositorMs { get; private set; } = float.NaN;
        public int DroppedFramesTotal { get; private set; } = -1;
        public int DroppedFramesSinceReset { get; private set; } = -1;
        public float MotionToPhotonMs { get; private set; } = float.NaN;

        public void ResetDroppedBaseline() => droppedAtStart = -1;

        /// <summary>Call once per frame while GPU timing is wanted.</summary>
        public void Sample()
        {
            if (xr.TryGetAppGpuMs(out float appMs))
            {
                Source = "XR runtime (app GPU time)";
                LastGpuMs = appMs;
                sumGpu += appMs;
                countGpu++;
            }
            else if (!frameTimingDisabled)
            {
                SampleFrameTimingManager();
            }

            if (xr.TryGetCompositorGpuMs(out float compositorMs))
            {
                sumCompositor += compositorMs;
                countCompositor++;
            }
        }

        /// <summary>Closes the current averaging window (call a few times per second).</summary>
        public void Publish()
        {
            HasGpuTime = countGpu > 0;
            AverageGpuMs = countGpu > 0 ? (float)(sumGpu / countGpu) : float.NaN;
            AverageCompositorMs = countCompositor > 0 ? (float)(sumCompositor / countCompositor) : float.NaN;
            if (!HasGpuTime)
                Source = frameTimingDisabled ? "unavailable (runtime does not report it; frame timing stats off)" : "unavailable";
            sumGpu = 0;
            countGpu = 0;
            sumCompositor = 0;
            countCompositor = 0;

            if (xr.TryGetDroppedFrames(out int dropped))
            {
                DroppedFramesTotal = dropped;
                if (droppedAtStart < 0)
                    droppedAtStart = dropped;
                DroppedFramesSinceReset = Math.Max(0, dropped - droppedAtStart);
            }
            else
            {
                DroppedFramesTotal = -1;
                DroppedFramesSinceReset = -1;
            }

            MotionToPhotonMs = xr.TryGetMotionToPhotonMs(out float m2p) ? m2p : float.NaN;
        }

        private void SampleFrameTimingManager()
        {
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                uint got = FrameTimingManager.GetLatestTimings(1, timings);
                double gpu = got > 0 ? timings[0].gpuFrameTime : 0.0;
                if (gpu > 0.0)
                {
                    frameTimingZeroFrames = 0;
                    Source = "Unity FrameTimingManager";
                    LastGpuMs = (float)gpu;
                    sumGpu += gpu;
                    countGpu++;
                }
                else if (++frameTimingZeroFrames > 300)
                {
                    // Feature not enabled in this build: stop paying for the capture call.
                    frameTimingDisabled = true;
                }
            }
            catch
            {
                frameTimingDisabled = true;
            }
        }

        /// <summary>Unity 2022.1+ can say up front whether frame timing is enabled.</summary>
        public void CheckFrameTimingFeature()
        {
            try
            {
                MethodInfo isEnabled = typeof(FrameTimingManager).GetMethod("IsFeatureEnabled", BindingFlags.Public | BindingFlags.Static);
                if (isEnabled != null && !(bool)isEnabled.Invoke(null, null))
                    frameTimingDisabled = true;
            }
            catch
            {
                // Older Unity: detection happens by sampling.
            }
        }
    }
}

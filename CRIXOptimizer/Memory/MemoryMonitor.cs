// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace CRIXOptimizer.Memory
{
    /// <summary>
    /// Samples memory every 5 seconds and watches for sustained growth.
    ///   Managed heap      - Mono heap used/reserved (Profiler.GetMonoUsedSizeLong / GetMonoHeapSizeLong).
    ///   Unity total       - memory allocated by Unity's allocators (native + managed bookkeeping).
    ///   Unity graphics    - the "Gfx Used Memory" profiler counter: an estimate of textures/meshes/RTs Unity
    ///                       tracks, if the build exposes it. It is NOT total GPU VRAM use, which a game
    ///                       cannot read through Unity; total VRAM use is always reported as unavailable.
    ///   Textures          - Texture.currentTextureMemory (loaded textures), when Unity fills it (0 = not available).
    /// Growth since start-up is tracked for Unity's allocators and for graphics memory: textures of visited maps and
    /// other players' cosmetics may only show in the second.
    /// </summary>
    internal sealed class MemoryMonitor : IDisposable
    {
        private readonly TrendAnalyzer managedTrend = new TrendAnalyzer(60);  // 5 minutes at 5 s
        private ProfilerRecorder gfxRecorder;
        private ProfilerRecorder gcAllocRecorder;
        private bool recordersStarted;
        private float nextSample;
        private long baselineTotalAllocated = -1;
        private long baselineGfx = -1;
        private int lastGcCount = -1;
        private float lastGcSampleTime;

        public long ManagedUsedBytes { get; private set; }
        public long ManagedHeapBytes { get; private set; }
        public long UnityAllocatedBytes { get; private set; }
        public long UnityReservedBytes { get; private set; }
        public long GfxUsedBytes { get; private set; } = -1;
        public long TextureBytes { get; private set; } = -1;
        public long GcAllocatedLastFrameBytes { get; private set; } = -1;
        public double GcPerSecond { get; private set; }
        public double ManagedGrowthMBPerMinute { get; private set; }
        public bool GrowthWindowFull => managedTrend.IsFull;
        public long GrowthSinceBaselineBytes => baselineTotalAllocated < 0 ? 0 : UnityAllocatedBytes - baselineTotalAllocated;
        public long GfxGrowthSinceBaselineBytes => baselineGfx < 0 || GfxUsedBytes < 0 ? 0 : GfxUsedBytes - baselineGfx;
        /// <summary>The larger of the Unity-allocator and graphics-memory growth since start-up (or the last reset).</summary>
        public long LargestGrowthSinceBaselineBytes => Math.Max(GrowthSinceBaselineBytes, GfxGrowthSinceBaselineBytes);

        public void Start()
        {
            if (recordersStarted)
                return;
            recordersStarted = true;
            try
            {
                gfxRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            }
            catch
            {
                gfxRecorder = default;
            }
            try
            {
                gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            }
            catch
            {
                gcAllocRecorder = default;
            }
        }

        /// <summary>Returns true when a new sample was taken.</summary>
        public bool Tick(float now)
        {
            if (now < nextSample)
                return false;
            nextSample = now + 5f;

            try
            {
                ManagedUsedBytes = Profiler.GetMonoUsedSizeLong();
                ManagedHeapBytes = Profiler.GetMonoHeapSizeLong();
                UnityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                UnityReservedBytes = Profiler.GetTotalReservedMemoryLong();
            }
            catch
            {
                ManagedUsedBytes = GC.GetTotalMemory(false);
            }
            if (ManagedUsedBytes <= 0)
                ManagedUsedBytes = GC.GetTotalMemory(false);
            if (baselineTotalAllocated < 0 && UnityAllocatedBytes > 0)
                baselineTotalAllocated = UnityAllocatedBytes;

            GfxUsedBytes = ReadRecorder(gfxRecorder);
            if (baselineGfx < 0 && GfxUsedBytes > 0)
                baselineGfx = GfxUsedBytes;
            TextureBytes = ReadTextureMemory();
            GcAllocatedLastFrameBytes = ReadRecorder(gcAllocRecorder);

            int gc = GC.CollectionCount(0);
            if (lastGcCount >= 0 && now > lastGcSampleTime)
                GcPerSecond = (gc - lastGcCount) / (now - lastGcSampleTime);
            lastGcCount = gc;
            lastGcSampleTime = now;

            managedTrend.Add(now, ManagedUsedBytes / (1024.0 * 1024.0));
            ManagedGrowthMBPerMinute = managedTrend.SlopePerSecond * 60.0;
            return true;
        }

        public void ResetGrowthBaseline()
        {
            baselineTotalAllocated = UnityAllocatedBytes > 0 ? UnityAllocatedBytes : -1;
            baselineGfx = GfxUsedBytes > 0 ? GfxUsedBytes : -1;
            managedTrend.Clear();
        }

        private static long ReadTextureMemory()
        {
            try
            {
                ulong bytes = Texture.currentTextureMemory;
                return bytes == 0 || bytes > long.MaxValue ? -1 : (long)bytes;
            }
            catch
            {
                return -1;
            }
        }

        private static long ReadRecorder(ProfilerRecorder recorder)
        {
            try
            {
                return recorder.Valid ? recorder.LastValue : -1;
            }
            catch
            {
                return -1;
            }
        }

        public void Dispose()
        {
            try { gfxRecorder.Dispose(); } catch { /* not started */ }
            try { gcAllocRecorder.Dispose(); } catch { /* not started */ }
            recordersStarted = false;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return "unavailable";
            double mb = bytes / (1024.0 * 1024.0);
            return mb >= 1024 ? (mb / 1024.0).ToString("0.00") + " GB" : mb.ToString("0") + " MB";
        }

        /// <summary>Driver-reported VRAM capacity (not usage).</summary>
        public static string VramCapacity()
        {
            try
            {
                int mb = SystemInfo.graphicsMemorySize;
                return mb > 0 ? mb + " MB" : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Globalization;
using System.Text;
using CRIXOptimizer.Core;
using CRIXOptimizer.CPU;
using CRIXOptimizer.Memory;
using CRIXOptimizer.Optimization;

namespace CRIXOptimizer.UI
{
    /// <summary>
    /// Builds the dashboard text. Every figure is labelled by where it comes from:
    ///   [M] measured in-game, [D] derived from measured values, [R] reported by the VR runtime / Unity,
    ///   "n/a" not available from any supported API (never estimated).
    /// Runs a few times per second while the dashboard is visible; one StringBuilder is reused.
    /// </summary>
    internal static class DashboardText
    {
        private static readonly StringBuilder sb = new StringBuilder(2048);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Build(OptimizerRuntime rt, bool compact)
        {
            sb.Length = 0;
            FrameStatsSnapshot s = rt.Frames.Snapshot();

            sb.Append("<b>CRIX OPTIMIZER ").Append(ModInfo.Version).Append("</b>  ");
            if (rt.Suspended || !rt.Config.Enabled.Value)
                sb.Append("<color=#ff7070>SUSPENDED - original settings</color>");
            else
                sb.Append("Profile: <b>").Append(rt.ActiveSection).Append("</b>");
            if (rt.AdaptiveActive)
                sb.Append("  Adaptive L").Append(rt.Adaptive.Level).Append(rt.Adaptive.Frozen ? " (frozen)" : "").Append(rt.Adaptive.ManualLevel.HasValue ? " (manual)" : "");
            sb.Append('\n');

            sb.Append("FPS ").Append(F1(rt.Frames.CurrentFps)).Append(" [M]   avg ").Append(F1(s.AverageFps))
              .Append(" / ").Append(F0(s.WindowSeconds)).Append("s [M]   1% low ").Append(F1(s.OnePercentLowFps)).Append(" [D]   target ").Append(F0(rt.TargetFps)).Append('\n');
            sb.Append("Frame ").Append(F2(rt.Frames.LastFrameMs)).Append(" ms [M]   avg ").Append(F2(s.AverageFrameMs))
              .Append("   σ ").Append(F2(s.StdDevMs)).Append("   p99 ").Append(F1(s.P99Ms)).Append("   max ").Append(F1(s.MaxMs))
              .Append("   over budget ").Append(F1(s.OverBudgetFraction * 100)).Append("% [D]\n");

            if (compact)
                return sb.ToString();

            // GPU / runtime
            sb.Append("GPU frame ");
            if (rt.Gpu.HasGpuTime)
                sb.Append(F2(rt.Gpu.AverageGpuMs)).Append(" ms [R: ").Append(rt.Gpu.Source).Append(']');
            else
                sb.Append("n/a");
            sb.Append("   compositor ").Append(float.IsNaN(rt.Gpu.AverageCompositorMs) ? "n/a" : F2(rt.Gpu.AverageCompositorMs) + " ms [R]");
            sb.Append("   dropped ").Append(rt.Gpu.DroppedFramesSinceReset >= 0 ? rt.Gpu.DroppedFramesSinceReset.ToString(Inv) + " [R]" : "n/a");
            if (rt.Adaptive.LastJudgedCpuBound && rt.AdaptiveActive)
                sb.Append("   <color=#ffd060>CPU-bound</color>");
            sb.Append('\n');

            sb.Append("XR ").Append(rt.Xr.DescribeRuntime());
            if (rt.Xr.IsVrActive)
                sb.Append("  ").Append(rt.Xr.EyeTextureWidth).Append('x').Append(rt.Xr.EyeTextureHeight)
                  .Append(" x").Append(F2(rt.Xr.EyeTextureScale)).Append(" MSAA ").Append(rt.Xr.EyeTextureMsaa)
                  .Append(' ').Append(rt.Xr.StereoMode);
            sb.Append("  ").Append(F0(rt.Xr.RefreshRate)).Append(" Hz [R]\n");

            // Memory
            MemoryMonitor m = rt.MemoryStats;
            sb.Append("Managed heap ").Append(MemoryMonitor.FormatBytes(m.ManagedUsedBytes)).Append(" [M]");
            if (m.GrowthWindowFull)
                sb.Append(" (").Append(m.ManagedGrowthMBPerMinute >= 0 ? "+" : "").Append(F1(m.ManagedGrowthMBPerMinute)).Append(" MB/min)");
            sb.Append("   GC ").Append(F2(m.GcPerSecond)).Append("/s   GC-hitches ").Append(rt.Frames.GcCoincidentHitches).Append(" [M]\n");
            sb.Append("Unity memory ").Append(MemoryMonitor.FormatBytes(m.UnityAllocatedBytes)).Append(" [R]   Unity gfx est. ")
              .Append(m.GfxUsedBytes >= 0 ? MemoryMonitor.FormatBytes(m.GfxUsedBytes) + " [R]" : "n/a").Append("   VRAM use n/a (capacity ").Append(MemoryMonitor.VramCapacity()).Append(")\n");

            // Mod + environment
            sb.Append("CRIX overhead ").Append(F3(rt.OverheadMsPerFrame)).Append(" ms/frame [M]   plugins ").Append(rt.Plugins.Count)
              .Append("   patches ").Append(rt.Status.Count(ModuleState.Active)).Append(" ok / ").Append(rt.Status.Count(ModuleState.Unavailable)).Append(" n/a");
            if (rt.Status.Count(ModuleState.Deferred) > 0)
                sb.Append(" / ").Append(rt.Status.Count(ModuleState.Deferred)).Append(" deferred");
            if (rt.Status.Count(ModuleState.Faulted) > 0)
                sb.Append(" / <color=#ff7070>").Append(rt.Status.Count(ModuleState.Faulted)).Append(" faulted</color>");
            sb.Append('\n');
            sb.Append("Skipped updates (total): voice ").Append(UpdateThrottlePatches.VoiceSkipped).Append(" proximity ").Append(UpdateThrottlePatches.ProximitySkipped)
              .Append(" tex ").Append(UpdateThrottlePatches.TextureTransitionSkipped).Append("  intervals ")
              .Append(Live.VoiceVisualInterval).Append('/').Append(Live.ProximityInterval).Append('/').Append(Live.TextureTransitionInterval).Append('\n');
            sb.Append("Apps: ").Append(CompanionApps.Describe(rt.Apps)).Append("   Hooks: ").Append(CompanionApps.Describe(rt.Hooks)).Append('\n');

            if (rt.Benchmark.State != Diagnostics.BenchmarkRecorder.Phase.Idle)
                sb.Append("<color=#70d0ff>Benchmark ").Append(rt.Benchmark.State).Append(' ').Append(F0(rt.Benchmark.SecondsRemaining)).Append("s</color>\n");
            else if (!string.IsNullOrEmpty(rt.Benchmark.LastSummary))
                sb.Append("Last benchmark: ").Append(rt.Benchmark.LastSummary).Append('\n');

            int warnings = 0;
            if (rt.Alerts.IsDegraded)
            {
                sb.Append("<color=#ffd060>! Below target for ").Append(F0(UnityEngine.Time.unscaledTime - rt.Alerts.DegradedSince)).Append("s</color>\n");
                warnings++;
            }
            foreach (string w in rt.Warnings)
            {
                if (warnings++ >= 4)
                    break;
                sb.Append("<color=#ffd060>! ").Append(w).Append("</color>\n");
            }
            if (rt.Ledger.CountInState(SettingState.ExternallyModified) > 0)
                sb.Append("<color=#ffd060>! ").Append(rt.Ledger.CountInState(SettingState.ExternallyModified)).Append(" setting(s) changed by game/another mod - left alone</color>\n");

            Configuration.PluginConfig keys = rt.Config;
            sb.Append("<size=").Append(rt.Config.DashboardFontSize.Value - 3).Append(">[M] measured  [D] derived  [R] runtime-reported  n/a not available   ")
              .Append(keys.KeyToggleDashboard.Value).Append(" hide  ").Append(keys.KeyCycleProfile.Value).Append(" profile  ")
              .Append(keys.KeyRestoreEverything.Value).Append(" restore  ").Append(keys.KeyBenchmark.Value).Append(" benchmark</size>");
            return sb.ToString();
        }

        private static string F0(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0", Inv);
        private static string F1(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.0", Inv);
        private static string F2(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.00", Inv);
        private static string F3(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.000", Inv);
    }
}

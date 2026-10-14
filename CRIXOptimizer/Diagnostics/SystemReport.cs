// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Globalization;
using System.Text;
using BepInEx;
using CRIXOptimizer.Compatibility;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.CPU;
using CRIXOptimizer.Memory;
using CRIXOptimizer.Optimization;
using UnityEngine;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Human-readable environment and status report (BepInEx/CRIXOptimizer/system-report.txt). Contains no
    /// personal data: versions, hardware model names, runtime names, plugin names and CRIX's own settings.
    /// Built on the main thread (Unity APIs), written to disk on a worker thread.
    /// </summary>
    internal static class SystemReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Build(OptimizerRuntime rt)
        {
            var sb = new StringBuilder(8192);
            sb.Append("CRIX OPTIMIZER system report\n");
            sb.Append("Generated (UTC): ").Append(DateTime.UtcNow.ToString("u", Inv)).Append('\n');
            sb.Append("Unofficial mod - not endorsed by Another Axiom. Values marked n/a are not available from supported APIs.\n\n");

            sb.Append("== What CRIX is doing ==\n").Append(rt.ChangesSummary()).Append("\n\n");
            sb.Append("== Environment ==\n").Append(EnvironmentBlock(rt)).Append('\n');

            sb.Append("== Detection ==\n");
            sb.Append("Companion apps: ").Append(CompanionApps.Describe(rt.Apps)).Append('\n');
            sb.Append("Graphics hooks: ").Append(CompanionApps.Describe(rt.Hooks)).Append('\n');
            if (rt.Scanner.Latest != null && rt.Scanner.Latest.HookModules.Count > 0)
                sb.Append("Hook modules: ").Append(string.Join(", ", rt.Scanner.Latest.HookModules.ToArray())).Append('\n');
            sb.Append("Virtual Desktop: ").Append(rt.VirtualDesktop.Describe()).Append('\n');
            sb.Append("Streaming: ").Append(rt.Streaming.Describe()).Append('\n');
            sb.Append("Discord: ").Append(rt.Discord.Describe()).Append('\n');
            sb.Append("ReShade: ").Append(rt.ReShade.Describe()).Append('\n');
            sb.Append("Potassium installed: ").Append(rt.PotassiumInstalled ? "yes" : "no").Append('\n');
            sb.Append("Sakuraa Client: ").Append(rt.Sakuraa.Detected ? rt.Sakuraa.Detail : "not detected").Append('\n');
            sb.Append("Cameras: ").Append(rt.Cameras.Describe()).Append('\n');
            sb.Append("Game desktop camera (DesktopCamera setting): ").Append(rt.DesktopCamera?.Status ?? "n/a").Append('\n');
            sb.Append("Extra camera limit ([Performance] ExtraCameraMaxFps): ").Append(rt.ExtraCameras?.Status ?? "n/a").Append('\n');
            if (rt.ReShadeAdvisor.Error.Length > 0)
                sb.Append("ReShade configuration: could not be read (").Append(rt.ReShadeAdvisor.Error).Append(")\n");
            foreach (ReShade.ReShadePresetAdvisor.RuntimeConfig r in rt.ReShadeAdvisor.Runtimes)
            {
                sb.Append("ReShade preset (").Append(r.Label).Append(", ").Append(r.ConfigFile).Append("): ").Append(r.PresetPath)
                  .Append(r.PresetExists ? "" : " (does not exist)")
                  .Append(r.PresetSource == ReShade.ReShadePresetAdvisor.FromStartupPreset ? " [start-up preset]" : r.PresetSource == ReShade.ReShadePresetAdvisor.FromDefault ? " [default]" : "")
                  .Append(", performance mode ").Append(r.PerformanceMode == null ? "unknown" : r.PerformanceMode.Value ? "on" : "off")
                  .Append(", effects: ").Append(r.Enabled.Count == 0 ? "none" : string.Join(", ", r.Enabled.ConvertAll(t => t.Name + (t.HeavyReason != null ? " (heavy)" : "")).ToArray()))
                  .Append('\n');
            }
            sb.Append("Headset refresh rate: ").Append(rt.Xr.RefreshRate > 1f ? rt.Xr.RefreshRate.ToString("0.#", Inv) + " Hz (" + rt.Xr.RefreshRateSource + ")" : "n/a")
              .Append(" - the frame rate cannot exceed it\n");
            sb.Append("Streaming (auto mode): ").Append(UI.DashboardText.StreamingLine(rt)).Append('\n');
            sb.Append("Base profile: ").Append(rt.BaseSection).Append(", running profile: ").Append(rt.ActiveSection).Append('\n');
            AppendLines(sb, "Advice", rt.VirtualDesktop.Advice);
            AppendLines(sb, "Advice", rt.Streaming.Advice);
            AppendLines(sb, "Advice", rt.Discord.Advice);
            AppendLines(sb, "Advice", rt.ReShade.Advice);
            AppendLines(sb, "Advice", rt.Sakuraa.Advice);
            AppendLines(sb, "Setup", rt.SetupAdvice);
            AppendLines(sb, "ReShade", rt.ReShadeAdvisor.Advice);
            if ((rt.Apps & CompanionApp.Spotify) != 0)
                sb.Append("Advice: ").Append(OptimizerRuntime.SpotifyAdvice).Append('\n');
            sb.Append('\n');

            sb.Append("== Performance (live window) ==\n");
            FrameStatsSnapshot s = rt.Frames.Snapshot();
            sb.Append("Target FPS: ").Append(rt.TargetFps.ToString("0.#", Inv)).Append('\n');
            sb.Append("Average FPS [M]: ").Append(s.AverageFps.ToString("0.0", Inv)).Append(" over ").Append(s.WindowSeconds.ToString("0.0", Inv)).Append(" s\n");
            sb.Append("1% low FPS (p99 frame time) [D]: ").Append(s.OnePercentLowFps.ToString("0.0", Inv)).Append('\n');
            sb.Append("Frame time avg/σ/p99/max ms [M]: ").Append(s.AverageFrameMs.ToString("0.00", Inv)).Append(" / ").Append(s.StdDevMs.ToString("0.00", Inv))
              .Append(" / ").Append(s.P99Ms.ToString("0.0", Inv)).Append(" / ").Append(s.MaxMs.ToString("0.0", Inv)).Append('\n');
            sb.Append("GPU frame time: ").Append(rt.Gpu.HasGpuTime ? rt.Gpu.AverageGpuMs.ToString("0.00", Inv) + " ms (" + rt.Gpu.Source + ")" : rt.Gpu.Source).Append('\n');
            sb.Append("Hitches (>2x budget) / GC-coincident [M]: ").Append(rt.Frames.Hitches).Append(" / ").Append(rt.Frames.GcCoincidentHitches).Append('\n');
            sb.Append("Big hitches (>70 ms) [M]: ").Append(rt.BigHitches).Append(" - during garbage collection ").Append(rt.BigHitchesGc)
              .Append(", during scene loads ").Append(rt.BigHitchesSceneLoad).Append(", right after a CRIX settings change ").Append(rt.BigHitchesAfterCrixChange)
              .Append(", other ").Append(rt.BigHitches - rt.BigHitchesGc - rt.BigHitchesSceneLoad - rt.BigHitchesAfterCrixChange).Append('\n');
            sb.Append("CRIX overhead [M]: ").Append(rt.OverheadMsPerFrame.ToString("0.000", Inv)).Append(" ms/frame\n");
            sb.Append("Managed heap [M]: ").Append(MemoryMonitor.FormatBytes(rt.MemoryStats.ManagedUsedBytes)).Append(", Unity allocated [R]: ")
              .Append(MemoryMonitor.FormatBytes(rt.MemoryStats.UnityAllocatedBytes)).Append(", Unity gfx estimate [R]: ")
              .Append(rt.MemoryStats.GfxUsedBytes >= 0 ? MemoryMonitor.FormatBytes(rt.MemoryStats.GfxUsedBytes) : "n/a").Append(", total VRAM use: n/a\n\n");

            sb.Append("== Active profile: ").Append(rt.ActiveSection).Append(rt.Suspended ? " (SUSPENDED)" : "").Append(" ==\n");
            sb.Append(SettingsBlock(rt)).Append('\n');
            AppendLines(sb, "Note", rt.SettingsNotes);
            sb.Append('\n');

            sb.Append("== Module and patch status ==\n");
            foreach (StatusBoard.Entry e in rt.Status.Entries)
                sb.Append(e.Group).Append(" | ").Append(e.Name).Append(": ").Append(e.State).Append(string.IsNullOrEmpty(e.Detail) ? "" : " - " + e.Detail).Append('\n');
            sb.Append("Map sanitizer lookups rewritten: ").Append(MapSanitizerPatch.RewrittenLookups).Append('\n');
            sb.Append("Room-leave GC calls redirected: ").Append(Memory.GcPolicyPatches.RewrittenCollectCalls).Append('\n');
            sb.Append('\n');

            sb.Append("== Engine settings managed by CRIX ==\n");
            foreach (ITrackedSetting setting in rt.Ledger.Settings)
                sb.Append(setting.Describe()).Append('\n');
            if (rt.Graphics != null)
            {
                sb.Append("Render pipeline: ").Append(GPU.RenderPipelineInfo.Describe(rt.Graphics.Pipeline)).Append(" (").Append(rt.Graphics.PipelineAssetType).Append(")\n");
                sb.Append("Render scale lever: ").Append(rt.Graphics.UsesUrpAsset ? "URP asset renderScale"
                    : rt.Graphics.Pipeline == GPU.PipelineKind.Universal ? "unavailable" : "XRSettings.eyeTextureResolutionScale")
                  .Append(" (check: ").Append(rt.Graphics.RenderScaleCheck).Append(")\n");
                foreach (string note in rt.Graphics.Notes)
                    sb.Append("Note: ").Append(note).Append('\n');
                sb.Append("Texture limit API: ").Append(rt.Graphics.TextureLimitApi).Append('\n');
                sb.Append("Incremental GC: ").Append(rt.Graphics.IncrementalGcAvailable ? "yes" : "no").Append('\n');
            }
            sb.Append('\n');

            sb.Append("== BepInEx plugins (").Append(rt.Plugins.Count).Append(") ==\n");
            foreach (PluginInventory.Entry p in rt.Plugins)
                sb.Append(p.Name).Append(' ').Append(p.Version).Append(" [").Append(p.Guid).Append("] ").Append(p.File)
                  .Append(p.Loaded ? (p.Enabled ? "" : " (disabled)") : " (not loaded)").Append('\n');
            sb.Append('\n');

            if (rt.Footprint != null)
            {
                sb.Append("== Harmony patches ==\n");
                sb.Append("Patched methods: ").Append(rt.Footprint.PatchedMethods).Append('\n');
                foreach (var pair in rt.Footprint.MethodsPerOwner)
                    sb.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).Append('\n');
                foreach (HarmonyFootprint.Overlap o in rt.Footprint.Overlaps)
                    sb.Append("Overlap: ").Append(o.Method).Append(" also patched by ").Append(string.Join(", ", o.OtherOwners.ToArray()))
                      .Append(o.OtherPrefixCanSkip ? " (prefix may skip original)" : "").Append('\n');
            }
            return sb.ToString();
        }

        public static string EnvironmentBlock(OptimizerRuntime rt)
        {
            var sb = new StringBuilder(1024);
            Line(sb, "CRIX OPTIMIZER", ModInfo.Version);
            Line(sb, "Game version", Safe(() => Application.version));
            Line(sb, "Unity version", Safe(() => Application.unityVersion));
            Line(sb, "BepInEx version", Safe(() => typeof(BaseUnityPlugin).Assembly.GetName().Version.ToString()));
            Line(sb, "Harmony version", Safe(() => typeof(HarmonyLib.Harmony).Assembly.GetName().Version.ToString()));
            Line(sb, "Game types indexed", rt.Types != null ? rt.Types.TypeCount.ToString(Inv) : "n/a");
            Line(sb, "OS", Safe(() => SystemInfo.operatingSystem));
            Line(sb, "CPU", Safe(() => SystemInfo.processorType + " (" + SystemInfo.processorCount + " threads)"));
            Line(sb, "RAM", Safe(() => SystemInfo.systemMemorySize + " MB"));
            Line(sb, "GPU", Safe(() => SystemInfo.graphicsDeviceName));
            Line(sb, "Graphics API", Safe(() => SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceVersion));
            Line(sb, "VRAM capacity (driver)", MemoryMonitor.VramCapacity());
            Line(sb, "XR", rt.Xr.DescribeRuntime());
            Line(sb, "XR eye texture", rt.Xr.IsVrActive ? $"{rt.Xr.EyeTextureWidth}x{rt.Xr.EyeTextureHeight} scale {rt.Xr.EyeTextureScale.ToString("0.00", Inv)} MSAA {rt.Xr.EyeTextureMsaa} {rt.Xr.StereoMode}" : "n/a");
            Line(sb, "Eye size requested by the VR runtime [D]", rt.Graphics != null && rt.Graphics.EyeMegapixelsAtScale1 > 0
                ? rt.Graphics.EyeMegapixelsAtScale1.ToString("0.0", Inv) + " MP per eye at render scale 1.0" : "n/a");
            Line(sb, "Refresh rate", rt.Xr.RefreshRate.ToString("0.#", Inv) + " Hz (" + rt.Xr.RefreshRateSource + ")");
            Line(sb, "Plugins loaded", rt.Plugins.Count.ToString(Inv));
            Line(sb, "Companion apps", CompanionApps.Describe(rt.Apps));
            Line(sb, "Graphics hooks", CompanionApps.Describe(rt.Hooks));
            return sb.ToString();
        }

        public static string SettingsBlock(OptimizerRuntime rt)
        {
            var sb = new StringBuilder(2048);
            OptimizationSettings s = rt.ActiveSettings;
            if (s == null)
                return "(none)";
            foreach (var field in SettingCatalog.ProfileFields())
            {
                object value = field.GetValue(s);
                sb.Append(field.Name).Append('=').Append(Convert.ToString(value, Inv)).Append('\n');
            }
            sb.Append("Adaptive=").Append(rt.AdaptiveActive ? "on" : "off").Append(" level=").Append(rt.Adaptive.Level).Append('\n');
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string key, string value) => sb.Append(key).Append(": ").Append(value).Append('\n');

        private static void AppendLines(StringBuilder sb, string prefix, System.Collections.Generic.IEnumerable<string> lines)
        {
            foreach (string line in lines)
                sb.Append(prefix).Append(": ").Append(line).Append('\n');
        }

        private static string Safe(Func<string> f)
        {
            try
            {
                return f() ?? "n/a";
            }
            catch
            {
                return "n/a";
            }
        }
    }
}

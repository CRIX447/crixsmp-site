// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using BepInEx.Configuration;
using UnityEngine;

namespace CRIXOptimizer.Configuration
{
    /// <summary>Main config file: BepInEx/config/com.crix.crixoptimizer.cfg. All values can be changed while the game runs.</summary>
    internal sealed class PluginConfig
    {
        public readonly ConfigFile File;

        // [General]
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<ProfileKind> Profile;
        public readonly ConfigEntry<VirtualDesktopPreset> VirtualDesktopVariant;
        public readonly ConfigEntry<bool> AutoSelectProfile;
        public readonly ConfigEntry<bool> VerboseLogging;

        // [Modules]
        public readonly ConfigEntry<bool> GameStutterFixes;
        public readonly ConfigEntry<bool> VisualThrottles;
        public readonly ConfigEntry<bool> CosmeticCulling;
        public readonly ConfigEntry<bool> GraphicsSettings;
        public readonly ConfigEntry<bool> FramePacingSettings;
        public readonly ConfigEntry<bool> Diagnostics;
        public readonly ConfigEntry<bool> CompatibilityScanner;
        public readonly ConfigEntry<bool> CompanionAppDetection;
        public readonly ConfigEntry<bool> GraphicsHookDetection;

        // [Adaptive]
        public readonly ConfigEntry<AdaptiveMode> AdaptiveModeSetting;
        public bool AdaptiveEnabled => AdaptiveModeSetting.Value == AdaptiveMode.On;
        public readonly ConfigEntry<float> AdaptiveTargetFps;
        public readonly ConfigEntry<float> AdaptiveSensitivity;
        public readonly ConfigEntry<int> AdaptiveMinLevel;
        public readonly ConfigEntry<int> AdaptiveMaxLevel;
        public readonly ConfigEntry<float> AdaptiveRenderScaleStep;
        public readonly ConfigEntry<float> AdaptiveMinRenderScale;
        public readonly ConfigEntry<float> AdaptiveMaxRenderScale;
        public readonly ConfigEntry<float> AdaptiveCooldownSeconds;
        public readonly ConfigEntry<float> AdaptiveDownSustainSeconds;
        public readonly ConfigEntry<float> AdaptiveUpSustainSeconds;
        public readonly ConfigEntry<bool> AdaptiveManualOverride;
        public readonly ConfigEntry<int> AdaptiveManualLevel;

        // [Streaming]
        public readonly ConfigEntry<bool> AutoStreamingMode;
        public readonly ConfigEntry<Core.StreamingTrigger> StreamingTriggerSetting;
        public readonly ConfigEntry<ProfileKind> ObsProfile;
        public readonly ConfigEntry<ProfileKind> DiscordProfile;
        public readonly ConfigEntry<float> StreamingHoldSeconds;
        public readonly ConfigEntry<bool> ObsWebSocket;
        public readonly ConfigEntry<int> ObsWebSocketPort;
        public readonly ConfigEntry<string> ObsWebSocketPassword;
        public readonly ConfigEntry<bool> StreamlabsApi;
        public readonly ConfigEntry<bool> DesktopCameraWhenModsUseIt;

        /// <summary>Set when a 1.0.0 key was replaced (shown once in the log).</summary>
        public string UpgradeNote { get; } = "";

        // [Dashboard]
        public readonly ConfigEntry<bool> DashboardVisible;
        public readonly ConfigEntry<float> DashboardRefreshHz;
        public readonly ConfigEntry<DashboardCorner> DashboardPosition;
        public readonly ConfigEntry<int> DashboardFontSize;
        public readonly ConfigEntry<bool> DashboardCompact;
        public readonly ConfigEntry<bool> AlertOverlayWhenHidden;

        // [Diagnostics]
        public readonly ConfigEntry<float> AverageWindowSeconds;
        public readonly ConfigEntry<bool> AlertsEnabled;
        public readonly ConfigEntry<float> AlertThresholdPercent;
        public readonly ConfigEntry<float> AlertSustainSeconds;
        public readonly ConfigEntry<float> AlertCooldownSeconds;
        public readonly ConfigEntry<float> MemoryGrowthWarnMBPerMinute;
        public readonly ConfigEntry<bool> WriteSystemReport;
        public readonly ConfigEntry<bool> PerformanceNotes;
        public readonly ConfigEntry<float> CompanionScanIntervalSeconds;

        // [Benchmark]
        public readonly ConfigEntry<float> BenchmarkDurationSeconds;
        public readonly ConfigEntry<float> BenchmarkWarmupSeconds;
        public readonly ConfigEntry<string> BenchmarkLabel;

        // [Memory]
        public readonly ConfigEntry<bool> DeferredAssetUnload;
        public readonly ConfigEntry<int> DeferredAssetUnloadGrowthMB;
        public readonly ConfigEntry<float> DeferredAssetUnloadMinIntervalMinutes;

        // [Compatibility]
        public readonly ConfigEntry<PotassiumCoexistence> WhenPotassiumInstalled;
        public readonly ConfigEntry<ExternalChangePolicy> ExternalChanges;
        public readonly ConfigEntry<float> DriftCheckSeconds;
        public readonly ConfigEntry<ReShadeGuardMode> ReShadeGuard;

        // [Hotkeys]
        public readonly ConfigEntry<KeyboardShortcut> KeyToggleDashboard;
        public readonly ConfigEntry<KeyboardShortcut> KeyCycleProfile;
        public readonly ConfigEntry<KeyboardShortcut> KeyRestoreEverything;
        public readonly ConfigEntry<KeyboardShortcut> KeyToggleAdaptiveFreeze;
        public readonly ConfigEntry<KeyboardShortcut> KeyBenchmark;
        public readonly ConfigEntry<KeyboardShortcut> KeyReloadConfig;

        // [Actions] - one-shot buttons for in-game config editors; they reset themselves to false.
        public readonly ConfigEntry<bool> ActionRestoreNow;
        public readonly ConfigEntry<bool> ActionReapplyProfile;
        public readonly ConfigEntry<bool> ActionResetConfig;
        public readonly ConfigEntry<bool> ActionStartBenchmark;
        public readonly ConfigEntry<bool> ActionWriteReport;
        public readonly ConfigEntry<bool> ActionLightReShadePreset;

        // [Performance]
        public readonly ConfigEntry<float> ExtraCameraMaxFps;
        public readonly ConfigEntry<string> ExtraCameraExclude;

        public PluginConfig(ConfigFile file)
        {
            File = file;

            Enabled = file.Bind("General", "Enabled", true,
                "Master switch. false = CRIX restores every setting it changed and all its patches pass through to the game.");
            Profile = file.Bind("General", "Profile", ProfileKind.Balanced,
                "Active optimisation profile. Profile values live in com.crix.crixoptimizer.profiles.cfg (Custom: [Custom] section below). Switching applies immediately.");
            VirtualDesktopVariant = file.Bind("General", "VirtualDesktopPreset", VirtualDesktopPreset.Balanced,
                "Variant used when Profile = VirtualDesktop: Balanced, Performance, Quality or Streaming.");
            AutoSelectProfile = file.Bind("General", "AutoSelectProfile", false,
                "Switch profile automatically from detected companion apps (Virtual Desktop, OBS, Discord). Only process names are read.");
            VerboseLogging = file.Bind("General", "VerboseLogging", false, "Log debug details.");

            GameStutterFixes = file.Bind("Modules", "GameStutterFixes", true,
                "Potassium-derived game patches: staggered startup/room-join work, skipped redundant scans, GC/asset-unload handling, map sanitizer.");
            VisualThrottles = file.Bind("Modules", "VisualUpdateThrottles", true,
                "Frame-staggered throttling of purely visual updates (texture transitions, voice-reactive cosmetics, proximity reactors).");
            CosmeticCulling = file.Bind("Modules", "DistantCosmeticCulling", true,
                "Local culling of distant remote-player cosmetics (CosmeticsThrottler).");
            GraphicsSettings = file.Bind("Modules", "GraphicsSettings", true,
                "Engine rendering settings from the active profile (render scale, shadows, LOD, lights...).");
            FramePacingSettings = file.Bind("Modules", "FramePacingSettings", true,
                "Loading/GC settings from the active profile (async upload slice, background loading priority, incremental GC slice).");
            Diagnostics = file.Bind("Modules", "Diagnostics", true,
                "Frame-time statistics, memory monitoring and alerts. Very low cost; required by adaptive mode and the dashboard.");
            CompatibilityScanner = file.Bind("Modules", "CompatibilityScanner", true,
                "List loaded BepInEx plugins and Harmony patch overlaps once after startup (and on demand).");
            CompanionAppDetection = file.Bind("Modules", "CompanionAppDetection", true,
                "Detect Virtual Desktop, OBS, Discord, SteamVR and Quest Link by process name on a background thread. Nothing is changed in those apps.");
            GraphicsHookDetection = file.Bind("Modules", "GraphicsHookDetection", true,
                "Detect ReShade and overlay/capture hooks (OBS game capture, Discord, Steam, RTSS) by looking at the game folder and the game's own loaded DLLs.");

            AdaptiveModeSetting = file.Bind("Adaptive", "Mode", AdaptiveMode.On,
                "Adaptive quality: when the frame rate drops below target, CRIX lowers (and later restores) distant cosmetic detail, cosmetic update rates, shadow distance, LOD bias, pixel lights and render scale. " +
                "It acts only when the frame rate drops below target or, when the VR runtime reports GPU time, when the GPU uses more than the profile's AdaptiveGpuHeadroomPercent share of the frame. Resolution is lowered at most once a minute and the drop is undone if it does not help. Never touches gameplay, physics or networking. (Replaces the 1.0.0 'Enabled' key.)");
            AdaptiveTargetFps = file.Bind("Adaptive", "TargetFps", 0f,
                new ConfigDescription("Target frame rate. 0 = headset refresh rate reported by the VR runtime (desktop: monitor refresh).", new AcceptableValueRange<float>(0f, 360f)));
            AdaptiveSensitivity = file.Bind("Adaptive", "Sensitivity", 0.5f,
                new ConfigDescription("0 = react only to large drops, 1 = react to small drops.", new AcceptableValueRange<float>(0f, 1f)));
            AdaptiveMinLevel = file.Bind("Adaptive", "MinimumQualityLevel", -6,
                new ConfigDescription("Lowest quality level adaptive mode may use (0 = profile baseline, -10 = most reduced).", new AcceptableValueRange<int>(-10, 0)));
            AdaptiveMaxLevel = file.Bind("Adaptive", "MaximumQualityLevel", 0,
                new ConfigDescription("Highest level. Above 0 adaptive mode may raise render scale above the profile when there is headroom.", new AcceptableValueRange<int>(0, 3)));
            AdaptiveRenderScaleStep = file.Bind("Adaptive", "RenderScaleStep", 0.1f,
                new ConfigDescription("Render-scale change per step.", new AcceptableValueRange<float>(0.05f, 0.25f)));
            AdaptiveMinRenderScale = file.Bind("Adaptive", "MinimumRenderScale", 0.6f,
                new ConfigDescription("Adaptive mode never lowers render scale below this.", new AcceptableValueRange<float>(0.5f, 1.0f)));
            AdaptiveMaxRenderScale = file.Bind("Adaptive", "MaximumRenderScale", 1.3f,
                new ConfigDescription("Adaptive mode never raises render scale above this.", new AcceptableValueRange<float>(1.0f, 2.0f)));
            AdaptiveCooldownSeconds = file.Bind("Adaptive", "CooldownSeconds", 8f,
                new ConfigDescription("Minimum time between two adjustments.", new AcceptableValueRange<float>(2f, 120f)));
            AdaptiveDownSustainSeconds = file.Bind("Adaptive", "DownSustainSeconds", 2f,
                new ConfigDescription("How long the frame rate must stay below target before quality is lowered.", new AcceptableValueRange<float>(0.5f, 30f)));
            AdaptiveUpSustainSeconds = file.Bind("Adaptive", "UpSustainSeconds", 20f,
                new ConfigDescription("How long performance must be comfortably on target before quality is raised again.", new AcceptableValueRange<float>(5f, 300f)));
            AdaptiveManualOverride = file.Bind("Adaptive", "ManualOverride", false,
                "Hold the quality level at ManualLevel instead of adapting.");
            AdaptiveManualLevel = file.Bind("Adaptive", "ManualLevel", 0,
                new ConfigDescription("Level used when ManualOverride is true.", new AcceptableValueRange<int>(-10, 3)));

            AutoStreamingMode = file.Bind("Streaming", "AutoStreamingMode", true,
                "While you record or stream (OBS/Streamlabs) or use Discord, automatically switch to the matching streaming profile and switch back afterwards. " +
                "Only switches away from Balanced, OBSStreaming or DiscordCall (VirtualDesktop switches to its Streaming preset); stronger profiles you picked stay as they are.");
            StreamingTriggerSetting = file.Bind("Streaming", "Trigger", Core.StreamingTrigger.Auto,
                "Auto: OBS's real live/recording state when the OBS WebSocket is connected, otherwise while OBS runs; Discord while it runs. " +
                "WhenAppRunning: whenever OBS/Discord run (OBS renders its preview even when idle). WhenRecordingOrLive: only while OBS is live/recording (needs the OBS WebSocket) or capturing the game.");
            ObsProfile = file.Bind("Streaming", "ObsProfile", ProfileKind.OBSStreaming, "Profile used while OBS/Streamlabs is active.");
            DiscordProfile = file.Bind("Streaming", "DiscordProfile", ProfileKind.DiscordCall, "Profile used while Discord is active (and OBS is not).");
            StreamingHoldSeconds = file.Bind("Streaming", "HoldSeconds", 20f,
                new ConfigDescription("Keep the streaming profile this long after the last streaming signal, so a short reconnect does not flip profiles.", new AcceptableValueRange<float>(0f, 600f)));
            ObsWebSocket = file.Bind("Streaming", "ObsWebSocket", true,
                "Read whether OBS is live or recording through OBS's built-in WebSocket server (OBS 28+, Tools > WebSocket Server Settings > Enable WebSocket server). " +
                "Read-only: CRIX only asks for the streaming/recording state and can never start, stop or change anything in OBS. Connects to 127.0.0.1 only.");
            ObsWebSocketPort = file.Bind("Streaming", "ObsWebSocketPort", 4455,
                new ConfigDescription("Port of the OBS WebSocket server (OBS default 4455).", new AcceptableValueRange<int>(1, 65535)));
            // Hidden from in-game config editors (they draw on the desktop window that OBS records); edit it in this file.
            ObsWebSocketPassword = file.Bind("Streaming", "ObsWebSocketPassword", "",
                new ConfigDescription("Password from OBS (Tools > WebSocket Server Settings > Show Connect Info). Leave empty if authentication is off in OBS. " +
                                      "Stored in plain text in this file and never written to the log. Not shown in in-game config editors, because those draw on the window OBS records.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            StreamlabsApi = file.Bind("Streaming", "StreamlabsApi", true,
                "While Streamlabs Desktop runs, read whether it is live or recording from its local API (127.0.0.1:28194, no setup, read-only: only StreamingService.getModel is called).");

            DashboardVisible = file.Bind("Dashboard", "Visible", false,
                "Show the performance dashboard on the desktop window (not in the headset). Off by default: drawing it costs a little CPU, and OBS window/game capture will record it.");
            DashboardRefreshHz = file.Bind("Dashboard", "RefreshHz", 2f,
                new ConfigDescription("Dashboard text updates per second.", new AcceptableValueRange<float>(0.5f, 10f)));
            DashboardPosition = file.Bind("Dashboard", "Position", DashboardCorner.TopLeft, "Screen corner.");
            DashboardFontSize = file.Bind("Dashboard", "FontSize", 14,
                new ConfigDescription("Font size.", new AcceptableValueRange<int>(8, 40)));
            DashboardCompact = file.Bind("Dashboard", "Compact", false, "Show only the headline numbers.");
            AlertOverlayWhenHidden = file.Bind("Dashboard", "ShowAlertsWhenHidden", false,
                "Show a one-line alert on the desktop window when sustained degradation is detected, even with the dashboard hidden.");

            AverageWindowSeconds = file.Bind("Diagnostics", "AverageWindowSeconds", 10f,
                new ConfigDescription("Length of the sliding window for average FPS, 1% low and frame-time variability.", new AcceptableValueRange<float>(1f, 120f)));
            AlertsEnabled = file.Bind("Diagnostics", "AlertsEnabled", true, "Log a warning when performance stays below target.");
            AlertThresholdPercent = file.Bind("Diagnostics", "AlertThresholdPercent", 10f,
                new ConfigDescription("Alert when the average frame rate is this many percent below target.", new AcceptableValueRange<float>(2f, 60f)));
            AlertSustainSeconds = file.Bind("Diagnostics", "AlertSustainSeconds", 5f,
                new ConfigDescription("...for at least this long.", new AcceptableValueRange<float>(1f, 120f)));
            AlertCooldownSeconds = file.Bind("Diagnostics", "AlertCooldownSeconds", 60f,
                new ConfigDescription("Minimum time between alerts.", new AcceptableValueRange<float>(5f, 3600f)));
            MemoryGrowthWarnMBPerMinute = file.Bind("Diagnostics", "MemoryGrowthWarnMBPerMinute", 8f,
                new ConfigDescription("Warn when managed memory grows faster than this over a 5-minute window.", new AcceptableValueRange<float>(0.5f, 500f)));
            WriteSystemReport = file.Bind("Diagnostics", "WriteSystemReport", true,
                "Write BepInEx/CRIXOptimizer/system-report.txt shortly after startup (versions, GPU, XR runtime, plugins, settings).");
            PerformanceNotes = file.Bind("Diagnostics", "PerformanceNotes", true,
                "Write performance notes for each game session to BepInEx/CRIXOptimizer/notes/ (updated every minute and when the game closes): " +
                "every 10 s the frame rate, zone, players in the room and recording/streaming state; zone changes, room joins/leaves and big hitches with their cause; " +
                "summaries per map and per lobby size. No room codes or player names. Send the newest file when asking for tuning. The last 10 sessions are kept.");
            CompanionScanIntervalSeconds = file.Bind("Diagnostics", "CompanionScanIntervalSeconds", 30f,
                new ConfigDescription("How often to re-check running companion apps (0 = only at startup).", new AcceptableValueRange<float>(0f, 600f)));

            BenchmarkDurationSeconds = file.Bind("Benchmark", "DurationSeconds", 60f,
                new ConfigDescription("Length of a benchmark capture.", new AcceptableValueRange<float>(5f, 1800f)));
            BenchmarkWarmupSeconds = file.Bind("Benchmark", "WarmupSeconds", 5f,
                new ConfigDescription("Delay between starting a benchmark and recording.", new AcceptableValueRange<float>(0f, 120f)));
            BenchmarkLabel = file.Bind("Benchmark", "Label", "",
                "Optional text added to benchmark file names (e.g. 'stump-12players').");

            DeferredAssetUnload = file.Bind("Memory", "DeferredAssetUnload", false,
                "When timed unused-asset unloads are skipped, run one unload later while not in a room and only after memory grew by DeferredAssetUnloadGrowthMB. Guards long sessions against memory growth.");
            DeferredAssetUnloadGrowthMB = file.Bind("Memory", "DeferredAssetUnloadGrowthMB", 1024,
                new ConfigDescription("Total Unity memory growth since startup (or the last unload) that triggers a deferred unload.", new AcceptableValueRange<int>(128, 16384)));
            DeferredAssetUnloadMinIntervalMinutes = file.Bind("Memory", "DeferredAssetUnloadMinIntervalMinutes", 10f,
                new ConfigDescription("Minimum minutes between deferred unloads.", new AcceptableValueRange<float>(1f, 240f)));

            WhenPotassiumInstalled = file.Bind("Compatibility", "WhenPotassiumInstalled", PotassiumCoexistence.DeferToPotassium,
                "If Potassium is also installed, its patches overlap with CRIX's stutter fixes. DeferToPotassium skips CRIX's versions (no duplicate patches). Keeping only one of the two mods is recommended.");
            ExternalChanges = file.Bind("Compatibility", "ExternalChangePolicy", ExternalChangePolicy.Respect,
                "When the game or another plugin changes a setting CRIX manages: Respect (stop managing it) or Reapply.");
            DriftCheckSeconds = file.Bind("Compatibility", "DriftCheckSeconds", 2f,
                new ConfigDescription("How often to check whether managed settings were changed by something else.", new AcceptableValueRange<float>(0.5f, 60f)));
            DesktopCameraWhenModsUseIt = file.Bind("Compatibility", "DesktopCameraWhenModsUseIt", false,
                "The DesktopCamera profile setting leaves the game's desktop camera on when another mod has taken it over (Sakuraa Client uses it as its spectator camera). " +
                "true = switch it off anyway while you are not recording/streaming (that mod's spectator view then disappears from the desktop window until you stream).");
            ReShadeGuard = file.Bind("Compatibility", "ReShadeGuard", ReShadeGuardMode.Auto,
                "Keep ReShade-sensitive settings (MSAA, HDR, desktop mirror, adaptive render scale) untouched. Auto = only when ReShade is detected.");

            KeyToggleDashboard = file.Bind("Hotkeys", "ToggleDashboard", new KeyboardShortcut(KeyCode.F8), "Show/hide the dashboard.");
            KeyCycleProfile = file.Bind("Hotkeys", "CycleProfile", new KeyboardShortcut(KeyCode.F9), "Switch to the next profile.");
            KeyRestoreEverything = file.Bind("Hotkeys", "RestoreEverything", new KeyboardShortcut(KeyCode.F10),
                "Panic button: restore every setting CRIX changed and suspend all optimisations. Press again to resume.");
            KeyToggleAdaptiveFreeze = file.Bind("Hotkeys", "FreezeAdaptive", new KeyboardShortcut(KeyCode.F7), "Freeze/unfreeze the adaptive quality level.");
            KeyBenchmark = file.Bind("Hotkeys", "Benchmark", new KeyboardShortcut(KeyCode.F6), "Start (or cancel) a benchmark capture.");
            KeyReloadConfig = file.Bind("Hotkeys", "ReloadConfig", new KeyboardShortcut(KeyCode.F5, KeyCode.LeftControl), "Reload both config files from disk.");

            ActionRestoreNow = file.Bind("Actions", "RestoreOriginalSettingsNow", false, "Set to true to restore every setting CRIX changed (resets itself).");
            ActionReapplyProfile = file.Bind("Actions", "ReapplyProfileNow", false, "Set to true to re-apply the profile, including settings that were changed externally (resets itself).");
            ActionResetConfig = file.Bind("Actions", "ResetConfigToDefaults", false, "Set to true to reset this file and the profiles file to defaults (resets itself).");
            ActionStartBenchmark = file.Bind("Actions", "StartBenchmarkNow", false, "Set to true to start a benchmark capture (resets itself).");
            ActionWriteReport = file.Bind("Actions", "WriteSystemReportNow", false, "Set to true to write the system report now (resets itself).");
            ActionLightReShadePreset = file.Bind("Actions", "CreateLightReShadePreset", false,
                "Set to true to write a copy of your active ReShade preset without the usually expensive effects (ambient occlusion, global illumination, reflections, depth of field, ...), " +
                "named <preset>.CRIX-light.ini next to it. Your presets, ReShade.ini and ReShadeVR.ini are not changed. Desktop window: pick the copy in the ReShade overlay (Home key); " +
                "headset: the log prints the PresetPath= line to set in ReShadeVR.ini (resets itself).");

            ExtraCameraMaxFps = file.Bind("Performance", "ExtraCameraMaxFps", 0f,
                new ConfigDescription("Cameras that render into a texture (minimaps, tablets, spectator displays from mods such as Sakuraa Client) draw the whole scene again every frame. " +
                                      "A value above 0 lets each of them render at most this many times per second; between updates their screen keeps the last image. " +
                                      "0 = off (default). This changes other mods' cameras, so it is only active when you set it. Mirrors, the game's LIV camera and the photo booth are excluded (ExtraCameraExclude).",
                    new AcceptableValueRange<float>(0f, 144f)));
            ExtraCameraExclude = file.Bind("Performance", "ExtraCameraExclude", "mirror,lck,photo,reflection,portal",
                "Comma-separated name fragments of render-texture cameras ExtraCameraMaxFps never limits (case-insensitive). Add a camera's name here if it must update every frame, " +
                "e.g. one whose image you stream.");

            // 1.0.0 had "[Adaptive] Enabled" (default false). 1.1.0 replaces it with "[Adaptive] Mode" (default On)
            // so existing installs get adaptive quality; drop the old key so the file does not show both.
            if (TryRemoveOrphan(file, "Adaptive", "Enabled", out string oldValue))
                UpgradeNote = $"Adaptive quality is now on by default (the 1.0.0 key [Adaptive] Enabled = {oldValue} was replaced by [Adaptive] Mode). " +
                              "Set [Adaptive] Mode = Off to turn it off.";
        }

        /// <summary>Removes a key that is still in the file but no longer bound (BepInEx keeps such "orphaned" entries).</summary>
        private static bool TryRemoveOrphan(ConfigFile file, string section, string key, out string value)
        {
            value = null;
            try
            {
                var property = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (!(property?.GetValue(file) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans))
                    return false;
                var definition = new ConfigDefinition(section, key);
                if (!orphans.TryGetValue(definition, out value))
                    return false;
                orphans.Remove(definition);
                file.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Read by BepInEx ConfigurationManager (and compatible in-game editors) by type and field name; there is no
    /// compile-time dependency on it.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
    }
}

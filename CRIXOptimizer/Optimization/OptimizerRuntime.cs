// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using CRIXOptimizer.Adaptive;
using CRIXOptimizer.Compatibility;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.CPU;
using CRIXOptimizer.Diagnostics;
using CRIXOptimizer.Discord;
using CRIXOptimizer.GPU;
using CRIXOptimizer.Memory;
using CRIXOptimizer.ReShade;
using CRIXOptimizer.Streaming;
using CRIXOptimizer.UI;
using CRIXOptimizer.Utilities;
using CRIXOptimizer.VirtualDesktop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace CRIXOptimizer.Optimization
{
    /// <summary>
    /// Composition root and main loop. Every subsystem is isolated: an exception in one module is logged,
    /// counted, and after repeated failures that module is shut off while everything else keeps running.
    /// Per-frame work is O(1) and allocation-free; everything else runs on timers (4 Hz, 2 Hz, 1 Hz, ...).
    /// </summary>
    internal sealed class OptimizerRuntime
    {
        // ------------------------------------------------------------------ infrastructure
        private readonly BaseUnityPlugin plugin;
        private readonly PluginConfig cfg;
        private readonly ProfileStore profiles;
        private readonly BepInExLogSink log;
        private readonly OnceLogger once;
        private readonly MainThreadQueue mainThread;
        private readonly Dictionary<string, int> moduleFailures = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> faultedModules = new HashSet<string>(StringComparer.Ordinal);

        public readonly StatusBoard Status = new StatusBoard();
        public readonly SettingsLedger Ledger;
        public readonly XRRuntimeInfo Xr = new XRRuntimeInfo();
        public readonly FrameTimeMonitor Frames;
        public readonly GpuTimingProbe Gpu;
        public readonly MemoryMonitor MemoryStats = new MemoryMonitor();
        public readonly AdaptiveController Adaptive = new AdaptiveController();
        public readonly QualityLadderSettings Ladder = new QualityLadderSettings();
        public readonly DegradationDetector Alerts = new DegradationDetector();
        public readonly BenchmarkRecorder Benchmark;
        public readonly EnvironmentScanner Scanner;
        public readonly VirtualDesktopSupport VirtualDesktop = new VirtualDesktopSupport();
        public readonly StreamingSupport Streaming = new StreamingSupport();
        public readonly DiscordSupport Discord = new DiscordSupport();
        public readonly ReShadeSupport ReShade = new ReShadeSupport();
        public readonly SakuraaSupport Sakuraa = new SakuraaSupport();
        public readonly CameraInventory Cameras = new CameraInventory();
        public readonly ReShadePresetAdvisor ReShadeAdvisor = new ReShadePresetAdvisor();
        public ExtraCameraLimiter ExtraCameras { get; private set; }
        public SpectatorCameraModule Spectator { get; private set; }
        private int reshadeAnalyses;

        public GraphicsSettingsModule Graphics { get; private set; }
        public DesktopCameraModule DesktopCamera { get; private set; }
        public PatchSet Patches { get; private set; }
        public GamePatchCatalog GamePatches { get; private set; }
        public GameTypes Types { get; private set; }

        // ------------------------------------------------------------------ state
        public OptimizationSettings ActiveSettings { get; private set; }
        public string ActiveSection { get; private set; } = "Balanced";
        public readonly List<string> SettingsNotes = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Suspended { get; private set; }
        public bool PotassiumInstalled { get; private set; }
        public List<PluginInventory.Entry> Plugins { get; private set; } = new List<PluginInventory.Entry>();
        public HarmonyFootprint.Report Footprint { get; private set; }
        public double OverheadMsPerFrame { get; private set; }
        public double TargetFps { get; private set; } = 90;
        public string OutputDirectory { get; }
        public string StartupTimeUtc { get; } = DateTime.UtcNow.ToString("u");
        public string LastAlertText { get; private set; } = "";
        public CompanionApp Apps => Scanner.Latest?.Apps ?? CompanionApp.None;
        public readonly CaptureStateMachine Capture = new CaptureStateMachine();
        /// <summary>Per-minute session history (system report); finds a frame rate that falls over a long session.</summary>
        public readonly SessionTrend Trend = new SessionTrend();
        /// <summary>The notes CRIX takes while the user plays (BepInEx/CRIXOptimizer/notes/), null when switched off.</summary>
        public PerformanceNotes Notes { get; private set; }
        public string NotesPath { get; private set; } = "";
        private GameContextProbe probe;
        private CameraRenderCounter renderCounter;
        private string notesMap = "";
        private string pendingMapScan = "";
        private float mapScanAt;
        /// <summary>Content of the custom map last counted (null before any), for the system report.</summary>
        public MapContent LastMapContent { get; private set; }
        private string notesZone = "";
        private int notesPlayers = -2;
        private CaptureKind notesCapture;
        private string notesProfile = "";
        private float nextNotesSecond, nextNotesTick, nextNotesFlush;
        private int notesFlushBusy;
        private int notesFlushCount;
        private string notesReportText = "";
        public int BigHitchesZoneOrRoom { get; private set; }
        public int BigHitchesStartup { get; private set; }
        public int BigHitchesTimeOfDay { get; private set; }
        public int BigHitchesDeferredUnload { get; private set; }
        private string lastSlowDrop = "";
        private float lastSlowDropLog = -1e9f;
        public ObsWebSocketClient Obs { get; private set; }
        public StreamlabsStatusClient Streamlabs { get; private set; }
        /// <summary>The profile the user selected (ActiveSection can differ while auto streaming mode is active).</summary>
        public string BaseSection { get; private set; } = "Balanced";
        public string AutoProfileReason { get; private set; } = "";
        public GraphicsHook Hooks => Scanner.Latest?.Hooks ?? GraphicsHook.None;

        private DashboardOverlay dashboard;
        private bool dirty;
        private bool profileDirty;
        private float applyAt;
        private bool handlingAction;
        private bool hotkeysBroken;
        private bool shutDown;
        private string lastSuggestion;
        private string obsClientKey;
        private bool spotifyRunning;
        private bool obsWasRunning;
        private bool obsSeen, discordSeen;
        private string lastUserProfileKey = "";
        private string UserProfileKey() => cfg.Profile.Value + "/" + cfg.VirtualDesktopVariant.Value;
        private int sakuraaChecks;

        // big-hitch log (diagnostics for "lag spikes")
        private const float BigHitchMs = 70f;
        private float lastSceneLoadTime = -1e9f;
        private float lastGraphicsApplyTime = -1e9f;
        private float lastUpdateMs;
        private int hitchesLogged, hitchesUnlogged;
        private float nextHitchSummary;
        public int BigHitches { get; private set; }
        public int BigHitchesGc { get; private set; }
        public int BigHitchesSceneLoad { get; private set; }
        public int BigHitchesAfterCrixChange { get; private set; }
        private float nextCameraInventory;
        private bool vrWasActive;
        private bool statusLogPending;
        private int environmentScans;
        private float alertVisibleUntil;
        private float lastDeferredUnload = -1e9f;
        private bool memoryBaselineAfterLoading;
        private readonly Dictionary<string, double> trendZoneTotals = new Dictionary<string, double>();
        private int notesTimeOfDay = -2;
        private float lastTimeOfDayChange = -1e9f;
        // Potassium's "Skip timed unused-asset unloads" (default on; read from its config every 30 s).
        private bool potassiumSkipsUnloads = true;

        /// <summary>The game's timed unused-asset unloads are skipped, by CRIX's profile or by Potassium.</summary>
        private bool TimedUnloadsSkipped => Live.SkipTimedAssetUnloads || (PotassiumInstalled && potassiumSkipsUnloads);
        private float lastMemoryWarning = -1e9f;

        // adaptive accumulation (per frame)
        private double adaptiveFrameSum;
        private int adaptiveFrameCount;
        private double adaptiveEmaMs;
        private float cpuSuspectedUntil = -1f;
        /// <summary>Until then frames were seen locked at half the refresh rate whatever the resolution; resolution is kept.</summary>
        private float reprojectionLockedUntil = -1f;
        private float cpuSuspectSeconds = 180f;
        private float lastRenderScaleDrop = -1e9f;
        /// <summary>Minimum seconds between two adaptive resolution drops (each one reallocates the eye textures).</summary>
        private const float RenderScaleDropIntervalSeconds = 60f;
        private readonly ResolutionProbe resolutionProbe = new ResolutionProbe();

        // timers
        private float nextQuarter, nextHalf, nextSecond, nextDrift, nextFive, nextDashboard, nextScan;
        private float lateApplyAt = 5f, compatScanAt = 6f, reportAt = 20f;
        private bool lateApplied, compatScanned, reportWritten;

        // overhead meter
        private long ticksAccumulated;
        private int framesAccumulated;
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public OptimizerRuntime(BaseUnityPlugin plugin, PluginConfig cfg, ProfileStore profiles, BepInExLogSink log)
        {
            this.plugin = plugin;
            this.cfg = cfg;
            this.profiles = profiles;
            this.log = log;
            once = new OnceLogger(log);
            mainThread = new MainThreadQueue(log);
            Ledger = new SettingsLedger(log);
            Frames = new FrameTimeMonitor(cfg.AverageWindowSeconds.Value);
            Gpu = new GpuTimingProbe(Xr);
            OutputDirectory = Path.Combine(Paths.BepInExRootPath, "CRIXOptimizer");
            Benchmark = new BenchmarkRecorder(log, Path.Combine(OutputDirectory, "benchmarks"));
            Scanner = new EnvironmentScanner(log, Paths.GameRootPath);
        }

        // ================================================================== lifecycle

        public void Initialize()
        {
            log.Verbose = cfg.VerboseLogging.Value;
            FrameClock.SetSource(() => Time.frameCount);
            PatchGuard.Log = log;
            PatchGuard.OnFault = (id, message) => Patches?.MarkFaulted(id, message);
            GcPolicyPatches.IncrementalCollector = TryIncrementalCollect;

            Guard("XR info", () => Xr.Refresh(0f, true));
            Guard("Graphics settings", () =>
            {
                Graphics = new GraphicsSettingsModule(Ledger, log, Xr);
                Graphics.Initialize();
                foreach (string note in Graphics.Notes)
                    log.Info(note);
            });
            Guard("GPU timing", Gpu.CheckFrameTimingFeature);
            Guard("Memory monitor", MemoryStats.Start);

            PotassiumInstalled = PluginInventory.IsLoaded(ModInfo.PotassiumGuid);
            Guard("Game patches", () =>
            {
                var sw = Stopwatch.StartNew();
                Types = GameTypes.FromLoadedAssemblies();
                log.Debug($"Indexed {Types.TypeCount} game types from {string.Join(", ", Types.IndexedAssemblies)} in {sw.ElapsedMilliseconds} ms");
                Patches = new PatchSet(new Harmony(ModInfo.HarmonyId), log, Status);
                GamePatches = new GamePatchCatalog(Patches, Types, log);
            });
            Guard("Desktop camera", () => DesktopCamera = new DesktopCameraModule(Types, Ledger, log));
            Guard("Performance notes", StartNotes);
            ExtraCameras = new ExtraCameraLimiter(log);
            ConfigureExtraCameras();
            Spectator = new SpectatorCameraModule(ExtraCameras, now => DesktopCamera?.FindShoulderCamera(now), cam => DesktopCamera?.OwnerLabel(cam) ?? "",
                                                  () => Xr.VrActiveNow, log);
            if (DesktopCamera != null)
                DesktopCamera.IsCrixTexture = SpectatorCameraModule.IsSpectatorTexture;

            if (cfg.UpgradeNote.Length > 0)
                log.Info(cfg.UpgradeNote);
            if (profiles.MigratedEntries > 0)
                log.Info($"Upgraded {profiles.MigratedEntries} profile value(s) to the {ModInfo.Version} defaults. Values you had changed yourself were kept.");
            ResolveProfile();
            lastUserProfileKey = UserProfileKey();
            // With the master switch off nothing is patched now; patches are applied when CRIX is enabled.
            Guard("Game patches", () => GamePatches?.Initialize(ActiveSettings, Groups(), PotassiumInstalled, cfg.WhenPotassiumInstalled.Value, cfg.Enabled.Value));
            ApplyAll("startup");

            cfg.File.SettingChanged += OnSettingChanged;
            cfg.File.ConfigReloaded += OnConfigReloaded;
            profiles.ProfilesFile.SettingChanged += OnSettingChanged;
            profiles.ProfilesFile.ConfigReloaded += OnConfigReloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (cfg.DashboardVisible.Value)
                SetDashboard(true);
            ScheduleEnvironmentScan(Time.unscaledTime);
            EnsureObsClient();
            UpdateStatus();

            log.Info($"{ModInfo.Name} {ModInfo.Version} loaded. Profile: {ActiveSection}. " +
                     $"Patches active: {Status.Count(ModuleState.Active)}, unavailable: {Status.Count(ModuleState.Unavailable)}, deferred: {Status.Count(ModuleState.Deferred)}. " +
                     "Unofficial mod - not endorsed by Another Axiom.");
        }

        public void Shutdown()
        {
            if (shutDown)
                return;
            shutDown = true;
            try
            {
                cfg.File.SettingChanged -= OnSettingChanged;
                cfg.File.ConfigReloaded -= OnConfigReloaded;
                profiles.ProfilesFile.SettingChanged -= OnSettingChanged;
                profiles.ProfilesFile.ConfigReloaded -= OnConfigReloaded;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
            catch
            {
                // already detached
            }

            Guard("Game patches", () => GamePatches?.Shutdown());
            RestoreEverything("shutdown");
            try { FlushNotes(Time.unscaledTime, synchronous: true); } catch (Exception e) { log.Warn("Performance notes: " + e.Message); }
            try { Spectator?.Dispose(); } catch (Exception e) { log.Warn("Spectator camera shutdown: " + e.Message); }
            try { ExtraCameras?.Dispose(); } catch (Exception e) { log.Warn("Extra camera limit shutdown: " + e.Message); }
            try { renderCounter?.Dispose(); } catch (Exception e) { log.Warn("Camera render counter shutdown: " + e.Message); }
            Guard("Memory monitor", MemoryStats.Dispose);
            StopObsClient();
            StopStreamlabsClient();
            SetDashboard(false);
            GcPolicyPatches.IncrementalCollector = null;
            PatchGuard.OnFault = null;
        }

        // ================================================================== per frame

        public void Update()
        {
            if (shutDown)
                return;
            long start = Stopwatch.GetTimestamp();
            long writesAtStart = SettingWrites.Count;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            Frames.Sample(dt);
            if (cfg.Diagnostics.Value)
            {
                Trend.AddFrame(dt, now);
                Notes?.AddFrame(dt, now);
                renderCounter?.AddFrame();
            }
            if (renderCounter != null)
                renderCounter.Counting = cfg.Diagnostics.Value;
            if (dt * 1000f >= BigHitchMs && dt < 5f && cfg.Diagnostics.Value)
                NoteBigHitch(dt * 1000f, now);

            bool wantGpu = Benchmark.WantsGpuSamples || (cfg.AdaptiveEnabled && !Suspended) || dashboard != null;
            if (wantGpu && !IsFaulted("GPU timing"))
            {
                try { Gpu.Sample(); } catch (Exception e) { Fail("GPU timing", e); }
            }

            if (Benchmark.State == BenchmarkRecorder.Phase.Warmup || Benchmark.State == BenchmarkRecorder.Phase.Recording)
            {
                try { Benchmark.Sample(dt, Gpu.LastGpuMs, Gpu.DroppedFramesTotal); } catch (Exception e) { Fail("Benchmark", e); }
            }

            if (dt > 0f && dt < 5f)
            {
                adaptiveFrameSum += dt * 1000.0;
                adaptiveFrameCount++;
            }

            if (GamePatches != null && !IsFaulted("Game patches"))
            {
                try { GamePatches.Tick(); } catch (Exception e) { Fail("Game patches", e); }
            }

            if (ExtraCameras != null && ExtraCameras.NeedsTick)
            {
                // Limiting only happens in frames where this call ran and allowed it, so a suspended, disabled or
                // faulted CRIX never leaves a camera switched off.
                try { ExtraCameras.Tick(!Suspended && cfg.Enabled.Value && !IsFaulted("Extra cameras")); } catch (Exception e) { Fail("Extra cameras", e); }
            }
            if (Spectator != null && Spectator.Engaged)
            {
                try { Spectator.Tick(); } catch (Exception e) { Spectator.EmergencyRelease(); Fail("Spectator camera", e); }
            }

            PollHotkeys();
            mainThread.Drain();

            if (dirty && now >= applyAt)
                ApplyConfigChange();

            RunTimers(now);

            if (dashboard != null)
            {
                ticksAccumulated += dashboard.TicksSpent;
                dashboard.TicksSpent = 0;
            }
            long spent = Stopwatch.GetTimestamp() - start;
            ticksAccumulated += spent;
            framesAccumulated++;
            // For the hitch log: did CRIX write engine settings this frame, and how long did its own work take?
            if (SettingWrites.Count != writesAtStart)
                lastGraphicsApplyTime = now;
            lastUpdateMs = (float)(spent * TicksToMs);
        }

        /// <summary>
        /// Logs what was going on during a very long frame, so "lag spikes" can be traced: a garbage collection,
        /// a scene/map load, or a graphics change CRIX had just made (e.g. a resolution change, which reallocates
        /// the VR eye textures). The first 15 are logged one by one, later ones as a summary once a minute.
        /// </summary>
        private void NoteBigHitch(float ms, float now)
        {
            BigHitches++;
            string cause;
            if (now - lastDeferredUnload <= ms / 1000f + 0.2f)
            {
                // Before the GC check: freeing unused assets also runs a garbage collection.
                BigHitchesDeferredUnload++;
                cause = "CRIX freed unused assets ([Memory] DeferredAssetUnload; only while not in a room)";
            }
            else if (Frames.LastFrameCollected)
            {
                BigHitchesGc++;
                cause = "a garbage collection ran in that frame";
            }
            else if (now - lastSceneLoadTime < 3f)
            {
                BigHitchesSceneLoad++;
                cause = "a scene/map was loading";
            }
            else if (Notes != null && Notes.InStartup)
            {
                BigHitchesStartup++;
                cause = "the game and mods are still loading (startup; e.g. Sakuraa Client loading its content)";
                ZoneOrRoomChangedNow(now, out _);
            }
            else if (ZoneOrRoomChangedNow(now, out string change))
            {
                BigHitchesZoneOrRoom++;
                cause = change;
            }
            else if (TimeOfDayChangedNow(now) || now - lastTimeOfDayChange <= 3f || (probe != null && probe.ReadLightmapLoads() > 0))
            {
                BigHitchesTimeOfDay++;
                cause = now - lastTimeOfDayChange <= 3f
                    ? "the time of day had just changed (the game swaps the lightmaps of every loaded map)"
                    : "a map was loading its lightmaps";
            }
            else if (now - lastGraphicsApplyTime <= ms / 1000f + 0.2f)
            {
                BigHitchesAfterCrixChange++;
                cause = "CRIX had just changed graphics settings";
            }
            else if (lastUpdateMs > 30f)
            {
                BigHitchesAfterCrixChange++;
                cause = $"CRIX's own work took {lastUpdateMs:0} ms in the previous frame";
            }
            else
            {
                cause = "no cause CRIX can see (game, other mods, VR runtime or PC)";
            }
            if (probe != null && probe.ReadZoneLoadsRunning() == 1)
                cause += "; zone scenes were still loading";
            Notes?.AddBigHitch(now, ms, cause);
            if (hitchesLogged < 15)
            {
                hitchesLogged++;
                log.Info($"Hitch: one frame took {ms:0} ms ({cause}). Profile {ActiveSection}, adaptive level {Adaptive.Level}.");
            }
            else
            {
                hitchesUnlogged++;
            }
        }

        private void RunTimers(float now)
        {
            if (Notes != null && cfg.Diagnostics.Value && now >= nextNotesSecond)
            {
                nextNotesSecond = now + 1f;
                Guard("Performance notes", () => PollNotes(now));
            }
            if (now >= nextQuarter)
            {
                nextQuarter = now + 0.25f;
                if (GamePatches != null && !IsFaulted("Game patches"))
                {
                    try { GamePatches.Poll(); } catch (Exception e) { Fail("Game patches", e); }
                }
            }

            if (now >= nextHalf)
            {
                nextHalf = now + 0.5f;
                try { Gpu.Publish(); } catch (Exception e) { Fail("GPU timing", e); }
                EvaluateAdaptive(now);
            }

            if (now >= nextSecond)
            {
                nextSecond = now + 1f;
                OnSecond(now);
            }

            if (now >= nextDrift)
            {
                nextDrift = now + Mathf.Max(0.5f, cfg.DriftCheckSeconds.Value);
                CheckDrift();
            }

            if (now >= nextFive)
            {
                nextFive = now + 5f;
                Guard("XR info", () => Xr.Refresh(now));
                // XR can start late or reconnect (cable/Wi-Fi drop): re-apply so VR-only settings (render scale, mirror,
                // occlusion mesh) reach the new session.
                if (Xr.IsVrActive != vrWasActive)
                {
                    vrWasActive = Xr.IsVrActive;
                    // ReShade's advice depends on whether a headset is in use (first VR session, headset presets).
                    if (vrWasActive && ReShade.Detected)
                        Guard("ReShade advisor", () =>
                        {
                            ReShadeAdvisor.Analyze(Paths.GameRootPath, true);
                            foreach (string line in ReShadeAdvisor.Advice)
                                once.Info("reshade-preset:" + line, line);
                        });
                    if (vrWasActive && lateApplied)
                    {
                        MarkDirty();
                        statusLogPending = true; // log what CRIX changes once the headset's settings are applied
                    }
                }
                Guard("Setup advice", UpdateSetupAdvice);
                if (Graphics != null && !Suspended && cfg.Enabled.Value)
                {
                    Guard("Graphics settings", () => Graphics.CheckCamera(ActiveSettings, cfg.GraphicsSettings.Value));
                    Guard("Graphics settings", Graphics.VerifyRenderScale);
                }
                if (ExtraCameras != null && ExtraCameras.Wanted && !Suspended && cfg.Enabled.Value && !IsFaulted("Extra cameras"))
                    Guard("Extra cameras", ExtraCameras.Discover);
                if (cfg.Diagnostics.Value)
                {
                    TickMemory(now);
                    if (Trend.MinuteDue(now))
                        Guard("Session trend", () => CloseTrendMinute(now));
                // The camera list is for the dashboard and the report: every 5 s while the dashboard is open, else 30 s.
                    if (dashboard != null || now >= nextCameraInventory)
                    {
                        nextCameraInventory = now + 30f;
                        RefreshCameras();
                    }
                }
            }

            if (!lateApplied && now >= lateApplyAt)
            {
                // XR often finishes initialising after plugins load; re-apply once it is up.
                lateApplied = true;
                Guard("XR info", () => Xr.Refresh(now, true));
                ApplyAll("XR initialised");
                LogChangesSummary();
            }

            if (!compatScanned && now >= compatScanAt)
            {
                compatScanned = true;
                if (cfg.CompatibilityScanner.Value)
                    RunCompatibilityScan();
            }

            if (!reportWritten && now >= reportAt)
            {
                reportWritten = true;
                if (cfg.WriteSystemReport.Value)
                    WriteSystemReport();
            }

            float interval = cfg.CompanionScanIntervalSeconds.Value;
            if (interval > 0f && now >= nextScan)
                ScheduleEnvironmentScan(now);

            if (now >= nextDashboard && dashboard != null)
            {
                nextDashboard = now + 1f / Mathf.Clamp(cfg.DashboardRefreshHz.Value, 0.5f, 10f);
                RefreshDashboard(now);
            }
        }

        private void OnSecond(float now)
        {
            // Overhead of CRIX's own per-frame work (measured with Stopwatch, includes dashboard drawing).
            if (framesAccumulated > 0)
                OverheadMsPerFrame = ticksAccumulated * TicksToMs / framesAccumulated;
            ticksAccumulated = 0;
            framesAccumulated = 0;

            TargetFps = cfg.AdaptiveTargetFps.Value > 0f ? cfg.AdaptiveTargetFps.Value : (Xr.RefreshRate > 1f ? Xr.RefreshRate : 90f);
            Frames.BudgetMs = (float)(1000.0 / TargetFps);

            if (GamePatches != null)
            {
                try { GamePatches.UpdateThrottleIntervals(Frames.CurrentFps, EffectiveSettings, Groups(), Suspended || !cfg.Enabled.Value); }
                catch (Exception e) { Fail("Game patches", e); }
            }

            UpdateCaptureState(now);
            if (hitchesUnlogged > 0 && now >= nextHitchSummary)
            {
                log.Info($"Hitches: {hitchesUnlogged} more frame(s) over {BigHitchMs:0} ms in the last minute. Totals: {BigHitches} " +
                         $"(garbage collection {BigHitchesGc}, scene loads {BigHitchesSceneLoad}, right after a CRIX change {BigHitchesAfterCrixChange}).");
                hitchesUnlogged = 0;
                nextHitchSummary = now + 60f;
            }
            // Scene loads recreate cameras, XR may start late and the game's LIV camera can take the desktop camera
            // over at any time: re-check once a second (reads a few flags; finds the camera only when needed).
            ApplyDesktopCamera(cfg.Enabled.Value && !Suspended);

            if (cfg.Diagnostics.Value && cfg.AlertsEnabled.Value)
                UpdateAlerts(now);

            if (alertVisibleUntil > 0f && now > alertVisibleUntil)
            {
                alertVisibleUntil = 0f;
                LastAlertText = "";
                if (dashboard != null)
                {
                    dashboard.Alert = "";
                    if (!cfg.DashboardVisible.Value)
                        SetDashboard(false);
                }
            }
        }

        // ================================================================== profile & settings

        private PatchGroups Groups() => new PatchGroups
        {
            StutterFixes = cfg.GameStutterFixes.Value,
            VisualThrottles = cfg.VisualThrottles.Value,
            CosmeticCulling = cfg.CosmeticCulling.Value,
        };

        private void ResolveProfile(bool logNotes = true)
        {
            ResolveEffectiveProfile(out ProfileKind kind, out VirtualDesktopPreset vdPreset);
            ActiveSection = ProfilePresets.SectionFor(kind, vdPreset);
            OptimizationSettings settings;
            try
            {
                settings = profiles.Resolve(ActiveSection);
            }
            catch (Exception e)
            {
                log.Error($"Profile '{ActiveSection}' could not be read ({e.Message}); using built-in Balanced values.");
                settings = ProfilePresets.Balanced();
            }

            SettingsNotes.Clear();
            SettingsNotes.AddRange(settings.Validate());
            if (spotifyRunning && settings.AdaptiveGpuHeadroomPercent < 35f)
            {
                // Spotify's desktop app renders with the GPU by default; keep a little more headroom while it runs.
                settings.AdaptiveGpuHeadroomPercent += 5f;
            }
            // With the game's desktop camera off, the window shows the XR mirror; make sure it shows one eye.
            if (DesktopCameraModeWantsOff(settings.DesktopCamera) && settings.DesktopMirror == DesktopMirrorMode.Unchanged)
                settings.DesktopMirror = DesktopMirrorMode.LeftEye;
            if (ReShade.GuardActive)
                SettingsNotes.AddRange(ReShadeGuard.Apply(settings));
            if (logNotes)
                foreach (string note in SettingsNotes)
                    log.Info($"[{ActiveSection}] {note}");

            ActiveSettings = settings;

            Adaptive.Parameters = new AdaptiveParameters
            {
                MinLevel = cfg.AdaptiveMinLevel.Value,
                MaxLevel = cfg.AdaptiveMaxLevel.Value,
                Sensitivity = cfg.AdaptiveSensitivity.Value,
                CooldownSeconds = cfg.AdaptiveCooldownSeconds.Value,
                DownSustainSeconds = cfg.AdaptiveDownSustainSeconds.Value,
                UpSustainSeconds = cfg.AdaptiveUpSustainSeconds.Value,
                GpuHeadroomFraction = settings.AdaptiveGpuHeadroomPercent / 100.0,
            };
            Adaptive.ManualLevel = cfg.AdaptiveManualOverride.Value ? cfg.AdaptiveManualLevel.Value : (int?)null;
            Ladder.RenderScaleStep = cfg.AdaptiveRenderScaleStep.Value;
            Ladder.MinRenderScale = cfg.AdaptiveMinRenderScale.Value;
            Ladder.MaxRenderScale = cfg.AdaptiveMaxRenderScale.Value;
            Ladder.AllowRenderScale = settings.AdaptiveMayChangeRenderScale;
            Ladder.AllowShadows = settings.AdaptiveMayChangeShadows;
            Ladder.AllowLod = settings.AdaptiveMayChangeLod;
            Ladder.AllowCosmetics = settings.AdaptiveMayChangeCosmetics;

            Frames.SetWindowSeconds(cfg.AverageWindowSeconds.Value);
            Alerts.ThresholdFraction = cfg.AlertThresholdPercent.Value / 100.0;
            Alerts.SustainSeconds = cfg.AlertSustainSeconds.Value;
            Alerts.CooldownSeconds = cfg.AlertCooldownSeconds.Value;
            log.Verbose = cfg.VerboseLogging.Value;
        }

        /// <summary>Pushes the active profile into every module. Safe to call repeatedly; only differing values are written.</summary>
        /// <param name="explicitChange">The user changed the profile or its values: apply resolution at once, without the adaptive rate limit.</param>
        private void ApplyAll(string reason, bool explicitChange = false)
        {
            bool active = cfg.Enabled.Value && !Suspended;
            long writesBefore = SettingWrites.Count;
            log.Debug($"Applying profile {ActiveSection} ({reason}); active={active}");
            RecomputeEffective();

            if (GamePatches != null && !IsFaulted("Game patches"))
            {
                try
                {
                    if (active)
                        GamePatches.Ensure(ActiveSettings, Groups());
                    GamePatches.UpdateLive(EffectiveSettings, Groups(), !active);
                    GamePatches.UpdateThrottleIntervals(Frames.CurrentFps, EffectiveSettings, Groups(), !active);
                }
                catch (Exception e)
                {
                    Fail("Game patches", e);
                }
            }

            if (Graphics != null && !IsFaulted("Graphics settings"))
            {
                try
                {
                    int level = active && cfg.AdaptiveEnabled ? Adaptive.Level : 0;
                    bool hadScale = Graphics.AppliedLevers.HasRenderScale;
                    float scaleBefore = Graphics.AppliedLevers.RenderScale;
                    bool hold = !explicitChange && Time.unscaledTime - lastRenderScaleDrop < RenderScaleDropIntervalSeconds;
                    Graphics.Apply(active ? ActiveSettings : null, active && cfg.GraphicsSettings.Value, active && cfg.FramePacingSettings.Value, level, Ladder, KeepResolution, hold);
                    // A resolution drop caused by the adaptive level (not by the user's own profile choice) is rate-limited and checked like any other.
                    if (!explicitChange && active && level < 0)
                        NoteRenderScaleChange(hadScale, scaleBefore, Time.unscaledTime);
                }
                catch (Exception e)
                {
                    Fail("Graphics settings", e);
                }
            }
            ApplyDesktopCamera(active);
            if (SettingWrites.Count != writesBefore)
                lastGraphicsApplyTime = Time.unscaledTime;
            UpdateStatus();
        }

        private void ConfigureExtraCameras()
        {
            if (ExtraCameras == null)
                return;
            // The configured value always; whether limiting may run (CRIX on, not suspended) is decided per frame in Tick.
            Guard("Extra cameras", () => ExtraCameras.Configure(cfg.ExtraCameraMaxFps.Value, cfg.ExtraCameraExclude.Value));
        }

        /// <summary>The profile asks for the game's desktop camera to be off right now (ignores whether CRIX is active).</summary>
        private bool DesktopCameraModeWantsOff(DesktopCameraMode mode)
        {
            if (ReShade.GuardActive)
                return false; // ReShade may be applied to the desktop window; leave what it draws alone
            switch (mode)
            {
                case DesktopCameraMode.Off:
                    return true;
                case DesktopCameraMode.OffWhenNotStreaming:
                    return Capture.Active != CaptureKind.Obs;
                default:
                    return false;
            }
        }

        private void ApplyDesktopCamera(bool active)
        {
            float now = Time.unscaledTime;
            bool desktopUsable = DesktopCamera != null && !IsFaulted("Desktop camera");
            // The spectator limit points the same camera at CRIX's texture: hand it back before the desktop-camera setting
            // looks at the camera, and take it again after, only while that setting leaves the camera on.
            string standBy = SpectatorStandBy(active);
            if (standBy != null && Spectator != null)
                Guard("Spectator camera", () => Spectator.Release(standBy));
            if (desktopUsable)
            {
                DesktopCameraMode mode = ActiveSettings?.DesktopCamera ?? DesktopCameraMode.Unchanged;
                // Never without a headset: the desktop camera may then be the only view of the game.
                bool off = active && Xr.IsVrActive && DesktopCameraModeWantsOff(mode);
                string reason = !active ? "CRIX suspended" : mode == DesktopCameraMode.Unchanged ? "profile leaves it alone"
                    : !Xr.IsVrActive ? "no VR headset active" : ReShade.GuardActive ? "ReShade guard"
                    : off ? (mode == DesktopCameraMode.Off ? "profile setting" : "not recording or streaming") : Capture.Reason;
                Guard("Desktop camera", () => DesktopCamera.Apply(off, reason, cfg.DesktopCameraWhenModsUseIt.Value, now));
                // Only when it really switched the camera off: it leaves it on for Sakuraa's spectator, while the game's LIV
                // camera drives it, or after an outside change, and the camera then still renders every frame.
                if (standBy == null && DesktopCamera.HoldsCameraOff)
                    standBy = "standing by: the DesktopCamera setting has switched the camera off (" + reason + ")";
            }
            else if (standBy == null)
            {
                standBy = DesktopCamera == null ? "unavailable: the game's camera rig was not found" : "unavailable: the desktop camera module is disabled after errors";
            }
            ApplySpectator(standBy, now);
        }

        /// <summary>Why the spectator limit must not run now, or null (the desktop-camera setting is checked after it acted).</summary>
        private string SpectatorStandBy(bool active)
        {
            if (!active)
                return "paused: CRIX suspended or disabled";
            if (!Xr.IsVrActive)
                return "standing by: no VR headset active (the camera may be the only view)";
            if (ReShade.GuardActive && !cfg.SpectatorCameraWithReShade.Value)
                return "paused: ReShade detected and [Compatibility] SpectatorCameraWithReShade = false";
            if (IsFaulted("Extra cameras"))
                return "unavailable: the camera limiter is disabled after errors";
            return null;
        }

        private void ApplySpectator(string standBy, float now)
        {
            if (Spectator == null || IsFaulted("Spectator camera"))
                return;
            if (ExtraCameras != null)
                ExtraCameras.NominalFrameTime = Xr.RefreshRate > 1f ? 1.0 / Xr.RefreshRate : 0;
            try
            {
                Spectator.Apply(cfg.SpectatorCameraMaxFps.Value, cfg.SpectatorCameraCadence.Value, cfg.SpectatorCameraPresenter.Value, standBy, now);
            }
            catch (Exception e)
            {
                Spectator.EmergencyRelease();
                Fail("Spectator camera", e);
            }
        }

        private void RestoreEverything(string reason)
        {
            try
            {
                Live.Suspended = true;
                Graphics?.Apply(null, false, false, 0, Ladder, false);
                try { Spectator?.Release(reason); } catch (Exception e) { Spectator?.EmergencyRelease(); log.Warn("Spectator camera restore: " + e.Message); }
                DesktopCamera?.Apply(false, "restored", false, Time.unscaledTime);
                ExtraCameras?.ReleaseAll(reason);
                SettingsLedger.RestoreReport report = Ledger.RestoreAll();
                if (report.Restored > 0 || report.SkippedExternal > 0 || report.Failed > 0)
                    log.Info($"Restore ({reason}): {report.Restored} setting(s) restored, {report.SkippedExternal} left alone (changed externally), {report.Failed} failed.");
                foreach (string note in report.Notes)
                    log.Warn("  " + note);
            }
            catch (Exception e)
            {
                log.Error("Restoring settings failed: " + e.Message);
            }
        }

        private void SetSuspended(bool suspend, string reason)
        {
            if (Suspended == suspend)
                return;
            Suspended = suspend;
            if (suspend)
            {
                Adaptive.Reset();
                ApplyAll(reason);
                RestoreEverything(reason);
                log.Warn($"CRIX OPTIMIZER suspended ({reason}): original settings restored, patches pass through. Press the RestoreEverything hotkey again to resume.");
            }
            else
            {
                // No re-arm: settings the game or another mod changed meanwhile stay theirs.
                ApplyAll(reason, explicitChange: true);
                log.Info($"CRIX OPTIMIZER resumed ({reason}).");
            }
        }

        // ================================================================== config events

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (handlingAction || e?.ChangedSetting == null)
                return;
            ConfigEntryBase entry = e.ChangedSetting;

            if (entry is ConfigEntry<bool> button && button.Value && entry.Definition.Section == "Actions")
            {
                handlingAction = true;
                try
                {
                    button.Value = false;
                }
                finally
                {
                    handlingAction = false;
                }
                // Defer to Update so actions never run inside another plugin's config-editor callback.
                mainThread.Post(() => RunAction(entry.Definition.Key));
                return;
            }

            if (entry == cfg.Profile || entry == cfg.VirtualDesktopVariant || entry.Definition.Section == "Custom" ||
                ReferenceEquals(sender, profiles.ProfilesFile))
                profileDirty = true;
            MarkDirty();
        }

        private void OnConfigReloaded(object sender, EventArgs e)
        {
            profileDirty = true;
            MarkDirty();
        }

        private void MarkDirty()
        {
            dirty = true;
            applyAt = Time.unscaledTime + 0.3f; // debounce bursts (config reloads fire one event per entry)
        }

        private void ApplyConfigChange()
        {
            dirty = false;
            bool rearm = profileDirty;
            profileDirty = false;

            if (DashboardShouldExist() != (dashboard != null))
                SetDashboard(DashboardShouldExist());

            if (!cfg.Enabled.Value)
            {
                if (!Suspended)
                {
                    ApplyAll("disabled in config");
                    RestoreEverything("disabled in config");
                }
                UpdateStatus();
                return;
            }

            string previousSection = ActiveSection;
            bool valuesEdited = rearm;
            // Only an explicit profile switch takes settings back from the game or other mods. Reloading the config
            // (Ctrl+F5), editing profile values or resuming after F10 respects changes made by others.
            string key = UserProfileKey();
            rearm = key != lastUserProfileKey;
            lastUserProfileKey = key;
            ResolveProfile(logNotes: valuesEdited);
            if (rearm)
            {
                Ledger.RearmAll(); // the user chose a profile: apply it even to settings changed externally before
                Spectator?.ResetHandBack();
            }
            if (!cfg.AdaptiveEnabled)
                Adaptive.Reset();
            ApplyAll(rearm ? "profile changed" : valuesEdited ? "profile values changed" : "config changed", explicitChange: rearm || valuesEdited);
            if (rearm)
                log.Info($"Profile applied: {ActiveSection}");
            else if (previousSection != ActiveSection)
                log.Info(AutoProfileReason.Length > 0
                    ? $"Auto streaming mode: {AutoProfileReason} -> profile {ActiveSection} (your profile {BaseSection} returns when it ends)."
                    : $"Auto streaming mode ended -> back to profile {ActiveSection}.");
            EnsureObsClient();
            EnsureStreamlabsClient();
            ConfigureExtraCameras();
            if (statusLogPending)
            {
                statusLogPending = false;
                LogChangesSummary();
            }
        }

        private void RunAction(string key)
        {
            switch (key)
            {
                case "RestoreOriginalSettingsNow":
                    SetSuspended(true, "restore requested");
                    break;
                case "ReapplyProfileNow":
                    Suspended = false;
                    Ledger.RearmAll();
                    Spectator?.ResetHandBack();
                    ResolveProfile();
                    ApplyAll("re-apply requested");
                    log.Info("Profile re-applied, including settings that had been changed externally.");
                    break;
                case "ResetConfigToDefaults":
                    ResetConfigToDefaults();
                    break;
                case "StartBenchmarkNow":
                    ToggleBenchmark();
                    break;
                case "WriteSystemReportNow":
                    WriteSystemReport();
                    break;
                case "CreateLightReShadePreset":
                    Guard("ReShade advisor", CreateLightReShadePresets);
                    break;
            }
        }

        /// <summary>[Actions] CreateLightReShadePreset: a lighter copy of each active ReShade preset (headset and
        /// desktop), next to it. File-system errors are reported, never counted as a module fault.</summary>
        private void CreateLightReShadePresets()
        {
            ReShadeAdvisor.Analyze(Paths.GameRootPath, Xr.IsVrActive);
            if (ReShadeAdvisor.Error.Length > 0)
            {
                log.Warn("CreateLightReShadePreset: could not read the ReShade configuration: " + ReShadeAdvisor.Error + "; nothing written.");
                return;
            }
            if (!ReShadeAdvisor.Found)
            {
                log.Warn("CreateLightReShadePreset: no ReShade.ini or ReShadeVR.ini in the game folder, nothing written.");
                return;
            }
            List<ReShadePresetAdvisor.RuntimeConfig> candidates = ReShadeAdvisor.LightPresetCandidates();
            if (candidates.Count == 0)
            {
                bool anyPreset = ReShadeAdvisor.Runtimes.Exists(r => r.PresetExists);
                log.Info("CreateLightReShadePreset: " + (anyPreset ? "the active preset has no usually expensive effects to remove" : "no active ReShade preset found") + "; nothing written.");
                return;
            }
            foreach (ReShadePresetAdvisor.RuntimeConfig rt in candidates)
            {
                try
                {
                    string path = ReShadeAdvisor.WriteLightPreset(rt);
                    log.Info($"Wrote a lighter ReShade preset for the {rt.Label} ({rt.ConfigFile}): {path}. Your own preset and ReShade's config files were not changed. " +
                             ReShadeAdvisor.HowToUse(rt, path));
                }
                catch (InvalidOperationException e)
                {
                    log.Info($"CreateLightReShadePreset ({rt.Label}): {e.Message}; nothing written.");
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException
                                          || e is ArgumentException || e is NotSupportedException)
                {
                    log.Warn($"CreateLightReShadePreset ({rt.Label}): could not write the light copy of {rt.PresetPath}: {e.Message}");
                }
            }
        }

        private void ResetConfigToDefaults()
        {
            handlingAction = true;
            try
            {
                bool save = cfg.File.SaveOnConfigSet;
                cfg.File.SaveOnConfigSet = false;
                var entries = new List<ConfigEntryBase>();
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in cfg.File)
                    entries.Add(pair.Value);
                foreach (ConfigEntryBase entry in entries)
                    entry.BoxedValue = entry.DefaultValue;
                cfg.File.SaveOnConfigSet = save;
                cfg.File.Save();

                save = profiles.ProfilesFile.SaveOnConfigSet;
                profiles.ProfilesFile.SaveOnConfigSet = false;
                profiles.ResetAllToDefaults();
                profiles.ProfilesFile.SaveOnConfigSet = save;
                profiles.ProfilesFile.Save();
            }
            finally
            {
                handlingAction = false;
            }
            log.Info("Configuration and profiles reset to defaults.");
            profileDirty = true;
            MarkDirty();
        }

        // ================================================================== hotkeys

        private void PollHotkeys()
        {
            if (hotkeysBroken)
                return;
            try
            {
                if (cfg.KeyToggleDashboard.Value.IsDown())
                    cfg.DashboardVisible.Value = !cfg.DashboardVisible.Value;
                if (cfg.KeyCycleProfile.Value.IsDown())
                    CycleProfile();
                if (cfg.KeyRestoreEverything.Value.IsDown())
                    SetSuspended(!Suspended, "hotkey");
                if (cfg.KeyToggleAdaptiveFreeze.Value.IsDown())
                {
                    Adaptive.Frozen = !Adaptive.Frozen;
                    log.Info(Adaptive.Frozen ? $"Adaptive quality frozen at level {Adaptive.Level}." : "Adaptive quality unfrozen.");
                }
                if (cfg.KeyBenchmark.Value.IsDown())
                    ToggleBenchmark();
                if (cfg.KeyReloadConfig.Value.IsDown())
                {
                    cfg.File.Reload();
                    profiles.ProfilesFile.Reload();
                    log.Info("Configuration reloaded from disk.");
                }
            }
            catch (Exception e)
            {
                hotkeysBroken = true;
                log.Warn("Keyboard input is not available (" + e.Message + "); hotkeys disabled. Use the config file or an in-game config editor instead.");
            }
        }

        private void CycleProfile()
        {
            var values = (ProfileKind[])Enum.GetValues(typeof(ProfileKind));
            int index = Array.IndexOf(values, cfg.Profile.Value);
            cfg.Profile.Value = values[(index + 1) % values.Length];
        }

        private void ToggleBenchmark()
        {
            if (Benchmark.State == BenchmarkRecorder.Phase.Warmup || Benchmark.State == BenchmarkRecorder.Phase.Recording)
            {
                Benchmark.Cancel();
                return;
            }
            if (Benchmark.State != BenchmarkRecorder.Phase.Idle)
                return;
            Gpu.ResetDroppedBaseline();
            Benchmark.Start(cfg.BenchmarkWarmupSeconds.Value, cfg.BenchmarkDurationSeconds.Value, new BenchmarkRecorder.Context
            {
                Label = cfg.BenchmarkLabel.Value,
                Profile = cfg.Enabled.Value && !Suspended ? ActiveSection : "Disabled",
                Adaptive = cfg.AdaptiveEnabled && cfg.Enabled.Value && !Suspended,
                BudgetMs = 1000.0 / TargetFps,
                Environment = SystemReport.EnvironmentBlock(this),
                Settings = SystemReport.SettingsBlock(this),
            });
        }

        // ================================================================== adaptive, alerts, memory, drift

        private void EvaluateAdaptive(float now)
        {
            double frameMs = adaptiveFrameCount > 0 ? adaptiveFrameSum / adaptiveFrameCount : 0;
            adaptiveFrameSum = 0;
            adaptiveFrameCount = 0;
            if (frameMs > 0)
                adaptiveEmaMs = adaptiveEmaMs <= 0 ? frameMs : adaptiveEmaMs * 0.7 + frameMs * 0.3;

            // Without the graphics module (faulted or unavailable) adaptive mode still has its CPU-side levers.
            if (!AdaptiveActive || IsFaulted("Adaptive"))
            {
                resolutionProbe.Cancel();
                return;
            }
            try
            {
                ProbeResult probe = resolutionProbe.Sample(now, frameMs);
                if (probe == ProbeResult.Ineffective || probe == ProbeResult.Inconclusive)
                {
                    // Either way the lower resolution bought nothing measurable: undo it rather than keep a blurrier
                    // image (and avoid further drops, each of which is a hitch). Only "Ineffective" means CPU-bound;
                    // "Inconclusive" (half-rate lock before and after) says nothing about the bottleneck.
                    if (probe == ProbeResult.Ineffective)
                        cpuSuspectedUntil = now + cpuSuspectSeconds;
                    else
                        reprojectionLockedUntil = now + cpuSuspectSeconds;
                    log.Info(probe == ProbeResult.Inconclusive
                        ? $"Adaptive quality: frames stayed locked at half the refresh rate (reprojection) after lowering resolution, so the drop is undone. That does not tell whether the CPU or the GPU is the limit; resolution is kept and other reductions are used (rechecked in {cpuSuspectSeconds / 60f:0} min)."
                        : $"Adaptive quality: lowering resolution did not make frames faster ({resolutionProbe.LastImprovement * 100:0.#}% change). " +
                          $"The bottleneck looks CPU-side (busy lobby, cosmetics), so resolution is restored and CPU-side reductions are used instead (rechecked in {cpuSuspectSeconds / 60f:0} min).");
                    // Each failed recheck costs two eye-texture reallocations, so recheck less often each time.
                    cpuSuspectSeconds = Mathf.Min(1800f, cpuSuspectSeconds * 2f);
                    ApplyAdaptiveLevel();
                    return;
                }
                if (probe == ProbeResult.Effective)
                    cpuSuspectSeconds = 180f;

                bool cpuBefore = KeepResolution;
                // The probe's verdict ends here; the change is handled below like any other.
                if (cpuSuspectedUntil > 0f && now >= cpuSuspectedUntil)
                    cpuSuspectedUntil = -1f;
                if (reprojectionLockedUntil > 0f && now >= reprojectionLockedUntil)
                    reprojectionLockedUntil = -1f;

                AdaptiveDecision decision = Adaptive.Evaluate(new AdaptiveSample
                {
                    Now = now,
                    FrameMs = frameMs,
                    TargetMs = 1000.0 / TargetFps,
                    GpuMs = Gpu.HasGpuTime ? Gpu.AverageGpuMs : double.NaN,
                });
                bool cpuNow = KeepResolution;
                // A drop held back by the rate limit is applied once the minute is over.
                bool heldDropDue = GraphicsUsable && Graphics.RenderScaleHeld && now - lastRenderScaleDrop >= RenderScaleDropIntervalSeconds;
                if (!decision.Changed && !heldDropDue && !(cpuNow != cpuBefore && Adaptive.Level < 0))
                    return;

                bool graphicsOk = GraphicsUsable;
                bool hadScale = graphicsOk && Graphics.AppliedLevers.HasRenderScale;
                float scaleBefore = graphicsOk ? Graphics.AppliedLevers.RenderScale : 0f;
                ApplyAdaptiveLevel();

                // Any resolution drop (a step down, or the CPU-bound verdict ending) is checked by the probe.
                if (!NoteRenderScaleChange(hadScale, scaleBefore, now) && decision.Changed)
                    resolutionProbe.Cancel();
                if (decision.Changed)
                {
                    log.Info($"Adaptive quality: level {Adaptive.Level} ({decision.Reason}); frame {frameMs:0.0} ms vs target {1000.0 / TargetFps:0.0} ms" +
                             (Gpu.HasGpuTime ? $", GPU {Gpu.AverageGpuMs:0.0} ms" : "") +
                             (CpuBoundSuspected ? " (CPU-bound: resolution kept)" : ReprojectionLocked ? " (half-rate lock: resolution kept)" : ""));
                }
            }
            catch (Exception e)
            {
                Fail("Adaptive", e);
            }
        }

        private bool GraphicsUsable => Graphics != null && !IsFaulted("Graphics settings");

        /// <summary>Profile plus adaptive CPU-side levers (cosmetic detail, update rates). What game patches run with.</summary>
        public OptimizationSettings EffectiveSettings { get; private set; }

        /// <summary>
        /// CPU-bound according to runtime GPU timing, or because a resolution drop did not help (kept until
        /// EvaluateAdaptive clears it, so the end of that period is noticed and re-applied there).
        /// </summary>
        public bool CpuBoundSuspected => Adaptive.LastJudgedCpuBound || cpuSuspectedUntil > 0f;

        /// <summary>A resolution drop recently left frames locked at half the refresh rate (cause unknown).</summary>
        public bool ReprojectionLocked => reprojectionLockedUntil > 0f;

        /// <summary>Adaptive mode keeps the resolution (CPU-bound, or a drop did not get frames off a half-rate lock).</summary>
        private bool KeepResolution => CpuBoundSuspected || ReprojectionLocked;

        /// <summary>
        /// Records an adaptive resolution drop (rate limit) and starts the probe that checks it helped.
        /// Returns true when the render scale went down.
        /// </summary>
        private bool NoteRenderScaleChange(bool hadScale, float scaleBefore, float now)
        {
            bool lowered = hadScale && GraphicsUsable && Graphics.AppliedLevers.HasRenderScale && Graphics.AppliedLevers.RenderScale < scaleBefore - 0.001f;
            if (!lowered)
                return false;
            lastRenderScaleDrop = now;
            if (!Gpu.HasGpuTime)
            {
                // Only a refresh rate the VR runtime reported can tell a half-rate lock apart.
                bool runtimeRate = Xr.RefreshRateSource == "XR display subsystem" || Xr.RefreshRateSource == "XRDevice";
                resolutionProbe.Begin(now, adaptiveEmaMs, runtimeRate && Xr.RefreshRate > 1f ? 1000.0 / Xr.RefreshRate : 0);
            }
            return true;
        }

        private void RecomputeEffective()
        {
            int level = AdaptiveActive ? Adaptive.Level : 0;
            EffectiveSettings = level < 0 && ActiveSettings != null
                ? QualityLadder.ApplyCpuLevers(ActiveSettings, QualityLadder.Compute(default, level, Ladder, KeepResolution))
                : ActiveSettings;
        }

        /// <summary>Applies the current adaptive level to graphics and game patches.</summary>
        private void ApplyAdaptiveLevel()
        {
            long writesBefore = SettingWrites.Count;
            RecomputeEffective();
            if (GraphicsUsable)
            {
                try
                {
                    bool hold = Time.unscaledTime - lastRenderScaleDrop < RenderScaleDropIntervalSeconds;
                    Graphics.Apply(ActiveSettings, cfg.GraphicsSettings.Value, cfg.FramePacingSettings.Value, AdaptiveActive ? Adaptive.Level : 0, Ladder, KeepResolution, hold);
                }
                catch (Exception e)
                {
                    Fail("Graphics settings", e);
                }
            }
            if (GamePatches != null && !IsFaulted("Game patches"))
            {
                GamePatches.UpdateLive(EffectiveSettings, Groups(), false);
                GamePatches.UpdateThrottleIntervals(Frames.CurrentFps, EffectiveSettings, Groups(), false);
            }
            if (SettingWrites.Count != writesBefore)
                lastGraphicsApplyTime = Time.unscaledTime;
        }

        private void UpdateAlerts(float now)
        {
            DegradationEvent ev = Alerts.Update(now, Frames.CurrentFps, TargetFps);
            if (ev == DegradationEvent.Started)
            {
                LastAlertText = $"CRIX: performance below target - {Frames.CurrentFps:0} FPS vs {TargetFps:0} for {Alerts.SustainSeconds:0}s+ (profile {ActiveSection})";
                log.Warn(LastAlertText);
                if (cfg.AlertOverlayWhenHidden.Value || dashboard != null)
                {
                    alertVisibleUntil = now + 10f;
                    if (dashboard == null)
                        SetDashboard(true);
                    if (dashboard != null)
                        dashboard.Alert = LastAlertText;
                }
            }
            else if (ev == DegradationEvent.Recovered)
            {
                log.Info($"CRIX: performance back on target ({Frames.CurrentFps:0} FPS).");
            }
        }

        // ------------------------------------------------------------------ performance notes

        private void StartNotes()
        {
            if (!cfg.PerformanceNotes.Value || !cfg.Diagnostics.Value)
                return;
            probe = new GameContextProbe(Types);
            Notes = new PerformanceNotes();
            string dir = Path.Combine(OutputDirectory, "notes");
            NotesPath = Path.Combine(dir, "notes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
            float now = Time.unscaledTime;
            nextNotesTick = now + 10f;
            nextNotesFlush = now + 60f;
            Notes.AddEvent(now, "game started; zone and player readers: " + (probe.CanReadZones ? "zones ok" : "zones unavailable") + ", " +
                                (probe.CanReadPlayers ? "players ok" : "players unavailable") + ", " +
                                (probe.CanReadCustomMaps ? "custom maps ok" : "custom maps unavailable"));
            try
            {
                renderCounter = new CameraRenderCounter();
                renderCounter.Start();
            }
            catch (Exception e)
            {
                renderCounter = null;
                log.Debug("Camera render counter unavailable: " + e.Message);
            }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var old = new List<string>(Directory.GetFiles(dir, "notes-*.txt"));
                    old.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < old.Count - 9; i++)
                        File.Delete(old[i]);
                }
                catch (Exception e)
                {
                    log.Debug("Performance notes cleanup: " + e.Message);
                }
            });
            log.Info("Performance notes for this session: " + NotesPath);
        }

        /// <summary>Once a second: zone, players, recording/streaming and profile changes become events.</summary>
        private void PollNotes(float now)
        {
            Notes.SetRefreshRate(Xr.RefreshRate);
            TimeOfDayChangedNow(now);
            ZoneOrRoomChangedNow(now, out _);
            PollCustomMap(now); // after the zone check, so a map that was just unloaded is no longer an active zone
            if (Capture.Active != notesCapture)
            {
                Notes.AddEvent(now, Capture.Active == CaptureKind.None ? "recording/streaming stopped"
                                   : "recording/streaming started (" + (Capture.Reason.Length > 0 ? Capture.Reason : Capture.Active.ToString()) + ")");
                notesCapture = Capture.Active;
                Notes.SetCapture(Capture.Active == CaptureKind.None ? "" : Capture.Active == CaptureKind.Obs ? "OBS/Streamlabs" : "Discord");
            }
            string profile = Suspended || !cfg.Enabled.Value ? "CRIX suspended" : ActiveSection;
            if (profile != notesProfile)
            {
                if (notesProfile.Length > 0)
                    Notes.AddEvent(now, "profile " + notesProfile + " -> " + profile);
                notesProfile = profile;
            }
            if (now >= nextNotesTick)
            {
                nextNotesTick = now + 10f;
                Notes.Tick(now, new PerformanceNotes.Snapshot
                {
                    Profile = profile,
                    AdaptiveLevel = AdaptiveActive ? Adaptive.Level : 0,
                    HeapBytes = MemoryStats.ManagedHeapBytes,
                    UnityBytes = MemoryStats.UnityAllocatedBytes,
                    EyeScale = Xr.EyeTextureScale,
                    VrActive = Xr.IsVrActive,
                    GfxBytes = MemoryStats.GfxUsedBytes,
                    TextureBytes = MemoryStats.TextureBytes,
                    Renders = renderCounter?.Counts.Describe() ?? "",
                });
                renderCounter?.Counts.Clear();
            }
            if (now >= nextNotesFlush)
            {
                nextNotesFlush = now + 60f;
                FlushNotes(now, synchronous: false);
            }
        }

        /// <summary>
        /// Custom (mod.io) maps: entering and leaving become events, and a few seconds after a map has loaded its content
        /// is counted once (read-only) and written to the notes with what it means for frame time.
        /// </summary>
        private void PollCustomMap(float now)
        {
            if (probe == null || !probe.CanReadCustomMaps)
                return;
            string map = probe.ReadCustomMapId();
            if (map != notesMap)
            {
                if (notesMap.Length > 0)
                {
                    Notes.AddEvent(now, "left custom map " + notesMap);
                    // The game unloads the map's bundle and frees unused assets when a map is unloaded.
                    Notes.AssetsFreed(now, "the game unloaded custom map " + notesMap);
                }
                if (map.Length > 0)
                {
                    Notes.AddEvent(now, "custom map " + map + " loaded");
                    pendingMapScan = map;
                    // After the map's entry window, so the count's own frame is not measured as part of entering the map.
                    mapScanAt = now + (float)PerformanceNotes.ZoneEntrySeconds + 3f;
                }
                else
                {
                    pendingMapScan = "";
                }
                notesMap = map;
            }
            if (pendingMapScan.Length == 0 || now < mapScanAt)
                return;
            if (probe.CustomMapLoading())
            {
                mapScanAt = now + 5f; // loading more of its scenes: count once that is done
                return;
            }
            string scanned = pendingMapScan;
            pendingMapScan = "";
            try
            {
                MapContent content = MapContentScanner.Scan(probe.IsCustomScene, scanned);
                LastMapContent = content;
                Notes.AddEvent(now, content.Describe());
                foreach (string hint in content.Hints(Graphics == null || Graphics.RealtimeShadowsPossible, ReadRealtimeProbesAllowed()))
                    Notes.AddEvent(now, "custom map " + scanned + ": " + hint);
                log.Info(content.Describe());
            }
            catch (Exception e)
            {
                log.Debug("Custom map content count failed: " + e.Message);
            }
        }

        private static bool ReadRealtimeProbesAllowed()
        {
            try
            {
                return QualitySettings.realtimeReflectionProbes;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Reads the time-of-day step; a change becomes an event. True if it changed just now.</summary>
        private bool TimeOfDayChangedNow(float now)
        {
            if (Notes == null || probe == null)
                return false;
            int index = probe.ReadTimeOfDay();
            if (index == notesTimeOfDay)
                return false;
            bool changed = notesTimeOfDay >= 0 && index >= 0;
            if (changed)
            {
                lastTimeOfDayChange = now;
                Notes.AddEvent(now, $"time of day changed (step {notesTimeOfDay} -> {index}; the game swaps the lightmaps of every loaded map)");
            }
            notesTimeOfDay = index;
            return changed;
        }

        /// <summary>Reads zone and player count; records changes as events. True if either changed just now (for hitch causes).</summary>
        private bool ZoneOrRoomChangedNow(float now, out string change)
        {
            change = "";
            if (Notes == null || probe == null)
                return false;
            string zone = probe.ReadZones();
            int players = probe.ReadPlayers();
            var parts = new List<string>(2);
            if (zone != notesZone)
            {
                if (notesZone.Length > 0)
                {
                    string visits = Notes.SetZone(zone, now);
                    Notes.AddEvent(now, "zone " + notesZone + " -> " + zone + (visits.Length > 0 ? " (" + visits + ")" : ""));
                    parts.Add("the zone changed (" + notesZone + " -> " + zone + (visits.Length > 0 ? "; " + visits : "") + ")");
                }
                else
                {
                    Notes.SetZone(zone, now);
                }
                notesZone = zone;
            }
            if (players != notesPlayers)
            {
                if (notesPlayers != -2)
                {
                    string text = notesPlayers <= 0 && players > 0 ? $"joined a room ({players} players)"
                        : players == 0 && notesPlayers > 0 ? "left the room"
                        : players > notesPlayers ? $"a player joined ({players} players)"
                        : players >= 0 ? $"a player left ({players} players)" : "player count no longer readable";
                    Notes.AddEvent(now, text);
                    parts.Add(text);
                }
                notesPlayers = players;
                Notes.SetLobby(players);
            }
            change = string.Join("; ", parts.ToArray());
            return parts.Count > 0;
        }

        /// <summary>Writes the notes file: the notes, then the current system report. In the background except at shutdown.</summary>
        private void FlushNotes(float now, bool synchronous)
        {
            if (Notes == null || NotesPath.Length == 0)
                return;
            var header = new List<string>
            {
                "CRIX OPTIMIZER " + ModInfo.Version + ", game " + Safe(() => Application.version) + ", Unity " + Application.unityVersion,
                "CPU " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " threads), RAM " + SystemInfo.systemMemorySize + " MB, GPU " + SystemInfo.graphicsDeviceName,
                "VR: " + (Xr.IsVrActive ? Xr.OpenXRRuntimeName + ", " + Xr.RefreshRate.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " Hz, eye texture " + Xr.EyeTextureWidth + "x" + Xr.EyeTextureHeight : "not active"),
                "What CRIX is doing: " + ChangesSummary(),
            };
            // Main thread: a quick copy; the text is built on the worker. The system report (a few KB, built here) is
            // refreshed every fifth write and at shutdown.
            PerformanceNotes.Frozen frozen = Notes.Freeze(now);
            if (synchronous || notesReportText.Length == 0 || notesFlushCount++ % 5 == 0)
                notesReportText = Safe(() => SystemReport.Build(this));
            string report = notesReportText;
            string path = NotesPath;
            if (synchronous)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, frozen.Render(header) + "\n\n== System report ==\n" + report);
                return;
            }
            if (System.Threading.Interlocked.Exchange(ref notesFlushBusy, 1) == 1)
                return; // the previous write is still running
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string text = frozen.Render(header) + "\n\n== System report ==\n" + report;
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, text);
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temp, path);
                }
                catch (Exception e)
                {
                    log.Debug("Performance notes write failed: " + e.Message);
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref notesFlushBusy, 0);
                }
            });
        }

        private static string Safe(Func<string> f)
        {
            try
            {
                return f() ?? "";
            }
            catch (Exception e)
            {
                return "n/a (" + e.GetType().Name + ")";
            }
        }

        private List<string> optimizerModAdvice = new List<string>();
        private float nextOptimizerModRefresh;

        /// <summary>Notes on GorillaOptimizer, I HATE ALIENS and Potassium (every 30 s; reads GorillaOptimizer's saved toggles).</summary>
        private void RefreshOptimizerMods(float now)
        {
            if (now < nextOptimizerModRefresh)
                return;
            nextOptimizerModRefresh = now + 30f;
            bool gorillaOptimizer = Plugins.Exists(p => p.Guid == OptimizerModsSupport.GorillaOptimizerGuid);
            bool aliens = Plugins.Exists(p => p.Guid == OptimizerModsSupport.IHateAliensGuid);
            Func<string, bool> toggle = null;
            if (gorillaOptimizer)
                toggle = key => ReadPlayerPrefsInt(OptimizerModsSupport.GorillaOptimizerPrefsPrefix + key) == 1;
            optimizerModAdvice = OptimizerModsSupport.Describe(toggle, Graphics != null && Graphics.UsesUrpAsset, aliens, PotassiumInstalled);
            if (PotassiumInstalled && PluginInventory.TryReadConfigBool(ModInfo.PotassiumGuid, "Stutter", "Skip timed unused-asset unloads", out bool skips))
                potassiumSkipsUnloads = skips;
        }

        /// <summary>Read-only (CRIX never writes PlayerPrefs). Separate so the engine call is compiled only when used.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int ReadPlayerPrefsInt(string key) => PlayerPrefs.GetInt(key, 0);

        private void CloseTrendMinute(float now)
        {
            var ctx = new SessionTrend.Context
            {
                HeapBytes = MemoryStats.ManagedHeapBytes,
                UnityBytes = MemoryStats.UnityAllocatedBytes,
                GfxBytes = MemoryStats.GfxUsedBytes,
                GcCount = GC.CollectionCount(0),
                Profile = ActiveSection,
                Capturing = Capture.Active != CaptureKind.None,
                AdaptiveLevel = AdaptiveActive ? Adaptive.Level : 0,
                VrActive = Xr.IsVrActive,
                RefreshHz = Xr.RefreshRate,
            };
            if (Notes != null)
            {
                // The map of the minute: the zone whose recorded play time grew most (nothing per frame).
                var totals = new List<KeyValuePair<string, double>>();
                foreach (PerformanceNotes.Segment s in Notes.Zones)
                    totals.Add(new KeyValuePair<string, double>(s.Key, s.Frames.TotalMs));
                (ctx.Zone, ctx.ZoneShare) = SessionTrend.MinuteZone(totals, trendZoneTotals);
            }
            if (Trend.CloseMinute(now, ctx) == null)
                return;
            string drop = Trend.FindSlowDrop();
            // Logged when first seen and then at most every 10 minutes (the numbers change every minute).
            if (drop != null && (lastSlowDrop.Length == 0 || now - lastSlowDropLog >= 600f))
            {
                lastSlowDropLog = now;
                log.Warn(drop + " The per-minute history is in BepInEx/CRIXOptimizer/system-report.txt ([Actions] WriteSystemReportNow = true).");
            }
            lastSlowDrop = drop ?? "";
        }

        private void TickMemory(float now)
        {
            if (IsFaulted("Memory monitor"))
                return;
            try
            {
                if (!MemoryStats.Tick(now))
                    return;

                if (MemoryStats.GrowthWindowFull && MemoryStats.ManagedGrowthMBPerMinute > cfg.MemoryGrowthWarnMBPerMinute.Value && now - lastMemoryWarning > 600f)
                {
                    lastMemoryWarning = now;
                    log.Warn($"Managed memory has grown {MemoryStats.ManagedGrowthMBPerMinute:0.0} MB/min over the last 5 minutes " +
                             $"(now {MemoryMonitor.FormatBytes(MemoryStats.ManagedUsedBytes)}). If this continues, check recently added mods.");
                }

                // What the game and mods load at startup is not growth over the session: the baseline is taken once loading
                // has finished (the notes' startup phase, or two minutes when the notes are off), and nothing is freed before.
                if (!memoryBaselineAfterLoading && (Notes != null ? !Notes.InStartup : now >= 120f))
                {
                    memoryBaselineAfterLoading = true;
                    MemoryStats.ResetGrowthBaseline();
                }

                // With Potassium installed CRIX's own game patches are off (Live flags cleared), but Potassium skips the
                // same unloads, so the deferred unload is checked against either.
                RefreshOptimizerMods(now);
                if (cfg.DeferredAssetUnload.Value && TimedUnloadsSkipped && !Suspended && memoryBaselineAfterLoading &&
                    MemoryStats.LargestGrowthSinceBaselineBytes >= cfg.DeferredAssetUnloadGrowthMB.Value * 1024L * 1024L &&
                    now - lastDeferredUnload >= cfg.DeferredAssetUnloadMinIntervalMinutes.Value * 60f &&
                    GamePatches != null && GamePatches.TryIsInRoom(out bool inRoom) && !inRoom)
                {
                    lastDeferredUnload = now;
                    log.Info($"Deferred unused-asset unload (memory grew: Unity {MemoryMonitor.FormatBytes(MemoryStats.GrowthSinceBaselineBytes)}, " +
                             $"graphics {MemoryMonitor.FormatBytes(MemoryStats.GfxGrowthSinceBaselineBytes)}; not in a room).");
                    Resources.UnloadUnusedAssets();
                    MemoryStats.ResetGrowthBaselineAtNextSample();
                    Notes?.AssetsFreed(now, "CRIX's deferred unload");
                }
            }
            catch (Exception e)
            {
                Fail("Memory monitor", e);
            }
        }

        private void CheckDrift()
        {
            if (Suspended || !cfg.Enabled.Value)
                return;
            try
            {
                int drifted = Ledger.CheckDrift();
                if (drifted > 0 && cfg.ExternalChanges.Value == ExternalChangePolicy.Reapply)
                {
                    Ledger.RearmAll();
                    ApplyAll("re-applying after external change");
                }
            }
            catch (Exception e)
            {
                Fail("Drift check", e);
            }
        }

        private bool TryIncrementalCollect(ulong nanoseconds)
        {
            if (!GarbageCollector.isIncremental)
                return false;
            GarbageCollector.CollectIncremental(nanoseconds);
            return true;
        }

        // ================================================================== compatibility & environment

        private void RunCompatibilityScan()
        {
            Guard("Compatibility scanner", () =>
            {
                Plugins = PluginInventory.Snapshot();
                Footprint = HarmonyFootprint.Scan(ModInfo.HarmonyId);
                Warnings.RemoveAll(w => w.StartsWith("Compat:", StringComparison.Ordinal));
                if (Footprint.Error != null)
                    log.Debug("Harmony scan: " + Footprint.Error);
                foreach (HarmonyFootprint.Overlap overlap in Footprint.Overlaps)
                {
                    string text = $"Compat: {overlap.Method} is also patched by {string.Join(", ", overlap.OtherOwners.ToArray())}" +
                                  (overlap.OtherPrefixCanSkip ? " (their prefix may skip the original)" : "");
                    Warnings.Add(text);
                    log.Info(text);
                }
                log.Info($"Compatibility scan: {Plugins.Count} BepInEx plugin(s) loaded, {Footprint.PatchedMethods} method(s) patched by Harmony, {Footprint.Overlaps.Count} overlap(s) with CRIX.");
            });
            Guard("Setup advice", UpdateSetupAdvice);
            if (Graphics != null && Graphics.UsesUrpAsset)
            {
                Guard("Graphics settings", Graphics.RefreshUrpAssetValues);
                if (Graphics.UrpOpaqueOrDepthCopies)
                    once.Info("urp-copies", "The URP asset copies the opaque colour and/or depth texture for every camera (" + Graphics.UrpAssetValues +
                              "). Some mod menus turn this on; it costs GPU time on both eyes and the desktop camera. CRIX leaves it as it is.");
            }
            RefreshSakuraa();
        }

        /// <summary>Sakuraa Client is injected by its own launcher, possibly after CRIX starts, so this re-runs with each environment scan.</summary>
        private void RefreshSakuraa()
        {
            sakuraaChecks++;
            Guard("Sakuraa detection", () =>
            {
                bool before = Sakuraa.Detected;
                Sakuraa.Refresh(Plugins, Footprint);
                if (Sakuraa.Detected && !before)
                    log.Info("Sakuraa Client detected: " + Sakuraa.Detail + ". CRIX does not change Sakuraa or GTFX.");
                foreach (string line in Sakuraa.Advice)
                    once.Info("sakuraa:" + line, line);
            });
        }

        /// <summary>Lists active cameras (every 5 s while diagnostics are on). Read-only.</summary>
        private void RefreshCameras()
        {
            Guard("Camera inventory", () =>
            {
                Cameras.Refresh(Xr.IsVrActive ? Xr.EyeTextureWidth : 0, Xr.IsVrActive ? Xr.EyeTextureHeight : 0);
                CameraInventory.Entry big = Cameras.LargestScreenCamera;
                if (big != null && big.PixelLoad >= 0.5)
                    once.Info("cam:" + big.Name,
                        $"Camera '{big.Name}' renders the scene a second time to the desktop window at {big.Width}x{big.Height} " +
                        $"(about {big.PixelLoad * 100:0}% of one VR eye's pixels, derived). A smaller game window lowers that cost. " +
                        "CRIX does not change other mods' cameras; for the game's own desktop camera see the DesktopCamera profile setting.");
            });
        }

        private void ScheduleEnvironmentScan(float now)
        {
            float interval = cfg.CompanionScanIntervalSeconds.Value;
            nextScan = now + (interval > 0f ? Mathf.Max(5f, interval) : float.MaxValue);
            bool processes = cfg.CompanionAppDetection.Value;
            // Loaded DLLs change rarely (ReShade/overlays load at start-up): check them on the first scan and then
            // on every 10th one, while process names are checked on every scan.
            bool hooks = cfg.GraphicsHookDetection.Value && environmentScans++ % 10 == 0;
            if (!processes && !hooks)
                return;
            // The hook scan only needs to run once in a while; process names change more often.
            Scanner.ScanAsync(processes, hooks, result => mainThread.Post(() => OnEnvironmentScanned(result)));
        }

        private void OnEnvironmentScanned(EnvironmentScanner.Result result)
        {
            Guard("Environment detection", () =>
            {
                string api = SafeGraphicsApi();
                bool guardChanged = ReShade.Update(result.Hooks, result.ReShadeDetail, cfg.ReShadeGuard.Value, cfg.Profile.Value, api);
                VirtualDesktop.Update(result.Apps, Xr.OpenXRRuntimeName, Xr.DeviceName, cfg.Profile.Value);
                Streaming.Update(result.Apps, result.Hooks, cfg.Profile.Value, cfg.DashboardVisible.Value);
                Discord.Update(result.Apps, result.Hooks, cfg.Profile.Value);

                EnsureStreamlabsClient();
                bool obsRunning = (result.Apps & CompanionApp.Obs) != 0;
                if (obsRunning && !obsWasRunning)
                    Obs?.Nudge(); // OBS was just started: connect now instead of waiting out the retry delay
                obsWasRunning = obsRunning;
                // Sakuraa is injected by its own launcher, sometimes after CRIX starts: re-check a few times only
                // (listing assemblies runs on the game thread).
                if (compatScanned && !Sakuraa.Detected && sakuraaChecks < 5)
                    RefreshSakuraa();

                bool spotify = (result.Apps & CompanionApp.Spotify) != 0;
                if (spotify != spotifyRunning)
                {
                    spotifyRunning = spotify;
                    MarkDirty(); // adjusts GPU headroom for adaptive mode
                    if (spotify)
                        once.Info("spotify", SpotifyAdvice);
                }

                foreach (string line in VirtualDesktop.Advice)
                    once.Info("vd:" + line, line);
                foreach (string line in Streaming.Advice)
                    once.Info("obs:" + line, line);
                foreach (string line in Discord.Advice)
                    once.Info("discord:" + line, line);
                foreach (string line in ReShade.Advice)
                    once.Info("reshade:" + line, line);

                if (ReShade.Detected && (reshadeAnalyses++ % 10) == 0)
                {
                    ReShadeAdvisor.Analyze(Paths.GameRootPath, Xr.IsVrActive);
                    foreach (string line in ReShadeAdvisor.Advice)
                        once.Info("reshade-preset:" + line, line);
                }

                if (guardChanged)
                {
                    log.Info(ReShade.GuardActive ? "ReShade guard active." : "ReShade guard inactive.");
                    profileDirty = false;
                    ResolveProfile();
                    ApplyAll("ReShade guard changed");
                }

                if (cfg.AutoSelectProfile.Value)
                {
                    ProfileSuggestion? suggestion = CompanionApps.Suggest(result.Apps);
                    string key = suggestion.HasValue ? suggestion.Value.Profile + "/" + suggestion.Value.VirtualDesktopPreset : "none";
                    if (key != lastSuggestion)
                    {
                        lastSuggestion = key;
                        if (suggestion.HasValue && (cfg.Profile.Value != suggestion.Value.Profile ||
                            (suggestion.Value.Profile == ProfileKind.VirtualDesktop && cfg.VirtualDesktopVariant.Value != suggestion.Value.VirtualDesktopPreset)))
                        {
                            log.Info($"AutoSelectProfile: {suggestion.Value.Reason} -> {suggestion.Value.Profile}");
                            cfg.VirtualDesktopVariant.Value = suggestion.Value.VirtualDesktopPreset;
                            cfg.Profile.Value = suggestion.Value.Profile;
                        }
                    }
                }
            });
        }

        private static string SafeGraphicsApi()
        {
            try
            {
                return SystemInfo.graphicsDeviceType.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            lastSceneLoadTime = Time.unscaledTime;
            // Cameras and quality-dependent objects may be recreated; re-apply (only differing values are written).
            if (!Suspended && cfg.Enabled.Value)
            {
                Guard("Graphics settings", () => Graphics?.CheckCamera(ActiveSettings, cfg.GraphicsSettings.Value));
                MarkDirty();
            }
        }

        // ================================================================== what is being optimised

        public int ChangedSettingsCount => Ledger.Settings.Count(x => x.ChangesValue);
        public int ActivePatchCount => Patches?.Records.Count(r => r.State == ModuleState.Active) ?? 0;

        /// <summary>One readable summary of everything CRIX is doing right now (log, report, dashboard).</summary>
        public string ChangesSummary()
        {
            var sb = new System.Text.StringBuilder(512);
            if (!cfg.Enabled.Value || Suspended)
                return "CRIX is suspended: the game runs with its own settings.";
            sb.Append("Profile ").Append(ActiveSection);
            if (AutoProfileReason.Length > 0)
                sb.Append(" (auto: ").Append(AutoProfileReason).Append(")");
            sb.Append(". ").Append(ActivePatchCount).Append(" game patch(es) active");
            if (GamePatches?.DeferredToPotassium == true)
                sb.Append(" (stutter fixes handled by Potassium)");
            sb.Append(". ").Append(ChangedSettingsCount).Append(" engine setting(s) changed");
            var changed = new List<string>();
            foreach (ITrackedSetting s in Ledger.Settings)
                if (s.ChangesValue)
                    changed.Add(s.Describe());
            if (changed.Count > 0)
                sb.Append(": ").Append(string.Join("; ", changed.ToArray()));
            sb.Append(". Adaptive quality ").Append(AdaptiveActive ? $"on (level {Adaptive.Level}, target {TargetFps:0} FPS)" : "off").Append('.');
            if (DesktopCamera != null && ActiveSettings != null && ActiveSettings.DesktopCamera != DesktopCameraMode.Unchanged)
                sb.Append(" Desktop camera: ").Append(DesktopCamera.Status).Append('.');
            if (Graphics != null && Graphics.EyeCapNote.Length > 0)
                sb.Append(" Eye resolution cap: ").Append(Graphics.EyeCapNote).Append('.');
            if (ExtraCameras != null && ExtraCameras.MaxFps > 0f)
                sb.Append(" Extra camera limit: ").Append(ExtraCameras.Status).Append('.');
            if (Spectator != null && cfg.SpectatorCameraMaxFps.Value > 0f)
                sb.Append(" Spectator camera limit: ").Append(Spectator.Status).Append('.');
            return sb.ToString();
        }

        private void LogChangesSummary()
        {
            log.Info("CRIX status: " + ChangesSummary());
            bool limitingCameras = (ExtraCameras != null && ExtraCameras.MaxFps > 0f && ExtraCameras.ManagedCount > 0) || (Spectator != null && Spectator.Engaged);
            if (cfg.Enabled.Value && !Suspended && ActivePatchCount == 0 && ChangedSettingsCount == 0 && !limitingCameras)
            {
                string why = Types == null || Types.IsEmpty ? "the game's code was not found (game update or unsupported build)"
                    : GamePatches?.DeferredToPotassium == true ? "Potassium is installed and handles the stutter fixes"
                    : !Xr.IsVrActive ? "no VR headset is active yet"
                    : "every value in this profile already matches the game";
                log.Warn("CRIX is loaded but nothing is being changed right now: " + why + ". " +
                         (AdaptiveActive ? "Adaptive quality still steps in if the frame rate drops. " : "Adaptive quality is off ([Adaptive] Mode). ") +
                         "See BepInEx/CRIXOptimizer/system-report.txt.");
            }
        }

        /// <summary>Setup problems CRIX can see but not change (VR runtime settings, other apps and mods). Main thread.</summary>
        public readonly List<string> SetupAdvice = new List<string>();
        private string lastEyeCapNote = "";

        /// <summary>Rebuilds SetupAdvice (every 5 s and after the compatibility scan) and logs each new line once.</summary>
        private void UpdateSetupAdvice()
        {
            SetupAdvice.Clear();
            // The runtime the game actually loaded decides where its settings are; Windows' active OpenXR runtime (what
            // VirtualDesktopSupport sees) can differ, e.g. a game started through the Meta Quest Link app uses Oculus.
            string runtime = (Xr.OpenXRRuntimeName + " " + Xr.DeviceName).ToLowerInvariant();
            bool loadedVd = runtime.Contains("virtualdesktop") || runtime.Contains("vdxr");
            bool loadedLink = runtime.Contains("oculus") || runtime.Contains("meta");
            bool steamvr = runtime.Contains("openvr") || runtime.Contains("steamvr");
            bool loadedKnown = loadedVd || loadedLink || steamvr;
            bool vd = loadedVd || (!loadedKnown && VirtualDesktop.Detected != VirtualDesktopSupport.Path.NotDetected);
            bool link = loadedLink || (!loadedKnown && !vd && (Apps & CompanionApp.MetaQuestLink) != 0);
            // Where each setting lives (menu names as the apps showed them when this was written).
            string whereRate = vd ? "in Virtual Desktop (in the headset: Settings > Streaming > VR Frame Rate). Also check SSW there: " +
                                    "'Always enabled' makes the game render at half the frame rate; use Automatic, or Disabled to see the real frame rate"
                : link ? "in the Meta Quest Link app (Devices > your headset > Graphics Preferences > Refresh rate)"
                : steamvr ? "in SteamVR (Settings > Video > refresh rate) and check that Motion Smoothing is not locking the game to half rate"
                : "in your VR app (Virtual Desktop, Meta Quest Link or SteamVR)";
            string whereResolution = vd ? "in Virtual Desktop (Settings > Streaming > VR Graphics Quality: High or lower)"
                : link ? "in the Meta Quest Link app (Graphics Preferences > Render resolution: default) and in the Oculus Debug Tool (Pixel Density 0 = default)"
                : steamvr ? "in SteamVR (Settings > Video > render resolution)"
                : "in your VR app (Virtual Desktop, Meta Quest Link or SteamVR)";

            bool runtimeRate = Xr.RefreshRateSource == "XR display subsystem" || Xr.RefreshRateSource == "XRDevice";
            if (Xr.IsVrActive && runtimeRate && Xr.RefreshRate > 1f && Xr.RefreshRate < 70f)
                SetupAdvice.Add($"The VR runtime runs the game at {Xr.RefreshRate:0} Hz, so it can never exceed {Xr.RefreshRate:0} FPS. Set 72, 80 or 90 Hz {whereRate}.");
            else if (Xr.IsVrActive && runtimeRate && ActiveSection == "HighRefresh" && Xr.RefreshRate > 1f && Xr.RefreshRate < 110f)
                SetupAdvice.Add($"HighRefresh profile: the headset reports {Xr.RefreshRate:0} Hz ({Xr.RefreshRateSource}), so the game cannot run faster than {Xr.RefreshRate:0} FPS. Set 120 Hz {whereRate}.");

            if (Xr.IsVrActive && loadedLink && VirtualDesktop.Detected == VirtualDesktopSupport.Path.VirtualDesktopOpenXR)
                SetupAdvice.Add("Windows' active OpenXR runtime is Virtual Desktop's, but the game is running on Meta Quest Link's runtime (Oculus), " +
                                "so its resolution and refresh rate come from the Meta Quest Link app, not from Virtual Desktop.");

            double mp = Graphics?.EyeMegapixelsAtScale1 ?? 0;
            if (Xr.IsVrActive && mp > 12)
            {
                float eyeCap = ActiveSettings?.MaxEyeMegapixels ?? 0f;
                string capNote = eyeCap > 0f
                    ? "Until then CRIX caps what the game renders (MaxEyeMegapixels in the profile)."
                    : $"The {ActiveSection} profile does not cap it: under URP a lower render scale also lowers the resolution of the desktop window that OBS, Streamlabs or Discord record. " +
                      "Lowering the resolution in your VR app keeps both sharp. To prefer frame rate over a sharp stream instead, set MaxEyeMegapixels = 8 in the " +
                      $"[{ActiveSection}] section of BepInEx/config/com.crix.crixoptimizer.profiles.cfg.";
                SetupAdvice.Add($"The VR runtime asks the game to render {mp:0} megapixels per eye at render scale 1.0 (a Quest 3 at default settings renders about {EyeResolution.Quest3DefaultMegapixels:0.0}). " +
                                $"That is far more than the headset can show and the main reason for a low frame rate on most GPUs. Lower the resolution {whereResolution}. " + capNote);
            }

            if ((Hooks & GraphicsHook.NvidiaOverlay) != 0 && (vd || link))
                SetupAdvice.Add("NVIDIA's overlay/capture (GeForce Experience / NVIDIA app: Instant Replay, recording) is hooked into the game. Instant Replay records all the time with the GPU's video encoder, " +
                                "which Virtual Desktop and Quest Link also use to stream to the headset. Turn Instant Replay off while playing in VR.");

            // Known optimisation mods get specific notes (OptimizerModsSupport); any other one the generic line.
            var optimizers = new List<string>();
            foreach (PluginInventory.Entry p in Plugins)
                if (p.Guid != null && p.Guid != ModInfo.Guid && p.Guid != ModInfo.PotassiumGuid && p.Guid != OptimizerModsSupport.GorillaOptimizerGuid &&
                    (p.Guid.IndexOf("optimi", StringComparison.OrdinalIgnoreCase) >= 0 || (p.Name ?? "").IndexOf("optimi", StringComparison.OrdinalIgnoreCase) >= 0))
                    optimizers.Add(p.Name ?? p.Guid);
            if (optimizers.Count > 0)
                SetupAdvice.Add("Other optimisation mods are installed (" + string.Join(", ", optimizers.ToArray()) + "). They may patch overlapping parts of the game and change the same settings. " +
                                "If you have problems, test with only one optimiser.");
            RefreshOptimizerMods(Time.unscaledTime);
            SetupAdvice.AddRange(optimizerModAdvice);

            // Timed unused-asset unloads skipped (by CRIX's profile or by Potassium) and memory has really grown.
            if (TimedUnloadsSkipped && !cfg.DeferredAssetUnload.Value && MemoryStats.LargestGrowthSinceBaselineBytes >= 1024L * 1024 * 1024)
                SetupAdvice.Add($"Memory has grown since the game finished loading (Unity {MemoryMonitor.FormatBytes(MemoryStats.GrowthSinceBaselineBytes)}, " +
                                $"graphics {MemoryMonitor.FormatBytes(MemoryStats.GfxGrowthSinceBaselineBytes)}). " +
                                (Live.SkipTimedAssetUnloads ? "The profile" : "Potassium") + " skips the game's timed unused-asset unloads (they cause hitches), so assets such as " +
                                "other players' cosmetics and visited maps stay in memory. Set [Memory] DeferredAssetUnload = true in com.crix.crixoptimizer.cfg: " +
                                "CRIX then frees them once memory has grown by DeferredAssetUnloadGrowthMB, and only while you are not in a room.");

            foreach (string line in SetupAdvice)
                once.Info("setup:" + line, line);

            string cap = Graphics?.EyeCapNote ?? "";
            if (cap != lastEyeCapNote)
            {
                lastEyeCapNote = cap;
                if (cap.Length > 0)
                    log.Info("Eye resolution cap: " + cap + ".");
            }
        }


        // ================================================================== streaming / recording awareness

        /// <summary>
        /// The profile to run: the user's choice, or a streaming profile while auto streaming mode detects OBS or
        /// Discord. Stronger profiles the user picked on purpose (MaximumPerformance, ReShadeCompatible, MultiMod,
        /// Custom) are never replaced; VirtualDesktop switches to its Streaming preset.
        /// </summary>
        private void ResolveEffectiveProfile(out ProfileKind kind, out VirtualDesktopPreset vdPreset)
        {
            kind = cfg.Profile.Value;
            vdPreset = cfg.VirtualDesktopVariant.Value;
            BaseSection = ProfilePresets.SectionFor(kind, vdPreset);
            AutoProfileReason = "";
            if (!cfg.AutoStreamingMode.Value || Capture.Active == CaptureKind.None)
                return;

            if (kind == ProfileKind.VirtualDesktop)
            {
                if (vdPreset != VirtualDesktopPreset.Streaming)
                {
                    vdPreset = VirtualDesktopPreset.Streaming;
                    AutoProfileReason = Capture.Reason;
                }
                return;
            }
            if (kind == ProfileKind.Balanced || kind == ProfileKind.OBSStreaming || kind == ProfileKind.DiscordCall)
            {
                ProfileKind target = Capture.Active == CaptureKind.Obs ? cfg.ObsProfile.Value : cfg.DiscordProfile.Value;
                if (target == ProfileKind.VirtualDesktop)
                    vdPreset = VirtualDesktopPreset.Streaming;
                if (target != kind)
                {
                    kind = target;
                    AutoProfileReason = Capture.Reason;
                }
            }
        }

        internal const string SpotifyAdvice =
            "Spotify is running. Its desktop app is a Chromium-based app that uses GPU hardware acceleration by default; " +
            "if you notice stutter, try turning that off in Spotify's settings (needs a Spotify restart). CRIX keeps a little extra GPU headroom while it runs and never touches Spotify.";

        private void UpdateCaptureState(float now)
        {
            // Evaluated even with auto streaming mode off: the DesktopCamera setting also needs it. Profile
            // switching itself is gated by AutoStreamingMode in ResolveEffectiveProfile.
            try
            {
                ObsStatus obs = Obs?.Status ?? ObsStatus.Disconnected;
                ObsStatus slobs = Streamlabs?.Status ?? ObsStatus.Disconnected;
                bool appsKnown = cfg.CompanionAppDetection.Value && Scanner.Latest != null && Scanner.Latest.ProcessesScanned;
                if (appsKnown)
                {
                    obsSeen |= (Apps & (CompanionApp.Obs | CompanionApp.StreamlabsDesktop)) != 0;
                    discordSeen |= (Apps & CompanionApp.Discord) != 0;
                }
                Capture.Trigger = cfg.StreamingTriggerSetting.Value;
                Capture.HoldSeconds = cfg.StreamingHoldSeconds.Value;
                bool changed = Capture.Update(now, new CaptureSignals
                {
                    Apps = Apps,
                    Hooks = Hooks,
                    ObsWebSocketConnected = obs.Connected || slobs.Connected,
                    ObsStreaming = obs.Streaming || slobs.Streaming,
                    ObsRecording = obs.Recording || slobs.Recording,
                    ObsOtherOutput = obs.OtherOutput || slobs.OtherOutput,
                    ObsHookStale = appsKnown && obsSeen && (Apps & (CompanionApp.Obs | CompanionApp.StreamlabsDesktop)) == 0,
                    DiscordHookStale = appsKnown && discordSeen && (Apps & CompanionApp.Discord) == 0,
                });
                if (changed)
                    MarkDirty();
            }
            catch (Exception e)
            {
                Fail("Streaming detection", e);
            }
        }

        private void EnsureObsClient()
        {
            bool wanted = cfg.ObsWebSocket.Value && !IsFaulted("OBS WebSocket");
            string key = wanted ? cfg.ObsWebSocketPort.Value + "|" + cfg.ObsWebSocketPassword.Value : null;
            if (key == obsClientKey)
                return;
            StopObsClient();
            obsClientKey = key;
            if (key == null)
                return;
            try
            {
                Obs = new ObsWebSocketClient(cfg.ObsWebSocketPort.Value, cfg.ObsWebSocketPassword.Value, log);
                Obs.Start();
            }
            catch (Exception e)
            {
                Obs = null;
                Fail("OBS WebSocket", e);
            }
        }

        /// <summary>Streamlabs Desktop's local API needs no setup, so it is read whenever Streamlabs is running.</summary>
        private void EnsureStreamlabsClient()
        {
            bool wanted = cfg.StreamlabsApi.Value && (Apps & CompanionApp.StreamlabsDesktop) != 0 && !IsFaulted("Streamlabs API");
            if (wanted == (Streamlabs != null))
                return;
            if (!wanted)
            {
                StopStreamlabsClient();
                return;
            }
            try
            {
                Streamlabs = new StreamlabsStatusClient(log);
                Streamlabs.Start();
            }
            catch (Exception e)
            {
                Streamlabs = null;
                Fail("Streamlabs API", e);
            }
        }

        private void StopStreamlabsClient()
        {
            StreamlabsStatusClient client = Streamlabs;
            Streamlabs = null;
            if (client == null)
                return;
            try { client.Stop(); } catch (Exception e) { log.Debug("Stopping Streamlabs client: " + e.Message); }
        }

        private void StopObsClient()
        {
            ObsWebSocketClient client = Obs;
            Obs = null;
            obsClientKey = null;
            if (client == null)
                return;
            // Never block the game thread waiting for the background thread; it exits on its own.
            try { client.Stop(); } catch (Exception e) { log.Debug("Stopping OBS client: " + e.Message); }
        }

        // ================================================================== dashboard & report

        private bool DashboardShouldExist() => cfg.DashboardVisible.Value || alertVisibleUntil > 0f;

        private void SetDashboard(bool visible)
        {
            try
            {
                if (visible && dashboard == null)
                {
                    dashboard = plugin.gameObject.AddComponent<DashboardOverlay>();
                    nextDashboard = 0f;
                }
                else if (!visible && dashboard != null)
                {
                    UnityEngine.Object.Destroy(dashboard);
                    dashboard = null;
                }
                if (dashboard != null)
                {
                    dashboard.ShowPanel = cfg.DashboardVisible.Value;
                    dashboard.Corner = cfg.DashboardPosition.Value;
                    dashboard.FontSize = cfg.DashboardFontSize.Value;
                    dashboard.Alert = alertVisibleUntil > 0f ? LastAlertText : "";
                    if (visible && cfg.DashboardVisible.Value)
                        Streaming.Update(Apps, Hooks, cfg.Profile.Value, true);
                    if (Streaming.GameCaptureHooked && cfg.DashboardVisible.Value)
                        once.Warn("dash-obs", "The dashboard is drawn into the game window, which OBS game capture is recording.");
                }
            }
            catch (Exception e)
            {
                Fail("Dashboard", e);
            }
        }

        private void RefreshDashboard(float now)
        {
            if (IsFaulted("Dashboard"))
                return;
            try
            {
                dashboard.ShowPanel = cfg.DashboardVisible.Value;
                dashboard.Corner = cfg.DashboardPosition.Value;
                dashboard.FontSize = cfg.DashboardFontSize.Value;
                if (cfg.DashboardVisible.Value)
                    dashboard.Text = DashboardText.Build(this, cfg.DashboardCompact.Value);
            }
            catch (Exception e)
            {
                Fail("Dashboard", e);
            }
        }

        public void WriteSystemReport()
        {
            Guard("System report", () =>
            {
                Plugins = PluginInventory.Snapshot();
                string text = SystemReport.Build(this);
                string path = Path.Combine(OutputDirectory, "system-report.txt");
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Directory.CreateDirectory(OutputDirectory);
                        File.WriteAllText(path, text);
                        log.Info("System report written to " + path);
                    }
                    catch (Exception e)
                    {
                        log.Warn("Could not write system report: " + e.Message);
                    }
                });
            });
        }

        // ================================================================== accessors for UI / report

        public PluginConfig Config => cfg;

        public bool AdaptiveActive => cfg.AdaptiveEnabled && cfg.Enabled.Value && !Suspended;

        // ================================================================== status & fault isolation

        private void UpdateStatus()
        {
            bool on = cfg.Enabled.Value && !Suspended;
            Status.Set("Modules", "Game stutter fixes", ModuleStateFor(cfg.GameStutterFixes.Value && on, "Game patches", GamePatches?.DeferredToPotassium == true));
            Status.Set("Modules", "Visual update throttles", ModuleStateFor(cfg.VisualThrottles.Value && on, "Game patches", GamePatches?.DeferredToPotassium == true));
            Status.Set("Modules", "Distant cosmetic culling", ModuleStateFor(cfg.CosmeticCulling.Value && on, "Game patches", GamePatches?.DeferredToPotassium == true));
            Status.Set("Modules", "Graphics settings", ModuleStateFor(cfg.GraphicsSettings.Value && on, "Graphics settings", false));
            Status.Set("Modules", "Frame pacing settings", ModuleStateFor(cfg.FramePacingSettings.Value && on, "Graphics settings", false));
            Status.Set("Modules", "Adaptive quality", ModuleStateFor(cfg.AdaptiveEnabled && on, "Adaptive", false));
            Status.Set("Modules", "Diagnostics", ModuleStateFor(cfg.Diagnostics.Value, "Memory monitor", false));
            Status.Set("Modules", "Compatibility scanner", ModuleStateFor(cfg.CompatibilityScanner.Value, "Compatibility scanner", false));
            Status.Set("Modules", "Companion app detection", cfg.CompanionAppDetection.Value ? ModuleState.Active : ModuleState.Disabled);
            Status.Set("Modules", "Graphics hook detection", cfg.GraphicsHookDetection.Value ? ModuleState.Active : ModuleState.Disabled);
        }

        private ModuleState ModuleStateFor(bool enabled, string faultKey, bool deferred)
        {
            if (IsFaulted(faultKey))
                return ModuleState.Faulted;
            if (deferred)
                return ModuleState.Deferred;
            return enabled ? ModuleState.Active : ModuleState.Disabled;
        }

        private bool IsFaulted(string module) => faultedModules.Contains(module);

        private void Guard(string module, Action action)
        {
            if (IsFaulted(module))
                return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Fail(module, e);
            }
        }

        private void Fail(string module, Exception e)
        {
            moduleFailures.TryGetValue(module, out int count);
            moduleFailures[module] = ++count;
            if (count >= 3)
            {
                faultedModules.Add(module);
                log.Error($"{module} failed {count} times and is disabled for this session. Last error: {e}");
                UpdateStatus();
            }
            else
            {
                log.Warn($"{module} error ({count}/3): {e.GetType().Name}: {e.Message}");
            }
        }
    }
}

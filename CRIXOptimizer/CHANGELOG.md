# Changelog

## 1.1.0 - 2026-10-14

Answers the report "it does 0 optimization", adds a 120/144 Hz profile, automatic streaming/recording mode (OBS,
Streamlabs, Discord), Spotify awareness and Sakuraa Client / GTFX awareness. **Built and unit/integration-tested;
still not tested inside Gorilla Tag or benchmarked on hardware** (see docs/TEST_REPORT.md). No performance numbers
are claimed.

### Why 1.0.0 could look like it did nothing (and what changed)

- **Unity 6 + URP.** Gorilla Tag PC runs Unity 6000.1.9f1 with the Universal Render Pipeline (URP 17). URP ignores
  the built-in quality settings (shadow quality/cascades, MSAA, pixel lights), and it copies its own asset's render
  scale and MSAA into the VR eye textures every frame; `XRSettings.eyeTextureResolutionScale` is not supported under
  URP. 1.1.0 changes render scale, MSAA, shadow distance, shadow cascades and additional lights per object through
  the **public properties of the active URP asset** (each only if the property exists and is public). Every 5 s it
  checks the XR display's render-target scale (what URP writes) and warns if it does not match.
- **Adaptive quality was off by default.** It is now on (new key `[Adaptive] Mode = On`; the 1.0.0 key
  `[Adaptive] Enabled` is removed from the file and the log says so). It only acts when the frame rate is below target.
- **CPU-bound lobbies.** Lowering resolution does not help when the CPU is the bottleneck (full lobbies, many
  cosmetics). Adaptive mode now also reduces distant remote-player cosmetic detail and cosmetic update rates, and
  checks whether a resolution drop really made frames faster. If it did not, resolution is restored and only
  CPU-side reductions are used; the next check waits 3 min, then 6, 12 ... up to 30 min while it keeps failing. A
  frame rate locked at half the refresh rate (reprojection) before and after counts as inconclusive, not CPU-bound.
- **Mild presets.** Balanced only capped shadow distance. Presets are stronger now (table in docs/PROFILES.md).
  Existing profile files are upgraded automatically: values that still equal the 1.0.0 default get the 1.1.0
  default, and any value you edited is kept (`[Meta] PresetRevision`, in both config files).
- **No visible summary.** The log prints "CRIX status: ..." once XR is up: profile, active patches, engine settings
  actually changed (old -> new), adaptive state, desktop camera. It warns with the reason when nothing is being
  changed. The dashboard and system report show the same summary. Settings applied at the game's own value no
  longer count as "changed".
- **VR frame rates are capped** at the headset refresh rate, so gains show up as fewer dropped frames, lower and
  steadier frame times and more GPU/CPU headroom, not as a higher FPS number. Gorilla Tag bakes most of its
  lighting, so shadow and light limits usually change little.

### Added

- **`HighRefresh` profile** for 120/144 Hz headsets: keeps resolution (adaptive mode does not change it in this
  profile), cuts per-frame CPU work (distant cosmetics 3.5/6 m and 4/8 players, cosmetic updates 20/s, LOD bias 0.8,
  two-bone skinning, 64 particle raycasts, 1 ms upload slices), MSAA capped at 2x, 10 % GPU headroom. The log points
  out when the headset reports less than 110 Hz and how to set 120 Hz in Virtual Desktop, Meta Quest Link or SteamVR.
- **`DesktopCamera` profile setting** (`Unchanged` / `OffWhenNotStreaming` / `Off`; `OffWhenNotStreaming` in
  HighRefresh, `Unchanged` elsewhere). Switches off the game's third-person camera, which renders the scene a second
  time to the desktop window, while OBS/Streamlabs is not recording or live; the window then shows the headset view
  (XR mirror, left eye). Rendering only (`Camera.enabled` of the "Shoulder Camera"; its CinemachineBrain and
  scripts keep running), never without an active headset, never a stereo or render-texture camera, restored like
  every other setting, and left alone if the game or another mod turns it back on. It stays on while the game's LIV
  camera drives it, under the ReShade guard, and when another mod has taken it over (Sakuraa Client uses it as its
  spectator camera) unless `[Compatibility] DesktopCameraWhenModsUseIt = true`.
- **Auto streaming mode** (`[Streaming]`, on by default). While OBS/Streamlabs records or streams, or Discord runs,
  CRIX switches to `ObsProfile` (OBSStreaming) or `DiscordProfile` (DiscordCall), and back after `HoldSeconds`
  (20 s). Only Balanced/OBSStreaming/DiscordCall are switched; VirtualDesktop moves to its Streaming preset;
  MaximumPerformance, HighRefresh, ReShadeCompatible, MultiMod and Custom are never replaced.
  - **OBS Studio 28+:** read-only obs-websocket v5 client (127.0.0.1 only, SHA-256 challenge authentication). It
    only ever sends `GetStreamStatus`, `GetRecordStatus`, `GetReplayBufferStatus` and `GetVirtualCamStatus` and
    listens to the matching state events; it cannot
    start, stop or change anything in OBS. The WebSocket server must be enabled in OBS (see INSTALLATION.md).
    Without it, "OBS running" is the signal. The password is hidden from in-game config editors.
  - **Streamlabs Desktop:** read-only poll of its local API (127.0.0.1:28194, `StreamingService.getModel`, every
    2 s) while Streamlabs runs (`[Streaming] StreamlabsApi`). Dual-output (horizontal + vertical) state is combined;
    "busy" answers while Streamlabs loads count as unknown.
  - **Discord:** its call/Go Live state cannot be read without injecting into Discord, which CRIX does not do;
    "Discord running" (or its overlay in the game) is the signal.
  - Capture DLLs stay loaded in the game after OBS/Discord close; they only count while their app still runs.
  - `Trigger`: `Auto`, `WhenAppRunning` or `WhenRecordingOrLive`.
- **Spotify detection.** While Spotify runs, adaptive mode keeps 5 % more GPU headroom (when the VR runtime reports
  GPU time). The log explains Spotify's hardware-acceleration option. CRIX never touches Spotify.
- **Sakuraa Client / GTFX awareness.** Detects Sakuraa (assemblies, plugin GUIDs and Harmony owners whose name
  contains "Sakuraa") and lists it in the log and system report. GTFX is part of Sakuraa and can only be tuned in
  Sakuraa's own presets; CRIX does not modify Sakuraa, GTFX or its cameras.
- **Camera inventory.** Lists every active camera (VR, desktop window, render textures) with resolution and a
  derived pixel load relative to one VR eye (system report, dashboard), and points out a large desktop camera.
- **URP asset report.** The game's URP asset values at start-up (render scale, MSAA, shadows, HDR, opaque/depth
  texture copies) go into the log and system report; the log notes when another mod has turned on the opaque/depth
  copies (extra GPU work on every camera). Read-only.
- System report: "What CRIX is doing", render-scale lever and check, URP values, Sakuraa, cameras, desktop camera,
  headset refresh rate, streaming state, base vs running profile.
- `tools/check_unity_api.py`: checks every Unity member the DLL uses against Unity's published C# source for a given
  version (all 162 references are declared in 6000.1.9f1; three obsolete-but-present fallbacks).

### Changed

- MSAA (`MsaaSamples`) is a cap on both pipelines and is never raised; MaximumPerformance, HighRefresh and
  VirtualDesktop.Performance cap it at 2x. Other presets still leave MSAA alone.
- Under URP the render scale also sets the desktop camera's resolution (what OBS records), so `OBSStreaming` and
  `VirtualDesktop.Streaming` no longer let adaptive mode lower it.
- Preset values (1.0.0 -> 1.1.0):
  - Balanced: shadow distance 60 -> 50 m, cascades max 2, lights 2, realtime reflection probes off, occlusion
    mesh, async upload 1 ms, background loading below normal.
  - OBSStreaming: shadows 40 -> 30 m, cascades 2, lights 1, soft particles off, particle raycasts 128, cosmetic
    rates 24/s, closer distant-cosmetic culling, GPU headroom 10 -> 20 %, adaptive render scale off.
  - DiscordCall: shadows 35 m, cascades 2, GPU headroom 10 %.
  - VirtualDesktop: cascades 2. VirtualDesktop.Streaming: shadows 30 m, cascades 2, lights 1, rates 24/s,
    headroom 20 %, adaptive render scale off. VirtualDesktop.Performance: cascades 1, MSAA 2x.
  - MaximumPerformance: MSAA 2x. ReShadeCompatible: cascades 2.
- Streamlabs Desktop's own obs64 backend process is no longer reported as OBS Studio; `DiscordDevelopment` is detected.

### Fixed during review (before release)

- OBS client: a WebSocket ping no longer blocks status polling (and drops the connection 10 s later); fragments are
  reassembled across interleaved control frames and capped at 4 MB in total; the reconnect delay resets after a
  working session and when OBS is started; a "kicked in OBS" state stays visible; password problems are rechecked
  every minute instead of every 5 minutes.
- Adaptive mode keeps its CPU-side levers if the graphics module has failed, and the end of a "CPU-bound" period is
  handled like any other change (a resolution drop then is checked by the probe).
- **Lag spikes from resolution changes.** Every render-scale change reallocates the VR eye textures (a visible
  hitch). Adaptive mode now lowers resolution at most once a minute, undoes a drop at once when frames do not get
  faster or stay locked at half the refresh rate (reprojection), and HighRefresh does not change resolution at all.
- **Hitch log.** Frames over 70 ms are logged with what was going on (garbage collection, map load, a CRIX change)
  and counted in the system report, so "lag spikes" can be traced.
- OBS: the replay buffer and virtual camera count as capturing (read-only `GetReplayBufferStatus` /
  `GetVirtualCamStatus`); Streamlabs is polled every 2 s and its replay buffer is read too.
- Desktop camera: left alone once the game or another mod turns it back on (until the profile is re-applied); the
  mirror setting recovers after a headset reconnect; status texts name the real reason.
- OBS password warnings are logged once instead of every minute; a reset to defaults no longer re-runs the 1.0.0
  preset upgrade; the refresh-rate advice only uses rates the VR runtime reported.

## 1.0.0 - 2026-10-12

First release. Based on Potassium 1.0.0 (GPL-3.0, https://github.com/niko-tar-gz/Potassium, tag `v1.0.0-release`).
**Built and unit/integration-tested; not yet tested inside Gorilla Tag or benchmarked on hardware** (see
docs/TEST_REPORT.md).

### Modifications to Potassium 1.0.0 code (GPL-3.0 section 5a notice)

Applies to every Potassium-derived file (listed in LICENSES.md):

- **No link to Gorilla Tag's assembly.** Game types are resolved by name at runtime (`Utilities/GameTypes.cs`) and
  members are accessed through compiled delegates (`Utilities/Accessors.cs`) instead of direct references, so builds
  need no game files and a renamed game member disables only the patch that uses it.
- **Per-patch application instead of `PatchAll`.** Each patch is applied individually and recorded
  (`Utilities/PatchSet.cs`). In Potassium, one failing target can stop the remaining patches from being applied.
- **Failed-patch cleanup.** When Harmony rejects a patch, CRIX removes the partially registered patch so the method
  stays patchable by other mods (regression test `FailedPatchDoesNotPoisonTheMethodForOtherMods`).
- **Fault isolation.** Every patch body catches exceptions, switches itself off and lets the game's code run.
- **Live configuration.** Potassium reads its config once at startup; every CRIX option applies immediately, and
  work held back by a feature is released when that feature is switched off.
- **Throttles** (texture transitions, voice-reactive visuals, proximity reactors): frame-staggered by object identity
  instead of per-instance timers in a `ConditionalWeakTable`. Same update rate, work spread evenly across frames
  (scheduling simulation in the tests: peak updates in one frame 10 vs 30 for 30 objects). The effective rate never
  drops below the configured rate, and throttling turns itself off when the frame rate is too low to throttle
  (e.g. reprojection at 45 FPS).
- **Shared Blocks deferral:** keeps every deferred manager (Potassium stores one and could lose an earlier one), and
  starts them immediately if the feature is disabled or CRIX is suspended or unloaded.
- **Rig pool staggering:** missing rigs are still created on demand after the feature is switched off live (fixes a
  case where a player could get no rig).
- **Room-leave GC:** all `GC.Collect` overloads are redirected (Potassium handled `GC.Collect(int)` only). New
  `Incremental` mode does a short time-sliced collection when the game uses incremental GC.
- **Custom-map sanitizer:** new default `SafeLookups` keeps the game's own sanitizer and replaces only its
  `List.Contains` allow-list lookups with cached hash lookups that give identical results (verified in the tests
  against 500 random objects and runtime allow-list changes). Potassium's full re-implementation is kept as opt-in
  `FullReplacement`, because it bypasses any rule the game adds later.
- **Inactive entity scans:** the active-manager lookup is a compiled delegate instead of `PropertyInfo.GetValue` per call.
- **Distant cosmetic culling:** original values are captured per instance and restored when disabled/unloaded; new
  values reach existing instances live.
- **Scoreboard staggering:** identity sets instead of Unity instance IDs; queued work is flushed when disabled.
- **Potassium coexistence:** if Potassium is installed, CRIX's copies of these patches are not applied (configurable).

### New in CRIX OPTIMIZER

- Profiles: Balanced, MaximumPerformance, VirtualDesktop (Balanced/Performance/Quality/Streaming), OBSStreaming,
  DiscordCall, ReShadeCompatible, MultiMod, Custom. All editable; switchable live (F9). Optional auto-selection.
- Engine graphics and frame-pacing settings through public Unity APIs, mostly as limits, never more expensive than the
  game's own value. Built-in-pipeline-only settings are skipped under URP/HDRP.
- Settings ledger: original capture, engine-clamp detection, drift detection (other mods/game), respectful restore,
  panic restore (F10), restore on disable/unload/quit.
- Adaptive quality engine with hysteresis, cooldown, settle time, exponential probe back-off, CPU-bound detection,
  GPU-headroom target, min/max levels, freeze and manual override.
- Diagnostics: frame-time window statistics (avg, 1%/0.1% low, σ, p99, max, over-budget %), GC rate and
  GC-coincident hitches, memory trend, Unity graphics-memory estimate, XR runtime GPU/compositor time, dropped frames
  and refresh rate when reported, CRIX's own overhead.
- Desktop dashboard (off by default), benchmark recorder (CSV + summary), `tools/compare_benchmarks.py`.
- Compatibility: plugin inventory, Harmony overlap report, read-only detection of Virtual Desktop, OBS, Discord,
  SteamVR, Quest Link, ReShade and overlay hooks; ReShade guard.
- System report, generated profile documentation and example configs, reference checker, Mono test suite.

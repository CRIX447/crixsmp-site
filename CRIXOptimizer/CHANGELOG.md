# Changelog

## 1.1.0 - 2026-10-12

Answers the report "it does 0 optimization", adds automatic streaming/recording mode (OBS, Streamlabs, Discord),
Spotify awareness and Sakuraa Client / GTFX awareness. **Built and unit/integration-tested; still not tested inside
Gorilla Tag or benchmarked on hardware** (see docs/TEST_REPORT.md). No performance numbers are claimed.

### Why 1.0.0 could look like it did nothing (and what changed)

- **URP.** Gorilla Tag PC appears to render with the Universal Render Pipeline. URP ignores the built-in quality
  settings (shadow quality/cascades, MSAA, pixel lights), and it copies its own asset's render scale and MSAA into
  the VR eye textures every frame, overwriting `XRSettings.eyeTextureResolutionScale`. 1.1.0 changes render scale,
  MSAA, shadow distance, shadow cascades, additional lights per object and soft shadows through the **public
  properties of the active URP asset** when the game uses URP (each one only if the property exists). It also
  checks every 5 s that the render scale actually reached the eye textures, and warns if it did not.
- **Adaptive quality was off by default.** It is now on (new key `[Adaptive] Mode = On`, which replaces
  `[Adaptive] Enabled`, so existing installs get the new default). It only acts when the frame rate is below target.
- **CPU-bound lobbies.** Lowering resolution does not help when the CPU is the bottleneck (full lobbies, many
  cosmetics). Adaptive mode now also reduces distant remote-player cosmetic detail and cosmetic update rates, and
  a resolution probe checks whether a resolution drop really made frames faster. If it did not, resolution is
  restored and only CPU-side reductions are used for 3 minutes.
- **Mild presets.** Balanced only capped shadow distance. Presets are stronger now (table in docs/PROFILES.md).
  Existing profile files are upgraded automatically: values that still equal the 1.0.0 default get the 1.1.0
  default, and any value you edited is kept (`[Meta] PresetRevision` in the profiles file).
- **No visible summary.** The log now prints "CRIX status: ..." once XR is up (profile, active patches, engine
  settings actually changed with old -> new values, adaptive state). It warns with the reason when nothing is being
  changed. The dashboard and system report show the same summary. Settings applied at the game's own value no
  longer count as "changed".
- **VR frame rates are capped** at the headset refresh rate, so gains show up as fewer dropped frames, lower and
  steadier frame times and more GPU/CPU headroom, not as a higher FPS number.

### Added

- **Auto streaming mode** (`[Streaming]`, on by default). While OBS/Streamlabs records or streams, or Discord runs,
  CRIX switches to `ObsProfile` (OBSStreaming) or `DiscordProfile` (DiscordCall). It switches back after `HoldSeconds`
  (20 s). Only Balanced/OBSStreaming/DiscordCall are switched; VirtualDesktop moves to its Streaming preset;
  MaximumPerformance, ReShadeCompatible, MultiMod and Custom are never replaced.
  - **OBS Studio 28+:** read-only obs-websocket v5 client (localhost only, SHA-256 challenge authentication).
    It only ever sends `GetStreamStatus` and `GetRecordStatus` and listens to stream/record state events. It cannot
    start, stop or change anything in OBS. The WebSocket server must be enabled in OBS (see INSTALLATION.md).
    Without it, "OBS running" is used as the signal.
  - **Streamlabs Desktop:** read-only poll of its local API (127.0.0.1:28194, `StreamingService.getModel`, every 5 s)
    while Streamlabs runs. No setup needed.
  - **Discord:** Discord's call/Go Live state cannot be read without injecting into Discord, which CRIX does not do;
    "Discord running" (or its overlay hook in the game) is the signal.
  - `Trigger`: `Auto`, `WhenAppRunning` or `WhenRecordingOrLive`.
- **Spotify detection.** While Spotify runs, adaptive mode keeps 5% more GPU headroom. The log explains Spotify's GPU
  hardware-acceleration option. CRIX never touches Spotify.
- **Sakuraa Client / GTFX awareness.** Detects Sakuraa (loaded assemblies, plugin GUIDs, Harmony owners), lists it
  in the log and system report, and warns about unofficial rebuilt copies. GTFX is Sakuraa's closed shader system and
  can only be tuned in Sakuraa's own presets; CRIX does not modify Sakuraa, GTFX or its cameras.
- **Camera inventory.** Lists every active camera (VR, desktop window, render textures) with resolution and a
  derived pixel load relative to one VR eye (system report, dashboard). While streaming, it points out a large
  desktop spectator camera. Read-only.
- System report: "What CRIX is doing" section, render-scale lever and check, graphics notes, Sakuraa, cameras,
  streaming state, base vs running profile.

### Changed

- MSAA (`MsaaSamples`) is now a cap on both pipelines and is never raised; MaximumPerformance and
  VirtualDesktop.Performance cap it at 2x. Other presets still leave MSAA alone.
- Preset values (1.0.0 -> 1.1.0):
  - Balanced: shadow distance 60 -> 50 m, cascades max 2, lights 2, realtime reflection probes off, occlusion
    mesh, async upload 1 ms, background loading below normal.
  - OBSStreaming: shadows 40 -> 30 m, cascades 2, lights 1, soft particles off, particle raycasts 128, cosmetic
    rates 24/s, closer distant-cosmetic culling, GPU headroom 10 -> 20%.
  - DiscordCall: shadows 35 m, cascades 2, GPU headroom 10%.
  - VirtualDesktop: cascades 2. VirtualDesktop.Streaming: shadows 30 m, cascades 2, lights 1, rates 24/s,
    headroom 20%. VirtualDesktop.Performance: cascades 1, MSAA 2x.
  - MaximumPerformance: MSAA 2x. ReShadeCompatible: cascades 2.
- Tests: 87 -> see docs/TEST_REPORT.md.

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

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
  `[Adaptive] Enabled` is removed from the file and the log says so). It acts only when the frame rate drops below target or, when the VR runtime reports GPU time, when the GPU uses more than the profile's AdaptiveGpuHeadroomPercent share of the frame.
- **CPU-bound lobbies.** Lowering resolution does not help when the CPU is the bottleneck (full lobbies, many
  cosmetics). Adaptive mode now also reduces distant remote-player cosmetic detail and cosmetic update rates, and
  checks whether a resolution drop really made frames faster. If it did not, resolution is restored and only
  CPU-side reductions are used; the next check waits 3 min, then 6, 12 ... up to 30 min while it keeps failing. If
  frames stay locked at half the refresh rate (reprojection) before and after, the drop is undone too and resolution
  is kept for the same time, but this is shown as a half-rate lock, not as CPU-bound.
- **Mild presets.** Balanced only capped shadow distance. Presets are stronger now (table in docs/PROFILES.md).
  Existing profile files are upgraded automatically: values that still equal the 1.0.0 default get the 1.1.0
  default, and any value you edited is kept (`[Meta] PresetRevision`, in both config files).
- **No visible summary.** The log prints "CRIX status: ..." about 5 s after start and again when the headset becomes
  active: profile, active patches, engine settings
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
  Sakuraa's own presets; CRIX does not modify Sakuraa or GTFX, and leaves its cameras alone unless
  `ExtraCameraMaxFps` is set.
- **Performance notes** (`[Diagnostics] PerformanceNotes`, on): one file per session in `BepInEx/CRIXOptimizer/notes/`
  (last 10 kept), written every minute on a worker thread and when the game closes. A timeline every 10 s (FPS, 1 %
  low, worst frame, hitches, zone, lobby size, recording/streaming, profile, adaptive level, eye scale, memory),
  events (zone changes, room joins/leaves, players joining/leaving, recording/streaming start/stop, profile changes,
  big hitches with cause), summaries per zone, per lobby size (1-4, 5-9, 10-14, 15-20) and per streaming state, the ten
  worst hitches, and the system report. Zones and player counts are read by name (`ZoneManagement.instance.activeZones`,
  `NetworkSystem.Instance.InRoom/RoomPlayerCount`), read-only; no room codes or player names.
- Big hitches at a zone change or a room join/leave are attributed to it (log, notes, system report).
- Descriptions corrected: `BackgroundLoading = BelowNormal` is Unity's own default on PC and the game does not change
  it, so in the presets it mostly keeps that value in place (Unity's per-frame budgets for finishing loads are listed).
- Startup loading is kept apart: the first minute after the game opens, extended by 20 s after each big hitch in it
  (at most 5 minutes), counts as loading (the game and mods such as Sakuraa Client loading their content). Its hitches
  are marked `[loading]`, summarised under *Startup* and left out of the per-zone/lobby/streaming summaries and the
  worst-hitch list; the system report counts them separately.
- **Entering maps** (notes): the first 10 s after a zone becomes active (plus the 1.2 s before CRIX saw the change, so
  the frame in which the game switched the zone on is included), split into the first visit this session and later
  visits (worst frame, big hitches, hitches). A hitch only on the first visit points to first use of shaders or
  textures; one on every visit to the game switching the map on. A visit after unused assets were freed (CRIX's
  deferred unload, or the game unloading a custom map) counts as a first visit again; leaving a zone ends its
  measurement. Zone change events say which visit it was.
- More hitch causes: a time-of-day change (the game swaps the lightmaps of every loaded map;
  `BetterDayNightManager.currentTimeIndex`), a map loading lightmaps (`BetterDayNightManager.allScenesRenderData`,
  `PerSceneRenderData.IsLoadingLightmaps`), and "zone scenes were still loading" (`ZoneManagement.AnyActiveLoadOps`);
  the last two are read only on hitch frames. Time-of-day changes are events. The
  system report counts them as "at a time-of-day change or lightmap load".
- **Custom maps** (notes): each mod.io map gets its own zone name (`customMaps:<map id>`; public map ids, nothing
  personal), entering and leaving are events (a map loading more of its scenes through its load zones keeps its id),
  and once its entry window is over its content is counted once, read-only: renderers, triangles, LOD groups, lights
  (realtime, with shadows), reflection probes (realtime, set to update every frame, without time slicing), cameras
  rendering into textures, particle systems (sum of their limits), with a note on what costs frame time (taking into
  account whether realtime shadows and realtime reflection probes can render at all). The count waits while the map
  is still loading and its duration is written next to it. The last map's line is also in the system report.
- **Camera renders per frame** in the notes timeline (headset, desktop window, cameras into textures, reflection
  probes), counted in `RenderPipelineManager.beginCameraRendering`. Shows whether reflection probes still render
  with `RealtimeReflectionProbes = Off` and how many extra renders a map or mod adds.
- Graphics-driver and texture memory in the notes timeline (when Unity reports them); the URP asset line in the
  system report includes main/additional light shadow support.
- The session trend knows the map of each minute (the zone with most play time in it): a slow drop is only looked for
  within one map, minutes split between maps are left out, and a lower frame rate in a different map is reported as
  the map ("you are in a different map"), not blamed on streaming or memory.
- **Session trend** (system report): one line per minute with FPS, 1 % low, hitches, garbage collections, managed,
  Unity and graphics memory, profile, recording/streaming state and adaptive level, for up to three hours. When the
  frame rate falls by 10 % and 8 FPS or more over the session (first three minutes after a two-minute warm-up against
  the last three, headset on), the log and report say from what to what and what changed with it (streaming started,
  profile, memory growth, hitches, garbage collections, adaptive level), or that nothing visible to CRIX changed and
  what usually causes it outside the game.
- **Notes on other optimisation mods**, from their published source: GorillaOptimizer's switched-on toggles (read
  from its saved PlayerPrefs, read-only) sorted into in effect / no effect in Gorilla Tag (built-in-pipeline settings
  URP ignores, its XR scale that URP overwrites every frame, frame caps VR ignores) with notes on Low Physics Rate,
  Low Textures, Low LOD Bias, Disable Post Processing and Aggressive GC; I HATE ALIENS (no overlap); Potassium (its
  own config is used and CRIX's adaptive CPU savings for busy lobbies are off while it is installed).
- A setup line when timed unused-asset unloads are skipped (profile or Potassium), `DeferredAssetUnload` is off and
  Unity or graphics memory has grown by 1 GB or more since loading finished.
- **Fixed: `DeferredAssetUnload` never ran while Potassium was installed** (CRIX's own patches are off then, and the
  check looked only at them, although Potassium skips the same unloads and the setup advice recommended the option).
  It now checks Potassium's own "Skip timed unused-asset unloads" setting (read from its config, read-only), and the
  read-only "in a room" check it needs is built in that case too (it was not, so the unload could still never run).
  Growth of graphics memory (visited maps, other players' cosmetics) now triggers it too; the growth baseline is taken
  once startup loading is over (also with the notes off) and again on the first sample after each unload, and nothing
  is freed while the game is still loading. Its hitch has its own count and cause in the log, notes and system report.
  It still only runs while not in a room.
- **Spectator camera limit** (experimental; `[Performance] SpectatorCameraMaxFps`, default `0` = off;
  `SpectatorCameraCadence`, `[Compatibility] SpectatorCameraPresenter`, `SpectatorCameraWithReShade`). The desktop
  spectator view (the game's Shoulder Camera, Sakuraa's spectator camera) renders into a window-sized texture of
  CRIX's (no alpha, the camera's MSAA) at most N times a second, every Nth headset frame by default (120 Hz and 60:
  every 2nd frame; N held steady against frame-time noise and snapped to the headset's refresh rate), and its latest
  image is drawn into the window every frame as the lowest screen overlay (or copied at the end of the frame), so the
  window never shows the headset view in between and other mods' HUDs stay on top. N is never chosen so that the
  camera renders more than about 5 % above the limit (also at 61-70 fps on 120 Hz). It stands by only while the
  `DesktopCamera` setting really holds the camera off (not when it leaves it on for Sakuraa). A presenter or MSAA
  change re-takes the camera at once; a window resize or the headset going away hands it back in the same frame. The
  texture's MSAA is the lower of the URP asset's and the quality setting's (never more samples than in the window). The
  status line says when the VR render scale no longer applies to the camera (it can then render at full window size).
  A 60 fps recording may repeat a frame now and then (OBS samples on its own clock). It is a "pinned" camera of the extra-camera limiter
  (planned on its own, never adopted by `ExtraCameraMaxFps`). Handed back (render target cleared only if still CRIX's)
  on: off, suspend, quit, no headset, `DesktopCamera` switching the camera off, resize (re-engaged after 1 s), HDR
  output, another screen camera drawing after it, no render within 10 frames (counted while it is on), errors; another
  owner's render target is left alone until the profile is switched or re-applied, and a texture another camera still
  renders into is never destroyed. The limit keeps working if the desktop-camera module fails, and nothing of the
  limiter stays installed once nothing is pinned or limited.
  Camera inventory and render counts treat it as the window camera. Not tested in Gorilla Tag.
- Headset cameras are recognised also when `Camera.stereoEnabled` reports false (URP renders every Game camera without a
  target texture to the headset unless its URP data turns XR off): the desktop-camera setting, the spectator limit and
  the camera list never treat such a camera as the window's. The system report's camera line shows each camera's
  depth, target eye, stereo flag and URP XR setting.
  render into a texture (minimaps, tablets, Sakuraa's `MiniMapCam`/`DisplayCam`) render at most N times a second
  (time-based: each camera on its own grid with evenly spaced phases, re-spaced when cameras come and go; earliest
  due first within a per-frame budget of cameras x rate x frame time, with the frame time capped at twice the typical
  one, so cameras take turns instead of rendering in the same frame, long frames keep the rate and a hitch frame
  renders at most about two frames' worth; no catch-up burst; every frame when the game is slower than the limit). A player-loop step before Unity's
  `FinishFrameRendering` switches off only the cameras that are on and not due; `RenderPipelineManager.
  beginContextRendering` (after Unity has built the frame's camera list) switches exactly those back on, so every
  script and rendering callback sees the camera as its mod set it (`endContextRendering` and a step after
  `FinishFrameRendering` are fallbacks). Never VR cameras or the
  desktop-window camera; names matching the exclusion list (default `mirror,lck,photo,reflection,portal`) are
  skipped, also when the list is changed later. Fail-open: nothing is limited in a frame in which CRIX's own update
  did not run and allow it (suspended, disabled, faulted, plugin disabled). Stops by itself if the render pipeline's
  end event is missing or another mod keeps replacing the player loop. Needs URP.
- **ReShade preset advisor** (read-only). When ReShade is detected, reads ReShade's configuration the way ReShade
  itself does (checked against ReShade's source): `ReShade.ini` for the desktop window and `ReShadeVR.ini` for the
  headset (ReShade 6, per-key fallback to `ReShade.ini`), `[INSTALL] BasePath`, the start-up preset before the last
  used preset and the `ReShadePreset.ini` default, escaped commas (`,,`) and comment lines. Logs and reports, per
  runtime (a runtime switched off with `[GENERAL] Disable` is reported as off; ReShade's boolean and element rules,
  including repeated keys, are followed), whether Performance Mode is off and which enabled effects are usually expensive (global illumination,
  ambient occlusion, screen-space reflections, depth of field, motion blur, Clarity, god rays/volumetrics,
  DisplayDepth), by name.
- **`[Actions] CreateLightReShadePreset`**: writes `<preset>.CRIX-light.ini` next to each active preset that has such
  effects, with them removed from its `Techniques` line (effect settings and `TechniqueSorting` kept). The user's
  presets and ReShade's config files are never written. File errors are logged and never disable the action. For the
  headset, the log gives the `ReShadeVR.ini` line to set (its ReShade has no Home-key overlay), escaped like ReShade
  writes it; a shared preset that is the desktop window's start-up preset is pointed out; before the first VR session
  (no `ReShadeVR.ini` yet) the desktop advice says that the headset will keep its own copy later.
- ReShade advisor: re-analysed when the headset becomes active (the first-VR-session note no longer depends on XR being
  up when CRIX loads); with the desktop runtime switched off (`Disable=1`) and no `ReShadeVR.ini` yet, the headset is
  still covered from `ReShade.ini`; when a start-up preset loads at launch but another preset was selected last, both
  are reported and the last selected one is offered a light copy; the SteamVR route mentions the start-up preset.
- The "CRIX status" summary and the system report include the extra-camera limit, and the "nothing is being changed"
  warning is not shown while it limits cameras.
- **Camera inventory.** Lists every active camera (VR, desktop window, render textures) with resolution and a
  derived pixel load relative to one VR eye (system report, dashboard), and points out a large desktop camera.
- **URP asset report.** The game's URP asset values at start-up (render scale, MSAA, shadows, HDR, opaque/depth
  texture copies) go into the log and system report; the log notes when the URP asset has the opaque/depth copies on
  (some mod menus turn them on; extra GPU work on every camera). Read-only.
- System report: "What CRIX is doing", render-scale lever and check, URP values, Sakuraa, cameras, desktop camera,
  headset refresh rate, streaming state, base vs running profile.
- `tools/check_unity_api.py`: checks every Unity member the DLL uses against Unity's published C# source for a given
  version (every reference is declared in 6000.1.9f1 and 6000.2.9f1; obsolete-but-present members only as fallbacks).

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
- Setup advice names the VR app the game actually runs on (the loaded OpenXR runtime, e.g. Oculus = Meta Quest Link)
  instead of Windows' active OpenXR runtime, and says when the two differ. The eye-resolution line no longer says
  CRIX caps it when the active profile has no cap (the streaming profiles), and says how to prefer frame rate there.
- `DesktopCamera`: the "never a VR camera" check uses `Camera.stereoEnabled` (whether the camera actually renders to
  the headset) instead of `stereoTargetEye`, which the game's desktop camera may leave at its default.

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
- **Hitch log.** The first 15 frames over 70 ms are logged with what was going on (garbage collection, map load, a
  CRIX change or CRIX's own work); later ones are summarised once a minute, and all are counted by cause in the
  system report, so "lag spikes" can be traced.
- **Eye-resolution cap** (`MaxEyeMegapixels`, megapixels per eye; Balanced and DiscordCall 8, VirtualDesktop 7,
  VirtualDesktop.Quality 10, HighRefresh 6, MaximumPerformance and VirtualDesktop.Performance 5; off in OBSStreaming,
  VirtualDesktop.Streaming, ReShadeCompatible, MultiMod). When the VR runtime asks for far more pixels per eye than
  that (seen in a user report: about 37 MP per eye, 8x a Quest 3 default), CRIX lowers the game's render scale to fit
  (not below 0.35) and logs it. Under URP this also lowers the desktop window's resolution, hence off in the
  streaming profiles. Measured once when the headset starts, while CRIX is not changing the scale.
- **Setup advice** (log and system report): headset below 70 Hz (or below 110 Hz with HighRefresh), runtime eye size
  above 12 MP, NVIDIA overlay/Instant Replay hooked in while streaming to a headset, other optimiser mods installed.
  Each line names the setting in the VR app in use (Virtual Desktop: VR Frame Rate, SSW, VR Graphics Quality; Meta
  Quest Link app / Oculus Debug Tool; SteamVR). In the field report this cap raised the average from 41 to 57 FPS
  at the same 60 Hz (1 % low 11.5 -> 24.4) - one user's measurement, not a benchmark.
- **Respecting other mods:** only an explicit profile switch (or `ReapplyProfileNow`) takes back a setting the game or
  another mod changed; Ctrl+F5, editing profile values and F10 resume leave it alone.
- Preset revision 3 upgrades files written by 1.1.0 preview builds (HighRefresh, OBSStreaming and
  VirtualDesktop.Streaming no longer let adaptive mode change resolution) and keeps edited values.
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
  `FullReplacement`, because it bypasses any rule the game adds later. (Corrected in 1.1.0: in the current game only
  the layer check is a `List.Contains`; the component check is a loop and stays as it is, so the saving is small. The
  test stand-in now has the game's shape.)
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

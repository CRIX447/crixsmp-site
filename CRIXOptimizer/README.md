# CRIX OPTIMIZER

**An unofficial, experimental BepInEx performance mod for Gorilla Tag on PC VR (Steam / Meta PC).**
It is derived from [Potassium 1.0.0](https://github.com/niko-tar-gz/Potassium) (GPL-3.0) and extends it with
engine-level graphics profiles, an adaptive quality engine, automatic streaming/recording mode, frame-time
diagnostics, benchmarking, safe settings management and compatibility tooling for Virtual Desktop, OBS, Streamlabs,
Discord, Spotify, ReShade, Sakuraa Client and multi-mod setups.

> **Not affiliated with or endorsed by Another Axiom (Gorilla Tag's developer).** Unofficial mods can break after
> any game update, and using mods may be restricted by the game's current rules, especially in public lobbies.
> Test in a private or modded room first, and keep a way to go back to an unmodded game (see INSTALLATION.md).

## Project status: read this first

| | Status |
|---|---|
| Builds | **Yes.** `CRIXOptimizer.dll` compiles with 0 warnings/0 errors against BepInEx 5.4.21, HarmonyX 2.9.0 and Unity 2021.3 engine modules (see [docs/BUILD_REPORT.md](docs/BUILD_REPORT.md)). |
| Automated tests | **87/87 pass** on Mono 6.8 with the real HarmonyX 2.9.0 and real BepInEx 5.4.21 *and* 5.4.23.5 code, including every game patch applied to a stand-in game assembly ([docs/TEST_REPORT.md](docs/TEST_REPORT.md)). |
| Tested inside Gorilla Tag | **No.** The development environment had no Gorilla Tag install, GPU or headset. Loading in the real game, VR-runtime behaviour and every performance effect are **untested**. |
| Benchmarks | **None yet.** No performance numbers are claimed. The mod ships a benchmark recorder and a reproducible protocol ([docs/BENCHMARKING.md](docs/BENCHMARKING.md)) so results can be measured honestly on real hardware. |

## New in 1.1.0

- **It now changes things on URP.** Gorilla Tag PC runs Unity 6 (6000.1) with the Universal Render Pipeline, which
  ignored most 1.0.0 graphics settings. Render scale, MSAA, shadow distance/cascades and additional lights now go
  through the URP asset (details in [CHANGELOG.md](CHANGELOG.md)).
- **HighRefresh profile for 120/144 Hz headsets**: keeps resolution and cuts per-frame CPU work, which is what limits
  frame rate at 120 Hz. Optional: switch off the game's second (desktop) camera while you are not streaming.
- **Adaptive quality is on by default** and also reduces CPU load in busy lobbies (distant cosmetics, cosmetic update
  rates). It undoes resolution drops that do not help.
- **Stronger presets.** Existing profile files are upgraded, and any value you edited yourself is kept.
- **Auto streaming mode:** OBS (read-only WebSocket), Streamlabs and Discord switch to a streaming profile
  automatically and back afterwards. Setup: [INSTALLATION.md](INSTALLATION.md#auto-streaming-mode-obs-streamlabs-discord).
- **Spotify, Sakuraa Client/GTFX and extra-camera awareness** (detection and advice only; other programs and mods are never changed).
- The log, dashboard and system report state exactly what CRIX is changing, or why it is changing nothing.

VR frame rates are capped at the headset refresh rate, so if the game already holds it the FPS number cannot rise;
gains show as fewer drops and steadier frame times. Measure with the benchmark recorder (F6).

The request this project came from asked for "100× better" than Potassium. That is not a measurable goal and no such
claim is made. What CRIX does provide is listed below, with what each part can and cannot do.

## What it does

| Module | What it actually changes | Default |
|---|---|---|
| **Game stutter fixes** (from Potassium) | Spreads room-join scoreboard creation and scoreboard refreshes over frames; creates the remote-rig pool over several frames; defers Shared Blocks start-up until you enter that zone; skips redundant entity scans in inactive zones; handles the forced GC on room leave (keep / skip / incremental); skips forced unused-asset sweeps; speeds up the custom-map sanitizer. | on |
| **Visual update throttles** (from Potassium, reworked) | Voice-reactive cosmetics, cosmetic proximity checks and texture transitions update at a set rate (30/s by default). CRIX staggers instances across frames, so the remaining work is spread evenly instead of arriving in spikes. | on |
| **Distant cosmetic culling** (from Potassium) | Tightens the game's own remote-player cosmetic culling distances/counts. Local rendering only. | on |
| **Graphics settings** (new) | Per-profile engine settings through documented Unity APIs: VR render scale, shadow quality/distance/resolution/cascades, LOD bias, pixel lights, reflection probes, soft particles, particle raycast budget, skin weights, camera HDR, XR occlusion mesh, desktop mirror mode, MSAA cap (performance presets only) and texture mip limit (off by default). Under URP, render scale, MSAA, shadow distance/cascades and additional lights are set on the URP asset (its render scale also applies to the desktop window's camera). Most values are **limits**, never more expensive than the game's own value. Gorilla Tag bakes most of its lighting, so the shadow and light limits usually change little; render scale and the CPU-side settings matter most. | on |
| **Frame pacing** (new) | Async upload time slice, background loading priority, incremental GC time slice: fewer hitches while maps and cosmetics load. | on (per profile) |
| **Adaptive quality** (new) | Holds a target frame rate (headset refresh rate by default) by stepping distant cosmetic detail, cosmetic update rates, shadow distance, LOD bias, lights and render scale down, then back up, with hysteresis, cooldown and back-off. Tells GPU-bound from CPU-bound using runtime-reported GPU time, or by checking whether a resolution drop actually helped. Does nothing while the target is held. | on |
| **Auto streaming mode** (1.1) | Switches to OBSStreaming / DiscordCall while you record or stream and back afterwards. OBS state is read through OBS's WebSocket (read-only, localhost), Streamlabs through its local API (read-only); Discord by process. | on |
| **Diagnostics** (new) | FPS, average FPS, 1% low, frame-time σ/p99/max, frames over budget, GC rate, GC-coincident hitches, managed memory and growth trend, Unity graphics-memory estimate, XR runtime GPU/compositor time and dropped frames when the runtime reports them, CRIX's own overhead. | on (no UI) |
| **Dashboard** (new) | Desktop-window overlay (not in the headset) showing the above, each value labelled measured / derived / runtime-reported / n/a. | **off** |
| **Benchmark recorder** (new) | Hotkey-triggered capture → CSV plus summary (avg, two 1% low definitions, 0.1% low, σ, over-budget %, hitches, GCs, process CPU %). | on demand |
| **Compatibility** (new) | Lists loaded BepInEx plugins; reports Harmony patch overlaps with other mods; detects settings changed by other mods and stops managing them; steps aside if Potassium is also installed; each patch fails on its own without stopping the others. | on |
| **Environment detection** (new) | Read-only detection of Virtual Desktop, OBS/Streamlabs, Discord, Spotify, SteamVR, Quest Link (process names) and ReShade / OBS game-capture / Discord / Steam / RTSS hooks (game folder and the game's own loaded DLLs). Sakuraa Client detection and a camera inventory (extra cameras such as spectator cams and their pixel load). Optional auto profile selection. | detection on, auto-select **off** |

CRIX **never** changes gameplay, physics, player movement, networking or multiplayer behaviour, never writes the
game's saved settings (PlayerPrefs), and never injects into, modifies, re-prioritises or terminates other programs
(Virtual Desktop, OBS, Streamlabs, Discord, Spotify, ReShade, SteamVR, Sakuraa, other mods). What it reads from them:
process names, which DLLs are loaded in the game, other mods' plugin names and Harmony patch lists, the cameras in
the scene, and (read-only, on 127.0.0.1) OBS's and Streamlabs' live/recording state.

## Profiles

`Balanced` (default) · `MaximumPerformance` · `VirtualDesktop` (variants Balanced / Performance / Quality / Streaming) ·
`OBSStreaming` · `DiscordCall` · `ReShadeCompatible` · `MultiMod` · `Custom`.

Every profile is fully editable in `BepInEx/config/com.crix.crixoptimizer.profiles.cfg`; `Custom` is in the main config.
Switching profile applies immediately, with no restart. The full table of built-in values is in
[docs/PROFILES.md](docs/PROFILES.md), generated from the source. No profile is claimed to be optimal for every PC.

## Quick start

1. Install BepInEx 5 (x64, 5.4.21 or newer 5.4.x) for Gorilla Tag, then copy `CRIXOptimizer.dll` to
   `Gorilla Tag/BepInEx/plugins/CRIXOptimizer/`. Full steps: [INSTALLATION.md](INSTALLATION.md).
2. Start the game once; the config files are created in `Gorilla Tag/BepInEx/config/`.
3. Pick a profile (`[General] Profile`) or press **F9** in game to cycle profiles.

| Key (default) | Action |
|---|---|
| F8 | Show/hide the dashboard (desktop window) |
| F9 | Next profile |
| **F10** | **Restore everything**: every setting CRIX changed goes back, all patches pass through. Press again to resume. |
| F7 | Freeze/unfreeze the adaptive quality level |
| F6 | Start/cancel a benchmark capture |
| Ctrl+F5 | Reload config files from disk |

## How it stays safe

- **Settings ledger.** Each engine setting's original value is captured the first time CRIX changes it. If the game
  or another mod changes it afterwards, CRIX notices within ~2 s, logs it, and stops managing that setting (policy
  configurable). On restore, CRIX only puts a value back if it is still CRIX's own, so it never overwrites someone
  else's change. Anything that could not be restored is logged.
- **Runtime-only.** All changes are in memory. Removing the DLL (or setting `Enabled = false`) returns the game to
  its normal behaviour; nothing persists in the game's own settings.
- **No game-assembly linking.** Game types are found by name at start-up. If an update renames something, only the
  affected feature is disabled (reported in the log, dashboard and system report). The plugin keeps loading.
- **Fault isolation.** A patch that throws switches itself off and the game's original code runs. A module that
  fails three times is disabled for the session. A failed Harmony patch is cleaned up, so it cannot break other mods'
  patches on the same method.
- **Minimal overhead.** Per-frame work is O(1) and allocation-free. Everything else runs on timers (4/2/1 Hz).
  Process scans run on a worker thread. The dashboard component only exists while visible. CRIX measures its own
  per-frame cost and shows it on the dashboard.

## Limitations (honest list)

- **Untested in the real game** (see status). Game-specific patches target the same members Potassium 1.0.0 uses;
  if the current game build differs, those patches report "unavailable" instead of running.
- **Virtual Desktop is not controlled.** Bitrate, codec, encoder, network, sharpening and streaming resolution live
  in Virtual Desktop and are not reachable from a game plugin. CRIX reduces game-side load and reports what the VR
  runtime exposes. See [docs/VIRTUAL_DESKTOP_GUIDE.md](docs/VIRTUAL_DESKTOP_GUIDE.md).
- **Not measurable in-process, never shown as numbers:** GPU utilisation, total VRAM usage, Virtual Desktop
  latency/bitrate, network latency, OBS encoder load. GPU frame time and compositor time appear only if the VR
  runtime reports them (provider dependent).
- **MSAA changes** may need a game restart to reach the VR eye textures on some runtimes. The dashboard shows the
  MSAA actually in use.
- **Desktop mirror mode** uses Unity's XR mirror API. Some runtimes or game cameras may ignore it.
- **Built-in-pipeline settings** (shadow quality/resolution, pixel lights, soft particles) are skipped automatically
  if the game uses URP/HDRP; the URP asset's own properties are used where it has public setters (soft shadows have
  none, so they are not changed). URP ignores render scales within 0.05 of 1.0, so very small render-scale changes do
  nothing (the system report shows the check). Under URP the render scale also sets the resolution of the desktop
  window's camera, which is what OBS records; the streaming profiles therefore never let adaptive mode lower it.
- **Compiled against Unity 2021.3 reference assemblies; the game runs Unity 6000.1.9f1.** Every Unity member CRIX uses
  was checked against Unity's published 6000.1.9f1 source (`tools/check_unity_api.py`); three obsolete-but-present
  fallbacks are used only when the newer API is missing. Not yet run in the real game.
- **GTFX (Sakuraa Client's shaders) cannot be optimised by CRIX.** It is closed, injected by Sakuraa's launcher, and
  its settings belong to Sakuraa. Use GTFX's lighter presets in Sakuraa while streaming. CRIX reduces the game's own
  load around it and reports extra cameras (e.g. a spectator camera) and their pixel load.
- **Discord's call/Go Live state is not readable** without injecting into Discord, so "Discord running" is the signal.
- **The OBS WebSocket password** is stored in plain text in the BepInEx config (it is never logged).
- **The dashboard is drawn into the game window**, so OBS window/game capture will record it. Keep it off while live.
- **Hotkeys** need keyboard focus on the game window.
- Throttled visual updates are cosmetic, but they do run less often (same trade-off as Potassium).

## Documentation

[INSTALLATION.md](INSTALLATION.md) · [BUILDING.md](BUILDING.md) · [TROUBLESHOOTING.md](TROUBLESHOOTING.md) ·
[CHANGELOG.md](CHANGELOG.md) · [LICENSES.md](LICENSES.md) · [docs/PROFILES.md](docs/PROFILES.md) ·
[docs/VIRTUAL_DESKTOP_GUIDE.md](docs/VIRTUAL_DESKTOP_GUIDE.md) · [docs/OBS_DISCORD_RESHADE_GUIDE.md](docs/OBS_DISCORD_RESHADE_GUIDE.md) ·
[docs/BENCHMARKING.md](docs/BENCHMARKING.md) · [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) ·
[docs/BUILD_REPORT.md](docs/BUILD_REPORT.md) · [docs/TEST_REPORT.md](docs/TEST_REPORT.md)

## License and credits

CRIX OPTIMIZER is free software under the **GNU GPL v3.0 or later** ([LICENSE](LICENSE)). It contains code derived from
**Potassium 1.0.0** by the Potassium contributors (niko / kanyewest300), also GPL-3.0. Every file derived from Potassium
says so in its header comment, and [CHANGELOG.md](CHANGELOG.md) lists the modifications. BepInEx and HarmonyX (MIT) are
used as compile-time references only and are not bundled. No Gorilla Tag files are included. Details: [LICENSES.md](LICENSES.md).

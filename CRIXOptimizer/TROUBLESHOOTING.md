# Troubleshooting

First look at `Gorilla Tag/BepInEx/LogOutput.log` (search for `CRIX`) and `BepInEx/CRIXOptimizer/system-report.txt`.
Both list which features are active, unavailable, deferred or faulted, and why.

## Quick escape hatches

| Problem | Do this |
|---|---|
| Anything looks or feels wrong | Press **F10**: all settings are restored and all patches pass through. Press again to resume. |
| Need CRIX off without uninstalling | `[General] Enabled = false` in `BepInEx/config/com.crix.crixoptimizer.cfg`. |
| Suspect one feature | Turn off its module in `[Modules]`, or set the specific value to `-1`/`Unchanged` in your profile. |
| Config got messy | `[Actions] ResetConfigToDefaults = true`, or delete both `com.crix.crixoptimizer*.cfg` files. |

## Plugin does not load

- **No `CRIX OPTIMIZER ... loaded` line in the log.** Check that the DLL is at
  `BepInEx/plugins/CRIXOptimizer/CRIXOptimizer.dll` and that BepInEx itself works (the log exists and lists other plugins).
- **`Skipping ... because of process filters`.** CRIX only loads in `Gorilla Tag.exe`. A renamed executable is not supported.
- **BepInEx 6 / IL2CPP.** Not supported; use BepInEx 5.4.21+ (x64).
- **`failed to start and is inactive`.** Startup threw. CRIX shut itself off and changed nothing. Please report the
  logged exception together with `system-report.txt`, your game version and your BepInEx version.

## "unavailable" features after a game update

Game-specific patches look up Gorilla Tag types and methods by name. If an update renamed or removed something, that
one feature is reported as `Unavailable` with the missing member, and everything else keeps working. This is
expected behaviour for unofficial mods; wait for an update of CRIX.

## Potassium is also installed

The log says `Potassium is also installed... not applied`. That is deliberate: both mods would otherwise patch the
same game methods. Remove one of the two (recommended), or set `[Compatibility] WhenPotassiumInstalled = ApplyBoth`
if you really want both (not recommended).

## A setting "was changed by the game or another plugin"

CRIX noticed that something else changed a value it manages (another mod, the game's own graphics options, or a
quality-level change). By default CRIX respects that and stops managing the setting. To force CRIX's value:
`[Actions] ReapplyProfileNow = true`, or switch to another profile and back. Reloading the config (Ctrl+F5), editing
profile values and resuming after F10 keep respecting the other change. `[Compatibility] ExternalChangePolicy = Reapply` makes this
permanent, but it can make two mods fight over a value. Use it only if you know nothing else manages that setting.

## Visual problems

| Symptom | Likely cause / fix |
|---|---|
| Image blurry | Profile render scale < 1 (MaximumPerformance 0.8, VirtualDesktop.Performance 0.85) or adaptive mode lowered it. Use another profile, set `RenderScale = 0`, or raise `[Adaptive] MinimumRenderScale`. |
| Shadows missing/short | `ShadowDistanceLimit` / `ShadowQuality` in your profile, or adaptive level < 0. |
| Desktop window black or different view | `DesktopMirror` set to `None`/another mode. Set it to `Unchanged`. |
| Colours/brightness changed | `CameraHdr = Off` (MaximumPerformance). Set it to `Unchanged`. |
| ReShade depth effects broken or flickering | Use the `ReShadeCompatible` profile, and keep `[Compatibility] ReShadeGuard = Auto` (or `On`). Avoid adaptive render-scale changes. |
| MSAA setting has no effect | `MsaaSamples` is a cap: CRIX never raises MSAA, so it changes nothing if the game already uses less (Gorilla Tag's URP asset uses 1). With the built-in pipeline some runtimes only apply MSAA when the XR session starts (restart the game). The dashboard shows the eye-texture MSAA actually in use. |
| Cosmetic effects update less smoothly | Visual throttles (voice-reactive, proximity, texture transitions). Raise the rates or set them to `0` (unthrottled). |
| Distant players' cosmetics disappear | Distant cosmetic culling. Raise `CosmeticMaximumDistance`/`CosmeticMaximumCount` or set `ReduceDistantCosmetics = false`. |

## "It does nothing" / FPS did not go up

1. Find the `CRIX status:` line in `BepInEx/LogOutput.log` (about 5 s after start). It lists every engine setting CRIX
   changed (old -> new) and how many game patches are active. If CRIX changes nothing, a `CRIX is loaded but nothing
   is being changed right now: <reason>` warning follows it. The same summary is at the top of `system-report.txt`.
2. **VR caps the frame rate** at your headset's refresh rate. If the game already holds 90 FPS at 90 Hz, the number
   cannot go higher. Look at frame times instead: `Frame ... p99` and `over budget %` on the dashboard (F8), or run a
   benchmark (F6) with and without CRIX.
3. **Updated from 1.0.0?** 1.1.0 upgrades preset values you never edited and turns adaptive quality on
   (`[Adaptive] Mode = On`). The log says `Upgraded N profile value(s)...`. Values you edited are kept, so an old
   edited profile may still be mild; `[Actions] ResetConfigToDefaults = true` returns everything to the 1.1.0 defaults.
4. In `system-report.txt`, check `Render pipeline` and `Render scale lever`. Under URP the lever must be
   `URP asset renderScale`. If the check says `eye textures report X, expected Y`, something else (the game, the VR
   runtime, or another mod such as a client with its own graphics menu) is setting the render scale.
5. **Busy lobbies are usually CPU-bound.** Lowering resolution does not help there; adaptive mode detects this and
   reduces distant cosmetic detail and cosmetic update rates instead (`CPU-bound` on the dashboard).
6. Other mods can cost more than CRIX can save. In particular, clients with extra cameras (spectator/shoulder
   cameras, minimaps, tablets) render the scene again. The `Cameras:` line in `system-report.txt` lists them with
   their pixel load relative to one VR eye.

## Lag spikes / stuck below 60 FPS

0. **Read the `Setup:` lines** in `system-report.txt` (also in the log). They point out a headset set to 60 Hz, a VR
   app asking for a huge eye resolution, NVIDIA Instant Replay competing with the headset stream for the GPU's video
   encoder, and other optimiser mods. In one report a VR app asked for about 37 megapixels per eye (8x a Quest 3 at
   default settings): no mod can make an RTX 4060 render that at 72-90 FPS. Lower the resolution / quality preset in
   the VR app; CRIX's `MaxEyeMegapixels` cap limits the damage until then.

1. **Check the rate the game is given.** `system-report.txt` shows `Headset refresh rate`. If it says 60 Hz, the game
   cannot exceed 60 FPS whatever any mod does. With Virtual Desktop, open *Settings > Streaming* in the headset and
   set *VR Frame Rate* to 90 (or 72/80); if *SSW* is *Always enabled*, the game renders at half the frame rate (60 at
   120 Hz), so set SSW to *Automatic* (or *Disabled* while testing). With Quest Link: Meta Quest Link app > Devices >
   Graphics Preferences > Refresh rate; Oculus Debug Tool > Asynchronous Spacewarp. SteamVR: Settings > Video
   (refresh rate, Motion Smoothing).
2. **Half-rate lock.** At 120 Hz, a game that misses frames is locked to 60 FPS by reprojection (Virtual Desktop SSW,
   Meta ASW, SteamVR motion smoothing); at 90 Hz to 45. Set the headset to **90 Hz** (or 72/80) first and only go
   to 120 once 90 is steady.
3. **Is CRIX involved?** Press **F10** in game (CRIX restores everything and steps aside) and play a minute. If the
   spikes are the same, they do not come from CRIX. Press F10 again to resume.
4. **The hitch log.** CRIX logs the first 15 frames over 70 ms in `BepInEx/LogOutput.log` (`Hitch: one frame took
   ... ms (reason)`), then a summary once a minute, and counts them all in `system-report.txt` (`Big hitches`). The reason says whether a garbage collection
   ran, a map was loading, or CRIX had just changed a setting.
5. **Resolution changes are hitches.** Each render-scale change reallocates the VR eye textures. Adaptive mode lowers
   resolution at most once a minute, undoes it at once when it does not help, and does not change it at all in the
   HighRefresh and streaming profiles. `AdaptiveMayChangeRenderScale = false` in your profile turns it off anywhere.
6. **Other mods.** Shader packs and client mods (e.g. Sakuraa's GTFX effects and its extra cameras) can cost more
   than CRIX saves; test once without them.
7. **Virtual Desktop vs Link cable.** Virtual Desktop streams over Wi-Fi; a USB cable plugged into the headset only
   charges it. Quest Link (cable) is a different runtime with its own settings. Do not run both at once. Check the
   Virtual Desktop performance overlay: if *Game FPS* is fine but the image stutters, the cause is the encoder or
   Wi-Fi, not the game.

## Not reaching 120 FPS

- Is the headset really at 120 Hz? The dashboard and `system-report.txt` (`Headset refresh rate`) show the rate and
  where it came from (`XR display subsystem` = the VR runtime; `monitor` = the runtime reported none); the frame rate
  cannot exceed the headset's rate. See INSTALLATION.md, "Aiming for 120 FPS".
- Use the `HighRefresh` profile and keep adaptive quality on.
- `CPU-bound` on the dashboard means the processor, not the graphics card, is the limit. Lowering resolution will not
  help; fewer players/cosmetics in view, `DesktopCamera = OffWhenNotStreaming` and the PC-side checks (RAM speed in
  particular) are what can.
- `Desktop camera: unavailable` in the report means this game build does not expose the camera CRIX looks for; the
  rest of the profile still works. `left on: another mod ... uses it` means Sakuraa (or a similar mod) uses it as a
  spectator camera; see `[Compatibility] DesktopCameraWhenModsUseIt`.
- Desktop window black or showing one eye: that is the desktop camera switched off by HighRefresh while you are not
  streaming. Set `DesktopCamera = Unchanged` in the `[HighRefresh]` section if you want the third-person view.

## FPS drops slowly while streaming or recording

1. **Look at the trend.** The system report (`[Actions] WriteSystemReportNow = true`, or written 20 s after start) has a
   *Session trend* section: one line per minute with FPS, 1 % low, hitches, garbage collections, memory, profile and
   whether OBS/Streamlabs/Discord was capturing. When the frame rate falls by 10 % or more over the session, the log
   and the report say from what to what, and what changed along with it (streaming started, memory grew, hitches or
   garbage collections increased, adaptive mode already stepped down). Send that report when asking for help.
2. **Eye resolution.** If the report's *Setup* lines say the VR runtime asks for far more than about 5 megapixels per
   eye, lower the resolution in the VR app the game actually runs on (the line names it: Meta Quest Link app >
   Devices > Graphics Preferences > Render resolution, and Pixel Density 0 in the Oculus Debug Tool; or Virtual
   Desktop's VR Graphics Quality). `OBSStreaming` and `VirtualDesktop.Streaming` do not cap the eye resolution (the
   other profiles do, at 5-10 megapixels), because under URP a lower render scale also lowers the resolution of the
   desktop window that OBS records. To prefer frame rate over a sharp stream, set `MaxEyeMegapixels = 8` in that
   profile's section of `BepInEx/config/com.crix.crixoptimizer.profiles.cfg`.
3. **Memory that only grows.** Potassium (and CRIX's profiles) skip the game's timed unused-asset unloads, which
   cause hitches; other players' cosmetics and visited maps then stay in memory. If the trend shows Unity memory
   growing, set `[Memory] DeferredAssetUnload = true`: CRIX frees them once memory has grown by
   `DeferredAssetUnloadGrowthMB`, only while you are not in a room.
4. **The video encoder.** OBS, the headset stream (Quest Link / Virtual Desktop) and NVIDIA Instant Replay can all
   use the GPU's encoder. Turn Instant Replay off, and check OBS's *View > Stats* for rendering or encoding lag.
5. **Heat.** A frame rate that falls after 20-40 minutes with nothing else changing is often the GPU or CPU getting
   hot; check temperatures with your GPU's tool.

## Big lobbies (up to 20 players)

No mod can guarantee no frame drops in a full lobby: with many players the CPU (and on AMD Ryzen the memory speed,
see *Aiming for 120 FPS* in INSTALLATION.md) is usually the limit. What helps:

- CRIX's adaptive CPU savings: fewer players at full cosmetic detail and slower cosmetic updates while frames run
  late. **They only work when Potassium is not installed** (with Potassium, CRIX leaves those patches to it and
  Potassium uses the fixed values in its own `BepInEx/config/zone.xenon.potassium.cfg`). Either remove Potassium.dll
  (CRIX contains its fixes) or lower Potassium's *Maximum detailed player count* and cosmetic distances there.
- The `HighRefresh` profile (fewer detailed cosmetics, 20/s cosmetic updates) when you are not streaming.
- Lower eye resolution (see above): a GPU at its limit makes every CPU spike visible.

## Auto streaming mode

| Symptom | Cause / fix |
|---|---|
| Dashboard says `OBS WebSocket not connected` | OBS's WebSocket server is off. OBS: Tools → WebSocket Server Settings → Enable WebSocket server (port 4455). |
| `OBS WebSocket requires a password` / `rejected the password` | Copy the password from OBS (Show Connect Info) into `[Streaming] ObsWebSocketPassword`, then Ctrl+F5. After a password problem CRIX retries about once a minute. |
| Profile is always `DiscordCall` | Discord is running. Discord's call state cannot be read without injecting into Discord. Set `[Streaming] DiscordProfile = Balanced` or `Trigger = WhenRecordingOrLive` (Discord's overlay DLL in the game still counts then). |
| Profile switches while OBS is open but idle | Without the WebSocket, CRIX treats "OBS running" as streaming (OBS's game-capture DLL too). Enable the WebSocket; `Trigger = WhenRecordingOrLive` alone still reacts to the capture DLL. |
| Do not want any automatic switching | `[Streaming] AutoStreamingMode = false`. |

## Sakuraa Client / GTFX

- CRIX detects Sakuraa and lists it in the log and system report. It never changes Sakuraa or GTFX.
- GTFX effects (clouds, rain, grass and similar) are only adjustable in Sakuraa's own GTFX presets. Pick a lighter one while streaming.
- Sakuraa's minimap and display cameras (`MiniMapCam`, `DisplayCam`) render the scene again every frame. Set
  `[Performance] ExtraCameraMaxFps = 30` to update them at most 30 times a second (opt-in; see
  [docs/OBS_DISCORD_RESHADE_GUIDE.md](docs/OBS_DISCORD_RESHADE_GUIDE.md#extra-camera-limit-opt-in-11)). If a view you
  need turns choppy, add its camera name to `[Performance] ExtraCameraExclude`.
- Sakuraa's spectator camera renders the game a second time to the desktop window. A smaller game window, or a lower
  quality setting for that camera in Sakuraa, saves GPU time.
- **Sakuraa's spectator camera at 60 FPS (experimental, opt-in):** it is the game's desktop camera and normally renders
  the whole scene again on every headset frame (120 times a second at 120 Hz). Set
  `[Performance] SpectatorCameraMaxFps = 60` in `com.crix.crixoptimizer.cfg`: it then renders on every 2nd headset
  frame at 120 Hz, and the game window still gets a picture every frame, the camera's latest one, so it does not
  flicker (a 60 fps recording may repeat a frame now and then). The headset keeps its full rate. Details and limits:
  [docs/OBS_DISCORD_RESHADE_GUIDE.md](docs/OBS_DISCORD_RESHADE_GUIDE.md#spectator-camera-limit-experimental-opt-in).
  Not tested in Gorilla Tag yet: check the system report's *Spectator camera limit* line, and set it back to `0` if
  the window shows the headset view, a black image, or Sakuraa's HUD disappears.
- More GPU time for it: lower the eye resolution, limit Sakuraa's minimap/display cameras with
  `ExtraCameraMaxFps = 30`, pick a lighter GTFX preset, and if ReShade is only for viewers, keep it out of the
  headset (`Disable=1` under `[GENERAL]` in `ReShadeVR.ini`; the desktop window keeps its effects).
- Download Sakuraa only from its official source; CRIX does not check where a copy came from.

## ReShade costs too much FPS

- Turn on *Performance Mode* at the bottom of the *Home* tab of the ReShade overlay (the log says when it is off). For
  the headset's ReShade (`ReShadeVR.ini`) set `PerformanceMode=1` there while the game is closed.
- If ReShade has a start-up preset (`StartupPresetPath`), it loads that one at every launch: after switching to the light
  copy, make the copy the start-up preset too (overlay, Settings; for the headset, `StartupPresetPath` in `ReShadeVR.ini`).
- The log and system report list the usually expensive effects in your active preset (ambient occlusion, global
  illumination, reflections, depth of field, motion blur, ...). `[Actions] CreateLightReShadePreset = true` writes
  `<preset>.CRIX-light.ini` without them. Your own preset is not changed. For the desktop window, select the copy in
  the ReShade overlay (Home key). For the headset's ReShade, close the game and set the `PresetPath=` line under
  `[GENERAL]` in `ReShadeVR.ini` to the copy (the log prints the exact line), or pick it in the SteamVR dashboard's
  ReShade tab if you play through SteamVR.
- Depth effects broken: see *Visual problems* above.

## Spotify

Spotify's desktop app uses GPU hardware acceleration by default. If you notice stutter while it plays, turn off
hardware acceleration in Spotify's settings (restart Spotify afterwards). While Spotify runs, adaptive mode keeps 5 %
more GPU headroom (only when the VR runtime reports GPU time). CRIX never touches Spotify.

## Performance did not improve

- CRIX can only reduce **game-side** work. If the bottleneck is the Virtual Desktop/Link encoder, Wi-Fi, OBS
  encoding, or another program using the GPU, game-side changes will help little. See
  [docs/VIRTUAL_DESKTOP_GUIDE.md](docs/VIRTUAL_DESKTOP_GUIDE.md) and [docs/OBS_DISCORD_RESHADE_GUIDE.md](docs/OBS_DISCORD_RESHADE_GUIDE.md).
- If the dashboard shows `CPU-bound`, lowering resolution will not help; distant cosmetics, cosmetic update rates,
  shadows/LOD/lights and the stutter fixes are the relevant levers.
- Measure before and after with the benchmark recorder ([docs/BENCHMARKING.md](docs/BENCHMARKING.md)). Judging by
  feel alone is unreliable.

## Adaptive mode oscillates or reacts too much

Lower `[Adaptive] Sensitivity`, raise `CooldownSeconds` and `UpSustainSeconds`, or narrow `MinimumQualityLevel`.
**F7** freezes the current level. `ManualOverride = true` + `ManualLevel` pins a level.

## Hotkeys do nothing

The desktop game window needs keyboard focus (click it). If the log says `Keyboard input is not available`, use the
config file or an in-game config editor ([BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)).
Hotkeys can be changed or cleared in `[Hotkeys]`.

## Memory warnings

`Managed memory has grown X MB/min` means the managed heap kept growing for 5 minutes. A single warning after a map or
cosmetic load is normal. If it repeats every 10 minutes, disable other mods one at a time to find the cause. With
`SkipTimedAssetUnloads` on, `[Memory] DeferredAssetUnload = true` runs an occasional unused-asset unload while you are
not in a room.

## Sending performance notes for tuning

CRIX takes notes while you play: `BepInEx/CRIXOptimizer/notes/notes-<date>-<time>.txt`, one file per session. Every
10 seconds it notes the frame rate, 1 % low, worst frame and hitches, which map (zone) you are in, how many players are
in the room, whether OBS/Streamlabs/Discord is recording or streaming, the profile and memory. It also notes every
zone change, room join/leave, player joining/leaving, streaming start/stop and big hitch (with what CRIX thinks caused
it), and at the top it sums everything up per map, per lobby size and per streaming state. No room codes or player
names are written. The file is updated every minute, so it is useful even if the game crashes.

Lag spikes right after the game opens are normal while the game and mods load (Sakuraa Client loads its content
then). The notes keep that apart: the first minute counts as loading, plus 20 seconds after each big spike in it (at
most 5 minutes). Those spikes are marked `[loading]`, summed up under *Startup*, and left out of the per-map and
per-lobby results.

*Entering maps* shows the first 10 seconds after each map becomes active, for the first visit and for later visits. A
spike only on the first visit is the game using shaders or textures for the first time; a spike on every visit is the
game switching the map on (its objects, lightmaps), which a mod cannot change without changing the game. Spikes at a
time-of-day change are named as such (the game swaps the lightmaps of every loaded map then). For custom maps the
notes list what the map contains (lights, reflection probes, cameras into textures, particles, triangles), and the
timeline shows how many times per frame the scene is rendered.

A good test session (20-40 minutes):

1. Start the game normally (with the mods you always use) and wait in the stump for a minute or two (loading).
2. Walk forest -> canyon -> forest -> canyon (or any two maps you play), so each map is entered twice.
3. Visit the other maps you play, a few minutes each, including a busy lobby (10+ players), a water or lava map, and
   one custom map you use (load it, play a bit, leave).
4. Start OBS (or Streamlabs/Discord) recording or streaming for 5-10 minutes while playing.
5. Send the newest notes file. It is updated every minute, so you can send it with the game still open; until 20
   minutes of play after loading are in it, its first lines say there is not enough yet.

## Reporting a problem

Include: `BepInEx/LogOutput.log`, `BepInEx/CRIXOptimizer/system-report.txt` (`[Actions] WriteSystemReportNow = true`
refreshes it), your profile name, and what you did right before the problem. The report contains versions, hardware
model names and plugin names. It contains no personal data, but read it before you share it.

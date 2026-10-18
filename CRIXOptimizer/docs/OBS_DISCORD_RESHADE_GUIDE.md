# OBS, Streamlabs, Discord, Spotify, Sakuraa and ReShade guide

CRIX only changes Gorilla Tag. It **never** changes OBS, Streamlabs, Discord, Spotify, Sakuraa or ReShade settings,
never injects into them, never changes process priorities and never terminates anything. Its interaction with them
is read-only: whether their process runs, whether their DLL is loaded into the game process, ReShade's `ReShade.ini`,
`ReShadeVR.ini` and active presets (read, never written), and (1.1) OBS's and Streamlabs' live/recording state through their own
local APIs on 127.0.0.1. Two options act only when you turn them on: `[Performance] ExtraCameraMaxFps` (limits
cameras that render into textures, including other mods' cameras, see Sakuraa below) and
`[Actions] CreateLightReShadePreset` (writes a new preset file next to yours, see ReShade below).

## Auto streaming mode (1.1)

With `[Streaming] AutoStreamingMode = true` (default), CRIX switches to `OBSStreaming` while OBS/Streamlabs is live or
recording and to `DiscordCall` while Discord runs, then back to your profile 20 s after the signal ends. Your own
`[General] Profile` setting is not changed. It only switches away from `Balanced`, `OBSStreaming` and `DiscordCall`;
`VirtualDesktop` moves to its Streaming variant; `MaximumPerformance`, `HighRefresh`, `ReShadeCompatible`, `MultiMod`
and `Custom` are kept as you chose them. Setup of the OBS WebSocket (needed to know when OBS is actually live):
[INSTALLATION.md](../INSTALLATION.md#auto-streaming-mode-obs-streamlabs-discord).

| Signal | How CRIX reads it | What it can see |
|---|---|---|
| OBS Studio 28+ | obs-websocket v5 on 127.0.0.1:4455 (your password), requests `GetStreamStatus`/`GetRecordStatus`/`GetReplayBufferStatus`/`GetVirtualCamStatus` and listens to their state events | live / recording / replay buffer / virtual camera / idle |
| OBS without WebSocket | process `obs64`/`obs32` | running only |
| Streamlabs Desktop | its local JSON-RPC API on 127.0.0.1:28194, `StreamingService.getModel` every 2 s | live / recording / replay buffer / idle |
| Discord | process name, or Discord's overlay DLL in the game | running only (calls/Go Live are not readable without injection) |

---

## OBS Studio / Streamlabs (`OBSStreaming` profile)

**What CRIX does:** reduces game-side GPU and CPU work (shadow distance ≤ 30 m, ≤ 2 shadow cascades, ≤ 1
additional/pixel light, no realtime reflections, soft particles off, cosmetic visual updates at 24/s, distant
cosmetics culled a little sooner), smooths frame pacing during loads (1 ms upload slice, lower background-loading
priority) and applies Potassium's stutter fixes. When the VR runtime reports GPU time, adaptive mode keeps 20 % of
the GPU budget free for OBS. It does **not** change the desktop mirror, and adaptive mode may not lower the render
scale in this profile: under URP that scale also sets the resolution of the desktop camera OBS records, so the
captured image keeps its resolution. For the same reason this profile does not cap the eye resolution
(`MaxEyeMegapixels = 0`): if your VR app asks for a very high resolution (the system report's *Setup* line says so),
lower it in the VR app, or set `MaxEyeMegapixels = 8` in `[OBSStreaming]` to prefer frame rate over a sharp stream.
The system report's *Session trend* shows minute by minute whether the frame rate falls during a long stream. It warns if the
dashboard is visible while OBS's game-capture hook is loaded (the dashboard would appear on stream).

**What CRIX cannot do:** fix encoder overload, upload bandwidth, dropped network frames, capture-card problems or OBS
scene complexity.

### Reading OBS's own statistics (View → Stats)

| OBS counter | Meaning | Does CRIX help? |
|---|---|---|
| *Frames missed due to rendering lag* | OBS could not compose frames in time; often GPU contention with the game | **Possibly.** Less game GPU load leaves more for OBS. Try `OBSStreaming` or `MaximumPerformance`. |
| *Skipped frames due to encoding lag* | Encoder overloaded | No. Use a hardware encoder, a faster preset or a lower resolution/frame rate in OBS. |
| *Dropped frames (network)* | Upload cannot keep up | No. Lower the bitrate or check your connection. |

OBS-side options worth testing yourself: a hardware encoder (NVENC/AMF/QuickSync), a capture method that suits your
setup (Game Capture of the Gorilla Tag window, Window Capture, or an OpenVR capture plugin), and running OBS as
administrator (OBS documents that this gives its rendering higher GPU priority). CRIX makes none of these changes.

### Recommended routine

1. Profile `OBSStreaming` (or `VirtualDesktop` + preset `Streaming` when you also play over Virtual Desktop).
2. Dashboard **off** while live (F8). Alerts still go to the log.
3. Adaptive quality is on by default (`[Adaptive] Mode = On`, down to `MinimumQualityLevel = -6`). To keep visuals
   closer to the profile while live, raise the floor, e.g. `MinimumQualityLevel = -3`.
4. Record a benchmark (F6) with OBS idle and with OBS recording, and compare (see [BENCHMARKING.md](BENCHMARKING.md)).

---

## Discord (`DiscordCall` profile)

Works the same whether or not Discord is installed. CRIX does not touch calls, audio, the microphone or Discord itself.

**What CRIX does:** modest game-side reductions, so the GPU encoder used for video/screen share and the CPU used for
voice processing have more room: shadow distance ≤ 35 m, ≤ 2 shadow cascades, ≤ 2 pixel/additional lights, no
realtime reflections, soft particles off, cosmetic visual updates at 24/s, smoother loading, and 10 % GPU headroom
for adaptive mode when the VR runtime reports GPU time. If Discord's in-game overlay DLL is loaded into the game, CRIX
mentions it: overlays hook the game's graphics presentation and can add frame-time spikes.

Discord-side options worth testing (all in Discord's own settings; names vary by version): hardware acceleration,
the in-game overlay on/off, and screen-share resolution/frame rate.

---

## Spotify

Detected by process name. Spotify's desktop app uses GPU hardware acceleration by default. If you see stutter while
it plays, turn that off in Spotify's settings (restart Spotify). While Spotify runs, adaptive mode keeps 5% more GPU
headroom. Nothing in Spotify is read or changed.

---

## Sakuraa Client and GTFX

Sakuraa Client is a commercial Gorilla Tag client with camera/casting features. GTFX is its own closed shader/effects
system, injected by Sakuraa's launcher. CRIX:

- detects Sakuraa (assemblies, plugin GUIDs and Harmony patch owners whose name contains "Sakuraa") and lists it in
  the log and system report;
- lists every extra camera (spectator/shoulder camera, minimap, tablet, replay viewer) with its resolution and
  derived pixel load relative to one VR eye, and points out a large desktop camera while you stream;
- reduces the game's own load around Sakuraa as usual.

CRIX does **not** change Sakuraa or GTFX, and leaves Sakuraa's cameras alone unless you turn on the extra-camera
limit below. Sakuraa's spectator view uses the game's third-person camera; the `DesktopCamera` setting of the HighRefresh profile
therefore leaves that camera on when Sakuraa has taken it over, unless you allow it with
`[Compatibility] DesktopCameraWhenModsUseIt = true` (the spectator view is then shown only while you record or stream). To make
GTFX cheaper, choose a lighter GTFX preset inside Sakuraa while streaming. Sakuraa sets Unity's
`targetFrameRate`/`vSyncCount`; VR runtimes ignore those, and CRIX does not manage them.

### Extra-camera limit (opt-in, 1.1)

Every camera that renders into a texture (a minimap, a tablet or in-game screen, Sakuraa's `MiniMapCam` and
`DisplayCam`) draws the whole scene again, every frame, on top of the two VR eyes. Most of them do not need 120
updates a second. `[Performance] ExtraCameraMaxFps` in `com.crix.crixoptimizer.cfg` (default `0` = off) limits them:
with `30`, each one renders at most 30 times a second, and the limited cameras take turns so that only as many render
in one frame as the limit needs (for example one per frame for three cameras at 30/s and 90 FPS), also after a
hitch; between updates its screen keeps the last image. A game running slower than the limit renders them every frame.

- How: directly before Unity collects the cameras for a frame (after every mod's Update and LateUpdate), CRIX switches
  off the limited cameras that are not due, and switches exactly those back on as soon as Unity has its list, before
  any camera renders. Every script, including the camera's own mod, its menus and its rendering callbacks, sees the
  camera on or off as its mod set it; a camera its mod switched off stays off. (Only code that runs in the short gap
  between those two points, such as another mod's own step placed directly before Unity's rendering or a
  `beginContextRendering` handler registered before CRIX's, could see the camera switched off.)
- Never touched: VR cameras, the camera that draws the desktop window, and cameras whose name contains an entry of
  `[Performance] ExtraCameraExclude` (default `mirror,lck,photo,reflection,portal`: the game's mirror, its LCK/LIV
  camera, photo booth, reflection and portal cameras). A camera that starts drawing to the screen is no longer limited.
- Nothing is limited while CRIX is suspended (F10), disabled, or not running its update; `0` removes the hook.
- Needs a scriptable render pipeline (Gorilla Tag uses URP). If Unity's end-of-frame event is not seen, or another
  mod keeps replacing Unity's player loop, the limit stops by itself and says so in the log and system report.
- The limited cameras are listed in the log (debug level) and in the system report (`Extra camera limit`).
- If a mod shows a render-texture camera in the desktop window or a stream, that view is also limited; add its name
  to `ExtraCameraExclude` (the system report lists the names).

This changes other mods' cameras, so it is off until you set it. It only saves GPU time when such cameras exist;
the camera inventory in the system report shows them and their pixel load.

Download Sakuraa only from its official source; CRIX does not check where a copy came from.

---

## ReShade (`ReShadeCompatible` profile and the ReShade guard)

ReShade applies post-processing shaders on the graphics API level. Depth-based effects (ambient occlusion, depth of
field, ...) find the game's depth buffer by its size and format. That selection breaks when render targets are
recreated or change format.

**ReShade guard** (`[Compatibility] ReShadeGuard = Auto`, the default): when ReShade is detected (ReShade.ini, a proxy
DLL such as `dxgi.dll` whose version info says ReShade, or a loaded ReShade module), CRIX does not change at runtime:

- MSAA (recreates depth buffers),
- camera HDR (changes render-target formats),
- the desktop mirror (ReShade may be applied to that window),
- render scale through adaptive mode (recreates eye textures). A fixed render scale from the profile is still applied once.

Everything else (shadows, LOD, lights, reflection probes, frame pacing, stutter fixes, cosmetic throttles) keeps
working, so you can trade game-side effects for ReShade's cost. `ReShadeGuard = On` forces the guard without
detection; `Off` disables it (not recommended with depth effects).

The `ReShadeCompatible` profile applies the same constraints and also leaves soft particles and the occlusion mesh
alone.

**Not supported / not guaranteed:** every ReShade shader, every ReShade version, graphics APIs other than the game's
default Direct3D 11 (the system report shows the API in use), VR-specific ReShade builds and add-ons, and any
combination where another mod changes resolution or MSAA. If an effect breaks, set the profile to
`ReShadeCompatible`, restart the game (so render targets are created once with final values), and check that nothing
else changes resolution.

Turning the ReShade mode off is safe at any time: CRIX restores the values it changed. Your ReShade configuration is
never touched.

### Making ReShade cheaper (preset advisor, 1.1)

ReShade's cost is the effects in the active preset. In VR each effect that is applied to the headset view runs on
every eye at the eye resolution, which is much more work than on a flat monitor (CRIX's eye-resolution cap,
`MaxEyeMegapixels`, therefore also makes ReShade cheaper).

When ReShade is detected, CRIX reads `ReShade.ini` and `ReShadeVR.ini` in ReShade's folder (normally the game folder)
and the active presets' enabled effects (read-only, following ReShade's own rules) and logs advice, also shown in the
system report:

- **Performance Mode off:** for the desktop window, turn it on at the bottom of the *Home* tab of the ReShade overlay
  (Home key). The headset's ReShade is not controlled by that overlay (ReShade gives it no keyboard input; it has an
  overlay only in the SteamVR dashboard): close the game and set `PerformanceMode=1` under `[GENERAL]` in
  `ReShadeVR.ini`, or use the ReShade tab of the SteamVR dashboard if you play through SteamVR. Effects are then
  compiled with fixed settings, which is faster; turn it off again only to edit settings.
- **Headset and desktop window:** ReShade 6 keeps the headset's settings in `ReShadeVR.ini` (anything it does not set
  comes from `ReShade.ini`) and the desktop window's in `ReShade.ini`. CRIX reads both, resolves the active preset the
  way ReShade does (start-up preset first, then the last used preset, then `ReShadePreset.ini`) and reports each one;
  the headset's preset is the one that matters for VR frame rate. A runtime switched off with `[GENERAL] Disable=1` in
  its own file is reported as off. Before your first VR session with ReShade 6 there is no `ReShadeVR.ini` yet: the
  headset then uses `ReShade.ini`'s values, and the log reminds you that changes in the Home-key overlay reach only the
  desktop window once ReShade has saved its own `ReShadeVR.ini`.
- **Usually expensive effects** are listed by kind: global illumination (RTGI and similar), ambient occlusion (MXAO,
  SSAO, HBAO, ...), screen-space reflections, depth of field/bokeh, motion blur, large-radius local contrast
  (Clarity), god rays/volumetric light, and the DisplayDepth debug view (only for setup). This is a name-based
  guess, not a measurement; the real cost depends on each effect's settings. Colour, sharpening, SMAA and similar
  effects are cheap and not listed.
- **`[Actions] CreateLightReShadePreset = true`** writes a copy of each active preset that has such effects (headset
  and desktop), `<preset>.CRIX-light.ini` next to it, with the listed effects switched off and every effect setting
  kept. Your presets, `ReShade.ini` and `ReShadeVR.ini` are not changed. Desktop window: select the copy in the
  preset list at the top of the ReShade overlay (Home key); select your own preset to go back. Headset: close the game
  and change the `PresetPath=` line under `[GENERAL]` in `ReShadeVR.ini` to the copy (the log prints the exact line,
  with any comma in the name written twice, as ReShade does), or
  pick it in the SteamVR dashboard's ReShade tab when playing through SteamVR. If your preset is set as a start-up
  preset (`StartupPresetPath`), point that at the copy too or ReShade loads the old one at the next launch. Running it
  again overwrites only the `.CRIX-light.ini` copy. If the copy cannot be written (for example it is read-only), the
  log says why.

Measure before and after with the benchmark recorder (F6) in the same lobby type.

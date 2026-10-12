# OBS, Discord and ReShade guide

CRIX only changes Gorilla Tag. It **never** reads or changes OBS, Discord or ReShade settings, never injects into
them, never changes process priorities and never terminates anything. The only interaction is read-only detection:
whether their process runs, and whether their DLL is loaded into the game process.

---

## OBS Studio / Streamlabs (`OBSStreaming` profile)

**What CRIX does:** reduces game-side GPU work (shadow distance ≤ 40 m, ≤ 2 pixel lights, no realtime reflections),
smooths frame pacing during loads (1 ms upload slice, lower background-loading priority) and applies Potassium's
stutter fixes. It does **not** change the desktop mirror, so the image OBS captures stays the same. It warns if the
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
3. Optional: `[Adaptive] Enabled = true` with `[Adaptive] MinimumQualityLevel = -4` to absorb heavy scenes.
4. Record a benchmark (F6) with OBS idle and with OBS recording, and compare (see [BENCHMARKING.md](BENCHMARKING.md)).

---

## Discord (`DiscordCall` profile)

Works the same whether or not Discord is installed. CRIX does not touch calls, audio, the microphone or Discord itself.

**What CRIX does:** modest game-side reductions, so the GPU encoder used for video/screen share and the CPU used for
voice processing have more room: shadow distance ≤ 40 m, ≤ 2 pixel lights, no realtime reflections, soft particles off,
cosmetic visual updates at 24/s, smoother loading. If Discord's in-game overlay DLL is loaded into the game, CRIX
mentions it: overlays hook the game's graphics presentation and can add frame-time spikes.

Discord-side options worth testing (all in Discord's own settings; names vary by version): hardware acceleration,
the in-game overlay on/off, and screen-share resolution/frame rate.

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

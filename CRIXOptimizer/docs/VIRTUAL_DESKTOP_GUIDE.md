# Virtual Desktop guide

Wireless PC VR has two halves. **CRIX can only work on the first one.**

| | Who controls it | Examples |
|---|---|---|
| **Game rendering** on the PC | Gorilla Tag, which CRIX can adjust | render resolution scale, shadows, LOD, effects, frame pacing, stutter during loads/room joins |
| **Streaming** PC → headset | Virtual Desktop (streamer on PC + app on the headset), GPU driver, Wi-Fi | bitrate, codec, encoder, streaming resolution ("VR Graphics Quality"), frame rate, sharpening, network latency, decoding |

CRIX **does not and cannot** change Virtual Desktop's bitrate, codec, encoder, network transport or streaming
resolution. There is no API for a game plugin to do that, and CRIX does not inject into or reverse-engineer Virtual
Desktop. CRIX also cannot read Virtual Desktop's latency or bitrate statistics; it never shows invented numbers for them.

What CRIX does for Virtual Desktop users:

- **Leaves GPU headroom for the video encoder.** The encoder (NVENC/AMF/QuickSync) runs on the same GPU and shares
  memory bandwidth with the game. The `VirtualDesktop` profiles cap shadow distance, pixel lights and realtime
  reflections. With adaptive mode on and runtime GPU timing available, they target 10-15 % GPU headroom
  (`AdaptiveGpuHeadroomPercent`).
- **Steadier frame pacing.** A frame the game delivers late shows up as a stutter in the stream. Smaller async-upload
  slices, lower background-loading priority and Potassium's stutter fixes target the spikes during map/cosmetic loads
  and room joins.
- **Single-eye desktop mirror** (`DesktopMirror = LeftEye`). The desktop window is not needed while you are in the
  headset, and one eye is cheaper than two. Use `VirtualDesktop.Streaming` if OBS captures that window.
- **Occlusion mesh on.** Pixels outside the lens area are not shaded.
- **Diagnostics.** Detects whether Virtual Desktop's OpenXR runtime or SteamVR is in use (from the OpenXR runtime name
  and running processes), and shows the game's frame rate, frame-time stability and any GPU/compositor timing the VR
  runtime reports.

## Choosing a VirtualDesktop variant

Set `[General] Profile = VirtualDesktop` and `[General] VirtualDesktopPreset`:

| Preset | Intent | Notable values (all editable) |
|---|---|---|
| `Balanced` | Default for wireless play | shadow distance ≤ 40 m, ≤ 2 pixel lights, no realtime reflections, LeftEye mirror, 1 ms upload slice |
| `Performance` | Weaker GPU, or encoder/compositor struggling | render scale 0.85, hard shadows ≤ 25 m, LOD bias ≤ 0.75, 1 pixel light, 20/s cosmetic updates |
| `Quality` | Strong GPU; keep the game's visuals | only stutter fixes, occlusion mesh, LeftEye mirror and smoother loading |
| `Streaming` | Virtual Desktop **and** OBS at the same time | like Balanced but the desktop mirror is left alone so OBS captures what it expects |

Full values: [PROFILES.md](PROFILES.md). None of them is "the best" setting; test on your own hardware.

## Settings that live in Virtual Desktop

These are configured in the Virtual Desktop app (headset, *Settings → Streaming*) or the Virtual Desktop Streamer on
the PC. Names and availability vary between Virtual Desktop versions, headsets and GPUs; treat this as a checklist,
not a recipe. Change **one thing at a time** and compare.

| Setting | What it trades | Testing tips |
|---|---|---|
| **VR Graphics Quality** | Resolution the VR runtime asks the game to render. Multiplies with the game-side render scale. | Do not stack supersampling: if this is high, keep CRIX `RenderScale` at 0 (unchanged) or below 1. |
| **VR Frame Rate** | Headset refresh rate (72/80/90/120 Hz). Higher = smoother, but each frame gets less time. | CRIX's adaptive target follows the refresh rate the runtime reports. 72/80 Hz leaves the most headroom. |
| **VR Bitrate** / *Automatically adjust bitrate* | Image quality vs. network and encoder load | Too high for your Wi-Fi gives latency spikes and dropped frames that no game setting can fix. |
| **Video Codec** (H.264, H.264+, HEVC, HEVC 10-bit, AV1 when the GPU supports it) | Efficiency vs. encoder/decoder cost | AV1 needs a recent GPU (encode) and headset (decode). Compare latency in the performance overlay. |
| **Sliced encoding** | Encoder latency | Usually helps latency; check your version's notes. |
| **Synchronous Spacewarp (SSW)** | Fills in frames when the game misses the refresh rate | Hides drops but adds artefacts. For measuring, disable it or keep it fixed. |
| **Sharpening**, **Snapdragon Game Super Resolution** | Perceived sharpness vs. headset processing | Useful with a lower game render scale. |
| **Performance overlay** | Shows Virtual Desktop's own game FPS, latency breakdown (game / encode / network / decode) | The authoritative source for streaming numbers. Use it next to CRIX's dashboard. |
| **OpenXR runtime** (Virtual Desktop's own "VDXR" or SteamVR) | Which runtime the game talks to | Running without SteamVR's compositor is often lighter; test both. CRIX's system report shows which one is active. |

### Network checklist

- PC connected to the router by **Ethernet**; headset on **5 GHz or 6 GHz** Wi-Fi from that router, ideally a
  dedicated router/access point in the same room with line of sight.
- Avoid mesh repeaters between headset and router, crowded channels and other heavy traffic during play.
- Watch the network-latency part of the performance overlay. Spikes there are not a game-performance problem.

## A simple diagnosis loop

1. Turn on Virtual Desktop's performance overlay **and** CRIX's dashboard (F8, desktop window).
2. **Game frame rate below the target** (CRIX `FPS` / Virtual Desktop "Game FPS")? That is game-side load, which CRIX
   can help with: try `VirtualDesktop.Performance`, enable adaptive mode, and check the dashboard for `CPU-bound`
   (then resolution will not help; shadows/LOD/lights will).
3. **Game on target but the stream stutters, or latency/drops in the overlay?** That is encoding or the network: lower
   the bitrate, try another codec, improve Wi-Fi. CRIX changes will help little here.
4. Measure each change with the benchmark recorder (F6) plus Virtual Desktop's overlay; see [BENCHMARKING.md](BENCHMARKING.md).

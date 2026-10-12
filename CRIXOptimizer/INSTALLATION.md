# Installation

> **Unofficial mod.** Not endorsed by Another Axiom. It can stop working after any Gorilla Tag update, and the
> game's rules may restrict modded clients, particularly in public lobbies. Check the current rules before you play
> online with any mod, and test in a private/modded room first. CRIX does not change gameplay, movement, physics or
> networking, but a modified client is still a modified client.

## Requirements

| Item | Version |
|---|---|
| Gorilla Tag | PC VR build (Steam or Meta PC), Windows. Standalone Quest builds are **not** supported. |
| BepInEx | **5.4.21 or newer 5.4.x**, x64 (Mono), the standard loader for Gorilla Tag PC mods. BepInEx 6 / IL2CPP builds are not supported. |
| Other mods | None required. Utilla is **not** needed. |
| .NET | Nothing to install. The plugin targets .NET Standard 2.1, which the game's Unity runtime provides. |

## Install

1. **Back up / note your current setup.** If you already use mods, note which ones are installed.
2. **Install BepInEx 5** if you do not have it: download the x64 BepInEx 5.4.x release from the official BepInEx
   GitHub releases page, extract it into the Gorilla Tag folder (the folder that contains `Gorilla Tag.exe`), start
   the game once, then quit. A `BepInEx/plugins` folder should now exist.
   - Steam: right-click Gorilla Tag → *Manage* → *Browse local files*.
3. **Copy the plugin.** From the release ZIP, copy the folder `install/BepInEx/plugins/CRIXOptimizer/` into
   `Gorilla Tag/BepInEx/plugins/`, so you end up with
   `Gorilla Tag/BepInEx/plugins/CRIXOptimizer/CRIXOptimizer.dll`.
4. **Potassium.** CRIX already contains Potassium's optimisations. If Potassium is also installed, CRIX detects it and
   does not apply its own copies of those patches (no double patching). Keeping **only one** of the two is
   recommended; remove `Potassium.dll` if you want CRIX's versions.
5. **Start the game.** In `Gorilla Tag/BepInEx/LogOutput.log` you should see
   `CRIX OPTIMIZER 1.1.0 loaded. Profile: Balanced. Patches active: ...`, and about 5 s later a
   `CRIX status: ...` line that lists exactly what CRIX is changing (see "Check that it is working" below).

Nothing else is installed or modified: no game files, no registry, no other programs.

## Files CRIX creates

| File | Purpose |
|---|---|
| `BepInEx/config/com.crix.crixoptimizer.cfg` | Main settings: profile, modules, adaptive, dashboard, hotkeys, compatibility, `[Custom]` profile. |
| `BepInEx/config/com.crix.crixoptimizer.profiles.cfg` | Editable values of every built-in profile (one section each). |
| `BepInEx/CRIXOptimizer/system-report.txt` | Written ~20 s after start: versions, GPU, XR runtime, plugins, patch status, managed settings. |
| `BepInEx/CRIXOptimizer/benchmarks/*.csv`, `*.summary.txt` | Benchmark captures (only when you start one). |

Annotated examples of both config files with all defaults are in [docs/examples/](docs/examples/).

## Choose a profile

Set `[General] Profile` in `com.crix.crixoptimizer.cfg`, or press **F9** in game (desktop window focused) to cycle:

| Profile | Use it for |
|---|---|
| `Balanced` | General play. Potassium's stutter fixes, capped shadows/lights, VR occlusion mesh, smoother loading. Keeps the game's resolution. |
| `MaximumPerformance` | Weak GPU or very demanding setups. Lower render scale, shadows, LOD, effects. Visibly softer. |
| `VirtualDesktop` | Wireless PC VR via Virtual Desktop. Choose the variant with `[General] VirtualDesktopPreset`: `Balanced`, `Performance`, `Quality`, `Streaming`. |
| `OBSStreaming` | Recording/streaming. Leaves GPU/CPU time for OBS's capture and encoder; never changes what the desktop window (OBS's capture) shows. |
| `DiscordCall` | Voice/video calls and screen sharing alongside the game. |
| `ReShadeCompatible` | ReShade users. No resolution/MSAA/HDR/mirror changes that would disturb depth-based effects. |
| `MultiMod` | Many other mods installed. Only low-risk patches, no engine graphics changes. |
| `Custom` | Your own values, in the `[Custom]` section of the main config. |

Changes apply immediately; no restart is needed. With the [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
plugin installed you can also edit everything in game. Edits made directly to the `.cfg` files are picked up after
**Ctrl+F5** (reload) or a restart.

`[General] AutoSelectProfile = true` lets CRIX pick a profile from running apps (Virtual Desktop → VirtualDesktop,
OBS → OBSStreaming, Discord → DiscordCall). It is off by default. For streaming, the auto streaming mode below is the
better choice: it switches back when you stop and does not overwrite your chosen profile.

## Auto streaming mode (OBS, Streamlabs, Discord)

On by default (`[Streaming] AutoStreamingMode`). While you record or stream, CRIX runs the `OBSStreaming` profile
(or `DiscordCall` for Discord) and returns to your own profile 20 s after it ends (`HoldSeconds`). The dashboard and
log show `auto: OBS live` (or similar) while it is active. It only replaces `Balanced`, `OBSStreaming` and
`DiscordCall`; `VirtualDesktop` switches to its Streaming variant; `MaximumPerformance`, `ReShadeCompatible`,
`MultiMod` and `Custom` are kept as you chose them.

**OBS Studio (28 or newer): turn on its WebSocket server so CRIX knows when you are actually live or recording.**

1. In OBS: **Tools → WebSocket Server Settings**. Tick **Enable WebSocket server**. Leave the port at **4455**.
2. If **Enable Authentication** is ticked (the default), click **Show Connect Info**, copy the **Server Password**,
   and paste it into `com.crix.crixoptimizer.cfg`:
   ```
   [Streaming]
   ObsWebSocketPassword = paste-the-password-here
   ```
   (The password is stored in plain text in that file. CRIX never writes it to the log.)
3. Restart the game or press **Ctrl+F5**. The log shows `Connected to OBS ... (read-only)`, and the dashboard's
   `Streaming:` line shows `OBS idle`, `OBS recording` or `OBS live`.

CRIX connects to `127.0.0.1` only and only asks OBS for its streaming/recording state. It cannot start, stop or
change anything in OBS. Without the WebSocket, CRIX treats "OBS is running" as streaming (OBS renders its preview
even when idle). If the password is wrong, the log says so and CRIX only retries every 5 minutes.

**Streamlabs Desktop:** nothing to set up. While Streamlabs runs, CRIX reads its live/recording state from
Streamlabs' local API (127.0.0.1:28194), read-only.

**Discord:** Discord's call / Go Live state cannot be read without injecting into Discord, which CRIX never does.
While Discord runs, CRIX uses `DiscordCall`. If you keep Discord open all the time and do not want that, set
`[Streaming] DiscordProfile = Balanced` or `[Streaming] Trigger = WhenRecordingOrLive`.

## Check that it is working

Open `Gorilla Tag/BepInEx/LogOutput.log` after ~10 s in game and look for:

- `CRIX status: Profile Balanced. N game patch(es) active. N engine setting(s) changed: ...`. This lists each engine
  setting with its old and new value. Under URP you should see entries such as `Shadow distance (URP asset): ...`.
- `CRIX is loaded but nothing is being changed right now: <reason>`. This is a warning that tells you why.
- `Adaptive quality: level -1 ...` lines when the frame rate drops and CRIX steps in.

VR frame rates are capped at the headset's refresh rate (72/80/90/120 Hz). If the game already holds that rate,
the FPS number will not go up. Improvements show as fewer drops in busy lobbies, lower and steadier frame times
(`Frame ... p99` on the dashboard) and more headroom. Use the benchmark recorder (F6) to compare profiles.

## Diagnostics and dashboard

- **F8** toggles the dashboard on the **desktop window** (it is not visible in the headset). It is off by default.
  OBS window/game capture will record it, so hide it while streaming.
- Each value is labelled: `[M]` measured in game, `[D]` derived from measured values, `[R]` reported by the VR
  runtime/Unity, `n/a` not available. Nothing is estimated.
- Alerts for sustained low frame rate go to the BepInEx log (`[Diagnostics] AlertsEnabled`). An on-screen alert line
  is optional (`[Dashboard] ShowAlertsWhenHidden`).
- **F6** records a benchmark (see [docs/BENCHMARKING.md](docs/BENCHMARKING.md)).

## Restore settings

- **F10** restores every engine setting CRIX changed and makes all patches pass through to the game's code. Press F10
  again to resume. The log lists anything that could not be restored, and anything left alone because the game or
  another mod had changed it.
- `[Actions] RestoreOriginalSettingsNow = true` does the same from a config editor (it resets itself).
- `[General] Enabled = false` keeps CRIX inactive until you set it back to `true`.
- `[Actions] ResetConfigToDefaults = true` resets both config files to their defaults.

## Uninstall

1. Quit the game.
2. Delete `Gorilla Tag/BepInEx/plugins/CRIXOptimizer/`.
3. Optional: delete `BepInEx/config/com.crix.crixoptimizer.cfg`, `BepInEx/config/com.crix.crixoptimizer.profiles.cfg`
   and `BepInEx/CRIXOptimizer/`.

CRIX's changes only exist while it runs, so nothing else needs undoing. To return to a completely unmodded game,
remove BepInEx as well (delete the `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version`
from the game folder) or use Steam's *Verify integrity of game files*. Verifying does not remove those extra files.

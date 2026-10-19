# Test report: CRIX OPTIMIZER 1.1.0

Date: 2026-10-14. Raw logs: `release/logs/tests-lib-bepinex.log` (BepInEx 5.4.21) and
`release/logs/tests-extra-bepinex.log` (BepInEx 5.4.23.5).

## Summary

| Run | Runtime | BepInEx | HarmonyX | Result |
|---|---|---|---|---|
| 1 | Mono 6.8.0.105 | 5.4.21 (built from tag `v5.4.21`) | 2.9.0 (net35, as shipped with BepInEx) | **171 passed, 0 failed** |
| 2 | Mono 6.8.0.105 | 5.4.23.5 (built from `v5-lts`; the log shows assembly version 0.0.0.0 because the scratch build omitted the version stamp) | 2.9.0 | **171 passed, 0 failed** |
| - | `tools/compare_benchmarks.py --self-test` (synthetic data) | | | passed |
| - | Reference check of the shipped DLL (see BUILD_REPORT.md) | 5.4.21 and 5.4.23.5 | 2.9.0 | all 298 type / 1315 member refs resolve |
| - | `tools/check_unity_api.py` against Unity's published C# source | Unity 6000.1.9f1 and 6000.2.9f1 | | all 105 Unity type / 259 member references declared |

**In the real game (field report, not a test run by the developers):** a user ran a 1.1.0 preview build on Gorilla
Tag 1.1.147 (Unity 6000.2.9f1, BepInEx 5.4.23.5, Windows 11, Ryzen 5 5500, RTX 4060, Quest via a Meta/OpenXR runtime)
and sent the system report. It shows CRIX loading, detecting URP, changing the URP asset's render scale (0.95 -> 0.85)
and confirming that the VR eye textures followed ("eye textures at 0.85 as intended"), changing the additional-light
limit and the upload slice, deferring its stutter patches to an installed Potassium, auto streaming mode switching to
DiscordCall while Discord ran, and the camera inventory finding the game's "Shoulder Camera". The same report showed the
VR runtime asking for about 37 megapixels per eye at 60 Hz, which led to the eye-resolution cap and setup advice in
1.1.0. No benchmark or frame-rate improvement is claimed from it.

## How the tests are built

- Tests run on **Mono**, the runtime family Unity's scripting backend is based on, using the **exact HarmonyX 2.9.0
  and MonoMod builds** BepInEx 5 ships, and the **real BepInEx** configuration code.
- Game patches are exercised through the **production code path** (`GameTypes` → `GamePatchCatalog` → `PatchSet` →
  Harmony). The target is `Tests/FakeGame`, a stand-in assembly compiled under the name `Assembly-CSharp`. Its types
  model only the members Potassium 1.0.0 references (names, kinds and signatures as Potassium uses them). It is
  original test code with no game code in it. Patched stand-in methods are marked `NoInlining`, because in Unity the
  engine calls those methods directly and they cannot be inlined.
- Pure logic (`Core/`, `Adaptive/`, configuration) is tested directly.

## What was tested (171 tests)

| Area | Tests | What they establish |
|---|---|---|
| Frame statistics | 5 | average FPS = frames/time; time-based window eviction; percentiles, 1% low, over-budget and hitch counts; invalid samples ignored; bounded memory and exact sums after 100k evictions |
| Throttle maths | 2 | interval never throttles below the requested rate (checked across 30 fps/rate pairs); every instance runs exactly once per cycle for any hash |
| Trend / degradation | 5 | memory-growth slope exact on linear data, ~0 on noise; alerts need sustained drops, recover with hysteresis, respect cooldown; 85 vs 90 FPS is not an alert at 10 % threshold |
| Benchmark maths | 2 | both 1% low definitions, p99 nearest rank, over-budget %, hitches, invalid-sample handling |
| Companion apps | 3 | exact process/module name classification (no substring false positives), profile suggestion priority |
| Settings ledger | 9 | only values that differ from the game's count as "changed"; original captured once and restored; external change detected, respected, never overwritten on restore; re-arm adopts the external value; engine clamping recorded and not mistaken for drift; failing accessors become "Failed" without throwing; Reapply policy; restore report counts |
| Status / logging | 2 | status board versioning and counts; once-only logger |
| Adaptive controller | 14 | stays at baseline when on target; steps down only after the sustain time; short dips ignored; cooldown/settle limit change rate; minimum level respected; probes up after a stable period without GPU timing; **failed probes back off exponentially**; GPU headroom allows step-up, a nearly full GPU blocks it; GPU-headroom target steps down before frames drop; CPU-bound detection; manual/freeze; tightened bounds enforced; sensitivity changes tolerance |
| Quality ladder | 6 | level 0 = baseline; cheapest reductions first; cost never increases with lower levels; CPU-bound keeps resolution; disallowed levers untouched; upward levels raise render scale only, up to the maximum; never raises a baseline that is below the minimum |
| Profiles / validation | 10 | every preset validates clean; section mapping; ReShade profile never touches render targets; streaming profiles keep the captured window; MultiMod is conservative; no preset bypasses the game's sanitizer or changes MSAA/texture resolution; validation clamps and fixes inconsistencies; ReShade guard forces safe values and is idempotent; every profile field has a description; every preset fits its config range |
| BepInEx config (real ConfigFile) | 5 | profiles file contains every preset section and resolves back to identical values; edits survive reload, reset restores defaults; out-of-range values clamped by BepInEx; main config binds every section with documented defaults; SettingChanged fires once per real change |
| Patch application | 7 | game types found by full and simple name (namespace moves tolerated); **all 13 game patches apply**; missing targets and Harmony-rejected patches fail alone; **with Potassium installed nothing is patched** (all 14 patch IDs deferred); full sanitizer replacement refuses a non-Unity signature; Harmony overlap report finds another mod's patch on the same method; **regression: a failed patch no longer prevents other mods from patching that method** |
| Throttle patches | 5 | voice reactors run exactly every 3rd frame at 90 FPS / 30 per s, smoothed reactors never throttled; staggering vs Potassium-style timers (below); texture transitions throttle and toggle live; suspend passes everything through; 45 FPS reprojection disables throttling |
| Stutter patches | 11 | **with Potassium installed (DeferToPotassium) no patch is applied but the read-only in-room check works (the deferred asset unload needs it)**; inactive entity managers skip scans, toggle live; room-leave GC: both `GC.Collect` overloads redirected, Skip / GameDefault (a collection really happens) / Incremental / fallback; timed asset unloads skipped and re-enabled live; **SafeLookups sanitizer (stand-in shaped like the current game: only the layer check is a List.Contains, so exactly one lookup is rewritten) returns identical verdicts and layer handling for 500 random objects, and follows runtime allow-list and layer-list changes**; scoreboard refresh one per frame with duplicate requests merged; queued refreshes flushed when disabled; scoreboard creation waits for the room and runs once per frame; Shared Blocks deferred until the zone is active and **no manager lost**; released immediately when disabled live; rig pool: 4 immediately, none while in a room, on-demand rig for the 5th player, completes to the game's 19 one per frame, every rig initialised once, post-spawn events raised; cosmetic culling applies, updates live and restores the game's values |
| Accessors | 2 | compiled delegates reach private/static fields, properties and methods (including enum-constant predicates) on Mono; missing or incompatible members return null |
| MiniJson | 3 | parsing of objects, arrays, escapes, numbers; malformed input rejected; serialisation round-trip |
| Capture state | 6 | Auto uses OBS's exact live/recording state when connected and falls back to "app running"; OBS outranks Discord; strict trigger; hold time before dropping back; stale capture/overlay DLLs ignored once their app was seen and closed, kept when the app was never visible (OBS as administrator) |
| OBS WebSocket client (integration, fake OBS server over TCP) | 10 | v5 handshake and SHA-256 authentication; password missing/rejected (close 4009); events and polled state; only the four read-only status requests are ever sent; non-local hosts refused; **a ping no longer stalls polling**; fragmented messages with an interleaved ping are reassembled; "kicked" (close 4011) stays visible; replay buffer counts as capture; auth formula matches the protocol |
| Streamlabs client | 6 | live/reconnecting/recording/transitional states; "busy"/error answers count as not connected; dual-output displays combined; replay buffer read; transitions logged once; real TCP poll sends only `StreamingService.getModel` |
| Resolution probe | 3 | ineffective drop detected; effective drop accepted with settle samples ignored; half-rate (reprojection) lock reported as inconclusive |
| CPU levers / eye resolution | 4 | adaptive cosmetic/update-rate levers scale the profile without changing it and keep disabled throttles disabled; eye megapixels measured from the report's real numbers (4992x5376 at 0.85 -> 37 MP); cap computes the largest render scale within the limit, never below 0.35, off at 0 |
| Desktop camera | 2 | game member found by name; waits safely (nothing managed) while the game is not ready; reports "unavailable" when the member is missing |
| Sakuraa / Spotify detection | 4 | detection by Harmony owner and plugin GUID, advice only when detected, state cleared on refresh; Spotify classified, described, never switches profile |
| ReShade preset advisor (real files in a temp folder) | 11 | reads `PerformanceMode` and `PresetPath`; flags ambient occlusion, depth of field and DisplayDepth, not SMAA or sharpening; the light copy drops only those from `Techniques`, keeps effect settings and `TechniqueSorting` (**regression: `TechniqueSorting` was rewritten too because it starts with "Techniques" when case is ignored**) and contains no expensive effect; the user's preset and `ReShade.ini` are byte-identical afterwards; **headset `ReShadeVR.ini` with per-key fallback is analysed and preferred**; **start-up preset wins, missing/invalid `PresetPath` falls back to `ReShadePreset.ini`, non-preset files and other extensions are rejected, a not-yet-existing preset is valid**; **escaped commas in paths and effect names, `/` comments and repeated keys parse as in ReShade**; `[INSTALL] BasePath` is followed; **headset instructions name `ReShadeVR.ini`, not the Home-key overlay, and are valid ini lines (a comma in the name is doubled; following the printed line makes the headset load the copy)**; **a shared preset that is the desktop window's start-up preset is pointed out**; **`[GENERAL] Disable` in the runtime's own file, ReShade's boolean rules and repeated keys that end in a comma behave as in ReShade**; **the first-VR-session note appears only with a headset**; **a start-up preset and a different last-selected preset are both reported, the heavy one gets the light copy; desktop `Disable=1` without `ReShadeVR.ini` still covers the headset**; missing `ReShade.ini`, old name-only format, nothing to remove |
| Extra-camera limit schedule | 10 | **spectator even-frames cadence as the limiter runs it: every 2nd frame at 120 Hz (60/s), every 2nd at 90 Hz (45/s, never above 60), every 3rd at 144 Hz (48/s), every frame at or below the limit and under reprojection (60 on 120 Hz); with 1.5 ms frame-time noise for 100 s every gap is exactly 2 frames at 120 Hz (99 % when the refresh rate is unknown); a short swing of the frame time does not change N, a lasting one does after 30 frames; a forced render (new texture, resize) happens at once**; simulated like the limiter (real frame time plus the smoothed typical one, even phases): at most N renders per second and per any 1-second window, and at least 90 % of N, for 90/120/72/144 fps against 2-100/s (**regression: the earlier frame-interval maths did not limit 60/s at 90 fps and gave 9/s for 2/s at 144 fps**); a slower game renders every camera every frame; irregular frame times and a 1.5 s hitch cause no burst; **3 x 30/s and 2 x 45/s at 90 and 89.5 fps with 0.3 ms frame-time noise (one noise generator per case, so frame times really vary), a 0.2 s hitch and one camera switched off by its owner: at most 2 % of frames with two renders, never more than two, rates 85-100 % of the limit (measured: one such frame, the hitch)**; **every 7th frame three vsyncs long: still 27-30 renders/s per camera**; **after a 15-frame load screen no frame renders all three cameras and no camera exceeds the limit**; the typical frame time moves at most 20 % on a hitch and follows a lasting drop; per-frame budget (jitter just above 1/90 adds no slot); grid points and phases |
| Player-loop hook | 2 | steps are inserted directly before and after `PostLateUpdate.FinishFrameRendering`; removal takes out only CRIX's steps and keeps another mod's; a missing target is reported, nothing inserted |
| Session trend | 6 | **a heavier map is not a slow drop: a steady heavy map compared with its own first minutes reports nothing; with too few minutes in the new map it says it is a different map and does not blame streaming; a real drop within one map is still found and says so; minutes split between maps are left out; the minute's map is the zone whose play time grew most**; per-minute FPS, hitches (over twice the 120 Hz budget), big hitches, GC deltas and report lines; a steady session reports no drop; **a drop from 110 to 85 FPS while streaming starts and Unity memory grows names the streaming start (with its minute), the profile change and the memory growth**; a drop with nothing visible names outside causes; warm-up minutes, idle-headset minutes, small changes and short sessions are not reported; history is bounded and empty minutes are not recorded |
| Performance notes | 8 | **entering maps: first visit vs later visits measured over the first 10 s (worst frame, big hitches in how many visits), the long activation frame recorded before the zone change is seen still counts for the visit, leaving and re-entering within 10 s counts no frame twice, a zone that leaves starts nothing, an unreadable moment does not count as entering again, a visit after freeing assets counts as a first visit again, zones active during loading marked as not measured**; **custom map content line and its frame-time notes (shadow note only when the pipeline can render realtime shadows, no probe-render claim while realtime probes are off, time slicing taken into account), camera renders per frame by kind**; **time of day, maps loading lightmaps (BetterDayNightManager.allScenesRenderData), zone loads running and custom maps (each map its own zone name, overloaded IsMapLoaded found by name, a new map not reported while it loads, a loaded map keeps its id while it loads more scenes) read through the real reflection path, unreadable = -1**; startup loading kept apart: a big hitch near the end of the first minute extends loading by 20 s, loading ends after 20 quiet seconds and after at most 5 minutes; loading hitches are marked, summarised separately and kept out of the worst list and the map summaries**; per-zone, per-lobby-size and per-streaming-state summaries (110 vs 79 FPS, big hitch counted in its zone), time-stamped events, ten worst hitches worst first, bounded events with a note when the limit is reached, 10 s timeline lines; histogram percentiles and the overflow bucket; lobby buckets; **zones and player counts read by name through the real reflection path** (stable zone order, not in a room = 0, unreadable = -1, missing members = unknown) |
| Other optimisation mods | 3 | GorillaOptimizer toggles sorted into in effect / no effect under URP (built-in-pipeline settings, XR scale, frame caps) with notes for Low Physics Rate and Low XR Scale; without URP the built-in settings count as working; no toggles / a failing read / not installed; I HATE ALIENS and Potassium notes |
| Preset upgrades (real BepInEx ConfigFile, real 1.0.0 profiles file as fixture) | 5 | frozen 1.0.0 presets match the shipped 1.0.0 file; upgrade from 1.0.0 keeps user edits and applies new defaults; upgrade from the 1.1.0 previews (revision 2) applies only the later changes; fresh installs need no upgrade; reset to defaults does not re-run the upgrade |

### Scheduling simulation (synthetic, not a game benchmark)

`ThrottlePatchTests.StaggeringLowersPerFramePeakVersusPotassiumTimers`: 30 proximity reactors created in the same
frame, 900 frames at a simulated 90 FPS, 30 updates/s requested.

| | Total updates | Most updates in one frame |
|---|---|---|
| CRIX (frame-staggered) | 9,000 (exactly 30/s each) | 10 |
| Potassium 1.0.0 timers (simulated with its semantics) | 8,880 | 30 |

This shows how the work is **distributed** across frames, not how fast the game runs. Whether the lower per-frame
peak is measurable in Gorilla Tag depends on how expensive these updates are in the real game, which is unmeasured.

## Defects found by the tests and fixed

1. **HarmonyX "poisoning" after a failed patch.** If Harmony rejected a CRIX patch (for example because a game update
   renamed an injected field), the failed patch stayed registered on that method, and every later patch of the same
   method, including other mods' patches, failed with `IL Compile Error`. `PatchSet` now removes the patch methods
   it tried to add. Covered by a regression test.
2. Scoreboard work was flushed only when *both* scoreboard features were switched off; each is now flushed when its
   own feature is switched off.
3. The frame-statistics window documentation was corrected (a filled window spans the window length plus at most one frame).
4. Unity engine calls were moved into separate non-inlined methods, so patch code can be compiled without the engine.

## Not tested (requires hardware, the game, or Windows)

| Item | Risk / what to check first |
|---|---|
| Loading in Gorilla Tag with BepInEx | Seen working in one field report of a 1.1.0 preview (see Summary). Expect `CRIX OPTIMIZER 1.1.0 loaded`. |
| Real game member shapes | The stand-in models Potassium 1.0.0's usage (game build of Sept 2026). If the current build differs, affected patches report `Unavailable` (they do not crash). Check `system-report.txt` → *Module and patch status*. |
| Unity version of the current build | Gorilla Tag runs Unity 6 (6000.2.9f1 in the field report). Compiled against the 2021.3 API; every Unity member used is declared in 6000.1.9f1 and 6000.2.9f1 (`tools/check_unity_api.py`, names only, not binary signatures). |
| Engine settings at runtime | Unity APIs used as documented, but not executed here: render scale, shadows, LOD, MSAA (may need a restart in VR), mirror mode (runtime dependent), occlusion mesh, camera HDR, async upload, GC slice. |
| XR runtime statistics | Provider dependent (Oculus/OpenXR/OpenVR/VDXR); timing units are auto-detected (seconds vs ms); unverified on real runtimes. |
| FrameTimingManager / ProfilerRecorder counters in a release build | Shown as `n/a` if unavailable; unverified. |
| IMGUI dashboard look and cost | Unverified. CRIX measures and displays its own per-frame cost once running. |
| Hotkeys (legacy vs new Input System via BepInEx `UnityInput`) | If keys fail, CRIX disables hotkeys and logs it; config/ConfigurationManager still work. |
| Process/module enumeration on Windows Mono | Wrapped in try/catch; detection shows "none detected" if unsupported. |
| `Expression.Compile` in Unity's Mono fork | Verified on Mono 6.8; a reflection fallback exists. |
| Any performance effect (all eight benchmark configurations) | **Unmeasured.** Use docs/BENCHMARKING.md. |
| Desktop camera switch, XR mirror, LIV/Sakuraa take-over detection | Logic tested without Unity; the in-game behaviour (what the window shows, the LIV camera's CinemachineBrain signal) is unverified. |
| OBS WebSocket / Streamlabs against the real applications | Tested against protocol-faithful fake servers only. |
| Interaction with other real mods, ReShade, OBS hooks, Virtual Desktop | Unverified beyond the logic tests. |
| Spectator camera limit (`SpectatorCameraMaxFps`, experimental) | Only the cadence is unit tested; rendering into CRIX's texture, the overlay-canvas and blit presenters, hand-back and the interplay with Sakuraa, OBS and ReShade need the game and are not tested. Built from Unity's and URP's published source and the decompiled game and Sakuraa code. Off by default. Check with it set to 60: the system report's *Spectator camera limit* line says "on", the window and OBS show the third-person view smoothly with Sakuraa's HUD and the CRIX dashboard on top, the headset view is unchanged, brightness matches a capture with it off, resizing the window and Sakuraa's camera modes and replay work. |
| Extra-camera limit (`ExtraCameraMaxFps`) | The schedule and the player-loop edit are unit tested; the hook itself is not executed (needs Unity). It relies on Unity 6 collecting the frame's cameras inside `FinishFrameRendering` before URP 17 raises `beginContextRendering`, and URP rendering only the cameras in that list (checked in Unity's and URP's published source, not in the game). Off by default. Check the system report's *Extra camera limit* line lists only render-texture cameras (e.g. Sakuraa's `MiniMapCam`/`DisplayCam`) and that the headset and desktop views stay smooth. |
| ReShade advisor against real ReShade installs | Tested with hand-written ReShade 5/6-style files only; preset paths written in other forms by other ReShade versions may not be found (then nothing is written). |

## Outstanding issues and known limitations

- Inherited from Potassium: the scoreboard-refresh patch skips the game's `UpdateActiveScoreboards` and refreshes
  boards individually. If that method does more than refresh each board, that extra work is skipped (as in Potassium).
  The `MultiMod` profile disables this patch.
- Inherited from Potassium: throttled texture transitions may animate more slowly if the game advances them per call
  rather than by elapsed time. Set `TextureTransitionRate = 0` to disable.
- The adaptive engine changes render scale by recreating eye textures. Each change can cause a one-off hitch, which
  is why it is rate-limited (at most one drop a minute, undone when it does not help), off in HighRefresh and the
  streaming profiles, and disabled under the ReShade guard.
- `TextureMipLimit` re-uploads textures when changed; it is off in every preset. `MsaaSamples` is a cap (never raised); MaximumPerformance, HighRefresh and VirtualDesktop.Performance cap it at 2x, which changes nothing when the game already uses less (the field report showed MSAA 1).

## Suggested in-game smoke test (for someone with the hardware)

1. Install as in INSTALLATION.md; check the log line and `system-report.txt` (all patches `Active`, or a stated reason).
2. F8 → the dashboard shows FPS/frame time; F9 cycles profiles (log: `Profile applied`); F10 → restore report in the
   log, F10 again → resumed.
3. Join and leave a public/private room several times; enter Shared Blocks; load a custom map. No errors in the log.
4. Enable adaptive mode and raise the load (e.g. a high Virtual Desktop resolution). The log shows level changes,
   with no rapid oscillation.
5. Quit the game. The log shows the restore summary, with nothing reported as failed.

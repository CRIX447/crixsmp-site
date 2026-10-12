# Test report: CRIX OPTIMIZER 1.0.0

Date: 2026-10-12. Raw logs: `release/logs/tests-lib-bepinex.log` (BepInEx 5.4.21) and
`release/logs/tests-extra-bepinex.log` (BepInEx 5.4.23.5).

## Summary

| Run | Runtime | BepInEx | HarmonyX | Result |
|---|---|---|---|---|
| 1 | Mono 6.8.0.105 | 5.4.21 (built from tag `v5.4.21`) | 2.9.0 (net35, as shipped with BepInEx) | **87 passed, 0 failed** |
| 2 | Mono 6.8.0.105 | 5.4.23.5 (built from `v5-lts`; the log shows assembly version 0.0.0.0 because the scratch build omitted the version stamp) | 2.9.0 | **87 passed, 0 failed** |
| - | `tools/compare_benchmarks.py --self-test` (synthetic data) | | | passed |
| - | Reference check of the shipped DLL (see BUILD_REPORT.md) | 5.4.21 and 5.4.23.5 | 2.9.0 | all 217 type / 803 member refs resolve |

**Nothing was tested inside Gorilla Tag**, and no headset, GPU or companion app was available. The "Not tested"
section below lists what that leaves open.

## How the tests are built

- Tests run on **Mono**, the runtime family Unity's scripting backend is based on, using the **exact HarmonyX 2.9.0
  and MonoMod builds** BepInEx 5 ships, and the **real BepInEx** configuration code.
- Game patches are exercised through the **production code path** (`GameTypes` → `GamePatchCatalog` → `PatchSet` →
  Harmony). The target is `Tests/FakeGame`, a stand-in assembly compiled under the name `Assembly-CSharp`. Its types
  model only the members Potassium 1.0.0 references (names, kinds and signatures as Potassium uses them). It is
  original test code with no game code in it. Patched stand-in methods are marked `NoInlining`, because in Unity the
  engine calls those methods directly and they cannot be inlined.
- Pure logic (`Core/`, `Adaptive/`, configuration) is tested directly.

## What was tested (87 tests)

| Area | Tests | What they establish |
|---|---|---|
| Frame statistics | 5 | average FPS = frames/time; time-based window eviction; percentiles, 1% low, over-budget and hitch counts; invalid samples ignored; bounded memory and exact sums after 100k evictions |
| Throttle maths | 2 | interval never throttles below the requested rate (checked across 30 fps/rate pairs); every instance runs exactly once per cycle for any hash |
| Trend / degradation | 5 | memory-growth slope exact on linear data, ~0 on noise; alerts need sustained drops, recover with hysteresis, respect cooldown; 85 vs 90 FPS is not an alert at 10 % threshold |
| Benchmark maths | 2 | both 1% low definitions, p99 nearest rank, over-budget %, hitches, invalid-sample handling |
| Companion apps | 3 | exact process/module name classification (no substring false positives), profile suggestion priority |
| Settings ledger | 8 | original captured once and restored; external change detected, respected, never overwritten on restore; re-arm adopts the external value; engine clamping recorded and not mistaken for drift; failing accessors become "Failed" without throwing; Reapply policy; restore report counts |
| Status / logging | 2 | status board versioning and counts; once-only logger |
| Adaptive controller | 14 | stays at baseline when on target; steps down only after the sustain time; short dips ignored; cooldown/settle limit change rate; minimum level respected; probes up after a stable period without GPU timing; **failed probes back off exponentially**; GPU headroom allows step-up, a nearly full GPU blocks it; GPU-headroom target steps down before frames drop; CPU-bound detection; manual/freeze; tightened bounds enforced; sensitivity changes tolerance |
| Quality ladder | 6 | level 0 = baseline; cheapest reductions first; cost never increases with lower levels; CPU-bound keeps resolution; disallowed levers untouched; upward levels raise render scale only, up to the maximum; never raises a baseline that is below the minimum |
| Profiles / validation | 10 | every preset validates clean; section mapping; ReShade profile never touches render targets; streaming profiles keep the captured window; MultiMod is conservative; no preset bypasses the game's sanitizer or changes MSAA/texture resolution; validation clamps and fixes inconsistencies; ReShade guard forces safe values and is idempotent; every profile field has a description; every preset fits its config range |
| BepInEx config (real ConfigFile) | 5 | profiles file contains every preset section and resolves back to identical values; edits survive reload, reset restores defaults; out-of-range values clamped by BepInEx; main config binds every section with documented defaults; SettingChanged fires once per real change |
| Patch application | 7 | game types found by full and simple name (namespace moves tolerated); **all 13 game patches apply**; missing targets and Harmony-rejected patches fail alone; **with Potassium installed nothing is patched** (all 14 patch IDs deferred); full sanitizer replacement refuses a non-Unity signature; Harmony overlap report finds another mod's patch on the same method; **regression: a failed patch no longer prevents other mods from patching that method** |
| Throttle patches | 5 | voice reactors run exactly every 3rd frame at 90 FPS / 30 per s, smoothed reactors never throttled; staggering vs Potassium-style timers (below); texture transitions throttle and toggle live; suspend passes everything through; 45 FPS reprojection disables throttling |
| Stutter patches | 11 | inactive entity managers skip scans, toggle live; room-leave GC: both `GC.Collect` overloads redirected, Skip / GameDefault (a collection really happens) / Incremental / fallback; timed asset unloads skipped and re-enabled live; **SafeLookups sanitizer returns identical verdicts and layer handling for 500 random objects, and follows runtime allow-list changes**; scoreboard refresh one per frame with duplicate requests merged; queued refreshes flushed when disabled; scoreboard creation waits for the room and runs once per frame; Shared Blocks deferred until the zone is active and **no manager lost**; released immediately when disabled live; rig pool: 4 immediately, none while in a room, on-demand rig for the 5th player, completes to the game's 19 one per frame, every rig initialised once, post-spawn events raised; cosmetic culling applies, updates live and restores the game's values |
| Accessors | 2 | compiled delegates reach private/static fields, properties and methods (including enum-constant predicates) on Mono; missing or incompatible members return null |

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
| Loading in Gorilla Tag with BepInEx | Expect `CRIX OPTIMIZER 1.0.0 loaded`. Run `tools/RefCheck` against the install. |
| Real game member shapes | The stand-in models Potassium 1.0.0's usage (game build of Sept 2026). If the current build differs, affected patches report `Unavailable` (they do not crash). Check `system-report.txt` → *Module and patch status*. |
| Unity version of the current build | Compiled against the 2021.3 API, and every API used exists through 2022.3. On a much newer Unity, guarded calls fall back or report unavailable. |
| Engine settings at runtime | Unity APIs used as documented, but not executed here: render scale, shadows, LOD, MSAA (may need a restart in VR), mirror mode (runtime dependent), occlusion mesh, camera HDR, async upload, GC slice. |
| XR runtime statistics | Provider dependent (Oculus/OpenXR/OpenVR/VDXR); timing units are auto-detected (seconds vs ms); unverified on real runtimes. |
| FrameTimingManager / ProfilerRecorder counters in a release build | Shown as `n/a` if unavailable; unverified. |
| IMGUI dashboard look and cost | Unverified. CRIX measures and displays its own per-frame cost once running. |
| Hotkeys (legacy vs new Input System via BepInEx `UnityInput`) | If keys fail, CRIX disables hotkeys and logs it; config/ConfigurationManager still work. |
| Process/module enumeration on Windows Mono | Wrapped in try/catch; detection shows "none detected" if unsupported. |
| `Expression.Compile` in Unity's Mono fork | Verified on Mono 6.8; a reflection fallback exists. |
| Any performance effect (all eight benchmark configurations) | **Unmeasured.** Use docs/BENCHMARKING.md. |
| Interaction with other real mods, ReShade, OBS hooks, Virtual Desktop | Unverified beyond the logic tests. |

## Outstanding issues and known limitations

- Inherited from Potassium: the scoreboard-refresh patch skips the game's `UpdateActiveScoreboards` and refreshes
  boards individually. If that method does more than refresh each board, that extra work is skipped (as in Potassium).
  The `MultiMod` profile disables this patch.
- Inherited from Potassium: throttled texture transitions may animate more slowly if the game advances them per call
  rather than by elapsed time. Set `TextureTransitionRate = 0` to disable.
- The adaptive engine changes render scale by recreating eye textures. Each change can cause a one-off hitch, which
  is why it is rate-limited (cooldown, settle time, back-off) and disabled under the ReShade guard.
- `TextureMipLimit`/`MsaaSamples` re-upload/recreate resources when changed. They are off in every preset.

## Suggested in-game smoke test (for someone with the hardware)

1. Install as in INSTALLATION.md; check the log line and `system-report.txt` (all patches `Active`, or a stated reason).
2. F8 → the dashboard shows FPS/frame time; F9 cycles profiles (log: `Profile applied`); F10 → restore report in the
   log, F10 again → resumed.
3. Join and leave a public/private room several times; enter Shared Blocks; load a custom map. No errors in the log.
4. Enable adaptive mode and raise the load (e.g. a high Virtual Desktop resolution). The log shows level changes,
   with no rapid oscillation.
5. Quit the game. The log shows the restore summary, with nothing reported as failed.

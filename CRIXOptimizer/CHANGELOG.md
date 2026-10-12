# Changelog

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

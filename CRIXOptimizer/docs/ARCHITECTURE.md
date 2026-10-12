# Architecture

```
Plugin (BepInEx entry, thin)
 └─ OptimizerRuntime  ── composition root, main loop, fault isolation, config events, hotkeys
     ├─ PluginConfig / ProfileStore ──► OptimizationSettings (validated, ReShade guard applied)
     ├─ GamePatchCatalog ── Potassium-derived Harmony patches (CPU/, Memory/, GPU/CosmeticCulling)
     │     GameTypes (name index of Assembly-CSharp) + Accessors (compiled delegates) + PatchSet (per-patch, cleanup)
     │     patches read static Live.* flags  ◄── UpdateLive / UpdateThrottleIntervals
     ├─ GraphicsSettingsModule ── engine settings via SettingsLedger (TrackedSetting<T> per value);
     │     under URP: public properties of the active URP asset (render scale, MSAA, shadows, lights)
     ├─ AdaptiveController + QualityLadder ── level → lever values → GraphicsSettingsModule.Apply
     │     + CPU levers (cosmetic detail, update rates) → EffectiveSettings → game patches
     │     ResolutionProbe ── did lowering resolution help? if not: CPU-bound for 3 min
     ├─ CaptureStateMachine ◄── ObsWebSocketClient / StreamlabsStatusClient (worker threads, read-only,
     │     127.0.0.1) + process/hook detection ──► effective profile (auto streaming mode)
     ├─ FrameTimeMonitor (FrameStatistics), GpuTimingProbe (XRRuntimeInfo), MemoryMonitor (TrendAnalyzer)
     ├─ DegradationDetector (alerts), BenchmarkRecorder (BenchmarkMath, worker-thread writes)
     ├─ EnvironmentScanner (worker thread) ──► VirtualDesktop/Streaming/Discord/ReShade support (advice, guard)
     ├─ PluginInventory, HarmonyFootprint (compatibility scan, once + on demand), SakuraaSupport, CameraInventory
     └─ DashboardOverlay (exists only while visible) ◄── DashboardText (rebuilt 2×/s), SystemReport
```

## Design rules

1. **No compile-time dependency on the game.** Only BepInEx, HarmonyX and Unity engine modules are referenced.
   Game types are looked up by name once; members are bound to compiled expression-tree delegates (no reflection or
   boxing on hot paths). Anything missing turns one feature into `Unavailable` and never stops the plugin.
2. **Every patch stands alone.** `PatchSet` applies patches one by one, records the result, and on a Harmony failure
   removes the partial registration, so other mods can still patch that method. Patch bodies catch their own
   exceptions, switch themselves off, and run the game's original code.
3. **Hot paths read plain statics.** Patches read `Live.*` fields that the runtime rewrites on configuration changes.
   No config lookups, dictionaries or allocations per call. Throttles use `ThrottleMath.IsInPhase(frame, interval,
   identityHash)`.
4. **Per-frame cost is O(1).** `OptimizerRuntime.Update` samples the frame time, runs at most one deferred scoreboard
   action and one pooled rig, polls hotkeys, and checks timers. Everything else runs at 4/2/1/0.5/0.2 Hz. Scans and
   file writes run on worker threads, with results handed back through `MainThreadQueue`.
5. **Settings are borrowed, not owned.** `TrackedSetting<T>` captures the original on first change, reads back what
   the engine actually stored, detects external changes (drift), and restores only values that are still CRIX's own.
6. **Pure logic is Unity-free and unit-tested.** `Core/`, `Adaptive/`, most of `Configuration/` and the
   `ReShadeGuard` have no Unity dependency. Engine calls are kept in small, non-inlined methods, so the patch code can
   run on plain Mono in the test suite.
7. **Honest diagnostics.** Values are labelled measured / derived / runtime-reported / unavailable. Nothing that
   cannot be read through a supported API (GPU utilisation, VRAM use, streaming latency) is ever displayed as a number.

## Lifecycle

`Awake` → bind configs → index game types → apply needed patches (lazily per group) → apply profile → start
background environment scan. At +5 s the profile is re-applied, because XR is often initialised after plugins load.
At +6 s the compatibility scan runs; at +20 s the system report is written.
After the +5 s re-apply, a `CRIX status:` line summarises what is being changed. The OBS WebSocket client starts at
load (if enabled), and the Streamlabs client starts when Streamlabs is detected. Both run on background threads and
publish an immutable status snapshot that the main thread reads once per second.

`OnDestroy` / `OnApplicationQuit` → release deferred work (Shared Blocks, scoreboards) → unpatch → restore every
ledger setting (logging any that could not be restored) → dispose profiler recorders → remove the dashboard.

Profile files carry `[Meta] PresetRevision`. On load, `ProfileStore` upgrades entries that still equal the previous
release's default (frozen in `ProfilePresetsV1`) to the current default, and keeps every edited value.

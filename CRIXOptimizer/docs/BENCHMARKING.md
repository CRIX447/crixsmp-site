# Benchmarking

**No benchmark results exist yet.** The development environment had no Gorilla Tag install, GPU or headset, so
nothing could be measured. This document is the protocol for producing honest, reproducible numbers. Please do not
publish performance claims for CRIX that were not measured this way.

## The recorder

Press **F6** (or `[Actions] StartBenchmarkNow = true`). After `[Benchmark] WarmupSeconds` (default 5 s) CRIX records
every frame for `[Benchmark] DurationSeconds` (default 60 s), then writes two files to
`BepInEx/CRIXOptimizer/benchmarks/`:

- `<time>_<profile>[_adaptive][_<label>].csv`: one frame time per line,
- `<time>_<profile>[...].summary.txt`: `key=value` statistics plus the environment and the active settings.

| Summary key | Meaning | Source |
|---|---|---|
| `avg_fps` | frames / total time | measured |
| `low1_fps_percentile` | FPS at the 99th-percentile frame time | derived |
| `low1_fps_average` | average FPS of the slowest 1 % of frames | derived |
| `low01_fps_percentile` | FPS at the 99.9th-percentile frame time | derived |
| `stddev_frame_ms`, `p95/p99/p999/max_frame_ms` | frame-time consistency | derived |
| `over_budget_percent` | frames slower than 1.05 × the target frame time | derived |
| `hitches_over_2x_budget` | frames slower than 2 × the target frame time | derived |
| `gc_collections`, `managed_heap_delta_mb` | garbage-collection activity during the capture | measured |
| `process_cpu_percent_of_all_cores`, `process_cpu_cores_busy` | Gorilla Tag process CPU time / wall time | measured |
| `gpu_frame_ms_avg` | GPU frame time, **only if** the VR runtime or Unity reports it | runtime-reported or `unavailable` |
| `xr_dropped_frames` | runtime-reported dropped frames | runtime-reported or `unavailable` |
| `gpu_utilization`, `vram_usage` | always `unavailable`; measure with an external tool | - |

Set `[Benchmark] Label` (e.g. `stump-empty`) to tag runs.

Compare runs:

```bash
python3 tools/compare_benchmarks.py "<Gorilla Tag>/BepInEx/CRIXOptimizer/benchmarks" --baseline Disabled --csv results.csv
```

## Configurations to compare

| # | Configuration | How to set it up | Note |
|---|---|---|---|
| 1 | Baseline: no Potassium, no CRIX | Move both DLLs out of `BepInEx/plugins`. CRIX's recorder is unavailable, so use an external tool (see below). | Use the external tool for all eight runs too, so every run is measured the same way. |
| 2 | Original Potassium 1.0.0 | Only `Potassium.dll` installed | External tool |
| 3 | CRIX loaded, all optimisations off | CRIX installed, `[General] Enabled = false` | Measures CRIX's own diagnostic overhead. Recorder labels it `Disabled`. |
| 4 | CRIX Balanced | `Profile = Balanced`, `[Adaptive] Mode = Off` | Fixed profile only, so runs are comparable |
| 5 | CRIX Virtual Desktop | `Profile = VirtualDesktop` (state the preset) | Over Virtual Desktop |
| 6 | CRIX OBS Streaming | `Profile = OBSStreaming` | With OBS recording |
| 7 | CRIX Discord Call | `Profile = DiscordCall` | With a Discord call (and screen share if you use it) |
| 8 | CRIX adaptive | Your profile + `[Adaptive] Mode = On` (the 1.1 default) | Recorder labels it `<profile>_adaptive` |

For configurations 1-2, and to get GPU utilisation for all of them, use an external tool **for every run**, so all
eight are measured identically: for example PresentMon / Intel PresentMon (frame times plus GPU busy), fpsVR (SteamVR),
Virtual Desktop's performance overlay, or Windows Task Manager (GPU engine graphs: *3D*, *Video Encode*).

## Keeping runs comparable

- **Adaptive mode is on by default since 1.1.0** and **auto streaming mode** switches profiles while OBS/Streamlabs is
  live or recording, or Discord runs. For configurations 4-7 set `[Adaptive] Mode = Off`, and set
  `[Streaming] AutoStreamingMode = false` so the profile you are measuring stays active. The recorder's summary
  names the profile that was actually running.

- **Same everything except the variable:** game version, headset, refresh rate, Virtual Desktop/Link settings, OBS
  scene/encoder, Discord state, Windows power plan, GPU driver, other running apps, BepInEx plugins (other than the
  ones under test).
- **Same scene.** Two scenarios are useful:
  - *Static*: a private room in a fixed map (e.g. Forest/Stump), standing in a fixed spot facing a fixed direction.
    Measures steady-state rendering cost.
  - *Room churn*: joining a busy room / players joining and leaving. This is where the stutter fixes act; look at
    `max_frame_ms`, `hitches_over_2x_budget` and `p999_frame_ms` rather than average FPS.
- **Warm up** once per game launch (load the map, wait for cosmetics to load) before the first capture.
- **Repeat 3+ times** per configuration, alternating the order (A B C, C B A, ...), and compare medians.
  Differences below ~2-3 % are usually noise in VR.
- **VR frame rates are capped** at the refresh rate (or locked to half under reprojection/spacewarp). If every
  configuration hits the cap, average FPS cannot show a difference. Compare `over_budget_percent`, `p99/p999`,
  `hitches`, and GPU frame time / GPU busy from the runtime or an external tool. For a headroom test, raise the load
  (higher Virtual Desktop resolution or game render scale) until the baseline misses the target.
- Record the system report (`BepInEx/CRIXOptimizer/system-report.txt`) with each set of results.

## Results template

Fill in after measuring on real hardware. Leave cells you could not measure as "not measured".

| # | Configuration | Avg FPS | 1% low (p99) | p99.9 ms | max ms | over budget % | hitches | GCs | CPU % | GPU ms / busy % | notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Baseline | | | | | | | | | | |
| 2 | Potassium 1.0.0 | | | | | | | | | | |
| 3 | CRIX disabled | | | | | | | | | | |
| 4 | CRIX Balanced | | | | | | | | | | |
| 5 | CRIX VirtualDesktop | | | | | | | | | | |
| 6 | CRIX OBSStreaming | | | | | | | | | | |
| 7 | CRIX DiscordCall | | | | | | | | | | |
| 8 | CRIX + adaptive | | | | | | | | | | |

Long-session stability: run a 30-60 min session with the dashboard's memory line or periodic system reports, and
note the managed-heap growth rate and any `Managed memory has grown` warnings.

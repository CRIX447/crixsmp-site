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
`[Actions] ReapplyProfileNow = true`, or switch profile. `[Compatibility] ExternalChangePolicy = Reapply` makes this
permanent, but it can make two mods fight over a value. Use it only if you know nothing else manages that setting.

## Visual problems

| Symptom | Likely cause / fix |
|---|---|
| Image blurry | Profile render scale < 1 (MaximumPerformance 0.8, VirtualDesktop.Performance 0.85) or adaptive mode lowered it. Use another profile, set `RenderScale = 0`, or raise `[Adaptive] MinimumRenderScale`. |
| Shadows missing/short | `ShadowDistanceLimit` / `ShadowQuality` in your profile, or adaptive level < 0. |
| Desktop window black or different view | `DesktopMirror` set to `None`/another mode. Set it to `Unchanged`. |
| Colours/brightness changed | `CameraHdr = Off` (MaximumPerformance). Set it to `Unchanged`. |
| ReShade depth effects broken or flickering | Use the `ReShadeCompatible` profile, and keep `[Compatibility] ReShadeGuard = Auto` (or `On`). Avoid adaptive render-scale changes. |
| MSAA setting has no effect | Some VR runtimes only apply MSAA when the XR session starts. Restart the game. The dashboard shows the eye-texture MSAA actually in use. |
| Cosmetic effects update less smoothly | Visual throttles (voice-reactive, proximity, texture transitions). Raise the rates or set them to `0` (unthrottled). |
| Distant players' cosmetics disappear | Distant cosmetic culling. Raise `CosmeticMaximumDistance`/`CosmeticMaximumCount` or set `ReduceDistantCosmetics = false`. |

## Performance did not improve

- CRIX can only reduce **game-side** work. If the bottleneck is the Virtual Desktop/Link encoder, Wi-Fi, OBS
  encoding, or another program using the GPU, game-side changes will help little. See
  [docs/VIRTUAL_DESKTOP_GUIDE.md](docs/VIRTUAL_DESKTOP_GUIDE.md) and [docs/OBS_DISCORD_RESHADE_GUIDE.md](docs/OBS_DISCORD_RESHADE_GUIDE.md).
- If the dashboard shows `CPU-bound`, lowering resolution will not help; shadows/LOD/pixel lights and the stutter
  fixes are the relevant levers.
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

## Reporting a problem

Include: `BepInEx/LogOutput.log`, `BepInEx/CRIXOptimizer/system-report.txt` (`[Actions] WriteSystemReportNow = true`
refreshes it), your profile name, and what you did right before the problem. The report contains versions, hardware
model names and plugin names. It contains no personal data, but read it before you share it.

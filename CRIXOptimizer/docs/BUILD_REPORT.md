# Build report: CRIX OPTIMIZER 1.1.0

Date: 2026-10-14. Raw logs: `release/logs/` (also inside the release ZIP under `logs/`).

## Result

| | |
|---|---|
| Output | `bin/Release/CRIXOptimizer.dll` (copied to `release/CRIXOptimizer.dll` and into the ZIP) |
| Size | 417,280 bytes |
| SHA-256 | `d6685e88db816675b327b4e8951cf6f4946f64c8e910becfa506e5473f56fe71` |
| Warnings / errors | **0 / 0** |
| Target | .NET Standard 2.1, AnyCPU, Release, deterministic (`ContinuousIntegrationBuild=true`) |
| Assembly references | `netstandard 2.1`, `BepInEx 5.4.21`, `0Harmony 2.9.0`, `UnityEngine.CoreModule`, `.IMGUIModule`, `.TextRenderingModule`, `.XRModule`, `.VRModule`, `.SubsystemsModule`. **No `Assembly-CSharp`**: no game code is referenced. |

The DLL was rebuilt from the committed source with the command below, and the hash above was confirmed to match. It
was not produced on, or copied from, any other machine.

## Environment

| | |
|---|---|
| OS | Ubuntu 24.04.5 LTS (cloud container, x86-64) |
| SDK | .NET SDK 8.0.131 (Ubuntu package `dotnet-sdk-8.0`) |
| Test runtime | Mono 6.8.0.105 (Ubuntu package `mono-complete`) |
| Not available | Windows, Gorilla Tag, a GPU, a VR headset, Virtual Desktop/OBS/Discord/ReShade |
| Network | `api.nuget.org` and GitHub (via git) reachable; `dot.net` and `nuget.bepinex.dev` blocked by the environment's egress policy, so BepInEx was built from source instead of downloaded |

## Compile-time references

| Reference | Version | Obtained from | SHA-256 |
|---|---|---|---|
| `BepInEx.dll` | 5.4.21 | Built from https://github.com/BepInEx/BepInEx tag `v5.4.21` (commit `0d06996b52c0215a8327b8c69a747f425bbb0023`) by `tools/setup-references.sh` | `09eaab259776e82ebf8bf25a7d95ae809f43e19e7fa5a3acb29b2b01d8d10920` |
| `0Harmony.dll` | HarmonyX 2.9.0 (net35 build) | nuget.org package `HarmonyX` 2.9.0 (nupkg `290cdc58…a18f`) | `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031` |
| `UnityEngine.*Module.dll` (65 files) | Unity 2021.3.33 API | nuget.org package `UnityEngine.Modules` 2021.3.33, `lib/net45` (nupkg `d32d3452…60c4`) | CoreModule `3cbe147e…aad7`, XRModule `1d3381fe…7aa6`, VRModule `319da7ca…c2e5` |

Why these versions: BepInEx 5.4.21 is the oldest 5.4.x release in common use for Gorilla Tag. Compiling against the
oldest version guarantees no newer-only API is used. This caught one real problem: `ConfigFile.Values` exists in
later versions but not in 5.4.21. Unity 2021.3 is the lowest Unity version that matches Potassium's
.NET Standard 2.1 target. The few APIs that newer Unity versions renamed are resolved at runtime
(`globalTextureMipmapLimit` first, `masterTextureLimit` as a fallback; `FrameTimingManager.IsFeatureEnabled` by
reflection).

## Commands

```bash
tools/setup-references.sh                               # lib/: BepInEx 5.4.21 from source, HarmonyX 2.9.0, Unity modules
dotnet build CRIXOptimizer.csproj -c Release -p:RefMode=Lib -p:ContinuousIntegrationBuild=true
tools/package-release.sh                                # build + tests + doc generation + reference check + ZIP
```

## Reference verification (`tools/RefCheck`)

Every type and member reference in the DLL was resolved with Mono.Cecil against each reference set:

| Reference set | Type refs | Member refs | Result |
|---|---|---|---|
| BepInEx 5.4.21 + HarmonyX 2.9.0 + Unity 2021.3.33 modules + Mono 4.5 BCL | 298 | 1314 | all resolve |
| BepInEx 5.4.23.5 (built from the `v5-lts` branch, commit `f4c1b1103a32884b7440d9681c60cc5d619f284f`) + same Unity modules | 298 | 1314 | all resolve |

## Unity 6 API check (`tools/check_unity_api.py`)

Gorilla Tag runs Unity 6 (a user's system report from a 1.1.0 preview shows Unity 6000.2.9f1; the official custom-map
project pins 6000.1.9f1). Unity's reference assemblies for Unity 6 are not on nuget.org, so every Unity type and member
the DLL references (listed with `monodis`) was compared with Unity's published C# source (UnityCsReference) at both
versions. Nested types and delegates (e.g. `PlayerLoopSystem.UpdateFunction`) are included; `UnityAction<T0,T1>` is
generated outside UnityCsReference and is accepted because Unity's own `SceneManager.sceneLoaded` is declared with it.
A run against a copy of the source without the PlayerLoop folder reports all 11 PlayerLoop references missing, so the
check does catch absent APIs.

| Unity source | Unity type / member references | Result |
|---|---|---|
| UnityCsReference tag `6000.1.9f1` | 105 / 259 | all declared |
| UnityCsReference tag `6000.2.9f1` (`release/logs/unity-api-check.log`) | 105 / 259 | all declared |

Obsolete but still present (used only as fallbacks): `QualitySettings.masterTextureLimit` (when
`globalTextureMipmapLimit` is missing), `GraphicsSettings.renderPipelineAsset` (when `currentRenderPipeline` fails) and
`Resolution.refreshRate` (desktop-monitor fallback when the VR runtime reports no refresh rate). The script's
`AddComponent` line refers to the obsolete string overload; CRIX uses the generic `AddComponent<T>()`.
This checks names, not binary signatures. Running `tools/RefCheck` against a real install's
`Gorilla Tag_Data/Managed` and `BepInEx/core` folders remains the strongest offline check (see BUILDING.md).

## Release package

`release/CRIXOptimizer-1.1.0.zip` contains:

- `install/BepInEx/plugins/CRIXOptimizer/CRIXOptimizer.dll` (+ README, LICENSE): drop into the game folder
- `source/`: complete corresponding source (GPL-3.0 §6), excluding generated `bin/`, `obj/`, `lib/`
- `README.md`, `INSTALLATION.md`, `BUILDING.md`, `TROUBLESHOOTING.md`, `LICENSES.md`, `CHANGELOG.md`, `LICENSE`, `docs/`
- `logs/`: build, test, doc-generation, reference-check and Unity API check logs
- `SHA256SUMS.txt`: checksums of every file in the package

No game files, no BepInEx/Harmony binaries and no Unity assemblies are included.

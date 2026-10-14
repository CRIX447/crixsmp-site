# Licenses and attribution

## CRIX OPTIMIZER

Copyright (C) 2026 CRIX OPTIMIZER contributors.

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public
License version 3** as published by the Free Software Foundation, or (at your option) any later version. It is
distributed WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR
PURPOSE. See [LICENSE](LICENSE) for the full text.

Because CRIX is a derivative of a GPL-3.0 work, **any distribution of the DLL must be accompanied by (or offer) the
complete corresponding source code** under the same license. The release ZIP contains it in `source/`.

## Potassium (upstream)

CRIX OPTIMIZER is based on **Potassium 1.0.0** - https://github.com/niko-tar-gz/Potassium (tag `v1.0.0-release`,
commit `262ba00c6ea492e7d08d03f5055a784ed4b32fb6`), Copyright (C) 2026 Potassium contributors, licensed under the
GNU GPL v3.0. The uploaded Potassium-1.0.0-release archive was verified to be byte-identical to that tag.

Files containing code derived from Potassium (each carries a notice in its header):

| CRIX file | Potassium source |
|---|---|
| `CPU/UpdateThrottlePatches.cs` | `Patches/TextureTransitionPatch.cs`, `Patches/VoiceLoudnessReactorPatch.cs`, `Patches/ProximityPatch.cs` |
| `CPU/EntityLeavePatch.cs` | `Patches/InactiveEntityLeavePatch.cs` |
| `CPU/ScoreboardPatches.cs` | `Patches/RoomJoinPatch.cs`, `Patches/ScoreboardRefreshPatch.cs`, `RoomJoinShit.cs` |
| `CPU/RigPoolPatch.cs` | `Patches/RigPoolPatch.cs` |
| `CPU/SharedBlocksPatch.cs` | `Patches/BlocksPatch.cs`, `MonkeBlocksShit.cs` |
| `CPU/MapSanitizerPatch.cs` | `Patches/MapSanitizerPatch.cs` |
| `Memory/GcPolicyPatches.cs` | `Patches/RoomTransitionPatch.cs`, `Patches/UnusedAssetUnloadPatch.cs` |
| `GPU/CosmeticCullingPatch.cs` | `Patches/CosmeticsThrottlerPatch.cs` |
| `Utilities/PatchSet.cs` | `HarmonyPatches.cs` |
| `Configuration/OptimizationSettings.cs`, `Configuration/SettingCatalog.cs` (stutter-fix defaults and descriptions) | `Plugin.cs` |

All modifications are listed in [CHANGELOG.md](CHANGELOG.md) (GPL-3.0 section 5a). The Potassium logo is not used.
Every other source file, including everything added in 1.1.0 (`Streaming/ObsWebSocketClient.cs`,
`Streaming/StreamlabsStatusClient.cs`, `Core/MiniJson.cs`, `Core/CaptureState.cs`, `Adaptive/ResolutionProbe.cs`,
`GPU/DesktopCameraModule.cs`, `Diagnostics/CameraInventory.cs`, `Compatibility/SakuraaSupport.cs`,
`Configuration/ProfilePresetsV1.cs`, `tools/check_unity_api.py`), is original CRIX code under the same license.
Potassium's README asks contributors not to submit large AI-written changes without understanding them. CRIX is a
separate project, not a contribution to Potassium.

## Build and runtime dependencies (not included in the release)

| Component | License | How CRIX uses it |
|---|---|---|
| [BepInEx 5](https://github.com/BepInEx/BepInEx) | MIT | Plugin framework the user installs. Compile-time reference only; for builds without a game install, `BepInEx.dll` is compiled from the official source (`tools/setup-references.sh`). Not redistributed. |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | MIT | Runtime patching library shipped with BepInEx. Compile-time reference only. Not redistributed. |
| [MonoMod](https://github.com/MonoMod/MonoMod), [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | HarmonyX dependencies, used only to run the test suite and `tools/RefCheck`. Not redistributed. |
| Unity engine module assemblies (`UnityEngine.Modules` NuGet package 2021.3.33, or the game's own `Managed` folder) | Unity's terms | Compile-time references only. Never redistributed. |
| [UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference) (Unity's published C# source) | Unity Reference-Only License | Read by `tools/check_unity_api.py` to check API names against the game's Unity version (6000.1.9f1). Not copied into CRIX and not redistributed. |
| OBS Studio's obs-websocket protocol, Streamlabs Desktop's local API | (protocols) | CRIX implements its own read-only clients from the public protocol documentation; no code from either project is included. |
| Gorilla Tag | Proprietary (Another Axiom) | **No game files are included or required to build.** The test suite uses an original stand-in assembly written for testing; it contains no game code. |

## Trademarks

Gorilla Tag is a trademark of Another Axiom. Virtual Desktop, OBS Studio, Streamlabs, Discord, Spotify, ReShade,
SteamVR, Meta Quest and Sakuraa are trademarks of their respective owners. They are named only to describe compatibility. CRIX OPTIMIZER is not affiliated
with or endorsed by any of them.

# Profile reference

Generated from the source (`Configuration/ProfilePresets.cs`, `Configuration/SettingCatalog.cs`). These are the **built-in starting values**. Every value can be edited per profile in `BepInEx/config/com.crix.crixoptimizer.profiles.cfg` (the `Custom` profile lives in the `[Custom]` section of `com.crix.crixoptimizer.cfg`). None of them is claimed to be optimal for every PC, GPU, headset or network.

Conventions: `-1` / `Unchanged` = CRIX does not touch the setting. *Limit* settings are upper bounds: if the game's own value is already cheaper, the game's value is kept. `RenderScale` 0 = unchanged.

| Setting | Balanced | MaximumPerformance | HighRefresh | VirtualDesktop | VD.Performance | VD.Quality | VD.Streaming | OBSStreaming | DiscordCall | ReShadeCompatible | MultiMod |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `RenderScale` | 0 | **0.8** | 0 | 0 | **0.85** | 0 | 0 | 0 | 0 | 0 | 0 |
| `MaxEyeMegapixels` | 8 | **5** | **6** | **7** | **5** | **10** | **0** | **0** | 8 | **0** | **0** |
| `ShadowQuality` | Unchanged | **HardOnly** | Unchanged | Unchanged | **HardOnly** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `ShadowDistanceLimit` | 50 | **25** | **20** | **40** | **25** | **-1** | **30** | **30** | **35** | 50 | **-1** |
| `ShadowResolution` | Unchanged | **Medium** | Unchanged | Unchanged | **Medium** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `ShadowCascadesLimit` | 2 | **1** | **1** | 2 | **1** | **-1** | 2 | 2 | 2 | 2 | **-1** |
| `MsaaSamples` | -1 | **2** | **2** | -1 | **2** | -1 | -1 | -1 | -1 | -1 | -1 |
| `AnisotropicFiltering` | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `LodBiasLimit` | -1 | **0.7** | **0.8** | -1 | **0.75** | -1 | -1 | -1 | -1 | -1 | -1 |
| `MinimumMaxLodLevel` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `PixelLightLimit` | 2 | **1** | **1** | 2 | **1** | **-1** | **1** | **1** | 2 | 2 | **-1** |
| `TextureMipLimit` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `RealtimeReflectionProbes` | Off | Off | Off | Off | Off | **Unchanged** | Off | Off | Off | Off | **Unchanged** |
| `SoftParticles` | Unchanged | **Off** | **Off** | Unchanged | **Off** | Unchanged | Unchanged | **Off** | **Off** | Unchanged | Unchanged |
| `ParticleRaycastBudgetLimit` | -1 | **64** | **64** | -1 | **64** | -1 | -1 | **128** | -1 | -1 | -1 |
| `SkinWeights` | Unchanged | **TwoBones** | **TwoBones** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `CameraHdr` | Unchanged | **Off** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `EnsureOcclusionMesh` | on | on | on | on | on | on | on | on | on | **off** | **off** |
| `DesktopMirror` | Unchanged | **LeftEye** | Unchanged | **LeftEye** | **LeftEye** | **LeftEye** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `DesktopCamera` | Unchanged | Unchanged | **OffWhenNotStreaming** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `AsyncUploadTimeSliceMs` | 1 | **2** | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | **-1** |
| `BackgroundLoading` | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | BelowNormal | **Unchanged** |
| `IncrementalGcSliceMs` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `DeferSharedBlocks` | on | on | on | on | on | on | on | on | on | on | **off** |
| `StaggerRigPool` | on | on | on | on | on | on | on | on | on | on | **off** |
| `InitialRigPoolSize` | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 |
| `StaggerScoreboardCreation` | on | on | on | on | on | on | on | on | on | on | on |
| `StaggerScoreboardRefresh` | on | on | on | on | on | on | on | on | on | on | **off** |
| `SkipInactiveEntityLeaveScans` | on | on | on | on | on | on | on | on | on | on | on |
| `RoomLeaveGc` | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip |
| `SkipTimedAssetUnloads` | on | on | on | on | on | on | on | on | on | on | on |
| `MapSanitizer` | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups |
| `TextureTransitionRate` | 30 | **20** | **20** | 30 | **20** | 30 | **24** | **24** | **24** | 30 | 30 |
| `VoiceVisualRate` | 30 | **20** | **20** | 30 | **20** | 30 | **24** | **24** | **24** | 30 | 30 |
| `ProximityRate` | 30 | **20** | **20** | 30 | **20** | 30 | **24** | **24** | **24** | 30 | 30 |
| `ReduceDistantCosmetics` | on | on | on | on | on | on | on | on | on | on | on |
| `CosmeticFullDetailDistance` | 4.5 | **3.5** | **3.5** | 4.5 | 4.5 | 4.5 | 4.5 | **4** | 4.5 | 4.5 | 4.5 |
| `CosmeticMaximumDistance` | 8 | **6** | **6** | 8 | 8 | 8 | 8 | **7** | 8 | 8 | 8 |
| `CosmeticFullDetailCount` | 5 | **4** | **4** | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 |
| `CosmeticMaximumCount` | 10 | **8** | **8** | 10 | 10 | 10 | 10 | 10 | 10 | 10 | 10 |
| `AdaptiveMayChangeRenderScale` | on | on | **off** | on | on | on | **off** | **off** | on | **off** | on |
| `AdaptiveMayChangeShadows` | on | on | on | on | on | on | on | on | on | on | on |
| `AdaptiveMayChangeLod` | on | on | on | on | on | on | on | on | on | on | on |
| `AdaptiveMayChangeCosmetics` | on | on | on | on | on | on | on | on | on | on | on |
| `AdaptiveGpuHeadroomPercent` | 0 | 0 | **10** | **10** | **15** | **5** | **20** | **20** | **10** | 0 | 0 |

Bold = differs from Balanced.

## Setting descriptions

- **`RenderScale`** (number) - [GPU] VR eye-texture resolution scale (0 = leave the game/runtime value; 0.5-2.0 = set). Under URP it is set through the URP asset's render scale, which URP ignores within 0.05 of 1.0. Recreates eye textures when changed (brief hitch). Range 0 to 2.
- **`MaxEyeMegapixels`** (number) - [GPU] Upper limit for the pixels CRIX lets the game render per eye, in megapixels (0 = no limit). VR runtimes set the eye resolution (Virtual Desktop's quality preset, Meta Quest Link's render resolution, SteamVR's slider); when that is far above what the GPU can draw, CRIX lowers the game's render scale until one eye is at most this many megapixels (a Quest 3 at default settings renders about 4.6 MP per eye). Under URP this also sets the desktop window's resolution, so streaming profiles leave it off. Range 0 to 50.
- **`ShadowQuality`** (choice) - [GPU] Upper limit for shadow quality (built-in pipeline). HardOnly removes soft-shadow filtering, Disable turns realtime shadows off. Values: Unchanged, HardOnly, Disable.
- **`ShadowDistanceLimit`** (number) - [GPU] Maximum realtime shadow distance in metres; kept if the game already uses less. -1 = unchanged. Range -1 to 1000.
- **`ShadowResolution`** (choice) - [GPU] Upper limit for shadow-map resolution (built-in pipeline only; no effect under URP). Values: Unchanged, Low, Medium, High.
- **`ShadowCascadesLimit`** (integer) - [GPU] Maximum shadow cascades (1, 2 or 4; built-in pipeline or URP asset). Kept if the game already uses fewer. -1 = unchanged. Range -1 to 4.
- **`MsaaSamples`** (integer) - [GPU] MSAA samples (0, 2, 4, 8). -1 = unchanged. A cap on both pipelines (never raised). Under URP it applies at once and reallocates the eye textures (brief hitch); with the built-in pipeline some VR runtimes only pick it up after a restart. The dashboard shows the eye-texture MSAA in use. Range -1 to 8.
- **`AnisotropicFiltering`** (choice) - [GPU] Anisotropic texture filtering mode. Values: Unchanged, Disable, Enable, ForceEnable.
- **`LodBiasLimit`** (number) - [GPU] Maximum LOD bias (lower = simpler meshes sooner). -1 = unchanged. Range -1 to 10.
- **`MinimumMaxLodLevel`** (integer) - [GPU] Skip the N most detailed LOD levels (0-7). -1 = unchanged. Range -1 to 7.
- **`PixelLightLimit`** (integer) - [GPU] Maximum per-pixel lights (built-in pipeline) or additional lights per object (URP asset). Kept if the game already uses fewer. -1 = unchanged. Range -1 to 16.
- **`TextureMipLimit`** (integer) - [GPU/VRAM] Minimum texture mip limit: 0 full, 1 half, 2 quarter resolution. Frees VRAM; re-uploads every texture when changed. -1 = unchanged. Range -1 to 3.
- **`RealtimeReflectionProbes`** (choice) - [GPU] Realtime reflection probe updates (Off saves GPU time; baked reflections remain). Values: Unchanged, On, Off.
- **`SoftParticles`** (choice) - [GPU] Soft particles (Off avoids depth-texture sampling for particles). Built-in pipeline only; no effect under URP. Values: Unchanged, On, Off.
- **`ParticleRaycastBudgetLimit`** (integer) - [CPU] Maximum particle-collision raycasts per frame (visual only). -1 = unchanged. Range -1 to 4096.
- **`SkinWeights`** (choice) - [CPU/GPU] Upper limit for bones per vertex in skinned meshes. Values: Unchanged, OneBone, TwoBones, FourBones.
- **`CameraHdr`** (choice) - [GPU] HDR rendering on the main camera. Off can reduce colour-buffer bandwidth when HDR is used; no effect if the URP asset already has HDR off; can change the look. Values: Unchanged, On, Off.
- **`EnsureOcclusionMesh`** (true/false) - [GPU] Make sure the XR occlusion (hidden-area) mesh is enabled so pixels outside the lenses are not shaded.
- **`DesktopMirror`** (choice) - [GPU] The headset view drawn into the desktop window (XR mirror). In Gorilla Tag the game's third-person camera draws over it, so it is only visible while that camera is off (DesktopCamera). LeftEye is cheaper than SideBySide; None makes the window black when the camera is off. Values: Unchanged, LeftEye, RightEye, SideBySide, None.
- **`DesktopCamera`** (choice) - [GPU/CPU] The game's third-person camera renders the whole scene a second time to the desktop window. OffWhenNotStreaming switches it off unless OBS/Streamlabs records or streams (the window then shows the headset view); Off always. Only while a VR headset is active; rendering only, restored with the profile. Discord screen sharing is not detected: use Unchanged if you share the game window there. Values: Unchanged, OffWhenNotStreaming, Off.
- **`AsyncUploadTimeSliceMs`** (integer) - [CPU] Milliseconds per frame for background texture/mesh uploads (lower = fewer hitches while maps/cosmetics load, slower loading). -1 = unchanged. Range -1 to 33.
- **`BackgroundLoading`** (choice) - [CPU] Priority of background asset loading: Unity's time per frame for finishing loads (Low 2 ms, BelowNormal 4 ms, Normal 10 ms, High 50 ms). Lower = smoother frames during loads, slower loads. BelowNormal is Unity's own default on PC and the game does not change it, so in the presets it mostly keeps that value in place. Values: Unchanged, Low, BelowNormal, Normal, High.
- **`IncrementalGcSliceMs`** (number) - [CPU] Incremental GC time slice in ms (only if the game uses incremental GC). Smaller slices = smaller GC spikes, but too small can fall back to full collections. -1 = unchanged. Range -1 to 20.
- **`DeferSharedBlocks`** (true/false) - [Stutter] Do not initialise Shared Blocks until the player enters its zone. (Potassium)
- **`StaggerRigPool`** (true/false) - [Stutter] Create the remote-player rig pool over several frames instead of all at startup; missing rigs are created on demand. (Potassium)
- **`InitialRigPoolSize`** (integer) - [Stutter] Remote rigs created immediately when the pool is staggered. (Potassium) Range 1 to 20.
- **`StaggerScoreboardCreation`** (true/false) - [Stutter] Create local scoreboards over successive frames after joining a room. (Potassium)
- **`StaggerScoreboardRefresh`** (true/false) - [Stutter] Refresh scoreboards one per frame when players join/leave. (Potassium)
- **`SkipInactiveEntityLeaveScans`** (true/false) - [Stutter] Skip unused full entity scans done by inactive zones when a player leaves. (Potassium)
- **`RoomLeaveGc`** (choice) - [Stutter] Forced garbage collection on room leave: GameDefault, Skip (Potassium) or Incremental (time-sliced, only if the game uses incremental GC). Values: GameDefault, Skip, Incremental.
- **`SkipTimedAssetUnloads`** (true/false) - [Stutter] Prevent scene components from forcing synchronous unused-asset sweeps during gameplay. May use more memory. (Potassium)
- **`MapSanitizer`** (choice) - [Maps] Custom-map sanitizer optimisation: Off, SafeLookups (keeps the game's rules, only speeds up allow-list lookups; in the current game that is only the layer check, a small saving) or FullReplacement (Potassium's re-implementation; bypasses any rule added by later game updates). Values: Off, SafeLookups, FullReplacement.
- **`TextureTransitionRate`** (number) - [Visual] Texture-transition updates per second (0 = game rate). (Potassium, frame-staggered) Range 0 to 240.
- **`VoiceVisualRate`** (number) - [Visual] Voice-reactive cosmetic updates per second when they use no smoothing (0 = game rate). (Potassium, frame-staggered) Range 0 to 240.
- **`ProximityRate`** (number) - [Visual] Cosmetic environment-proximity checks per second (0 = game rate). (Potassium, frame-staggered) Range 0 to 240.
- **`ReduceDistantCosmetics`** (true/false) - [Visual] Locally cull distant remote-player cosmetics. Does not affect networking or what others see. (Potassium)
- **`CosmeticFullDetailDistance`** (number) - [Visual] Distance (m) within which remote cosmetics are always drawn. (Potassium) Range 0 to 100.
- **`CosmeticMaximumDistance`** (number) - [Visual] Maximum distance (m) for remote cosmetics. (Potassium) Range 0 to 200.
- **`CosmeticFullDetailCount`** (integer) - [Visual] Players always drawn with full cosmetics. (Potassium) Range 0 to 20.
- **`CosmeticMaximumCount`** (integer) - [Visual] Maximum players drawn with cosmetics. (Potassium) Range 0 to 20.
- **`AdaptiveMayChangeRenderScale`** (true/false) - [Adaptive] Allow adaptive mode to change render scale while this profile is active.
- **`AdaptiveMayChangeShadows`** (true/false) - [Adaptive] Allow adaptive mode to change shadow distance and pixel lights.
- **`AdaptiveMayChangeLod`** (true/false) - [Adaptive] Allow adaptive mode to change LOD bias.
- **`AdaptiveMayChangeCosmetics`** (true/false) - [Adaptive] Allow adaptive mode to reduce distant remote-player cosmetic detail and cosmetic update rates (CPU savings in busy lobbies).
- **`AdaptiveGpuHeadroomPercent`** (number) - [Adaptive] Share of the frame budget to keep free on the GPU (for video encoders/compositor). Only used when the VR runtime reports GPU time. Range 0 to 40.

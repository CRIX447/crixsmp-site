# Profile reference

Generated from the source (`Configuration/ProfilePresets.cs`, `Configuration/SettingCatalog.cs`). These are the **built-in starting values**. Every value can be edited per profile in `BepInEx/config/com.crix.crixoptimizer.profiles.cfg` (the `Custom` profile lives in the `[Custom]` section of `com.crix.crixoptimizer.cfg`). None of them is claimed to be optimal for every PC, GPU, headset or network.

Conventions: `-1` / `Unchanged` = CRIX does not touch the setting. *Limit* settings are upper bounds: if the game's own value is already cheaper, the game's value is kept. `RenderScale` 0 = unchanged.

| Setting | Balanced | MaximumPerformance | VirtualDesktop | VD.Performance | VD.Quality | VD.Streaming | OBSStreaming | DiscordCall | ReShadeCompatible | MultiMod |
|---|---|---|---|---|---|---|---|---|---|---|
| `RenderScale` | 0 | **0.8** | 0 | **0.85** | 0 | 0 | 0 | 0 | 0 | 0 |
| `ShadowQuality` | Unchanged | **HardOnly** | Unchanged | **HardOnly** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `ShadowDistanceLimit` | 60 | **25** | **40** | **25** | **-1** | **35** | **40** | **40** | **50** | **-1** |
| `ShadowResolution` | Unchanged | **Medium** | Unchanged | **Medium** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `ShadowCascadesLimit` | -1 | **1** | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `MsaaSamples` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `AnisotropicFiltering` | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `LodBiasLimit` | -1 | **0.7** | -1 | **0.75** | -1 | -1 | -1 | -1 | -1 | -1 |
| `MinimumMaxLodLevel` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `PixelLightLimit` | -1 | **1** | **2** | **1** | -1 | **2** | **2** | **2** | **2** | -1 |
| `TextureMipLimit` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `RealtimeReflectionProbes` | Unchanged | **Off** | **Off** | **Off** | Unchanged | **Off** | **Off** | **Off** | **Off** | Unchanged |
| `SoftParticles` | Unchanged | **Off** | Unchanged | **Off** | Unchanged | Unchanged | Unchanged | **Off** | Unchanged | Unchanged |
| `ParticleRaycastBudgetLimit` | -1 | **64** | -1 | **64** | -1 | -1 | -1 | -1 | -1 | -1 |
| `SkinWeights` | Unchanged | **TwoBones** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `CameraHdr` | Unchanged | **Off** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `EnsureOcclusionMesh` | off | **on** | **on** | **on** | **on** | **on** | **on** | **on** | off | off |
| `DesktopMirror` | Unchanged | **LeftEye** | **LeftEye** | **LeftEye** | **LeftEye** | Unchanged | Unchanged | Unchanged | Unchanged | Unchanged |
| `AsyncUploadTimeSliceMs` | -1 | **2** | **1** | **1** | **1** | **1** | **1** | **1** | **1** | -1 |
| `BackgroundLoading` | Unchanged | **BelowNormal** | **BelowNormal** | **BelowNormal** | **BelowNormal** | **BelowNormal** | **BelowNormal** | **BelowNormal** | **BelowNormal** | Unchanged |
| `IncrementalGcSliceMs` | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 |
| `DeferSharedBlocks` | on | on | on | on | on | on | on | on | on | **off** |
| `StaggerRigPool` | on | on | on | on | on | on | on | on | on | **off** |
| `InitialRigPoolSize` | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 |
| `StaggerScoreboardCreation` | on | on | on | on | on | on | on | on | on | on |
| `StaggerScoreboardRefresh` | on | on | on | on | on | on | on | on | on | **off** |
| `SkipInactiveEntityLeaveScans` | on | on | on | on | on | on | on | on | on | on |
| `RoomLeaveGc` | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip | Skip |
| `SkipTimedAssetUnloads` | on | on | on | on | on | on | on | on | on | on |
| `MapSanitizer` | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups | SafeLookups |
| `TextureTransitionRate` | 30 | **20** | 30 | **20** | 30 | 30 | 30 | **24** | 30 | 30 |
| `VoiceVisualRate` | 30 | **20** | 30 | **20** | 30 | 30 | 30 | **24** | 30 | 30 |
| `ProximityRate` | 30 | **20** | 30 | **20** | 30 | 30 | 30 | **24** | 30 | 30 |
| `ReduceDistantCosmetics` | on | on | on | on | on | on | on | on | on | on |
| `CosmeticFullDetailDistance` | 4.5 | **3.5** | 4.5 | 4.5 | 4.5 | 4.5 | 4.5 | 4.5 | 4.5 | 4.5 |
| `CosmeticMaximumDistance` | 8 | **6** | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 |
| `CosmeticFullDetailCount` | 5 | **4** | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 |
| `CosmeticMaximumCount` | 10 | **8** | 10 | 10 | 10 | 10 | 10 | 10 | 10 | 10 |
| `AdaptiveMayChangeRenderScale` | on | on | on | on | on | on | on | on | **off** | on |
| `AdaptiveMayChangeShadows` | on | on | on | on | on | on | on | on | on | on |
| `AdaptiveMayChangeLod` | on | on | on | on | on | on | on | on | on | on |
| `AdaptiveGpuHeadroomPercent` | 0 | 0 | **10** | **15** | **5** | **15** | **10** | **5** | 0 | 0 |

Bold = differs from Balanced.

## Setting descriptions

- **`RenderScale`** (number) - [GPU] VR eye-texture resolution scale (0 = leave the game/runtime value; 0.5-2.0 = set). Recreates eye textures when changed (brief hitch). Range 0 to 2.
- **`ShadowQuality`** (choice) - [GPU] Upper limit for shadow quality (built-in pipeline). HardOnly removes soft-shadow filtering, Disable turns realtime shadows off. Values: Unchanged, HardOnly, Disable.
- **`ShadowDistanceLimit`** (number) - [GPU] Maximum realtime shadow distance in metres; kept if the game already uses less. -1 = unchanged. Range -1 to 1000.
- **`ShadowResolution`** (choice) - [GPU] Upper limit for shadow-map resolution. Values: Unchanged, Low, Medium, High.
- **`ShadowCascadesLimit`** (integer) - [GPU] Maximum shadow cascades (1, 2 or 4). -1 = unchanged. Range -1 to 4.
- **`MsaaSamples`** (integer) - [GPU] MSAA samples (0, 2, 4, 8). -1 = unchanged. Applied once; some VR runtimes only pick it up after a restart - the dashboard shows the eye-texture MSAA actually in use. Range -1 to 8.
- **`AnisotropicFiltering`** (choice) - [GPU] Anisotropic texture filtering mode. Values: Unchanged, Disable, Enable, ForceEnable.
- **`LodBiasLimit`** (number) - [GPU] Maximum LOD bias (lower = simpler meshes sooner). -1 = unchanged. Range -1 to 10.
- **`MinimumMaxLodLevel`** (integer) - [GPU] Skip the N most detailed LOD levels (0-7). -1 = unchanged. Range -1 to 7.
- **`PixelLightLimit`** (integer) - [GPU] Maximum per-pixel lights (built-in forward rendering). -1 = unchanged. Range -1 to 16.
- **`TextureMipLimit`** (integer) - [GPU/VRAM] Minimum texture mip limit: 0 full, 1 half, 2 quarter resolution. Frees VRAM; re-uploads every texture when changed. -1 = unchanged. Range -1 to 3.
- **`RealtimeReflectionProbes`** (choice) - [GPU] Realtime reflection probe updates (Off saves GPU time; baked reflections remain). Values: Unchanged, On, Off.
- **`SoftParticles`** (choice) - [GPU] Soft particles (Off avoids depth-texture sampling for particles). Values: Unchanged, On, Off.
- **`ParticleRaycastBudgetLimit`** (integer) - [CPU] Maximum particle-collision raycasts per frame (visual only). -1 = unchanged. Range -1 to 4096.
- **`SkinWeights`** (choice) - [CPU/GPU] Upper limit for bones per vertex in skinned meshes. Values: Unchanged, OneBone, TwoBones, FourBones.
- **`CameraHdr`** (choice) - [GPU] HDR rendering on the main camera (Off halves colour-buffer bandwidth; can change the look if the game relies on HDR). Values: Unchanged, On, Off.
- **`EnsureOcclusionMesh`** (true/false) - [GPU] Make sure the XR occlusion (hidden-area) mesh is enabled so pixels outside the lenses are not shaded.
- **`DesktopMirror`** (choice) - [GPU] What the desktop window shows in VR. LeftEye is cheaper than SideBySide; None makes the window black. OBS captures this window - keep Unchanged/LeftEye when recording. Values: Unchanged, LeftEye, RightEye, SideBySide, None.
- **`AsyncUploadTimeSliceMs`** (integer) - [CPU] Milliseconds per frame for background texture/mesh uploads (lower = fewer hitches while maps/cosmetics load, slower loading). -1 = unchanged. Range -1 to 33.
- **`BackgroundLoading`** (choice) - [CPU] Priority of background asset loading (lower = smoother frames during loads, slower loads). Values: Unchanged, Low, BelowNormal, Normal, High.
- **`IncrementalGcSliceMs`** (number) - [CPU] Incremental GC time slice in ms (only if the game uses incremental GC). Smaller slices = smaller GC spikes, but too small can fall back to full collections. -1 = unchanged. Range -1 to 20.
- **`DeferSharedBlocks`** (true/false) - [Stutter] Do not initialise Shared Blocks until the player enters its zone. (Potassium)
- **`StaggerRigPool`** (true/false) - [Stutter] Create the remote-player rig pool over several frames instead of all at startup; missing rigs are created on demand. (Potassium)
- **`InitialRigPoolSize`** (integer) - [Stutter] Remote rigs created immediately when the pool is staggered. (Potassium) Range 1 to 20.
- **`StaggerScoreboardCreation`** (true/false) - [Stutter] Create local scoreboards over successive frames after joining a room. (Potassium)
- **`StaggerScoreboardRefresh`** (true/false) - [Stutter] Refresh scoreboards one per frame when players join/leave. (Potassium)
- **`SkipInactiveEntityLeaveScans`** (true/false) - [Stutter] Skip unused full entity scans done by inactive zones when a player leaves. (Potassium)
- **`RoomLeaveGc`** (choice) - [Stutter] Forced garbage collection on room leave: GameDefault, Skip (Potassium) or Incremental (time-sliced, only if the game uses incremental GC). Values: GameDefault, Skip, Incremental.
- **`SkipTimedAssetUnloads`** (true/false) - [Stutter] Prevent scene components from forcing synchronous unused-asset sweeps during gameplay. May use more memory. (Potassium)
- **`MapSanitizer`** (choice) - [Maps] Custom-map sanitizer optimisation: Off, SafeLookups (keeps the game's rules, only speeds up allow-list lookups) or FullReplacement (Potassium's re-implementation; bypasses any rule added by later game updates). Values: Off, SafeLookups, FullReplacement.
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
- **`AdaptiveGpuHeadroomPercent`** (number) - [Adaptive] Share of the frame budget to keep free on the GPU (for video encoders/compositor). Only used when the VR runtime reports GPU time. Range 0 to 40.

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (Plugin.cs config descriptions), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CRIXOptimizer.Configuration
{
    /// <summary>Descriptions and ranges for every OptimizationSettings field (used for the BepInEx config files).</summary>
    public static class SettingCatalog
    {
        public sealed class Info
        {
            public string Description;
            public double Min;
            public double Max;
            public bool HasRange;
        }

        private static Info D(string description) => new Info { Description = description };
        private static Info R(string description, double min, double max) => new Info { Description = description, Min = min, Max = max, HasRange = true };

        public static readonly Dictionary<string, Info> Fields = new Dictionary<string, Info>(StringComparer.Ordinal)
        {
            // Rendering
            { nameof(OptimizationSettings.RenderScale), R("[GPU] VR eye-texture resolution scale (0 = leave the game/runtime value; 0.5-2.0 = set). Under URP it is set through the URP asset's render scale, which URP ignores within 0.05 of 1.0. Recreates eye textures when changed (brief hitch).", 0, 2) },
            { nameof(OptimizationSettings.ShadowQuality), D("[GPU] Upper limit for shadow quality (built-in pipeline). HardOnly removes soft-shadow filtering, Disable turns realtime shadows off.") },
            { nameof(OptimizationSettings.MaxEyeMegapixels), R("[GPU] Upper limit for the pixels CRIX lets the game render per eye, in megapixels (0 = no limit). " +
                "VR runtimes set the eye resolution (Virtual Desktop's quality preset, Meta Quest Link's render resolution, SteamVR's slider); when that is far above what the GPU can draw, " +
                "CRIX lowers the game's render scale until one eye is at most this many megapixels (a Quest 3 at default settings renders about 4.6 MP per eye). " +
                "Under URP this also sets the desktop window's resolution, so streaming profiles leave it off.", 0, 50) },
            { nameof(OptimizationSettings.ShadowDistanceLimit), R("[GPU] Maximum realtime shadow distance in metres; kept if the game already uses less. -1 = unchanged.", -1, 1000) },
            { nameof(OptimizationSettings.ShadowResolution), D("[GPU] Upper limit for shadow-map resolution (built-in pipeline only; no effect under URP).") },
            { nameof(OptimizationSettings.ShadowCascadesLimit), R("[GPU] Maximum shadow cascades (1, 2 or 4; built-in pipeline or URP asset). Kept if the game already uses fewer. -1 = unchanged.", -1, 4) },
            { nameof(OptimizationSettings.MsaaSamples), R("[GPU] MSAA samples (0, 2, 4, 8). -1 = unchanged. A cap on both pipelines (never raised). Under URP it applies at once and reallocates the eye textures (brief hitch); with the built-in pipeline some VR runtimes only pick it up after a restart. The dashboard shows the eye-texture MSAA in use.", -1, 8) },
            { nameof(OptimizationSettings.AnisotropicFiltering), D("[GPU] Anisotropic texture filtering mode.") },
            { nameof(OptimizationSettings.LodBiasLimit), R("[GPU] Maximum LOD bias (lower = simpler meshes sooner). -1 = unchanged.", -1, 10) },
            { nameof(OptimizationSettings.MinimumMaxLodLevel), R("[GPU] Skip the N most detailed LOD levels (0-7). -1 = unchanged.", -1, 7) },
            { nameof(OptimizationSettings.PixelLightLimit), R("[GPU] Maximum per-pixel lights (built-in pipeline) or additional lights per object (URP asset). Kept if the game already uses fewer. -1 = unchanged.", -1, 16) },
            { nameof(OptimizationSettings.TextureMipLimit), R("[GPU/VRAM] Minimum texture mip limit: 0 full, 1 half, 2 quarter resolution. Frees VRAM; re-uploads every texture when changed. -1 = unchanged.", -1, 3) },
            { nameof(OptimizationSettings.RealtimeReflectionProbes), D("[GPU] Realtime reflection probe updates (Off saves GPU time; baked reflections remain).") },
            { nameof(OptimizationSettings.SoftParticles), D("[GPU] Soft particles (Off avoids depth-texture sampling for particles). Built-in pipeline only; no effect under URP.") },
            { nameof(OptimizationSettings.ParticleRaycastBudgetLimit), R("[CPU] Maximum particle-collision raycasts per frame (visual only). -1 = unchanged.", -1, 4096) },
            { nameof(OptimizationSettings.SkinWeights), D("[CPU/GPU] Upper limit for bones per vertex in skinned meshes.") },
            { nameof(OptimizationSettings.CameraHdr), D("[GPU] HDR rendering on the main camera. Off can reduce colour-buffer bandwidth when HDR is used; no effect if the URP asset already has HDR off; can change the look.") },
            { nameof(OptimizationSettings.EnsureOcclusionMesh), D("[GPU] Make sure the XR occlusion (hidden-area) mesh is enabled so pixels outside the lenses are not shaded.") },
            { nameof(OptimizationSettings.DesktopCamera), D("[GPU/CPU] The game's third-person camera renders the whole scene a second time to the desktop window. " +
                "OffWhenNotStreaming switches it off unless OBS/Streamlabs records or streams (the window then shows the headset view); Off always. " +
                "Only while a VR headset is active; rendering only, restored with the profile. Discord screen sharing is not detected: use Unchanged if you share the game window there.") },
            { nameof(OptimizationSettings.DesktopMirror), D("[GPU] The headset view drawn into the desktop window (XR mirror). In Gorilla Tag the game's third-person camera draws over it, so it is only visible while that camera is off (DesktopCamera). LeftEye is cheaper than SideBySide; None makes the window black when the camera is off.") },
            // Frame pacing
            { nameof(OptimizationSettings.AsyncUploadTimeSliceMs), R("[CPU] Milliseconds per frame for background texture/mesh uploads (lower = fewer hitches while maps/cosmetics load, slower loading). -1 = unchanged.", -1, 33) },
            { nameof(OptimizationSettings.BackgroundLoading), D("[CPU] Priority of background asset loading: Unity's time per frame for finishing loads (Low 2 ms, BelowNormal 4 ms, Normal 10 ms, High 50 ms). " +
                "Lower = smoother frames during loads, slower loads. BelowNormal is Unity's own default on PC and the game does not change it, so in the presets it mostly keeps that value in place.") },
            { nameof(OptimizationSettings.IncrementalGcSliceMs), R("[CPU] Incremental GC time slice in ms (only if the game uses incremental GC). Smaller slices = smaller GC spikes, but too small can fall back to full collections. -1 = unchanged.", -1, 20) },
            // Potassium-derived stutter fixes
            { nameof(OptimizationSettings.DeferSharedBlocks), D("[Stutter] Do not initialise Shared Blocks until the player enters its zone. (Potassium)") },
            { nameof(OptimizationSettings.StaggerRigPool), D("[Stutter] Create the remote-player rig pool over several frames instead of all at startup; missing rigs are created on demand. (Potassium)") },
            { nameof(OptimizationSettings.InitialRigPoolSize), R("[Stutter] Remote rigs created immediately when the pool is staggered. (Potassium)", 1, 20) },
            { nameof(OptimizationSettings.StaggerScoreboardCreation), D("[Stutter] Create local scoreboards over successive frames after joining a room. (Potassium)") },
            { nameof(OptimizationSettings.StaggerScoreboardRefresh), D("[Stutter] Refresh scoreboards one per frame when players join/leave. (Potassium)") },
            { nameof(OptimizationSettings.SkipInactiveEntityLeaveScans), D("[Stutter] Skip unused full entity scans done by inactive zones when a player leaves. (Potassium)") },
            { nameof(OptimizationSettings.RoomLeaveGc), D("[Stutter] Forced garbage collection on room leave: GameDefault, Skip (Potassium) or Incremental (time-sliced, only if the game uses incremental GC).") },
            { nameof(OptimizationSettings.SkipTimedAssetUnloads), D("[Stutter] Prevent scene components from forcing synchronous unused-asset sweeps during gameplay. May use more memory. (Potassium)") },
            { nameof(OptimizationSettings.MapSanitizer), D("[Maps] Custom-map sanitizer optimisation: Off, SafeLookups (keeps the game's rules, only speeds up allow-list lookups; in the current game that is only the layer check, a small saving) or FullReplacement (Potassium's re-implementation; bypasses any rule added by later game updates).") },
            // Throttles
            { nameof(OptimizationSettings.TextureTransitionRate), R("[Visual] Texture-transition updates per second (0 = game rate). (Potassium, frame-staggered)", 0, 240) },
            { nameof(OptimizationSettings.VoiceVisualRate), R("[Visual] Voice-reactive cosmetic updates per second when they use no smoothing (0 = game rate). (Potassium, frame-staggered)", 0, 240) },
            { nameof(OptimizationSettings.ProximityRate), R("[Visual] Cosmetic environment-proximity checks per second (0 = game rate). (Potassium, frame-staggered)", 0, 240) },
            // Cosmetics
            { nameof(OptimizationSettings.ReduceDistantCosmetics), D("[Visual] Locally cull distant remote-player cosmetics. Does not affect networking or what others see. (Potassium)") },
            { nameof(OptimizationSettings.CosmeticFullDetailDistance), R("[Visual] Distance (m) within which remote cosmetics are always drawn. (Potassium)", 0, 100) },
            { nameof(OptimizationSettings.CosmeticMaximumDistance), R("[Visual] Maximum distance (m) for remote cosmetics. (Potassium)", 0, 200) },
            { nameof(OptimizationSettings.CosmeticFullDetailCount), R("[Visual] Players always drawn with full cosmetics. (Potassium)", 0, 20) },
            { nameof(OptimizationSettings.CosmeticMaximumCount), R("[Visual] Maximum players drawn with cosmetics. (Potassium)", 0, 20) },
            // Adaptive
            { nameof(OptimizationSettings.AdaptiveMayChangeRenderScale), D("[Adaptive] Allow adaptive mode to change render scale while this profile is active.") },
            { nameof(OptimizationSettings.AdaptiveMayChangeShadows), D("[Adaptive] Allow adaptive mode to change shadow distance and pixel lights.") },
            { nameof(OptimizationSettings.AdaptiveMayChangeLod), D("[Adaptive] Allow adaptive mode to change LOD bias.") },
            { nameof(OptimizationSettings.AdaptiveMayChangeCosmetics), D("[Adaptive] Allow adaptive mode to reduce distant remote-player cosmetic detail and cosmetic update rates (CPU savings in busy lobbies).") },
            { nameof(OptimizationSettings.AdaptiveGpuHeadroomPercent), R("[Adaptive] Share of the frame budget to keep free on the GPU (for video encoders/compositor). Only used when the VR runtime reports GPU time.", 0, 40) },
        };

        public static IEnumerable<FieldInfo> ProfileFields()
        {
            foreach (FieldInfo field in typeof(OptimizationSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                yield return field;
        }
    }
}

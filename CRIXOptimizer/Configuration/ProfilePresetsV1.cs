// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (Plugin.cs config defaults), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Configuration
{
    /// <summary>
    /// FROZEN copy of the CRIX 1.0.0 built-in presets. Do not edit. Used only by <see cref="ProfileMigration"/> to
    /// tell values a user never changed (equal to the 1.0.0 default) from values they edited on purpose.
    /// </summary>
    internal static class ProfilePresetsV1
    {
        /// <summary>Profile-file section names, in display order.</summary>
        private static readonly string[] SectionNamesV1 =
        {
            "Balanced",
            "MaximumPerformance",
            "VirtualDesktop",
            "VirtualDesktop.Performance",
            "VirtualDesktop.Quality",
            "VirtualDesktop.Streaming",
            "OBSStreaming",
            "DiscordCall",
            "ReShadeCompatible",
            "MultiMod",
        };

        public static string SectionFor(ProfileKind profile, VirtualDesktopPreset vdPreset)
        {
            switch (profile)
            {
                case ProfileKind.VirtualDesktop:
                    return vdPreset == VirtualDesktopPreset.Balanced ? "VirtualDesktop" : "VirtualDesktop." + vdPreset;
                case ProfileKind.Custom:
                    return "Custom";
                default:
                    return profile.ToString();
            }
        }

        public static OptimizationSettings ForSection(string section)
        {
            switch (section)
            {
                case "Balanced": return Balanced();
                case "MaximumPerformance": return MaximumPerformance();
                case "VirtualDesktop": return VirtualDesktopBalanced();
                case "VirtualDesktop.Performance": return VirtualDesktopPerformance();
                case "VirtualDesktop.Quality": return VirtualDesktopQuality();
                case "VirtualDesktop.Streaming": return VirtualDesktopStreaming();
                case "OBSStreaming": return ObsStreaming();
                case "DiscordCall": return DiscordCall();
                case "ReShadeCompatible": return ReShadeCompatible();
                case "MultiMod": return MultiMod();
                case "Custom": return Balanced();
                default: throw new ArgumentException("Unknown profile section " + section, nameof(section));
            }
        }

        public static OptimizationSettings For(ProfileKind profile, VirtualDesktopPreset vdPreset = VirtualDesktopPreset.Balanced) =>
            ForSection(SectionFor(profile, vdPreset));

        /// <summary>General use: the Potassium-derived stutter fixes plus a mild shadow-distance cap.</summary>
        public static OptimizationSettings Balanced() => new OptimizationSettings
        {
            ShadowDistanceLimit = 60f,
        };

        /// <summary>Prioritises lower rendering workload over visuals. Noticeably softer image.</summary>
        public static OptimizationSettings MaximumPerformance() => new OptimizationSettings
        {
            RenderScale = 0.8f,
            ShadowQuality = ShadowQualityLimit.HardOnly,
            ShadowDistanceLimit = 25f,
            ShadowResolution = ShadowResolutionLimit.Medium,
            ShadowCascadesLimit = 1,
            LodBiasLimit = 0.7f,
            PixelLightLimit = 1,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            ParticleRaycastBudgetLimit = 64,
            SkinWeights = SkinWeightLimit.TwoBones,
            CameraHdr = TriState.Off,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.LeftEye,
            AsyncUploadTimeSliceMs = 2,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 20f,
            VoiceVisualRate = 20f,
            ProximityRate = 20f,
            CosmeticFullDetailDistance = 3.5f,
            CosmeticMaximumDistance = 6f,
            CosmeticFullDetailCount = 4,
            CosmeticMaximumCount = 8,
        };

        /// <summary>Wireless PCVR via Virtual Desktop, balanced: leaves GPU time for the video encoder and keeps loads smooth.</summary>
        public static OptimizationSettings VirtualDesktopBalanced() => new OptimizationSettings
        {
            ShadowDistanceLimit = 40f,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.LeftEye,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveGpuHeadroomPercent = 10f,
        };

        public static OptimizationSettings VirtualDesktopPerformance() => new OptimizationSettings
        {
            RenderScale = 0.85f,
            ShadowQuality = ShadowQualityLimit.HardOnly,
            ShadowDistanceLimit = 25f,
            ShadowResolution = ShadowResolutionLimit.Medium,
            LodBiasLimit = 0.75f,
            PixelLightLimit = 1,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            ParticleRaycastBudgetLimit = 64,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.LeftEye,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 20f,
            VoiceVisualRate = 20f,
            ProximityRate = 20f,
            AdaptiveGpuHeadroomPercent = 15f,
        };

        /// <summary>Keeps the game's visuals; only stutter fixes, occlusion mesh, single-eye mirror and smoother loading.</summary>
        public static OptimizationSettings VirtualDesktopQuality() => new OptimizationSettings
        {
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.LeftEye,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveGpuHeadroomPercent = 5f,
        };

        /// <summary>Virtual Desktop while also recording/streaming: leaves the desktop mirror alone so capture is unaffected.</summary>
        public static OptimizationSettings VirtualDesktopStreaming() => new OptimizationSettings
        {
            ShadowDistanceLimit = 35f,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.Unchanged,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveGpuHeadroomPercent = 15f,
        };

        /// <summary>Recording/broadcasting: steadier frame pacing; never changes what the desktop window shows.</summary>
        public static OptimizationSettings ObsStreaming() => new OptimizationSettings
        {
            ShadowDistanceLimit = 40f,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.Unchanged,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveGpuHeadroomPercent = 10f,
        };

        /// <summary>Voice/video calls and screen sharing: modest game-side reductions.</summary>
        public static OptimizationSettings DiscordCall() => new OptimizationSettings
        {
            ShadowDistanceLimit = 40f,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            EnsureOcclusionMesh = true,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 24f,
            VoiceVisualRate = 24f,
            ProximityRate = 24f,
            AdaptiveGpuHeadroomPercent = 5f,
        };

        /// <summary>
        /// Keeps everything ReShade depends on stable: no resolution, MSAA, HDR, mirror, occlusion-mesh or
        /// soft-particle (depth) changes, and adaptive mode may not change render scale. Saves elsewhere.
        /// </summary>
        public static OptimizationSettings ReShadeCompatible() => new OptimizationSettings
        {
            ShadowDistanceLimit = 50f,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveMayChangeRenderScale = false,
        };

        /// <summary>
        /// Conservative for heavily modded installs: no engine graphics changes, and no patches that change
        /// when game objects start (Shared Blocks, rig pool) or that skip methods other mods commonly hook
        /// (scoreboard refresh).
        /// </summary>
        public static OptimizationSettings MultiMod() => new OptimizationSettings
        {
            DeferSharedBlocks = false,
            StaggerRigPool = false,
            StaggerScoreboardRefresh = false,
            AdaptiveMayChangeRenderScale = true,
        };

        public static IEnumerable<KeyValuePair<string, OptimizationSettings>> All()
        {
            foreach (string section in SectionNamesV1)
                yield return new KeyValuePair<string, OptimizationSettings>(section, ForSection(section));
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Configuration
{
    /// <summary>
    /// Built-in starting values for every profile. These are only defaults: each profile is a section in
    /// BepInEx/config/com.crix.crixoptimizer.profiles.cfg and every value can be edited there.
    /// None of these values is claimed to be optimal for every PC, GPU, headset or network; they are
    /// starting points for testing. Most graphics values are limits, so they only take effect when the
    /// game's own setting is more expensive.
    /// </summary>
    public static class ProfilePresets
    {
        /// <summary>Profile-file section names, in display order.</summary>
        public static readonly string[] SectionNames =
        {
            "Balanced",
            "MaximumPerformance",
            "HighRefresh",
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
                case "HighRefresh": return HighRefresh();
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

        /// <summary>
        /// General use: the Potassium-derived stutter fixes, capped realtime shadows and lights, the VR occlusion
        /// mesh (skips pixels the lenses never show) and smoother asset loading. Keeps the game's resolution;
        /// adaptive quality lowers more only when the frame rate drops.
        /// </summary>
        public static OptimizationSettings Balanced() => new OptimizationSettings
        {
            MaxEyeMegapixels = 8f,
            ShadowDistanceLimit = 50f,
            ShadowCascadesLimit = 2,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            EnsureOcclusionMesh = true,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
        };

        /// <summary>Prioritises lower rendering workload over visuals. Noticeably softer image.</summary>
        public static OptimizationSettings MaximumPerformance() => new OptimizationSettings
        {
            MaxEyeMegapixels = 5f,
            RenderScale = 0.8f,
            ShadowQuality = ShadowQualityLimit.HardOnly,
            ShadowDistanceLimit = 25f,
            ShadowResolution = ShadowResolutionLimit.Medium,
            ShadowCascadesLimit = 1,
            MsaaSamples = 2,
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

        /// <summary>
        /// 120/144 Hz headsets. At 120 Hz every frame has 8.3 ms instead of 11.1 ms, and Gorilla Tag also runs its
        /// physics at the refresh rate, so the CPU is usually the limit. This profile keeps the game's resolution
        /// (adaptive mode lowers it only if that is shown to help) and cuts per-frame CPU work: distant remote
        /// cosmetics, cosmetic update rates, LOD detail, skinning and particle work, smaller loading slices.
        /// </summary>
        public static OptimizationSettings HighRefresh() => new OptimizationSettings
        {
            MaxEyeMegapixels = 6f,
            ShadowDistanceLimit = 20f,
            ShadowCascadesLimit = 1,
            MsaaSamples = 2,
            LodBiasLimit = 0.8f,
            PixelLightLimit = 1,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            ParticleRaycastBudgetLimit = 64,
            SkinWeights = SkinWeightLimit.TwoBones,
            EnsureOcclusionMesh = true,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 20f,
            VoiceVisualRate = 20f,
            ProximityRate = 20f,
            CosmeticFullDetailDistance = 3.5f,
            CosmeticMaximumDistance = 6f,
            CosmeticFullDetailCount = 4,
            CosmeticMaximumCount = 8,
            AdaptiveGpuHeadroomPercent = 10f,
            DesktopCamera = DesktopCameraMode.OffWhenNotStreaming,
            // At 120 Hz the CPU is the usual limit, and every resolution change is a hitch (eye-texture reallocation).
            AdaptiveMayChangeRenderScale = false,
        };

        /// <summary>Wireless PCVR via Virtual Desktop, balanced: leaves GPU time for the video encoder and keeps loads smooth.</summary>
        public static OptimizationSettings VirtualDesktopBalanced() => new OptimizationSettings
        {
            MaxEyeMegapixels = 7f,
            ShadowDistanceLimit = 40f,
            ShadowCascadesLimit = 2,
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
            MaxEyeMegapixels = 5f,
            RenderScale = 0.85f,
            ShadowQuality = ShadowQualityLimit.HardOnly,
            ShadowDistanceLimit = 25f,
            ShadowResolution = ShadowResolutionLimit.Medium,
            ShadowCascadesLimit = 1,
            MsaaSamples = 2,
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
            MaxEyeMegapixels = 10f,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.LeftEye,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            AdaptiveGpuHeadroomPercent = 5f,
        };

        /// <summary>Virtual Desktop while also recording/streaming: leaves the desktop mirror alone so capture is unaffected.</summary>
        public static OptimizationSettings VirtualDesktopStreaming() => new OptimizationSettings
        {
            ShadowDistanceLimit = 30f,
            ShadowCascadesLimit = 2,
            PixelLightLimit = 1,
            RealtimeReflectionProbes = TriState.Off,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.Unchanged,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 24f,
            VoiceVisualRate = 24f,
            ProximityRate = 24f,
            AdaptiveGpuHeadroomPercent = 20f,
            // Under URP the render scale also sets the desktop camera's resolution, i.e. what OBS records.
            AdaptiveMayChangeRenderScale = false,
        };

        /// <summary>
        /// Recording/broadcasting (OBS, Streamlabs): leaves GPU and CPU time for capture and encoding. Keeps the
        /// game's resolution and never changes what the desktop window shows; adaptive quality reacts sooner.
        /// </summary>
        public static OptimizationSettings ObsStreaming() => new OptimizationSettings
        {
            ShadowDistanceLimit = 30f,
            ShadowCascadesLimit = 2,
            PixelLightLimit = 1,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            ParticleRaycastBudgetLimit = 128,
            EnsureOcclusionMesh = true,
            DesktopMirror = DesktopMirrorMode.Unchanged,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 24f,
            VoiceVisualRate = 24f,
            ProximityRate = 24f,
            CosmeticFullDetailDistance = 4f,
            CosmeticMaximumDistance = 7f,
            AdaptiveGpuHeadroomPercent = 20f,
            // Under URP the render scale also sets the desktop camera's resolution, i.e. what viewers see.
            AdaptiveMayChangeRenderScale = false,
        };

        /// <summary>Voice/video calls and screen sharing: modest game-side reductions.</summary>
        public static OptimizationSettings DiscordCall() => new OptimizationSettings
        {
            MaxEyeMegapixels = 8f,
            ShadowDistanceLimit = 35f,
            ShadowCascadesLimit = 2,
            PixelLightLimit = 2,
            RealtimeReflectionProbes = TriState.Off,
            SoftParticles = TriState.Off,
            EnsureOcclusionMesh = true,
            AsyncUploadTimeSliceMs = 1,
            BackgroundLoading = LoadingPriority.BelowNormal,
            TextureTransitionRate = 24f,
            VoiceVisualRate = 24f,
            ProximityRate = 24f,
            AdaptiveGpuHeadroomPercent = 10f,
        };

        /// <summary>
        /// Keeps everything ReShade depends on stable: no resolution, MSAA, HDR, mirror, occlusion-mesh or
        /// soft-particle (depth) changes, and adaptive mode may not change render scale. Saves elsewhere.
        /// </summary>
        public static OptimizationSettings ReShadeCompatible() => new OptimizationSettings
        {
            ShadowDistanceLimit = 50f,
            ShadowCascadesLimit = 2,
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
            foreach (string section in SectionNames)
                yield return new KeyValuePair<string, OptimizationSettings>(section, ForSection(section));
        }
    }
}

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
    /// Every value a profile controls. "Limit"/"Cap" settings are upper bounds: when the game's own value is
    /// already cheaper, the game's value is kept. Sentinels: -1 (or Unchanged) means "do not touch".
    /// This class has no Unity dependency so presets and validation are unit-testable.
    /// </summary>
    public sealed class OptimizationSettings
    {
        // ---------------- Rendering (GPU) ----------------
        /// <summary>XR eye-texture resolution scale. 0 = unchanged. Absolute value (can raise quality).</summary>
        public float RenderScale;
        /// <summary>Upper limit for one eye's rendered pixels in megapixels (render scale is lowered to fit). 0 = no limit.</summary>
        public float MaxEyeMegapixels;
        public ShadowQualityLimit ShadowQuality;
        /// <summary>Maximum shadow distance in metres. -1 = unchanged.</summary>
        public float ShadowDistanceLimit = -1f;
        public ShadowResolutionLimit ShadowResolution;
        /// <summary>Maximum shadow cascades (1, 2 or 4). -1 = unchanged.</summary>
        public int ShadowCascadesLimit = -1;
        /// <summary>MSAA sample count (0, 2, 4, 8). -1 = unchanged. Absolute; applied once, may need a restart in VR.</summary>
        public int MsaaSamples = -1;
        public AnisotropicMode AnisotropicFiltering;
        /// <summary>Maximum LOD bias (lower = switch to simpler meshes sooner). -1 = unchanged.</summary>
        public float LodBiasLimit = -1f;
        /// <summary>Minimum "maximum LOD level" (higher = skip the most detailed LODs). -1 = unchanged.</summary>
        public int MinimumMaxLodLevel = -1;
        /// <summary>Maximum per-pixel lights (built-in pipeline). -1 = unchanged.</summary>
        public int PixelLightLimit = -1;
        /// <summary>Minimum texture mip limit (0 = full res, 1 = half ...). Frees VRAM; re-uploads all textures. -1 = unchanged.</summary>
        public int TextureMipLimit = -1;
        public TriState RealtimeReflectionProbes;
        public TriState SoftParticles;
        /// <summary>Maximum particle collision raycasts per frame. -1 = unchanged.</summary>
        public int ParticleRaycastBudgetLimit = -1;
        public SkinWeightLimit SkinWeights;
        public TriState CameraHdr;
        /// <summary>Make sure the XR occlusion (hidden-area) mesh is used, so pixels outside the lenses are not shaded.</summary>
        public bool EnsureOcclusionMesh;
        public DesktopMirrorMode DesktopMirror;
        public DesktopCameraMode DesktopCamera;

        // ---------------- Frame pacing / loading (CPU) ----------------
        /// <summary>Main-thread milliseconds per frame for async texture/mesh uploads (1-33). -1 = unchanged.</summary>
        public int AsyncUploadTimeSliceMs = -1;
        public LoadingPriority BackgroundLoading;
        /// <summary>Incremental GC time slice in milliseconds (only when the game uses incremental GC). -1 = unchanged.</summary>
        public float IncrementalGcSliceMs = -1f;

        // ---------------- Game stutter fixes (derived from Potassium 1.0.0) ----------------
        public bool DeferSharedBlocks = true;
        public bool StaggerRigPool = true;
        public int InitialRigPoolSize = 4;
        public bool StaggerScoreboardCreation = true;
        public bool StaggerScoreboardRefresh = true;
        public bool SkipInactiveEntityLeaveScans = true;
        public RoomLeaveGcMode RoomLeaveGc = RoomLeaveGcMode.Skip;
        public bool SkipTimedAssetUnloads = true;
        public SanitizerMode MapSanitizer = SanitizerMode.SafeLookups;

        // ---------------- Visual update throttles (derived from Potassium 1.0.0) ----------------
        /// <summary>Updates per second. 0 = unthrottled.</summary>
        public float TextureTransitionRate = 30f;
        public float VoiceVisualRate = 30f;
        public float ProximityRate = 30f;

        // ---------------- Remote-player cosmetic detail (derived from Potassium 1.0.0) ----------------
        public bool ReduceDistantCosmetics = true;
        public float CosmeticFullDetailDistance = 4.5f;
        public float CosmeticMaximumDistance = 8f;
        public int CosmeticFullDetailCount = 5;
        public int CosmeticMaximumCount = 10;

        // ---------------- What adaptive mode may change while this profile is active ----------------
        public bool AdaptiveMayChangeRenderScale = true;
        public bool AdaptiveMayChangeShadows = true;
        public bool AdaptiveMayChangeLod = true;
        public bool AdaptiveMayChangeCosmetics = true;
        /// <summary>Share of the frame budget the GPU should keep free when GPU timing is available (encoders, compositor).</summary>
        public float AdaptiveGpuHeadroomPercent;

        public OptimizationSettings Clone() => (OptimizationSettings)MemberwiseClone();

        /// <summary>Clamps everything to safe ranges and fixes inconsistent combinations. Returns human-readable notes.</summary>
        public List<string> Validate()
        {
            var notes = new List<string>();

            if (RenderScale != 0f && (RenderScale < 0.5f || RenderScale > 2.0f || float.IsNaN(RenderScale)))
            {
                float fixedValue = float.IsNaN(RenderScale) ? 0f : Clamp(RenderScale, 0.5f, 2.0f);
                notes.Add($"RenderScale {RenderScale} out of range 0.5-2.0 (0 = unchanged); using {fixedValue}");
                RenderScale = fixedValue;
            }

            if (MaxEyeMegapixels != 0f && (MaxEyeMegapixels < 1f || MaxEyeMegapixels > 50f || float.IsNaN(MaxEyeMegapixels)))
            {
                float fixedValue = float.IsNaN(MaxEyeMegapixels) || MaxEyeMegapixels < 0f ? 0f : Clamp(MaxEyeMegapixels, 1f, 50f);
                notes.Add($"MaxEyeMegapixels {MaxEyeMegapixels} out of range 1-50 (0 = no limit); using {fixedValue}");
                MaxEyeMegapixels = fixedValue;
            }
            ShadowDistanceLimit = NormalizeOptional(ShadowDistanceLimit, 0f, 1000f, "ShadowDistanceLimit", notes);
            LodBiasLimit = NormalizeOptional(LodBiasLimit, 0.1f, 10f, "LodBiasLimit", notes);
            IncrementalGcSliceMs = NormalizeOptional(IncrementalGcSliceMs, 0.25f, 20f, "IncrementalGcSliceMs", notes);

            if (ShadowCascadesLimit != -1 && ShadowCascadesLimit != 1 && ShadowCascadesLimit != 2 && ShadowCascadesLimit != 4)
            {
                int fixedValue = ShadowCascadesLimit <= 0 ? -1 : (ShadowCascadesLimit < 2 ? 1 : (ShadowCascadesLimit < 4 ? 2 : 4));
                notes.Add($"ShadowCascadesLimit {ShadowCascadesLimit} must be 1, 2, 4 or -1; using {fixedValue}");
                ShadowCascadesLimit = fixedValue;
            }

            if (MsaaSamples != -1 && MsaaSamples != 0 && MsaaSamples != 2 && MsaaSamples != 4 && MsaaSamples != 8)
            {
                int fixedValue = MsaaSamples < 0 ? -1 : (MsaaSamples <= 1 ? 0 : (MsaaSamples <= 2 ? 2 : (MsaaSamples <= 4 ? 4 : 8)));
                notes.Add($"MsaaSamples {MsaaSamples} must be 0, 2, 4, 8 or -1; using {fixedValue}");
                MsaaSamples = fixedValue;
            }

            MinimumMaxLodLevel = NormalizeOptional(MinimumMaxLodLevel, 0, 7, "MinimumMaxLodLevel", notes);
            PixelLightLimit = NormalizeOptional(PixelLightLimit, 0, 16, "PixelLightLimit", notes);
            TextureMipLimit = NormalizeOptional(TextureMipLimit, 0, 3, "TextureMipLimit", notes);
            ParticleRaycastBudgetLimit = NormalizeOptional(ParticleRaycastBudgetLimit, 0, 4096, "ParticleRaycastBudgetLimit", notes);
            AsyncUploadTimeSliceMs = NormalizeOptional(AsyncUploadTimeSliceMs, 1, 33, "AsyncUploadTimeSliceMs", notes);

            InitialRigPoolSize = ClampLogged(InitialRigPoolSize, 1, 20, "InitialRigPoolSize", notes);
            TextureTransitionRate = ClampRate(TextureTransitionRate, "TextureTransitionRate", notes);
            VoiceVisualRate = ClampRate(VoiceVisualRate, "VoiceVisualRate", notes);
            ProximityRate = ClampRate(ProximityRate, "ProximityRate", notes);

            CosmeticFullDetailDistance = ClampLogged(CosmeticFullDetailDistance, 0f, 100f, "CosmeticFullDetailDistance", notes);
            CosmeticMaximumDistance = ClampLogged(CosmeticMaximumDistance, 0f, 200f, "CosmeticMaximumDistance", notes);
            if (CosmeticMaximumDistance < CosmeticFullDetailDistance)
            {
                notes.Add($"CosmeticMaximumDistance ({CosmeticMaximumDistance}) < CosmeticFullDetailDistance ({CosmeticFullDetailDistance}); raised to match");
                CosmeticMaximumDistance = CosmeticFullDetailDistance;
            }
            CosmeticFullDetailCount = ClampLogged(CosmeticFullDetailCount, 0, 20, "CosmeticFullDetailCount", notes);
            CosmeticMaximumCount = ClampLogged(CosmeticMaximumCount, 0, 20, "CosmeticMaximumCount", notes);
            if (CosmeticMaximumCount < CosmeticFullDetailCount)
            {
                notes.Add($"CosmeticMaximumCount ({CosmeticMaximumCount}) < CosmeticFullDetailCount ({CosmeticFullDetailCount}); raised to match");
                CosmeticMaximumCount = CosmeticFullDetailCount;
            }

            AdaptiveGpuHeadroomPercent = ClampLogged(AdaptiveGpuHeadroomPercent, 0f, 40f, "AdaptiveGpuHeadroomPercent", notes);
            return notes;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static float NormalizeOptional(float value, float min, float max, string name, List<string> notes)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                if (value != -1f && !float.IsNaN(value) && value < 0f)
                    notes.Add($"{name} {value} is negative; treated as -1 (unchanged)");
                return -1f;
            }
            if (value < min || value > max)
            {
                float c = Clamp(value, min, max);
                notes.Add($"{name} {value} out of range {min}-{max}; using {c}");
                return c;
            }
            return value;
        }

        private static int NormalizeOptional(int value, int min, int max, string name, List<string> notes)
        {
            if (value < 0)
            {
                if (value != -1)
                    notes.Add($"{name} {value} is negative; treated as -1 (unchanged)");
                return -1;
            }
            if (value < min || value > max)
            {
                int c = Math.Max(min, Math.Min(max, value));
                notes.Add($"{name} {value} out of range {min}-{max}; using {c}");
                return c;
            }
            return value;
        }

        private static int ClampLogged(int value, int min, int max, string name, List<string> notes)
        {
            int c = Math.Max(min, Math.Min(max, value));
            if (c != value)
                notes.Add($"{name} {value} out of range {min}-{max}; using {c}");
            return c;
        }

        private static float ClampLogged(float value, float min, float max, string name, List<string> notes)
        {
            float c = float.IsNaN(value) ? min : Clamp(value, min, max);
            if (c != value)
                notes.Add($"{name} {value} out of range {min}-{max}; using {c}");
            return c;
        }

        private static float ClampRate(float value, string name, List<string> notes)
        {
            if (float.IsNaN(value) || value <= 0f)
                return 0f;
            return ClampLogged(value, 1f, 240f, name, notes);
        }
    }
}

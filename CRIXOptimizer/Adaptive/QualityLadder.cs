// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Adaptive
{
    /// <summary>The engine values adaptive mode is allowed to move. Has* flags mark which ones are in play.</summary>
    public struct QualityLevers
    {
        public bool HasRenderScale;
        public float RenderScale;
        public bool HasShadowDistance;
        public float ShadowDistance;
        public bool HasLodBias;
        public float LodBias;
        public bool HasPixelLights;
        public int PixelLightCount;
        /// <summary>Factor (0-1] for distant remote-cosmetic distances/counts. Saves CPU and GPU in busy lobbies.</summary>
        public float CosmeticScale;
        /// <summary>Factor (0-1] for cosmetic visual update rates (voice-reactive, proximity, texture transitions). Saves CPU.</summary>
        public float ThrottleScale;
    }

    public sealed class QualityLadderSettings
    {
        public float RenderScaleStep = 0.1f;
        public float MinRenderScale = 0.6f;
        public float MaxRenderScale = 1.3f;
        public bool AllowRenderScale = true;
        public bool AllowShadows = true;
        public bool AllowLod = true;
        public bool AllowCosmetics = true;
    }

    /// <summary>
    /// Maps an integer quality level to lever values relative to the profile baseline.
    /// Level 0 is the baseline. Negative levels reduce cost, cheapest-to-notice first:
    ///   -1: distant cosmetic detail x0.85, cosmetic update rates x0.9, shadow distance x0.8, LOD bias x0.9
    ///   -2: + render scale one step down, cosmetics x0.7, update rates x0.8
    ///   -3: shadow distance x0.6, LOD bias x0.8, one fewer pixel light, cosmetics x0.55
    ///   -4: render scale two steps down ... and so on, bounded by the configured minimums.
    /// The cosmetic and update-rate levers reduce CPU work, which is what helps in full lobbies; render scale only
    /// helps when the GPU is the bottleneck.
    /// Positive levels only raise render scale (never above the configured maximum).
    /// When the frame is known to be CPU-bound, render scale is not lowered because it would not help.
    /// </summary>
    public static class QualityLadder
    {
        public static QualityLevers Compute(QualityLevers baseline, int level, QualityLadderSettings settings, bool cpuBound)
        {
            if (settings == null)
                settings = new QualityLadderSettings();
            QualityLevers result = baseline;
            result.CosmeticScale = 1f;
            result.ThrottleScale = 1f;
            if (level == 0)
                return result;

            if (level < 0)
            {
                int d = -level;
                int halfSteps = (d + 1) / 2;
                if (settings.AllowShadows && baseline.HasShadowDistance)
                    result.ShadowDistance = baseline.ShadowDistance * (float)Math.Max(0.2, 1.0 - 0.2 * halfSteps);
                if (settings.AllowLod && baseline.HasLodBias)
                    result.LodBias = baseline.LodBias * (float)Math.Max(0.5, 1.0 - 0.1 * halfSteps);
                if (settings.AllowShadows && baseline.HasPixelLights)
                    result.PixelLightCount = Math.Max(0, baseline.PixelLightCount - d / 3);
                if (settings.AllowCosmetics)
                {
                    result.CosmeticScale = (float)Math.Max(0.4, 1.0 - 0.15 * d);
                    result.ThrottleScale = (float)Math.Max(0.5, 1.0 - 0.1 * d);
                }
                if (settings.AllowRenderScale && baseline.HasRenderScale && !cpuBound)
                {
                    int steps = d / 2;
                    float lowered = baseline.RenderScale - steps * settings.RenderScaleStep;
                    float floor = Math.Min(baseline.RenderScale, settings.MinRenderScale);
                    result.RenderScale = Math.Max(floor, lowered);
                }
            }
            else if (settings.AllowRenderScale && baseline.HasRenderScale)
            {
                float raised = baseline.RenderScale + level * settings.RenderScaleStep;
                float ceiling = Math.Max(baseline.RenderScale, settings.MaxRenderScale);
                result.RenderScale = Math.Min(ceiling, raised);
            }
            return result;
        }

        /// <summary>Returns a copy of the profile with the CPU-side levers (cosmetics, update rates) applied.</summary>
        public static Configuration.OptimizationSettings ApplyCpuLevers(Configuration.OptimizationSettings profile, QualityLevers levers)
        {
            if (profile == null)
                return null;
            Configuration.OptimizationSettings s = profile.Clone();
            float c = levers.CosmeticScale > 0f && levers.CosmeticScale < 1f ? levers.CosmeticScale : 1f;
            float r = levers.ThrottleScale > 0f && levers.ThrottleScale < 1f ? levers.ThrottleScale : 1f;
            if (c < 1f)
            {
                s.CosmeticFullDetailDistance *= c;
                s.CosmeticMaximumDistance *= c;
                s.CosmeticFullDetailCount = (int)Math.Floor(s.CosmeticFullDetailCount * c);
                s.CosmeticMaximumCount = Math.Max(s.CosmeticFullDetailCount, (int)Math.Floor(s.CosmeticMaximumCount * c));
            }
            if (r < 1f)
            {
                // A rate of 0 means the user turned that throttle off; adaptive mode respects that.
                s.TextureTransitionRate = s.TextureTransitionRate > 0f ? Math.Max(1f, s.TextureTransitionRate * r) : 0f;
                s.VoiceVisualRate = s.VoiceVisualRate > 0f ? Math.Max(1f, s.VoiceVisualRate * r) : 0f;
                s.ProximityRate = s.ProximityRate > 0f ? Math.Max(1f, s.ProximityRate * r) : 0f;
            }
            return s;
        }
    }
}

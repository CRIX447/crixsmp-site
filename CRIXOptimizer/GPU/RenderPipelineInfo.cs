// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using UnityEngine.Rendering;

namespace CRIXOptimizer.GPU
{
    public enum PipelineKind
    {
        BuiltIn,
        Universal,
        HighDefinition,
        Other,
    }

    /// <summary>Detects which render pipeline the game uses, so only settings that pipeline honours are changed.</summary>
    internal static class RenderPipelineInfo
    {
        public static PipelineKind Detect(out object asset, out string assetTypeName)
        {
            RenderPipelineAsset current = null;
            try
            {
                current = GraphicsSettings.currentRenderPipeline;
            }
            catch
            {
                // Very old Unity: fall back to the project-wide asset.
                try { current = GraphicsSettings.renderPipelineAsset; } catch { current = null; }
            }

            asset = current;
            if (current == null)
            {
                assetTypeName = "none";
                return PipelineKind.BuiltIn;
            }

            assetTypeName = current.GetType().FullName ?? current.GetType().Name;
            if (assetTypeName.IndexOf("Universal", System.StringComparison.Ordinal) >= 0)
                return PipelineKind.Universal;
            if (assetTypeName.IndexOf("HighDefinition", System.StringComparison.Ordinal) >= 0)
                return PipelineKind.HighDefinition;
            return PipelineKind.Other;
        }

        public static string Describe(PipelineKind kind)
        {
            switch (kind)
            {
                case PipelineKind.BuiltIn: return "Built-in render pipeline";
                case PipelineKind.Universal: return "Universal Render Pipeline (URP)";
                case PipelineKind.HighDefinition: return "High Definition Render Pipeline (HDRP)";
                default: return "Custom scriptable render pipeline";
            }
        }
    }
}

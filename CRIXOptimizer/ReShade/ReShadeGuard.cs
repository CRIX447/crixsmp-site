// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Collections.Generic;
using CRIXOptimizer.Configuration;

namespace CRIXOptimizer.ReShade
{
    /// <summary>
    /// Constraints that keep a ReShade setup stable whatever profile is selected.
    ///
    /// ReShade's depth-aware effects pick the game's depth buffer by matching its size and format, and the
    /// selection is lost when render targets are recreated. So while the guard is active CRIX does not change,
    /// at runtime, anything that recreates or reformats the main render targets (MSAA, HDR, adaptive render
    /// scale) or what the desktop window shows (many ReShade setups apply effects to the desktop mirror).
    /// A fixed RenderScale from the profile is still applied once. Everything else (shadows, LOD, lights,
    /// throttles, stutter fixes) is unaffected. This cannot guarantee compatibility with every shader,
    /// graphics API or driver.
    /// </summary>
    public static class ReShadeGuard
    {
        public static List<string> Apply(OptimizationSettings settings)
        {
            var notes = new List<string>();
            if (settings == null)
                return notes;

            if (settings.MsaaSamples != -1)
            {
                notes.Add("ReShade guard: MSAA left unchanged (changing it recreates the depth buffers ReShade uses)");
                settings.MsaaSamples = -1;
            }
            if (settings.CameraHdr != TriState.Unchanged)
            {
                notes.Add("ReShade guard: camera HDR left unchanged (changes render-target formats)");
                settings.CameraHdr = TriState.Unchanged;
            }
            if (settings.DesktopMirror != DesktopMirrorMode.Unchanged)
            {
                notes.Add("ReShade guard: desktop mirror left unchanged (ReShade may be applied to it)");
                settings.DesktopMirror = DesktopMirrorMode.Unchanged;
            }
            if (settings.AdaptiveMayChangeRenderScale)
            {
                notes.Add("ReShade guard: adaptive mode will not change render scale");
                settings.AdaptiveMayChangeRenderScale = false;
            }
            return notes;
        }
    }
}

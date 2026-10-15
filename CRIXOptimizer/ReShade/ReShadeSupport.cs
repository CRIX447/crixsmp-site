// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Collections.Generic;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.ReShade
{
    /// <summary>Decides whether the ReShade guard applies and explains why.</summary>
    internal sealed class ReShadeSupport
    {
        public bool Detected { get; private set; }
        public string Detail { get; private set; } = "";
        public bool GuardActive { get; private set; }
        public readonly List<string> Advice = new List<string>();

        public bool Update(GraphicsHook hooks, string detail, ReShadeGuardMode mode, ProfileKind profile, string graphicsApi)
        {
            Detected = (hooks & GraphicsHook.ReShade) != 0;
            Detail = detail ?? "";
            bool wasActive = GuardActive;
            GuardActive = mode == ReShadeGuardMode.On || (mode == ReShadeGuardMode.Auto && Detected);

            Advice.Clear();
            if (Detected && profile != ProfileKind.ReShadeCompatible)
                Advice.Add("ReShade detected: the ReShadeCompatible profile keeps render targets stable for depth-based effects.");
            if (Detected && !string.IsNullOrEmpty(graphicsApi) && graphicsApi.IndexOf("Direct3D11", System.StringComparison.OrdinalIgnoreCase) < 0)
                Advice.Add($"Graphics API is {graphicsApi}; ReShade support for it depends on your ReShade version and install method.");
            if (Detected && mode == ReShadeGuardMode.Off)
                Advice.Add("ReShade guard is Off: MSAA/HDR/mirror/adaptive render-scale changes may break depth-based effects.");
            return wasActive != GuardActive;
        }

        public string Describe() =>
            Detected ? "detected" + (string.IsNullOrEmpty(Detail) ? "" : " (" + Detail + ")") + (GuardActive ? ", guard active" : "")
                     : GuardActive ? "not detected, guard forced on" : "not detected";
    }
}

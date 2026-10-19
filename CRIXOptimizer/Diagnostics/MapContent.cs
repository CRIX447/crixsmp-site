// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// What a loaded custom (mod.io) map contains that costs frame time, for the performance notes: realtime lights
    /// and their shadows, reflection probes, cameras rendering into textures, particle systems, renderers and
    /// triangles. Counted once, read-only, after the map has loaded (see MapContentScanner); nothing is changed.
    /// No Unity dependency (unit tested).
    /// </summary>
    internal sealed class MapContent
    {
        public string MapId = "";
        public int Scenes;
        public int Renderers;
        public int SkinnedRenderers;
        public int LodGroups;
        public long Triangles;
        public int Lights;
        public int RealtimeLights;
        public int ShadowedRealtimeLights;
        public int ShadowedPointLights;
        public int ReflectionProbes;
        public int RealtimeProbes;
        public int EveryFrameProbes;
        public readonly List<string> TextureCameras = new List<string>();
        public int ParticleSystems;
        public long MaxParticles;
        public int ParticleLightSystems;
        public double ScanMs;

        /// <summary>One line for the notes, e.g. "custom map 1234: 2 400 renderers, 1.9 M triangles, ...".</summary>
        public string Describe()
        {
            var parts = new List<string>(8)
            {
                Count(Renderers, "renderer") + (SkinnedRenderers > 0 ? " (" + SkinnedRenderers.ToString(CultureInfo.InvariantCulture) + " skinned)" : ""),
                Triangles >= 1_000_000
                    ? (Triangles / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M triangles"
                    : (Triangles / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k triangles",
                Count(LodGroups, "LOD group"),
            };
            string lights = Count(Lights, "light");
            if (Lights > 0)
            {
                lights += " (" + RealtimeLights.ToString(CultureInfo.InvariantCulture) + " realtime";
                if (ShadowedRealtimeLights > 0)
                    lights += ", " + ShadowedRealtimeLights.ToString(CultureInfo.InvariantCulture) + " with shadows" +
                              (ShadowedPointLights > 0 ? ", " + ShadowedPointLights.ToString(CultureInfo.InvariantCulture) + " of them point lights (6 shadow renders each)" : "");
                lights += ")";
            }
            parts.Add(lights);
            string probes = Count(ReflectionProbes, "reflection probe");
            if (RealtimeProbes > 0)
                probes += " (" + RealtimeProbes.ToString(CultureInfo.InvariantCulture) + " realtime" +
                          (EveryFrameProbes > 0 ? ", " + EveryFrameProbes.ToString(CultureInfo.InvariantCulture) + " every frame" : "") + ")";
            parts.Add(probes);
            parts.Add(Count(TextureCameras.Count, "camera") + " rendering into textures" +
                      (TextureCameras.Count > 0 ? " (" + string.Join(", ", TextureCameras.ToArray()) + ")" : ""));
            parts.Add(Count(ParticleSystems, "particle system") + (ParticleSystems > 0
                ? " (up to " + MaxParticles.ToString(CultureInfo.InvariantCulture) + " particles" +
                  (ParticleLightSystems > 0 ? ", " + ParticleLightSystems.ToString(CultureInfo.InvariantCulture) + " with lights" : "") + ")"
                : ""));
            var sb = new StringBuilder();
            sb.Append("custom map ").Append(MapId.Length > 0 ? MapId : "?").Append(" contains: ").Append(string.Join(", ", parts.ToArray()));
            sb.Append(string.Format(CultureInfo.InvariantCulture, " ({0} scene{1}, counted in {2:0} ms)", Scenes, Scenes == 1 ? "" : "s", ScanMs));
            return sb.ToString();
        }

        /// <summary>What the content means for frame time, for the notes; empty when nothing stands out.</summary>
        public List<string> Hints(bool realtimeShadowsPossible)
        {
            var hints = new List<string>(4);
            if (ShadowedRealtimeLights > 0 && realtimeShadowsPossible)
                hints.Add("realtime lights with shadows render the map again for each shadow");
            if (EveryFrameProbes > 0)
                hints.Add("reflection probes updating every frame render the map six more times each (CRIX's RealtimeReflectionProbes = Off stops realtime probes)");
            if (TextureCameras.Count > 0)
                hints.Add("each camera rendering into a texture (screens, mirrors) renders the map again; [Performance] ExtraCameraMaxFps limits how often");
            if (Triangles >= 5_000_000)
                hints.Add("a very detailed map: the GPU and CPU work per frame is high whatever CRIX changes; a lower eye resolution (MaxEyeMegapixels) helps most");
            if (MaxParticles >= 20_000)
                hints.Add("many particles: overdraw at headset resolution");
            return hints;
        }

        private static string Count(long n, string what) =>
            n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? "" : "s");
    }

    /// <summary>
    /// Camera renders per frame by kind, counted in RenderPipelineManager.beginCameraRendering (read-only). Shows how
    /// many times a frame the scene is rendered (headset, desktop window, cameras into textures, reflection probes),
    /// which is the main cost a map can add. No Unity dependency here: the runtime feeds it.
    /// </summary>
    internal sealed class CameraRenderCounts
    {
        public int Frames;
        public int Headset;
        public int Window;
        public int Texture;
        public int Reflection;
        public int Other;

        public void Clear()
        {
            Frames = Headset = Window = Texture = Reflection = Other = 0;
        }

        /// <summary>e.g. "renders/frame: headset 1.0, window 1.0, textures 2.5, reflection 0.0"; empty with no frames.</summary>
        public string Describe()
        {
            if (Frames <= 0)
                return "";
            double f = Frames;
            return string.Format(CultureInfo.InvariantCulture, "renders/frame: headset {0:0.0}, window {1:0.0}, textures {2:0.0}, reflection {3:0.0}{4}",
                Headset / f, Window / f, Texture / f, Reflection / f, Other > 0 ? string.Format(CultureInfo.InvariantCulture, ", other {0:0.0}", Other / f) : "");
        }
    }
}

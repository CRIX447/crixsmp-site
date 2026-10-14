// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using CRIXOptimizer.Core;
using CRIXOptimizer.Utilities;
using UnityEngine;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Optional ([Performance] ExtraCameraMaxFps, off by default): limits how often cameras that render into a
    /// texture (minimaps, tablets, spectator displays added by mods such as Sakuraa Client) render the scene. Each
    /// such camera draws the whole scene again every frame; between its updates the texture keeps the last image.
    ///
    /// Only Camera.enabled is toggled, frame-staggered. Never touched: VR cameras, cameras that draw to the screen,
    /// cameras whose name matches the exclusion list (mirrors, the game's LIV camera, photo booth by default), and
    /// any camera whose owner turns it on or off itself (CRIX then stops managing it and leaves it as the owner set
    /// it). Everything is handed back enabled when the option is turned off, on F10 and on quit.
    /// </summary>
    internal sealed class ExtraCameraLimiter
    {
        private sealed class Managed
        {
            public Camera Camera;
            public string Name;
            public int Hash;
            public bool LastSet;
        }

        private readonly ILogSink log;
        private readonly List<Managed> managed = new List<Managed>();
        private readonly HashSet<Camera> released = new HashSet<Camera>();
        private Camera[] buffer = new Camera[16];
        private string[] exclusions = new string[0];

        public float MaxFps { get; private set; }
        public int ManagedCount => managed.Count;
        public string Status { get; private set; } = "off";

        public ExtraCameraLimiter(ILogSink log)
        {
            this.log = log ?? NullLogSink.Instance;
        }

        public void Configure(float maxFps, string exclusionList)
        {
            exclusions = (exclusionList ?? "").ToLowerInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < exclusions.Length; i++)
                exclusions[i] = exclusions[i].Trim();
            MaxFps = maxFps > 0f ? maxFps : 0f;
            if (MaxFps <= 0f)
                ReleaseAll("off");
        }

        /// <summary>Finds new candidate cameras (every few seconds; main thread).</summary>
        public void Discover()
        {
            if (MaxFps <= 0f)
                return;
            managed.RemoveAll(m => !UnityObjects.IsAlive(m.Camera));
            released.RemoveWhere(c => !UnityObjects.IsAlive(c));
            int count = Camera.allCamerasCount;
            if (buffer.Length < count)
                buffer = new Camera[Math.Max(count, buffer.Length * 2)];
            count = Camera.GetAllCameras(buffer);
            for (int i = 0; i < count; i++)
            {
                Camera cam = buffer[i];
                buffer[i] = null;
                // A camera with a target texture never renders to the headset (URP only uses XR for screen cameras),
                // whatever its stereoTargetEye says.
                if (!UnityObjects.IsAlive(cam) || !cam.enabled || cam.targetTexture == null || cam.stereoEnabled)
                    continue;
                if (released.Contains(cam) || managed.Exists(m => ReferenceEquals(m.Camera, cam)))
                    continue;
                string name = SafeName(cam);
                if (IsExcluded(name))
                    continue;
                managed.Add(new Managed { Camera = cam, Name = name, Hash = cam.GetInstanceID(), LastSet = true });
                log.Debug("Extra camera limited: " + name);
            }
            Status = managed.Count == 0 ? $"on ({MaxFps:0}/s), no render-texture cameras found"
                : $"on ({MaxFps:0}/s): " + string.Join(", ", managed.ConvertAll(m => m.Name).ToArray());
        }

        /// <summary>Per frame: enables each managed camera only on its frames. O(cameras), no allocation.</summary>
        public void Tick(int frame, double currentFps)
        {
            if (MaxFps <= 0f || managed.Count == 0)
                return;
            int interval = ThrottleMath.IntervalFrames(currentFps, MaxFps);
            for (int i = managed.Count - 1; i >= 0; i--)
            {
                Managed m = managed[i];
                if (!UnityObjects.IsAlive(m.Camera))
                {
                    managed.RemoveAt(i);
                    continue;
                }
                // Someone else switched it since our last write: it is theirs again.
                if (m.Camera.enabled != m.LastSet)
                {
                    released.Add(m.Camera);
                    managed.RemoveAt(i);
                    log.Info($"Extra camera '{m.Name}' is switched by its own mod or the game; CRIX stopped limiting it.");
                    continue;
                }
                // It now draws to the screen (e.g. the game's desktop camera after a camera takeover ends): hand it back.
                if (m.Camera.targetTexture == null)
                {
                    if (!m.LastSet)
                        m.Camera.enabled = true;
                    released.Add(m.Camera);
                    managed.RemoveAt(i);
                    log.Info($"Extra camera '{m.Name}' no longer renders into a texture; CRIX stopped limiting it.");
                    continue;
                }
                bool render = interval <= 1 || ThrottleMath.IsInPhase(frame, interval, m.Hash);
                if (render != m.LastSet)
                {
                    m.Camera.enabled = render;
                    m.LastSet = render;
                }
            }
        }

        /// <summary>Hands every camera back enabled (only those still in the state CRIX left them).</summary>
        public void ReleaseAll(string reason)
        {
            foreach (Managed m in managed)
            {
                if (UnityObjects.IsAlive(m.Camera) && !m.LastSet && !m.Camera.enabled)
                    m.Camera.enabled = true;
            }
            if (managed.Count > 0)
                log.Debug($"Extra camera limit released ({reason}).");
            managed.Clear();
            Status = MaxFps > 0f ? $"paused ({reason})" : "off";
        }

        private bool IsExcluded(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (string fragment in exclusions)
                if (fragment.Length > 0 && lower.Contains(fragment))
                    return true;
            return false;
        }

        private static string SafeName(Camera cam)
        {
            try
            {
                return cam.gameObject.name;
            }
            catch
            {
                return "camera";
            }
        }
    }
}

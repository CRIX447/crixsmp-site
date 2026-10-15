// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using CRIXOptimizer.Core;
using CRIXOptimizer.Utilities;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Optional ([Performance] ExtraCameraMaxFps, off by default): limits how often cameras that render into a
    /// texture (minimaps, tablets, spectator displays added by mods such as Sakuraa Client) render the scene. Each
    /// such camera draws the whole scene again every frame; between its renders the texture keeps the last image.
    ///
    /// How a render is skipped without taking the camera away from its owner: a player-loop step that runs after
    /// every script's Update/LateUpdate and directly before Unity collects the cameras to render
    /// (PostLateUpdate.FinishFrameRendering) switches off only the limited cameras that are on and not due this
    /// frame. Unity then builds the frame's camera list, and RenderPipelineManager.beginContextRendering (raised by
    /// URP after that list is built; URP renders only the cameras in it) switches exactly those cameras back on, so
    /// they still skip this frame. Every script callback during rendering (culling, begin/endCameraRendering,
    /// OnRenderObject, OnGUI) and every Update/LateUpdate therefore sees Camera.enabled as its owner set it, and an
    /// owner switching its camera off or on is respected. The only code that can see a limited camera switched off
    /// is code that runs between the step and CRIX's beginContextRendering handler: another mod's player-loop step
    /// inserted directly before FinishFrameRendering, and beginContextRendering handlers subscribed before CRIX's. endContextRendering and a step after FinishFrameRendering are
    /// fallbacks in case the begin event does not come.
    ///
    /// Fail-open: if CRIX's own Update stops (plugin disabled, suspended, faulted), the step does nothing; if the
    /// render pipeline's end event is not seen, the cameras are switched back on at the next step and the limiter
    /// stops. Never touched: cameras that render to the headset or the screen, and cameras whose name matches the
    /// exclusion list (mirrors, the game's LCK/LIV camera, photo booth by default). Needs a scriptable render
    /// pipeline (Gorilla Tag uses URP).
    /// </summary>
    internal sealed class ExtraCameraLimiter : IDisposable
    {
        private sealed class Managed
        {
            public Camera Camera;
            public string Name;
            public double Phase;
            public double Due;
        }

        private readonly ILogSink log;
        private readonly List<Managed> managed = new List<Managed>();
        private readonly List<Camera> skipped = new List<Camera>();
        private readonly PlayerLoopSystem.UpdateFunction beforeFrameRendering;
        private readonly PlayerLoopSystem.UpdateFunction afterFrameRendering;
        private readonly Action<ScriptableRenderContext, List<Camera>> beginContextRendering;
        private readonly Action<ScriptableRenderContext, List<Camera>> endContextRendering;
        private Camera[] buffer = new Camera[16];
        private double[] dues = new double[8];
        private double[] phases = new double[8];
        private bool[] eligible = new bool[8];
        private bool[] render = new bool[8];
        private string[] exclusions = new string[0];
        private bool installed;
        private bool active;
        private int activeFrame = -10;
        private double typicalFrameTime;
        private int frameTimeOutliers;
        private int stepFrame = -10;
        private int missedEndEvents;
        private int reinstalls;
        private string stoppedReason;
        private bool uninstallPending;
        private int discovered;
        private string status = "off";

        public float MaxFps { get; private set; }
        public int ManagedCount => managed.Count;

        /// <summary>Shown in the system report. "paused" while CRIX's update does not allow limiting (disabled,
        /// suspended, faulted): cameras then render normally.</summary>
        public string Status => MaxFps > 0f && stoppedReason == null && installed && !active
            ? "paused (CRIX disabled, suspended or faulted; cameras render normally)"
            : status;

        public ExtraCameraLimiter(ILogSink log)
        {
            this.log = log ?? NullLogSink.Instance;
            beforeFrameRendering = BeforeFrameRendering;
            afterFrameRendering = AfterFrameRendering;
            beginContextRendering = OnBeginContextRendering;
            endContextRendering = OnEndContextRendering;
        }

        /// <summary>Applies the config (main thread). 0 = off: removes the player-loop step, nothing stays changed.</summary>
        public void Configure(float maxFps, string exclusionList)
        {
            exclusions = (exclusionList ?? "").ToLowerInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < exclusions.Length; i++)
                exclusions[i] = exclusions[i].Trim();
            managed.RemoveAll(m => IsExcluded(m.Name));
            MaxFps = maxFps > 0f ? maxFps : 0f;
            if (MaxFps <= 0f)
            {
                Uninstall();
                managed.Clear();
                stoppedReason = null; // setting it again later tries again
                reinstalls = 0;
                missedEndEvents = 0;
                status = "off";
                return;
            }
            if (stoppedReason != null)
            {
                status = "stopped: " + stoppedReason;
                return;
            }
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                status = "unavailable: needs a scriptable render pipeline (URP)";
                return;
            }
            Install();
            if (stoppedReason == null)
                UpdateStatus();
        }

        /// <summary>Per frame from CRIX's Update: whether limiting is allowed this frame (CRIX on, not suspended).</summary>
        public void Tick(bool allowed)
        {
            active = allowed;
            activeFrame = Time.frameCount;
            if (uninstallPending)
            {
                // A stop raised inside a player-loop step: the loop is changed here, never from inside the step.
                uninstallPending = false;
                Uninstall();
            }
        }

        /// <summary>Finds new render-texture cameras (every few seconds; main thread).</summary>
        public void Discover()
        {
            if (MaxFps <= 0f || stoppedReason != null)
                return;
            if (!installed)
            {
                // The render pipeline may not be active yet when CRIX starts: try again here.
                if (GraphicsSettings.currentRenderPipeline == null)
                {
                    status = "unavailable: needs a scriptable render pipeline (URP)";
                    return;
                }
                Install();
                if (!installed)
                    return;
            }
            managed.RemoveAll(m => !UnityObjects.IsAlive(m.Camera));
            CheckStepRuns();
            if (stoppedReason != null || !installed)
                return; // stopped or the re-install failed: do not refill the list or overwrite the status
            int count = Camera.allCamerasCount;
            if (buffer.Length < count)
                buffer = new Camera[Math.Max(count, buffer.Length * 2)];
            count = Camera.GetAllCameras(buffer);
            double now = Time.unscaledTimeAsDouble;
            for (int i = 0; i < count; i++)
            {
                Camera cam = buffer[i];
                buffer[i] = null;
                // A camera with a target texture never renders to the headset (URP only uses XR for screen cameras).
                if (!UnityObjects.IsAlive(cam) || !cam.enabled || cam.targetTexture == null || cam.stereoEnabled)
                    continue;
                if (managed.Exists(m => ReferenceEquals(m.Camera, cam)))
                    continue;
                string name = SafeName(cam);
                if (IsExcluded(name))
                    continue;
                // Phases by discovery order (golden-ratio spacing), so cameras a mod creates together are spread out.
                double phase = RenderRateSchedule.PhaseFor(discovered++);
                managed.Add(new Managed { Camera = cam, Name = name, Phase = phase, Due = RenderRateSchedule.InitialDue(now, 1.0 / MaxFps, phase) });
                log.Debug("Extra camera limited: " + name);
            }
            UpdateStatus();
        }

        /// <summary>Stops limiting all cameras now (suspend, restore). They are rediscovered when allowed again.</summary>
        public void ReleaseAll(string reason)
        {
            RestoreSkipped();
            if (managed.Count > 0)
                log.Debug($"Extra camera limit released ({reason}).");
            managed.Clear();
            if (MaxFps > 0f && stoppedReason == null)
                status = $"paused ({reason})";
        }

        public void Dispose()
        {
            RestoreSkipped();
            Uninstall();
            managed.Clear();
        }

        // ------------------------------------------------------------------ render-time steps

        private void BeforeFrameRendering()
        {
            try
            {
                if (skipped.Count > 0)
                    RestoreSkipped(); // not expected (the step after rendering restores); never carry it over
                stepFrame = Time.frameCount;
                // Kept up to date every frame; a hitch frame does not inflate it (see SmoothFrameTime).
                typicalFrameTime = RenderRateSchedule.SmoothFrameTime(typicalFrameTime, ref frameTimeOutliers, Time.unscaledDeltaTime);
                // Only while CRIX's own Update ran this frame and allows it (fail-open when CRIX stops).
                if (stoppedReason != null || !active || activeFrame != stepFrame || managed.Count == 0 || MaxFps <= 0f)
                    return;
                double now = Time.unscaledTimeAsDouble;
                double period = 1.0 / MaxFps;
                double frameTime = typicalFrameTime;
                for (int i = managed.Count - 1; i >= 0; i--)
                {
                    Managed m = managed[i];
                    Camera cam = m.Camera;
                    if (!UnityObjects.IsAlive(cam))
                    {
                        managed.RemoveAt(i);
                        continue;
                    }
                    if (cam.targetTexture == null)
                    {
                        // It draws to the screen now (e.g. a camera handed back after a takeover): not ours to limit.
                        managed.RemoveAt(i);
                        log.Info($"Extra camera '{m.Name}' no longer renders into a texture; CRIX stopped limiting it.");
                    }
                }
                int count = managed.Count;
                if (dues.Length < count)
                {
                    dues = new double[count * 2];
                    phases = new double[count * 2];
                    eligible = new bool[count * 2];
                    render = new bool[count * 2];
                }
                for (int i = 0; i < count; i++)
                {
                    dues[i] = managed[i].Due;
                    phases[i] = managed[i].Phase;
                    eligible[i] = managed[i].Camera.enabled; // switched off by its owner: not ours to decide
                }
                RenderRateSchedule.Plan(dues, phases, eligible, render, count, now, period, frameTime);
                for (int i = 0; i < count; i++)
                {
                    Managed m = managed[i];
                    m.Due = dues[i];
                    if (!eligible[i] || render[i])
                        continue;
                    m.Camera.enabled = false;
                    skipped.Add(m.Camera);
                }
            }
            catch (Exception e)
            {
                RestoreSkipped();
                Stop("error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Directly after FinishFrameRendering: normally nothing left; if the render pipeline's events did
        /// not come, hand the cameras back here and stop after three such frames.</summary>
        private void AfterFrameRendering()
        {
            try
            {
                if (skipped.Count == 0)
                    return;
                RestoreSkipped();
                if (++missedEndEvents >= 3)
                    Stop("the render pipeline's begin/end-of-rendering events were not seen");
            }
            catch (Exception e)
            {
                Stop("error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>The frame's camera list is built: switch the skipped cameras back on (they still skip this frame).</summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            try
            {
                if (skipped.Count > 0)
                {
                    RestoreSkipped();
                    missedEndEvents = 0;
                }
            }
            catch (Exception e)
            {
                Stop("error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            try
            {
                if (skipped.Count > 0)
                {
                    RestoreSkipped();
                    missedEndEvents = 0;
                }
            }
            catch (Exception e)
            {
                Stop("error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Switches back on exactly the cameras this frame's step switched off.</summary>
        private void RestoreSkipped()
        {
            for (int i = 0; i < skipped.Count; i++)
            {
                Camera cam = skipped[i];
                try
                {
                    if (UnityObjects.IsAlive(cam) && !cam.enabled)
                        cam.enabled = true;
                }
                catch
                {
                    // destroyed while switched off: nothing to restore
                }
            }
            skipped.Clear();
        }

        // ------------------------------------------------------------------ installation

        private void Install()
        {
            if (installed)
                return;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopHook.Remove(ref root, typeof(ExtraCameraLimiter));
            var before = new PlayerLoopSystem { type = typeof(ExtraCameraLimiter), updateDelegate = beforeFrameRendering };
            var after = new PlayerLoopSystem { type = typeof(ExtraCameraLimiter), updateDelegate = afterFrameRendering };
            Type finish = typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering);
            if (!PlayerLoopHook.InsertBefore(ref root, finish, before) || !PlayerLoopHook.InsertAfter(ref root, finish, after))
            {
                Stop("Unity's FinishFrameRendering step was not found in the player loop");
                uninstallPending = false;
                return;
            }
            PlayerLoop.SetPlayerLoop(root);
            RenderPipelineManager.beginContextRendering += beginContextRendering;
            RenderPipelineManager.endContextRendering += endContextRendering;
            installed = true;
            stepFrame = Time.frameCount;
        }

        private void Uninstall()
        {
            if (!installed)
                return;
            installed = false;
            RenderPipelineManager.beginContextRendering -= beginContextRendering;
            RenderPipelineManager.endContextRendering -= endContextRendering;
            RestoreSkipped();
            try
            {
                PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
                if (PlayerLoopHook.Remove(ref root, typeof(ExtraCameraLimiter)) > 0)
                    PlayerLoop.SetPlayerLoop(root);
            }
            catch (Exception e)
            {
                log.Warn("Extra camera limit: removing the player-loop step failed: " + e.Message);
            }
        }

        /// <summary>Another mod may replace the player loop and drop the step; put it back (a few times at most).</summary>
        private void CheckStepRuns()
        {
            if (Time.frameCount - stepFrame < 30)
                return;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (PlayerLoopHook.Contains(root, typeof(ExtraCameraLimiter)))
                return;
            if (++reinstalls > 3)
            {
                Stop("another mod keeps replacing Unity's player loop");
                return;
            }
            log.Info("Extra camera limit: the player-loop step was removed (another mod replaced the loop); adding it again.");
            installed = false;
            RenderPipelineManager.beginContextRendering -= beginContextRendering;
            RenderPipelineManager.endContextRendering -= endContextRendering;
            Install();
        }

        /// <summary>Stops limiting for this session (until ExtraCameraMaxFps is set to 0 and back). May run inside a
        /// player-loop step, so the loop itself is only changed later, from Tick.</summary>
        private void Stop(string reason)
        {
            if (stoppedReason == null)
                log.Warn("Extra camera limit stopped: " + reason + ". Cameras render normally.");
            stoppedReason = reason;
            status = "stopped: " + reason;
            managed.Clear();
            uninstallPending = installed;
        }

        private void UpdateStatus()
        {
            status = managed.Count == 0 ? $"on (at most {MaxFps:0}/s), no render-texture cameras found"
                : $"on (at most {MaxFps:0}/s): " + string.Join(", ", managed.ConvertAll(m => m.Name).ToArray());
        }

        private bool IsExcluded(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
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

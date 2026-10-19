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
    ///
    /// One camera can also be "pinned" with its own rate and cadence (the spectator view, see SpectatorCameraModule):
    /// it is planned on its own, so the other cameras never take its turn, and Discover never adopts it. If it is
    /// destroyed, or its render target is no longer the texture it was pinned with, the pin is dropped without touching
    /// the camera and the owner of the pin is told (TryTakePinLost).
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
        private bool respace = true;
        private int stepFrame = -10;
        private int missedEndEvents;
        private int reinstalls;
        private string stoppedReason;
        private bool uninstallPending;
        private string status = "off";

        // The pinned camera (spectator view).
        private Camera pinned;
        private RenderTexture pinnedTexture;
        private double pinnedPeriod;
        private bool pinnedEvenFrames;
        private bool pinnedForce;
        private int pinnedLastRender = -1;
        private string pinnedLabel = "";
        private string pinLost;
        private readonly double[] pinDue = new double[1];
        private readonly double[] pinPhase = new double[1];
        private readonly bool[] pinEligible = new bool[1];
        private readonly bool[] pinRender = new bool[1];
        private RenderRateSchedule.Cadence pinCadence;

        /// <summary>The headset's frame time (1 / refresh rate), 0 when unknown; keeps the even-frames cadence steady.</summary>
        public double NominalFrameTime { get; set; }

        public float MaxFps { get; private set; }
        public int ManagedCount => managed.Count;
        /// <summary>The limiter has work: ExtraCameraMaxFps is set or a camera is pinned.</summary>
        public bool Wanted => MaxFps > 0f || pinned != null;
        /// <summary>Even-frames cadence of the pinned camera this frame (1 = every frame).</summary>
        public int PinnedEveryNth { get; private set; } = 1;
        public bool IsPinned(Camera cam) => pinned != null && ReferenceEquals(pinned, cam);

        /// <summary>Shown in the system report. "paused" while CRIX's update does not allow limiting (disabled,
        /// suspended, faulted): cameras then render normally.</summary>
        public string Status
        {
            get
            {
                bool paused = Wanted && stoppedReason == null && installed && !active;
                string text = paused ? "paused (CRIX disabled, suspended or faulted; cameras render normally)" : status;
                if (pinned == null)
                    return text;
                string rate = pinnedEvenFrames ? (PinnedEveryNth <= 1 ? "every frame" : "every " + Ordinal(PinnedEveryNth) + " frame")
                    : $"at most {1.0 / pinnedPeriod:0}/s";
                string pin = "spectator: " + pinnedLabel + " (" + rate + ")";
                return MaxFps > 0f || paused ? text + "; " + pin : pin;
            }
        }

        /// <summary>CRIX's Update should call Tick: something is limited or pinned, or an uninstall is still pending.</summary>
        public bool NeedsTick => Wanted || uninstallPending;

        private static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");

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
            if (managed.RemoveAll(m => IsExcluded(m.Name)) > 0)
                respace = true;
            float newMax = maxFps > 0f ? maxFps : 0f;
            if (newMax != MaxFps)
                respace = true;
            MaxFps = newMax;
            if (MaxFps <= 0f)
            {
                managed.Clear();
                status = "off";
                if (pinned != null)
                    return; // the pinned camera keeps the step installed
                Uninstall();
                stoppedReason = null; // setting it again later tries again
                reinstalls = 0;
                missedEndEvents = 0;
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

        /// <summary>
        /// Pins one camera (main thread): from the next step it renders at most once per <paramref name="period"/>
        /// seconds, every Nth frame when <paramref name="evenFrames"/>, and on the next frame in any case. It must already
        /// render into <paramref name="texture"/>. False (with the reason in Status) when the limiter cannot run.
        /// </summary>
        public bool Pin(Camera cam, RenderTexture texture, double period, bool evenFrames, string label)
        {
            if (!UnityObjects.IsAlive(cam) || texture == null || !(period > 0))
                return false;
            if (stoppedReason != null)
                return false;
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                status = "unavailable: needs a scriptable render pipeline (URP)";
                return false;
            }
            if (managed.RemoveAll(m => ReferenceEquals(m.Camera, cam)) > 0)
                respace = true;
            bool samePin = ReferenceEquals(pinned, cam) && ReferenceEquals(pinnedTexture, texture);
            pinned = cam;
            pinnedTexture = texture;
            pinnedPeriod = period;
            pinnedEvenFrames = evenFrames;
            pinnedLabel = label ?? SafeName(cam);
            pinLost = null;
            if (!samePin)
            {
                pinnedForce = true;
                pinnedLastRender = -1;
                pinCadence = default;
                pinDue[0] = RenderRateSchedule.InitialDue(Time.unscaledTimeAsDouble, period, 0);
            }
            Install();
            if (!installed || stoppedReason != null)
            {
                pinned = null;
                pinnedTexture = null;
                return false;
            }
            return true;
        }

        /// <summary>Renders the pinned camera on the next frame whatever the cadence (new texture, resize).</summary>
        public void RequestRender() => pinnedForce = true;

        /// <summary>Drops the pin (main thread). The camera itself is not touched.</summary>
        public void Unpin()
        {
            if (pinned == null)
                return;
            RestoreSkipped();
            pinned = null;
            pinnedTexture = null;
            pinLost = null;
            if (MaxFps <= 0f)
                Uninstall();
        }

        /// <summary>The pin was dropped inside a render step (camera destroyed, render target changed, limiter stopped).</summary>
        public bool TryTakePinLost(out string reason)
        {
            reason = pinLost;
            pinLost = null;
            return reason != null;
        }

        /// <summary>Finds new render-texture cameras (every few seconds; main thread). Also re-installs the step if another
        /// mod removed it, while anything is limited or pinned.</summary>
        public void Discover()
        {
            if (!Wanted || stoppedReason != null)
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
            if (managed.RemoveAll(m => !UnityObjects.IsAlive(m.Camera)) > 0)
                respace = true;
            CheckStepRuns();
            if (stoppedReason != null || !installed)
                return; // stopped or the re-install failed: do not refill the list or overwrite the status
            if (MaxFps <= 0f)
                return; // only the pinned camera
            int count = Camera.allCamerasCount;
            if (buffer.Length < count)
                buffer = new Camera[Math.Max(count, buffer.Length * 2)];
            count = Camera.GetAllCameras(buffer);
            for (int i = 0; i < count; i++)
            {
                Camera cam = buffer[i];
                buffer[i] = null;
                // A camera with a target texture never renders to the headset (URP only uses XR for screen cameras).
                if (!UnityObjects.IsAlive(cam) || !cam.enabled || cam.targetTexture == null || cam.stereoEnabled)
                    continue;
                if (managed.Exists(m => ReferenceEquals(m.Camera, cam)) || IsPinned(cam))
                    continue;
                string name = SafeName(cam);
                if (IsExcluded(name))
                    continue;
                // Phases and due times are (re)assigned evenly over the period at the next step (Respace).
                managed.Add(new Managed { Camera = cam, Name = name });
                respace = true;
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
            pinned = null;
            pinnedTexture = null;
        }

        // ------------------------------------------------------------------ render-time steps

        private void BeforeFrameRendering()
        {
            try
            {
                if (skipped.Count > 0)
                    RestoreSkipped(); // not expected (the step after rendering restores); never carry it over
                stepFrame = Time.frameCount;
                // The plan uses the typical frame time from before this frame (a hitch frame must not widen its own
                // budget); it is updated every frame, also when nothing is limited.
                double frameTime = Time.unscaledDeltaTime;
                double typicalBefore = typicalFrameTime;
                typicalFrameTime = RenderRateSchedule.SmoothFrameTime(typicalFrameTime, frameTime);
                // Only while CRIX's own Update ran this frame and allows it (fail-open when CRIX stops).
                if (stoppedReason != null || !active || activeFrame != stepFrame)
                    return;
                double now = Time.unscaledTimeAsDouble;
                if (pinned != null)
                    PlanPinned(now, frameTime, typicalBefore);
                if (managed.Count == 0 || MaxFps <= 0f)
                    return;
                double period = 1.0 / MaxFps;
                for (int i = managed.Count - 1; i >= 0; i--)
                {
                    Managed m = managed[i];
                    Camera cam = m.Camera;
                    if (!UnityObjects.IsAlive(cam))
                    {
                        managed.RemoveAt(i);
                        respace = true;
                        continue;
                    }
                    if (cam.targetTexture == null)
                    {
                        // It draws to the screen now (e.g. a camera handed back after a takeover): not ours to limit.
                        managed.RemoveAt(i);
                        respace = true;
                        log.Info($"Extra camera '{m.Name}' no longer renders into a texture; CRIX stopped limiting it.");
                    }
                }
                int count = managed.Count;
                if (respace)
                {
                    // Evenly spread over the period whenever the set of cameras or the limit changes.
                    for (int i = 0; i < count; i++)
                    {
                        managed[i].Phase = RenderRateSchedule.EvenPhase(i, count);
                        managed[i].Due = RenderRateSchedule.InitialDue(now, period, managed[i].Phase);
                    }
                    respace = false;
                }
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
                RenderRateSchedule.Plan(dues, phases, eligible, render, count, now, period, frameTime, typicalBefore);
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

        /// <summary>The pinned camera: render or skip this frame (inside the step).</summary>
        private void PlanPinned(double now, double frameTime, double typicalBefore)
        {
            Camera cam = pinned;
            if (!UnityObjects.IsAlive(cam))
            {
                LosePin("the camera was destroyed");
                return;
            }
            if (!ReferenceEquals(cam.targetTexture, pinnedTexture))
            {
                LosePin("its render target was changed by the game or another mod");
                return;
            }
            PinnedEveryNth = RenderRateSchedule.UpdateCadence(ref pinCadence, pinnedPeriod, typicalBefore, NominalFrameTime);
            if (!cam.enabled)
                return; // switched off by its owner: not ours to decide
            bool force = pinnedForce || pinnedLastRender < 0 || !pinnedTexture.IsCreated();
            bool renderNow;
            if (pinnedEvenFrames)
            {
                renderNow = RenderRateSchedule.EveryNthDue(stepFrame, pinnedLastRender, PinnedEveryNth, force);
            }
            else
            {
                pinPhase[0] = 0;
                pinEligible[0] = true;
                RenderRateSchedule.Plan(pinDue, pinPhase, pinEligible, pinRender, 1, now, pinnedPeriod, frameTime, typicalBefore);
                renderNow = force || pinRender[0];
            }
            if (renderNow)
            {
                pinnedForce = false;
                pinnedLastRender = stepFrame;
                return;
            }
            cam.enabled = false;
            skipped.Add(cam);
        }

        private void LosePin(string reason)
        {
            pinLost = reason;
            pinned = null;
            pinnedTexture = null;
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
            if (pinned != null)
                LosePin("CRIX's camera limiter stopped: " + reason);
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

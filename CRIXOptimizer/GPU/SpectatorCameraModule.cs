// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.Utilities;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Optional ([Performance] SpectatorCameraMaxFps, off by default): lets the desktop spectator view (the game's
    /// third-person "Shoulder Camera", which Sakuraa Client uses as its spectator camera) render the scene at most N
    /// times a second while the headset keeps its full rate, without the window or OBS flickering.
    ///
    /// A camera that draws straight into the window cannot simply skip frames: on a skipped frame the window would show
    /// the headset mirror instead (or an undefined image). So CRIX points the camera at a window-sized texture of its
    /// own, lets the camera limiter (ExtraCameraLimiter, "pinned" camera) render it every Nth frame, and draws the
    /// latest image into the window every frame, underneath every overlay canvas and all IMGUI (Sakuraa's HUD, CRIX's
    /// dashboard), opaquely. Only Camera.targetTexture is changed: its position, field of view, culling mask and every
    /// script on it stay as they are.
    ///
    /// The camera is handed back to the window (targetTexture null, only if it is still CRIX's texture) on: the setting
    /// at 0, CRIX suspended or disabled, quit, no headset, the DesktopCamera setting switching the camera off, a window
    /// resize (re-engaged after a second of stable size), HDR display output, another screen camera drawing after it
    /// (Sakuraa's replay camera), the camera not rendering into the texture, and any error. If the game or another mod
    /// sets its own render target on it, CRIX leaves it alone until the profile is re-applied. Not tested in Gorilla Tag.
    /// </summary>
    internal sealed class SpectatorCameraModule : IDisposable
    {
        private const float ResizeSettleSeconds = 1f;
        private const int MinWindowSize = 64;
        private const int FramesToFirstRender = 10;
        private const int ScanEveryFrames = 15;

        /// <summary>CRIX's spectator texture while one is in use (diagnostics treat a camera rendering into it as a
        /// desktop-window camera).</summary>
        public static RenderTexture ActiveTexture { get; private set; }
        public static bool IsSpectatorTexture(RenderTexture rt) => rt != null && ReferenceEquals(rt, ActiveTexture);

        private readonly ExtraCameraLimiter limiter;
        private readonly Func<float, Camera> findCamera;
        private readonly Func<Camera, string> ownerLabel;
        private readonly ILogSink log;
        private readonly Action<Camera> onEndCamera;
        private readonly Action<ScriptableRenderContext, Camera> endCameraRendering;
        private readonly List<RenderTexture> keptTextures = new List<RenderTexture>();
        private Camera[] buffer = new Camera[16];

        private Camera cam;
        private RenderTexture rt;
        private ISpectatorPresenter presenter;
        private bool engaged;
        private bool renderedSinceEngage;
        private int enabledFramesWithoutRender;
        private int engageFrame;
        private int nextScanFrame;
        private float maxFps;
        private bool evenFrames = true;
        private string handedBack;
        private string stopped;
        private int lastWidth = -1, lastHeight = -1;
        private float sizeChangedAt = -1e9f;
        private string label = "";

        public string Status { get; private set; } = "off ([Performance] SpectatorCameraMaxFps = 0)";
        public bool Engaged => engaged;
        public string TextureInfo { get; private set; } = "";

        public SpectatorCameraModule(ExtraCameraLimiter limiter, Func<float, Camera> findCamera, Func<Camera, string> ownerLabel, ILogSink log)
        {
            this.limiter = limiter;
            this.findCamera = findCamera;
            this.ownerLabel = ownerLabel ?? (_ => "");
            this.log = log ?? NullLogSink.Instance;
            onEndCamera = OnEndCamera;
            endCameraRendering = (context, camera) => onEndCamera(camera);
        }

        /// <summary>
        /// Main thread, about once a second and on every profile change. <paramref name="standBy"/>: why the limit must not
        /// run now (null = it may). Engages, adjusts or hands the camera back.
        /// </summary>
        public void Apply(float wantedMaxFps, SpectatorCadence cadence, SpectatorPresenterKind presenterKind, string standBy, float now)
        {
            if (stopped != null)
            {
                Status = "stopped for this session: " + stopped;
                return;
            }
            if (!(wantedMaxFps > 0f))
            {
                Release("off");
                Status = "off ([Performance] SpectatorCameraMaxFps = 0)";
                return;
            }
            if (standBy != null)
            {
                Release(standBy);
                Status = standBy;
                return;
            }
            if (handedBack != null)
            {
                Status = "handed back: " + handedBack + "; left alone until the profile is re-applied";
                return;
            }

            int width = Screen.width, height = Screen.height;
            if (width != lastWidth || height != lastHeight)
            {
                lastWidth = width;
                lastHeight = height;
                sizeChangedAt = now;
            }
            if (width < MinWindowSize || height < MinWindowSize)
            {
                Release("window minimised");
                Status = "paused: window minimised";
                return;
            }
            if (engaged && (rt.width != width || rt.height != height))
                Release("window resized");
            if (!engaged && now - sizeChangedAt < ResizeSettleSeconds)
            {
                Status = "paused: window being resized";
                return;
            }
            if (HdrOutputActive())
            {
                Release("HDR display output");
                Status = "paused: HDR display output is on (a camera rendering into a texture loses it)";
                return;
            }

            bool wantEven = cadence == SpectatorCadence.EvenFrames;
            if (engaged)
            {
                if (wantedMaxFps != maxFps || wantEven != evenFrames)
                {
                    maxFps = wantedMaxFps;
                    evenFrames = wantEven;
                    limiter.Pin(cam, rt, 1.0 / maxFps, evenFrames, label);
                }
                Status = OnStatus();
                return;
            }

            Camera found = findCamera(now);
            if (!UnityObjects.IsAlive(found))
            {
                Status = "standing by: the spectator camera ('Shoulder Camera') was not found (yet)";
                return;
            }
            string refuse = Refuse(found, presenterKind);
            if (refuse != null)
            {
                Status = "standing by: " + refuse;
                return;
            }
            maxFps = wantedMaxFps;
            evenFrames = wantEven;
            Engage(found, presenterKind);
        }

        /// <summary>Per frame from CRIX's Update (main thread): hand-back and safety checks, presenter visibility.</summary>
        public void Tick()
        {
            if (!engaged)
                return;
            if (limiter.TryTakePinLost(out string lost))
            {
                bool ours = UnityObjects.IsAlive(cam) && ReferenceEquals(cam.targetTexture, rt);
                if (!ours && UnityObjects.IsAlive(cam))
                    handedBack = "the game or another mod set its own render target on the camera";
                else if (ours)
                    stopped = lost; // the limiter itself stopped: it stays stopped for the session
                Release(lost);
                return;
            }
            if (!UnityObjects.IsAlive(cam))
            {
                Release("the camera was destroyed");
                return;
            }
            if (!ReferenceEquals(cam.targetTexture, rt))
            {
                handedBack = "the game or another mod set its own render target on the camera";
                Release(handedBack);
                return;
            }
            if (!rt.IsCreated())
                limiter.RequestRender();
            // Switched off by its owner: show nothing, so the window shows what it shows today without CRIX.
            presenter.SetVisible(cam.isActiveAndEnabled);

            int frame = Time.frameCount;
            // Counted only on frames the camera was on (its owner may switch it off right after CRIX took it).
            if (!renderedSinceEngage && cam.isActiveAndEnabled && frame > engageFrame && ++enabledFramesWithoutRender > FramesToFirstRender)
            {
                stopped = "the camera did not render into CRIX's texture";
                Release(stopped);
                return;
            }
            if (frame >= nextScanFrame)
            {
                nextScanFrame = frame + ScanEveryFrames;
                string conflict = ScanOtherCameras(cam, rt, out bool sharesTexture);
                if (conflict != null)
                {
                    if (sharesTexture)
                        stopped = conflict;
                    Release(conflict, keepTexture: sharesTexture);
                }
            }
        }

        /// <summary>The profile was re-applied: a camera handed back to another owner may be used again.</summary>
        public void ResetHandBack() => handedBack = null;

        /// <summary>Hands the camera back to the window now (main thread). Safe to call when not engaged.</summary>
        public void Release(string reason, bool keepTexture = false)
        {
            if (!engaged)
                return;
            engaged = false;
            try
            {
                presenter?.SetVisible(false);
            }
            catch
            {
                // presenter already gone
            }
            limiter.Unpin();
            try
            {
                if (UnityObjects.IsAlive(cam) && ReferenceEquals(cam.targetTexture, rt))
                    cam.targetTexture = null;
            }
            catch (Exception e)
            {
                log.Warn("Spectator camera: handing the camera back failed: " + e.Message);
            }
            RenderPipelineManager.endCameraRendering -= endCameraRendering;
            try
            {
                presenter?.Dispose();
            }
            catch
            {
                // nothing left to clean up
            }
            presenter = null;
            ActiveTexture = null;
            if (rt != null)
            {
                if (keepTexture)
                {
                    // Another camera still renders into it: destroying it would send that camera to the window.
                    keptTextures.Add(rt);
                }
                else
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
            }
            rt = null;
            log.Info("Spectator camera handed back to the window (" + reason + ").");
            cam = null;
            TextureInfo = "";
            Status = "off: " + reason;
        }

        /// <summary>Never throws: used from error paths.</summary>
        public void EmergencyRelease()
        {
            try
            {
                Release("error");
            }
            catch
            {
                try
                {
                    if (UnityObjects.IsAlive(cam) && rt != null && ReferenceEquals(cam.targetTexture, rt))
                        cam.targetTexture = null;
                }
                catch
                {
                    // nothing more can be done
                }
                engaged = false;
                ActiveTexture = null;
            }
        }

        public void Dispose()
        {
            EmergencyRelease();
            foreach (RenderTexture kept in keptTextures)
            {
                try
                {
                    if (kept != null)
                        kept.Release();
                }
                catch
                {
                    // already gone
                }
            }
            keptTextures.Clear();
        }

        // ------------------------------------------------------------------ engage

        private string Refuse(Camera found, SpectatorPresenterKind kind)
        {
            if (!found.isActiveAndEnabled)
                return "the camera is switched off by its owner";
            if (found.targetTexture != null || CameraRoles.RendersToHeadset(found))
                return "the camera renders into a texture or to the headset (not CRIX's to change)";
            if (found.cameraType != CameraType.Game)
                return "not a game camera";
            Rect r = found.rect;
            if (Mathf.Abs(r.x) > 0.001f || Mathf.Abs(r.y) > 0.001f || Mathf.Abs(r.width - 1f) > 0.001f || Mathf.Abs(r.height - 1f) > 0.001f)
                return "the camera does not fill the whole window";
            if (kind == SpectatorPresenterKind.Blit && Display.main != null && Display.main.requiresSrgbBlitToBackbuffer)
                return "SpectatorCameraPresenter = Blit is not possible on this display (use Canvas)";
            if (kind == SpectatorPresenterKind.Blit)
            {
                Camera main = Camera.main;
                if (ReferenceEquals(main, found) || (main != null && main.targetTexture != null))
                    return "SpectatorCameraPresenter = Blit cannot reach the window here (use Canvas)";
            }
            string conflict = ScanOtherCameras(found, null, out _);
            return conflict;
        }

        private void Engage(Camera found, SpectatorPresenterKind kind)
        {
            int width = Screen.width, height = Screen.height;
            RenderTexture texture = CreateTexture(found, width, height, out string error);
            if (texture == null)
            {
                Status = "unavailable: " + error;
                return;
            }
            ISpectatorPresenter view = null;
            try
            {
                view = kind == SpectatorPresenterKind.Blit ? (ISpectatorPresenter)new BlitSpectatorPresenter(found) : new CanvasSpectatorPresenter();
                found.targetTexture = texture;
                label = "'" + SafeName(found) + "'" + (ownerLabel(found) is string owner && owner.Length > 0 ? " (" + owner + ")" : "");
                if (!limiter.Pin(found, texture, 1.0 / maxFps, evenFrames, label))
                {
                    // Stopped or unavailable for the session: do not create a texture again every second.
                    stopped = "the camera limiter cannot run (" + limiter.Status + ")";
                    throw new InvalidOperationException(stopped);
                }
                view.Show(texture);
            }
            catch (Exception e)
            {
                limiter.Unpin();
                try
                {
                    if (ReferenceEquals(found.targetTexture, texture))
                        found.targetTexture = null;
                    view?.Dispose();
                }
                catch
                {
                    // best effort
                }
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                Status = "unavailable: " + e.Message;
                log.Warn("Spectator camera limit could not start: " + e.Message);
                return;
            }
            cam = found;
            rt = texture;
            presenter = view;
            ActiveTexture = texture;
            engaged = true;
            renderedSinceEngage = false;
            enabledFramesWithoutRender = 0;
            engageFrame = Time.frameCount;
            nextScanFrame = engageFrame + ScanEveryFrames;
            RenderPipelineManager.endCameraRendering += endCameraRendering;
            TextureInfo = $"{width}x{height}, {texture.graphicsFormat}, MSAA {Math.Max(1, texture.antiAliasing)}x, presenter {kind}";
            Status = OnStatus();
            log.Info("Spectator camera limit on: " + label + ", " + TextureInfo + ". The window is updated every frame from CRIX's texture.");
        }

        private string OnStatus()
        {
            string rate = evenFrames
                ? (limiter.PinnedEveryNth <= 1 ? "every frame (the game runs at or below the limit)" : $"every {limiter.PinnedEveryNth} frames, at most {maxFps:0}/s")
                : $"at most {maxFps:0}/s";
            return "on: " + label + " renders " + rate + "; the window shows its latest image every frame";
        }

        /// <summary>
        /// A window-sized colour target without alpha (so drawing it can never let the headset mirror show through), with
        /// a depth buffer and the camera's MSAA. B10G11R11 keeps HDR values when the camera renders HDR and is as small
        /// as an 8-bit target.
        /// </summary>
        private static RenderTexture CreateTexture(Camera found, int width, int height, out string error)
        {
            error = null;
            try
            {
#pragma warning disable 618 // the GraphicsFormatUsage overload does not exist in older Unity reference assemblies
                GraphicsFormat color = GraphicsFormat.B10G11R11_UFloatPack32;
                if (!SystemInfo.IsFormatSupported(color, FormatUsage.Render) || !SystemInfo.IsFormatSupported(color, FormatUsage.Sample))
                {
                    error = "the graphics card does not support an alpha-free render texture format (B10G11R11)";
                    return null;
                }
#pragma warning restore 618
                var desc = new RenderTextureDescriptor(width, height, color, SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil))
                {
                    msaaSamples = found.allowMSAA ? Mathf.Clamp(QualitySettings.antiAliasing, 1, 8) : 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                var texture = new RenderTexture(desc)
                {
                    name = "CRIX Spectator",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (!texture.Create())
                {
                    UnityEngine.Object.Destroy(texture);
                    error = "the render texture could not be created";
                    return null;
                }
                // Black until the first render (it renders in the same frame).
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = texture;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = previous;
                return texture;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private void OnEndCamera(Camera camera)
        {
            try
            {
                if (engaged && ReferenceEquals(camera, cam) && ReferenceEquals(camera.targetTexture, rt))
                    renderedSinceEngage = true;
            }
            catch
            {
                // diagnostics only
            }
        }

        /// <summary>
        /// Another camera that would break the composite: one drawing to the window after the spectator (it would be
        /// covered), or one rendering into CRIX's texture (e.g. a recorder that copied the camera's settings).
        /// </summary>
        private string ScanOtherCameras(Camera spectator, RenderTexture ours, out bool sharesTexture)
        {
            sharesTexture = false;
            int count = Camera.allCamerasCount;
            if (buffer.Length < count)
                buffer = new Camera[Math.Max(count, buffer.Length * 2)];
            count = Camera.GetAllCameras(buffer);
            string conflict = null;
            int depth = (int)spectator.depth;
            for (int i = 0; i < count; i++)
            {
                Camera other = buffer[i];
                buffer[i] = null;
                if (conflict != null || ReferenceEquals(other, spectator) || !UnityObjects.IsAlive(other) || !other.enabled)
                    continue;
                RenderTexture target = other.targetTexture;
                if (ours != null && ReferenceEquals(target, ours))
                {
                    sharesTexture = true;
                    conflict = $"another camera ('{SafeName(other)}') renders into CRIX's spectator texture (e.g. a recorder that copied the camera's settings)";
                }
                else if (target == null && other.cameraType == CameraType.Game && (int)other.depth >= depth && !CameraRoles.RendersToHeadset(other))
                {
                    conflict = $"another camera ('{SafeName(other)}') draws to the window after it (e.g. Sakuraa's replay camera)";
                }
            }
            return conflict;
        }

        private static bool HdrOutputActive()
        {
            try
            {
                return HDROutputSettings.main != null && HDROutputSettings.main.active;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeName(Camera c)
        {
            try
            {
                return c.gameObject.name;
            }
            catch
            {
                return "camera";
            }
        }
    }

    internal interface ISpectatorPresenter : IDisposable
    {
        void Show(RenderTexture texture);
        void SetVisible(bool visible);
    }

    /// <summary>
    /// Draws the spectator texture as the lowest Screen Space Overlay canvas. While a headset is active the engine draws
    /// overlay canvases into the window after every camera (and never into the headset), in sorting order, and IMGUI
    /// after them; so the image sits where the camera's own image was, under Sakuraa's HUD canvases and every OnGUI.
    /// No Graphic component and no input: a raw CanvasRenderer with one quad. The texture has no alpha, so the default
    /// UI material draws it opaquely.
    /// </summary>
    internal sealed class CanvasSpectatorPresenter : ISpectatorPresenter
    {
        private GameObject root;
        private Canvas canvas;
        private CanvasRenderer renderer;
        private Mesh mesh;

        public void Show(RenderTexture texture)
        {
            root = new GameObject("CRIX Spectator Presenter");
            UnityEngine.Object.DontDestroyOnLoad(root);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MinValue;
            canvas.pixelPerfect = false;
            canvas.targetDisplay = 0;
            var image = new GameObject("Image", typeof(RectTransform));
            image.transform.SetParent(root.transform, false);
            renderer = image.AddComponent<CanvasRenderer>();
            float w = texture.width, h = texture.height;
            mesh = new Mesh { name = "CRIX Spectator Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            renderer.SetMesh(mesh);
            renderer.SetMaterial(Canvas.GetDefaultCanvasMaterial(), texture);
            renderer.SetColor(Color.white);
            renderer.cull = false;
        }

        public void SetVisible(bool visible)
        {
            if (canvas != null && canvas.enabled != visible)
                canvas.enabled = visible;
        }

        public void Dispose()
        {
            if (renderer != null)
                renderer.Clear();
            if (root != null)
                UnityEngine.Object.Destroy(root);
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
            root = null;
            canvas = null;
            renderer = null;
            mesh = null;
        }
    }

    /// <summary>
    /// Copies the spectator texture into the window at the end of URP's frame (endContextRendering), once per frame and
    /// only for the frame's main render (the one with the headset camera), before the engine draws overlay UI.
    /// </summary>
    internal sealed class BlitSpectatorPresenter : ISpectatorPresenter
    {
        private readonly Camera spectator;
        private readonly Action<ScriptableRenderContext, List<Camera>> onEnd;
        private RenderTexture texture;
        private bool visible = true;
        private int lastFrame = -1;

        public BlitSpectatorPresenter(Camera spectator)
        {
            this.spectator = spectator;
            onEnd = OnEndContextRendering;
        }

        public void Show(RenderTexture rt)
        {
            texture = rt;
            RenderPipelineManager.endContextRendering += onEnd;
        }

        public void SetVisible(bool value) => visible = value;

        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            try
            {
                if (!visible || texture == null || Time.frameCount == lastFrame || cameras == null)
                    return;
                bool mainRender = false;
                foreach (Camera c in cameras)
                {
                    if (c != null && (ReferenceEquals(c, spectator) || CameraRoles.RendersToHeadset(c)))
                    {
                        mainRender = true;
                        break;
                    }
                }
                if (!mainRender)
                    return;
                lastFrame = Time.frameCount;
                Graphics.Blit(texture, (RenderTexture)null);
            }
            catch
            {
                // the window shows the headset mirror for this frame
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.endContextRendering -= onEnd;
            texture = null;
        }
    }
}

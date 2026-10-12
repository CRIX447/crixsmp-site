// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;
using CRIXOptimizer.Adaptive;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.Diagnostics;
using CRIXOptimizer.Utilities;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Applies the engine-level parts of a profile through the settings ledger. Only documented, public Unity
    /// APIs are used. Built-in-pipeline-only settings are skipped under URP/HDRP (they would do nothing), and
    /// settings whose API is missing in the running Unity version are reported as unavailable.
    /// </summary>
    internal sealed class GraphicsSettingsModule
    {
        private readonly SettingsLedger ledger;
        private readonly ILogSink log;
        private readonly XRRuntimeInfo xr;
        private readonly List<string> notes = new List<string>();

        private TrackedSetting<float> renderScale;
        private TrackedSetting<ShadowQuality> shadows;
        private TrackedSetting<float> shadowDistance;
        private TrackedSetting<ShadowResolution> shadowResolution;
        private TrackedSetting<int> shadowCascades;
        private TrackedSetting<int> msaa;
        private TrackedSetting<AnisotropicFiltering> anisotropic;
        private TrackedSetting<float> lodBias;
        private TrackedSetting<int> maxLodLevel;
        private TrackedSetting<int> pixelLights;
        private TrackedSetting<int> textureLimit;
        private TrackedSetting<bool> reflectionProbes;
        private TrackedSetting<bool> softParticles;
        private TrackedSetting<int> particleBudget;
        private TrackedSetting<SkinWeights> skinWeights;
        private TrackedSetting<bool> occlusionMesh;
        private TrackedSetting<int> mirror;
        private TrackedSetting<bool> cameraHdr;
        private Camera trackedCamera;
        private TrackedSetting<int> asyncUploadSlice;
        private TrackedSetting<ThreadPriority> backgroundLoading;
        private TrackedSetting<ulong> incrementalGcSlice;

        private int mirrorLeftEye, mirrorRightEye, mirrorSideBySide, mirrorNone;
        private bool mirrorConstantsResolved;

        public PipelineKind Pipeline { get; private set; }
        public string PipelineAssetType { get; private set; } = "none";
        public string TextureLimitApi { get; private set; } = "unavailable";
        public bool IncrementalGcAvailable { get; private set; }
        public IReadOnlyList<string> Notes => notes;

        /// <summary>Lever values the profile resolved to (before adaptive changes). Adaptive mode scales from these.</summary>
        public QualityLevers ProfileLevers { get; private set; }
        public QualityLevers AppliedLevers { get; private set; }

        public GraphicsSettingsModule(SettingsLedger ledger, ILogSink log, XRRuntimeInfo xr)
        {
            this.ledger = ledger;
            this.log = log;
            this.xr = xr;
        }

        public void Initialize()
        {
            Pipeline = RenderPipelineInfo.Detect(out object asset, out string assetType);
            PipelineAssetType = assetType;
            var approx = ApproxFloatComparer.Default;

            renderScale = Track(new TrackedSetting<float>("XR eye texture scale",
                () => XRSettings.eyeTextureResolutionScale, v => XRSettings.eyeTextureResolutionScale = v, approx, F2));

            if (Pipeline == PipelineKind.BuiltIn)
            {
                shadows = Track(new TrackedSetting<ShadowQuality>("Shadow quality", () => QualitySettings.shadows, v => QualitySettings.shadows = v));
                shadowDistance = Track(new TrackedSetting<float>("Shadow distance", () => QualitySettings.shadowDistance, v => QualitySettings.shadowDistance = v, approx, F1));
                shadowResolution = Track(new TrackedSetting<ShadowResolution>("Shadow resolution", () => QualitySettings.shadowResolution, v => QualitySettings.shadowResolution = v));
                shadowCascades = Track(new TrackedSetting<int>("Shadow cascades", () => QualitySettings.shadowCascades, v => QualitySettings.shadowCascades = v));
                msaa = Track(new TrackedSetting<int>("MSAA samples", () => QualitySettings.antiAliasing, v => QualitySettings.antiAliasing = v));
                pixelLights = Track(new TrackedSetting<int>("Pixel light count", () => QualitySettings.pixelLightCount, v => QualitySettings.pixelLightCount = v));
                softParticles = Track(new TrackedSetting<bool>("Soft particles", () => QualitySettings.softParticles, v => QualitySettings.softParticles = v));
            }
            else
            {
                notes.Add($"{RenderPipelineInfo.Describe(Pipeline)} detected ({assetType}): built-in-only quality settings (shadow quality/resolution/cascades, MSAA, pixel lights, soft particles) are not changed.");
                // URP exposes shadow distance on its asset as a public property; use it if present.
                Func<float> urpGet = Accessors.BoundGetter<float>(asset, "shadowDistance");
                Action<float> urpSet = Accessors.BoundSetter<float>(asset, "shadowDistance");
                if (Pipeline == PipelineKind.Universal && urpGet != null && urpSet != null)
                    shadowDistance = Track(new TrackedSetting<float>("Shadow distance (URP asset)", urpGet, urpSet, approx, F1));
            }

            anisotropic = Track(new TrackedSetting<AnisotropicFiltering>("Anisotropic filtering", () => QualitySettings.anisotropicFiltering, v => QualitySettings.anisotropicFiltering = v));
            lodBias = Track(new TrackedSetting<float>("LOD bias", () => QualitySettings.lodBias, v => QualitySettings.lodBias = v, approx, F2));
            maxLodLevel = Track(new TrackedSetting<int>("Maximum LOD level", () => QualitySettings.maximumLODLevel, v => QualitySettings.maximumLODLevel = v));
            reflectionProbes = Track(new TrackedSetting<bool>("Realtime reflection probes", () => QualitySettings.realtimeReflectionProbes, v => QualitySettings.realtimeReflectionProbes = v));
            particleBudget = Track(new TrackedSetting<int>("Particle raycast budget", () => QualitySettings.particleRaycastBudget, v => QualitySettings.particleRaycastBudget = v));
            skinWeights = Track(new TrackedSetting<SkinWeights>("Skin weights", () => QualitySettings.skinWeights, v => QualitySettings.skinWeights = v));

            // Unity 2022.2 replaced masterTextureLimit with globalTextureMipmapLimit; prefer the new API when present.
            Func<int> mipGet = Accessors.StaticGetter<int>(typeof(QualitySettings), "globalTextureMipmapLimit");
            Action<int> mipSet = Accessors.StaticSetter<int>(typeof(QualitySettings), "globalTextureMipmapLimit");
            if (mipGet != null && mipSet != null)
            {
                TextureLimitApi = "QualitySettings.globalTextureMipmapLimit";
                textureLimit = Track(new TrackedSetting<int>("Texture mip limit", mipGet, mipSet));
            }
            else
            {
                TextureLimitApi = "QualitySettings.masterTextureLimit";
                textureLimit = Track(new TrackedSetting<int>("Texture mip limit", () => QualitySettings.masterTextureLimit, v => QualitySettings.masterTextureLimit = v));
            }

            occlusionMesh = Track(new TrackedSetting<bool>("XR occlusion mesh", () => XRSettings.useOcclusionMesh, v => XRSettings.useOcclusionMesh = v));
            mirror = Track(new TrackedSetting<int>("Desktop mirror mode", ReadMirror, WriteMirror, null, DescribeMirror));

            asyncUploadSlice = Track(new TrackedSetting<int>("Async upload time slice (ms)", () => QualitySettings.asyncUploadTimeSlice, v => QualitySettings.asyncUploadTimeSlice = v));
            backgroundLoading = Track(new TrackedSetting<ThreadPriority>("Background loading priority", () => Application.backgroundLoadingPriority, v => Application.backgroundLoadingPriority = v));

            try
            {
                IncrementalGcAvailable = GarbageCollector.isIncremental;
            }
            catch
            {
                IncrementalGcAvailable = false;
            }
            if (IncrementalGcAvailable)
                incrementalGcSlice = Track(new TrackedSetting<ulong>("Incremental GC time slice (ns)",
                    () => GarbageCollector.incrementalTimeSliceNanoseconds, v => GarbageCollector.incrementalTimeSliceNanoseconds = v));
        }

        private TrackedSetting<T> Track<T>(TrackedSetting<T> setting) => ledger.Register(setting);

        /// <summary>Applies a resolved profile plus the current adaptive level. Writes only values that differ.</summary>
        public void Apply(OptimizationSettings s, bool graphicsEnabled, bool pacingEnabled, int adaptiveLevel, QualityLadderSettings ladder, bool cpuBound)
        {
            xr.Refresh(Time.unscaledTime);
            bool vr = xr.IsVrActive;

            if (!graphicsEnabled || s == null)
            {
                ReleaseGraphics();
            }
            else
            {
                // ---- profile baseline for the adaptive levers ----
                var levers = new QualityLevers();
                float gameScale = 0f, gameShadow = 0f, gameLod = 0f;
                int gameLights = 0;
                if (vr && renderScale.TryGetBaseline(out gameScale))
                {
                    levers.HasRenderScale = true;
                    levers.RenderScale = s.RenderScale > 0f ? s.RenderScale : gameScale;
                }
                if (shadowDistance != null && shadowDistance.TryGetBaseline(out gameShadow))
                {
                    levers.HasShadowDistance = true;
                    levers.ShadowDistance = s.ShadowDistanceLimit >= 0f ? Math.Min(gameShadow, s.ShadowDistanceLimit) : gameShadow;
                }
                if (lodBias.TryGetBaseline(out gameLod))
                {
                    levers.HasLodBias = true;
                    levers.LodBias = s.LodBiasLimit > 0f ? Math.Min(gameLod, s.LodBiasLimit) : gameLod;
                }
                if (pixelLights != null && pixelLights.TryGetBaseline(out gameLights))
                {
                    levers.HasPixelLights = true;
                    levers.PixelLightCount = s.PixelLightLimit >= 0 ? Math.Min(gameLights, s.PixelLightLimit) : gameLights;
                }
                ProfileLevers = levers;

                QualityLevers target = adaptiveLevel == 0 ? levers : QualityLadder.Compute(levers, adaptiveLevel, ladder, cpuBound);
                AppliedLevers = target;

                // ---- levers (profile + adaptive): only managed when they differ from the game's own value ----
                ApplyOrRelease(renderScale, target.HasRenderScale && Differs(target.RenderScale, gameScale), target.RenderScale);
                ApplyOrRelease(shadowDistance, target.HasShadowDistance && Differs(target.ShadowDistance, gameShadow), target.ShadowDistance);
                ApplyOrRelease(lodBias, target.HasLodBias && Differs(target.LodBias, gameLod), target.LodBias);
                ApplyOrRelease(pixelLights, target.HasPixelLights && target.PixelLightCount != gameLights, target.PixelLightCount);

                // ---- other limits (never more expensive than the game's value) ----
                if (shadows != null)
                {
                    bool limit = s.ShadowQuality != ShadowQualityLimit.Unchanged;
                    ShadowQuality cap = s.ShadowQuality == ShadowQualityLimit.Disable ? ShadowQuality.Disable : ShadowQuality.HardOnly;
                    ApplyOrRelease(shadows, limit, limit ? MinShadow(Baseline(shadows), cap) : default);
                }
                if (shadowResolution != null)
                {
                    bool limit = s.ShadowResolution != ShadowResolutionLimit.Unchanged;
                    ShadowResolution cap = s.ShadowResolution == ShadowResolutionLimit.Low ? ShadowResolution.Low
                        : s.ShadowResolution == ShadowResolutionLimit.Medium ? ShadowResolution.Medium : ShadowResolution.High;
                    ApplyOrRelease(shadowResolution, limit, limit ? (ShadowResolution)Math.Min((int)Baseline(shadowResolution), (int)cap) : default);
                }
                if (shadowCascades != null)
                    ApplyOrRelease(shadowCascades, s.ShadowCascadesLimit > 0, Math.Min(Baseline(shadowCascades), Math.Max(1, s.ShadowCascadesLimit)));
                if (msaa != null)
                    ApplyOrRelease(msaa, s.MsaaSamples >= 0, s.MsaaSamples);
                if (softParticles != null)
                    ApplyOrRelease(softParticles, s.SoftParticles != TriState.Unchanged, s.SoftParticles == TriState.On);

                ApplyOrRelease(anisotropic, s.AnisotropicFiltering != AnisotropicMode.Unchanged, ToUnity(s.AnisotropicFiltering));
                ApplyOrRelease(maxLodLevel, s.MinimumMaxLodLevel >= 0, Math.Max(Baseline(maxLodLevel), s.MinimumMaxLodLevel));
                ApplyOrRelease(textureLimit, s.TextureMipLimit >= 0, Math.Max(Baseline(textureLimit), s.TextureMipLimit));
                ApplyOrRelease(reflectionProbes, s.RealtimeReflectionProbes != TriState.Unchanged, s.RealtimeReflectionProbes == TriState.On);
                ApplyOrRelease(particleBudget, s.ParticleRaycastBudgetLimit >= 0, Math.Min(Baseline(particleBudget), s.ParticleRaycastBudgetLimit));
                ApplyOrRelease(skinWeights, s.SkinWeights != SkinWeightLimit.Unchanged, MinSkin(Baseline(skinWeights), s.SkinWeights));

                ApplyOrRelease(occlusionMesh, vr && s.EnsureOcclusionMesh, true);
                bool mirrorWanted = vr && s.DesktopMirror != DesktopMirrorMode.Unchanged && ResolveMirrorConstants();
                ApplyOrRelease(mirror, mirrorWanted, mirrorWanted ? MirrorValue(s.DesktopMirror) : 0);

                ApplyCameraHdr(s.CameraHdr);
            }

            if (!pacingEnabled || s == null)
            {
                asyncUploadSlice.Release();
                backgroundLoading.Release();
                incrementalGcSlice?.Release();
            }
            else
            {
                ApplyOrRelease(asyncUploadSlice, s.AsyncUploadTimeSliceMs > 0, s.AsyncUploadTimeSliceMs);
                ApplyOrRelease(backgroundLoading, s.BackgroundLoading != LoadingPriority.Unchanged, ToUnity(s.BackgroundLoading));
                if (incrementalGcSlice != null)
                    ApplyOrRelease(incrementalGcSlice, s.IncrementalGcSliceMs > 0f, (ulong)(s.IncrementalGcSliceMs * 1_000_000.0));
            }
        }

        /// <summary>Checks whether the main camera changed (scene loads recreate cameras).</summary>
        public void CheckCamera(OptimizationSettings s, bool graphicsEnabled)
        {
            if (!graphicsEnabled || s == null || s.CameraHdr == TriState.Unchanged)
                return;
            // Reference comparison: Unity's == treats a destroyed camera as null, which would hide a camera swap.
            if (!ReferenceEquals(SafeMainCamera(), trackedCamera))
                ApplyCameraHdr(s.CameraHdr);
        }

        public void ReleaseGraphics()
        {
            Release(renderScale);
            Release(shadows);
            Release(shadowDistance);
            Release(shadowResolution);
            Release(shadowCascades);
            Release(msaa);
            Release(anisotropic);
            Release(lodBias);
            Release(maxLodLevel);
            Release(pixelLights);
            Release(textureLimit);
            Release(reflectionProbes);
            Release(softParticles);
            Release(particleBudget);
            Release(skinWeights);
            Release(occlusionMesh);
            Release(mirror);
            ApplyCameraHdr(TriState.Unchanged);
        }

        private static void Release(ITrackedSetting setting) => setting?.Release();

        private static void ApplyOrRelease<T>(TrackedSetting<T> setting, bool apply, T value)
        {
            if (setting == null)
                return;
            if (apply)
                setting.Apply(value);
            else
                setting.Release();
        }

        private static T Baseline<T>(TrackedSetting<T> setting) => setting.TryGetBaseline(out T value) ? value : default;

        private static bool Differs(float a, float b) => Math.Abs(a - b) > 0.0005f;

        private void ApplyCameraHdr(TriState mode)
        {
            Camera main = mode == TriState.Unchanged ? null : SafeMainCamera();
            if (!ReferenceEquals(main, trackedCamera))
            {
                if (cameraHdr != null)
                {
                    // A destroyed camera (Unity-null) has nothing left to restore.
                    if (trackedCamera != null)
                        cameraHdr.Release();
                    ledger.Unregister(cameraHdr);
                    cameraHdr = null;
                }
                trackedCamera = main;
                if (main != null)
                {
                    Camera captured = main;
                    cameraHdr = Track(new TrackedSetting<bool>("Main camera HDR", () => captured.allowHDR, v => captured.allowHDR = v));
                }
            }
            if (cameraHdr != null && trackedCamera != null)
                ApplyOrRelease(cameraHdr, mode != TriState.Unchanged, mode == TriState.On);
        }

        private static Camera SafeMainCamera()
        {
            try
            {
                return Camera.main;
            }
            catch
            {
                return null;
            }
        }

        // ---- desktop mirror ----

        private bool ResolveMirrorConstants()
        {
            if (mirrorConstantsResolved)
                return true;
            try
            {
                mirrorLeftEye = XRMirrorViewBlitMode.LeftEye;
                mirrorRightEye = XRMirrorViewBlitMode.RightEye;
                mirrorSideBySide = XRMirrorViewBlitMode.SideBySide;
                mirrorNone = XRMirrorViewBlitMode.None;
                mirrorConstantsResolved = true;
            }
            catch
            {
                mirrorConstantsResolved = false;
            }
            return mirrorConstantsResolved;
        }

        private int MirrorValue(DesktopMirrorMode mode)
        {
            switch (mode)
            {
                case DesktopMirrorMode.LeftEye: return mirrorLeftEye;
                case DesktopMirrorMode.RightEye: return mirrorRightEye;
                case DesktopMirrorMode.SideBySide: return mirrorSideBySide;
                case DesktopMirrorMode.None: return mirrorNone;
                default: return mirrorLeftEye;
            }
        }

        private int ReadMirror()
        {
            XRDisplaySubsystem display = xr.Display;
            if (display == null)
                throw new InvalidOperationException("no running XR display subsystem");
            return display.GetPreferredMirrorBlitMode();
        }

        private void WriteMirror(int value)
        {
            XRDisplaySubsystem display = xr.Display;
            if (display == null)
                throw new InvalidOperationException("no running XR display subsystem");
            display.SetPreferredMirrorBlitMode(value);
        }

        private string DescribeMirror(int value)
        {
            if (!mirrorConstantsResolved)
                return value.ToString();
            if (value == mirrorLeftEye) return "LeftEye";
            if (value == mirrorRightEye) return "RightEye";
            if (value == mirrorSideBySide) return "SideBySide";
            if (value == mirrorNone) return "None";
            return "Default(" + value + ")";
        }

        // ---- conversions ----

        private static ShadowQuality MinShadow(ShadowQuality game, ShadowQuality cap)
        {
            int Rank(ShadowQuality q) => q == ShadowQuality.Disable ? 0 : (q == ShadowQuality.HardOnly ? 1 : 2);
            return Rank(game) <= Rank(cap) ? game : cap;
        }

        private static SkinWeights MinSkin(SkinWeights game, SkinWeightLimit limit)
        {
            SkinWeights cap = limit == SkinWeightLimit.OneBone ? SkinWeights.OneBone : limit == SkinWeightLimit.TwoBones ? SkinWeights.TwoBones : SkinWeights.FourBones;
            int Rank(SkinWeights w) => w == SkinWeights.OneBone ? 1 : w == SkinWeights.TwoBones ? 2 : w == SkinWeights.FourBones ? 4 : 255;
            return Rank(game) <= Rank(cap) ? game : cap;
        }

        private static AnisotropicFiltering ToUnity(AnisotropicMode mode) =>
            mode == AnisotropicMode.Disable ? AnisotropicFiltering.Disable
            : mode == AnisotropicMode.ForceEnable ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;

        private static ThreadPriority ToUnity(LoadingPriority p) =>
            p == LoadingPriority.Low ? ThreadPriority.Low
            : p == LoadingPriority.BelowNormal ? ThreadPriority.BelowNormal
            : p == LoadingPriority.High ? ThreadPriority.High : ThreadPriority.Normal;

        private static string F1(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        private static string F2(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}

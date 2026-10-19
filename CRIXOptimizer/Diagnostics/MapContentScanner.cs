// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CRIXOptimizer.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Counts a loaded custom map's content (<see cref="MapContent"/>): walks the scenes the game marks as custom
    /// (CustomMapLoader.IsCustomScene) once, including inactive objects. Read-only: nothing is enabled, disabled or
    /// changed. Runs once per map, after the game has finished loading it.
    /// </summary>
    internal static class MapContentScanner
    {
        private const int MaxListedCameras = 6;

        public static MapContent Scan(Func<string, bool> isCustomScene, string mapId)
        {
            var watch = Stopwatch.StartNew();
            var content = new MapContent { MapId = mapId ?? "" };
            var roots = new List<GameObject>(64);
            var renderers = new List<Renderer>(1024);
            var filters = new List<MeshFilter>(1024);
            var lights = new List<Light>(32);
            var probes = new List<ReflectionProbe>(8);
            var cameras = new List<Camera>(4);
            var lods = new List<LODGroup>(64);
            var particles = new List<ParticleSystem>(64);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !SafeIsCustom(isCustomScene, scene.name))
                    continue;
                content.Scenes++;
                roots.Clear();
                scene.GetRootGameObjects(roots);
                foreach (GameObject root in roots)
                {
                    if (!UnityObjects.IsAlive(root))
                        continue;
                    root.GetComponentsInChildren(true, renderers);
                    foreach (Renderer r in renderers)
                    {
                        content.Renderers++;
                        if (r is SkinnedMeshRenderer skinned)
                        {
                            content.SkinnedRenderers++;
                            content.Triangles += Triangles(skinned.sharedMesh);
                        }
                    }
                    root.GetComponentsInChildren(true, filters);
                    foreach (MeshFilter f in filters)
                        content.Triangles += Triangles(f.sharedMesh);
                    root.GetComponentsInChildren(true, lods);
                    content.LodGroups += lods.Count;
                    root.GetComponentsInChildren(true, lights);
                    foreach (Light l in lights)
                        CountLight(content, l);
                    root.GetComponentsInChildren(true, probes);
                    foreach (ReflectionProbe p in probes)
                    {
                        content.ReflectionProbes++;
                        if (p.mode == ReflectionProbeMode.Realtime)
                        {
                            content.RealtimeProbes++;
                            if (p.refreshMode == ReflectionProbeRefreshMode.EveryFrame)
                            {
                                content.EveryFrameProbes++;
                                if (p.timeSlicingMode == ReflectionProbeTimeSlicingMode.NoTimeSlicing)
                                    content.UnslicedEveryFrameProbes++;
                            }
                        }
                    }
                    root.GetComponentsInChildren(true, cameras);
                    foreach (Camera c in cameras)
                    {
                        RenderTexture rt = c.targetTexture;
                        if (rt == null)
                            continue;
                        if (content.TextureCameras.Count < MaxListedCameras)
                            content.TextureCameras.Add(rt.width + "x" + rt.height);
                        else if (content.TextureCameras.Count == MaxListedCameras)
                            content.TextureCameras.Add("...");
                    }
                    root.GetComponentsInChildren(true, particles);
                    foreach (ParticleSystem ps in particles)
                    {
                        content.ParticleSystems++;
                        content.MaxParticles += Math.Max(0, ps.main.maxParticles);
                        if (ps.lights.enabled)
                            content.ParticleLightSystems++;
                    }
                }
            }
            content.ScanMs = watch.Elapsed.TotalMilliseconds;
            return content;
        }

        private static void CountLight(MapContent content, Light light)
        {
            content.Lights++;
            if (!light.enabled)
                return; // the game's custom vertex lighting turns tagged map lights off and lights them itself
            LightBakingOutput baking = light.bakingOutput;
            if (baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Baked)
                return;
            content.RealtimeLights++;
            if (light.shadows != LightShadows.None && light.shadowStrength > 0f)
            {
                content.ShadowedRealtimeLights++;
                if (light.type == LightType.Point)
                    content.ShadowedPointLights++;
            }
        }

        private static long Triangles(Mesh mesh)
        {
            if (mesh == null)
                return 0;
            long count = 0;
            int sub = mesh.subMeshCount;
            for (int i = 0; i < sub; i++)
                count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        private static bool SafeIsCustom(Func<string, bool> isCustomScene, string name)
        {
            try
            {
                return isCustomScene(name);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Counts camera renders by kind (<see cref="CameraRenderCounts"/>) from RenderPipelineManager.beginCameraRendering.</summary>
    internal sealed class CameraRenderCounter : IDisposable
    {
        public readonly CameraRenderCounts Counts = new CameraRenderCounts();
        /// <summary>Counts only while diagnostics are on (frames are only counted then too).</summary>
        public bool Counting = true;
        private bool subscribed;

        public void Start()
        {
            if (subscribed)
                return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            subscribed = true;
        }

        /// <summary>Per frame (main thread).</summary>
        public void AddFrame() => Counts.Frames++;

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            try
            {
                if (!Counting || camera == null)
                    return;
                switch (camera.cameraType)
                {
                    case CameraType.Reflection:
                        Counts.Reflection++;
                        return;
                    case CameraType.Game:
                    case CameraType.VR:
                        if (camera.targetTexture != null && !GPU.SpectatorCameraModule.IsSpectatorTexture(camera.targetTexture))
                            Counts.Texture++;
                        else if (camera.stereoEnabled)
                            Counts.Headset++;
                        else
                            Counts.Window++;
                        return;
                    default:
                        Counts.Other++;
                        return;
                }
            }
            catch
            {
                // Diagnostics only: never let a counter break rendering.
            }
        }

        public void Dispose()
        {
            if (!subscribed)
                return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            subscribed = false;
        }
    }
}

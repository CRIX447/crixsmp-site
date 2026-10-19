// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Lists the cameras that render each frame: the VR camera, cameras drawing to the desktop window (spectator /
    /// shoulder cameras used for streaming, e.g. by Sakuraa Client) and cameras drawing into render textures
    /// (mirrors, tablets, minimaps, replay viewers). Every extra camera renders the scene again, so this is often
    /// where streaming setups lose the most GPU time.
    ///
    /// The "pixel load" figure is derived (rendered pixels relative to one VR eye) and is NOT measured GPU time;
    /// cost per pixel differs between cameras. Read-only: nothing is changed. Runs every few seconds, not per frame.
    /// </summary>
    internal sealed class CameraInventory
    {
        public sealed class Entry
        {
            public string Name;
            public string Target;
            public int Width;
            public int Height;
            public float Depth;
            public bool IsVr;
            public bool ToScreen;
            public double PixelLoad;
        }

        private Camera[] buffer = new Camera[16];

        public readonly List<Entry> Cameras = new List<Entry>();
        public int ExtraCameras { get; private set; }
        public int ScreenCameras { get; private set; }
        public double ExtraPixelLoad { get; private set; }
        public Entry LargestScreenCamera { get; private set; }

        public void Refresh(int eyeWidth, int eyeHeight)
        {
            Cameras.Clear();
            ExtraCameras = 0;
            ScreenCameras = 0;
            ExtraPixelLoad = 0;
            LargestScreenCamera = null;

            int count = Camera.allCamerasCount;
            if (buffer.Length < count)
                buffer = new Camera[Math.Max(count, buffer.Length * 2)];
            count = Camera.GetAllCameras(buffer);
            // Without an active headset there is no eye size to compare against; pixel load is then n/a (0).
            double eyePixels = eyeWidth > 0 && eyeHeight > 0 ? (double)eyeWidth * eyeHeight : 0;

            for (int i = 0; i < count; i++)
            {
                Camera cam = buffer[i];
                buffer[i] = null;
                if (cam == null || !cam.enabled)
                    continue;

                var entry = new Entry { Name = SafeName(cam), Depth = cam.depth };
                RenderTexture rt = cam.targetTexture;
                if (GPU.SpectatorCameraModule.IsSpectatorTexture(rt))
                {
                    // CRIX's spectator limit: the camera still shows in the window, through CRIX's texture.
                    entry.ToScreen = true;
                    entry.Target = "desktop window (via CRIX's spectator texture, limited)";
                    entry.Width = rt.width;
                    entry.Height = rt.height;
                }
                else if (rt != null)
                {
                    entry.Target = "render texture";
                    entry.Width = rt.width;
                    entry.Height = rt.height;
                }
                else if (cam.stereoEnabled && cam.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    entry.IsVr = true;
                    entry.Target = "VR headset";
                    entry.Width = eyeWidth;
                    entry.Height = eyeHeight;
                }
                else
                {
                    entry.ToScreen = true;
                    entry.Target = "desktop window";
                    entry.Width = cam.pixelWidth;
                    entry.Height = cam.pixelHeight;
                }

                entry.PixelLoad = entry.IsVr ? 2.0 : eyePixels > 0 ? (double)entry.Width * entry.Height / eyePixels : 0;
                Cameras.Add(entry);
                if (!entry.IsVr)
                {
                    ExtraCameras++;
                    ExtraPixelLoad += entry.PixelLoad;
                }
                if (entry.ToScreen)
                {
                    ScreenCameras++;
                    if (LargestScreenCamera == null || entry.Width * entry.Height > LargestScreenCamera.Width * LargestScreenCamera.Height)
                        LargestScreenCamera = entry;
                }
            }
        }

        public string Describe()
        {
            if (Cameras.Count == 0)
                return "no active cameras found";
            var sb = new StringBuilder();
            foreach (Entry e in Cameras)
            {
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append(e.Name).Append(" -> ").Append(e.Target);
                if (e.Width > 0)
                    sb.Append(' ').Append(e.Width).Append('x').Append(e.Height);
                if (!e.IsVr && e.PixelLoad > 0)
                    sb.Append(" (pixel load ").Append((e.PixelLoad * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append("% of one eye [D])");
            }
            return sb.ToString();
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

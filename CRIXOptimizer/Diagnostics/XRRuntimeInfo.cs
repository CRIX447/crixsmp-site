// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// What the VR runtime reports through Unity's supported XR APIs. Every value here comes from the runtime
    /// (Oculus/OpenXR/OpenVR provider); values a runtime does not report are shown as unavailable, never
    /// estimated. Virtual Desktop's own streaming statistics are not exposed to games and are not shown.
    /// </summary>
    internal sealed class XRRuntimeInfo
    {
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private float nextRefresh;
        private bool openXrLookedUp;
        private Func<string> openXrName;
        private Func<string> openXrVersion;

        public XRDisplaySubsystem Display { get; private set; }
        public bool XrEnabled { get; private set; }
        public bool DeviceActive { get; private set; }
        public string DeviceName { get; private set; } = "";
        public string OpenXRRuntimeName { get; private set; } = "";
        public string OpenXRRuntimeVersion { get; private set; } = "";
        public float RefreshRate { get; private set; }
        public string RefreshRateSource { get; private set; } = "unavailable";
        public int EyeTextureWidth { get; private set; }
        public int EyeTextureHeight { get; private set; }
        public float EyeTextureScale { get; private set; }
        public int EyeTextureMsaa { get; private set; }
        public string StereoMode { get; private set; } = "";

        public bool IsVrActive => XrEnabled && DeviceActive;

        /// <summary>Refreshes the cached runtime description (every 5 s, or immediately when forced). Main thread only.</summary>
        public void Refresh(float now, bool force = false)
        {
            if (!force && now < nextRefresh)
                return;
            nextRefresh = now + 5f;

            try
            {
                XrEnabled = XRSettings.enabled;
                DeviceActive = XRSettings.isDeviceActive;
                DeviceName = XRSettings.loadedDeviceName ?? "";
                EyeTextureWidth = XRSettings.eyeTextureWidth;
                EyeTextureHeight = XRSettings.eyeTextureHeight;
                EyeTextureScale = XRSettings.eyeTextureResolutionScale;
                EyeTextureMsaa = XRSettings.eyeTextureDesc.msaaSamples;
                StereoMode = XRSettings.stereoRenderingMode.ToString();
            }
            catch
            {
                XrEnabled = false;
                DeviceActive = false;
            }

            Display = null;
            try
            {
                displays.Clear();
                SubsystemManager.GetSubsystems(displays);
                foreach (XRDisplaySubsystem display in displays)
                {
                    if (display != null && display.running)
                    {
                        Display = display;
                        break;
                    }
                }
            }
            catch
            {
                Display = null;
            }

            // URP sets the display's render-target scale itself every frame (from its asset); prefer that value.
            try
            {
                if (Display != null && Display.scaleOfAllRenderTargets > 0f)
                    EyeTextureScale = Display.scaleOfAllRenderTargets;
            }
            catch
            {
                // keep the XRSettings value
            }

            RefreshRate = 0f;
            RefreshRateSource = "unavailable";
            try
            {
                if (Display != null && Display.TryGetDisplayRefreshRate(out float rate) && rate > 1f)
                {
                    RefreshRate = rate;
                    RefreshRateSource = "XR display subsystem";
                }
                else if (IsVrActive && XRDevice.refreshRate > 1f)
                {
                    RefreshRate = XRDevice.refreshRate;
                    RefreshRateSource = "XRDevice";
                }
            }
            catch
            {
                // fall through to the monitor refresh rate
            }
            if (RefreshRate <= 1f)
            {
                try
                {
                    int monitor = Screen.currentResolution.refreshRate;
                    if (monitor > 1)
                    {
                        RefreshRate = monitor;
                        RefreshRateSource = IsVrActive ? "monitor (VR runtime did not report)" : "monitor";
                    }
                }
                catch
                {
                    RefreshRate = 0f;
                }
            }

            LookUpOpenXr();
        }

        private void LookUpOpenXr()
        {
            if (!openXrLookedUp)
            {
                openXrLookedUp = true;
                try
                {
                    Type runtime = Type.GetType("UnityEngine.XR.OpenXR.OpenXRRuntime, Unity.XR.OpenXR", false);
                    PropertyInfo name = runtime?.GetProperty("name", BindingFlags.Public | BindingFlags.Static);
                    PropertyInfo version = runtime?.GetProperty("version", BindingFlags.Public | BindingFlags.Static);
                    if (name != null)
                        openXrName = () => name.GetValue(null, null) as string;
                    if (version != null)
                        openXrVersion = () => version.GetValue(null, null) as string;
                }
                catch
                {
                    openXrName = null;
                }
            }

            try
            {
                OpenXRRuntimeName = openXrName?.Invoke() ?? "";
                OpenXRRuntimeVersion = openXrVersion?.Invoke() ?? "";
            }
            catch
            {
                OpenXRRuntimeName = "";
            }
        }

        // ---- per-frame statistics reported by the runtime (provider dependent) ----

        /// <summary>App GPU time of the last frame in milliseconds, if the runtime reports it.</summary>
        public bool TryGetAppGpuMs(out float milliseconds)
        {
            milliseconds = 0f;
            XRDisplaySubsystem display = Display;
            if (display == null)
                return false;
            try
            {
                if (!display.TryGetAppGPUTimeLastFrame(out float value) || !(value > 0f))
                    return false;
                milliseconds = NormaliseToMilliseconds(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetCompositorGpuMs(out float milliseconds)
        {
            milliseconds = 0f;
            XRDisplaySubsystem display = Display;
            if (display == null)
                return false;
            try
            {
                if (!display.TryGetCompositorGPUTimeLastFrame(out float value) || !(value > 0f))
                    return false;
                milliseconds = NormaliseToMilliseconds(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetDroppedFrames(out int dropped)
        {
            dropped = 0;
            XRDisplaySubsystem display = Display;
            if (display == null)
                return false;
            try
            {
                return display.TryGetDroppedFrameCount(out dropped);
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetMotionToPhotonMs(out float milliseconds)
        {
            milliseconds = 0f;
            XRDisplaySubsystem display = Display;
            if (display == null)
                return false;
            try
            {
                if (!display.TryGetMotionToPhoton(out float value) || !(value > 0f))
                    return false;
                milliseconds = NormaliseToMilliseconds(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// XR providers do not agree on units for timing statistics (some report seconds, some milliseconds).
        /// A rendered VR frame cannot take less than 0.25 ms of GPU time or more than 250 seconds, so values
        /// below 0.25 are interpreted as seconds.
        /// </summary>
        internal static float NormaliseToMilliseconds(float value) => value < 0.25f ? value * 1000f : value;

        public string DescribeRuntime()
        {
            if (!IsVrActive)
                return XrEnabled ? "XR enabled, no active headset" : "XR not active (desktop)";
            string runtime = string.IsNullOrEmpty(OpenXRRuntimeName) ? DeviceName : $"{DeviceName} / OpenXR runtime: {OpenXRRuntimeName} {OpenXRRuntimeVersion}".Trim();
            return runtime;
        }
    }
}

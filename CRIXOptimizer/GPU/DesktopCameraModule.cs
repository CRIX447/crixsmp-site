// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Core;
using CRIXOptimizer.Utilities;
using UnityEngine;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Optional: switches off the game's own third-person ("shoulder") camera, which renders the whole scene a
    /// second time to the desktop window on PC. That second render costs CPU (culling, draw calls) and GPU every
    /// frame. While it is off, the desktop window shows the headset view through Unity's XR mirror instead.
    ///
    /// Rendering only: the camera object, its position and every script on it stay as they are; only
    /// Camera.enabled is changed, through the settings ledger, so it is restored on profile change, F10, disable
    /// and quit, and CRIX stops managing it if the game or another mod turns it back on. It is never touched when
    /// no VR headset is active (the camera may then be the only view), and never if the camera found is a stereo
    /// (VR) camera or renders into a texture.
    /// </summary>
    internal sealed class DesktopCameraModule
    {
        private readonly SettingsLedger ledger;
        private readonly ILogSink log;
        private readonly Func<object> instance;
        private readonly Func<object, object> cameraMember;
        private Camera tracked;
        private TrackedSetting<bool> enabledSetting;

        public bool Available { get; }
        public string Status { get; private set; } = "not used by this profile";

        public DesktopCameraModule(GameTypes types, SettingsLedger ledger, ILogSink log)
        {
            this.ledger = ledger;
            this.log = log ?? NullLogSink.Instance;
            Type tagger = types?.Find("GorillaTagger");
            instance = Accessors.StaticGetter<object>(tagger, "Instance")
                       ?? Accessors.StaticGetter<object>(tagger, "instance")
                       ?? Accessors.StaticGetter<object>(tagger, "_instance");
            cameraMember = Accessors.Getter<object>(tagger, "thirdPersonCamera");
            Available = instance != null && cameraMember != null;
            if (!Available)
                Status = "unavailable (GorillaTagger.thirdPersonCamera not found in this game build)";
        }

        /// <summary>The game's desktop camera is currently switched off by CRIX.</summary>
        public bool IsOff => enabledSetting != null && enabledSetting.State == SettingState.Applied && tracked != null && !SafeEnabled(tracked);

        /// <summary>Turns the camera off (wantOff) or gives it back to the game. Main thread; cheap when nothing changes.</summary>
        public void Apply(bool wantOff, string reason)
        {
            if (!Available)
                return;
            Camera cam = wantOff ? Resolve() : tracked;
            if (!ReferenceEquals(cam, tracked))
                Track(cam);

            if (enabledSetting == null)
            {
                Status = wantOff ? "desktop camera not found yet (scene still loading?)" : "on (game default)";
                return;
            }
            if (wantOff)
            {
                enabledSetting.Apply(false);
                Status = enabledSetting.State == SettingState.Applied ? "off: " + reason
                    : enabledSetting.State == SettingState.ExternallyModified ? "turned back on by the game or another mod; left alone"
                    : "could not be changed: " + enabledSetting.LastError;
            }
            else
            {
                enabledSetting.Release();
                Status = "on (" + reason + ")";
            }
        }

        private void Track(Camera cam)
        {
            if (enabledSetting != null)
            {
                // A destroyed camera (scene change) has nothing left to restore.
                if (tracked != null)
                    enabledSetting.Release();
                ledger.Unregister(enabledSetting);
                enabledSetting = null;
            }
            tracked = cam;
            if (cam == null)
                return;
            Camera captured = cam;
            enabledSetting = ledger.Register(new TrackedSetting<bool>("Desktop camera (game's third-person view)",
                () => captured.enabled, v => captured.enabled = v, null, v => v ? "on" : "off"));
            log.Debug("Desktop camera found: " + SafeName(cam));
        }

        private Camera Resolve()
        {
            object owner;
            object value;
            try
            {
                owner = instance();
                value = owner == null || !UnityObjects.IsAlive(owner) ? null : cameraMember(owner);
            }
            catch
            {
                return null;
            }
            Camera cam = value as Camera;
            if (cam == null && value is GameObject go && go != null)
                cam = go.GetComponentInChildren<Camera>(true);
            if (cam == null && value is Component component && component != null)
                cam = component.GetComponentInChildren<Camera>(true);
            // Safety: only a camera that draws to the desktop window, never the VR camera or a texture camera.
            if (cam == null || cam.targetTexture != null || cam.stereoTargetEye != StereoTargetEyeMask.None)
                return null;
            return cam;
        }

        private static bool SafeEnabled(Camera cam)
        {
            try
            {
                return cam != null && cam.enabled;
            }
            catch
            {
                return false;
            }
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

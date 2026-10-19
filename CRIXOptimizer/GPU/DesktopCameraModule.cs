// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Core;
using CRIXOptimizer.Utilities;
using UnityEngine;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Optional: switches off the game's own third-person "Shoulder Camera" (child of GorillaTagger.thirdPersonCamera),
    /// which renders the whole scene a second time to the desktop window on PC. That second render costs CPU
    /// (culling, draw calls) and GPU every frame. While it is off, URP's XR mirror view (drawn into the window before
    /// the shoulder camera) shows the headset view instead.
    ///
    /// Rendering only: the camera object, its position, its CinemachineBrain and every script on it stay as they
    /// are; only Camera.enabled is changed, through the settings ledger, so it is restored on profile change, F10,
    /// disable and quit, and CRIX stops managing it if the game or another mod turns it back on.
    /// The camera is left on:
    ///  - when no VR headset is active (it may then be the only view);
    ///  - while the game's LIV camera (LCK) drives it: the game then disables the shoulder camera's CinemachineBrain
    ///    and shows the LCK view through it (MonitorOutputController.TakeOverShoulderCamera);
    ///  - when another mod has moved it out of the game's camera rig (Sakuraa Client uses it as its spectator
    ///    camera), unless the user allowed that explicitly;
    ///  - if the camera found is a stereo (VR) camera or renders into a texture.
    /// </summary>
    internal sealed class DesktopCameraModule
    {
        /// <summary>GameObject.Find walks the whole scene, so the by-name lookup runs at most this often.</summary>
        private const float FindByNameIntervalSeconds = 30f;

        private readonly SettingsLedger ledger;
        private readonly ILogSink log;
        private readonly Func<object> instance;
        private readonly Func<object, object> rigMember;
        private Camera tracked;
        private Behaviour brain;
        private TrackedSetting<bool> enabledSetting;
        private float nextFindByName;

        public bool Available { get; }
        public string Status { get; private set; } = "not used by this profile";
        /// <summary>CRIX's own spectator texture (SpectatorCameraModule): a camera rendering into it still draws to the window.</summary>
        public Func<RenderTexture, bool> IsCrixTexture { get; set; } = _ => false;

        public DesktopCameraModule(GameTypes types, SettingsLedger ledger, ILogSink log)
        {
            this.ledger = ledger;
            this.log = log ?? NullLogSink.Instance;
            Type tagger = types?.Find("GorillaTagger");
            instance = Accessors.StaticGetter<object>(tagger, "Instance")
                       ?? Accessors.StaticGetter<object>(tagger, "_instance");
            rigMember = Accessors.Getter<object>(tagger, "thirdPersonCamera");
            Available = instance != null && rigMember != null;
            if (!Available)
                Status = "unavailable (GorillaTagger.thirdPersonCamera not found in this game build)";
        }

        /// <summary>
        /// Turns the camera off (wantOff) or gives it back to the game. Main thread, about once a second; cheap when
        /// nothing changes. <paramref name="allowWhenTakenOver"/>: also switch it off when another mod moved it.
        /// </summary>
        public void Apply(bool wantOff, string reason, bool allowWhenTakenOver, float now)
        {
            if (!Available)
                return;
            GameObject rig = Rig();
            // UnityObjects.IsAlive: plain reference check first, Unity's "destroyed" check only for real objects.
            bool trackedAlive = UnityObjects.IsAlive(tracked);
            Camera cam = trackedAlive ? tracked : wantOff ? Resolve(rig, now) : null;
            if (!ReferenceEquals(cam, tracked) || (!trackedAlive && enabledSetting != null))
                Track(cam);

            if (enabledSetting == null)
            {
                Status = wantOff ? "desktop camera not found (yet)" : "on (" + reason + ")";
                return;
            }

            // The game or another mod turned it back on after CRIX switched it off: leave it alone until the profile
            // is re-applied (the ledger re-arms it then). Releasing here would reset that and switch it off again.
            if (enabledSetting.State == SettingState.ExternallyModified)
            {
                Status = "turned back on by the game or another mod; left alone until the profile is re-applied";
                return;
            }

            string keepOn = null;
            if (wantOff)
            {
                if ((cam.targetTexture != null && !IsCrixTexture(cam.targetTexture)) || CameraRoles.RendersToHeadset(cam))
                    keepOn = "left on: it no longer draws only to the desktop window";
                else if (UnityObjects.IsAlive(brain) && !brain.enabled)
                    keepOn = "left on: its camera controller is paused (the game's LIV camera or another mod is driving it)";
                else if (UnityObjects.IsAlive(rig) && !cam.transform.IsChildOf(rig.transform) && !allowWhenTakenOver)
                    keepOn = "left on: another mod (e.g. Sakuraa's spectator camera) uses it; [Compatibility] DesktopCameraWhenModsUseIt = true allows switching it off";
            }

            if (wantOff && keepOn == null)
            {
                enabledSetting.Apply(false);
                Status = enabledSetting.State == SettingState.Applied ? "off: " + reason
                    : enabledSetting.State == SettingState.ExternallyModified ? "turned back on by the game or another mod; left alone until the profile is re-applied"
                    : "could not be changed: " + enabledSetting.LastError;
            }
            else
            {
                enabledSetting.Release();
                Status = keepOn ?? "on (" + reason + ")";
            }
        }

        /// <summary>
        /// The shoulder camera for the spectator limit (main thread, rate-limited by-name lookup): the camera this module
        /// tracks, else the rig's camera, else "Shoulder Camera" by name. Null when none draws to the window.
        /// </summary>
        public Camera FindShoulderCamera(float now)
        {
            if (!Available)
                return null;
            if (UnityObjects.IsAlive(tracked) && tracked.targetTexture == null && !CameraRoles.RendersToHeadset(tracked))
                return tracked;
            return Resolve(Rig(), now);
        }

        /// <summary>Who uses the camera now, for status lines: the game's rig, Sakuraa's spectator, or another mod.</summary>
        public string OwnerLabel(Camera cam)
        {
            try
            {
                GameObject rig = Rig();
                if (UnityObjects.IsAlive(rig) && cam.transform.IsChildOf(rig.transform))
                    return "game camera";
                Transform parent = cam.transform.parent;
                return parent != null && parent.name.IndexOf("Sakuraa", StringComparison.OrdinalIgnoreCase) >= 0 ? "Sakuraa spectator" : "moved by another mod";
            }
            catch
            {
                return "?";
            }
        }

        private GameObject Rig()
        {
            try
            {
                object owner = instance();
                if (owner == null || !UnityObjects.IsAlive(owner))
                    return null;
                object value = rigMember(owner);
                if (value is GameObject go && go != null)
                    return go;
                if (value is Component component && component != null)
                    return component.gameObject;
            }
            catch
            {
                // game not ready
            }
            return null;
        }

        private Camera Resolve(GameObject rig, float now)
        {
            Camera cam = null;
            if (UnityObjects.IsAlive(rig))
                cam = rig.GetComponentInChildren<Camera>(true);
            // Another mod may already have moved the camera out of the rig; look it up by name, rate-limited.
            if (!UnityObjects.IsAlive(cam) && now >= nextFindByName)
            {
                nextFindByName = now + FindByNameIntervalSeconds;
                cam = FindByName("Shoulder Camera");
            }
            // Safety: only a camera that draws to the desktop window, never one rendering in stereo (VR) or into a
            // texture. (stereoTargetEye is not used: a desktop-only camera can keep its default "Both" and simply not
            // be an XR camera.)
            if (!UnityObjects.IsAlive(cam) || cam.targetTexture != null || CameraRoles.RendersToHeadset(cam))
                return null;
            return cam;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Camera FindByName(string name)
        {
            try
            {
                GameObject named = GameObject.Find(name);
                return named != null ? named.GetComponent<Camera>() : null;
            }
            catch
            {
                return null;
            }
        }

        private void Track(Camera cam)
        {
            if (enabledSetting != null)
            {
                // A destroyed camera (scene change) has nothing left to restore.
                if (UnityObjects.IsAlive(tracked))
                    enabledSetting.Release();
                ledger.Unregister(enabledSetting);
                enabledSetting = null;
            }
            tracked = cam;
            brain = null;
            if (ReferenceEquals(cam, null))
                return;
            foreach (Behaviour b in cam.GetComponents<Behaviour>())
            {
                if (b != null && b.GetType().Name == "CinemachineBrain")
                {
                    brain = b;
                    break;
                }
            }
            Camera captured = cam;
            enabledSetting = ledger.Register(new TrackedSetting<bool>("Desktop camera (game's third-person view)",
                () => captured.enabled, v => captured.enabled = v, null, v => v ? "on" : "off"));
            log.Debug("Desktop camera found: " + SafeName(cam) + (brain != null ? " (with CinemachineBrain)" : ""));
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

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Whether a camera renders to the headset. Camera.stereoEnabled alone is not enough under URP with an XR plug-in:
    /// URP makes every Game camera without a target texture an XR camera unless its URP camera data has
    /// allowXRRendering off, whatever stereoEnabled reports. So a camera counts as a headset camera when it is
    /// stereoEnabled, or when a headset is active, it has no target texture, a stereo target eye other than None, and
    /// URP allows it XR rendering (read by name; true when it has no URP camera data). Read-only.
    /// </summary>
    internal static class CameraRoles
    {
        private static readonly Dictionary<Type, PropertyInfo> AllowXrProperty = new Dictionary<Type, PropertyInfo>();
        private static Type urpCameraDataType;
        private static bool urpTypeSearched;

        public static bool RendersToHeadset(Camera cam)
        {
            try
            {
                if (cam == null)
                    return false;
                if (cam.stereoEnabled)
                    return true;
                if (cam.targetTexture != null || cam.stereoTargetEye == StereoTargetEyeMask.None || !XRSettings.isDeviceActive)
                    return false;
                if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR)
                    return false;
                return AllowXrRendering(cam) != false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>URP's per-camera allowXRRendering, or null when the camera has no URP camera data (URP then allows it).</summary>
        public static bool? AllowXrRendering(Camera cam)
        {
            Type type = UrpCameraDataType();
            if (type == null)
                return null;
            Component data = cam.GetComponent(type);
            if (data == null)
                return null;
            if (!AllowXrProperty.TryGetValue(type, out PropertyInfo property))
                AllowXrProperty[type] = property = type.GetProperty("allowXRRendering", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool))
                return null;
            return (bool)property.GetValue(data, null);
        }

        private static Type UrpCameraDataType()
        {
            if (urpTypeSearched)
                return urpCameraDataType;
            urpTypeSearched = true;
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = assembly.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false);
                    if (t != null)
                    {
                        urpCameraDataType = t;
                        break;
                    }
                }
            }
            catch
            {
                urpCameraDataType = null;
            }
            return urpCameraDataType;
        }
    }
}

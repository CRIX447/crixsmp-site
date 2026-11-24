// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>
    /// Detects Sakuraa Client (a third-party Gorilla Tag client with camera/casting features; "GTFX" is part of
    /// it). CRIX never reads, patches or changes Sakuraa: it only checks loaded assembly names, BepInEx plugin GUIDs
    /// and Harmony patch owners that contain "Sakuraa", and the camera inventory reports any extra cameras.
    /// GTFX's effects can only be adjusted in Sakuraa itself.
    /// </summary>
    internal sealed class SakuraaSupport
    {
        public bool Detected { get; private set; }
        public string Detail { get; private set; } = "";
        public readonly List<string> Advice = new List<string>();

        public void Refresh(IEnumerable<PluginInventory.Entry> plugins, HarmonyFootprint.Report footprint)
        {
            var found = new List<string>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }
                if (name != null && name.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(name + SafeVersion(assembly));
            }

            if (plugins != null)
                foreach (PluginInventory.Entry p in plugins)
                    if (p.Guid != null && p.Guid.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) >= 0)
                        found.Add("plugin " + p.Guid);

            int patched = 0;
            if (footprint?.MethodsPerOwner != null)
                foreach (KeyValuePair<string, int> owner in footprint.MethodsPerOwner)
                    if (owner.Key.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) >= 0)
                        patched += owner.Value;

            Detected = found.Count > 0 || patched > 0;
            Detail = Detected
                ? string.Join(", ", found.ToArray()) + (patched > 0 ? (found.Count > 0 ? ", " : "") + $"Harmony patches on {patched} method(s)" : "")
                : "";

            Advice.Clear();
            if (!Detected)
                return;
            Advice.Add("Sakuraa Client detected. Extra cameras (spectator/shoulder camera, tablets, minimaps) render the scene again; " +
                       "the camera list in system-report.txt shows how many pixels each one draws.");
            Advice.Add("GTFX effects can only be adjusted in Sakuraa itself (its GTFX presets): pick a lighter preset while streaming or aiming for high frame rates. " +
                       "CRIX reduces the game's own load and does not change Sakuraa or GTFX.");
        }

        private static string SafeVersion(Assembly assembly)
        {
            try
            {
                Version v = assembly.GetName().Version;
                return v != null && v.Major + v.Minor + v.Build > 0 ? " v" + v : "";
            }
            catch
            {
                return "";
            }
        }
    }
}

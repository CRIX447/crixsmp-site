// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>
    /// Detects Sakuraa Client (a commercial Gorilla Tag camera/casting client whose "GTFX" shader system is part of it).
    /// Sakuraa is closed source and loaded by its own launcher, so CRIX never reads, patches or changes it; it only
    /// checks which assemblies are loaded, which Harmony owners exist, and reports the cameras it renders through the
    /// camera inventory. Coexistence notes:
    ///  - Sakuraa sets Application.targetFrameRate / vSyncCount; CRIX does not manage those (VR runtimes ignore them).
    ///  - Sakuraa patches player, rig, tag-manager and wardrobe methods; CRIX's Harmony overlap report lists any overlap.
    ///  - GTFX effects run on top of Gorilla Tag's render pipeline; CRIX's post-processing budget can only lighten
    ///    standard URP post-processing volumes, not GTFX's own clouds/rain/grass. Use GTFX's presets in Sakuraa for those.
    /// </summary>
    internal sealed class SakuraaSupport
    {
        private static readonly string[] AssemblyNames = { "SakuraaCastingMod", "SakMerge", "GorillaRigUtil", "SakuraaOfflinePresets" };

        public bool Detected { get; private set; }
        public string Detail { get; private set; } = "";
        /// <summary>A GUID used by unofficial "recovered"/rebuilt copies rather than the official injected client.</summary>
        public bool UnofficialRebuild { get; private set; }
        public readonly List<string> Advice = new List<string>();

        public void Refresh(IEnumerable<PluginInventory.Entry> plugins, HarmonyFootprint.Report footprint)
        {
            var found = new List<string>();
            string version = "";
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
                foreach (string wanted in AssemblyNames)
                {
                    if (!string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                        continue;
                    found.Add(name);
                    if (version.Length == 0 && name.Equals("SakuraaCastingMod", StringComparison.OrdinalIgnoreCase))
                        version = SafeVersion(assembly);
                }
            }

            UnofficialRebuild = false;
            if (plugins != null)
            {
                foreach (PluginInventory.Entry p in plugins)
                {
                    if (p.Guid == null || p.Guid.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    found.Add("plugin " + p.Guid);
                    if (p.Guid.StartsWith("com.dusted.", StringComparison.OrdinalIgnoreCase) || p.Guid.StartsWith("com.devcurly.", StringComparison.OrdinalIgnoreCase))
                        UnofficialRebuild = true;
                }
            }

            int patched = 0;
            if (footprint?.MethodsPerOwner != null)
                foreach (KeyValuePair<string, int> owner in footprint.MethodsPerOwner)
                    if (owner.Key.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) >= 0 || owner.Key.IndexOf("gorillarigutil", StringComparison.OrdinalIgnoreCase) >= 0)
                        patched += owner.Value;

            Detected = found.Count > 0 || patched > 0;
            Detail = Detected
                ? string.Join(", ", found.ToArray()) + (version.Length > 0 ? " v" + version : "") + (patched > 0 ? $", patches {patched} method(s)" : "")
                : "";

            Advice.Clear();
            if (!Detected)
                return;
            Advice.Add("Sakuraa Client detected. Its spectator/shoulder camera and tablet/minimap cameras render the scene again; " +
                       "the camera inventory in system-report.txt shows how many pixels each one draws.");
            Advice.Add("GTFX (Sakuraa's shaders) can only be tuned in Sakuraa itself (its GTFX presets): pick a lighter preset while streaming. " +
                       "CRIX lightens standard URP post-processing and the game's own load, not GTFX's clouds/rain/grass.");
            if (UnofficialRebuild)
                Advice.Add("This looks like an unofficial rebuilt Sakuraa copy, not the official client. Rebuilt copies shared on GitHub were reported to " +
                           "bundle a hidden library with download code; consider using only the official client.");
        }

        private static string SafeVersion(Assembly assembly)
        {
            try
            {
                return assembly.GetName().Version?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}

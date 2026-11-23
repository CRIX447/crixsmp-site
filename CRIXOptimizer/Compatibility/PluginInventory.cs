// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>Read-only list of loaded BepInEx plugins (Chainloader.PluginInfos). Built on demand, never per frame.</summary>
    internal static class PluginInventory
    {
        public sealed class Entry
        {
            public string Guid;
            public string Name;
            public string Version;
            public string File;
            public bool Loaded;
            public bool Enabled;
        }

        public static List<Entry> Snapshot()
        {
            var list = new List<Entry>();
            try
            {
                foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
                {
                    PluginInfo info = pair.Value;
                    BaseUnityPlugin instance = info?.Instance;
                    list.Add(new Entry
                    {
                        Guid = pair.Key,
                        Name = info?.Metadata?.Name ?? pair.Key,
                        Version = info?.Metadata?.Version?.ToString() ?? "?",
                        File = SafeFileName(info?.Location),
                        Loaded = instance != null,
                        Enabled = instance != null && instance.enabled,
                    });
                }
            }
            catch (Exception)
            {
                // The chainloader dictionary may be modified while plugins are still loading; caller retries later.
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The plugin is loaded and its component is enabled (BepInEx lists a disabled one as "(disabled)").</summary>
        public static bool IsEnabled(string guid)
        {
            try
            {
                return Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) && info?.Instance != null && info.Instance.enabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How many methods Harmony instances whose id is <paramref name="harmonyId"/> have patched (read-only).</summary>
        public static int PatchedMethodCount(string harmonyId)
        {
            int n = 0;
            try
            {
                foreach (System.Reflection.MethodBase m in HarmonyLib.Harmony.GetAllPatchedMethods())
                {
                    HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info?.Owners != null && info.Owners.Contains(harmonyId))
                        n++;
                }
            }
            catch
            {
                // Harmony not readable: count what was found
            }
            return n;
        }

        public static bool IsLoaded(string guid)
        {
            try
            {
                return Chainloader.PluginInfos.ContainsKey(guid);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads one bool setting of another plugin from its BepInEx config (read-only; never written). False when the
        /// plugin or the entry is not there (for example before that plugin's Awake bound it).
        /// </summary>
        public static bool TryReadConfigBool(string guid, string section, string key, out bool value)
        {
            value = false;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || info?.Instance == null)
                    return false;
                if (!info.Instance.Config.TryGetEntry(section, key, out BepInEx.Configuration.ConfigEntry<bool> entry) || entry == null)
                    return false;
                value = entry.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeFileName(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? "?" : Path.GetFileName(path);
            }
            catch
            {
                return "?";
            }
        }
    }
}

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

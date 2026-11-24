// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Opt-in ([Diagnostics] ModCodeTiming, off by default): times other mods' per-frame and room-callback methods
    /// to name the one behind a hitch. It adds a timing prefix and postfix (its own Harmony id) to
    ///  - Update, LateUpdate, FixedUpdate, OnJoinedRoom, OnLeftRoom, OnPlayerEnteredRoom and OnPlayerLeftRoom
    ///    declared by MonoBehaviours in other BepInEx plugins' assemblies and in Sakuraa's;
    ///  - every prefix and postfix other mods' Harmony instances put on the game's methods.
    /// The wrappers only read a timestamp; what the methods do, their order and their results are unchanged (a
    /// prefix that skips the original still does). Everything is removed when the setting is switched off or the game
    /// closes. Because it does add code around other mods' methods, it only runs when the user turns it on.
    /// </summary>
    internal sealed class ModCodeTimer : IDisposable
    {
        public const string HarmonyId = ModInfo.HarmonyId + ".modtiming";
        private static readonly HashSet<string> CallbackNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Update", "LateUpdate", "FixedUpdate", "OnJoinedRoom", "OnLeftRoom", "OnPlayerEnteredRoom", "OnPlayerLeftRoom",
        };

        private static ModCodeStats stats;
        private static Dictionary<MethodBase, int> ids;
        private static int mainThreadId;
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private readonly Harmony harmony = new Harmony(HarmonyId);
        public ModCodeStats Stats { get; } = new ModCodeStats();
        public int Wrapped { get; private set; }
        public int Failed { get; private set; }
        public List<string> Mods { get; } = new List<string>();

        private readonly Dictionary<MethodBase, int> wrappedIds = new Dictionary<MethodBase, int>();

        /// <summary>Main thread. Wraps the methods not wrapped yet (call again later for mods injected after the first
        /// call, such as Sakuraa); returns how many were added.</summary>
        public int Install(string ownAssemblyName, Action<string> debug)
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var targets = new List<(MethodBase method, string mod, string name)>();
            var seen = new HashSet<MethodBase>(wrappedIds.Keys);

            // MonoBehaviour callbacks in other plugins' assemblies (and Sakuraa's, which its launcher injects).
            var assemblies = new Dictionary<Assembly, string>();
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                Assembly a = info?.Instance != null ? info.Instance.GetType().Assembly : null;
                if (a != null && a.GetName().Name != ownAssemblyName && !assemblies.ContainsKey(a))
                    assemblies[a] = info.Metadata?.Name ?? a.GetName().Name;
            }
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = SafeName(a);
                if (name.IndexOf("sakuraa", StringComparison.OrdinalIgnoreCase) >= 0 && !assemblies.ContainsKey(a))
                    assemblies[a] = name;
            }
            foreach (KeyValuePair<Assembly, string> pair in assemblies)
            {
                if (!Mods.Contains(pair.Value))
                    Mods.Add(pair.Value);
                foreach (Type type in SafeTypes(pair.Key))
                {
                    if (type == null || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type))
                        continue;
                    MethodInfo[] declared;
                    try
                    {
                        declared = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    }
                    catch
                    {
                        continue;
                    }
                    foreach (MethodInfo m in declared)
                    {
                        if (CallbackNames.Contains(m.Name) && !m.IsAbstract && !m.ContainsGenericParameters && m.GetMethodBody() != null && seen.Add(m))
                            targets.Add((m, pair.Value, type.Name + "." + m.Name));
                    }
                }
            }

            // Other mods' Harmony prefixes and postfixes on the game's methods.
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null)
                    continue;
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes))
                {
                    if (p.owner == ModInfo.HarmonyId || p.owner == HarmonyId || p.PatchMethod == null || !seen.Add(p.PatchMethod))
                        continue;
                    if (p.PatchMethod.ContainsGenericParameters || p.PatchMethod.GetMethodBody() == null)
                        continue;
                    targets.Add((p.PatchMethod, p.owner, "patch on " + (original.DeclaringType?.Name ?? "?") + "." + original.Name + " (" + p.PatchMethod.Name + ")"));
                }
            }

            stats = Stats;
            ids = wrappedIds;
            int added = 0;
            var prefix = new HarmonyMethod(typeof(ModCodeTimer).GetMethod(nameof(Before), BindingFlags.Static | BindingFlags.NonPublic));
            var postfix = new HarmonyMethod(typeof(ModCodeTimer).GetMethod(nameof(After), BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var t in targets)
            {
                try
                {
                    // Registered first: the method can run (and be looked up) as soon as it is patched.
                    wrappedIds[t.method] = Stats.Register(t.mod, t.name);
                    harmony.Patch(t.method, prefix: prefix, postfix: postfix);
                    Wrapped++;
                    added++;
                }
                catch (Exception e)
                {
                    Failed++;
                    debug?.Invoke("Mod code timing: " + t.mod + " " + t.name + ": " + e.Message);
                }
            }
            return added;
        }

        public void Dispose()
        {
            try
            {
                harmony.UnpatchSelf();
            }
            finally
            {
                if (stats == Stats)
                {
                    stats = null;
                    ids = null;
                }
            }
        }

        private static void Before(out long __state) => __state = Stopwatch.GetTimestamp();

        private static void After(long __state, MethodBase __originalMethod)
        {
            ModCodeStats s = stats;
            if (s == null || Thread.CurrentThread.ManagedThreadId != mainThreadId || !ids.TryGetValue(__originalMethod, out int id))
                return;
            double ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
            // Unity's frame number and clock only for calls worth noting (each read crosses into the engine).
            if (ms >= ModCodeStats.NoteMs)
                s.Record(id, ms, Time.frameCount, Stopwatch.GetTimestamp() * TicksToMs / 1000.0);
            else
                s.Record(id, ms, 0, 0);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try
            {
                return a.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }

        private static string SafeName(Assembly a)
        {
            try
            {
                return a.GetName().Name ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}

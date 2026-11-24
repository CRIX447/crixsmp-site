// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (HarmonyPatches), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using System.Reflection;
using CRIXOptimizer.Core;
using HarmonyLib;

namespace CRIXOptimizer.Utilities
{
    /// <summary>
    /// Applies Harmony patches one at a time. Potassium uses PatchAll, where one missing target stops the
    /// remaining patches from being applied; here each patch succeeds or fails on its own and the result is
    /// recorded for the dashboard and system report. Each original is patched at most once by CRIX.
    /// </summary>
    internal sealed class PatchSet
    {
        public sealed class Record
        {
            public string Id;
            public string Target;
            public ModuleState State;
            public string Detail;
        }

        private readonly Harmony harmony;
        private readonly ILogSink log;
        private readonly StatusBoard status;
        private readonly Dictionary<string, Record> records = new Dictionary<string, Record>(StringComparer.Ordinal);
        private readonly HashSet<string> patchedKeys = new HashSet<string>(StringComparer.Ordinal);

        public PatchSet(Harmony harmony, ILogSink log, StatusBoard status)
        {
            this.harmony = harmony;
            this.log = log ?? NullLogSink.Instance;
            this.status = status ?? new StatusBoard();
        }

        public string HarmonyId => harmony.Id;
        public IEnumerable<Record> Records => records.Values;

        public bool IsApplied(string id) => records.TryGetValue(id, out Record r) && r.State == ModuleState.Active;

        public bool TryPatch(string id, MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null, MethodInfo transpiler = null, int priority = -1)
        {
            if (records.TryGetValue(id, out Record existing) && existing.State == ModuleState.Active)
                return true;

            if (original == null)
            {
                Note(id, "(not found)", ModuleState.Unavailable, "target method not found in this game version");
                return false;
            }

            string target = Describe(original);
            string key = id + "|" + target;
            if (!patchedKeys.Add(key))
                return true;

            try
            {
                harmony.Patch(original,
                    prefix: prefix != null ? MakeMethod(prefix, priority) : null,
                    postfix: postfix != null ? MakeMethod(postfix, priority) : null,
                    transpiler: transpiler != null ? MakeMethod(transpiler, priority) : null);
                Note(id, target, ModuleState.Active, null);
                log.Debug($"Patched {target} ({id})");
                return true;
            }
            catch (Exception e)
            {
                patchedKeys.Remove(key);
                // HarmonyX records a patch in the method's shared patch list before compiling the wrapper. If
                // compiling fails, that record stays and breaks every later patch of the same method - including
                // other mods' patches. Remove exactly the patch methods we tried to add.
                RemovePartialPatch(original, prefix, postfix, transpiler);
                Note(id, target, ModuleState.Unavailable, "patch failed: " + e.GetType().Name + ": " + e.Message);
                log.Warn($"Could not patch {target} for {id}; that optimisation is disabled. ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }

        public void MarkUnavailable(string id, string reason) => Note(id, "-", ModuleState.Unavailable, reason);
        public void MarkDeferred(string id, string reason) => Note(id, "-", ModuleState.Deferred, reason);

        public void MarkFaulted(string id, string reason)
        {
            if (records.TryGetValue(id, out Record r))
            {
                r.State = ModuleState.Faulted;
                r.Detail = reason;
            }
            status.Set("Patches", id, ModuleState.Faulted, reason);
        }

        public void UnpatchAll()
        {
            try
            {
                harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                log.Warn("Unpatching failed: " + e.Message);
            }
            patchedKeys.Clear();
            foreach (Record r in records.Values)
                if (r.State == ModuleState.Active)
                    r.State = ModuleState.Disabled;
        }

        private void RemovePartialPatch(MethodBase original, params MethodInfo[] patchMethods)
        {
            foreach (MethodInfo patch in patchMethods)
            {
                if (patch == null)
                    continue;
                try
                {
                    harmony.Unpatch(original, patch);
                }
                catch (Exception e)
                {
                    log.Debug($"Cleanup after failed patch of {Describe(original)} failed: {e.Message}");
                }
            }
        }

        private static HarmonyMethod MakeMethod(MethodInfo method, int priority)
        {
            var hm = new HarmonyMethod(method);
            if (priority >= 0)
                hm.priority = priority;
            return hm;
        }

        private void Note(string id, string target, ModuleState state, string detail)
        {
            if (!records.TryGetValue(id, out Record r))
            {
                r = new Record { Id = id };
                records.Add(id, r);
            }
            r.Target = target;
            r.State = state;
            r.Detail = detail;
            status.Set("Patches", id, state, detail ?? target);
        }

        public static string Describe(MethodBase method) =>
            method == null ? "(null)" : (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
    }

    /// <summary>
    /// Shared fault handling for patch bodies. A patch that throws is switched off (the game's original code
    /// runs from then on) and reported once, so a game update cannot turn a CRIX bug into a broken game.
    /// </summary>
    internal static class PatchGuard
    {
        public static ILogSink Log = NullLogSink.Instance;
        public static Action<string, string> OnFault;

        public static void Fault(string id, Exception e)
        {
            try
            {
                Log.Error($"{id} threw {e.GetType().Name}: {e.Message}. It is disabled for this session; the game's original code runs instead.");
                OnFault?.Invoke(id, e.GetType().Name + ": " + e.Message);
            }
            catch
            {
                // Never let error reporting throw into game code.
            }
        }
    }
}

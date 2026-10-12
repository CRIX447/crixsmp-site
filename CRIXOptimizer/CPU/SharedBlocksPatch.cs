// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (BlocksPatch, MonkeBlocksShit), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using CRIXOptimizer.Core;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Derived from Potassium 1.0.0 BlocksPatch/MonkeBlocksShit: SharedBlocksManager.Start is deferred until
    /// the player is in the Shared Blocks zone.
    ///
    /// Fixes over Potassium: every deferred manager is kept (Potassium stores one and would lose an earlier
    /// one forever), deferred managers are started immediately when the feature is disabled live or CRIX is
    /// suspended/unloaded, and the zone query is a compiled delegate polled 4 times per second.
    /// </summary>
    internal static class SharedBlocksPatch
    {
        public const string Id = "Stutter.SharedBlocksDeferral";

        private enum ZoneState { NoManager, Active, Inactive }

        private static readonly List<object> deferred = new List<object>();
        private static object allowOnce;
        private static Func<object> zoneManagement;
        private static Func<object, bool> isSharedBlocksZoneActive;
        private static Action<object> startManager;
        private static bool faulted;

        public static int DeferredCount => deferred.Count;
        public static int Released;

        public static void Apply(PatchSet patches, GameTypes types)
        {
            Type manager = types.Find("GorillaTagScripts.Builder.SharedBlocksManager", "SharedBlocksManager");
            Type zones = types.Find("ZoneManagement");
            Type zoneEnum = types.Find("GTZone");

            object sharedZone = null;
            if (zoneEnum != null && zoneEnum.IsEnum)
            {
                try
                {
                    sharedZone = Enum.Parse(zoneEnum, "monkeBlocksShared");
                }
                catch (ArgumentException)
                {
                    sharedZone = null;
                }
            }

            zoneManagement = Accessors.StaticGetter<object>(zones, "instance");
            isSharedBlocksZoneActive = Accessors.PredicateWithConstant(zones, "IsZoneActive", sharedZone);
            startManager = Accessors.Action0(manager, "Start");

            if (manager == null)
            {
                patches.MarkUnavailable(Id, "SharedBlocksManager not found");
                return;
            }
            if (zoneManagement == null || isSharedBlocksZoneActive == null || startManager == null)
            {
                patches.MarkUnavailable(Id, "ZoneManagement.IsZoneActive(GTZone.monkeBlocksShared) or SharedBlocksManager.Start not found");
                return;
            }

            patches.TryPatch(Id, Accessors.FindMethod(manager, "Start", 0),
                prefix: typeof(SharedBlocksPatch).GetMethod(nameof(Prefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        }

        private static ZoneState QueryZone()
        {
            object instance = zoneManagement();
            if (!UnityObjects.IsAlive(instance))
                return ZoneState.NoManager;
            return isSharedBlocksZoneActive(instance) ? ZoneState.Active : ZoneState.Inactive;
        }

        private static bool Prefix(object __instance)
        {
            if (ReferenceEquals(allowOnce, __instance))
                return true;
            if (!Live.DeferSharedBlocks || Live.Suspended || faulted)
                return true;
            try
            {
                // Same rule as Potassium: run normally only if the zone is already active.
                if (QueryZone() == ZoneState.Active)
                    return true;
                if (!ContainsReference(__instance))
                    deferred.Add(__instance);
                return false;
            }
            catch (Exception e)
            {
                faulted = true;
                PatchGuard.Fault(Id, e);
                return true;
            }
        }

        /// <summary>Polled ~4 times per second. Starts deferred managers once the zone is active or the feature is off.</summary>
        public static void Poll()
        {
            if (deferred.Count == 0)
                return;
            bool release;
            try
            {
                release = !Live.DeferSharedBlocks || Live.Suspended || faulted || QueryZone() == ZoneState.Active;
            }
            catch (Exception e)
            {
                faulted = true;
                PatchGuard.Fault(Id, e);
                release = true;
            }
            if (release)
                ReleaseAll();
        }

        public static void ReleaseAll()
        {
            if (deferred.Count == 0)
                return;
            object[] managers = deferred.ToArray();
            deferred.Clear();
            foreach (object manager in managers)
            {
                if (!UnityObjects.IsAlive(manager))
                    continue;
                allowOnce = manager;
                try
                {
                    startManager(manager);
                    Released++;
                }
                catch (Exception e)
                {
                    PatchGuard.Fault(Id, e);
                }
                finally
                {
                    allowOnce = null;
                }
            }
        }

        private static bool ContainsReference(object instance)
        {
            for (int i = 0; i < deferred.Count; i++)
                if (ReferenceEquals(deferred[i], instance))
                    return true;
            return false;
        }
    }
}

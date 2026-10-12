// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (InactiveEntityLeavePatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Derived from Potassium 1.0.0 InactiveEntityLeavePatch. Per Potassium's analysis, when a remote player
    /// leaves, every inactive GameEntityManager scans all its entities, increments a local counter that is
    /// never read, and returns. Only the active manager's scan does real work, so the others are skipped.
    /// Change from Potassium: the active-manager lookup is a compiled delegate instead of PropertyInfo.GetValue.
    /// </summary>
    internal static class EntityLeavePatch
    {
        public const string Id = "Stutter.InactiveEntityLeaveScans";
        public static long Skipped;

        private static Func<object> activeManager;
        private static bool faulted;

        public static void Apply(PatchSet patches, GameTypes types)
        {
            Type manager = types.Find("GameEntityManager");
            activeManager = Accessors.StaticGetter<object>(manager, "activeManager");
            if (manager != null && activeManager == null)
            {
                patches.MarkUnavailable(Id, "GameEntityManager.activeManager not found");
                return;
            }
            patches.TryPatch(Id, Accessors.FindMethod(manager, "OnRigDeactivated"),
                prefix: typeof(EntityLeavePatch).GetMethod(nameof(Prefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        }

        private static bool Prefix(object __instance)
        {
            if (!Live.SkipInactiveEntityLeaveScans || Live.Suspended || faulted)
                return true;
            try
            {
                if (ReferenceEquals(__instance, activeManager()))
                    return true;
                Skipped++;
                return false;
            }
            catch (Exception e)
            {
                faulted = true;
                PatchGuard.Fault(Id, e);
                return true;
            }
        }
    }
}

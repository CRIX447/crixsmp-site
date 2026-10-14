// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Adds or removes one system in a player loop tree, identified by its marker type. Pure struct manipulation
    /// (unit tested); the caller reads and writes the engine's loop with PlayerLoop.GetCurrentPlayerLoop /
    /// SetPlayerLoop. Systems added by other mods are kept as they are.
    /// </summary>
    internal static class PlayerLoopHook
    {
        /// <summary>Inserts <paramref name="add"/> directly before the first system of type <paramref name="target"/>.</summary>
        public static bool InsertBefore(ref PlayerLoopSystem system, Type target, PlayerLoopSystem add)
        {
            PlayerLoopSystem[] subs = system.subSystemList;
            if (subs == null)
                return false;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == target)
                {
                    var list = new List<PlayerLoopSystem>(subs);
                    list.Insert(i, add);
                    system.subSystemList = list.ToArray();
                    return true;
                }
                if (InsertBefore(ref subs[i], target, add))
                    return true;
            }
            return false;
        }

        /// <summary>Inserts <paramref name="add"/> directly after the first system of type <paramref name="target"/>.</summary>
        public static bool InsertAfter(ref PlayerLoopSystem system, Type target, PlayerLoopSystem add)
        {
            PlayerLoopSystem[] subs = system.subSystemList;
            if (subs == null)
                return false;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == target)
                {
                    var list = new List<PlayerLoopSystem>(subs);
                    list.Insert(i + 1, add);
                    system.subSystemList = list.ToArray();
                    return true;
                }
                if (InsertAfter(ref subs[i], target, add))
                    return true;
            }
            return false;
        }

        /// <summary>Removes every system of type <paramref name="marker"/>; returns how many were removed.</summary>
        public static int Remove(ref PlayerLoopSystem system, Type marker)
        {
            PlayerLoopSystem[] subs = system.subSystemList;
            if (subs == null)
                return 0;
            int removed = 0;
            var kept = new List<PlayerLoopSystem>(subs.Length);
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == marker)
                {
                    removed++;
                    continue;
                }
                PlayerLoopSystem child = subs[i];
                removed += Remove(ref child, marker);
                kept.Add(child);
            }
            if (removed > 0)
                system.subSystemList = kept.ToArray();
            return removed;
        }

        public static bool Contains(PlayerLoopSystem system, Type marker)
        {
            if (system.type == marker)
                return true;
            if (system.subSystemList == null)
                return false;
            foreach (PlayerLoopSystem child in system.subSystemList)
                if (Contains(child, marker))
                    return true;
            return false;
        }
    }
}

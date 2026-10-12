// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (RoomTransitionPatch, UnusedAssetUnloadPatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.CPU;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;
using HarmonyLib;

namespace CRIXOptimizer.Memory
{
    /// <summary>
    /// Derived from Potassium 1.0.0 RoomTransitionPatch and UnusedAssetUnloadPatch.
    ///
    /// Room leave: the game forces a full garbage collection in RoomSystem.OnLeftRoom, which stalls the main
    /// thread. The call is redirected to a policy method: GameDefault (keep it), Skip (Potassium), or
    /// Incremental (a time-sliced incremental collection when the game runs with incremental GC).
    /// Unlike Potassium, all GC.Collect overloads are redirected and the policy can be changed live.
    ///
    /// Timed asset unloads: SimpleUnloadUnusedAssets.OnEnable forces a synchronous unused-asset sweep; it is
    /// skipped while enabled. Pair with [Memory] DeferredAssetUnload for long sessions.
    /// </summary>
    internal static class GcPolicyPatches
    {
        public const string RoomLeaveId = "Stutter.RoomLeaveCollection";
        public const string AssetUnloadId = "Stutter.TimedAssetUnloads";

        public static int RewrittenCollectCalls { get; private set; }
        public static long ForcedCollectionsSkipped;
        public static long ForcedCollectionsReplaced;
        public static long AssetUnloadsSkipped;

        /// <summary>Set by the runtime (Unity layer): performs an incremental collection, returns false if unavailable.</summary>
        public static Func<ulong, bool> IncrementalCollector;

        private static readonly MethodInfo CollectGen = typeof(GC).GetMethod(nameof(GC.Collect), new[] { typeof(int) });
        private static readonly MethodInfo CollectAll = typeof(GC).GetMethod(nameof(GC.Collect), Type.EmptyTypes);
        private static readonly MethodInfo CollectGenMode = typeof(GC).GetMethod(nameof(GC.Collect), new[] { typeof(int), typeof(GCCollectionMode) });

        public static void Apply(PatchSet patches, GameTypes types)
        {
            Type roomSystem = types.Find("RoomSystem");
            patches.TryPatch(RoomLeaveId, Accessors.FindMethod(roomSystem, "OnLeftRoom"),
                transpiler: Method(nameof(RoomLeaveTranspiler)), postfix: Method(nameof(RoomLeavePostfix)));

            Type unloader = types.Find("SimpleUnloadUnusedAssets");
            patches.TryPatch(AssetUnloadId, Accessors.FindMethod(unloader, "OnEnable", 0),
                prefix: Method(nameof(AssetUnloadPrefix)));
        }

        private static MethodInfo Method(string name) =>
            typeof(GcPolicyPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        internal static IEnumerable<CodeInstruction> RoomLeaveTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var output = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method)
                {
                    MethodInfo replacement = null;
                    if (method == CollectGen)
                        replacement = Method(nameof(OnGameCollect));
                    else if (method == CollectAll)
                        replacement = Method(nameof(OnGameCollectAll));
                    else if (method == CollectGenMode)
                        replacement = Method(nameof(OnGameCollectWithMode));

                    if (replacement != null)
                    {
                        output.Add(new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction));
                        RewrittenCollectCalls++;
                        continue;
                    }
                }
                output.Add(instruction);
            }
            return output;
        }

        private static void RoomLeavePostfix()
        {
            // Queued room-join work is meaningless after leaving (same as Potassium).
            ScoreboardPatches.Clear();
        }

        public static void OnGameCollect(int generation)
        {
            if (Decide())
                GC.Collect(generation);
        }

        public static void OnGameCollectAll()
        {
            if (Decide())
                GC.Collect();
        }

        public static void OnGameCollectWithMode(int generation, GCCollectionMode mode)
        {
            if (Decide())
                GC.Collect(generation, mode);
        }

        /// <summary>Returns true when the game's own forced collection should run.</summary>
        private static bool Decide()
        {
            RoomLeaveGcMode mode = Live.Suspended ? RoomLeaveGcMode.GameDefault : Live.RoomLeaveGc;
            switch (mode)
            {
                case RoomLeaveGcMode.Skip:
                    ForcedCollectionsSkipped++;
                    return false;
                case RoomLeaveGcMode.Incremental:
                    if (TryIncremental())
                    {
                        ForcedCollectionsReplaced++;
                        return false;
                    }
                    // Without incremental GC there is no time-sliced alternative: skip, like Potassium.
                    ForcedCollectionsSkipped++;
                    return false;
                default:
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryIncremental()
        {
            try
            {
                return IncrementalCollector != null && IncrementalCollector(Live.IncrementalCollectBudgetNs);
            }
            catch
            {
                return false;
            }
        }

        private static bool AssetUnloadPrefix()
        {
            if (!Live.SkipTimedAssetUnloads || Live.Suspended)
                return true;
            AssetUnloadsSkipped++;
            return false;
        }
    }
}

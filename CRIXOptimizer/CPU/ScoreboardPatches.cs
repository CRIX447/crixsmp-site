// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (RoomJoinPatch, ScoreboardRefreshPatch, RoomJoinShit), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections;
using System.Collections.Generic;
using CRIXOptimizer.Core;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Derived from Potassium 1.0.0 RoomJoinPatch and ScoreboardRefreshPatch: scoreboard creation after
    /// joining a room and scoreboard refreshes after a player joins/leaves are spread over successive frames
    /// (one per frame) instead of all happening in a single frame.
    /// Changes from Potassium: identity sets instead of Unity instance IDs, compiled delegates instead of
    /// direct game calls, live enable/disable (queued work is flushed when disabled), fault isolation.
    /// </summary>
    internal static class ScoreboardPatches
    {
        public const string CreationId = "Stutter.ScoreboardCreation";
        public const string RefreshId = "Stutter.ScoreboardRefresh";

        // creation
        private static readonly Queue<object> pendingSpawners = new Queue<object>();
        private static readonly HashSet<object> pendingSpawnerSet = new HashSet<object>(ReferenceComparer.Instance);
        private static readonly HashSet<object> allowedSpawners = new HashSet<object>(ReferenceComparer.Instance);
        private static Action<object> spawnerOnJoinedRoom;

        // refresh
        private static readonly Queue<object> pendingBoards = new Queue<object>();
        private static readonly HashSet<object> pendingBoardSet = new HashSet<object>(ReferenceComparer.Instance);
        private static Func<object> allScoreboards;
        private static Func<object> updaterInstance;
        private static Action<object, object> updateScoreboard;

        private static Func<bool> inRoom;
        private static bool creationFaulted, refreshFaulted;

        public static long DeferredCreations, DeferredRefreshes;

        public static void Apply(PatchSet patches, GameTypes types, Func<bool> inRoomCheck)
        {
            inRoom = inRoomCheck;

            Type spawner = types.Find("GorillaNetworking.GorillaScoreboardSpawner", "GorillaScoreboardSpawner");
            spawnerOnJoinedRoom = Accessors.Action0(spawner, "OnJoinedRoom");
            if (spawner != null && (spawnerOnJoinedRoom == null || inRoom == null))
                patches.MarkUnavailable(CreationId, "GorillaScoreboardSpawner.OnJoinedRoom or NetworkSystem.InRoom not found");
            else
                patches.TryPatch(CreationId, Accessors.FindMethod(spawner, "OnJoinedRoom", 0),
                    prefix: Method(nameof(CreationPrefix)));

            Type updater = types.Find("GorillaScoreboardTotalUpdater");
            allScoreboards = Accessors.StaticGetter<object>(updater, "allScoreboards");
            updaterInstance = Accessors.StaticGetter<object>(updater, "instance");
            updateScoreboard = Accessors.Action1(updater, "UpdateScoreboard");
            if (updater != null && (allScoreboards == null || updaterInstance == null || updateScoreboard == null))
                patches.MarkUnavailable(RefreshId, "GorillaScoreboardTotalUpdater members not found");
            else
                patches.TryPatch(RefreshId, Accessors.FindMethod(updater, "UpdateActiveScoreboards", 0),
                    prefix: Method(nameof(RefreshPrefix)));
        }

        private static System.Reflection.MethodInfo Method(string name) =>
            typeof(ScoreboardPatches).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        private static bool CreationPrefix(object __instance)
        {
            if (allowedSpawners.Remove(__instance))
                return true;
            if (!Live.StaggerScoreboardCreation || Live.Suspended || creationFaulted)
                return true;
            try
            {
                if (!UnityObjects.IsActiveInHierarchy(__instance))
                    return true;
                if (pendingSpawnerSet.Add(__instance))
                {
                    pendingSpawners.Enqueue(__instance);
                    DeferredCreations++;
                }
                return false;
            }
            catch (Exception e)
            {
                creationFaulted = true;
                PatchGuard.Fault(CreationId, e);
                return true;
            }
        }

        private static bool RefreshPrefix()
        {
            if (!Live.StaggerScoreboardRefresh || Live.Suspended || refreshFaulted)
                return true;
            try
            {
                if (!(allScoreboards() is IList boards))
                    return true;
                for (int i = 0; i < boards.Count; i++)
                {
                    object board = boards[i];
                    if (UnityObjects.IsAlive(board) && pendingBoardSet.Add(board))
                    {
                        pendingBoards.Enqueue(board);
                        DeferredRefreshes++;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                refreshFaulted = true;
                PatchGuard.Fault(RefreshId, e);
                return true;
            }
        }

        /// <summary>Called once per frame: performs at most one deferred creation and one deferred refresh.</summary>
        public static void RunNext()
        {
            if (pendingSpawners.Count > 0)
                RunNextCreation();
            if (pendingBoards.Count > 0)
                RunNextRefresh();
        }

        public static int PendingCount => pendingSpawners.Count + pendingBoards.Count;

        /// <summary>Runs everything queued right now (used when the feature is switched off or CRIX shuts down).</summary>
        public static void Flush()
        {
            FlushCreation();
            FlushRefresh();
        }

        public static void FlushCreation()
        {
            int guard = 256;
            while (pendingSpawners.Count > 0 && guard-- > 0)
                RunNextCreation(force: true);
        }

        public static void FlushRefresh()
        {
            int guard = 256;
            while (pendingBoards.Count > 0 && guard-- > 0)
                RunNextRefresh();
        }

        public static void Clear()
        {
            pendingSpawners.Clear();
            pendingSpawnerSet.Clear();
            allowedSpawners.Clear();
            pendingBoards.Clear();
            pendingBoardSet.Clear();
        }

        private static void RunNextCreation(bool force = false)
        {
            try
            {
                if (!force && (inRoom == null || !inRoom()))
                    return;
                object spawner = pendingSpawners.Dequeue();
                pendingSpawnerSet.Remove(spawner);
                if (!UnityObjects.IsAlive(spawner))
                    return;
                if (force && (inRoom == null || !inRoom()))
                    return; // left the room: nothing to create
                allowedSpawners.Add(spawner);
                try
                {
                    spawnerOnJoinedRoom(spawner);
                }
                finally
                {
                    allowedSpawners.Remove(spawner);
                }
            }
            catch (Exception e)
            {
                creationFaulted = true;
                pendingSpawners.Clear();
                pendingSpawnerSet.Clear();
                PatchGuard.Fault(CreationId, e);
            }
        }

        private static void RunNextRefresh()
        {
            try
            {
                while (pendingBoards.Count > 0)
                {
                    object board = pendingBoards.Dequeue();
                    pendingBoardSet.Remove(board);
                    if (!UnityObjects.IsAlive(board))
                        continue;
                    object updater = updaterInstance();
                    if (!UnityObjects.IsAlive(updater))
                    {
                        pendingBoards.Clear();
                        pendingBoardSet.Clear();
                        return;
                    }
                    updateScoreboard(updater, board);
                    break;
                }
            }
            catch (Exception e)
            {
                refreshFaulted = true;
                pendingBoards.Clear();
                pendingBoardSet.Clear();
                PatchGuard.Fault(RefreshId, e);
            }
        }
    }
}

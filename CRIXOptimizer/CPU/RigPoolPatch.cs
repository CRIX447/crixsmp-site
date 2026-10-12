// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (RigPoolPatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections;
using System.Reflection;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Derived from Potassium 1.0.0 RigPoolPatch: the remote-player rig pool is created over several frames
    /// instead of all rigs in the startup frame. A few rigs are created immediately, the rest one per frame
    /// while not in a room, and any missing rig is created on demand when a player needs one.
    ///
    /// Fix over Potassium: on-demand creation keeps working whenever the pool is still short, even if the
    /// feature is switched off live (otherwise a player joining after the toggle could get no rig).
    /// </summary>
    internal static class RigPoolPatch
    {
        public const string Id = "Stutter.RigPoolStaggering";
        public const string OnDemandId = "Stutter.RigPoolOnDemand";

        private static Type cacheType;
        private static Func<bool> isInitialized;
        private static Func<object> allRigContainers;
        private static Func<object> freeRigs;
        private static Func<object> rigsInUse;
        private static Func<object, object> spawnRig;
        private static Func<object, object> rigOfContainer;
        private static Action<object> buildInitialize;
        private static FieldInfo postSpawnEvent;
        private static MethodInfo enqueueMethod;
        private static Func<object, bool> playerIsLocal;
        private static Func<object, bool> playerInRoom;
        private static Func<bool> inRoom;

        private static object cache;
        private static int targetRemoteRigCount;
        private static bool warmupFailed;
        private static PropertyInfo countProperty;

        public static int WarmedRigs;
        public static int OnDemandRigs;

        public static bool IsWarming => cache != null && !warmupFailed;

        public static void Apply(PatchSet patches, GameTypes types, Func<bool> inRoomCheck)
        {
            inRoom = inRoomCheck;
            cacheType = types.Find("VRRigCache");
            Type container = types.Find("RigContainer");
            Type netPlayer = types.Find("NetPlayer");

            isInitialized = Accessors.StaticGetter<bool>(cacheType, "isInitialized");
            allRigContainers = Accessors.StaticGetter<object>(cacheType, "AllRigContainers");
            freeRigs = Accessors.StaticGetter<object>(cacheType, "freeRigs");
            rigsInUse = Accessors.StaticGetter<object>(cacheType, "rigsInUse");
            spawnRig = Accessors.Func0(cacheType, "SpawnRig");
            rigOfContainer = Accessors.Getter<object>(container, "Rig");
            playerIsLocal = Accessors.Getter<bool>(netPlayer, "IsLocal");
            playerInRoom = Accessors.Getter<bool>(netPlayer, "InRoom");
            postSpawnEvent = cacheType?.GetField("OnPostSpawnRig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            buildInitialize = Accessors.Action0(types.Find("VRRig"), "BuildInitialize");

            if (cacheType == null)
            {
                patches.MarkUnavailable(Id, "VRRigCache not found");
                return;
            }
            if (isInitialized == null || allRigContainers == null || freeRigs == null || rigsInUse == null ||
                spawnRig == null || rigOfContainer == null || buildInitialize == null || inRoom == null ||
                playerIsLocal == null || playerInRoom == null)
            {
                patches.MarkUnavailable(Id, "one or more VRRigCache/RigContainer/VRRig/NetPlayer members not found");
                return;
            }

            BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            if (!patches.TryPatch(Id, cacheType.GetMethod("InitializeVRRigCache", any),
                    prefix: Method(nameof(InitializePrefix)), postfix: Method(nameof(InitializePostfix))))
                return;

            MethodInfo tryGet = null;
            foreach (MethodInfo m in cacheType.GetMethods(any))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "TryGetVrrig" && ps.Length == 2 && ps[0].ParameterType == netPlayer && ps[1].ParameterType.IsByRef)
                {
                    tryGet = m;
                    break;
                }
            }
            patches.TryPatch(OnDemandId, tryGet, prefix: Method(nameof(TryGetPrefix)));
        }

        private static MethodInfo Method(string name) =>
            typeof(RigPoolPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        private static void InitializePrefix(ref int ___rigAmount)
        {
            if (!Live.StaggerRigPool || Live.Suspended)
                return;
            try
            {
                targetRemoteRigCount = ___rigAmount;
                ___rigAmount = Math.Max(1, Math.Min(Live.InitialRigPoolSize, targetRemoteRigCount));
            }
            catch (Exception e)
            {
                warmupFailed = true;
                PatchGuard.Fault(Id, e);
            }
        }

        private static void InitializePostfix(object __instance)
        {
            try
            {
                if (targetRemoteRigCount > 0 && isInitialized())
                    cache = __instance;
            }
            catch (Exception e)
            {
                warmupFailed = true;
                PatchGuard.Fault(Id, e);
            }
        }

        private static void TryGetPrefix(object __instance, object __0)
        {
            if (warmupFailed || targetRemoteRigCount <= 0 || __0 == null)
                return;
            try
            {
                if (playerIsLocal(__0) || !playerInRoom(__0))
                    return;
                if (Count(freeRigs()) == 0 && !((IDictionary)rigsInUse()).Contains(__0))
                {
                    if (SpawnOne(__instance))
                        OnDemandRigs++;
                }
            }
            catch (Exception e)
            {
                warmupFailed = true;
                PatchGuard.Fault(OnDemandId, e);
            }
        }

        /// <summary>Called once per frame: creates one more pooled rig while not in a room.</summary>
        public static void WarmNext()
        {
            if (warmupFailed || cache == null || targetRemoteRigCount <= 0)
                return;
            try
            {
                if (!isInitialized() || inRoom())
                    return;
                if (Count(allRigContainers()) - 1 >= targetRemoteRigCount)
                {
                    cache = null; // pool complete
                    return;
                }
                if (SpawnOne(cache))
                    WarmedRigs++;
            }
            catch (Exception e)
            {
                warmupFailed = true;
                PatchGuard.Fault(Id, e);
            }
        }

        private static bool SpawnOne(object instance)
        {
            object container = spawnRig(instance);
            if (container == null)
                throw new InvalidOperationException("VRRigCache.SpawnRig returned null");

            object queue = freeRigs();
            if (enqueueMethod == null || enqueueMethod.DeclaringType != queue.GetType())
                enqueueMethod = queue.GetType().GetMethod("Enqueue");
            enqueueMethod.Invoke(queue, new[] { container });

            object rig = rigOfContainer(container);
            buildInitialize(rig);
            UnityObjects.DetachFromParent(rig);
            (postSpawnEvent?.GetValue(null) as Action)?.Invoke();
            return true;
        }

        private static int Count(object collection)
        {
            if (collection is ICollection c)
                return c.Count;
            if (collection == null)
                return 0;
            if (countProperty == null || countProperty.DeclaringType != collection.GetType())
                countProperty = collection.GetType().GetProperty("Count");
            return countProperty != null ? (int)countProperty.GetValue(collection, null) : 0;
        }
    }
}

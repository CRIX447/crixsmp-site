// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.GPU;
using CRIXOptimizer.Memory;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>Which groups of game patches the configuration allows.</summary>
    internal struct PatchGroups
    {
        public bool StutterFixes;
        public bool VisualThrottles;
        public bool CosmeticCulling;
    }

    /// <summary>
    /// Owns every game-specific (Potassium-derived) patch: applies them lazily when first needed, keeps the
    /// live flags in sync with the active profile, drives the per-frame staggering queues, and steps aside
    /// when Potassium itself is installed so the same game methods are never patched twice.
    /// </summary>
    internal sealed class GamePatchCatalog
    {
        private readonly PatchSet patches;
        private readonly GameTypes types;
        private readonly ILogSink log;
        private Func<bool> inRoom;
        private bool stutterApplied, throttlesApplied, cullingApplied;
        private SanitizerMode sanitizerApplied = SanitizerMode.Off;

        public GamePatchCatalog(PatchSet patches, GameTypes types, ILogSink log)
        {
            this.patches = patches;
            this.types = types;
            this.log = log;
        }

        public bool DeferredToPotassium { get; private set; }
        public bool GameTypesFound => !types.IsEmpty;

        public void Initialize(OptimizationSettings settings, PatchGroups groups, bool potassiumInstalled, PotassiumCoexistence coexistence, bool applyNow)
        {
            if (types.IsEmpty)
            {
                log.Warn("Gorilla Tag's game assembly (Assembly-CSharp) was not found; game-specific optimisations are unavailable. Engine-level features still work.");
                return;
            }

            if (potassiumInstalled && coexistence == PotassiumCoexistence.DeferToPotassium)
            {
                DeferredToPotassium = true;
                const string reason = "Potassium is installed and already patches this; CRIX skips its copy to avoid duplicate patches";
                foreach (string id in AllPatchIds)
                    patches.MarkDeferred(id, reason);
                log.Warn("Potassium is also installed. CRIX's Potassium-derived game patches are not applied (WhenPotassiumInstalled = DeferToPotassium). " +
                         "Engine-level optimisations, adaptive mode and diagnostics still run. Keeping only one of the two mods is recommended.");
                return;
            }
            if (potassiumInstalled)
                log.Warn("Potassium is also installed and WhenPotassiumInstalled = ApplyBoth: the same game methods are patched by both mods.");

            inRoom = BuildInRoomCheck();
            if (applyNow)
                Ensure(settings, groups);
        }

        /// <summary>Applies any patch group that became needed (patching is skipped entirely while a group is off).</summary>
        public void Ensure(OptimizationSettings settings, PatchGroups groups)
        {
            if (DeferredToPotassium || types.IsEmpty || settings == null)
                return;

            if (groups.StutterFixes && !stutterApplied)
            {
                stutterApplied = true;
                EntityLeavePatch.Apply(patches, types);
                ScoreboardPatches.Apply(patches, types, inRoom);
                RigPoolPatch.Apply(patches, types, inRoom);
                SharedBlocksPatch.Apply(patches, types);
                GcPolicyPatches.Apply(patches, types);
            }

            if (groups.StutterFixes && settings.MapSanitizer != SanitizerMode.Off && settings.MapSanitizer != sanitizerApplied)
            {
                // SafeLookups rewrites IL once; FullReplacement adds a prefix that takes over while selected.
                if (settings.MapSanitizer == SanitizerMode.FullReplacement || sanitizerApplied == SanitizerMode.Off)
                    MapSanitizerPatch.Apply(patches, types, settings.MapSanitizer);
                sanitizerApplied = settings.MapSanitizer;
            }

            if (groups.VisualThrottles && !throttlesApplied)
            {
                throttlesApplied = true;
                UpdateThrottlePatches.Apply(patches, types);
            }

            if (groups.CosmeticCulling && !cullingApplied)
            {
                cullingApplied = true;
                CosmeticCullingPatch.Apply(patches, types);
            }
        }

        /// <summary>Publishes the profile to the static flags patch code reads.</summary>
        public void UpdateLive(OptimizationSettings s, PatchGroups groups, bool suspended)
        {
            Live.Suspended = suspended;
            if (s == null || DeferredToPotassium)
            {
                Live.DisableAll();
                return;
            }

            bool stutter = groups.StutterFixes;
            bool wasDeferringBlocks = Live.DeferSharedBlocks;
            bool wasStaggeringCreation = Live.StaggerScoreboardCreation;
            bool wasStaggeringRefresh = Live.StaggerScoreboardRefresh;

            Live.DeferSharedBlocks = stutter && s.DeferSharedBlocks;
            Live.StaggerRigPool = stutter && s.StaggerRigPool;
            Live.InitialRigPoolSize = s.InitialRigPoolSize;
            Live.StaggerScoreboardCreation = stutter && s.StaggerScoreboardCreation;
            Live.StaggerScoreboardRefresh = stutter && s.StaggerScoreboardRefresh;
            Live.SkipInactiveEntityLeaveScans = stutter && s.SkipInactiveEntityLeaveScans;
            Live.RoomLeaveGc = stutter ? s.RoomLeaveGc : RoomLeaveGcMode.GameDefault;
            Live.SkipTimedAssetUnloads = stutter && s.SkipTimedAssetUnloads;
            Live.MapSanitizer = stutter ? s.MapSanitizer : SanitizerMode.Off;
            Live.ReduceDistantCosmetics = groups.CosmeticCulling && s.ReduceDistantCosmetics;

            if (!groups.VisualThrottles)
            {
                Live.TextureTransitionInterval = 1;
                Live.VoiceVisualInterval = 1;
                Live.ProximityInterval = 1;
            }

            // Work held back by a feature that was just switched off runs now instead of waiting.
            if (wasDeferringBlocks && (!Live.DeferSharedBlocks || suspended))
                SharedBlocksPatch.Poll();
            if (wasStaggeringCreation && (!Live.StaggerScoreboardCreation || suspended))
                ScoreboardPatches.FlushCreation();
            if (wasStaggeringRefresh && (!Live.StaggerScoreboardRefresh || suspended))
                ScoreboardPatches.FlushRefresh();

            CosmeticCullingPatch.Refresh(s);
        }

        /// <summary>Recomputes throttle intervals from the measured frame rate (call about once per second).</summary>
        public void UpdateThrottleIntervals(double measuredFps, OptimizationSettings s, PatchGroups groups, bool suspended)
        {
            if (s == null || !groups.VisualThrottles || suspended || DeferredToPotassium)
            {
                Live.TextureTransitionInterval = 1;
                Live.VoiceVisualInterval = 1;
                Live.ProximityInterval = 1;
                return;
            }
            Live.TextureTransitionInterval = ThrottleMath.IntervalFrames(measuredFps, s.TextureTransitionRate);
            Live.VoiceVisualInterval = ThrottleMath.IntervalFrames(measuredFps, s.VoiceVisualRate);
            Live.ProximityInterval = ThrottleMath.IntervalFrames(measuredFps, s.ProximityRate);
        }

        /// <summary>Per-frame work: at most one deferred scoreboard action and one pooled rig.</summary>
        public void Tick()
        {
            if (ScoreboardPatches.PendingCount > 0)
                ScoreboardPatches.RunNext();
            if (RigPoolPatch.IsWarming)
                RigPoolPatch.WarmNext();
        }

        /// <summary>About four times per second.</summary>
        public void Poll() => SharedBlocksPatch.Poll();

        /// <summary>Whether the local player is in a multiplayer room; false when that cannot be determined.</summary>
        public bool TryIsInRoom(out bool value)
        {
            value = false;
            if (inRoom == null)
                return false;
            try
            {
                value = inRoom();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Shutdown()
        {
            Live.DisableAll();
            Live.Suspended = true;
            try { SharedBlocksPatch.ReleaseAll(); } catch (Exception e) { log.Warn("Releasing Shared Blocks failed: " + e.Message); }
            try { ScoreboardPatches.Flush(); } catch (Exception e) { log.Warn("Flushing scoreboards failed: " + e.Message); }
            try { CosmeticCullingPatch.RestoreAll(); } catch (Exception e) { log.Warn("Restoring cosmetic culling failed: " + e.Message); }
            patches.UnpatchAll();
        }

        private Func<bool> BuildInRoomCheck()
        {
            Type networkSystem = types.Find("NetworkSystem");
            Func<object> instance = Accessors.StaticGetter<object>(networkSystem, "Instance");
            Func<object, bool> inRoomGetter = Accessors.Getter<bool>(networkSystem, "InRoom");
            if (instance == null || inRoomGetter == null)
            {
                log.Warn("NetworkSystem.Instance.InRoom not found; room-dependent staggering is disabled.");
                return null;
            }
            return () =>
            {
                object system = instance();
                return UnityObjects.IsAlive(system) && inRoomGetter(system);
            };
        }

        public static readonly string[] AllPatchIds =
        {
            EntityLeavePatch.Id,
            ScoreboardPatches.CreationId,
            ScoreboardPatches.RefreshId,
            RigPoolPatch.Id,
            RigPoolPatch.OnDemandId,
            SharedBlocksPatch.Id,
            GcPolicyPatches.RoomLeaveId,
            GcPolicyPatches.AssetUnloadId,
            MapSanitizerPatch.SafeId,
            MapSanitizerPatch.FullId,
            UpdateThrottlePatches.TextureTransitionId,
            UpdateThrottlePatches.VoiceVisualId,
            UpdateThrottlePatches.ProximityId,
            CosmeticCullingPatch.Id,
        };
    }
}

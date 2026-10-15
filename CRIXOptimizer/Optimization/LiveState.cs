// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using CRIXOptimizer.Configuration;

namespace CRIXOptimizer.Optimization
{
    /// <summary>
    /// Values read by patch code on the game's hot paths. Patches read plain static fields (no config
    /// lookups, no dictionary access); the runtime rewrites them when the profile or config changes, which is
    /// what makes every toggle work live without a restart (Potassium reads its config once at startup).
    /// </summary>
    internal static class Live
    {
        /// <summary>Panic button / master switch: every patch passes through to the original game code.</summary>
        public static bool Suspended;

        // Stutter fixes
        public static bool DeferSharedBlocks;
        public static bool StaggerRigPool;
        public static int InitialRigPoolSize = 4;
        public static bool StaggerScoreboardCreation;
        public static bool StaggerScoreboardRefresh;
        public static bool SkipInactiveEntityLeaveScans;
        public static RoomLeaveGcMode RoomLeaveGc = RoomLeaveGcMode.GameDefault;
        public static bool SkipTimedAssetUnloads;
        public static SanitizerMode MapSanitizer = SanitizerMode.Off;

        // Throttles: frames between updates (1 = every frame). Recomputed once per second from the measured FPS.
        public static int TextureTransitionInterval = 1;
        public static int VoiceVisualInterval = 1;
        public static int ProximityInterval = 1;

        // Cosmetics
        public static bool ReduceDistantCosmetics;

        // Room-leave incremental GC budget in nanoseconds.
        public static ulong IncrementalCollectBudgetNs = 2_000_000;

        public static void DisableAll()
        {
            DeferSharedBlocks = false;
            StaggerRigPool = false;
            StaggerScoreboardCreation = false;
            StaggerScoreboardRefresh = false;
            SkipInactiveEntityLeaveScans = false;
            RoomLeaveGc = RoomLeaveGcMode.GameDefault;
            SkipTimedAssetUnloads = false;
            MapSanitizer = SanitizerMode.Off;
            TextureTransitionInterval = 1;
            VoiceVisualInterval = 1;
            ProximityInterval = 1;
            ReduceDistantCosmetics = false;
        }
    }

    /// <summary>
    /// Frame counter used by staggered throttles. Defaults to Unity's Time.frameCount; tests substitute
    /// their own source. A delegate keeps the hot path free of Unity calls when running outside Unity.
    /// </summary>
    internal static class FrameClock
    {
        private static Func<int> source;

        public static void SetSource(Func<int> frameSource) => source = frameSource;

        public static int Current => source != null ? source() : 0;
    }
}

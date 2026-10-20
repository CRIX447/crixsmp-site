// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Splits each frame into the parts of Unity's player loop (no Unity dependency, unit tested). Markers placed in
    /// the player loop (<see cref="FramePhaseHooks"/>) call <see cref="Mark"/>; the time from one marker to the next
    /// is added to the segment the first one opens, so a segment that runs several times a frame (FixedUpdate) adds
    /// up. A frame starts right after Unity has waited for the previous one to be shown and read the time, which is
    /// where Time.unscaledDeltaTime is measured, so the last completed frame lines up with the delta time the runtime
    /// sees in its Update.
    /// </summary>
    internal sealed class FramePhaseTimer
    {
        private readonly List<string> names = new List<string>();
        private long[] acc = new long[8], last = new long[8];
        private int current = -1;
        private long lastTs, frameStartTs;
        private readonly double ticksPerMs;

        public FramePhaseTimer(double ticksPerSecond)
        {
            ticksPerMs = ticksPerSecond / 1000.0;
        }

        /// <summary>Frames completed since the start (stays put when the markers stop running, e.g. another mod replaced
        /// the player loop).</summary>
        public long FramesCompleted { get; private set; }
        public double LastFrameMs { get; private set; }

        /// <summary>The id of a segment, registered on first use (main thread, before the markers run).</summary>
        public int Segment(string name)
        {
            int i = names.IndexOf(name);
            if (i >= 0)
                return i;
            names.Add(name);
            if (names.Count > acc.Length)
            {
                Array.Resize(ref acc, acc.Length * 2);
                Array.Resize(ref last, last.Length * 2);
            }
            return names.Count - 1;
        }

        /// <summary>A marker: the segment that starts here; frameStart on the one marker that opens a frame.</summary>
        public void Mark(int segment, bool frameStart, long timestamp)
        {
            if (current >= 0)
                acc[current] += timestamp - lastTs;
            if (frameStart)
            {
                if (current >= 0)
                {
                    long[] t = last;
                    last = acc;
                    acc = t;
                    LastFrameMs = (timestamp - frameStartTs) / ticksPerMs;
                    FramesCompleted++;
                }
                Array.Clear(acc, 0, acc.Length);
                frameStartTs = timestamp;
            }
            current = segment;
            lastTs = timestamp;
        }

        /// <summary>The segment that took longest in the last completed frame, if that frame is the one that took
        /// frameMs (a hitch the runtime has just measured); false when it cannot tell.</summary>
        public bool Largest(double frameMs, out string segment, out double ms)
        {
            segment = "";
            ms = 0;
            if (FramesCompleted == 0 || frameMs <= 0 || Math.Abs(LastFrameMs - frameMs) > Math.Max(10.0, frameMs * 0.25))
                return false;
            int best = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (best < 0 || last[i] > last[best])
                    best = i;
            }
            if (best < 0)
                return false;
            segment = names[best];
            ms = last[best] / ticksPerMs;
            return true;
        }

        /// <summary>Plain words for a segment name ("Update.ScriptRunBehaviourUpdate", "PostLateUpdate (other)", ...).</summary>
        public static string Describe(string segment)
        {
            switch (segment)
            {
                case "TimeUpdate.WaitForLastPresentationAndUpdateTime": return "waiting for the previous frame to be shown (VR runtime or GPU)";
                case "EarlyUpdate.UpdatePreloading": return "finishing scene or asset loads";
                case "EarlyUpdate.XRUpdate": return "the VR runtime's per-frame update";
                case "FixedUpdate.ScriptRunBehaviourFixedUpdate": return "scripts' FixedUpdate (the game or mods)";
                case "FixedUpdate.PhysicsFixedUpdate": return "physics";
                case "Update.ScriptRunBehaviourUpdate": return "scripts' Update (the game or mods)";
                case "Update.ScriptRunDelayedDynamicFrameRate": return "coroutines (the game or mods)";
                case "Update.ScriptRunDelayedTasks": return "async tasks (the game or mods)";
                case "PreLateUpdate.ScriptRunBehaviourLateUpdate": return "scripts' LateUpdate (the game or mods)";
                case "PostLateUpdate.FinishFrameRendering": return "rendering (all cameras, including mods' cameras)";
                case "PostLateUpdate.PresentAfterDraw": return "handing the frame to the VR runtime or GPU";
                case "PostLateUpdate.TriggerEndOfFrameCallbacks": return "end-of-frame callbacks (the game or mods)";
                case "between frames": return "between frames (outside Unity's frame loop)";
            }
            if (segment.EndsWith(" (other)", StringComparison.Ordinal))
                return "other Unity work in " + segment.Substring(0, segment.Length - 8);
            return segment;
        }

        /// <summary>True for segments that run the game's and mods' own code.</summary>
        public static bool IsScripts(string segment) =>
            segment == "FixedUpdate.ScriptRunBehaviourFixedUpdate" || segment == "Update.ScriptRunBehaviourUpdate" ||
            segment == "Update.ScriptRunDelayedDynamicFrameRate" || segment == "Update.ScriptRunDelayedTasks" ||
            segment == "PreLateUpdate.ScriptRunBehaviourLateUpdate" || segment == "PostLateUpdate.TriggerEndOfFrameCallbacks";

        /// <summary>True for segments where Unity waits on, or hands work to, the GPU or the VR runtime.</summary>
        public static bool IsWaitOrRender(string segment) =>
            segment == "TimeUpdate.WaitForLastPresentationAndUpdateTime" || segment == "PostLateUpdate.FinishFrameRendering" ||
            segment == "PostLateUpdate.PresentAfterDraw" || segment == "EarlyUpdate.XRUpdate" || segment == "between frames";

        /// <summary>The player-loop systems that get markers of their own (phase start markers cover the rest).</summary>
        public static readonly HashSet<string> Measured = new HashSet<string>(StringComparer.Ordinal)
        {
            "TimeUpdate.WaitForLastPresentationAndUpdateTime",
            "EarlyUpdate.UpdatePreloading",
            "EarlyUpdate.XRUpdate",
            "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "FixedUpdate.PhysicsFixedUpdate",
            "Update.ScriptRunBehaviourUpdate",
            "Update.ScriptRunDelayedDynamicFrameRate",
            "Update.ScriptRunDelayedTasks",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "PostLateUpdate.FinishFrameRendering",
            "PostLateUpdate.PresentAfterDraw",
            "PostLateUpdate.TriggerEndOfFrameCallbacks",
        };
    }
}

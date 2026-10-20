// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Adds CRIX's own timing markers to Unity's player loop, around the systems in <see cref="FramePhaseTimer.Measured"/>
    /// and at the start of each phase, so a hitch can be traced to the part of the frame it was spent in (scripts,
    /// loading, physics, rendering, waiting for the VR runtime). Read-only: nothing of the game or other mods is
    /// patched, moved or removed; each marker only reads a timestamp (at most 33 per frame). Removing takes out
    /// CRIX's markers only, so player-loop changes other mods made in the meantime stay.
    /// </summary>
    internal sealed class FramePhaseHooks : IDisposable
    {
        /// <summary>The type CRIX's markers carry in the player loop (how they are found again).</summary>
        private struct CrixFrameMarker
        {
        }

        private const string FrameStartAfter = "TimeUpdate.WaitForLastPresentationAndUpdateTime";
        private readonly FramePhaseTimer timer;
        private bool installed;

        public FramePhaseHooks(FramePhaseTimer timer)
        {
            this.timer = timer;
        }

        public int MarkerCount { get; private set; }

        public void Install()
        {
            if (installed)
                return;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null || root.subSystemList.Length == 0)
                throw new InvalidOperationException("the player loop has no phases");
            root = Without(root);
            bool hasWait = false;
            foreach (PlayerLoopSystem phase in root.subSystemList)
            {
                if (phase.subSystemList == null)
                    continue;
                foreach (PlayerLoopSystem sub in phase.subSystemList)
                    hasWait |= Name(phase) + "." + Name(sub) == FrameStartAfter;
            }

            int markers = 0;
            var phases = new PlayerLoopSystem[root.subSystemList.Length];
            for (int p = 0; p < phases.Length; p++)
            {
                PlayerLoopSystem phase = root.subSystemList[p];
                string phaseName = Name(phase);
                string other = phaseName + " (other)";
                var list = new List<PlayerLoopSystem>((phase.subSystemList?.Length ?? 0) + 8);
                list.Add(Marker(other, frameStart: !hasWait && p == 0));
                markers++;
                if (phase.subSystemList != null)
                {
                    foreach (PlayerLoopSystem sub in phase.subSystemList)
                    {
                        string key = phaseName + "." + Name(sub);
                        if (!FramePhaseTimer.Measured.Contains(key))
                        {
                            list.Add(sub);
                            continue;
                        }
                        list.Add(Marker(key, frameStart: false));
                        list.Add(sub);
                        list.Add(Marker(other, frameStart: key == FrameStartAfter));
                        markers += 2;
                    }
                }
                if (p == phases.Length - 1)
                {
                    list.Add(Marker("between frames", frameStart: false));
                    markers++;
                }
                phase.subSystemList = list.ToArray();
                phases[p] = phase;
            }
            root.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(root);
            installed = true;
            MarkerCount = markers;
        }

        public void Dispose()
        {
            if (!installed)
                return;
            installed = false;
            PlayerLoop.SetPlayerLoop(Without(PlayerLoop.GetCurrentPlayerLoop()));
        }

        private PlayerLoopSystem Marker(string segment, bool frameStart)
        {
            int id = timer.Segment(segment);
            FramePhaseTimer t = timer;
            return new PlayerLoopSystem
            {
                type = typeof(CrixFrameMarker),
                updateDelegate = () => t.Mark(id, frameStart, Stopwatch.GetTimestamp()),
            };
        }

        /// <summary>The loop with CRIX's markers taken out (at any depth); everything else as it is.</summary>
        private static PlayerLoopSystem Without(PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
                return system;
            var list = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem sub in system.subSystemList)
            {
                if (sub.type == typeof(CrixFrameMarker))
                    continue;
                list.Add(Without(sub));
            }
            system.subSystemList = list.ToArray();
            return system;
        }

        private static string Name(PlayerLoopSystem system) => system.type != null ? system.type.Name : "?";
    }
}

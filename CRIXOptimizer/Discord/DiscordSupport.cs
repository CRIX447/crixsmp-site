// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Collections.Generic;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Discord
{
    /// <summary>
    /// Discord awareness. Works whether or not Discord is installed. CRIX never injects into Discord, never
    /// touches calls, audio or the microphone, and never modifies Discord; it only checks whether a Discord
    /// process is running and whether Discord's in-game overlay DLL is loaded into the game process.
    /// </summary>
    internal sealed class DiscordSupport
    {
        public bool Running { get; private set; }
        public bool OverlayHooked { get; private set; }
        public readonly List<string> Advice = new List<string>();

        public void Update(CompanionApp apps, GraphicsHook hooks, ProfileKind profile)
        {
            Running = (apps & CompanionApp.Discord) != 0;
            OverlayHooked = (hooks & GraphicsHook.DiscordOverlay) != 0;

            Advice.Clear();
            if (!Running && !OverlayHooked)
                return;
            if (profile == ProfileKind.Balanced)
                Advice.Add("Discord detected: the DiscordCall profile trims a little more game-side work for calls and screen sharing.");
            if (OverlayHooked)
                Advice.Add("Discord's in-game overlay is hooked into the game. If you see frame-time spikes, test with the overlay disabled in Discord's settings.");
        }

        public string Describe()
        {
            if (!Running && !OverlayHooked)
                return "not detected";
            return (Running ? "running" : "") + (OverlayHooked ? (Running ? ", " : "") + "overlay hooked" : "");
        }
    }
}

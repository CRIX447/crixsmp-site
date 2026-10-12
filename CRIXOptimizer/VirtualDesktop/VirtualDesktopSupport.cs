// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.VirtualDesktop
{
    /// <summary>
    /// Virtual Desktop awareness. CRIX cannot and does not control Virtual Desktop: bitrate, codec, encoder,
    /// network transport, sharpening and the streaming resolution are set in the Virtual Desktop app and are
    /// not reachable through any API available to a game plugin. What CRIX can do is reduce the game's own GPU
    /// load (headroom for the video encoder), keep frame pacing steady, and report what the VR runtime says.
    /// See docs/VIRTUAL_DESKTOP_GUIDE.md for the settings that live in Virtual Desktop itself.
    /// </summary>
    internal sealed class VirtualDesktopSupport
    {
        public enum Path
        {
            NotDetected,
            /// <summary>Virtual Desktop's own OpenXR runtime is the active runtime.</summary>
            VirtualDesktopOpenXR,
            /// <summary>Virtual Desktop streamer running, game uses SteamVR.</summary>
            ViaSteamVR,
            /// <summary>Virtual Desktop streamer running, runtime unknown.</summary>
            Running,
        }

        public Path Detected { get; private set; }
        public readonly List<string> Advice = new List<string>();

        public void Update(CompanionApp apps, string openXrRuntimeName, string xrDeviceName, ProfileKind profile)
        {
            bool streamer = (apps & CompanionApp.VirtualDesktop) != 0;
            bool vdRuntime = Mentions(openXrRuntimeName, "VirtualDesktop") || Mentions(openXrRuntimeName, "Virtual Desktop") || Mentions(openXrRuntimeName, "VDXR");

            if (vdRuntime)
                Detected = Path.VirtualDesktopOpenXR;
            else if (streamer && ((apps & CompanionApp.SteamVR) != 0 || Mentions(openXrRuntimeName, "SteamVR") || Mentions(xrDeviceName, "OpenVR")))
                Detected = Path.ViaSteamVR;
            else if (streamer)
                Detected = Path.Running;
            else
                Detected = Path.NotDetected;

            Advice.Clear();
            if (Detected == Path.NotDetected)
                return;
            if (profile != ProfileKind.VirtualDesktop)
                Advice.Add("Virtual Desktop detected: the VirtualDesktop profile is tuned for wireless streaming.");
            if (Detected == Path.ViaSteamVR)
                Advice.Add("Game is running through SteamVR. If your Virtual Desktop version offers its own OpenXR runtime (VDXR), selecting it in Virtual Desktop avoids SteamVR's compositor; test both.");
            Advice.Add("Bitrate, codec, sharpening and streaming resolution are set in the Virtual Desktop app, not by CRIX (see docs/VIRTUAL_DESKTOP_GUIDE.md).");
        }

        public string Describe()
        {
            switch (Detected)
            {
                case Path.VirtualDesktopOpenXR: return "Virtual Desktop (its OpenXR runtime is active)";
                case Path.ViaSteamVR: return "Virtual Desktop streamer running, game via SteamVR";
                case Path.Running: return "Virtual Desktop streamer running";
                default: return "not detected";
            }
        }

        private static bool Mentions(string text, string needle) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

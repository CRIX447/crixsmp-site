// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Text;
using CRIXOptimizer.Configuration;

namespace CRIXOptimizer.Core
{
    [Flags]
    public enum CompanionApp
    {
        None = 0,
        Obs = 1 << 0,
        StreamlabsDesktop = 1 << 1,
        Discord = 1 << 2,
        VirtualDesktop = 1 << 3,
        SteamVR = 1 << 4,
        MetaQuestLink = 1 << 5,
        Alvr = 1 << 6,
        Spotify = 1 << 7,
    }

    [Flags]
    public enum GraphicsHook
    {
        None = 0,
        ReShade = 1 << 0,
        ObsGameCapture = 1 << 1,
        DiscordOverlay = 1 << 2,
        SteamOverlay = 1 << 3,
        RivaTuner = 1 << 4,
        NvidiaOverlay = 1 << 5,
    }

    public struct ProfileSuggestion
    {
        public ProfileKind Profile;
        public VirtualDesktopPreset VirtualDesktopPreset;
        public string Reason;
    }

    /// <summary>
    /// Classifies process and module names. Read-only: the plugin only compares names; it never injects into or
    /// changes other processes. (Mono's Process API opens a handle to each process to read its name; CRIX
    /// releases those handles right away. Processes running as administrator are not visible to a game that runs
    /// without administrator rights.)
    /// </summary>
    public static class CompanionApps
    {
        private static readonly Dictionary<string, CompanionApp> ProcessNames =
            new Dictionary<string, CompanionApp>(StringComparer.OrdinalIgnoreCase)
            {
                { "obs64", CompanionApp.Obs },
                { "obs32", CompanionApp.Obs },
                { "obs", CompanionApp.Obs },
                { "Streamlabs OBS", CompanionApp.StreamlabsDesktop },
                { "Streamlabs Desktop", CompanionApp.StreamlabsDesktop },
                { "Discord", CompanionApp.Discord },
                { "DiscordPTB", CompanionApp.Discord },
                { "DiscordCanary", CompanionApp.Discord },
                { "DiscordDevelopment", CompanionApp.Discord },
                { "VirtualDesktop.Streamer", CompanionApp.VirtualDesktop },
                { "VirtualDesktop.Service", CompanionApp.VirtualDesktop },
                { "vrserver", CompanionApp.SteamVR },
                { "vrcompositor", CompanionApp.SteamVR },
                { "vrmonitor", CompanionApp.SteamVR },
                { "OVRServer_x64", CompanionApp.MetaQuestLink },
                { "OculusClient", CompanionApp.MetaQuestLink },
                { "ALVR Dashboard", CompanionApp.Alvr },
                { "alvr_dashboard", CompanionApp.Alvr },
                { "Spotify", CompanionApp.Spotify },
            };

        // Module (DLL) names loaded into the game process by well-known overlays/capture tools.
        private static readonly Dictionary<string, GraphicsHook> ModuleNames =
            new Dictionary<string, GraphicsHook>(StringComparer.OrdinalIgnoreCase)
            {
                { "ReShade64.dll", GraphicsHook.ReShade },
                { "ReShade32.dll", GraphicsHook.ReShade },
                { "graphics-hook64.dll", GraphicsHook.ObsGameCapture },
                { "graphics-hook32.dll", GraphicsHook.ObsGameCapture },
                { "DiscordHook64.dll", GraphicsHook.DiscordOverlay },
                { "DiscordHook.dll", GraphicsHook.DiscordOverlay },
                { "GameOverlayRenderer64.dll", GraphicsHook.SteamOverlay },
                { "RTSSHooks64.dll", GraphicsHook.RivaTuner },
                { "nvspcap64.dll", GraphicsHook.NvidiaOverlay },
            };

        public static CompanionApp ClassifyProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName))
                return CompanionApp.None;
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName = processName.Substring(0, processName.Length - 4);
            return ProcessNames.TryGetValue(processName, out CompanionApp app) ? app : CompanionApp.None;
        }

        public static CompanionApp ClassifyProcesses(IEnumerable<string> processNames)
        {
            CompanionApp result = CompanionApp.None;
            if (processNames == null)
                return result;
            foreach (string name in processNames)
                result |= ClassifyProcess(name);
            return Normalize(result);
        }

        /// <summary>
        /// Streamlabs Desktop runs its OBS backend as a child process named obs64, so "obs64 + Streamlabs" is
        /// reported as Streamlabs only. (Running OBS Studio and Streamlabs at the same time cannot be told apart by
        /// process name; that combination is reported as Streamlabs.)
        /// </summary>
        public static CompanionApp Normalize(CompanionApp apps) =>
            (apps & CompanionApp.StreamlabsDesktop) != 0 ? apps & ~CompanionApp.Obs : apps;

        public static GraphicsHook ClassifyModule(string moduleFileName)
        {
            if (string.IsNullOrEmpty(moduleFileName))
                return GraphicsHook.None;
            return ModuleNames.TryGetValue(moduleFileName, out GraphicsHook hook) ? hook : GraphicsHook.None;
        }

        /// <summary>Suggests a profile for the detected companion apps, or null when nothing relevant runs.</summary>
        public static ProfileSuggestion? Suggest(CompanionApp apps)
        {
            bool streaming = (apps & (CompanionApp.Obs | CompanionApp.StreamlabsDesktop)) != 0;
            bool virtualDesktop = (apps & CompanionApp.VirtualDesktop) != 0;
            bool discord = (apps & CompanionApp.Discord) != 0;

            if (virtualDesktop && streaming)
                return new ProfileSuggestion { Profile = ProfileKind.VirtualDesktop, VirtualDesktopPreset = VirtualDesktopPreset.Streaming, Reason = "Virtual Desktop and a streaming app are running" };
            if (virtualDesktop)
                return new ProfileSuggestion { Profile = ProfileKind.VirtualDesktop, VirtualDesktopPreset = VirtualDesktopPreset.Balanced, Reason = "Virtual Desktop is running" };
            if (streaming)
                return new ProfileSuggestion { Profile = ProfileKind.OBSStreaming, Reason = "OBS/Streamlabs is running" };
            if (discord)
                return new ProfileSuggestion { Profile = ProfileKind.DiscordCall, Reason = "Discord is running" };
            return null;
        }

        public static string Describe(CompanionApp apps)
        {
            if (apps == CompanionApp.None)
                return "none detected";
            var sb = new StringBuilder();
            AppendFlag(sb, apps, CompanionApp.Obs, "OBS Studio");
            AppendFlag(sb, apps, CompanionApp.StreamlabsDesktop, "Streamlabs Desktop");
            AppendFlag(sb, apps, CompanionApp.Discord, "Discord");
            AppendFlag(sb, apps, CompanionApp.VirtualDesktop, "Virtual Desktop");
            AppendFlag(sb, apps, CompanionApp.SteamVR, "SteamVR");
            AppendFlag(sb, apps, CompanionApp.MetaQuestLink, "Meta Quest Link");
            AppendFlag(sb, apps, CompanionApp.Alvr, "ALVR");
            AppendFlag(sb, apps, CompanionApp.Spotify, "Spotify");
            return sb.ToString();
        }

        public static string Describe(GraphicsHook hooks)
        {
            if (hooks == GraphicsHook.None)
                return "none detected";
            var sb = new StringBuilder();
            AppendFlag(sb, hooks, GraphicsHook.ReShade, "ReShade");
            AppendFlag(sb, hooks, GraphicsHook.ObsGameCapture, "OBS game-capture hook");
            AppendFlag(sb, hooks, GraphicsHook.DiscordOverlay, "Discord overlay");
            AppendFlag(sb, hooks, GraphicsHook.SteamOverlay, "Steam overlay");
            AppendFlag(sb, hooks, GraphicsHook.RivaTuner, "RivaTuner/RTSS");
            AppendFlag(sb, hooks, GraphicsHook.NvidiaOverlay, "NVIDIA overlay/capture");
            return sb.ToString();
        }

        private static void AppendFlag<T>(StringBuilder sb, T value, T flag, string label) where T : Enum
        {
            if (!value.HasFlag(flag))
                return;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(label);
        }
    }
}

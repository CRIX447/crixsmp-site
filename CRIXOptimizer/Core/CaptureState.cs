// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Core
{
    /// <summary>What kind of capture/streaming load is present. Ordered by weight: OBS outranks Discord.</summary>
    public enum CaptureKind
    {
        None = 0,
        Discord = 1,
        Obs = 2,
    }

    public enum StreamingTrigger
    {
        /// <summary>Best signal available: OBS's live/recording state when its WebSocket is connected, otherwise OBS running or capturing the game; Discord running.</summary>
        Auto,
        /// <summary>Whenever OBS/Streamlabs or Discord is running (they use GPU/CPU even when idle: OBS renders its preview).</summary>
        WhenAppRunning,
        /// <summary>Only while OBS reports streaming or recording (needs the OBS WebSocket); otherwise while OBS's game-capture hook is in the game. Discord: while its hook is in the game.</summary>
        WhenRecordingOrLive,
    }

    public struct CaptureSignals
    {
        public CompanionApp Apps;
        public GraphicsHook Hooks;
        public bool ObsWebSocketConnected;
        public bool ObsStreaming;
        public bool ObsRecording;
    }

    /// <summary>
    /// Turns raw detection signals into a stable "capture active" state. Escalation (None to Discord to OBS) is
    /// immediate; dropping back waits for <see cref="HoldSeconds"/> without the stronger signal, so a brief
    /// WebSocket reconnect or a scene change does not flip profiles back and forth.
    /// </summary>
    public sealed class CaptureStateMachine
    {
        private double lastStrongSignal = double.NegativeInfinity;

        public StreamingTrigger Trigger = StreamingTrigger.Auto;
        public double HoldSeconds = 20;

        public CaptureKind Active { get; private set; }
        public string Reason { get; private set; } = "";

        /// <summary>Returns true when <see cref="Active"/> changed.</summary>
        public bool Update(double now, CaptureSignals signals)
        {
            CaptureKind raw = Evaluate(Trigger, signals, out string why);
            if (raw >= Active)
            {
                lastStrongSignal = now;
                bool changed = raw != Active;
                Active = raw;
                Reason = why;
                return changed;
            }

            if (now - lastStrongSignal < HoldSeconds)
                return false;
            Active = raw;
            Reason = why;
            lastStrongSignal = now;
            return true;
        }

        public void Reset()
        {
            Active = CaptureKind.None;
            Reason = "";
            lastStrongSignal = double.NegativeInfinity;
        }

        public static CaptureKind Evaluate(StreamingTrigger trigger, CaptureSignals s, out string reason)
        {
            bool obsApp = (s.Apps & (CompanionApp.Obs | CompanionApp.StreamlabsDesktop)) != 0;
            bool obsHook = (s.Hooks & GraphicsHook.ObsGameCapture) != 0;
            bool obsLive = s.ObsWebSocketConnected && (s.ObsStreaming || s.ObsRecording);
            bool discordApp = (s.Apps & CompanionApp.Discord) != 0;
            bool discordHook = (s.Hooks & GraphicsHook.DiscordOverlay) != 0;

            switch (trigger)
            {
                case StreamingTrigger.WhenAppRunning:
                    if (obsApp || obsHook)
                    {
                        reason = obsHook ? "OBS is capturing the game" : "OBS/Streamlabs is running";
                        return CaptureKind.Obs;
                    }
                    if (discordApp)
                    {
                        reason = "Discord is running";
                        return CaptureKind.Discord;
                    }
                    break;

                case StreamingTrigger.WhenRecordingOrLive:
                    if (s.ObsWebSocketConnected)
                    {
                        if (obsLive)
                        {
                            reason = ObsLiveReason(s);
                            return CaptureKind.Obs;
                        }
                    }
                    else if (obsHook)
                    {
                        reason = "OBS is capturing the game (enable the OBS WebSocket for exact live/recording state)";
                        return CaptureKind.Obs;
                    }
                    if (discordHook)
                    {
                        reason = "Discord is hooked into the game";
                        return CaptureKind.Discord;
                    }
                    break;

                default: // Auto
                    if (s.ObsWebSocketConnected)
                    {
                        if (obsLive)
                        {
                            reason = ObsLiveReason(s);
                            return CaptureKind.Obs;
                        }
                        // OBS connected and idle: not recording, not live. Fall through to Discord.
                    }
                    else if (obsApp || obsHook)
                    {
                        reason = obsHook ? "OBS is capturing the game" : "OBS/Streamlabs is running";
                        return CaptureKind.Obs;
                    }
                    if (discordApp || discordHook)
                    {
                        reason = discordHook ? "Discord is hooked into the game" : "Discord is running";
                        return CaptureKind.Discord;
                    }
                    break;
            }
            reason = "";
            return CaptureKind.None;
        }

        private static string ObsLiveReason(CaptureSignals s) =>
            s.ObsStreaming && s.ObsRecording ? "OBS is live and recording"
            : s.ObsStreaming ? "OBS is live" : "OBS is recording";
    }
}

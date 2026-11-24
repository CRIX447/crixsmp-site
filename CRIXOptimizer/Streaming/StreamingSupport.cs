// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Collections.Generic;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Streaming
{
    /// <summary>
    /// OBS / Streamlabs awareness. CRIX never reads or changes OBS settings, never changes process priorities
    /// and cannot fix encoder overload, upload bandwidth or capture-card problems; it reduces the game's own
    /// load and reports frame-time problems. It warns when the dashboard is visible while OBS's game-capture
    /// hook is loaded, because the dashboard is drawn into the captured window.
    /// </summary>
    internal sealed class StreamingSupport
    {
        public bool StreamingAppRunning { get; private set; }
        public bool GameCaptureHooked { get; private set; }
        public readonly List<string> Advice = new List<string>();

        public void Update(CompanionApp apps, GraphicsHook hooks, ProfileKind profile, bool dashboardVisible)
        {
            StreamingAppRunning = (apps & (CompanionApp.Obs | CompanionApp.StreamlabsDesktop)) != 0;
            GameCaptureHooked = (hooks & GraphicsHook.ObsGameCapture) != 0;

            Advice.Clear();
            if (!StreamingAppRunning && !GameCaptureHooked)
                return;
            if (profile != ProfileKind.OBSStreaming && profile != ProfileKind.VirtualDesktop)
                Advice.Add("OBS/Streamlabs detected: the OBSStreaming profile is tuned for steadier frame pacing while recording.");
            if (dashboardVisible && GameCaptureHooked)
                Advice.Add("OBS game capture is hooked into the game: the CRIX dashboard will appear in your recording/stream.");
            Advice.Add("Encoder overload, dropped network frames and upload limits are OBS-side issues; check OBS's own stats (View > Stats).");
        }

        public string Describe()
        {
            if (!StreamingAppRunning && !GameCaptureHooked)
                return "not detected";
            return (StreamingAppRunning ? "streaming app running" : "") +
                   (GameCaptureHooked ? (StreamingAppRunning ? ", " : "") + "OBS game-capture hook loaded" : "");
        }
    }
}

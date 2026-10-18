// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// The "notes" CRIX takes while the user plays (no Unity dependency, unit tested): a timeline every 10 seconds
    /// (frame rate, 1 % low, worst frame, hitches, zone, players in the room, recording/streaming, profile, adaptive
    /// level, memory), events (zone changes, joining/leaving rooms, players joining/leaving, streaming starting/stopping,
    /// profile changes, big hitches with their cause) and summaries per zone, per lobby size and per streaming state.
    /// No room codes, player names or anything else personal are recorded. The runtime writes the text to
    /// BepInEx/CRIXOptimizer/notes/ so the user can send it in for tuning.
    ///
    /// Startup is kept apart: right after the game opens, the game and mods (e.g. Sakuraa Client) load their content
    /// and cause hitches that say nothing about the maps. The first minute counts as loading, extended by 20 seconds
    /// after each big hitch in it (at most 5 minutes); its frames and hitches are summarised on their own and left out
    /// of the per-zone, per-lobby and per-streaming summaries and the worst-hitch list.
    /// </summary>
    internal sealed class PerformanceNotes
    {
        /// <summary>Frame-time histogram: 0.25 ms buckets up to 50 ms, 2 ms buckets up to 250 ms, then one overflow bucket.</summary>
        internal sealed class FrameHistogram
        {
            private const int FineBuckets = 200;   // 0..50 ms
            private const int CoarseBuckets = 100; // 50..250 ms
            private readonly int[] counts = new int[FineBuckets + CoarseBuckets + 1];

            public int Count { get; private set; }
            public double TotalMs { get; private set; }
            public float MaxMs { get; private set; }

            public void Add(float ms)
            {
                int i = ms < 50f ? (int)(ms / 0.25f) : ms < 250f ? FineBuckets + (int)((ms - 50f) / 2f) : counts.Length - 1;
                counts[Math.Max(0, Math.Min(counts.Length - 1, i))]++;
                Count++;
                TotalMs += ms;
                if (ms > MaxMs)
                    MaxMs = ms;
            }

            public FrameHistogram Clone()
            {
                var copy = new FrameHistogram();
                Array.Copy(counts, copy.counts, counts.Length);
                copy.Count = Count;
                copy.TotalMs = TotalMs;
                copy.MaxMs = MaxMs;
                return copy;
            }

            public void Clear()
            {
                Array.Clear(counts, 0, counts.Length);
                Count = 0;
                TotalMs = 0;
                MaxMs = 0;
            }

            /// <summary>Upper edge of the bucket holding the given percentile (e.g. 0.99), in ms.</summary>
            public double Percentile(double p)
            {
                if (Count == 0)
                    return 0;
                long target = (long)Math.Ceiling(Count * p);
                long seen = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    seen += counts[i];
                    if (seen >= target)
                        return i < FineBuckets ? (i + 1) * 0.25 : i < counts.Length - 1 ? 50 + (i - FineBuckets + 1) * 2.0 : MaxMs;
                }
                return MaxMs;
            }

            public double AverageFps => TotalMs > 0 ? Count / (TotalMs / 1000.0) : 0;
            public double Low1Fps => Percentile(0.99) > 0 ? 1000.0 / Percentile(0.99) : 0;
        }

        internal sealed class Segment
        {
            public readonly string Key;
            public readonly FrameHistogram Frames;
            public int Hitches;
            public int BigHitches;
            public Segment(string key) : this(key, new FrameHistogram()) { }
            private Segment(string key, FrameHistogram frames) { Key = key; Frames = frames; }
            public Segment Clone() => new Segment(Key, Frames.Clone()) { Hitches = Hitches, BigHitches = BigHitches };
        }

        /// <summary>A copy of everything recorded so far, taken on the main thread in well under a millisecond, so the text
        /// can be built and written on a worker thread while recording goes on.</summary>
        internal sealed class Frozen
        {
            public string Stamp;
            public List<Segment> Zones, Lobbies, Captures;
            public (string stamp, float ms, string cause, string zone, string lobby)[] Worst;
            public string[] Events;
            public int DroppedEvents;
            public string[] Timeline;
            public bool TimelineFull;
            public Segment Startup;
            public string StartupEnd;
            public bool StartupOver;

            public string Render(IEnumerable<string> header)
            {
                var sb = new StringBuilder(64 * 1024);
                sb.Append("CRIX OPTIMIZER performance notes\n");
                foreach (string line in header)
                    sb.Append(line).Append('\n');
                sb.Append("Recorded: ").Append(Stamp).Append(" (h:mm:ss since the game started). No room codes or player names are recorded.\n\n");
                sb.Append("== Startup (game and mods loading) ==\n");
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}: {2:0.0} FPS average, worst {3:0} ms, {4} big hitches. Normal while the game and mods (e.g. Sakuraa Client) load; " +
                    "left out of the summaries below.\n\n",
                    StartupOver ? "Until" : "Still loading at", StartupEnd, Startup.Frames.AverageFps, Startup.Frames.MaxMs, Startup.BigHitches));
                AppendSegments(sb, "== Per zone (map) ==", Zones);
                AppendSegments(sb, "== Per lobby size ==", Lobbies);
                AppendSegments(sb, "== Recording / streaming ==", Captures);
                sb.Append("== Worst hitches ==\n");
                if (Worst.Length == 0)
                    sb.Append("none over 70 ms\n");
                foreach (var h in Worst)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} {1:0} ms: {2} (zone {3}, {4})\n", h.stamp, h.ms, h.cause, h.zone, h.lobby));
                sb.Append('\n');
                sb.Append("== Events ==\n");
                foreach (string e in Events)
                    sb.Append(e).Append('\n');
                if (DroppedEvents > 0)
                    sb.Append("(").Append(DroppedEvents).Append(" later events not recorded: limit reached)\n");
                sb.Append('\n');
                sb.Append("== Timeline (every 10 s) ==\n");
                foreach (string line in Timeline)
                    sb.Append(line).Append('\n');
                if (TimelineFull)
                    sb.Append("(timeline limit reached)\n");
                return sb.ToString();
            }

            private static void AppendSegments(StringBuilder sb, string title, List<Segment> list)
            {
                sb.Append(title).Append('\n');
                list = list.FindAll(s => s.Frames.Count > 0);
                list.Sort((a, b) => b.Frames.TotalMs.CompareTo(a.Frames.TotalMs));
                if (list.Count == 0)
                    sb.Append("no frames yet\n");
                foreach (Segment s in list)
                {
                    double minutes = s.Frames.TotalMs / 60000.0;
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1:0.0} min, {2:0.0} FPS average, 1% low {3:0.0}, worst {4:0} ms, {5:0.0} hitches/min, {6} big\n",
                        s.Key, minutes, s.Frames.AverageFps, s.Frames.Low1Fps, s.Frames.MaxMs, minutes > 0 ? s.Hitches / minutes : 0, s.BigHitches));
                }
                sb.Append('\n');
            }
        }

        /// <summary>What the runtime knows at each timeline line.</summary>
        public struct Snapshot
        {
            public string Profile;
            public int AdaptiveLevel;
            public long HeapBytes;
            public long UnityBytes;
            public float EyeScale;
            public bool VrActive;
        }

        public const int MaxEvents = 3000;
        public const int MaxTimelineLines = 2160; // 6 hours at one line per 10 s

        private readonly Dictionary<string, Segment> zones = new Dictionary<string, Segment>();
        private readonly Dictionary<string, Segment> lobbies = new Dictionary<string, Segment>();
        private readonly Dictionary<string, Segment> captures = new Dictionary<string, Segment>();
        private readonly FrameHistogram window = new FrameHistogram();
        private readonly List<string> timeline = new List<string>();
        private readonly List<string> events = new List<string>();
        private readonly List<(double t, float ms, string cause, string zone, string lobby)> worstHitches = new List<(double, float, string, string, string)>();
        private Segment zone, lobby, capture;
        private readonly Segment startup = new Segment("startup");
        private double startupEnd = double.NaN;
        private bool startupOver;
        public const double StartupMinSeconds = 60, StartupQuietSeconds = 20, StartupMaxSeconds = 300;

        /// <summary>True while the game and mods are still loading (see the class comment).</summary>
        public bool InStartup => !startupOver;
        private string zoneName = "unknown", lobbyName = "not in a room", captureName = "not recording or streaming";
        private int windowHitches, windowBig;
        private int droppedEvents;
        private double startTime = -1;
        private double hitchBudgetMs = 1000.0 / 90;

        public double StartTime => startTime;
        public int EventCount => events.Count;
        public int TimelineCount => timeline.Count;

        public PerformanceNotes()
        {
            SetZone("unknown");
            SetLobby(-1);
            SetCapture("");
        }

        /// <summary>Frames over twice this budget (one headset frame) count as hitches.</summary>
        public void SetRefreshRate(double hz)
        {
            if (hz > 1)
                hitchBudgetMs = 1000.0 / hz;
        }

        /// <summary>Lobby-size bucket for a player count (0 = not in a room, -1 = cannot be read).</summary>
        public static string LobbyBucket(int players) =>
            players < 0 ? "lobby size unknown" : players == 0 ? "not in a room" : players <= 4 ? "1-4 players" : players <= 9 ? "5-9 players" : players <= 14 ? "10-14 players" : "15-20 players";

        public void SetZone(string name)
        {
            zoneName = string.IsNullOrEmpty(name) ? "unknown" : name;
            zone = Get(zones, zoneName);
        }

        public void SetLobby(int players)
        {
            lobbyName = LobbyBucket(players);
            lobby = Get(lobbies, lobbyName);
        }

        public void SetCapture(string what)
        {
            captureName = string.IsNullOrEmpty(what) ? "not recording or streaming" : what;
            capture = Get(captures, captureName);
        }

        /// <summary>Per frame. Frames over 5 s (pauses) are ignored.</summary>
        public void AddFrame(float dtSeconds, double now)
        {
            if (startTime < 0)
            {
                startTime = now;
                startupEnd = now + StartupMinSeconds;
            }
            if (!startupOver && now >= startupEnd)
            {
                startupOver = true;
                AddEvent(now, string.Format(CultureInfo.InvariantCulture, "startup finished: game and mods loaded ({0} big hitches while loading)", startup.BigHitches));
            }
            if (dtSeconds <= 0f || dtSeconds >= 5f)
                return;
            float ms = dtSeconds * 1000f;
            bool hitch = ms > hitchBudgetMs * 2;
            bool big = ms > 70f;
            window.Add(ms);
            if (hitch)
                windowHitches++;
            if (big)
                windowBig++;
            if (!startupOver)
            {
                Count(startup, ms, hitch, big);
                return;
            }
            Count(zone, ms, hitch, big);
            Count(lobby, ms, hitch, big);
            Count(capture, ms, hitch, big);
        }

        private static void Count(Segment s, float ms, bool hitch, bool big)
        {
            s.Frames.Add(ms);
            if (hitch)
                s.Hitches++;
            if (big)
                s.BigHitches++;
        }

        public void AddEvent(double now, string text)
        {
            if (events.Count >= MaxEvents)
            {
                droppedEvents++;
                return;
            }
            events.Add(Stamp(now) + " " + text);
        }

        /// <summary>A frame over 70 ms with what CRIX thinks caused it; the ten worst are kept for the summary.</summary>
        public void AddBigHitch(double now, float ms, string cause)
        {
            if (!startupOver)
            {
                // Still loading: keep loading going while big hitches continue (bounded), and keep them out of the worst list.
                if (startTime >= 0)
                    startupEnd = Math.Min(startTime + StartupMaxSeconds, Math.Max(startupEnd, now + StartupQuietSeconds));
                AddEvent(now, string.Format(CultureInfo.InvariantCulture, "[loading] hitch {0:0} ms: {1}", ms, cause));
                return;
            }
            AddEvent(now, string.Format(CultureInfo.InvariantCulture, "hitch {0:0} ms: {1} (zone {2}, {3})", ms, cause, zoneName, lobbyName));
            worstHitches.Add((now, ms, cause, zoneName, lobbyName));
            worstHitches.Sort((a, b) => b.ms.CompareTo(a.ms));
            if (worstHitches.Count > 10)
                worstHitches.RemoveAt(worstHitches.Count - 1);
        }

        /// <summary>Every 10 seconds: one timeline line for the frames since the last one.</summary>
        public void Tick(double now, Snapshot s)
        {
            if (window.Count > 0 && timeline.Count < MaxTimelineLines)
            {
                timeline.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1,5:0.0} FPS, 1% low {2,5:0.0}, worst {3,4:0} ms, hitches {4,2} (big {5}) | {6} | {7} | {8} | {9}, adaptive {10}, eye scale {11:0.00} | heap {12}, Unity {13}{14}{15}",
                    Stamp(now), window.AverageFps, window.Low1Fps, window.MaxMs, windowHitches, windowBig, zoneName, lobbyName, captureName,
                    s.Profile ?? "", s.AdaptiveLevel, s.EyeScale, Mb(s.HeapBytes), Mb(s.UnityBytes), s.VrActive ? "" : " | headset not active",
                    startupOver ? "" : " | loading"));
            }
            window.Clear();
            windowHitches = 0;
            windowBig = 0;
        }

        /// <summary>The whole notes text: header lines first, then summaries, worst hitches, events and the timeline.</summary>
        public string Build(IEnumerable<string> header, double now) => Freeze(now).Render(header);

        /// <summary>Main thread: copies what was recorded (references and a few small arrays) for rendering elsewhere.</summary>
        internal Frozen Freeze(double now)
        {
            var worst = new (string, float, string, string, string)[worstHitches.Count];
            for (int i = 0; i < worstHitches.Count; i++)
                worst[i] = (Stamp(worstHitches[i].t), worstHitches[i].ms, worstHitches[i].cause, worstHitches[i].zone, worstHitches[i].lobby);
            return new Frozen
            {
                Stamp = Stamp(now),
                Zones = CloneAll(zones),
                Lobbies = CloneAll(lobbies),
                Captures = CloneAll(captures),
                Worst = worst,
                Events = events.ToArray(),
                DroppedEvents = droppedEvents,
                Timeline = timeline.ToArray(),
                TimelineFull = timeline.Count >= MaxTimelineLines,
                Startup = startup.Clone(),
                StartupEnd = startTime < 0 ? "0:00:00" : Stamp(Math.Min(startupEnd, startupOver ? startupEnd : now)),
                StartupOver = startupOver,
            };
        }

        private static List<Segment> CloneAll(Dictionary<string, Segment> map)
        {
            var list = new List<Segment>(map.Count);
            foreach (Segment s in map.Values)
                list.Add(s.Clone());
            return list;
        }

        internal IEnumerable<Segment> Zones => zones.Values;
        internal IEnumerable<Segment> Lobbies => lobbies.Values;

        private static Segment Get(Dictionary<string, Segment> map, string key)
        {
            if (!map.TryGetValue(key, out Segment s))
                map[key] = s = new Segment(key);
            return s;
        }

        private string Stamp(double now)
        {
            double t = startTime < 0 ? 0 : Math.Max(0, now - startTime);
            var span = TimeSpan.FromSeconds(t);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds);
        }

        private static string Mb(long bytes) =>
            bytes < 0 ? "n/a" : (bytes / (1024.0 * 1024.0)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }
}

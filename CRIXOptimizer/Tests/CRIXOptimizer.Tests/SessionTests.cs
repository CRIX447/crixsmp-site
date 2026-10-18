using System;
using System.Linq;
using CRIXOptimizer.Compatibility;
using CRIXOptimizer.Diagnostics;

namespace CRIXOptimizer.Tests
{
    public sealed class SessionTrendTests
    {
        private const long MB = 1024L * 1024;

        /// <summary>Plays <paramref name="minutes"/> minutes; each minute's fps, memory and state come from the callbacks.</summary>
        private static SessionTrend Play(int minutes, Func<int, double> fps, Func<int, SessionTrend.Context> ctx, Func<int, int, float> hitchMs = null)
        {
            var t = new SessionTrend();
            double now = 1000;
            int gcs = 0;
            for (int m = 0; m < minutes; m++)
            {
                double end = now + 60;
                int frame = 0;
                while (now < end)
                {
                    float dt = hitchMs != null && hitchMs(m, frame) > 0 ? hitchMs(m, frame) / 1000f : (float)(1.0 / fps(m));
                    t.AddFrame(dt, now);
                    now += dt;
                    frame++;
                }
                Assert.True(t.MinuteDue(now));
                SessionTrend.Context c = ctx(m);
                gcs += c.GcCount;
                c.GcCount = gcs;
                Assert.True(t.CloseMinute(now, c) != null);
            }
            return t;
        }

        private static SessionTrend.Context Ctx(long unityMb = 2600, bool capturing = false, string profile = "Balanced", bool vr = true, int gcs = 1, int level = 0) =>
            new SessionTrend.Context { HeapBytes = 400 * MB, UnityBytes = unityMb * MB, GfxBytes = -1, GcCount = gcs, Profile = profile, Capturing = capturing,
                                       AdaptiveLevel = level, VrActive = vr, RefreshHz = 120 };

        [Test]
        public void SteadySessionHasNoDropAndPerMinuteLines()
        {
            var t = Play(12, m => 100, m => Ctx(), (m, f) => f == 10 ? 90f : 0f);
            Assert.Equal(null, t.FindSlowDrop());
            Assert.Equal(12, t.Minutes.Count);
            Assert.Near(100, t.Minutes[5].AvgFps, 1.5);
            Assert.Equal(1, t.Minutes[5].Hitches, "one frame over twice the 120 Hz budget");
            Assert.Equal(1, t.Minutes[5].BigHitches);
            Assert.Equal(1, t.Minutes[5].Gcs, "garbage collections per minute from the running count");
            var lines = t.Describe(30).ToList();
            Assert.Equal(12, lines.Count);
            Assert.True(lines[0].StartsWith("min   1:") && lines[0].Contains("FPS") && lines[0].Contains("Unity 2600 MB"), lines[0]);
        }

        [Test]
        public void SlowDropWhileStreamingNamesWhatChanged()
        {
            // 110 FPS at first; streaming starts in minute 4; Unity memory grows 150 MB a minute; frame rate sinks to 85.
            var t = Play(14, m => m < 4 ? 110 : Math.Max(85, 110 - (m - 3) * 4),
                         m => Ctx(unityMb: 2600 + m * 150, capturing: m >= 4, profile: m >= 4 ? "OBSStreaming" : "Balanced"));
            string drop = t.FindSlowDrop();
            Assert.True(drop != null, "a 25 FPS drop is reported");
            Assert.True(drop.Contains("from about 110 to 85 FPS"), drop);
            Assert.True(drop.Contains("after recording or streaming started"), drop);
            Assert.True(drop.Contains("profile changed from Balanced to OBSStreaming"), drop);
            Assert.True(drop.Contains("Unity memory grew by") && drop.Contains("DeferredAssetUnload"), drop);
        }

        [Test]
        public void DropWithNothingVisibleNamesOutsideCauses()
        {
            var t = Play(12, m => m < 5 ? 118 : 96, m => Ctx());
            string drop = t.FindSlowDrop();
            Assert.True(drop != null && drop.Contains("Nothing CRIX can see changed") && drop.Contains("encoder"), drop);
        }

        [Test]
        public void IgnoresWarmUpIdleHeadsetAndSmallChanges()
        {
            Assert.Equal(null, Play(7, m => m < 3 ? 120 : 80, m => Ctx()).FindSlowDrop(), "too few minutes");
            Assert.Equal(null, Play(12, m => m < 6 ? 100 : 94, m => Ctx()).FindSlowDrop(), "a 6 % change is not a drop");
            Assert.Equal(null, Play(12, m => m < 2 ? 40 : 100, m => Ctx()).FindSlowDrop(), "the first two minutes (loading) are not the baseline");
            // Headset off for most of the session: those minutes do not count.
            Assert.Equal(null, Play(12, m => m < 9 ? 50 : 100, m => Ctx(vr: m >= 9)).FindSlowDrop());
        }

        [Test]
        public void KeepsABoundedHistory()
        {
            var t = new SessionTrend();
            double now = 0;
            for (int m = 0; m < SessionTrend.MaxMinutes + 20; m++)
            {
                t.AddFrame(0.01f, now);
                now += 60;
                t.CloseMinute(now, Ctx());
            }
            Assert.Equal(SessionTrend.MaxMinutes, t.Minutes.Count);
            Assert.Equal(null, new SessionTrend().CloseMinute(5, Ctx()), "a minute without frames is not recorded");
        }
    }

    public sealed class OptimizerModsSupportTests
    {
        [Test]
        public void GorillaOptimizerTogglesAreSortedByWhatTheyDoUnderUrp()
        {
            var on = new[] { "LowTextures", "NoShadows", "LowRenderScale", "LowPhysicsRate", "CapFPS", "NoAniso" };
            var lines = OptimizerModsSupport.Describe(k => on.Contains(k), urp: true, iHateAliens: false, potassium: false);
            string main = lines[0];
            Assert.True(main.Contains("in effect: Low Textures, No Anisotropic, Low Physics Rate."), main);
            Assert.True(main.Contains("No effect in Gorilla Tag: No Shadows, Cap FPS 60, Low XR Scale"), main);
            Assert.True(lines.Any(l => l.Contains("Low Physics Rate") && l.Contains("CRIX never changes physics")), string.Join(" | ", lines));
            Assert.True(lines.Any(l => l.Contains("Low XR Scale") && l.Contains("MaxEyeMegapixels")), "points to CRIX's eye cap");
        }

        [Test]
        public void BuiltInPipelineAndEmptyAndFailingReader()
        {
            var lines = OptimizerModsSupport.Describe(k => k == "NoShadows", urp: false, iHateAliens: false, potassium: false);
            Assert.True(lines[0].Contains("in effect: No Shadows"), "without URP the built-in settings work: " + lines[0]);
            Assert.True(OptimizerModsSupport.Describe(k => false, true, false, false)[0].Contains("no toggles switched on"));
            Assert.True(OptimizerModsSupport.Describe(k => throw new InvalidOperationException(), true, false, false)[0].Contains("no toggles switched on"),
                        "a failing read counts as off");
            Assert.Equal(0, OptimizerModsSupport.Describe(null, true, false, false).Count, "not installed: nothing");
        }

        [Test]
        public void IHateAliensAndPotassiumNotes()
        {
            var lines = OptimizerModsSupport.Describe(null, true, iHateAliens: true, potassium: true);
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].StartsWith("I HATE ALIENS") && lines[0].Contains("does not overlap"));
            Assert.True(lines[1].StartsWith("Potassium") && lines[1].Contains("zone.xenon.potassium.cfg") && lines[1].Contains("Maximum detailed player count"));
        }
    }
}

namespace CRIXOptimizer.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CRIXOptimizer.Diagnostics;

    public sealed class PerformanceNotesTests
    {
        private static void Play(PerformanceNotes n, ref double now, double seconds, double fps, Func<int, float> hitch = null)
        {
            double end = now + seconds;
            int f = 0;
            while (now < end)
            {
                float dt = hitch != null && hitch(f) > 0 ? hitch(f) / 1000f : (float)(1.0 / fps);
                n.AddFrame(dt, now);
                now += dt;
                f++;
            }
        }

        [Test]
        public void SummariesPerZoneLobbyAndCaptureAndATimeline()
        {
            var n = new PerformanceNotes();
            n.SetRefreshRate(120);
            double now = 100;
            Play(n, ref now, PerformanceNotes.StartupMinSeconds + 1, 60); // loading minute, kept apart
            n.SetZone("forest");
            n.SetLobby(0);
            Play(n, ref now, 60, 110);
            n.Tick(now, new PerformanceNotes.Snapshot { Profile = "Balanced", HeapBytes = 400L << 20, UnityBytes = 2600L << 20, EyeScale = 0.95f, VrActive = true });
            n.SetZone("city+forest");
            n.SetLobby(12);
            n.AddEvent(now, "zone forest -> city+forest");
            n.SetCapture("OBS/Streamlabs");
            Play(n, ref now, 120, 80, f => f == 500 ? 150f : 0f);
            n.AddBigHitch(now, 150f, "the zone changed");
            n.Tick(now, new PerformanceNotes.Snapshot { Profile = "OBSStreaming", VrActive = true, HeapBytes = -1, UnityBytes = -1 });

            var zones = n.Zones.Where(z => z.Frames.Count > 0).ToDictionary(z => z.Key);
            Assert.Near(110, zones["forest"].Frames.AverageFps, 1.0);
            Assert.Near(80, zones["city+forest"].Frames.AverageFps, 1.0);
            Assert.Equal(1, zones["city+forest"].BigHitches);
            var lobbies = n.Lobbies.Where(l => l.Frames.Count > 0).Select(l => l.Key).ToList();
            Assert.True(lobbies.Contains("not in a room") && lobbies.Contains("10-14 players"), string.Join(",", lobbies));

            string text = n.Build(new[] { "header line" }, now);
            Assert.True(text.Contains("header line") && text.Contains("== Per zone (map) =="), "sections");
            Assert.True(text.Contains("city+forest: 2.0 min, 79."), text.Substring(text.IndexOf("== Per zone", StringComparison.Ordinal), 200));
            Assert.True(text.Contains("OBS/Streamlabs:") && text.Contains("not recording or streaming:"), "per capture state");
            Assert.True(text.Contains("150 ms: the zone changed (zone city+forest, 10-14 players)"), "worst hitches");
            Assert.True(text.Contains("0:02:01 zone forest -> city+forest"), "events are time-stamped from the start");
            Assert.Equal(2, n.TimelineCount);
            Assert.True(text.Contains("| OBSStreaming, adaptive 0") && text.Contains("heap n/a"), "timeline line content");
        }

        [Test]
        public void StartupLoadingIsKeptApartAndExtendedWhileItHitches()
        {
            var n = new PerformanceNotes();
            double now = 0;
            n.SetZone("forest");
            Assert.True(n.InStartup);
            Play(n, ref now, 50, 90);
            n.AddFrame(0.9f, now); // Sakuraa loading at about 50 s: the long frame itself...
            now += 0.9;
            n.AddBigHitch(now, 900f, "a garbage collection ran in that frame"); // ...and the runtime's report of it
            Play(n, ref now, 15, 90);
            Assert.True(n.InStartup, "a big hitch near the end of the first minute keeps loading going for 20 s");
            Play(n, ref now, 10, 90);
            Assert.False(n.InStartup, "loading ends after 20 quiet seconds");
            Play(n, ref now, 60, 100);
            n.AddFrame(0.12f, now);
            now += 0.12;
            n.AddBigHitch(now, 120f, "no cause CRIX can see");
            string text = n.Build(new string[0], now);
            Assert.True(text.Contains("== Startup (game and mods loading) ==") && text.Contains("1 big hitches"), text.Substring(0, 400));
            Assert.True(text.Contains("[loading] hitch 900 ms"), "loading hitches are marked");
            Assert.True(text.Contains("startup finished: game and mods loaded (1 big hitches while loading)"), "end of loading is an event");
            int worst = text.IndexOf("== Worst hitches ==", StringComparison.Ordinal);
            string section = text.Substring(worst, text.IndexOf("== Events ==", StringComparison.Ordinal) - worst);
            Assert.False(section.Contains("900 ms"), "loading hitches are not in the worst list");
            Assert.True(section.Contains("120 ms"), "later hitches are");
            var forest = n.Zones.First(z => z.Key == "forest");
            Assert.Near(100, forest.Frames.AverageFps, 1.5, "the map summary starts after loading");

            var capped = new PerformanceNotes();
            double t = 0;
            for (int i = 0; i < 40; i++)
            {
                Play(capped, ref t, 10, 60);
                capped.AddBigHitch(t, 200f, "loading");
            }
            Assert.False(capped.InStartup, "loading counts for at most 5 minutes");
        }

        [Test]
        public void HistogramPercentilesAndLobbyBuckets()
        {
            var h = new PerformanceNotes.FrameHistogram();
            for (int i = 0; i < 990; i++)
                h.Add(10f);
            for (int i = 0; i < 10; i++)
                h.Add(40f);
            Assert.Near(10.25, h.Percentile(0.99), 0.01, "99th percentile in the 10 ms bucket");
            Assert.Near(40.25, h.Percentile(0.995), 0.01);
            h.Add(400f);
            Assert.Equal(400f, h.MaxMs);
            Assert.Equal(400.0, h.Percentile(1.0), "overflow bucket reports the worst frame");
            Assert.Equal("lobby size unknown", PerformanceNotes.LobbyBucket(-1));
            Assert.Equal("not in a room", PerformanceNotes.LobbyBucket(0));
            Assert.Equal("1-4 players", PerformanceNotes.LobbyBucket(4));
            Assert.Equal("5-9 players", PerformanceNotes.LobbyBucket(5));
            Assert.Equal("15-20 players", PerformanceNotes.LobbyBucket(20));
        }

        [Test]
        public void BoundedEventsAndWorstHitches()
        {
            var n = new PerformanceNotes();
            n.AddFrame(0.01f, 0);
            n.AddFrame(0.01f, PerformanceNotes.StartupMinSeconds + 1); // past the loading minute
            for (int i = 0; i < PerformanceNotes.MaxEvents + 5; i++)
                n.AddEvent(i, "e" + i);
            Assert.Equal(PerformanceNotes.MaxEvents, n.EventCount);
            for (int i = 0; i < 15; i++)
                n.AddBigHitch(i, 80 + i * 10, "cause " + i);
            string text = n.Build(new string[0], 20);
            Assert.True(text.Contains("later events not recorded"), "says that events were dropped");
            int worst = text.IndexOf("== Worst hitches ==", StringComparison.Ordinal);
            string section = text.Substring(worst, text.IndexOf("== Events ==", StringComparison.Ordinal) - worst);
            Assert.Equal(10, section.Split('\n').Count(l => l.Contains(" ms: cause ")), "ten worst kept");
            Assert.True(section.IndexOf("220 ms", StringComparison.Ordinal) < section.IndexOf("210 ms", StringComparison.Ordinal), "worst first");
        }

        [Test]
        public void GameProbeReadsZonesAndPlayersByName()
        {
            var probe = new GameContextProbe(Utilities.GameTypes.FromLoadedAssemblies());
            Assert.True(probe.CanReadZones && probe.CanReadPlayers, "members found by name");
            ZoneManagement.instance = null;
            NetworkSystem.Instance = null;
            Assert.Equal("unknown", probe.ReadZones());
            Assert.Equal(-1, probe.ReadPlayers());
            ZoneManagement.instance = new ZoneManagement();
            Assert.Equal("none", probe.ReadZones());
            ZoneManagement.instance.activeZones.Add(GTZone.forest);
            ZoneManagement.instance.activeZones.Add(GTZone.city);
            Assert.Equal("city+forest", probe.ReadZones(), "stable order");
            NetworkSystem.Instance = new NetworkSystem { InRoom = false, RoomPlayerCount = 9 };
            Assert.Equal(0, probe.ReadPlayers(), "not in a room");
            NetworkSystem.Instance.InRoom = true;
            Assert.Equal(9, probe.ReadPlayers());
            ZoneManagement.instance = null;
            NetworkSystem.Instance = null;

            var missing = new GameContextProbe(Utilities.GameTypes.FromAssemblies(new[] { typeof(PerformanceNotesTests).Assembly }));
            Assert.False(missing.CanReadZones || missing.CanReadPlayers);
            Assert.Equal("unknown", missing.ReadZones());
            Assert.Equal(-1, missing.ReadPlayers());
        }
    }
}

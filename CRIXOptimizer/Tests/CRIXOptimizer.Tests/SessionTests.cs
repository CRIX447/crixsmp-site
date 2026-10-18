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

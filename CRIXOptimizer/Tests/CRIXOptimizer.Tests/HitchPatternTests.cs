using System;
using System.Linq;
using CRIXOptimizer.Diagnostics;

namespace CRIXOptimizer.Tests
{
    public sealed class RepeatingHitchTests
    {
        /// <summary>Random "other" hitches at about the rate of a real session (one every 2.5 s on average).</summary>
        private static void AddNoise(RepeatingHitchDetector d, Random r, double from, double to, System.Collections.Generic.List<(double t, float ms)> into)
        {
            double t = from;
            while (true)
            {
                t += -Math.Log(1 - r.NextDouble()) * 2.5;
                if (t >= to)
                    break;
                into.Add((t, 75f + (float)r.NextDouble() * 60f));
            }
        }

        private static RepeatingHitchDetector Feed(System.Collections.Generic.List<(double t, float ms)> hitches)
        {
            var d = new RepeatingHitchDetector();
            foreach (var h in hitches.OrderBy(h => h.t))
                d.Add(h.t, h.ms);
            d.Update();
            return d;
        }

        [Test]
        public void FindsAFifteenSecondTimerAmongRandomHitches()
        {
            // Like the user's session: about 90 ms every 15 s from 9 s on, one round missing, plus random hitches.
            var r = new Random(7);
            var hitches = new System.Collections.Generic.List<(double t, float ms)>();
            for (int k = 0; k < 92; k++)
            {
                if (k == 15)
                    continue;
                hitches.Add((9 + 15.0 * k + (r.NextDouble() - 0.5) * 0.06, 80f + (float)r.NextDouble() * 25f));
            }
            AddNoise(null, r, 0, 1380, hitches);
            RepeatingHitchDetector d = Feed(hitches);
            var p = d.Current;
            Assert.True(p != null, "the timer is found");
            Assert.Near(15.0, p.Period, 0.05, "its period");
            Assert.True(p.Matched >= 90 && p.Slots >= 91 && p.Coverage > 0.95, $"{p.Matched} of {p.Slots}");
            Assert.True(p.TypicalMs >= 80 && p.TypicalMs <= 105, "typical length " + p.TypicalMs);
            Assert.True(d.Matches(p.Last + 15.0 + 0.05), "the next round matches");
            Assert.False(d.Matches(p.Last + 7.5), "half way between rounds does not");
        }

        [Test]
        public void RandomHitchesAreNoPattern()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var r = new Random(seed);
                var hitches = new System.Collections.Generic.List<(double t, float ms)>();
                AddNoise(null, r, 0, 1800, hitches);
                // Heavier bursts too, like loading: one big hitch a second for a minute.
                for (int i = 0; i < 60; i++)
                    hitches.Add((60 + i + r.NextDouble(), 300f));
                RepeatingHitchDetector d = Feed(hitches);
                Assert.True(d.Current == null, $"seed {seed}: " + (d.Current == null ? "" : $"{d.Current.Period:0.00} s, {d.Current.Matched} of {d.Current.Slots}"));
            }
        }

        [Test]
        public void ACoroutineThatDriftsAFrameARoundStillMatches()
        {
            // A coroutine waiting 15 s starts its next wait one (hitch) frame later each round.
            var hitches = new System.Collections.Generic.List<(double t, float ms)>();
            double t = 30;
            for (int k = 0; k < 40; k++)
            {
                hitches.Add((t, 90f));
                t += 15 + 0.09 + 0.008;
            }
            RepeatingHitchDetector d = Feed(hitches);
            Assert.True(d.Current != null && d.Current.Matched == 40, d.Current == null ? "null" : d.Current.Matched.ToString());
            Assert.Near(15.098, d.Current.Period, 0.01);
        }

        [Test]
        public void PrefersTheRealPeriodOverAMultiple()
        {
            var hitches = new System.Collections.Generic.List<(double t, float ms)>();
            for (int k = 0; k < 30; k++)
                hitches.Add((5 + 10.0 * k, 85f));
            RepeatingHitchDetector d = Feed(hitches);
            Assert.Near(10.0, d.Current.Period, 0.02, "10 s, not 20 or 30");
            Assert.True(new RepeatingHitchDetector().Current == null && !new RepeatingHitchDetector().Matches(10), "empty");
        }

        [Test]
        public void NotesShowThePatternAndItNoLongerHoldsUpLoading()
        {
            var n = new PerformanceNotes();
            n.SetRefreshRate(120);
            n.SetZone("forest");
            double now = 0;
            int nextHitch = 9;
            var d = new RepeatingHitchDetector();
            while (now < 200)
            {
                if (now >= nextHitch)
                {
                    n.AddFrame(0.09f, now);
                    now += 0.09;
                    d.Add(now, 90f, "Update.ScriptRunBehaviourUpdate");
                    n.AddBigHitch(now, 90f, "no cause CRIX can see");
                    nextHitch += 15;
                    continue;
                }
                n.AddFrame(1f / 110, now);
                now += 1.0 / 110;
            }
            Assert.False(n.InStartup, "90 ms hitches every 15 s do not keep loading going past the first minute");
            d.Update();
            n.SetRepeating(d.Current);
            string text = n.Build(new string[0], now);
            int at = text.IndexOf("== Repeating hitches ==", StringComparison.Ordinal);
            Assert.True(at > 0, "section");
            string section = text.Substring(at, text.IndexOf("== Events ==", StringComparison.Ordinal) - at);
            Assert.True(section.Contains("Every 15.0 s: 13 of 13 times from 0:00:09 to 0:03:09, typically 90 ms"), section);
            Assert.True(section.Contains("Most of the frame went to scripts' Update (the game or mods) in 13 of 13 measured") &&
                        section.Contains("code of the game or a mod"), section);
            Assert.True(text.Contains("startup finished") && text.IndexOf("0:01:00 startup finished", StringComparison.Ordinal) > 0, "loading ends at 1:00");

            Assert.True(new PerformanceNotes().Build(new string[0], 0).Contains("none: no big hitch comes back on a fixed timer"), "no pattern");
        }
    }

    public sealed class FramePhaseTimerTests
    {
        [Test]
        public void SplitsAFrameAndNamesTheLongestPart()
        {
            var t = new FramePhaseTimer(1000.0); // 1 tick = 1 ms
            int start = t.Segment("TimeUpdate (other)");
            int update = t.Segment("Update.ScriptRunBehaviourUpdate");
            int fixedScripts = t.Segment("FixedUpdate.ScriptRunBehaviourFixedUpdate");
            int other = t.Segment("Update (other)");
            int wait = t.Segment("TimeUpdate.WaitForLastPresentationAndUpdateTime");
            Assert.Equal(update, t.Segment("Update.ScriptRunBehaviourUpdate"), "ids are reused");

            t.Mark(start, true, 0);
            t.Mark(fixedScripts, false, 1); t.Mark(other, false, 3);
            t.Mark(fixedScripts, false, 4); t.Mark(other, false, 6); // FixedUpdate twice: 2 + 2 ms
            t.Mark(update, false, 7);
            t.Mark(other, false, 90);  // 83 ms in scripts' Update
            t.Mark(wait, false, 92);
            Assert.False(t.Largest(100, out _, out _), "no frame completed yet");
            t.Mark(start, true, 100);
            Assert.Equal(1L, t.FramesCompleted);
            Assert.Near(100, t.LastFrameMs, 1e-9);
            Assert.True(t.Largest(98, out string seg, out double ms) && seg == "Update.ScriptRunBehaviourUpdate", seg);
            Assert.Near(83, ms, 1e-9);
            Assert.False(t.Largest(300, out _, out _), "a different frame than the measured hitch is not explained");
            Assert.Equal("scripts' Update (the game or mods)", FramePhaseTimer.Describe(seg));
            Assert.Equal("other Unity work in PreLateUpdate", FramePhaseTimer.Describe("PreLateUpdate (other)"));
            Assert.True(FramePhaseTimer.IsScripts(seg) && !FramePhaseTimer.IsWaitOrRender(seg), "scripts");
            Assert.True(FramePhaseTimer.IsWaitOrRender("TimeUpdate.WaitForLastPresentationAndUpdateTime"), "waiting");
            foreach (string m in FramePhaseTimer.Measured)
                Assert.False(FramePhaseTimer.Describe(m) == m, "plain words for " + m);
        }
    }
}

namespace CRIXOptimizer.Tests
{
    public sealed class SteadyThrottleTests
    {
        [Test]
        public void TenFpsHeldEvenlyIsAThrottleAndUnevenSlowFramesAreNot()
        {
            var d = new SteadyThrottleDetector();
            var r = new Random(5);
            double now = 100;
            int started = 0, ended = 0;
            for (int i = 0; i < 90; i++)
            {
                float ms = 100f + (float)(r.NextDouble() - 0.5) * 6f; // like the session: 97-103 ms
                now += ms / 1000.0;
                int c = d.AddFrame(ms, now);
                if (c > 0) { started++; Assert.Equal(SteadyThrottleDetector.ConfirmFrames - 1, i, "recognised after 20 frames"); }
            }
            Assert.True(d.Throttled && started == 1, "throttled");
            Assert.Near(100, d.FrameMs, 1.5);
            Assert.Near(100.0, d.RunStart, 0.01, "from the start of the first slow frame");
            now += 0.011;
            ended = d.AddFrame(11f, now);
            Assert.True(ended < 0 && !d.Throttled, "a normal frame ends it");
            Assert.Equal(90, d.EndedFrames);
            Assert.Near(9.0, d.EndedSeconds, 0.2);

            // Real slow loading frames vary far more than 4 %, and reprojection rates are faster than 66 ms.
            var load = new SteadyThrottleDetector();
            for (int i = 0; i < 200; i++)
                Assert.False(load.AddFrame(80f + (float)r.NextDouble() * 60f, i) > 0, "uneven slow frames");
            var reproj = new SteadyThrottleDetector();
            for (int i = 0; i < 200; i++)
                Assert.False(reproj.AddFrame(33.3f, i) > 0, "30 FPS reprojection");
        }

        [Test]
        public void NotesTakeTheThrottledHitchesBackAndLeaveItsFramesOut()
        {
            var n = new PerformanceNotes();
            n.SetRefreshRate(120);
            n.SetZone("city");
            n.SetLobby(2);
            var d = new SteadyThrottleDetector();
            double now = 0;
            while (now < 70)
            {
                n.AddFrame(1f / 110, now);
                now += 1.0 / 110;
            }
            n.AddFrame(0.25f, now);
            now += 0.25;
            n.AddBigHitch(now, 250f, "the zone changed"); // a real hitch before the throttle stays
            n.AddFrame(1f / 110, now);
            now += 1.0 / 110;
            for (int i = 0; i < 80; i++)
            {
                now += 0.1;
                int c = d.AddFrame(100f, now);
                if (c > 0)
                    n.ThrottleStarted(d.RunStart, d.FrameMs);
                n.AddFrame(0.1f, now);
                if (!d.Throttled)
                    n.AddBigHitch(now, 100f, "no cause CRIX can see");
                if (i == 5)
                    n.AddEvent(now, "a player left (1 players)"); // non-hitch events inside the run stay
            }
            now += 0.009;
            Assert.True(d.AddFrame(9f, now) < 0, "ended");
            n.ThrottleEnded(now, d.EndedSeconds);
            n.AddFrame(0.009f, now);
            string text = n.Build(new string[0], now);
            Assert.False(text.Contains("100 ms: no cause"), "the throttled frames are no hitches");
            Assert.True(text.Contains("250 ms: the zone changed"), "the earlier hitch stays");
            Assert.True(text.Contains("frames held at a steady 10.0 FPS: a throttle, not load") && text.Contains("a player left (1 players)"), "events");
            Assert.True(text.Contains("the steady-rate throttle ended after 8 s (61 more frames left out)"), text.Substring(text.IndexOf("== Events", StringComparison.Ordinal)).Replace("\n", " | "));
            int held = text.IndexOf("frames held at", StringComparison.Ordinal), left = text.IndexOf("a player left", StringComparison.Ordinal);
            Assert.True(held < left, "the throttle event is placed at its start time, before the events inside it");
            var city = n.Zones.First(z => z.Key == "city");
            Assert.True(city.Frames.AverageFps > 60, "the map's frame rate is not pulled down by the throttle: " + city.Frames.AverageFps);
        }
    }
}

namespace CRIXOptimizer.Tests
{
    public sealed class ModCodeTimingTests
    {
        [Test]
        public void StatsNameTheSlowMethodOfAFrameAndATimer()
        {
            var s = new ModCodeStats();
            int menu = s.Register("OculusReportMenu", "Menu.Update");
            int rig = s.Register("GorillaRigUtil", "RigHandler.LateUpdate");
            double now = 0;
            for (int frame = 1; frame <= 120 * 300; frame++)
            {
                now += 1.0 / 120;
                s.Record(rig, 0.05, frame, now);
                bool timer = frame % (120 * 15) == 0; // every 15 s
                s.Record(menu, timer ? 75 + frame % 7 : 0.02, frame, now);
                if (frame == 120 * 100)
                    s.Record(rig, 30, frame, now); // one slow call, not on a timer
            }
            Assert.True(s.Slowest(120 * 15 * 3, out string label, out double ms) && label == "OculusReportMenu: Menu.Update" && ms >= 75, label);
            Assert.False(s.Slowest(17, out _, out _), "nothing slow in an ordinary frame");
            ModCodeStats.Method onTimer = s.OnTimer(15.0, 4);
            Assert.True(onTimer != null && onTimer.Label == "OculusReportMenu: Menu.Update", onTimer?.Label ?? "null");
            Assert.True(s.OnTimer(7.3, 4) == null, "a different period names nothing");
            var lines = s.Describe(10);
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].StartsWith("OculusReportMenu: Menu.Update: 20 slow calls (20+ ms)") && lines[0].Contains(", every 15.0 s;"), lines[0]);
            Assert.True(lines[1].StartsWith("GorillaRigUtil: RigHandler.LateUpdate: 1 slow calls") && !lines[1].Contains("every"), lines[1]);
        }

        public static int TargetCalls;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int Target(int x)
        {
            TargetCalls++;
            return x + 1;
        }

        public static int OtherModPostfixRuns;

        public static void OtherModPostfix(ref int __result)
        {
            OtherModPostfixRuns++;
            __result *= 10;
        }

        [Test]
        public void WrapsOtherModsPatchesWithoutChangingThemAndRemovesTheWrappers()
        {
            var otherMod = new HarmonyLib.Harmony("tests.othermod");
            var target = typeof(ModCodeTimingTests).GetMethod(nameof(Target));
            otherMod.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(ModCodeTimingTests).GetMethod(nameof(OtherModPostfix))));
            var timer = new ModCodeTimer();
            try
            {
                Assert.Equal(4 * 10, Target(3), "the other mod's patch works");
                int wrapped = timer.Install("CRIXOptimizer", null);
                Assert.True(wrapped >= 1 && timer.Failed == 0, $"wrapped {wrapped}, failed {timer.Failed}");
                var entry = timer.Stats.Methods.First(m => m.Mod == "tests.othermod");
                Assert.Equal("patch on ModCodeTimingTests.Target (OtherModPostfix)", entry.Name);
                Assert.Equal(0, timer.Install("CRIXOptimizer", null), "a second pass adds nothing new");
                int runs = OtherModPostfixRuns;
                Assert.Equal(6 * 10, Target(5), "results unchanged while timed");
                Assert.Equal(runs + 1, OtherModPostfixRuns, "the other mod's postfix still runs once");
                Assert.Equal(1L, entry.Calls, "the call was timed");
            }
            finally
            {
                timer.Dispose();
            }
            var info = HarmonyLib.Harmony.GetPatchInfo(typeof(ModCodeTimingTests).GetMethod(nameof(OtherModPostfix)));
            Assert.True(info == null || !info.Owners.Contains(ModCodeTimer.HarmonyId), "wrappers removed");
            Assert.Equal(8 * 10, Target(7), "the other mod's patch still works after");
            otherMod.UnpatchSelf();
        }

        [Test]
        public void NotesSayWhetherModCodeWasTimedAndNameTheCulprit()
        {
            var n = new PerformanceNotes();
            Assert.True(n.Build(new string[0], 0).Contains("not timed ([Diagnostics] ModCodeTiming = false)"), "off");
            n.SetModCode(new string[0]);
            Assert.True(n.Build(new string[0], 0).Contains("most likely the game's own code"), "timed, nothing slow");
            n.SetModCode(new[] { "OculusReportMenu: Menu.Update: 20 slow calls" });
            var d = new RepeatingHitchDetector();
            for (int k = 0; k < 12; k++)
                d.Add(9 + 15 * k, 80f, "Update.ScriptRunBehaviourUpdate");
            d.Update();
            n.SetRepeating(d.Current, "OculusReportMenu: Menu.Update (12 slow calls on the same timer)");
            string text = n.Build(new string[0], 200);
            Assert.True(text.Contains("CRIX's timing of mods' code names it: OculusReportMenu: Menu.Update (12 slow calls on the same timer)"), text);
            Assert.True(text.Contains("== Mods' code ==\nOculusReportMenu: Menu.Update: 20 slow calls"), "section");
        }
    }
}

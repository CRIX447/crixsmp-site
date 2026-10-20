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

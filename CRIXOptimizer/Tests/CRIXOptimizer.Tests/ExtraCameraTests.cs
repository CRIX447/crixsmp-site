using System;
using System.Linq;
using CRIXOptimizer.Core;
using CRIXOptimizer.GPU;
using UnityEngine.LowLevel;
using PostLate = UnityEngine.PlayerLoop.PostLateUpdate;

namespace CRIXOptimizer.Tests
{
    public sealed class RenderRateScheduleTests
    {
        /// <summary>
        /// Runs the schedule like the limiter does: each frame the frame that just passed and SmoothFrameTime go into
        /// Plan; cameras get even phases. <paramref name="frameTime"/> gives each frame's duration;
        /// <paramref name="ownerOff"/> says whether a camera's owner has it switched off in a frame.
        /// Returns per camera the render times, and per frame how many cameras rendered.
        /// </summary>
        private static (System.Collections.Generic.List<double>[] renders, int[] perFrame) Run(
            int cameras, double rate, int frames, Func<int, double> frameTime, Func<int, int, bool> ownerOff = null)
        {
            double period = 1.0 / rate, now = 100.0, typical = 0, lastDt = 0;
            var due = new double[cameras];
            var phase = new double[cameras];
            var eligible = new bool[cameras];
            var render = new bool[cameras];
            var renders = new System.Collections.Generic.List<double>[cameras];
            for (int k = 0; k < cameras; k++)
            {
                phase[k] = RenderRateSchedule.EvenPhase(k, cameras);
                due[k] = RenderRateSchedule.InitialDue(now, period, phase[k]);
                renders[k] = new System.Collections.Generic.List<double>();
            }
            var perFrame = new int[frames];
            for (int f = 0; f < frames; f++)
            {
                for (int k = 0; k < cameras; k++)
                    eligible[k] = ownerOff == null || !ownerOff(f, k);
                perFrame[f] = RenderRateSchedule.Plan(due, phase, eligible, render, cameras, now, period, lastDt, typical);
                typical = RenderRateSchedule.SmoothFrameTime(typical, lastDt); // like the limiter: after planning
                for (int k = 0; k < cameras; k++)
                {
                    if (render[k])
                    {
                        Assert.True(eligible[k], "a camera its owner switched off is never planned");
                        renders[k].Add(now);
                    }
                }
                lastDt = frameTime(f);
                now += lastDt;
            }
            return (renders, perFrame);
        }

        private static int MaxInAnyWindow(System.Collections.Generic.List<double> times, double window)
        {
            int best = 0;
            for (int i = 0, j = 0; i < times.Count; i++)
            {
                while (times[i] - times[j] >= window)
                    j++;
                best = Math.Max(best, i - j + 1);
            }
            return best;
        }

        private static Func<int, double> Steady(double fps) => f => 1.0 / fps;

        /// <summary>Normal frame-time noise around a nominal rate (Box-Muller, fixed seed).</summary>
        private static Func<int, double> Jittered(double fps, double sigmaSeconds, int seed)
        {
            var rng = new Random(seed);
            return f =>
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                return Math.Max(0.001, 1.0 / fps + gauss * sigmaSeconds);
            };
        }

        /// <summary>The spectator's even-frames cadence as the limiter runs it: N from the typical frame time before this
        /// frame (snapped to the headset's), decision by frame count. Returns the gaps (in frames) between renders.</summary>
        private static System.Collections.Generic.Dictionary<int, int> EvenFrameGaps(double limit, double refreshHz, int frames, Func<int, double> frameTime, out int renders)
        {
            var gaps = new System.Collections.Generic.Dictionary<int, int>();
            var cadence = new RenderRateSchedule.Cadence();
            double typical = 0;
            int last = -1;
            renders = 0;
            for (int f = 0; f < frames; f++)
            {
                double typicalBefore = typical;
                typical = RenderRateSchedule.SmoothFrameTime(typical, frameTime(f));
                int n = RenderRateSchedule.UpdateCadence(ref cadence, 1.0 / limit, typicalBefore, refreshHz > 0 ? 1.0 / refreshHz : 0);
                if (!RenderRateSchedule.EveryNthDue(f, last, n, forced: f == 0))
                    continue;
                if (last >= 0 && f > 300)
                {
                    gaps.TryGetValue(f - last, out int c);
                    gaps[f - last] = c + 1;
                }
                last = f;
                renders++;
            }
            return gaps;
        }

        private static string Show(System.Collections.Generic.Dictionary<int, int> gaps) =>
            string.Join(", ", gaps.OrderBy(g => g.Key).Select(g => g.Key + " frames x" + g.Value));

        [Test]
        public void SpectatorEvenFramesCadenceIsSteadyAndWithinTheLimit()
        {
            Assert.Equal(2, RenderRateSchedule.EveryNthFrame(1.0 / 60, 1.0 / 120), "120 Hz, 60/s: every 2nd frame");
            Assert.Equal(2, RenderRateSchedule.EveryNthFrame(1.0 / 60, 1.0 / 90), "90 Hz: every 2nd (45/s, never above 60)");
            Assert.Equal(3, RenderRateSchedule.EveryNthFrame(1.0 / 60, 1.0 / 144), "144 Hz: every 3rd (48/s)");
            Assert.Equal(1, RenderRateSchedule.EveryNthFrame(1.0 / 60, 1.0 / 60), "game at the limit: every frame");
            Assert.Equal(1, RenderRateSchedule.EveryNthFrame(1.0 / 60, 1.0 / 45), "slower game: every frame");
            Assert.Equal(1, RenderRateSchedule.EveryNthFrame(1.0 / 60, 0), "unknown frame time: every frame");
            Assert.Near(1.0 / 60, RenderRateSchedule.CadenceFrameTime(0.0161, 1.0 / 120), 1e-9, "half rate under reprojection snaps to 2 frames");
            Assert.Near(0.02, RenderRateSchedule.CadenceFrameTime(0.02, 1.0 / 120), 1e-9, "50 fps is no multiple of 120 Hz: measured");

            // 120 Hz headset with 1.5 ms of frame-time noise, 100 s: every gap exactly 2 frames, 60/s.
            var gaps = EvenFrameGaps(60, 120, 12000, Jittered(120, 0.0015, 11), out int renders);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "120 Hz: " + Show(gaps));
            Assert.True(Math.Abs(renders - 6000) <= 1, "renders " + renders);
            // Without the refresh rate known the same noise still keeps 2 frames almost always (hysteresis).
            gaps = EvenFrameGaps(60, 0, 12000, Jittered(120, 0.0015, 12), out _);
            int total = gaps.Values.Sum();
            Assert.True(gaps.TryGetValue(2, out int twos) && twos >= total * 0.99, "120 Hz, refresh unknown: " + Show(gaps));
            // 90 Hz: 45/s evenly; 144 Hz: 48/s evenly; never two renders on consecutive frames.
            gaps = EvenFrameGaps(60, 90, 9000, Jittered(90, 0.0015, 13), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "90 Hz: " + Show(gaps));
            gaps = EvenFrameGaps(60, 144, 14400, Jittered(144, 0.001, 14), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(3), "144 Hz: " + Show(gaps));
            // Reprojection: the game runs at 60 on a 120 Hz headset -> the spectator renders every frame.
            gaps = EvenFrameGaps(60, 120, 6000, Jittered(60, 0.0015, 15), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(1), "60 fps under reprojection: " + Show(gaps));
            // A struggling game between the two (61-70 fps, not locked to half rate): at most about 5 % above the limit
            // (61 fps: every frame), clearly faster: every 2nd frame.
            foreach (double fps in new[] { 61.0, 65.0, 70.0 })
            {
                gaps = EvenFrameGaps(60, 120, (int)(fps * 60), Jittered(fps, 0.0005, 16), out int r);
                Assert.True(r <= 60 * 60 * 1.05 + 1, fps + " fps: " + r + " renders in 60 s");
                if (fps >= 65)
                    Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), fps + " fps on 120 Hz: " + Show(gaps));
            }
            // Mixed 1- and 2-vsync frames averaging about 70 fps on 120 Hz: still every 2nd frame.
            gaps = EvenFrameGaps(60, 120, 8400, f => f % 7 < 3 ? 1.0 / 60 : 1.0 / 120, out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "vsync mix: " + Show(gaps));
            // Odd limits never give more than about 5 % above the limit: 110/s at 120 Hz -> 60/s, 58/s -> 60/s (3 % above),
            // 55/s -> 40/s, 70/s at 144 Hz -> 72/s (3 % above), 65/s at 144 Hz -> 48/s.
            gaps = EvenFrameGaps(110, 120, 12000, Steady(120), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "110 at 120 Hz: " + Show(gaps));
            gaps = EvenFrameGaps(58, 120, 12000, Steady(120), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "58 at 120 Hz: " + Show(gaps));
            gaps = EvenFrameGaps(55, 120, 12000, Steady(120), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(3), "55 at 120 Hz: " + Show(gaps));
            gaps = EvenFrameGaps(70, 144, 14400, Steady(144), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(2), "70 at 144 Hz: " + Show(gaps));
            gaps = EvenFrameGaps(65, 144, 14400, Steady(144), out _);
            Assert.True(gaps.Count == 1 && gaps.ContainsKey(3), "65 at 144 Hz: " + Show(gaps));

            // A short swing does not change N; a lasting change does, after PersistFrames frames.
            var c = new RenderRateSchedule.Cadence();
            Assert.Equal(2, RenderRateSchedule.UpdateCadence(ref c, 1.0 / 60, 1.0 / 120, 0));
            for (int i = 0; i < 10; i++)
                Assert.Equal(2, RenderRateSchedule.UpdateCadence(ref c, 1.0 / 60, 1.0 / 150, 0), "10 fast frames: still 2");
            Assert.Equal(2, RenderRateSchedule.UpdateCadence(ref c, 1.0 / 60, 1.0 / 120, 0));
            int final = 0;
            for (int i = 0; i <= RenderRateSchedule.Cadence.PersistFrames; i++)
                final = RenderRateSchedule.UpdateCadence(ref c, 1.0 / 60, 1.0 / 150, 0);
            Assert.Equal(3, final, "a lasting change to 150 fps moves to every 3rd frame");
            Assert.True(RenderRateSchedule.EveryNthDue(10, 9, 2, forced: true), "forced (new texture, resize) renders at once");
            Assert.False(RenderRateSchedule.EveryNthDue(10, 9, 2, forced: false));
            Assert.True(RenderRateSchedule.EveryNthDue(11, 9, 2, forced: false));
        }

        [Test]
        public void RendersAtMostTheLimitAndCloseToIt()
        {
            foreach (var c in new (double fps, double rate)[] { (90, 30), (120, 30), (90, 60), (144, 100), (72, 30), (120, 45), (144, 2) })
            {
                var r = Run(1, c.rate, (int)(c.fps * 10), Steady(c.fps)).renders[0];
                Assert.True(r.Count <= c.rate * 10 + 1, $"{c.fps} fps / {c.rate}: {r.Count} renders in 10 s");
                Assert.True(r.Count >= c.rate * 10 * 0.9, $"{c.fps} fps / {c.rate}: {r.Count} renders in 10 s (too few)");
                Assert.True(MaxInAnyWindow(r, 1.0) <= c.rate + 1, $"{c.fps} fps / {c.rate}: per-second peak {MaxInAnyWindow(r, 1.0)}");
            }
            // The old frame-interval maths rendered every frame here (90/60 rounds down to 1) and 9/s at 144 fps / 2.
            Assert.True(Run(1, 60, 900, Steady(90)).renders[0].Count <= 601, "60/s at 90 fps is limited");
            Assert.True(Run(1, 2, 1440, Steady(144)).renders[0].Count <= 21, "2/s at 144 fps is limited");
        }

        [Test]
        public void SlowerGameRendersEveryFrame()
        {
            var r = Run(1, 60, 450, Steady(45)).renders[0];
            Assert.True(r.Count >= 449, $"45 fps under a 60/s limit renders every frame ({r.Count})");
            var two = Run(2, 60, 450, Steady(45));
            Assert.True(two.renders.All(x => x.Count >= 449), "every camera, every frame");
        }

        [Test]
        public void IrregularFramesAndHitchesNeverBurst()
        {
            var rng = new Random(7);
            var r = Run(1, 30, 1800, f => f == 300 ? 1.5 : 0.005 + rng.NextDouble() * 0.012).renders[0];
            double seconds = r[r.Count - 1] - r[0];
            Assert.True(r.Count <= 30 * seconds + 2, $"{r.Count} renders in {seconds:0.0} s");
            Assert.True(MaxInAnyWindow(r, 1.0) <= 31, "no catch-up burst after a 1.5 s hitch");
            Assert.True(MaxInAnyWindow(r, 1.0 / 30) <= 2, $"at most 2 renders within one period ({MaxInAnyWindow(r, 1.0 / 30)})");
            int hitch = Enumerable.Range(1, r.Count - 1).First(i => r[i] - r[i - 1] > 1.0); // first render after the 1.5 s gap
            Assert.True(r[hitch + 1] - r[hitch] > 0.02, $"after the hitch: one render, then a new period ({r[hitch + 1] - r[hitch]:0.000} s)");
        }

        [Test]
        public void CamerasTakeTurnsWithJitterAndAfterAHitch()
        {
            // Three cameras at 30/s or two at 45/s on a 90 Hz headset: one render per frame is exactly what the rate
            // needs. With 0.3 ms frame-time noise, a 0.2 s hitch and one camera switched off by its owner for a while,
            // frames with two renders stay rare.
            foreach (var c in new (int cameras, double rate)[] { (3, 30), (2, 45) })
            {
                foreach (double fps in new[] { 90.0, 89.5 })
                {
                    var jitter = Jittered(fps, 0.0003, 11 + c.cameras); // one generator per case, so frame times really vary
                    var (renders, perFrame) = Run(c.cameras, c.rate, 2700,
                        f => f == 900 ? 0.2 : jitter(f), (f, k) => k == 1 && f >= 1500 && f < 1570);
                    int doubles = perFrame.Where((n, f) => f > 60 && n >= 2).Count();
                    Assert.True(doubles <= 2700 * 0.02, $"{c.cameras} x {c.rate}/s at {fps} fps: {doubles} frames with two renders");
                    Assert.True(perFrame.Max() <= 2, $"{c.cameras} x {c.rate}/s: {perFrame.Max()} renders in one frame");
                    for (int k = 0; k < c.cameras; k++)
                    {
                        double perSecond = renders[k].Count / (2700 / fps);
                        Assert.True(perSecond <= c.rate + 0.5 && perSecond >= c.rate * (k == 1 ? 0.85 : 0.9),
                            $"{c.cameras} x {c.rate}/s at {fps} fps: camera {k} rendered {perSecond:0.0}/s");
                    }
                }
            }
        }

        [Test]
        public void FrequentLongFramesKeepTheRate()
        {
            // 90 Hz where every 7th frame takes three vsyncs (the game at about 69 fps): the per-frame budget follows
            // the real frame time, so three cameras at 30/s still get close to 30 renders a second.
            var (renders, _) = Run(3, 30, 2100, f => f % 7 == 3 ? 3.0 / 90 : 1.0 / 90);
            double seconds = 2100 * (6 + 3) / 7.0 / 90;
            for (int k = 0; k < 3; k++)
            {
                double perSecond = renders[k].Count / seconds;
                Assert.True(perSecond >= 27 && perSecond <= 30.5, $"camera {k}: {perSecond:0.0}/s");
            }
        }

        [Test]
        public void NoBurstAfterALoadScreen()
        {
            // 15 frames of 200 ms (a map load), then 90 fps again: the typical frame time must not keep every camera
            // rendering every frame.
            var (renders, perFrame) = Run(3, 30, 400, f => f >= 100 && f < 115 ? 0.2 : 1.0 / 90);
            for (int f = 116; f < 140; f++)
                Assert.True(perFrame[f] <= 2, $"frame {f}: {perFrame[f]} cameras");
            double end = 100.0 + Enumerable.Range(0, 116).Sum(f => f >= 100 && f < 115 ? 0.2 : 1.0 / 90);
            foreach (var r in renders)
                Assert.True(r.Count(t => t >= end && t < end + 0.25) <= 30 * 0.25 + 2, "at most the limit right after the load");
        }

        [Test]
        public void TypicalFrameTimeBarelyMovesOnAHitchButFollowsASlowdown()
        {
            double typical = 0;
            for (int i = 0; i < 200; i++)
                typical = RenderRateSchedule.SmoothFrameTime(typical, 1.0 / 90);
            Assert.Near(1.0 / 90, typical, 1e-6);
            typical = RenderRateSchedule.SmoothFrameTime(typical, 0.6);
            Assert.True(typical <= 1.25 / 90, $"a single hitch frame moves it at most 20 % ({typical * 1000:0.0} ms)");
            for (int i = 0; i < 40; i++)
                typical = RenderRateSchedule.SmoothFrameTime(typical, 1.0 / 30);
            Assert.True(typical > 1.0 / 32, $"a lasting drop to 30 fps is followed ({1 / typical:0} fps)");
        }

        [Test]
        public void FrameBudget()
        {
            Assert.Equal(1, RenderRateSchedule.FrameBudget(3, 30, 1.0 / 90), "3 x 30/s at 90 fps: one per frame");
            Assert.Equal(1, RenderRateSchedule.FrameBudget(3, 30, 1.0 / 89.5), "jitter just above 1/90 does not add a slot");
            Assert.Equal(2, RenderRateSchedule.FrameBudget(4, 30, 1.0 / 90));
            Assert.Equal(1, RenderRateSchedule.FrameBudget(1, 30, 1.0 / 120));
            Assert.Equal(3, RenderRateSchedule.FrameBudget(3, 30, 3.0 / 90), "a three-vsync frame may render all three");
            Assert.Equal(3, RenderRateSchedule.FrameBudget(3, 60, 1.0 / 45), "never more than the cameras");
            Assert.Equal(0, RenderRateSchedule.FrameBudget(0, 30, 1.0 / 90));
            Assert.Equal(1, RenderRateSchedule.FrameBudget(5, 30, 0), "unknown frame time: one");
        }

        [Test]
        public void GridAndPhases()
        {
            Assert.Equal(0.0, RenderRateSchedule.EvenPhase(0, 3));
            Assert.Near(1.0 / 3, RenderRateSchedule.EvenPhase(1, 3), 1e-12);
            double p = 1.0 / 30;
            double t = RenderRateSchedule.NextOnGrid(100.0, p, 0.25, false);
            Assert.True(t >= 100.0 && t < 100.0 + p, "next grid point within one period");
            Assert.Near(0.25, (t / p) - Math.Floor(t / p), 1e-6, "on the camera's phase");
            double g = (Math.Floor(100.0 / p) + 0.25) * p;
            Assert.True(RenderRateSchedule.NextOnGrid(g, p, 0.25, true) > g, "strictly after");
        }
    }

    public sealed class PlayerLoopHookTests
    {
        private struct OtherModStep { }
        private struct Marker { }

        private static PlayerLoopSystem Sys(Type t, params PlayerLoopSystem[] subs) => new PlayerLoopSystem { type = t, subSystemList = subs.Length > 0 ? subs : null };

        private static PlayerLoopSystem Sample() =>
            Sys(null,
                Sys(typeof(UnityEngine.PlayerLoop.Update), Sys(typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate))),
                Sys(typeof(PostLate),
                    Sys(typeof(PostLate.UpdateAudio)),
                    Sys(typeof(PostLate.FinishFrameRendering)),
                    Sys(typeof(OtherModStep)),
                    Sys(typeof(PostLate.PlayerSendFrameComplete))));

        private static string Order(PlayerLoopSystem root) =>
            string.Join(",", root.subSystemList[1].subSystemList.Select(s => s.type.Name).ToArray());

        [Test]
        public void InsertsAroundFinishFrameRenderingAndRemovesOnlyItsOwn()
        {
            PlayerLoopSystem root = Sample();
            Assert.True(PlayerLoopHook.InsertBefore(ref root, typeof(PostLate.FinishFrameRendering), Sys(typeof(Marker))));
            Assert.True(PlayerLoopHook.InsertAfter(ref root, typeof(PostLate.FinishFrameRendering), Sys(typeof(Marker))));
            Assert.Equal("UpdateAudio,Marker,FinishFrameRendering,Marker,OtherModStep,PlayerSendFrameComplete", Order(root));
            Assert.True(PlayerLoopHook.Contains(root, typeof(Marker)));
            Assert.Equal(1, root.subSystemList[0].subSystemList.Length, "other branches untouched");

            Assert.Equal(2, PlayerLoopHook.Remove(ref root, typeof(Marker)));
            Assert.Equal("UpdateAudio,FinishFrameRendering,OtherModStep,PlayerSendFrameComplete", Order(root));
            Assert.False(PlayerLoopHook.Contains(root, typeof(Marker)));
            Assert.Equal(0, PlayerLoopHook.Remove(ref root, typeof(Marker)), "removing again changes nothing");
        }

        [Test]
        public void MissingTargetIsReported()
        {
            PlayerLoopSystem root = Sys(null, Sys(typeof(PostLate), Sys(typeof(PostLate.UpdateAudio))));
            Assert.False(PlayerLoopHook.InsertBefore(ref root, typeof(PostLate.FinishFrameRendering), Sys(typeof(Marker))));
            Assert.False(PlayerLoopHook.InsertAfter(ref root, typeof(PostLate.FinishFrameRendering), Sys(typeof(Marker))));
            Assert.False(PlayerLoopHook.Contains(root, typeof(Marker)));
        }
    }
}

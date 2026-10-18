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

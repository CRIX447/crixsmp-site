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
        /// <summary>Simulates <paramref name="seconds"/> of frames; returns the render times.</summary>
        private static System.Collections.Generic.List<double> Run(double fps, double rate, double seconds, double phase = 0.37, Func<int, double> frameTime = null, double typicalFrameTime = 0)
        {
            var renders = new System.Collections.Generic.List<double>();
            double period = 1.0 / rate;
            double now = 100.0;
            var due = new[] { RenderRateSchedule.InitialDue(now, period, phase) };
            var eligible = new[] { true };
            var render = new bool[1];
            // Like the limiter: the plan uses the typical (averaged) frame time, not the last frame's.
            double typical = typicalFrameTime > 0 ? typicalFrameTime : 1.0 / fps;
            for (int frame = 0; now < 100.0 + seconds; frame++)
            {
                if (RenderRateSchedule.Plan(due, eligible, render, 1, now, period, typical) == 1)
                    renders.Add(now);
                now += frameTime != null ? frameTime(frame) : 1.0 / fps;
            }
            return renders;
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

        [Test]
        public void RendersAtMostTheLimitAndCloseToIt()
        {
            foreach (var c in new (double fps, double rate)[] { (90, 30), (120, 30), (90, 60), (144, 100), (72, 30), (120, 45), (144, 2) })
            {
                var r = Run(c.fps, c.rate, 10);
                Assert.True(r.Count <= c.rate * 10 + 1, $"{c.fps} fps / {c.rate}: {r.Count} renders in 10 s");
                Assert.True(r.Count >= c.rate * 10 * 0.9, $"{c.fps} fps / {c.rate}: {r.Count} renders in 10 s (too few)");
                Assert.True(MaxInAnyWindow(r, 1.0) <= c.rate + 1, $"{c.fps} fps / {c.rate}: per-second peak {MaxInAnyWindow(r, 1.0)}");
            }
            // The old frame-interval maths rendered every frame here (90/60 rounds down to 1) and 9/s at 144 fps / 2.
            Assert.True(Run(90, 60, 10).Count <= 601, "60/s at 90 fps is limited");
            Assert.True(Run(144, 2, 10).Count <= 21, "2/s at 144 fps is limited");
        }

        [Test]
        public void SlowerGameRendersEveryFrame()
        {
            var r = Run(45, 60, 10);
            Assert.True(r.Count >= 449, $"45 fps under a 60/s limit renders every frame ({r.Count})");
        }

        [Test]
        public void IrregularFramesAndHitchesNeverBurst()
        {
            var rng = new Random(7);
            var r = Run(0, 30, 20, 0.5, f => f == 300 ? 1.5 : 0.005 + rng.NextDouble() * 0.012, typicalFrameTime: 0.011);
            Assert.True(r.Count <= 30 * 20 + 1, $"{r.Count} renders in 20 s");
            Assert.True(MaxInAnyWindow(r, 1.0) <= 31, "no catch-up burst after a 1.5 s hitch");
            Assert.True(MaxInAnyWindow(r, 1.0 / 30) <= 2, $"at most 2 renders within one period ({MaxInAnyWindow(r, 1.0 / 30)})");
            int hitch = Enumerable.Range(1, r.Count - 1).First(i => r[i] - r[i - 1] > 1.0); // first render after the 1.5 s gap
            Assert.True(r[hitch + 1] - r[hitch] > 0.02, $"after the hitch: one render, then a new period ({r[hitch + 1] - r[hitch]:0.000} s)");
        }

        [Test]
        public void CamerasFoundTogetherRenderInDifferentFrames()
        {
            // Three cameras a mod creates together (found in the same scan), 90 fps, 30/s each, with a 0.2 s hitch
            // and a stretch where the owner switched one camera off: never two renders in one frame.
            const double rate = 30, fps = 90;
            double period = 1.0 / rate, now = 50.0, dt = 1.0 / fps;
            var due = new double[3];
            var eligible = new bool[3];
            var render = new bool[3];
            for (int k = 0; k < 3; k++)
                due[k] = RenderRateSchedule.InitialDue(now, period, RenderRateSchedule.PhaseFor(k));
            var counts = new int[3];
            double lastDt = dt;
            for (int frame = 0; frame < 900; frame++)
            {
                for (int k = 0; k < 3; k++)
                    eligible[k] = !(k == 1 && frame >= 400 && frame < 470); // owner switched camera 1 off for a while
                int rendered = RenderRateSchedule.Plan(due, eligible, render, 3, now, period, dt); // typical frame time
                for (int k = 0; k < 3; k++)
                    if (render[k])
                        counts[k]++;
                Assert.False(render[1] && !eligible[1], "a camera its owner switched off is never planned");
                Assert.True(rendered <= 1, $"frame {frame}: {rendered} cameras rendered together");
                lastDt = frame == 300 ? 0.2 : dt;
                now += lastDt;
            }
            Assert.True(counts[0] >= 280 && counts[0] <= 301, $"camera 0 rendered {counts[0]} times in ~10 s");
            Assert.True(counts[2] >= 280 && counts[2] <= 301, $"camera 2 rendered {counts[2]} times in ~10 s");
            Assert.True(counts[1] >= 260 && counts[1] <= 301, $"camera 1 rendered {counts[1]} times (off for 70 frames)");
        }

        [Test]
        public void FrameBudget()
        {
            Assert.Equal(1, RenderRateSchedule.FrameBudget(3, 30, 1.0 / 90), "3 x 30/s at 90 fps: one per frame");
            Assert.Equal(2, RenderRateSchedule.FrameBudget(4, 30, 1.0 / 90));
            Assert.Equal(1, RenderRateSchedule.FrameBudget(1, 30, 1.0 / 120));
            Assert.Equal(3, RenderRateSchedule.FrameBudget(3, 60, 1.0 / 45), "game slower than the rate: all cameras");
            Assert.Equal(0, RenderRateSchedule.FrameBudget(0, 30, 1.0 / 90));
            Assert.Equal(1, RenderRateSchedule.FrameBudget(5, 30, 0), "unknown frame time: one");
        }

        [Test]
        public void PhasesSpreadAnyNumberOfCameras()
        {
            for (int n = 2; n <= 8; n++)
            {
                var p = Enumerable.Range(0, n).Select(RenderRateSchedule.PhaseFor).OrderBy(x => x).ToList();
                double minGap = Enumerable.Range(0, n).Min(i => (i + 1 < n ? p[i + 1] : p[0] + 1) - p[i]);
                Assert.True(p.All(x => x >= 0 && x < 1), "phase within one period");
                Assert.True(minGap > 0.3 / n, $"{n} cameras: smallest gap {minGap:0.000} of a period");
            }
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

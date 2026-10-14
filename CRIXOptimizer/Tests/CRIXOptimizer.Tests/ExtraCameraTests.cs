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
        private static System.Collections.Generic.List<double> Run(double fps, double rate, double seconds, int hash = 12345, Func<int, double> frameTime = null)
        {
            var renders = new System.Collections.Generic.List<double>();
            double period = 1.0 / rate;
            double now = 100.0;
            double due = RenderRateSchedule.InitialDue(now, period, hash);
            for (int frame = 0; now < 100.0 + seconds; frame++)
            {
                double dt = frameTime != null ? frameTime(frame) : 1.0 / fps;
                if (RenderRateSchedule.Due(ref due, now, period, dt))
                    renders.Add(now);
                now += dt;
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
            var r = Run(0, 30, 20, 99, f => f == 300 ? 1.5 : 0.005 + rng.NextDouble() * 0.012);
            Assert.True(r.Count <= 30 * 20 + 1, $"{r.Count} renders in 20 s");
            Assert.True(MaxInAnyWindow(r, 1.0) <= 31, "no catch-up burst after a 1.5 s hitch");
            for (int i = 1; i < r.Count; i++)
                Assert.True(r[i] - r[i - 1] > 0.02, $"renders {r[i - 1]:0.000} and {r[i]:0.000} too close");
        }

        [Test]
        public void CamerasAreStaggered()
        {
            double period = 1.0 / 30;
            var phases = new[] { 1, 2, 77, 4096, 40000, -3 }.Select(h => RenderRateSchedule.InitialDue(0, period, h)).ToList();
            Assert.True(phases.All(p => p >= 0 && p < period), "initial due time within one period");
            Assert.True(phases.Distinct().Count() == phases.Count, "different cameras start at different times");
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

using System;
using CRIXOptimizer.Adaptive;

namespace CRIXOptimizer.Tests
{
    public sealed class AdaptiveControllerTests
    {
        private const double Budget = 1000.0 / 90.0;

        private static AdaptiveController Make(Action<AdaptiveParameters> configure = null)
        {
            var p = new AdaptiveParameters
            {
                MinLevel = -6,
                MaxLevel = 0,
                Sensitivity = 0.5,
                DownSustainSeconds = 2,
                UpSustainSeconds = 20,
                CooldownSeconds = 8,
                SettleSeconds = 2,
                ProbeFailWindowSeconds = 30,
                BaseBackoffSeconds = 60,
                MaxBackoffSeconds = 900,
            };
            configure?.Invoke(p);
            return new AdaptiveController(p);
        }

        private static AdaptiveDecision Step(AdaptiveController c, double t, double fps, double gpuMs = double.NaN) =>
            c.Evaluate(new AdaptiveSample { Now = t, FrameMs = 1000.0 / fps, TargetMs = Budget, GpuMs = gpuMs });

        [Test]
        public void StaysAtBaselineWhenOnTarget()
        {
            AdaptiveController c = Make();
            for (double t = 0; t < 300; t += 0.5)
                Assert.False(Step(c, t, 90).Changed);
            Assert.Equal(0, c.Level);
        }

        [Test]
        public void StepsDownOnlyAfterSustainedDrop()
        {
            AdaptiveController c = Make();
            Assert.False(Step(c, 0.0, 60).Changed);
            Assert.False(Step(c, 1.0, 60).Changed);
            AdaptiveDecision d = Step(c, 2.0, 60);
            Assert.True(d.Changed);
            Assert.Equal(-1, d.Level);
            Assert.Equal(AdaptiveReason.SteppedDown, d.Reason);
        }

        [Test]
        public void ShortDipDoesNothing()
        {
            AdaptiveController c = Make();
            Step(c, 0, 60);
            Step(c, 1, 60);
            Step(c, 1.5, 90); // recovered before the 2 s sustain
            Step(c, 2.5, 60);
            Assert.Equal(0, c.Level);
        }

        [Test]
        public void CooldownAndSettleLimitChangeRate()
        {
            AdaptiveController c = Make();
            int changes = 0;
            for (double t = 0; t <= 30; t += 0.5)
                if (Step(c, t, 45).Changed)
                    changes++;
            // first change at t=2, then every cooldown (8 s) at most: 2, 10, 18, 26 -> 4
            Assert.InRange(changes, 3, 4);
            Assert.True(c.Level >= -4);
        }

        [Test]
        public void RespectsMinimumLevel()
        {
            AdaptiveController c = Make(p => p.MinLevel = -2);
            for (double t = 0; t <= 200; t += 0.5)
                Step(c, t, 30);
            Assert.Equal(-2, c.Level);
            Assert.Equal(AdaptiveReason.AtMinimum, c.LastReason);
        }

        [Test]
        public void ProbesUpAfterLongStablePeriodWithoutGpuTiming()
        {
            AdaptiveController c = Make();
            for (double t = 0; t <= 3; t += 0.5)
                Step(c, t, 60);
            Assert.Equal(-1, c.Level);
            bool steppedUp = false;
            for (double t = 3.5; t <= 40; t += 0.5)
                steppedUp |= Step(c, t, 90).Reason == AdaptiveReason.SteppedUp;
            Assert.True(steppedUp, "probe up after ~20 s at target");
            Assert.Equal(0, c.Level);
        }

        [Test]
        public void FailedProbesBackOffExponentially()
        {
            AdaptiveController c = Make(p => p.MaxLevel = 0);
            double t = 0;
            // Drop to -1.
            for (; t <= 3; t += 0.5) Step(c, t, 60);
            Assert.Equal(-1, c.Level);

            // Simulate: at level 0 the game cannot hold 90 FPS, at -1 it can.
            int ups = 0;
            double lastUp = -1;
            var upTimes = new System.Collections.Generic.List<double>();
            for (; t <= 2000; t += 0.5)
            {
                double fps = c.Level >= 0 ? 60 : 90;
                AdaptiveDecision d = Step(c, t, fps);
                if (d.Reason == AdaptiveReason.SteppedUp)
                {
                    ups++;
                    upTimes.Add(t);
                    lastUp = t;
                }
            }
            Assert.True(ups >= 3, "keeps probing occasionally");
            Assert.True(ups <= 8, $"but backs off (ups={ups})");
            Assert.True(upTimes[2] - upTimes[1] > upTimes[1] - upTimes[0], "gaps between probes grow");
            Assert.True(c.ConsecutiveFailedProbes >= 2);
            Assert.True(lastUp > 0);
        }

        [Test]
        public void GpuHeadroomAllowsUpWithoutProbingDelay()
        {
            AdaptiveController c = Make(p => { p.MaxLevel = 2; p.UpSustainSeconds = 5; });
            bool up = false;
            for (double t = 0; t <= 20; t += 0.5)
                up |= Step(c, t, 90, gpuMs: 5.0).Changed;
            Assert.True(up);
            Assert.True(c.Level > 0);
        }

        [Test]
        public void NoUpgradeWhenGpuIsNearlyFull()
        {
            AdaptiveController c = Make(p => p.MaxLevel = 2);
            for (double t = 0; t <= 300; t += 0.5)
                Step(c, t, 90, gpuMs: 10.5);
            Assert.Equal(0, c.Level, "90 FPS but GPU at 95% of budget: no headroom to raise quality");
        }

        [Test]
        public void GpuHeadroomTargetStepsDownBeforeFramesDrop()
        {
            AdaptiveController c = Make(p => p.GpuHeadroomFraction = 0.15);
            for (double t = 0; t <= 3; t += 0.5)
                Step(c, t, 90, gpuMs: 10.8);
            Assert.Equal(-1, c.Level, "keeps encoder headroom even though frame rate is still on target");
        }

        [Test]
        public void DetectsCpuBound()
        {
            AdaptiveController c = Make();
            Step(c, 0, 60, gpuMs: 4.0);
            Assert.True(c.LastJudgedCpuBound);
            Step(c, 1, 60, gpuMs: 15.0);
            Assert.False(c.LastJudgedCpuBound);
        }

        [Test]
        public void ManualAndFreeze()
        {
            AdaptiveController c = Make();
            c.ManualLevel = -3;
            Assert.Equal(-3, Step(c, 0, 90).Level);
            c.ManualLevel = -99;
            Assert.Equal(-6, Step(c, 1, 90).Level, "manual level clamped to bounds");
            c.ManualLevel = null;
            c.Frozen = true;
            for (double t = 2; t < 100; t += 0.5)
                Step(c, t, 90);
            Assert.Equal(-6, c.Level);
            Assert.Equal(AdaptiveReason.Frozen, c.LastReason);
            c.Reset();
            Assert.Equal(0, c.Level);
        }

        [Test]
        public void TighterBoundsAreEnforcedImmediately()
        {
            AdaptiveController c = Make();
            for (double t = 0; t <= 40; t += 0.5)
                Step(c, t, 30);
            Assert.True(c.Level <= -3);
            c.Parameters.MinLevel = -1;
            Assert.Equal(-1, Step(c, 41, 30).Level);
        }

        [Test]
        public void SensitivityChangesTolerance()
        {
            AdaptiveController tolerant = Make(p => p.Sensitivity = 0);
            AdaptiveController sensitive = Make(p => p.Sensitivity = 1);
            for (double t = 0; t <= 5; t += 0.5)
            {
                Step(tolerant, t, 82); // ~9% below 90
                Step(sensitive, t, 82);
            }
            Assert.Equal(0, tolerant.Level);
            Assert.Equal(-1, sensitive.Level);
        }
    }

    public sealed class QualityLadderTests
    {
        private static QualityLevers Baseline() => new QualityLevers
        {
            HasRenderScale = true, RenderScale = 1.0f,
            HasShadowDistance = true, ShadowDistance = 50f,
            HasLodBias = true, LodBias = 1.0f,
            HasPixelLights = true, PixelLightCount = 4,
        };

        [Test]
        public void LevelZeroIsBaseline()
        {
            QualityLevers l = QualityLadder.Compute(Baseline(), 0, new QualityLadderSettings(), false);
            Assert.Equal(1.0f, l.RenderScale);
            Assert.Equal(50f, l.ShadowDistance);
        }

        [Test]
        public void CheapestReductionsComeFirst()
        {
            var s = new QualityLadderSettings { RenderScaleStep = 0.1f, MinRenderScale = 0.6f };
            QualityLevers l1 = QualityLadder.Compute(Baseline(), -1, s, false);
            Assert.Equal(1.0f, l1.RenderScale, "level -1 keeps resolution");
            Assert.Near(40.0, l1.ShadowDistance, 1e-4);
            QualityLevers l2 = QualityLadder.Compute(Baseline(), -2, s, false);
            Assert.Near(0.9, l2.RenderScale, 1e-5);
            QualityLevers l10 = QualityLadder.Compute(Baseline(), -10, s, false);
            Assert.Near(0.6, l10.RenderScale, 1e-5, "never below the minimum");
            Assert.Near(10.0, l10.ShadowDistance, 1e-4, "shadow distance floors at 20%");
            Assert.Near(0.5, l10.LodBias, 1e-5);
            Assert.Equal(1, l10.PixelLightCount);
        }

        [Test]
        public void MonotonicNonIncreasingCost()
        {
            var s = new QualityLadderSettings();
            QualityLevers previous = QualityLadder.Compute(Baseline(), 0, s, false);
            for (int level = -1; level >= -10; level--)
            {
                QualityLevers l = QualityLadder.Compute(Baseline(), level, s, false);
                Assert.True(l.RenderScale <= previous.RenderScale + 1e-6f);
                Assert.True(l.ShadowDistance <= previous.ShadowDistance + 1e-4f);
                Assert.True(l.LodBias <= previous.LodBias + 1e-6f);
                Assert.True(l.PixelLightCount <= previous.PixelLightCount);
                previous = l;
            }
        }

        [Test]
        public void CpuBoundKeepsResolutionAndDisallowedLeversStay()
        {
            QualityLevers cpu = QualityLadder.Compute(Baseline(), -4, new QualityLadderSettings(), cpuBound: true);
            Assert.Equal(1.0f, cpu.RenderScale);
            QualityLevers noRs = QualityLadder.Compute(Baseline(), -4, new QualityLadderSettings { AllowRenderScale = false, AllowShadows = false }, false);
            Assert.Equal(1.0f, noRs.RenderScale);
            Assert.Equal(50f, noRs.ShadowDistance);
            Assert.Equal(4, noRs.PixelLightCount);
            Assert.True(noRs.LodBias < 1.0f);
        }

        [Test]
        public void PositiveLevelsRaiseOnlyRenderScaleUpToMax()
        {
            var s = new QualityLadderSettings { RenderScaleStep = 0.1f, MaxRenderScale = 1.15f };
            QualityLevers up = QualityLadder.Compute(Baseline(), 3, s, false);
            Assert.Near(1.15, up.RenderScale, 1e-5);
            Assert.Equal(50f, up.ShadowDistance);
        }

        [Test]
        public void BaselineBelowMinimumIsNeverRaisedByReductions()
        {
            QualityLevers b = Baseline();
            b.RenderScale = 0.5f;
            QualityLevers l = QualityLadder.Compute(b, -6, new QualityLadderSettings { MinRenderScale = 0.6f }, false);
            Assert.Equal(0.5f, l.RenderScale);
        }
    }
}

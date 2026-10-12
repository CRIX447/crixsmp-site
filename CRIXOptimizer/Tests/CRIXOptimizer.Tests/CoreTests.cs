using System;
using System.Collections.Generic;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Tests
{
    public sealed class FrameStatisticsTests
    {
        [Test]
        public void AverageFpsIsFramesOverTime()
        {
            var stats = new FrameStatistics(1024, 100);
            for (int i = 0; i < 90; i++)
                stats.Push(11.111f);
            Assert.Near(90.0, stats.AverageFps, 0.05);
            Assert.Near(11.111, stats.AverageFrameMs, 0.001);
            Assert.Near(0.0, stats.StdDevMs, 1e-3);
        }

        [Test]
        public void WindowEvictsOldFramesByDuration()
        {
            var stats = new FrameStatistics(8192, 1.0); // 1 second window
            for (int i = 0; i < 200; i++)
                stats.Push(20f); // 4 seconds of 50 FPS
            for (int i = 0; i < 90; i++)
                stats.Push(11.111f); // then 1 second of 90 FPS
            Assert.InRange(stats.TotalMs, 1000, 1020.01, "window covers one second plus at most one frame");
            Assert.Near(90.0, stats.AverageFps, 1.0);
        }

        [Test]
        public void PercentilesAndOnePercentLow()
        {
            var stats = new FrameStatistics(4096, 600);
            for (int i = 0; i < 990; i++)
                stats.Push(10f);
            for (int i = 0; i < 10; i++)
                stats.Push(40f); // exactly 1% slow frames
            FrameStatsSnapshot s = stats.Snapshot(11.11f);
            Assert.Near(10.0, s.MedianMs, 0.11);
            Assert.Near(10.0, s.P99Ms, 0.11, "p99 is the last fast frame (nearest rank 990)");
            Assert.Near(40.0, s.P999Ms, 0.11);
            Assert.Near(40.0, s.MaxMs, 1e-6);
            Assert.Near(0.01, s.OverBudgetFraction, 1e-9);
            Assert.Equal(10, s.HitchCount);
        }

        [Test]
        public void InvalidSamplesAreIgnored()
        {
            var stats = new FrameStatistics(64, 10);
            stats.Push(float.NaN);
            stats.Push(-1f);
            stats.Push(0f);
            stats.Push(float.PositiveInfinity);
            Assert.Equal(0, stats.Count);
            Assert.Equal(0.0, stats.AverageFps);
        }

        [Test]
        public void CapacityBoundsMemoryAndStaysConsistent()
        {
            var stats = new FrameStatistics(16, 1000);
            var rng = new Random(1);
            for (int i = 0; i < 100000; i++)
                stats.Push(5f + (float)rng.NextDouble() * 20f);
            Assert.Equal(16, stats.Count);
            Assert.InRange(stats.AverageFrameMs, 5, 25);
            Assert.True(stats.StdDevMs >= 0);
            Assert.True(stats.TotalMs > 0 && stats.TotalMs < 16 * 25.01, "running sum stays exact after many evictions");
        }
    }

    public sealed class ThrottleMathTests
    {
        [Test]
        public void IntervalNeverThrottlesBelowRequestedRate()
        {
            Assert.Equal(3, ThrottleMath.IntervalFrames(90, 30));
            Assert.Equal(4, ThrottleMath.IntervalFrames(120, 30));
            Assert.Equal(2, ThrottleMath.IntervalFrames(72, 30));
            Assert.Equal(1, ThrottleMath.IntervalFrames(45, 30), "reprojection at 45 FPS: no throttling");
            Assert.Equal(1, ThrottleMath.IntervalFrames(90, 0), "0 = unthrottled");
            Assert.Equal(1, ThrottleMath.IntervalFrames(0, 30), "unknown FPS = unthrottled");
            Assert.Equal(ThrottleMath.MaxInterval, ThrottleMath.IntervalFrames(1000, 1));
            foreach (double fps in new[] { 60.0, 72, 80, 90, 120, 144 })
                foreach (double rate in new[] { 10.0, 20, 24, 30, 45 })
                    Assert.True(fps / ThrottleMath.IntervalFrames(fps, rate) >= rate - 1e-9, $"effective rate >= requested at {fps}/{rate}");
        }

        [Test]
        public void EveryInstanceRunsExactlyOncePerCycle()
        {
            const int interval = 3;
            var hashes = new[] { 0, 1, 2, 7, 13, -5, int.MinValue, int.MaxValue, 123456789 };
            foreach (int hash in hashes)
            {
                int runs = 0;
                for (int frame = 0; frame < interval * 100; frame++)
                    if (ThrottleMath.IsInPhase(frame, interval, hash))
                        runs++;
                Assert.Equal(100, runs, $"hash {hash}");
            }
        }
    }

    public sealed class TrendAndDegradationTests
    {
        [Test]
        public void TrendSlopeMatchesLinearGrowth()
        {
            var trend = new TrendAnalyzer(60);
            for (int i = 0; i < 60; i++)
                trend.Add(i * 5.0, 100 + i * 5.0 * (10.0 / 60.0)); // 10 MB per minute
            Assert.True(trend.IsFull);
            Assert.Near(10.0, trend.SlopePerSecond * 60, 1e-6);
        }

        [Test]
        public void TrendIsFlatForNoise()
        {
            var trend = new TrendAnalyzer(60);
            var rng = new Random(7);
            for (int i = 0; i < 200; i++)
                trend.Add(i * 5.0, 300 + rng.NextDouble() * 4 - 2);
            Assert.InRange(trend.SlopePerSecond * 60, -0.5, 0.5);
        }

        [Test]
        public void DegradationNeedsSustainAndRecovers()
        {
            var d = new DegradationDetector { ThresholdFraction = 0.1, SustainSeconds = 5, RecoverSeconds = 3, CooldownSeconds = 60 };
            Assert.Equal(DegradationEvent.None, d.Update(0, 70, 90));
            Assert.Equal(DegradationEvent.None, d.Update(4, 70, 90));
            Assert.Equal(DegradationEvent.Started, d.Update(5, 70, 90));
            Assert.True(d.IsDegraded);
            Assert.Equal(DegradationEvent.None, d.Update(6, 90, 90));
            Assert.Equal(DegradationEvent.None, d.Update(7, 70, 90), "a dip resets recovery");
            Assert.Equal(DegradationEvent.None, d.Update(8, 90, 90));
            Assert.Equal(DegradationEvent.Recovered, d.Update(11, 90, 90));
        }

        [Test]
        public void DegradationAlertCooldown()
        {
            var d = new DegradationDetector { ThresholdFraction = 0.1, SustainSeconds = 1, RecoverSeconds = 1, CooldownSeconds = 60 };
            d.Update(0, 50, 90);
            Assert.Equal(DegradationEvent.Started, d.Update(1, 50, 90));
            d.Update(2, 90, 90);
            Assert.Equal(DegradationEvent.Recovered, d.Update(3, 90, 90));
            d.Update(4, 50, 90);
            Assert.Equal(DegradationEvent.None, d.Update(5, 50, 90), "second alert suppressed inside cooldown");
            Assert.Equal(1, d.AlertCount);
        }

        [Test]
        public void SmallDipsAreNotAlerts()
        {
            var d = new DegradationDetector { ThresholdFraction = 0.1, SustainSeconds = 2 };
            for (int t = 0; t < 100; t++)
                Assert.Equal(DegradationEvent.None, d.Update(t, 85, 90), "85 FPS is within 10% of 90");
        }
    }

    public sealed class BenchmarkMathTests
    {
        [Test]
        public void ComputesBothOnePercentLowDefinitions()
        {
            var frames = new float[1000];
            for (int i = 0; i < 1000; i++)
                frames[i] = i < 980 ? 10f : 20f + (i - 980); // 20 slow frames: 20..39 ms
            BenchmarkResult r = BenchmarkMath.Compute(frames, frames.Length, 11.11);
            Assert.Equal(1000, r.Frames);
            Assert.Near(29.0, r.P99Ms, 1e-6, "nearest-rank p99 = 990th sorted value");
            Assert.Near(1000.0 / 29.0, r.OnePercentLowFps, 1e-6);
            Assert.Near(1000.0 / 34.5, r.OnePercentLowAverageFps, 1e-6, "average of worst 10 frames (30..39 ms)");
            Assert.Near(2.0, r.OverBudgetPercent, 1e-9);
            Assert.Equal(17, r.Hitches, "frames over 2x budget (22.2 ms): 23..39 ms");
            double expectedAvg = 1000.0 * 1000 / (980 * 10 + 590);
            Assert.Near(expectedAvg, r.AverageFps, 1e-6);
        }

        [Test]
        public void IgnoresInvalidSamplesAndEmpty()
        {
            BenchmarkResult empty = BenchmarkMath.Compute(new float[0], 0, 11.1);
            Assert.Equal(0, empty.Frames);
            BenchmarkResult r = BenchmarkMath.Compute(new[] { 10f, float.NaN, -2f, 10f }, 4, 11.1);
            Assert.Equal(2, r.Frames);
            Assert.Near(100.0, r.AverageFps, 1e-9);
            Assert.True(r.ToKeyValueText().Contains("avg_fps=100"));
        }
    }

    public sealed class CompanionAppTests
    {
        [Test]
        public void ClassifiesKnownProcesses()
        {
            CompanionApp apps = CompanionApps.ClassifyProcesses(new[] { "explorer", "obs64", "Discord", "VirtualDesktop.Streamer.exe", "vrserver", "svchost", "OVRServer_x64" });
            Assert.True(apps.HasFlag(CompanionApp.Obs));
            Assert.True(apps.HasFlag(CompanionApp.Discord));
            Assert.True(apps.HasFlag(CompanionApp.VirtualDesktop));
            Assert.True(apps.HasFlag(CompanionApp.SteamVR));
            Assert.True(apps.HasFlag(CompanionApp.MetaQuestLink));
            Assert.False(apps.HasFlag(CompanionApp.Alvr));
            Assert.Equal(CompanionApp.None, CompanionApps.ClassifyProcess("obsidian"), "no substring matching");
        }

        [Test]
        public void SuggestsProfilesByPriority()
        {
            Assert.Null(CompanionApps.Suggest(CompanionApp.SteamVR));
            Assert.Equal(Configuration.ProfileKind.DiscordCall, CompanionApps.Suggest(CompanionApp.Discord).Value.Profile);
            Assert.Equal(Configuration.ProfileKind.OBSStreaming, CompanionApps.Suggest(CompanionApp.Obs | CompanionApp.Discord).Value.Profile);
            ProfileSuggestion vd = CompanionApps.Suggest(CompanionApp.VirtualDesktop | CompanionApp.Discord).Value;
            Assert.Equal(Configuration.ProfileKind.VirtualDesktop, vd.Profile);
            Assert.Equal(Configuration.VirtualDesktopPreset.Balanced, vd.VirtualDesktopPreset);
            Assert.Equal(Configuration.VirtualDesktopPreset.Streaming, CompanionApps.Suggest(CompanionApp.VirtualDesktop | CompanionApp.Obs).Value.VirtualDesktopPreset);
        }

        [Test]
        public void ClassifiesHookModules()
        {
            Assert.Equal(GraphicsHook.ObsGameCapture, CompanionApps.ClassifyModule("graphics-hook64.dll"));
            Assert.Equal(GraphicsHook.ReShade, CompanionApps.ClassifyModule("ReShade64.dll"));
            Assert.Equal(GraphicsHook.None, CompanionApps.ClassifyModule("d3d11.dll"), "proxy names need a version-info check");
            Assert.True(CompanionApps.Describe(GraphicsHook.ReShade | GraphicsHook.SteamOverlay).Contains("Steam overlay"));
        }
    }

    public sealed class TrackedSettingTests
    {
        private sealed class Box<T> { public T Value; }

        [Test]
        public void CapturesOriginalAppliesAndRestores()
        {
            var box = new Box<float> { Value = 150f };
            var s = new TrackedSetting<float>("shadowDistance", () => box.Value, v => box.Value = v, ApproxFloatComparer.Default);
            Assert.True(s.Apply(40f));
            Assert.Equal(40f, box.Value);
            Assert.Equal(SettingState.Applied, s.State);
            Assert.True(s.Apply(30f), "re-apply while still ours");
            Assert.Equal(150f, s.Original, "original captured once");
            Assert.Equal(ReleaseOutcome.Restored, s.Release());
            Assert.Equal(150f, box.Value);
            Assert.Equal(SettingState.Untouched, s.State);
        }

        [Test]
        public void ExternalChangeIsRespectedAndNotOverwritten()
        {
            var box = new Box<int> { Value = 4 };
            var log = new MemoryLogSink();
            var ledger = new SettingsLedger(log);
            var s = ledger.Register(new TrackedSetting<int>("pixelLights", () => box.Value, v => box.Value = v));
            s.Apply(2);
            box.Value = 6; // another mod changes it
            Assert.Equal(1, ledger.CheckDrift());
            Assert.Equal(SettingState.ExternallyModified, s.State);
            Assert.True(log.Contains("changed by the game or another plugin"));
            Assert.False(s.Apply(2), "CRIX does not fight over it");
            Assert.Equal(6, box.Value);
            Assert.Equal(ReleaseOutcome.NothingToRestore, s.Release());
            Assert.Equal(6, box.Value, "release never overwrites an external value");
        }

        [Test]
        public void ChangesValueOnlyWhenTheValueDiffers()
        {
            var box = new Box<int> { Value = 2 };
            var s = new TrackedSetting<int>("cascades", () => box.Value, v => box.Value = v);
            Assert.False(s.ChangesValue, "untouched");
            s.Apply(2);
            Assert.Equal(SettingState.Applied, s.State);
            Assert.False(s.ChangesValue, "applying the game's own value is not a change");
            s.Apply(1);
            Assert.True(s.ChangesValue);
            s.Release();
            Assert.False(s.ChangesValue);
        }

        [Test]
        public void ReleaseSkipsWhenChangedAfterLastCheck()
        {
            var box = new Box<int> { Value = 4 };
            var s = new TrackedSetting<int>("x", () => box.Value, v => box.Value = v);
            s.Apply(1);
            box.Value = 9;
            Assert.Equal(ReleaseOutcome.SkippedExternallyChanged, s.Release());
            Assert.Equal(9, box.Value);
        }

        [Test]
        public void RearmAdoptsExternalValueAsNewOriginal()
        {
            var box = new Box<int> { Value = 4 };
            var s = new TrackedSetting<int>("x", () => box.Value, v => box.Value = v);
            s.Apply(1);
            box.Value = 7;
            s.CheckDrift();
            s.Rearm();
            Assert.True(s.Apply(2));
            Assert.Equal(ReleaseOutcome.Restored, s.Release());
            Assert.Equal(7, box.Value, "restores to the externally chosen value, not the stale original");
        }

        [Test]
        public void EngineClampingIsRecorded()
        {
            var box = new Box<int> { Value = 0 };
            var s = new TrackedSetting<int>("msaa", () => box.Value, v => box.Value = Math.Min(v, 4)); // engine clamps
            s.Apply(8);
            Assert.True(s.WasAdjustedByEngine);
            Assert.False(s.CheckDrift(), "clamped value is what we track, so it is not drift");
            Assert.True(s.Describe().Contains("engine adjusted"));
        }

        [Test]
        public void FailingAccessorMarksFailedWithoutThrowing()
        {
            var s = new TrackedSetting<int>("broken", () => throw new InvalidOperationException("no XR"), v => { });
            Assert.False(s.Apply(1));
            Assert.Equal(SettingState.Failed, s.State);
            Assert.Equal(ReleaseOutcome.Failed, s.Release());
            Assert.True(s.Describe().Contains("no XR"));
        }

        [Test]
        public void ReapplyPolicyOverwritesExternalChange()
        {
            var box = new Box<int> { Value = 4 };
            var s = new TrackedSetting<int>("x", () => box.Value, v => box.Value = v) { ReapplyAfterExternalChange = true };
            s.Apply(1);
            box.Value = 9;
            s.CheckDrift();
            Assert.True(s.Apply(1));
            Assert.Equal(1, box.Value);
            s.Release();
            Assert.Equal(9, box.Value, "original becomes the external value");
        }

        [Test]
        public void LedgerRestoreAllReportsOutcomes()
        {
            var a = new Box<int> { Value = 1 };
            var b = new Box<int> { Value = 2 };
            var ledger = new SettingsLedger(NullLogSink.Instance);
            var sa = ledger.Register(new TrackedSetting<int>("a", () => a.Value, v => a.Value = v));
            var sb = ledger.Register(new TrackedSetting<int>("b", () => b.Value, v => b.Value = v));
            ledger.Register(new TrackedSetting<int>("untouched", () => 5, v => { }));
            sa.Apply(10);
            sb.Apply(20);
            b.Value = 99;
            SettingsLedger.RestoreReport report = ledger.RestoreAll();
            Assert.Equal(1, report.Restored);
            Assert.Equal(1, report.SkippedExternal);
            Assert.Equal(0, report.Failed);
            Assert.Equal(1, a.Value);
            Assert.Equal(99, b.Value);
        }
    }

    public sealed class StatusBoardTests
    {
        [Test]
        public void TracksStatesAndVersion()
        {
            var board = new StatusBoard();
            board.Set("Patches", "a", ModuleState.Active);
            board.Set("Patches", "b", ModuleState.Unavailable, "renamed");
            int v = board.Version;
            board.Set("Patches", "a", ModuleState.Active);
            Assert.Equal(v, board.Version, "no-op update does not bump version");
            board.Set("Patches", "a", ModuleState.Faulted, "threw");
            Assert.Equal(1, board.Count(ModuleState.Faulted));
            Assert.Equal(1, board.Count(ModuleState.Unavailable));
            Assert.True(board.TryGet("b", out StatusBoard.Entry e) && e.Detail == "renamed");
        }

        [Test]
        public void OnceLoggerSuppressesRepeats()
        {
            var sink = new MemoryLogSink();
            var once = new OnceLogger(sink);
            once.Warn("k", "hello");
            once.Warn("k", "hello");
            Assert.Equal(1, sink.Lines.Count);
            once.Reset("k");
            once.Warn("k", "hello");
            Assert.Equal(2, sink.Lines.Count);
        }
    }
}

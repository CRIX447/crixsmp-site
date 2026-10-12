using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRIXOptimizer.Compatibility;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.CPU;
using CRIXOptimizer.GPU;
using CRIXOptimizer.Memory;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;
using GorillaNetworking;
using GorillaTag;
using GorillaTag.Cosmetics;
using GorillaTagScripts.Builder;
using HarmonyLib;

namespace CRIXOptimizer.Tests
{
    /// <summary>
    /// One-time setup shared by all integration tests: applies every CRIX game patch to the stand-in
    /// Assembly-CSharp through the production code path (GameTypes -> GamePatchCatalog -> PatchSet -> Harmony).
    /// </summary>
    internal static class Fixture
    {
        public const string HarmonyId = "crix.tests";
        public static int Frame;
        public static MemoryLogSink Log;
        public static StatusBoard Status;
        public static PatchSet Patches;
        public static GamePatchCatalog Catalog;
        public static OptimizationSettings Settings;
        public static readonly PatchGroups AllGroups = new PatchGroups { StutterFixes = true, VisualThrottles = true, CosmeticCulling = true };
        private static bool ready;

        public static void Ensure()
        {
            if (ready)
                return;
            ready = true;

            _ = typeof(VRRigCache).Assembly; // make sure the stand-in Assembly-CSharp is loaded
            FrameClock.SetSource(() => Frame);
            Log = new MemoryLogSink();
            PatchGuard.Log = Log;
            Status = new StatusBoard();

            NetworkSystem.Instance = new NetworkSystem { InRoom = false };
            ZoneManagement.instance = new ZoneManagement();
            VRRigCache.ResetForTest();

            GameTypes types = GameTypes.FromLoadedAssemblies();
            Patches = new PatchSet(new Harmony(HarmonyId), Log, Status);
            PatchGuard.OnFault = (id, message) => Patches.MarkFaulted(id, message);
            Catalog = new GamePatchCatalog(Patches, types, Log);

            Settings = ProfilePresets.Balanced();
            Catalog.Initialize(Settings, AllGroups, potassiumInstalled: false, PotassiumCoexistence.DeferToPotassium, applyNow: true);
            Catalog.UpdateLive(Settings, AllGroups, suspended: false);
            Catalog.UpdateThrottleIntervals(90, Settings, AllGroups, suspended: false);
        }

        public static void Live(Action<OptimizationSettings> change = null, bool suspended = false, double fps = 90)
        {
            Ensure();
            Settings = ProfilePresets.Balanced();
            change?.Invoke(Settings);
            Catalog.UpdateLive(Settings, AllGroups, suspended);
            Catalog.UpdateThrottleIntervals(fps, Settings, AllGroups, suspended);
        }
    }

    [Integration]
    public sealed class PatchApplicationTests
    {
        [Test]
        public void GameTypesResolveByFullAndSimpleName()
        {
            GameTypes types = GameTypes.FromLoadedAssemblies();
            Assert.True(types.IndexedAssemblies.Contains("Assembly-CSharp"));
            Assert.Equal(typeof(SharedBlocksManager), types.Find("GorillaTagScripts.Builder.SharedBlocksManager"));
            Assert.Equal(typeof(SharedBlocksManager), types.Find("SomeOld.Namespace.SharedBlocksManager"), "namespace moves are tolerated");
            Assert.Equal(typeof(EnvironmentProximityReactor), types.Find("EnvironmentProximityReactor"));
            Assert.Null(types.Find("DoesNotExist", "Nope.AlsoMissing"));
        }

        [Test]
        public void EveryGamePatchAppliesAgainstTheStandIn()
        {
            Fixture.Ensure();
            string[] expectedActive =
            {
                EntityLeavePatch.Id, ScoreboardPatches.CreationId, ScoreboardPatches.RefreshId, RigPoolPatch.Id, RigPoolPatch.OnDemandId,
                SharedBlocksPatch.Id, GcPolicyPatches.RoomLeaveId, GcPolicyPatches.AssetUnloadId, MapSanitizerPatch.SafeId,
                UpdateThrottlePatches.TextureTransitionId, UpdateThrottlePatches.VoiceVisualId, UpdateThrottlePatches.ProximityId,
                CosmeticCullingPatch.Id,
            };
            foreach (string id in expectedActive)
                Assert.True(Fixture.Patches.IsApplied(id), $"{id} not applied: {Fixture.Patches.Records.FirstOrDefault(r => r.Id == id)?.Detail}");
            Assert.Equal(0, Fixture.Status.Count(ModuleState.Faulted));
        }

        [Test]
        public void MissingTargetsDisableOnlyThemselves()
        {
            var log = new MemoryLogSink();
            var set = new PatchSet(new Harmony("crix.tests.missing"), log, new StatusBoard());
            Assert.False(set.TryPatch("missing", null, prefix: typeof(PatchApplicationTests).GetMethod(nameof(DummyPrefix))));
            Assert.Equal(ModuleState.Unavailable, set.Records.Single().State);

            // A patch Harmony rejects (injects a field that does not exist) fails alone, without throwing.
            MethodInfo target = typeof(GameEntityManager).GetMethod(nameof(GameEntityManager.OnRigDeactivated));
            Assert.False(set.TryPatch("bad", target, prefix: typeof(PatchApplicationTests).GetMethod(nameof(BadPrefix))));
            Assert.True(log.Contains("Could not patch"));
            Assert.Equal(ModuleState.Unavailable, set.Records.First(r => r.Id == "bad").State);
        }

        [Test]
        public void FailedPatchDoesNotPoisonTheMethodForOtherMods()
        {
            // Regression: without cleanup, HarmonyX keeps a failed patch registered and every later patch of
            // that method (by any mod) fails to compile.
            var set = new PatchSet(new Harmony("crix.tests.poison"), new MemoryLogSink(), new StatusBoard());
            MethodInfo target = typeof(SimpleUnloadUnusedAssets).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.False(set.TryPatch("bad", target, prefix: typeof(PatchApplicationTests).GetMethod(nameof(BadPrefix))));

            var other = new Harmony("another.mod.after.failure");
            try
            {
                other.Patch(target, postfix: new HarmonyMethod(typeof(PatchApplicationTests).GetMethod(nameof(CountingPostfix))));
                int before = postfixCalls;
                new SimpleUnloadUnusedAssets().InvokeOnEnable();
                Assert.Equal(before + 1, postfixCalls, "the other mod's patch works");
            }
            finally
            {
                other.UnpatchSelf();
            }
        }

        private static int postfixCalls;
        public static void CountingPostfix() => postfixCalls++;
        public static void DummyPrefix() { }
        public static void BadPrefix(ref int ___fieldThatDoesNotExist) { }

        [Test]
        public void PotassiumInstalledMeansNoDuplicatePatches()
        {
            var status = new StatusBoard();
            var set = new PatchSet(new Harmony("crix.tests.deferred"), new MemoryLogSink(), status);
            var catalog = new GamePatchCatalog(set, GameTypes.FromLoadedAssemblies(), new MemoryLogSink());
            catalog.Initialize(ProfilePresets.Balanced(), Fixture.AllGroups, potassiumInstalled: true, PotassiumCoexistence.DeferToPotassium, applyNow: true);
            Assert.True(catalog.DeferredToPotassium);
            Assert.Equal(0, set.Records.Count(r => r.State == ModuleState.Active));
            Assert.Equal(GamePatchCatalog.AllPatchIds.Length, status.Count(ModuleState.Deferred));
            Assert.False(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains("crix.tests.deferred")));
        }

        [Test]
        public void FullSanitizerReplacementRefusesNonUnitySignature()
        {
            var set = new PatchSet(new Harmony("crix.tests.full"), new MemoryLogSink(), new StatusBoard());
            MapSanitizerPatch.Apply(set, GameTypes.FromLoadedAssemblies(), SanitizerMode.FullReplacement);
            PatchSet.Record r = set.Records.Single();
            Assert.Equal(ModuleState.Unavailable, r.State);
            Assert.True(r.Detail.Contains("signature"));
        }

        [Test]
        public void HarmonyFootprintReportsOverlaps()
        {
            Fixture.Ensure();
            var other = new Harmony("some.other.mod");
            MethodInfo target = typeof(GameEntityManager).GetMethod(nameof(GameEntityManager.OnRigDeactivated));
            other.Patch(target, prefix: new HarmonyMethod(typeof(PatchApplicationTests).GetMethod(nameof(OtherModPrefix))));
            try
            {
                HarmonyFootprint.Report report = HarmonyFootprint.Scan(Fixture.HarmonyId);
                Assert.Null(report.Error);
                HarmonyFootprint.Overlap overlap = report.Overlaps.SingleOrDefault(o => o.Method.EndsWith("GameEntityManager.OnRigDeactivated"));
                Assert.NotNull(overlap, "overlap not reported");
                Assert.True(overlap.OtherOwners.Contains("some.other.mod"));
                Assert.True(overlap.OtherPrefixCanSkip);
                Assert.True(report.MethodsPerOwner[Fixture.HarmonyId] >= 13);
            }
            finally
            {
                other.UnpatchSelf();
            }
        }

        public static bool OtherModPrefix() => true;
    }

    [Integration]
    public sealed class ThrottlePatchTests
    {
        [Test]
        public void VoiceReactorsRunAtRequestedRateAndSmoothedOnesAlways()
        {
            Fixture.Live();
            Assert.Equal(3, Live.VoiceVisualInterval, "30/s at 90 FPS");
            var plain = Enumerable.Range(0, 30).Select(_ => new VoiceLoudnessReactor()).ToList();
            var smoothed = new VoiceLoudnessReactor(attack: 0.2f);
            for (int f = 0; f < 300; f++)
            {
                Fixture.Frame = f;
                foreach (VoiceLoudnessReactor r in plain)
                    r.Tick();
                smoothed.Tick();
            }
            foreach (VoiceLoudnessReactor r in plain)
                Assert.Equal(100, r.Updates, "each reactor runs exactly every 3rd frame");
            Assert.Equal(300, smoothed.Updates, "reactors with attack/decay smoothing are never throttled");
        }

        [Test]
        public void StaggeringLowersPerFramePeakVersusPotassiumTimers()
        {
            // Scheduling simulation (not a game benchmark): 30 reactors created in the same frame,
            // 90 FPS, 30 updates/s. Compares how many updates land in the busiest frame.
            Fixture.Live();
            const int n = 30, frames = 900;
            var reactors = Enumerable.Range(0, n).Select(_ => new EnvironmentProximityReactor()).ToList();
            var perFrame = new int[frames];
            for (int f = 0; f < frames; f++)
            {
                Fixture.Frame = f;
                int before = reactors.Sum(r => r.Updates);
                foreach (EnvironmentProximityReactor r in reactors)
                    r.Tick();
                perFrame[f] = reactors.Sum(r => r.Updates) - before;
            }
            int crixTotal = perFrame.Sum();
            int crixPeak = perFrame.Max();

            // Potassium 1.0.0 ProximityPatch semantics: per-instance timer next = now + 1/rate.
            var next = new double[n];
            int potassiumTotal = 0, potassiumPeak = 0;
            for (int f = 0; f < frames; f++)
            {
                double now = f / 90.0;
                int count = 0;
                for (int i = 0; i < n; i++)
                {
                    if (now < next[i])
                        continue;
                    next[i] = now + 1.0 / 30.0;
                    count++;
                }
                potassiumTotal += count;
                potassiumPeak = Math.Max(potassiumPeak, count);
            }

            Console.WriteLine($"  # scheduling simulation: CRIX total {crixTotal} peak/frame {crixPeak}; Potassium-style total {potassiumTotal} peak/frame {potassiumPeak}");
            Assert.Equal(n * frames / 3, crixTotal, "same number of updates as a 30/s rate");
            Assert.True(crixPeak < potassiumPeak, "work spread across frames");
            Assert.InRange(crixPeak, 1, n * 2 / 3);
        }

        [Test]
        public void TextureTransitionsThrottleAndToggleLive()
        {
            Fixture.Live();
            var manager = new TextureTransitionerManager();
            int start = TextureTransitionerManager.Updates;
            for (int f = 0; f < 90; f++)
            {
                Fixture.Frame = f;
                manager.Tick();
            }
            Assert.Equal(30, TextureTransitionerManager.Updates - start);

            Fixture.Live(s => s.TextureTransitionRate = 0f); // unthrottled, applied live
            start = TextureTransitionerManager.Updates;
            for (int f = 0; f < 90; f++)
            {
                Fixture.Frame = f;
                manager.Tick();
            }
            Assert.Equal(90, TextureTransitionerManager.Updates - start);
        }

        [Test]
        public void SuspendPassesEverythingThrough()
        {
            Fixture.Live(suspended: true);
            var reactor = new EnvironmentProximityReactor();
            for (int f = 0; f < 60; f++)
            {
                Fixture.Frame = f;
                reactor.Tick();
            }
            Assert.Equal(60, reactor.Updates);
            Fixture.Live();
        }

        [Test]
        public void ReprojectionFrameRateDisablesThrottling()
        {
            Fixture.Live(fps: 45);
            Assert.Equal(1, Live.ProximityInterval);
            Fixture.Live();
        }
    }

    [Integration]
    public sealed class StutterPatchTests
    {
        [Test]
        public void InactiveEntityManagersSkipLeaveScans()
        {
            Fixture.Live();
            var active = new GameEntityManager();
            var inactive = new GameEntityManager();
            GameEntityManager.activeManager = active;
            active.OnRigDeactivated(null);
            inactive.OnRigDeactivated(null);
            Assert.Equal(1, active.ScanCount);
            Assert.Equal(0, inactive.ScanCount);

            Fixture.Live(s => s.SkipInactiveEntityLeaveScans = false);
            inactive.OnRigDeactivated(null);
            Assert.Equal(1, inactive.ScanCount, "toggle applies live");
            Fixture.Live();
        }

        [Test]
        public void RoomLeaveCollectionPolicies()
        {
            Fixture.Live(s => s.RoomLeaveGc = RoomLeaveGcMode.Skip);
            Assert.Equal(2, GcPolicyPatches.RewrittenCollectCalls, "GC.Collect(int) and GC.Collect() both redirected");
            long skipped = GcPolicyPatches.ForcedCollectionsSkipped;
            int calls = RoomSystem.LeftRoomCalls;
            RoomSystem.InvokeOnLeftRoom();
            Assert.Equal(calls + 1, RoomSystem.LeftRoomCalls, "the rest of OnLeftRoom still runs");
            Assert.Equal(skipped + 2, GcPolicyPatches.ForcedCollectionsSkipped);

            Fixture.Live(s => s.RoomLeaveGc = RoomLeaveGcMode.GameDefault);
            int gcBefore = GC.CollectionCount(0);
            RoomSystem.InvokeOnLeftRoom();
            Assert.True(GC.CollectionCount(0) > gcBefore, "game default keeps the forced collection");

            ulong requested = 0;
            GcPolicyPatches.IncrementalCollector = ns => { requested = ns; return true; };
            Fixture.Live(s => s.RoomLeaveGc = RoomLeaveGcMode.Incremental);
            long replaced = GcPolicyPatches.ForcedCollectionsReplaced;
            RoomSystem.InvokeOnLeftRoom();
            Assert.Equal(replaced + 2, GcPolicyPatches.ForcedCollectionsReplaced);
            Assert.Equal(Live.IncrementalCollectBudgetNs, requested);

            GcPolicyPatches.IncrementalCollector = _ => false; // game without incremental GC
            long skippedBefore = GcPolicyPatches.ForcedCollectionsSkipped;
            RoomSystem.InvokeOnLeftRoom();
            Assert.Equal(skippedBefore + 2, GcPolicyPatches.ForcedCollectionsSkipped, "falls back to skipping");
            GcPolicyPatches.IncrementalCollector = null;
            Fixture.Live();
        }

        [Test]
        public void TimedAssetUnloadsAreSkippedWhileEnabled()
        {
            Fixture.Live();
            var unloader = new SimpleUnloadUnusedAssets();
            unloader.InvokeOnEnable();
            Assert.Equal(0, unloader.Unloads);
            Fixture.Live(s => s.SkipTimedAssetUnloads = false);
            unloader.InvokeOnEnable();
            Assert.Equal(1, unloader.Unloads);
            Fixture.Live();
        }

        [Test]
        public void SafeSanitizerLookupsGiveIdenticalResults()
        {
            Fixture.Live();
            Assert.True(MapSanitizerPatch.RewrittenLookups >= 2, "List<int>.Contains and List<Type>.Contains rewritten");
            var candidates = new[] { typeof(string), typeof(int), typeof(Uri), typeof(Version), typeof(List<int>), typeof(System.Text.StringBuilder), typeof(DateTime), typeof(Random), typeof(Dictionary<string, int>) };
            var rng = new Random(3);
            long lookupsBefore = FastLookups.Lookups;
            for (int i = 0; i < 500; i++)
            {
                var obj = new FakeMapObject { Layer = rng.Next(0, 25) };
                int count = rng.Next(0, 4);
                for (int c = 0; c < count; c++)
                    obj.Components.Add(candidates[rng.Next(candidates.Length)]);
                bool expected = ReferenceSanitize(obj, out int expectedLayer);
                bool actual = CustomMapLoader.Invoke(obj);
                Assert.Equal(expected, actual, "sanitizer verdict");
                Assert.Equal(expectedLayer, obj.Layer, "layer handling");
            }
            Assert.True(FastLookups.Lookups > lookupsBefore, "cached lookups were used");

            // The game changing its allow-list at runtime is picked up (cache invalidation).
            var probe = new FakeMapObject { Components = { typeof(DateTime) } };
            Assert.False(CustomMapLoader.Invoke(probe));
            CustomMapLoader.Allowlist.Add(typeof(DateTime));
            try
            {
                Assert.True(CustomMapLoader.Invoke(probe));
            }
            finally
            {
                CustomMapLoader.Allowlist.Remove(typeof(DateTime));
            }
            Assert.False(CustomMapLoader.Invoke(probe));
        }

        /// <summary>Unpatched copy of the stand-in rules, to compare against.</summary>
        private static bool ReferenceSanitize(FakeMapObject obj, out int layer)
        {
            var layers = new List<int> { 0, 1, 2, 4, 5, 9, 11, 18, 20 };
            var types = new List<Type> { typeof(string), typeof(int), typeof(Uri), typeof(Version) };
            var fragments = new List<string> { "Collections.Generic", "Text.StringBuilder" };
            layer = layers.Contains(obj.Layer) ? obj.Layer : 0;
            foreach (Type t in obj.Components)
            {
                if (types.Contains(t))
                    continue;
                string name = t.ToString();
                if (!fragments.Any(name.Contains))
                    return false;
            }
            return true;
        }

        [Test]
        public void ScoreboardRefreshIsStaggeredOnePerFrame()
        {
            Fixture.Live();
            GorillaScoreboardTotalUpdater.instance = new GorillaScoreboardTotalUpdater();
            GorillaScoreboardTotalUpdater.allScoreboards.Clear();
            var boards = Enumerable.Range(0, 4).Select(_ => new GorillaScoreBoard()).ToList();
            GorillaScoreboardTotalUpdater.allScoreboards.AddRange(boards);

            GorillaScoreboardTotalUpdater.UpdateActiveScoreboards();
            GorillaScoreboardTotalUpdater.UpdateActiveScoreboards(); // duplicate requests coalesce
            Assert.Equal(0, boards.Sum(b => b.Updates), "nothing in the request frame");
            for (int frame = 1; frame <= 4; frame++)
            {
                Fixture.Catalog.Tick();
                Assert.Equal(frame, boards.Sum(b => b.Updates));
            }
            Fixture.Catalog.Tick();
            Assert.Equal(4, boards.Sum(b => b.Updates));
            Assert.True(boards.All(b => b.Updates == 1));
        }

        [Test]
        public void ScoreboardWorkIsFlushedWhenFeatureTurnedOff()
        {
            Fixture.Live();
            GorillaScoreboardTotalUpdater.instance = new GorillaScoreboardTotalUpdater();
            GorillaScoreboardTotalUpdater.allScoreboards.Clear();
            var boards = Enumerable.Range(0, 3).Select(_ => new GorillaScoreBoard()).ToList();
            GorillaScoreboardTotalUpdater.allScoreboards.AddRange(boards);
            GorillaScoreboardTotalUpdater.UpdateActiveScoreboards();
            Fixture.Live(s => s.StaggerScoreboardRefresh = false);
            Assert.True(boards.All(b => b.Updates == 1), "pending refreshes ran immediately");
            Fixture.Live();
        }

        [Test]
        public void ScoreboardCreationWaitsForRoomAndRunsOncePerFrame()
        {
            Fixture.Live();
            NetworkSystem.Instance.InRoom = false;
            var spawners = Enumerable.Range(0, 3).Select(_ => new GorillaScoreboardSpawner()).ToList();
            foreach (GorillaScoreboardSpawner s in spawners)
                s.OnJoinedRoom();
            Assert.Equal(0, spawners.Sum(s => s.Joins));
            Fixture.Catalog.Tick();
            Assert.Equal(0, spawners.Sum(s => s.Joins), "not in a room yet");
            NetworkSystem.Instance.InRoom = true;
            Fixture.Catalog.Tick();
            Assert.Equal(1, spawners.Sum(s => s.Joins));
            Fixture.Catalog.Tick();
            Fixture.Catalog.Tick();
            Assert.True(spawners.All(s => s.Joins == 1));
            NetworkSystem.Instance.InRoom = false;
        }

        [Test]
        public void SharedBlocksDeferredUntilZoneActiveAndNoneLost()
        {
            Fixture.Live();
            ZoneManagement.instance.Active.Clear();
            var first = new SharedBlocksManager();
            var second = new SharedBlocksManager();
            first.InvokeStart();
            second.InvokeStart();
            Assert.Equal(0, first.StartCalls + second.StartCalls);
            Assert.Equal(2, SharedBlocksPatch.DeferredCount, "both managers kept (Potassium keeps only the last)");
            Fixture.Catalog.Poll();
            Assert.Equal(0, first.StartCalls);

            ZoneManagement.instance.Active.Add(GTZone.monkeBlocksShared);
            Fixture.Catalog.Poll();
            Assert.Equal(1, first.StartCalls);
            Assert.Equal(1, second.StartCalls);

            var third = new SharedBlocksManager();
            third.InvokeStart();
            Assert.Equal(1, third.StartCalls, "zone already active: starts normally");
        }

        [Test]
        public void SharedBlocksReleasedWhenDisabledLive()
        {
            Fixture.Live();
            ZoneManagement.instance.Active.Clear();
            var manager = new SharedBlocksManager();
            manager.InvokeStart();
            Assert.Equal(0, manager.StartCalls);
            Fixture.Live(s => s.DeferSharedBlocks = false);
            Assert.Equal(1, manager.StartCalls);
            Fixture.Live();
        }

        [Test]
        public void RigPoolStaggersCreationAndFillsOnDemand()
        {
            Fixture.Live(s => s.InitialRigPoolSize = 4);
            VRRigCache.ResetForTest();
            NetworkSystem.Instance.InRoom = false;
            var cache = new VRRigCache();
            cache.InitializeVRRigCache();
            Assert.Equal(1 + 4, VRRigCache.AllRigContainers.Count, "local rig + 4 immediately");

            NetworkSystem.Instance.InRoom = true;
            for (int i = 0; i < 5; i++)
                Fixture.Catalog.Tick();
            Assert.Equal(5, VRRigCache.AllRigContainers.Count, "no warm-up while in a room");

            // Five players need rigs while only four are free: the fifth is created on demand.
            var players = Enumerable.Range(0, 5).Select(_ => new NetPlayer()).ToList();
            foreach (NetPlayer p in players)
                Assert.True(cache.TryGetVrrig(p, out RigContainer _), "every player gets a rig");
            Assert.Equal(6, VRRigCache.AllRigContainers.Count);
            Assert.True(RigPoolPatch.OnDemandRigs >= 1);

            NetworkSystem.Instance.InRoom = false;
            for (int i = 0; i < 40; i++)
                Fixture.Catalog.Tick();
            Assert.Equal(1 + 19, VRRigCache.AllRigContainers.Count, "pool completes to the game's size, one per frame");
            Assert.True(VRRigCache.AllRigContainers.Skip(1).All(c => c.Rig.BuildInitializeCalls == 1), "every rig initialised once");
            Assert.True(VRRigCache.PostSpawnEvents >= 15, "post-spawn event raised for warmed rigs");
            Assert.False(RigPoolPatch.IsWarming);
        }

        [Test]
        public void CosmeticCullingAppliesLiveAndRestoresOriginals()
        {
            Fixture.Live();
            var throttler = new CosmeticsThrottler();
            throttler.InvokeAwake();
            Assert.Equal(4.5f, throttler.DrawAllDistance);
            Assert.Equal(8f, throttler.MaxDrawDistance);
            Assert.Equal(5, throttler.DrawAllCount);
            Assert.Equal(10, throttler.DrawMaxCount);

            Fixture.Live(s => { s.CosmeticFullDetailDistance = 3f; s.CosmeticMaximumDistance = 6f; });
            Assert.Equal(3f, throttler.DrawAllDistance, "new values reach existing instances");
            Assert.Equal(6f, throttler.MaxDrawDistance);

            Fixture.Live(s => s.ReduceDistantCosmetics = false);
            Assert.Equal(10f, throttler.DrawAllDistance, "game values restored");
            Assert.Equal(20f, throttler.MaxDrawDistance);
            Assert.Equal(10, throttler.DrawAllCount);
            Assert.Equal(19, throttler.DrawMaxCount);
            Fixture.Live();
        }
    }

    [Integration]
    public sealed class AccessorTests
    {
        private class Sample
        {
            private float hidden = 1.5f;
            private int Prop { get; set; } = 3;
#pragma warning disable CS0414 // read through reflection by the test
            private static readonly string staticField = "s";
#pragma warning restore CS0414
            private static int StaticProp { get; set; } = 7;
            public int Calls;
            private void Method() => Calls++;
            private void Method1(string arg) => Calls += arg.Length;
            private bool Has(GTZone zone) => zone == GTZone.city;
            private string Make() => "made";
            public float Hidden => hidden;
        }

        [Test]
        public void CompiledAccessorsReachPrivateMembers()
        {
            var s = new Sample();
            Assert.Equal(1.5f, Accessors.Getter<float>(typeof(Sample), "hidden")(s));
            Accessors.Setter<float>(typeof(Sample), "hidden")(s, 2.5f);
            Assert.Equal(2.5f, s.Hidden);
            Assert.Equal(3, Accessors.Getter<int>(typeof(Sample), "Prop")(s));
            Assert.Equal(3f, Accessors.Getter<float>(typeof(Sample), "Prop")(s), "numeric conversion");
            Assert.Equal("s", Accessors.StaticGetter<object>(typeof(Sample), "staticField")());
            Accessors.StaticSetter<int>(typeof(Sample), "StaticProp")(9);
            Assert.Equal(9, Accessors.StaticGetter<int>(typeof(Sample), "StaticProp")());
            Accessors.Action0(typeof(Sample), "Method")(s);
            Accessors.Action1(typeof(Sample), "Method1")(s, "abcd");
            Assert.Equal(5, s.Calls);
            Assert.True(Accessors.PredicateWithConstant(typeof(Sample), "Has", GTZone.city)(s));
            Assert.False(Accessors.PredicateWithConstant(typeof(Sample), "Has", GTZone.forest)(s));
            Assert.Equal("made", Accessors.Func0(typeof(Sample), "Make")(s));
        }

        [Test]
        public void MissingOrIncompatibleMembersReturnNull()
        {
            Assert.Null(Accessors.Getter<float>(typeof(Sample), "nope"));
            Assert.Null(Accessors.Getter<string>(typeof(Sample), "hidden"), "type mismatch");
            Assert.Null(Accessors.StaticGetter<int>(typeof(Sample), "hidden"), "instance field is not static");
            Assert.Null(Accessors.Action0(null, "Method"));
            Assert.Null(Accessors.PredicateWithConstant(typeof(Sample), "Has", "not an enum"));
        }
    }
}

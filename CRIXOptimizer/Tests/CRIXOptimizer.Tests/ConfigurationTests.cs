using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.ReShade;

namespace CRIXOptimizer.Tests
{
    public sealed class ProfilePresetTests
    {
        [Test]
        public void EverySectionResolvesAndValidatesClean()
        {
            foreach (KeyValuePair<string, OptimizationSettings> pair in ProfilePresets.All())
            {
                List<string> notes = pair.Value.Clone().Validate();
                Assert.Equal(0, notes.Count, $"{pair.Key} preset produces validation notes: {string.Join("; ", notes)}");
            }
        }

        [Test]
        public void SectionMapping()
        {
            Assert.Equal("VirtualDesktop", ProfilePresets.SectionFor(ProfileKind.VirtualDesktop, VirtualDesktopPreset.Balanced));
            Assert.Equal("VirtualDesktop.Performance", ProfilePresets.SectionFor(ProfileKind.VirtualDesktop, VirtualDesktopPreset.Performance));
            Assert.Equal("OBSStreaming", ProfilePresets.SectionFor(ProfileKind.OBSStreaming, VirtualDesktopPreset.Performance));
            Assert.Equal("Custom", ProfilePresets.SectionFor(ProfileKind.Custom, VirtualDesktopPreset.Balanced));
            foreach (ProfileKind kind in Enum.GetValues(typeof(ProfileKind)))
                Assert.NotNull(ProfilePresets.For(kind));
        }

        [Test]
        public void ReShadeProfileNeverTouchesRenderTargets()
        {
            OptimizationSettings s = ProfilePresets.ReShadeCompatible();
            Assert.Equal(0f, s.RenderScale);
            Assert.Equal(-1, s.MsaaSamples);
            Assert.Equal(TriState.Unchanged, s.CameraHdr);
            Assert.Equal(DesktopMirrorMode.Unchanged, s.DesktopMirror);
            Assert.Equal(TriState.Unchanged, s.SoftParticles);
            Assert.False(s.AdaptiveMayChangeRenderScale);
        }

        [Test]
        public void StreamingProfilesKeepTheCapturedWindow()
        {
            Assert.Equal(DesktopMirrorMode.Unchanged, ProfilePresets.ObsStreaming().DesktopMirror);
            Assert.Equal(DesktopMirrorMode.Unchanged, ProfilePresets.VirtualDesktopStreaming().DesktopMirror);
        }

        [Test]
        public void MultiModIsConservative()
        {
            OptimizationSettings s = ProfilePresets.MultiMod();
            Assert.False(s.DeferSharedBlocks);
            Assert.False(s.StaggerRigPool);
            Assert.False(s.StaggerScoreboardRefresh);
            Assert.Equal(-1f, s.ShadowDistanceLimit);
            Assert.Equal(0f, s.RenderScale);
            Assert.True(s.MapSanitizer != SanitizerMode.FullReplacement);
        }

        [Test]
        public void NoPresetUsesFullSanitizerReplacementAndMsaaIsOnlyCappedInPerformancePresets()
        {
            foreach (KeyValuePair<string, OptimizationSettings> pair in ProfilePresets.All())
            {
                Assert.True(pair.Value.MapSanitizer != SanitizerMode.FullReplacement, pair.Key + " must not bypass the game's sanitizer by default");
                bool performance = pair.Key == "MaximumPerformance" || pair.Key == "VirtualDesktop.Performance";
                if (performance)
                    Assert.Equal(2, pair.Value.MsaaSamples, pair.Key + " caps MSAA at 2x");
                else
                    Assert.Equal(-1, pair.Value.MsaaSamples, pair.Key + " leaves MSAA alone by default");
                Assert.Equal(-1, pair.Value.TextureMipLimit, pair.Key + " leaves texture resolution alone by default");
            }
        }

        [Test]
        public void ValidationClampsAndFixesInconsistencies()
        {
            var s = new OptimizationSettings
            {
                RenderScale = 0.2f,
                ShadowCascadesLimit = 3,
                MsaaSamples = 5,
                CosmeticFullDetailDistance = 10f,
                CosmeticMaximumDistance = 5f,
                CosmeticFullDetailCount = 8,
                CosmeticMaximumCount = 4,
                VoiceVisualRate = -3f,
                AsyncUploadTimeSliceMs = 99,
                ShadowDistanceLimit = -7f,
            };
            List<string> notes = s.Validate();
            Assert.Equal(0.5f, s.RenderScale);
            Assert.Equal(2, s.ShadowCascadesLimit);
            Assert.Equal(8, s.MsaaSamples);
            Assert.Equal(10f, s.CosmeticMaximumDistance);
            Assert.Equal(8, s.CosmeticMaximumCount);
            Assert.Equal(0f, s.VoiceVisualRate, "negative rate = unthrottled");
            Assert.Equal(33, s.AsyncUploadTimeSliceMs);
            Assert.Equal(-1f, s.ShadowDistanceLimit);
            Assert.True(notes.Count >= 7);
        }

        [Test]
        public void ReShadeGuardForcesSafeValues()
        {
            OptimizationSettings s = ProfilePresets.MaximumPerformance();
            List<string> notes = ReShadeGuard.Apply(s);
            Assert.Equal(TriState.Unchanged, s.CameraHdr);
            Assert.Equal(DesktopMirrorMode.Unchanged, s.DesktopMirror);
            Assert.False(s.AdaptiveMayChangeRenderScale);
            Assert.Equal(0.8f, s.RenderScale, "fixed render scale is still allowed (applied once)");
            Assert.True(notes.Count >= 3);
            Assert.Equal(0, ReShadeGuard.Apply(s).Count, "idempotent");
        }

        [Test]
        public void CatalogDescribesEveryField()
        {
            foreach (FieldInfo field in SettingCatalog.ProfileFields())
            {
                Assert.True(SettingCatalog.Fields.TryGetValue(field.Name, out SettingCatalog.Info info), "missing description for " + field.Name);
                Assert.False(string.IsNullOrEmpty(info.Description));
                if (info.HasRange)
                    Assert.True(field.FieldType == typeof(float) || field.FieldType == typeof(int), field.Name + " has a range but is not numeric");
            }
            Assert.Equal(SettingCatalog.ProfileFields().Count(), SettingCatalog.Fields.Count, "no stale catalog entries");
        }

        [Test]
        public void PresetDefaultsFitTheirConfigRanges()
        {
            foreach (KeyValuePair<string, OptimizationSettings> pair in ProfilePresets.All())
                foreach (FieldInfo field in SettingCatalog.ProfileFields())
                {
                    SettingCatalog.Info info = SettingCatalog.Fields[field.Name];
                    if (!info.HasRange)
                        continue;
                    double v = Convert.ToDouble(field.GetValue(pair.Value));
                    Assert.InRange(v, info.Min, info.Max, $"{pair.Key}.{field.Name}");
                }
        }
    }

    /// <summary>Round-trips the real BepInEx ConfigFile implementation (the version the suite runs against).</summary>
    public sealed class ConfigFileTests
    {
        private static bool pathsReady;

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "crix-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (!pathsReady)
            {
                // The BepInEx chainloader normally does this before any plugin loads (ConfigFile's static
                // CoreConfig needs it). Same internal call, made by reflection.
                pathsReady = true;
                string root = Path.Combine(dir, "BepInEx");
                Directory.CreateDirectory(Path.Combine(root, "config"));
                typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { Path.Combine(dir, "Gorilla Tag.exe"), root, null, null });
            }
            return dir;
        }

        [Test]
        public void ProfilesFileContainsEveryPresetAndResolves()
        {
            string dir = TempDir();
            var main = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var profilesFile = new ConfigFile(Path.Combine(dir, "profiles.cfg"), true);
            var store = new ProfileStore(profilesFile, main);

            string text = File.ReadAllText(Path.Combine(dir, "profiles.cfg"));
            foreach (string section in ProfilePresets.SectionNames)
                Assert.True(text.Contains("[" + section + "]"), "profiles file lacks section " + section);
            Assert.True(File.ReadAllText(Path.Combine(dir, "main.cfg")).Contains("[Custom]"));

            foreach (string section in ProfilePresets.SectionNames)
            {
                OptimizationSettings resolved = store.Resolve(section);
                OptimizationSettings preset = ProfilePresets.ForSection(section);
                foreach (FieldInfo f in SettingCatalog.ProfileFields())
                    Assert.Equal(f.GetValue(preset), f.GetValue(resolved), $"{section}.{f.Name}");
            }
        }

        [Test]
        public void EditedValuesSurviveReloadAndResetRestoresDefaults()
        {
            string dir = TempDir();
            string profilesPath = Path.Combine(dir, "profiles.cfg");
            var main = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var profilesFile = new ConfigFile(profilesPath, true);
            var store = new ProfileStore(profilesFile, main);

            string text = File.ReadAllText(profilesPath);
            int section = text.IndexOf("[OBSStreaming]", StringComparison.Ordinal);
            int key = text.IndexOf("ShadowDistanceLimit = 30", section, StringComparison.Ordinal);
            Assert.True(key > section, "expected default line in OBSStreaming");
            text = text.Substring(0, key) + "ShadowDistanceLimit = 12.5" + text.Substring(key + "ShadowDistanceLimit = 30".Length);
            File.WriteAllText(profilesPath, text);

            profilesFile.Reload();
            Assert.Equal(12.5f, store.Resolve("OBSStreaming").ShadowDistanceLimit);
            Assert.Equal(50f, store.Resolve("Balanced").ShadowDistanceLimit, "other sections untouched");

            store.ResetAllToDefaults();
            Assert.Equal(30f, store.Resolve("OBSStreaming").ShadowDistanceLimit);
        }

        private static string WriteFixture(string dir, string name)
        {
            string path = Path.Combine(dir, name);
            using (Stream s = typeof(ConfigFileTests).Assembly.GetManifestResourceStream("profiles-1.0.0.cfg"))
            using (FileStream f = File.Create(path))
                s.CopyTo(f);
            return path;
        }

        [Test]
        public void FrozenV1PresetsMatchTheShipped100ProfilesFile()
        {
            string dir = TempDir();
            var file = new ConfigFile(WriteFixture(dir, "profiles.cfg"), false);
            int compared = 0;
            foreach (string section in ProfilePresets.SectionNames)
            {
                OptimizationSettings v1 = ProfilePresetsV1.ForSection(section);
                foreach (FieldInfo f in SettingCatalog.ProfileFields())
                {
                    if (f.Name == nameof(OptimizationSettings.AdaptiveMayChangeCosmetics))
                        continue; // added in 1.1.0
                    object stored = typeof(ConfigFile).GetMethods()
                        .First(m => m.Name == nameof(ConfigFile.Bind) && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[3].ParameterType == typeof(ConfigDescription))
                        .MakeGenericMethod(f.FieldType)
                        .Invoke(file, new object[] { section, f.Name, f.GetValue(new OptimizationSettings()), null });
                    object value = stored.GetType().GetProperty("Value").GetValue(stored);
                    Assert.Equal(f.GetValue(v1), value, $"{section}.{f.Name}");
                    compared++;
                }
            }
            Assert.True(compared > 300, "compared " + compared);
        }

        [Test]
        public void UpgradeFrom100KeepsUserEditsAndAppliesNewDefaults()
        {
            string dir = TempDir();
            string path = WriteFixture(dir, "profiles.cfg");
            // The user had edited one Balanced value and one OBSStreaming value in 1.0.0.
            string text = File.ReadAllText(path);
            text = ReplaceInSection(text, "[Balanced]", "ShadowDistanceLimit = 60", "ShadowDistanceLimit = 45");
            text = ReplaceInSection(text, "[OBSStreaming]", "PixelLightLimit = 2", "PixelLightLimit = 3");
            File.WriteAllText(path, text);

            var main = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var store = new ProfileStore(new ConfigFile(path, true), main);

            Assert.Equal(0, store.MigratedFromRevision, "1.0.0 files have no revision");
            Assert.True(store.MigratedEntries > 10, "upgraded entries: " + store.MigratedEntries);
            OptimizationSettings balanced = store.Resolve("Balanced");
            Assert.Equal(45f, balanced.ShadowDistanceLimit, "user edit kept");
            Assert.Equal(2, balanced.ShadowCascadesLimit, "new default applied");
            Assert.True(balanced.EnsureOcclusionMesh, "new default applied");
            OptimizationSettings obs = store.Resolve("OBSStreaming");
            Assert.Equal(3, obs.PixelLightLimit, "user edit kept");
            Assert.Equal(30f, obs.ShadowDistanceLimit, "unchanged 1.0.0 default upgraded");
            Assert.Equal(20f, obs.AdaptiveGpuHeadroomPercent);
            Assert.Equal(-1f, store.Resolve("MultiMod").ShadowDistanceLimit, "unchanged presets stay");
            Assert.True(File.ReadAllText(path).Contains("PresetRevision = " + ProfileStore.CurrentPresetRevision), "revision saved");

            // A second start migrates nothing and keeps everything.
            var again = new ProfileStore(new ConfigFile(path, true), new ConfigFile(Path.Combine(dir, "main.cfg"), true));
            Assert.Equal(0, again.MigratedEntries);
            Assert.Equal(45f, again.Resolve("Balanced").ShadowDistanceLimit);
            Assert.Equal(30f, again.Resolve("OBSStreaming").ShadowDistanceLimit);
        }

        [Test]
        public void FreshInstallNeedsNoMigration()
        {
            string dir = TempDir();
            var store = new ProfileStore(new ConfigFile(Path.Combine(dir, "profiles.cfg"), true), new ConfigFile(Path.Combine(dir, "main.cfg"), true));
            Assert.Equal(0, store.MigratedEntries);
            Assert.Equal(50f, store.Resolve("Balanced").ShadowDistanceLimit);
        }

        private static string ReplaceInSection(string text, string section, string oldLine, string newLine)
        {
            int start = text.IndexOf(section, StringComparison.Ordinal);
            int key = text.IndexOf("\n" + oldLine, start, StringComparison.Ordinal) + 1;
            Assert.True(start >= 0 && key > start, $"{oldLine} not found in {section}");
            return text.Substring(0, key) + newLine + text.Substring(key + oldLine.Length);
        }

        [Test]
        public void OutOfRangeValuesAreClampedByBepInEx()
        {
            string dir = TempDir();
            string profilesPath = Path.Combine(dir, "profiles.cfg");
            var main = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var profilesFile = new ConfigFile(profilesPath, true);
            var store = new ProfileStore(profilesFile, main);
            string text = File.ReadAllText(profilesPath);
            int section = text.IndexOf("[MaximumPerformance]", StringComparison.Ordinal);
            int key = text.IndexOf("RenderScale = 0.8", section, StringComparison.Ordinal);
            text = text.Substring(0, key) + "RenderScale = 7" + text.Substring(key + "RenderScale = 0.8".Length);
            File.WriteAllText(profilesPath, text);
            profilesFile.Reload();
            Assert.Equal(2f, store.Resolve("MaximumPerformance").RenderScale, "AcceptableValueRange clamps to 2");
        }

        [Test]
        public void MainConfigBindsAllEntriesWithDefaults()
        {
            string dir = TempDir();
            var file = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var cfg = new PluginConfig(file);
            Assert.True(cfg.Enabled.Value);
            Assert.Equal(ProfileKind.Balanced, cfg.Profile.Value);
            Assert.True(cfg.AdaptiveEnabled, "adaptive is on by default since 1.1.0");
            Assert.True(cfg.AutoStreamingMode.Value, "auto streaming mode is on by default");
            Assert.Equal(4455, cfg.ObsWebSocketPort.Value);
            Assert.False(cfg.DashboardVisible.Value, "dashboard is off by default");
            Assert.False(cfg.AutoSelectProfile.Value);
            Assert.Equal(PotassiumCoexistence.DeferToPotassium, cfg.WhenPotassiumInstalled.Value);
            Assert.Equal(ExternalChangePolicy.Respect, cfg.ExternalChanges.Value);
            Assert.Equal(UnityEngine.KeyCode.F8, cfg.KeyToggleDashboard.Value.MainKey);
            string text = File.ReadAllText(Path.Combine(dir, "main.cfg"));
            foreach (string section in new[] { "[General]", "[Modules]", "[Adaptive]", "[Dashboard]", "[Diagnostics]", "[Benchmark]", "[Memory]", "[Compatibility]", "[Hotkeys]", "[Actions]", "[Streaming]" })
                Assert.True(text.Contains(section), "missing " + section);
        }

        [Test]
        public void SettingChangedFiresForLiveUpdates()
        {
            string dir = TempDir();
            var file = new ConfigFile(Path.Combine(dir, "main.cfg"), true);
            var cfg = new PluginConfig(file);
            int events = 0;
            file.SettingChanged += (s, e) => events++;
            cfg.Profile.Value = ProfileKind.VirtualDesktop;
            cfg.Profile.Value = ProfileKind.VirtualDesktop; // unchanged value: no event
            Assert.Equal(1, events);
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;

namespace CRIXOptimizer.Configuration
{
    /// <summary>
    /// Binds every profile to a config section. Built-in presets live in the profiles file; the Custom
    /// profile lives in the main config so in-game config editors can change it live.
    /// Binding uses reflection over OptimizationSettings once at startup; resolving a profile reads the
    /// already-parsed entries (only on profile changes, never per frame).
    /// </summary>
    internal sealed class ProfileStore
    {
        private sealed class Binding
        {
            public FieldInfo Field;
            public ConfigEntryBase Entry;
        }

        private static readonly MethodInfo BindMethod = typeof(ConfigFile).GetMethods()
            .First(m => m.Name == nameof(ConfigFile.Bind) && m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 4 &&
                        m.GetParameters()[0].ParameterType == typeof(string) &&
                        m.GetParameters()[3].ParameterType == typeof(ConfigDescription));

        private readonly Dictionary<string, List<Binding>> sections = new Dictionary<string, List<Binding>>(StringComparer.Ordinal);

        /// <summary>Revision of the built-in preset values. 1 = CRIX 1.0.0, 2 = 1.1.0 previews, 3 = CRIX 1.1.0.</summary>
        public const int CurrentPresetRevision = 3;

        /// <summary>
        /// Default changes between revision 2 (1.1.0 preview builds) and 3. Only entries that still hold the old value
        /// are changed. New fields need no entry: BepInEx gives a missing key its (new) default.
        /// </summary>
        private static readonly (string Section, string Field, object From, object To)[] Revision3Changes =
        {
            ("HighRefresh", nameof(OptimizationSettings.AdaptiveMayChangeRenderScale), true, false),
            ("OBSStreaming", nameof(OptimizationSettings.AdaptiveMayChangeRenderScale), true, false),
            ("VirtualDesktop.Streaming", nameof(OptimizationSettings.AdaptiveMayChangeRenderScale), true, false),
        };

        public ConfigFile ProfilesFile { get; }
        private ConfigEntry<int> profilesRevision;
        private ConfigEntry<int> mainRevision;
        public int MigratedEntries { get; private set; }
        public int MigratedFromRevision { get; private set; } = CurrentPresetRevision;

        public ProfileStore(ConfigFile profilesFile, ConfigFile mainFile)
        {
            ProfilesFile = profilesFile;
            // Bind hundreds of entries without rewriting the file after each one.
            bool profilesSave = profilesFile.SaveOnConfigSet;
            bool mainSave = mainFile.SaveOnConfigSet;
            profilesFile.SaveOnConfigSet = false;
            mainFile.SaveOnConfigSet = false;
            try
            {
                foreach (string section in ProfilePresets.SectionNames)
                    sections[section] = BindSection(profilesFile, section, ProfilePresets.ForSection(section));
                sections["Custom"] = BindSection(mainFile, "Custom", ProfilePresets.ForSection("Custom"));
                Migrate(profilesFile, mainFile);
            }
            finally
            {
                profilesFile.SaveOnConfigSet = profilesSave;
                mainFile.SaveOnConfigSet = mainSave;
            }
            profilesFile.Save();
            mainFile.Save();
        }

        /// <summary>
        /// Upgrades preset values the user never changed. BepInEx keeps values already stored in a config file,
        /// so without this an existing 1.0.0 install would never receive improved defaults. Only entries that
        /// still equal the old default are updated; anything the user edited is left exactly as it is.
        /// </summary>
        private void Migrate(ConfigFile profilesFile, ConfigFile mainFile)
        {
            // Each file carries its own marker: the built-in presets live in the profiles file, Custom in the
            // main config, and either file can be deleted or reset on its own.
            var description = new ConfigDescription("Managed by CRIX: version of the built-in preset values this file was last upgraded to. Do not edit.");
            profilesRevision = profilesFile.Bind("Meta", "PresetRevision", 0, description);
            mainRevision = mainFile.Bind("Meta", "PresetRevision", 0, description);
            MigratedFromRevision = profilesRevision.Value;
            bool migrateProfiles = profilesRevision.Value < CurrentPresetRevision;
            bool migrateCustom = mainRevision.Value < CurrentPresetRevision;

            int changed = 0;
            foreach (KeyValuePair<string, List<Binding>> section in sections)
            {
                bool isCustom = section.Key == "Custom";
                if (isCustom ? !migrateCustom : !migrateProfiles)
                    continue;
                int from = isCustom ? mainRevision.Value : profilesRevision.Value;
                if (from < 2)
                {
                    // 1.0.0 file: compare with the frozen 1.0.0 presets. Sections added later have nothing to upgrade.
                    if (!ProfilePresetsV1.HasSection(section.Key))
                        continue;
                    OptimizationSettings oldDefaults = ProfilePresetsV1.ForSection(section.Key);
                    OptimizationSettings newDefaults = ProfilePresets.ForSection(section.Key);
                    foreach (Binding binding in section.Value)
                    {
                        object oldValue = binding.Field.GetValue(oldDefaults);
                        object newValue = binding.Field.GetValue(newDefaults);
                        if (Equals(oldValue, newValue) || !Equals(binding.Entry.BoxedValue, oldValue))
                            continue;
                        binding.Entry.BoxedValue = newValue;
                        changed++;
                    }
                }
                else
                {
                    foreach (var change in Revision3Changes)
                    {
                        if (change.Section != section.Key)
                            continue;
                        Binding binding = section.Value.Find(b => b.Field.Name == change.Field);
                        if (binding == null || !Equals(binding.Entry.BoxedValue, change.From))
                            continue;
                        binding.Entry.BoxedValue = change.To;
                        changed++;
                    }
                }
            }
            MigratedEntries = changed;
            profilesRevision.Value = CurrentPresetRevision;
            mainRevision.Value = CurrentPresetRevision;
        }

        public OptimizationSettings Resolve(string section)
        {
            if (!sections.TryGetValue(section, out List<Binding> bindings))
                return ProfilePresets.Balanced();

            var settings = new OptimizationSettings();
            foreach (Binding binding in bindings)
                binding.Field.SetValue(settings, binding.Entry.BoxedValue);
            return settings;
        }

        public void ResetAllToDefaults()
        {
            foreach (List<Binding> bindings in sections.Values)
                foreach (Binding binding in bindings)
                    binding.Entry.BoxedValue = binding.Entry.DefaultValue;
            // The values are now the current defaults; a reset must not re-arm the 1.0.0 -> 1.1.0 upgrade.
            MarkCurrentRevision();
        }

        /// <summary>Marks both files as holding current-revision preset values (after a reset to defaults).</summary>
        public void MarkCurrentRevision()
        {
            if (profilesRevision != null)
                profilesRevision.Value = CurrentPresetRevision;
            if (mainRevision != null)
                mainRevision.Value = CurrentPresetRevision;
        }

        private static List<Binding> BindSection(ConfigFile file, string section, OptimizationSettings defaults)
        {
            var result = new List<Binding>();
            foreach (FieldInfo field in SettingCatalog.ProfileFields())
            {
                SettingCatalog.Fields.TryGetValue(field.Name, out SettingCatalog.Info info);
                object defaultValue = field.GetValue(defaults);
                ConfigDescription description = new ConfigDescription(
                    info?.Description ?? field.Name,
                    info != null && info.HasRange ? MakeRange(field.FieldType, info.Min, info.Max) : null);

                var entry = (ConfigEntryBase)BindMethod.MakeGenericMethod(field.FieldType)
                    .Invoke(file, new[] { section, field.Name, defaultValue, description });
                result.Add(new Binding { Field = field, Entry = entry });
            }
            return result;
        }

        private static AcceptableValueBase MakeRange(Type type, double min, double max)
        {
            if (type == typeof(float))
                return new AcceptableValueRange<float>((float)min, (float)max);
            if (type == typeof(int))
                return new AcceptableValueRange<int>((int)min, (int)max);
            return null;
        }
    }
}

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

        public ConfigFile ProfilesFile { get; }

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
            }
            finally
            {
                profilesFile.SaveOnConfigSet = profilesSave;
                mainFile.SaveOnConfigSet = mainSave;
            }
            profilesFile.Save();
            mainFile.Save();
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

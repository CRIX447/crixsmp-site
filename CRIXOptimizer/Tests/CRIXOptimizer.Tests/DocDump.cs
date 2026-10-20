using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using CRIXOptimizer.Configuration;

namespace CRIXOptimizer.Tests
{
    /// <summary>
    /// Generates docs/PROFILES.md and example config files from the code, so the documentation cannot drift
    /// from the real defaults. Invoked by `Runner.exe --dump-docs &lt;docs-dir&gt;` (see tools/package-release.sh).
    /// </summary>
    public static class DocDump
    {
        public static int Run(string docsDir)
        {
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "PROFILES.md"), ProfilesMarkdown());

            string examples = Path.Combine(docsDir, "examples");
            Directory.CreateDirectory(examples);
            string temp = Path.Combine(Path.GetTempPath(), "crix-docdump-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(temp, "BepInEx", "config"));
            typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { Path.Combine(temp, "Gorilla Tag.exe"), Path.Combine(temp, "BepInEx"), null, null });

            string mainPath = Path.Combine(examples, "com.crix.crixoptimizer.cfg");
            string profilesPath = Path.Combine(examples, "com.crix.crixoptimizer.profiles.cfg");
            File.Delete(mainPath);
            File.Delete(profilesPath);
            var meta = new BepInEx.BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version);
            var main = new ConfigFile(mainPath, true, meta);
            var profilesFile = new ConfigFile(profilesPath, true, meta);
            _ = new PluginConfig(main);
            _ = new ProfileStore(profilesFile, main);
            main.Save();
            profilesFile.Save();
            Console.WriteLine("Wrote " + Path.Combine(docsDir, "PROFILES.md"));
            Console.WriteLine("Wrote " + mainPath);
            Console.WriteLine("Wrote " + profilesPath);
            return 0;
        }

        private static string ProfilesMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("# Profile reference\n\n");
            sb.Append("Generated from the source (`Configuration/ProfilePresets.cs`, `Configuration/SettingCatalog.cs`). ");
            sb.Append("These are the **built-in starting values**. Every value can be edited per profile in ");
            sb.Append("`BepInEx/config/com.crix.crixoptimizer.profiles.cfg` (the `Custom` profile lives in the `[Custom]` section of ");
            sb.Append("`com.crix.crixoptimizer.cfg`). None of them is claimed to be optimal for every PC, GPU, headset or network.\n\n");
            sb.Append("Conventions: `-1` / `Unchanged` = CRIX does not touch the setting. *Limit* settings are upper bounds: ");
            sb.Append("if the game's own value is already cheaper, the game's value is kept. `RenderScale` 0 = unchanged.\n\n");

            List<KeyValuePair<string, OptimizationSettings>> all = ProfilePresets.All().ToList();
            sb.Append("| Setting | ").Append(string.Join(" | ", all.Select(p => p.Key.Replace("VirtualDesktop.", "VD.")))).Append(" |\n");
            sb.Append("|---|").Append(string.Concat(all.Select(_ => "---|"))).Append('\n');
            foreach (FieldInfo field in SettingCatalog.ProfileFields())
            {
                sb.Append("| `").Append(field.Name).Append("` |");
                foreach (KeyValuePair<string, OptimizationSettings> p in all)
                {
                    object value = field.GetValue(p.Value);
                    object balanced = field.GetValue(all[0].Value);
                    string text = Format(value);
                    bool differs = p.Key != "Balanced" && !Equals(value, balanced);
                    sb.Append(' ').Append(differs ? "**" + text + "**" : text).Append(" |");
                }
                sb.Append('\n');
            }
            sb.Append("\nBold = differs from Balanced.\n\n## Setting descriptions\n\n");
            foreach (FieldInfo field in SettingCatalog.ProfileFields())
            {
                SettingCatalog.Info info = SettingCatalog.Fields[field.Name];
                sb.Append("- **`").Append(field.Name).Append("`** (").Append(TypeName(field.FieldType)).Append(") - ").Append(info.Description);
                if (info.HasRange)
                    sb.Append(" Range ").Append(info.Min.ToString(CultureInfo.InvariantCulture)).Append(" to ").Append(info.Max.ToString(CultureInfo.InvariantCulture)).Append('.');
                if (field.FieldType.IsEnum)
                    sb.Append(" Values: ").Append(string.Join(", ", Enum.GetNames(field.FieldType))).Append('.');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case float f: return f.ToString("0.##", CultureInfo.InvariantCulture);
                case bool b: return b ? "on" : "off";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static string TypeName(Type t) => t == typeof(float) ? "number" : t == typeof(int) ? "integer" : t == typeof(bool) ? "true/false" : "choice";
    }
}

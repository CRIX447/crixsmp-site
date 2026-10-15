// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CRIXOptimizer.ReShade
{
    /// <summary>
    /// Reads (never changes) the ReShade configuration in the game folder: ReShade.ini and the active preset. Lists
    /// the enabled effects, flags the ones that are usually expensive (depth-based or multi-pass effects; a general
    /// rule of thumb, not a measurement on this PC) and checks ReShade's Performance Mode.
    /// On request it writes a separate, lighter copy of the preset; the user's own files are never modified.
    /// No Unity dependency (unit-tested).
    /// </summary>
    public sealed class ReShadePresetAdvisor
    {
        /// <summary>Name fragments of effects that are usually expensive, with the reason shown to the user.</summary>
        private static readonly (string Fragment, string Why)[] HeavyEffects =
        {
            ("rtgi", "ray-traced global illumination"),
            ("globalillumination", "global illumination"),
            ("ssgi", "screen-space global illumination"),
            ("raytrac", "ray tracing"),
            ("pathtrac", "path tracing"),
            ("mxao", "ambient occlusion"),
            ("ssao", "ambient occlusion"),
            ("hbao", "ambient occlusion"),
            ("ssdo", "directional occlusion"),
            ("gloomao", "ambient occlusion"),
            ("ambientocclusion", "ambient occlusion"),
            ("ssr", "screen-space reflections"),
            ("dof", "depth of field"),
            ("bokeh", "depth of field"),
            ("motionblur", "motion blur"),
            ("clarity", "large-radius local contrast"),
            ("godray", "light shafts"),
            ("lightshaft", "light shafts"),
            ("volumetric", "volumetric effect"),
            ("displaydepth", "debug depth view (only for setup; turn it off)"),
        };

        public sealed class Technique
        {
            public string Name;
            public string File;
            public string HeavyReason;
            public string Entry;
        }

        public const string FromStartupPreset = "start-up preset";
        public const string FromPresetPath = "PresetPath";
        public const string FromDefault = "default ReShadePreset.ini";

        /// <summary>One ReShade effect runtime: the desktop window (ReShade.ini) or the VR headset (ReShadeVR.ini,
        /// ReShade 6; each key falls back to ReShade.ini when ReShadeVR.ini does not set it).</summary>
        public sealed class RuntimeConfig
        {
            public string Label = "";
            public string ConfigFile = "";
            public bool IsVr;
            public string PresetPath = "";
            /// <summary>Which setting chose the preset: FromStartupPreset, FromPresetPath or FromDefault.</summary>
            public string PresetSource = "";
            public bool PresetExists;
            /// <summary>null when the config does not say.</summary>
            public bool? PerformanceMode;
            public readonly List<Technique> Enabled = new List<Technique>();

            public IEnumerable<Technique> Heavy
            {
                get
                {
                    foreach (Technique t in Enabled)
                        if (t.HeavyReason != null)
                            yield return t;
                }
            }
        }

        public bool Found { get; private set; }
        /// <summary>ReShade's base folder (the game folder unless [INSTALL] BasePath or RESHADE_BASE_PATH_OVERRIDE move it).</summary>
        public string BasePath { get; private set; } = "";
        public string IniPath { get; private set; } = "";
        /// <summary>The headset runtime first when ReShadeVR.ini exists, then the desktop window.</summary>
        public readonly List<RuntimeConfig> Runtimes = new List<RuntimeConfig>();
        public readonly List<string> Advice = new List<string>();
        public string Error { get; private set; } = "";

        /// <summary>The runtime that matters most in VR: the headset's when ReShadeVR.ini exists, else the desktop's.</summary>
        public RuntimeConfig Primary => Runtimes.Count == 0 ? null : Runtimes.Find(r => r.IsVr) ?? Runtimes[0];
        public string PresetPath => Primary?.PresetPath ?? "";
        public bool? PerformanceMode => Primary?.PerformanceMode;
        public List<Technique> Enabled => Primary?.Enabled ?? new List<Technique>();

        public IEnumerable<Technique> Heavy
        {
            get
            {
                RuntimeConfig primary = Primary;
                return primary != null ? primary.Heavy : new Technique[0];
            }
        }

        /// <summary>Reads ReShade's configuration (and the active presets) the way ReShade itself resolves them.
        /// Read-only. Safe to call off the game thread.</summary>
        public void Analyze(string gameRoot)
        {
            Found = false;
            Runtimes.Clear();
            Advice.Clear();
            Error = "";
            BasePath = "";
            IniPath = "";
            try
            {
                if (string.IsNullOrEmpty(gameRoot))
                    return;
                BasePath = ResolveBasePath(gameRoot);
                IniPath = Path.Combine(BasePath, "ReShade.ini");
                string vrIniPath = Path.Combine(BasePath, "ReShadeVR.ini");
                bool hasIni = File.Exists(IniPath);
                bool hasVrIni = File.Exists(vrIniPath);
                if (!hasIni && !hasVrIni)
                    return;
                Found = true;
                Dictionary<string, Dictionary<string, string>> global = hasIni ? ParseIni(File.ReadAllText(IniPath)) : ParseIni("");
                if (hasVrIni)
                    Runtimes.Add(ResolveRuntime("headset", "ReShadeVR.ini", true, ParseIni(File.ReadAllText(vrIniPath)), global));
                if (hasIni)
                    Runtimes.Add(ResolveRuntime("desktop window", "ReShade.ini", false, global, null));
                BuildAdvice();
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        private RuntimeConfig ResolveRuntime(string label, string file, bool isVr,
            Dictionary<string, Dictionary<string, string>> local, Dictionary<string, Dictionary<string, string>> global)
        {
            // ReShade's config_get: the runtime's own file first, the global ReShade.ini when the key is missing.
            string Get(string key) => Value(local, "GENERAL", key) ?? (global != null ? Value(global, "GENERAL", key) : null);

            var rt = new RuntimeConfig { Label = label, ConfigFile = file, IsVr = isVr };
            string performance = FirstElement(Get("PerformanceMode"));
            if (performance != null)
                rt.PerformanceMode = performance.Trim() == "1" || performance.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            // ReShade's load_config: a valid start-up preset wins, then PresetPath, then <base>\ReShadePreset.ini.
            string startup = FirstElement(Get("StartupPresetPath"));
            string current = FirstElement(Get("PresetPath"));
            if (!string.IsNullOrEmpty(startup) && ResolvePresetPath(BasePath, startup, out string full))
            {
                rt.PresetPath = full;
                rt.PresetSource = FromStartupPreset;
            }
            else if (ResolvePresetPath(BasePath, current, out full))
            {
                rt.PresetPath = full;
                rt.PresetSource = FromPresetPath;
            }
            else
            {
                rt.PresetPath = Path.Combine(BasePath, "ReShadePreset.ini");
                rt.PresetSource = FromDefault;
            }
            rt.PresetExists = File.Exists(rt.PresetPath);
            if (rt.PresetExists)
                ParseTechniques(File.ReadAllText(rt.PresetPath), rt.Enabled);
            return rt;
        }

        /// <summary>ReShade's resolve_preset_path: .ini/.txt only; a file that does not exist yet is valid (new
        /// preset); an existing file must have a Techniques key in its root section.</summary>
        internal static bool ResolvePresetPath(string basePath, string value, out string full)
        {
            full = "";
            if (string.IsNullOrEmpty(value))
                return false;
            string normalized = NormalizeSeparators(value);
            string ext = Path.GetExtension(normalized);
            if (ext != ".ini" && ext != ".txt")
                return false;
            full = FullPath(basePath, normalized);
            if (Directory.Exists(full))
                return false;
            if (!File.Exists(full))
                return true;
            Dictionary<string, Dictionary<string, string>> preset = ParseIni(File.ReadAllText(full));
            return preset[""].ContainsKey("Techniques");
        }

        private static string ResolveBasePath(string gameRoot)
        {
            try
            {
                // ReShade reads [INSTALL] BasePath from the ReShade.ini next to the game, then the environment override.
                string exeIni = Path.Combine(gameRoot, "ReShade.ini");
                if (File.Exists(exeIni))
                {
                    string over = FirstElement(Value(ParseIni(File.ReadAllText(exeIni)), "INSTALL", "BasePath"));
                    if (!string.IsNullOrEmpty(over) && Directory.Exists(FullPath(gameRoot, NormalizeSeparators(over))))
                        return FullPath(gameRoot, NormalizeSeparators(over));
                }
                string env = Environment.GetEnvironmentVariable("RESHADE_BASE_PATH_OVERRIDE");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(FullPath(gameRoot, NormalizeSeparators(env))))
                    return FullPath(gameRoot, NormalizeSeparators(env));
            }
            catch
            {
                // fall back to the game folder
            }
            return gameRoot;
        }

        private static string FullPath(string basePath, string path) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

        /// <summary>ReShade writes Windows paths (".\\x.ini"); on other platforms (the unit tests) use the local separator.</summary>
        private static string NormalizeSeparators(string path) =>
            Path.DirectorySeparatorChar == '/' ? path.Replace('\\', '/') : path;

        /// <summary>Parses the Techniques line of a preset into a single desktop runtime. Internal for tests.</summary>
        internal void AnalyzePresetText(string presetText)
        {
            Runtimes.Clear();
            var rt = new RuntimeConfig { Label = "desktop window", ConfigFile = "ReShade.ini" };
            ParseTechniques(presetText, rt.Enabled);
            Runtimes.Add(rt);
        }

        private static void ParseTechniques(string presetText, List<Technique> into)
        {
            into.Clear();
            foreach (string element in SplitElements(Value(ParseIni(presetText), "", "Techniques")))
            {
                string entry = element.Trim();
                if (entry.Length == 0)
                    continue;
                int at = entry.IndexOf('@');
                var t = new Technique
                {
                    Entry = entry,
                    Name = at >= 0 ? entry.Substring(0, at) : entry,
                    File = at >= 0 ? entry.Substring(at + 1) : "",
                };
                t.HeavyReason = Classify(t.Name + " " + t.File);
                into.Add(t);
            }
        }

        internal void BuildAdvice()
        {
            Advice.Clear();
            foreach (RuntimeConfig rt in Runtimes)
            {
                string prefix = Runtimes.Count > 1 ? $"ReShade ({rt.Label}, {rt.ConfigFile})" : "ReShade";
                if (!rt.PresetExists)
                {
                    Advice.Add($"{prefix}: the active preset {rt.PresetPath} does not exist, so no effects are loaded.");
                    continue;
                }
                if (rt.PerformanceMode == false)
                    Advice.Add(rt.IsVr
                        ? $"{prefix}: Performance Mode is off. The Home-key overlay only controls the desktop window's ReShade, not the headset's: " +
                          "close the game and set PerformanceMode=1 under [GENERAL] in ReShadeVR.ini (or use the ReShade tab of the SteamVR dashboard if you play through SteamVR). " +
                          "It compiles the effects with fixed values and usually runs them noticeably faster."
                        : $"{prefix}: Performance Mode is off. Turn it on with the Performance Mode checkbox at the bottom of the Home tab " +
                          "in the ReShade overlay (Home key) once your preset is set up: it compiles the effects with fixed values and usually runs them noticeably faster.");
                var heavy = new List<string>();
                foreach (Technique t in rt.Heavy)
                    heavy.Add(t.Name + " (" + t.HeavyReason + ")");
                string where = rt.IsVr ? "for each eye at the headset's resolution" : "at the desktop window's resolution";
                if (heavy.Count > 0)
                {
                    string startupNote = rt.PresetSource != FromStartupPreset ? ""
                        : rt.IsVr ? " This preset is the headset's start-up preset (StartupPresetPath in ReShadeVR.ini), loaded at every launch: point it at the copy too, or clear it."
                        : " This preset is ReShade's start-up preset, loaded at every launch: make the copy the start-up preset too (ReShade overlay, Settings), or clear it.";
                    Advice.Add($"{prefix}: {rt.Enabled.Count} effect(s) enabled in {Path.GetFileName(rt.PresetPath)}; usually expensive ones: {string.Join(", ", heavy.ToArray())}. " +
                               $"They run every frame {where}. Disable them while you need frame rate, " +
                               "or use [Actions] CreateLightReShadePreset = true to get a copy of your preset without them." + startupNote);
                }
                else if (rt.Enabled.Count > 0)
                {
                    Advice.Add($"{prefix}: {rt.Enabled.Count} effect(s) enabled in {Path.GetFileName(rt.PresetPath)}, none of the usually expensive kinds.");
                }
            }
        }

        /// <summary>The runtimes whose preset exists and has usually expensive effects (each preset once).</summary>
        public List<RuntimeConfig> LightPresetCandidates()
        {
            var result = new List<RuntimeConfig>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RuntimeConfig rt in Runtimes)
            {
                bool heavy = false;
                foreach (Technique t in rt.Heavy)
                {
                    heavy = true;
                    break;
                }
                if (rt.PresetExists && heavy && seen.Add(rt.PresetPath))
                    result.Add(rt);
            }
            return result;
        }

        /// <summary>True when another runtime (headset or desktop window) uses the same preset file.</summary>
        public bool IsShared(RuntimeConfig rt) =>
            rt != null && Runtimes.FindAll(r => r.PresetExists && string.Equals(r.PresetPath, rt.PresetPath, StringComparison.OrdinalIgnoreCase)).Count > 1;

        /// <summary>How to start using a light copy written for <paramref name="rt"/>. The headset's ReShade has no
        /// Home-key overlay (ReShade gives the VR runtime no keyboard input; its overlay exists only in SteamVR).</summary>
        public string HowToUse(RuntimeConfig rt, string copyPath)
        {
            string rel = RelativeToBase(copyPath);
            bool startup = rt.PresetSource == FromStartupPreset;
            if (rt.IsVr)
            {
                string how = $"For the headset: close the game and set PresetPath={rel} under [GENERAL] in ReShadeVR.ini" +
                             (startup ? $" (and StartupPresetPath={rel}, which is set)" : "") +
                             ", or pick the copy in the ReShade tab of the SteamVR dashboard if you play through SteamVR.";
                if (IsShared(rt))
                    how += " The desktop window uses the same preset: select the copy there in the ReShade overlay (Home key).";
                return how;
            }
            return "Select the copy in the ReShade overlay (Home key)" +
                   (startup ? "; your old preset is ReShade's start-up preset, so also make the copy the start-up preset (overlay, Settings) or clear it." : ".");
        }

        private string RelativeToBase(string path)
        {
            string root = BasePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return BasePath.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? "." + Path.DirectorySeparatorChar + path.Substring(root.Length)
                : path;
        }

        /// <summary>
        /// Writes a copy of the runtime's active preset without the usually expensive effects, next to it, and returns
        /// its path. The original preset and ReShade's config files are not changed; select the copy in the overlay.
        /// </summary>
        public string WriteLightPreset(RuntimeConfig rt)
        {
            if (rt == null || string.IsNullOrEmpty(rt.PresetPath) || !File.Exists(rt.PresetPath))
                throw new InvalidOperationException("no active ReShade preset found");
            string text = File.ReadAllText(rt.PresetPath);
            string light = MakeLightPresetText(text, out int removed);
            if (removed == 0)
                throw new InvalidOperationException("the preset has no usually expensive effects to remove");
            string target = Path.Combine(Path.GetDirectoryName(rt.PresetPath) ?? "", Path.GetFileNameWithoutExtension(rt.PresetPath) + ".CRIX-light.ini");
            File.WriteAllText(target, light);
            return target;
        }

        /// <summary>Returns the preset text with heavy effects removed from its Techniques line. Internal for tests.</summary>
        internal static string MakeLightPresetText(string presetText, out int removed)
        {
            removed = 0;
            var sb = new StringBuilder(presetText.Length);
            string[] lines = presetText.Replace("\r\n", "\n").Split('\n');
            bool inRoot = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                    inRoot = false;
                int eq = trimmed.IndexOf('=');
                bool comment = trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal);
                // Exact key: "TechniqueSorting" also starts with "Techniques" when case is ignored.
                if (inRoot && !comment && eq > 0 && string.Equals(trimmed.Substring(0, eq).Trim(), "Techniques", StringComparison.OrdinalIgnoreCase))
                {
                    var kept = new List<string>();
                    foreach (string element in SplitElements(trimmed.Substring(eq + 1).Trim()))
                    {
                        if (element.Trim().Length == 0)
                            continue;
                        if (Classify(element.Replace('@', ' ')) != null)
                            removed++;
                        else
                            kept.Add(element.Replace(",", ",,")); // ReShade's escape for a comma inside an entry
                    }
                    line = "Techniques=" + string.Join(",", kept.ToArray());
                }
                sb.Append(line);
                if (i < lines.Length - 1)
                    sb.Append("\r\n");
            }
            return sb.ToString();
        }

        internal static string Classify(string nameAndFile)
        {
            string key = (nameAndFile ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "");
            foreach (var heavy in HeavyEffects)
                if (key.Contains(heavy.Fragment))
                    return heavy.Why;
            return null;
        }

        /// <summary>Parses an ini file the way ReShade's ini_file::load does: comment lines start with ';', '/' or
        /// '#'; a section name ends at ']'; a key repeated in a section adds its elements to the earlier value; an
        /// empty value never clears an existing one. Values are raw (see SplitElements). Internal for tests.</summary>
        internal static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in (text ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim(' ', '\t', '\r', '\n');
                if (line.Length == 0 || line[0] == ';' || line[0] == '/' || line[0] == '#')
                    continue;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    section = (close >= 0 ? line.Substring(0, close) : line).Trim(' ', '\t', '[', ']');
                    if (!result.ContainsKey(section))
                        result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                Dictionary<string, string> values = result[section];
                int eq = line.IndexOf('=');
                string key = eq >= 0 ? line.Substring(0, eq).Trim() : line;
                string value = eq >= 0 ? line.Substring(eq + 1).Trim() : "";
                if (!values.TryGetValue(key, out string existing))
                    values[key] = value;
                else if (value.Length > 0)
                    values[key] = existing.Length > 0 ? existing + "," + value : value;
            }
            return result;
        }

        /// <summary>Splits a raw ReShade value into its elements: a single ',' separates, ",," is a literal comma.</summary>
        internal static List<string> SplitElements(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(raw))
                return result;
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == ',')
                {
                    if (i + 1 < raw.Length && raw[i + 1] == ',')
                    {
                        sb.Append(',');
                        i++;
                        continue;
                    }
                    result.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        /// <summary>The first element of a raw value (how ReShade reads a single path), or null when the key is missing.</summary>
        internal static string FirstElement(string raw)
        {
            if (raw == null)
                return null;
            List<string> elements = SplitElements(raw);
            return elements.Count > 0 ? elements[0] : "";
        }

        private static string Value(Dictionary<string, Dictionary<string, string>> ini, string section, string key) =>
            ini.TryGetValue(section, out Dictionary<string, string> values) && values.TryGetValue(key, out string value) ? value : null;
    }
}

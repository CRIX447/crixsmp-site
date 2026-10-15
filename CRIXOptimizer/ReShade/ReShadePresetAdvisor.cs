// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ini = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>>;

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
        /// <summary>The headset runtime first when ReShadeVR.ini exists, then the desktop window. A runtime switched off
        /// with [GENERAL] Disable in its own file is left out (ReShade does not create it).</summary>
        public readonly List<RuntimeConfig> Runtimes = new List<RuntimeConfig>();
        public readonly List<string> Advice = new List<string>();
        public string Error { get; private set; } = "";
        /// <summary>A headset is active but there is no ReShadeVR.ini yet: if ReShade also runs in the headset, it uses
        /// ReShade.ini's values until it saves its own file when the game closes.</summary>
        public bool VrWithoutOwnConfig { get; private set; }
        private readonly List<string> disabledNotes = new List<string>();

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
        /// Read-only. Safe to call off the game thread. <paramref name="vrActive"/>: a headset is in use.</summary>
        public void Analyze(string gameRoot, bool vrActive = false)
        {
            Found = false;
            Runtimes.Clear();
            Advice.Clear();
            disabledNotes.Clear();
            Error = "";
            BasePath = "";
            IniPath = "";
            VrWithoutOwnConfig = false;
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
                Ini global = hasIni ? ParseIni(File.ReadAllText(IniPath)) : ParseIni("");
                if (hasVrIni)
                {
                    Ini vr = ParseIni(File.ReadAllText(vrIniPath));
                    // ReShade's create_effect_runtime reads Disable from the runtime's own file only (no fallback).
                    if (ParseBool(FirstElement(Value(vr, "GENERAL", "Disable"))))
                        disabledNotes.Add("ReShade (headset): switched off with [GENERAL] Disable in ReShadeVR.ini, so it does not run in the headset.");
                    else
                        Runtimes.Add(ResolveRuntime("headset", "ReShadeVR.ini", true, vr, global));
                }
                if (hasIni)
                {
                    if (ParseBool(FirstElement(Value(global, "GENERAL", "Disable"))))
                        disabledNotes.Add("ReShade (desktop window): switched off with [GENERAL] Disable in ReShade.ini.");
                    else
                        Runtimes.Add(ResolveRuntime("desktop window", "ReShade.ini", false, global, null));
                }
                VrWithoutOwnConfig = vrActive && !hasVrIni;
                BuildAdvice();
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        private RuntimeConfig ResolveRuntime(string label, string file, bool isVr, Ini local, Ini global)
        {
            // ReShade's config_get: the runtime's own file first, the global ReShade.ini when the key is missing.
            List<string> Get(string key) => Value(local, "GENERAL", key) ?? (global != null ? Value(global, "GENERAL", key) : null);

            var rt = new RuntimeConfig { Label = label, ConfigFile = file, IsVr = isVr };
            string performance = FirstElement(Get("PerformanceMode"));
            if (performance != null)
                rt.PerformanceMode = ParseBool(performance);

            // ReShade's load_config: a valid start-up preset wins, then PresetPath, then <base>\\ReShadePreset.ini.
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

        /// <summary>ReShade's bool: a leading non-zero integer, or exactly "true", "True" or "TRUE".</summary>
        internal static bool ParseBool(string element)
        {
            if (string.IsNullOrEmpty(element))
                return false;
            if (element == "true" || element == "True" || element == "TRUE")
                return true;
            int i = 0;
            while (i < element.Length && char.IsWhiteSpace(element[i]))
                i++;
            if (i < element.Length && (element[i] == '+' || element[i] == '-'))
                i++;
            for (; i < element.Length && element[i] >= '0' && element[i] <= '9'; i++)
                if (element[i] != '0')
                    return true;
            return false;
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
            return ParseIni(File.ReadAllText(full))[""].ContainsKey("Techniques");
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
            List<string> elements = Value(ParseIni(presetText), "", "Techniques");
            if (elements == null)
                return;
            foreach (string element in elements)
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

        private const string FirstVrSessionNote =
            " If ReShade also runs in your headset, it uses these settings for now, but it saves its own ReShadeVR.ini when the game closes; " +
            "the Home-key overlay only changes the desktop window's settings, so after that set the same values under [GENERAL] in ReShadeVR.ini.";

        internal void BuildAdvice()
        {
            Advice.Clear();
            Advice.AddRange(disabledNotes);
            foreach (RuntimeConfig rt in Runtimes)
            {
                string prefix = Runtimes.Count > 1 ? $"ReShade ({rt.Label}, {rt.ConfigFile})" : "ReShade";
                string vrNote = !rt.IsVr && VrWithoutOwnConfig ? FirstVrSessionNote : "";
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
                          "in the ReShade overlay (Home key) once your preset is set up: it compiles the effects with fixed values and usually runs them noticeably faster." + vrNote);
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
                               "or use [Actions] CreateLightReShadePreset = true to get a copy of your preset without them." + startupNote + vrNote);
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
        public bool IsShared(RuntimeConfig rt) => OthersUsing(rt).Count > 0;

        private List<RuntimeConfig> OthersUsing(RuntimeConfig rt) =>
            rt == null ? new List<RuntimeConfig>()
                : Runtimes.FindAll(r => !ReferenceEquals(r, rt) && r.PresetExists && string.Equals(r.PresetPath, rt.PresetPath, StringComparison.OrdinalIgnoreCase));

        /// <summary>How to start using a light copy written for <paramref name="rt"/>. The headset's ReShade has no
        /// Home-key overlay (ReShade gives the VR runtime no keyboard input; its overlay exists only in SteamVR).
        /// Values to type into an ini file are escaped like ReShade writes them (a comma is written twice).</summary>
        public string HowToUse(RuntimeConfig rt, string copyPath)
        {
            string value = RelativeToBase(copyPath).Replace(",", ",,");
            bool startup = rt.PresetSource == FromStartupPreset;
            string how;
            if (rt.IsVr)
            {
                how = $"For the headset: close the game and set PresetPath={value} under [GENERAL] in ReShadeVR.ini" +
                      (startup ? $" (and StartupPresetPath={value}, which is set)" : "") +
                      ", or pick the copy in the ReShade tab of the SteamVR dashboard if you play through SteamVR.";
            }
            else
            {
                how = "Select the copy in the ReShade overlay (Home key)" +
                      (startup ? "; your old preset is ReShade's start-up preset, so also make the copy the start-up preset (overlay, Settings) or clear it." : ".") +
                      (VrWithoutOwnConfig ? FirstVrSessionNote : "");
            }
            foreach (RuntimeConfig other in OthersUsing(rt))
            {
                if (other.IsVr)
                    continue; // candidates list the headset first, so a shared preset is explained from the headset's side
                how += " The desktop window uses the same preset: select the copy there in the ReShade overlay (Home key)" +
                       (other.PresetSource == FromStartupPreset
                           ? "; it is also the desktop window's start-up preset, so make the copy the start-up preset too (overlay, Settings) or clear it."
                           : ".");
            }
            return how;
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
        /// '#'; a section name ends at ']'; values are split into elements (see SplitElements); a key repeated in a
        /// section adds its elements to the earlier ones; an empty value adds none. Internal for tests.</summary>
        internal static Ini ParseIni(string text)
        {
            var result = new Ini(StringComparer.OrdinalIgnoreCase);
            string section = "";
            result[section] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
                        result[section] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                Dictionary<string, List<string>> values = result[section];
                int eq = line.IndexOf('=');
                string key = eq >= 0 ? line.Substring(0, eq).Trim() : line;
                string value = eq >= 0 ? line.Substring(eq + 1).Trim() : "";
                if (!values.TryGetValue(key, out List<string> elements))
                    values[key] = elements = new List<string>();
                if (value.Length > 0)
                    elements.AddRange(SplitElements(value));
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

        /// <summary>The first element (how ReShade reads a single path or flag), or null when the key is missing.</summary>
        internal static string FirstElement(List<string> elements)
        {
            if (elements == null)
                return null;
            return elements.Count > 0 ? elements[0] : "";
        }

        private static List<string> Value(Ini ini, string section, string key) =>
            ini.TryGetValue(section, out Dictionary<string, List<string>> values) && values.TryGetValue(key, out List<string> value) ? value : null;
    }
}

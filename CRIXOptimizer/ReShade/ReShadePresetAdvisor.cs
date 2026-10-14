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

        public bool Found { get; private set; }
        public string IniPath { get; private set; } = "";
        public string PresetPath { get; private set; } = "";
        /// <summary>null when ReShade.ini does not say.</summary>
        public bool? PerformanceMode { get; private set; }
        public readonly List<Technique> Enabled = new List<Technique>();
        public readonly List<string> Advice = new List<string>();
        public string Error { get; private set; } = "";

        public IEnumerable<Technique> Heavy
        {
            get
            {
                foreach (Technique t in Enabled)
                    if (t.HeavyReason != null)
                        yield return t;
            }
        }

        /// <summary>Reads ReShade.ini (and its preset) from the game folder. Safe to call off the game thread.</summary>
        public void Analyze(string gameRoot)
        {
            Found = false;
            Enabled.Clear();
            Advice.Clear();
            Error = "";
            PerformanceMode = null;
            PresetPath = "";
            try
            {
                IniPath = string.IsNullOrEmpty(gameRoot) ? "" : Path.Combine(gameRoot, "ReShade.ini");
                if (IniPath.Length == 0 || !File.Exists(IniPath))
                    return;
                Found = true;
                Dictionary<string, Dictionary<string, string>> ini = ParseIni(File.ReadAllText(IniPath));
                string general = Value(ini, "GENERAL", "PerformanceMode");
                if (general != null)
                    PerformanceMode = general.Trim() == "1";
                string preset = Value(ini, "GENERAL", "PresetPath");
                if (string.IsNullOrEmpty(preset))
                {
                    Advice.Add("ReShade: no active preset found in ReShade.ini.");
                    return;
                }
                PresetPath = Path.GetFullPath(Path.Combine(gameRoot, preset.Trim()));
                if (!File.Exists(PresetPath))
                {
                    Advice.Add("ReShade: the active preset " + PresetPath + " does not exist.");
                    return;
                }
                AnalyzePresetText(File.ReadAllText(PresetPath));
                BuildAdvice();
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        /// <summary>Parses the Techniques line of a preset. Internal for tests.</summary>
        internal void AnalyzePresetText(string presetText)
        {
            Enabled.Clear();
            Dictionary<string, Dictionary<string, string>> preset = ParseIni(presetText);
            string techniques = Value(preset, "", "Techniques") ?? "";
            foreach (string raw in techniques.Split(','))
            {
                string entry = raw.Trim();
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
                Enabled.Add(t);
            }
        }

        internal void BuildAdvice()
        {
            Advice.Clear();
            var heavy = new List<string>();
            foreach (Technique t in Heavy)
                heavy.Add(t.Name + " (" + t.HeavyReason + ")");
            if (PerformanceMode == false)
                Advice.Add("ReShade: Performance Mode is off. Turn it on in the ReShade overlay (Home key > Settings or the Home tab checkbox) once your preset is set up: " +
                           "it compiles the effects with fixed values and usually runs them noticeably faster.");
            if (heavy.Count > 0)
                Advice.Add($"ReShade: {Enabled.Count} effect(s) enabled; usually expensive ones: {string.Join(", ", heavy.ToArray())}. " +
                           "These run on every frame at full resolution (in VR, for each eye if ReShade applies them there). Disable them while you need frame rate, " +
                           "or use [Actions] CreateLightReShadePreset = true to get a copy of your preset without them.");
            else if (Enabled.Count > 0)
                Advice.Add($"ReShade: {Enabled.Count} effect(s) enabled, none of the usually expensive kinds.");
        }

        /// <summary>
        /// Writes a copy of the active preset without the usually expensive effects, next to it, and returns its path.
        /// The original preset and ReShade.ini are not changed; select the copy in the ReShade overlay.
        /// </summary>
        public string WriteLightPreset()
        {
            if (string.IsNullOrEmpty(PresetPath) || !File.Exists(PresetPath))
                throw new InvalidOperationException("no active ReShade preset found");
            string text = File.ReadAllText(PresetPath);
            string light = MakeLightPresetText(text, out int removed);
            if (removed == 0)
                throw new InvalidOperationException("the preset has no usually expensive effects to remove");
            string target = Path.Combine(Path.GetDirectoryName(PresetPath) ?? "", Path.GetFileNameWithoutExtension(PresetPath) + ".CRIX-light.ini");
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
                // Exact key: "TechniqueSorting" also starts with "Techniques" when case is ignored.
                if (inRoot && eq > 0 && string.Equals(trimmed.Substring(0, eq).Trim(), "Techniques", StringComparison.OrdinalIgnoreCase))
                {
                    string value = trimmed.Substring(eq + 1);
                    var kept = new List<string>();
                    foreach (string raw in value.Split(','))
                    {
                        string entry = raw.Trim();
                        if (entry.Length == 0)
                            continue;
                        if (Classify(entry.Replace('@', ' ')) != null)
                            removed++;
                        else
                            kept.Add(entry);
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

        internal static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!result.ContainsKey(section))
                        result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                result[section][line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        private static string Value(Dictionary<string, Dictionary<string, string>> ini, string section, string key) =>
            ini.TryGetValue(section, out Dictionary<string, string> values) && values.TryGetValue(key, out string value) ? value : null;
    }
}

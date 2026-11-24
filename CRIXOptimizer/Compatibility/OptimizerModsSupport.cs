// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>
    /// What other optimisation mods the user runs alongside CRIX actually do in this game, from reading their
    /// published source: GorillaOptimizer (menu toggles saved in PlayerPrefs, applied with Unity's built-in-pipeline
    /// settings), I HATE ALIENS (removes the alien ship) and Potassium (stutter fixes CRIX also contains). Read-only:
    /// CRIX reads GorillaOptimizer's saved toggles and never changes them or any other mod's settings.
    /// No Unity dependency (the caller supplies the toggle reader; unit tested).
    /// </summary>
    internal static class OptimizerModsSupport
    {
        public const string GorillaOptimizerGuid = "com.sev.gorillatag.GorillaOptimizer";
        public const string IHateAliensGuid = "com.eldon.ihatealiens";
        /// <summary>GorillaOptimizer stores each toggle as PlayerPrefs int "GorillaOptimizer-&lt;key&gt;" (1 = on).</summary>
        public const string GorillaOptimizerPrefsPrefix = "GorillaOptimizer-";

        private enum Effect
        {
            /// <summary>Works in Gorilla Tag.</summary>
            Works,
            /// <summary>A built-in render pipeline setting: Gorilla Tag uses URP, which ignores or overwrites it.</summary>
            NoEffectUrp,
            /// <summary>Frame-rate setting that VR runtimes ignore (the headset sets the pace).</summary>
            NoEffectVr,
            /// <summary>Works, with a cost or side effect worth knowing.</summary>
            SideEffect,
        }

        private static readonly (string Key, string Label, Effect Effect, string Note)[] GorillaOptimizerToggles =
        {
            ("LowTextures", "Low Textures", Effect.SideEffect, "textures at 1/8 resolution: saves graphics memory, looks much blurrier"),
            ("NoShadows", "No Shadows", Effect.NoEffectUrp, ""),
            ("LowShadowRes", "Low Shadow Res", Effect.NoEffectUrp, ""),
            ("ZeroShadowDist", "Zero Shadow Dist", Effect.NoEffectUrp, ""),
            ("NoAA", "No AntiAliasing", Effect.NoEffectUrp, "URP sets anti-aliasing from its own asset"),
            ("NoAniso", "No Anisotropic", Effect.Works, ""),
            ("LowPixelLights", "Zero Pixel Lights", Effect.NoEffectUrp, ""),
            ("NoSoftParticles", "No Soft Particles", Effect.NoEffectUrp, ""),
            ("LowLODBias", "Low LOD Bias", Effect.SideEffect, "LOD bias 0.25: distant objects switch to low detail very early; CRIX then leaves LOD bias to it"),
            ("NoReflections", "No Reflection Probes", Effect.Works, ""),
            ("NoVSync", "No VSync", Effect.NoEffectVr, ""),
            ("CapFPS", "Cap FPS 60", Effect.NoEffectVr, ""),
            ("LowRenderScale", "Low XR Scale", Effect.NoEffectUrp, "URP sets the eye resolution itself every frame; use CRIX's MaxEyeMegapixels instead"),
            ("UltraLowRenderScale", "Ultra Low XR Scale", Effect.NoEffectUrp, "URP sets the eye resolution itself every frame; use CRIX's MaxEyeMegapixels instead"),
            ("DisableMotionVectors", "Disable Motion Vectors", Effect.NoEffectUrp, ""),
            ("LowPhysicsRate", "Low Physics Rate", Effect.SideEffect,
                "sets the physics step to 30 per second; Gorilla Tag's movement runs on physics, so movement can feel different. CRIX never changes physics; turn it off if climbing or tagging feels off"),
            ("CullRendererShadows", "Cull Renderer Shadows", Effect.SideEffect, "only objects that exist when it is switched on; the game's URP asset already has shadows off"),
            ("DisablePostProcessing", "Disable Post Processing", Effect.SideEffect,
                "switches off every component whose type name contains \"Post\", not only post-processing"),
            ("AggressiveGC", "Aggressive GC", Effect.SideEffect, "a full garbage collection when switched on and on low-memory warnings: a hitch each time"),
            ("NoParticles", "No Particles", Effect.SideEffect, "only particle systems that exist when it is switched on"),
            ("LowParticleCount", "Low Particle Count", Effect.SideEffect, "only particle systems that exist when it is switched on"),
            ("PauseOffscreenParticles", "Pause Offscreen Particles", Effect.SideEffect, "only once, when switched on"),
        };

        /// <summary>
        /// Advice lines for the mods found. <paramref name="gorillaOptimizerToggle"/> reads one saved toggle by key
        /// (null = GorillaOptimizer not installed). <paramref name="urp"/>: the game renders with URP.
        /// </summary>
        public static List<string> Describe(Func<string, bool> gorillaOptimizerToggle, bool urp, bool iHateAliens, bool potassium)
        {
            var lines = new List<string>();
            if (gorillaOptimizerToggle != null)
            {
                var works = new List<string>();
                var noEffect = new List<string>();
                var notes = new List<string>();
                foreach (var t in GorillaOptimizerToggles)
                {
                    bool on;
                    try
                    {
                        on = gorillaOptimizerToggle(t.Key);
                    }
                    catch
                    {
                        on = false;
                    }
                    if (!on)
                        continue;
                    Effect effect = !urp && t.Effect == Effect.NoEffectUrp ? Effect.Works : t.Effect;
                    if (effect == Effect.NoEffectUrp || effect == Effect.NoEffectVr)
                        noEffect.Add(t.Label);
                    else
                        works.Add(t.Label);
                    if (t.Note.Length > 0 && (effect == Effect.SideEffect || effect == Effect.NoEffectUrp))
                        notes.Add(t.Label + ": " + t.Note);
                }
                if (works.Count == 0 && noEffect.Count == 0)
                {
                    lines.Add("GorillaOptimizer: installed, no toggles switched on.");
                }
                else
                {
                    string text = "GorillaOptimizer (its menu toggles, read from its saved settings):";
                    if (works.Count > 0)
                        text += " in effect: " + string.Join(", ", works.ToArray()) + ".";
                    if (noEffect.Count > 0)
                        text += " No effect in Gorilla Tag: " + string.Join(", ", noEffect.ToArray()) +
                                (urp ? " (built-in-pipeline settings that URP ignores, or frame-rate caps that VR runtimes ignore)." : " (frame-rate caps that VR runtimes ignore).");
                    lines.Add(text);
                    foreach (string note in notes)
                        lines.Add("GorillaOptimizer " + note + ".");
                }
            }
            if (iHateAliens)
                lines.Add("I HATE ALIENS: removes the alien ship (its rendering, sounds and particles) and checks it twice a second; it does not overlap with anything CRIX changes.");
            if (potassium)
                lines.Add("Potassium: CRIX contains the same stutter fixes and leaves them to Potassium. Potassium's distant-cosmetic and update-rate values come from its own " +
                          "BepInEx/config/zone.xenon.potassium.cfg, and CRIX's adaptive CPU savings for busy lobbies (fewer detailed players and slower cosmetic updates when " +
                          "frames run late) are off while it is installed. For big lobbies, either remove Potassium.dll so CRIX applies them, or lower Potassium's " +
                          "'Maximum detailed player count' and cosmetic distances in its config.");
            return lines;
        }
    }
}

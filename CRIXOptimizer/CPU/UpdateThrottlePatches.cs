// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (TextureTransitionPatch, VoiceLoudnessReactorPatch, ProximityPatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Runtime.CompilerServices;
using CRIXOptimizer.Core;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Throttles for purely visual per-frame updates (derived from Potassium 1.0.0's TextureTransitionPatch,
    /// VoiceLoudnessReactorPatch and ProximityPatch).
    ///
    /// Changes from Potassium:
    /// - frame-staggered instead of per-instance timers: same work saved, but spread evenly across frames;
    /// - no ConditionalWeakTable lookup per call (phase comes from the object's identity hash);
    /// - the rate never drops below the configured value (interval rounds down);
    /// - rates change live; a throwing prefix disables itself and the game's code runs.
    /// </summary>
    internal static class UpdateThrottlePatches
    {
        public const string TextureTransitionId = "Throttle.TextureTransitions";
        public const string VoiceVisualId = "Throttle.VoiceReactiveVisuals";
        public const string ProximityId = "Throttle.ProximityReactors";

        // Counters are main-thread only; read by diagnostics once per second.
        public static long TextureTransitionCalls, TextureTransitionSkipped;
        public static long VoiceCalls, VoiceSkipped;
        public static long ProximityCalls, ProximitySkipped;

        private static Func<object, float> voiceAttack;
        private static Func<object, float> voiceDecay;
        private static bool textureFaulted, voiceFaulted, proximityFaulted;

        public static void Apply(PatchSet patches, GameTypes types)
        {
            Type textureManager = types.Find("GorillaTag.TextureTransitionerManager", "TextureTransitionerManager");
            patches.TryPatch(TextureTransitionId, Accessors.FindMethod(textureManager, "LateUpdate", 0),
                prefix: Method(nameof(TextureTransitionPrefix)));

            Type voiceReactor = types.Find("VoiceLoudnessReactor", "GorillaTag.VoiceLoudnessReactor");
            voiceAttack = Accessors.Getter<float>(voiceReactor, "attack");
            voiceDecay = Accessors.Getter<float>(voiceReactor, "decay");
            if (voiceReactor != null && (voiceAttack == null || voiceDecay == null))
            {
                // Without the smoothing fields we cannot tell which reactors are safe to throttle.
                patches.MarkUnavailable(VoiceVisualId, "VoiceLoudnessReactor.attack/decay not found; throttling would be unsafe");
            }
            else
            {
                patches.TryPatch(VoiceVisualId, Accessors.FindMethod(voiceReactor, "Update", 0),
                    prefix: Method(nameof(VoicePrefix)));
            }

            Type proximityReactor = types.Find("GorillaTag.Cosmetics.EnvironmentProximityReactor", "EnvironmentProximityReactor");
            patches.TryPatch(ProximityId, Accessors.FindMethod(proximityReactor, "Update", 0),
                prefix: Method(nameof(ProximityPrefix)));
        }

        private static System.Reflection.MethodInfo Method(string name) =>
            typeof(UpdateThrottlePatches).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        private static bool TextureTransitionPrefix()
        {
            int interval = Live.TextureTransitionInterval;
            if (interval <= 1 || Live.Suspended || textureFaulted)
                return true;
            try
            {
                TextureTransitionCalls++;
                // A single manager: run on every Nth frame.
                if (ThrottleMath.IsInPhase(FrameClock.Current, interval, 0))
                    return true;
                TextureTransitionSkipped++;
                return false;
            }
            catch (Exception e)
            {
                textureFaulted = true;
                PatchGuard.Fault(TextureTransitionId, e);
                return true;
            }
        }

        private static bool VoicePrefix(object __instance)
        {
            int interval = Live.VoiceVisualInterval;
            if (interval <= 1 || Live.Suspended || voiceFaulted)
                return true;
            try
            {
                // Reactors with attack/decay smoothing integrate over time; throttling them would change
                // their behaviour, so they always run (same rule as Potassium).
                if (voiceAttack(__instance) > 0f || voiceDecay(__instance) > 0f)
                    return true;
                VoiceCalls++;
                if (ThrottleMath.IsInPhase(FrameClock.Current, interval, RuntimeHelpers.GetHashCode(__instance)))
                    return true;
                VoiceSkipped++;
                return false;
            }
            catch (Exception e)
            {
                voiceFaulted = true;
                PatchGuard.Fault(VoiceVisualId, e);
                return true;
            }
        }

        private static bool ProximityPrefix(object __instance)
        {
            int interval = Live.ProximityInterval;
            if (interval <= 1 || Live.Suspended || proximityFaulted)
                return true;
            try
            {
                ProximityCalls++;
                if (ThrottleMath.IsInPhase(FrameClock.Current, interval, RuntimeHelpers.GetHashCode(__instance)))
                    return true;
                ProximitySkipped++;
                return false;
            }
            catch (Exception e)
            {
                proximityFaulted = true;
                PatchGuard.Fault(ProximityId, e);
                return true;
            }
        }
    }
}

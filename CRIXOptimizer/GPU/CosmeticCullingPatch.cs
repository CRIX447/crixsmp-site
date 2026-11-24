// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (CosmeticsThrottlerPatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.GPU
{
    /// <summary>
    /// Derived from Potassium 1.0.0 CosmeticsThrottlerPatch: tightens the game's own distant-cosmetic
    /// culling for remote players (local rendering only; networking and what others see are unaffected).
    ///
    /// Changes from Potassium: the game's original values are captured per instance and restored when the
    /// feature is turned off or CRIX unloads; new values apply live to existing instances; instances created
    /// before the plugin loaded are picked up once via a scene search.
    /// </summary>
    internal static class CosmeticCullingPatch
    {
        public const string Id = "Visual.DistantCosmeticCulling";

        private sealed class Originals
        {
            public float DrawAllDistance;
            public float MaxDrawDistance;
            public int DrawAllCount;
            public int DrawMaxCount;
        }

        private static readonly ConditionalWeakTable<object, Originals> originals = new ConditionalWeakTable<object, Originals>();
        private static readonly List<WeakReference> instances = new List<WeakReference>();

        private static Func<object, float> getDrawAllDistance, getMaxDrawDistance;
        private static Action<object, float> setDrawAllDistance, setMaxDrawDistance;
        private static Func<object, int> getDrawAllCount, getDrawMaxCount;
        private static Action<object, int> setDrawAllCount, setDrawMaxCount;
        private static OptimizationSettings current;
        private static bool faulted;

        public static Type ThrottlerType { get; private set; }
        public static int TrackedInstances => instances.Count;

        public static void Apply(PatchSet patches, GameTypes types)
        {
            ThrottlerType = types.Find("GorillaNetworking.CosmeticsThrottler", "CosmeticsThrottler");
            Type t = ThrottlerType;
            getDrawAllDistance = Accessors.Getter<float>(t, "DrawAllDistance");
            getMaxDrawDistance = Accessors.Getter<float>(t, "MaxDrawDistance");
            getDrawAllCount = Accessors.Getter<int>(t, "DrawAllCount");
            getDrawMaxCount = Accessors.Getter<int>(t, "DrawMaxCount");
            setDrawAllDistance = Accessors.Setter<float>(t, "DrawAllDistance");
            setMaxDrawDistance = Accessors.Setter<float>(t, "MaxDrawDistance");
            setDrawAllCount = Accessors.Setter<int>(t, "DrawAllCount");
            setDrawMaxCount = Accessors.Setter<int>(t, "DrawMaxCount");

            if (t == null)
            {
                patches.MarkUnavailable(Id, "CosmeticsThrottler not found");
                return;
            }
            if (getDrawAllDistance == null || getMaxDrawDistance == null || getDrawAllCount == null || getDrawMaxCount == null ||
                setDrawAllDistance == null || setMaxDrawDistance == null || setDrawAllCount == null || setDrawMaxCount == null)
            {
                patches.MarkUnavailable(Id, "CosmeticsThrottler distance/count members not found");
                return;
            }

            patches.TryPatch(Id, Accessors.FindMethod(t, "Awake", 0),
                postfix: typeof(CosmeticCullingPatch).GetMethod(nameof(AwakePostfix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        }

        private static void AwakePostfix(object __instance)
        {
            try
            {
                Register(__instance);
            }
            catch (Exception e)
            {
                faulted = true;
                PatchGuard.Fault(Id, e);
            }
        }

        /// <summary>Starts tracking an instance (captures its original values) and applies current settings.</summary>
        public static void Register(object instance)
        {
            if (instance == null || faulted || getDrawAllDistance == null)
                return;
            if (originals.TryGetValue(instance, out _))
                return;
            originals.Add(instance, new Originals
            {
                DrawAllDistance = getDrawAllDistance(instance),
                MaxDrawDistance = getMaxDrawDistance(instance),
                DrawAllCount = getDrawAllCount(instance),
                DrawMaxCount = getDrawMaxCount(instance),
            });
            instances.Add(new WeakReference(instance));
            ApplyTo(instance);
        }

        /// <summary>Re-applies (or restores) every tracked instance. Called when the profile changes.</summary>
        public static void Refresh(OptimizationSettings settings)
        {
            current = settings;
            if (faulted)
                return;
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                object instance = instances[i].Target;
                if (!UnityObjects.IsAlive(instance))
                {
                    instances.RemoveAt(i);
                    continue;
                }
                try
                {
                    ApplyTo(instance);
                }
                catch (Exception e)
                {
                    faulted = true;
                    PatchGuard.Fault(Id, e);
                    return;
                }
            }
        }

        public static void RestoreAll()
        {
            OptimizationSettings saved = current;
            current = null;
            Refresh(null);
            current = saved;
        }

        private static void ApplyTo(object instance)
        {
            if (!originals.TryGetValue(instance, out Originals o))
                return;
            OptimizationSettings s = current;
            if (s == null || !s.ReduceDistantCosmetics || !Live.ReduceDistantCosmetics || Live.Suspended)
            {
                setDrawAllDistance(instance, o.DrawAllDistance);
                setMaxDrawDistance(instance, o.MaxDrawDistance);
                setDrawAllCount(instance, o.DrawAllCount);
                setDrawMaxCount(instance, o.DrawMaxCount);
                return;
            }

            float drawAll = Math.Max(0f, s.CosmeticFullDetailDistance);
            int drawAllCount = Math.Max(0, s.CosmeticFullDetailCount);
            setDrawAllDistance(instance, drawAll);
            setMaxDrawDistance(instance, Math.Max(drawAll, s.CosmeticMaximumDistance));
            setDrawAllCount(instance, drawAllCount);
            setDrawMaxCount(instance, Math.Max(drawAllCount, s.CosmeticMaximumCount));
        }
    }
}

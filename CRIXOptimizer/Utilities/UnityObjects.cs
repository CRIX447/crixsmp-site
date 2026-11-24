// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Runtime.CompilerServices;
using UnityEngine;

namespace CRIXOptimizer.Utilities
{
    /// <summary>
    /// Null/liveness checks that respect Unity's "destroyed object" semantics. Patches hold game objects as
    /// plain object references (no compile-time game types), so the Unity-aware comparison must be explicit.
    /// Engine calls live in separate non-inlined methods so they are only compiled when a Unity object is
    /// actually passed in.
    /// </summary>
    internal static class UnityObjects
    {
        public static bool IsAlive(object obj)
        {
            if (obj == null)
                return false;
            if (obj is Object unityObject)
                return IsUnityObjectAlive(unityObject);
            return true;
        }

        public static bool IsActiveInHierarchy(object obj)
        {
            if (obj is Component component)
                return IsComponentActive(component);
            return obj != null;
        }

        public static void DetachFromParent(object obj)
        {
            if (obj is Component component)
                Detach(component);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsUnityObjectAlive(Object unityObject) => unityObject != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsComponentActive(Component component) => component != null && component.gameObject.activeInHierarchy;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Detach(Component component)
        {
            if (component != null)
                component.transform.parent = null;
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CRIXOptimizer.Utilities
{
    /// <summary>
    /// Drop-in replacements for List&lt;T&gt;.Contains used by the custom-map sanitizer transpiler.
    /// Each call site keeps the game's own list; we build a HashSet mirror of it and rebuild the mirror
    /// whenever the list changes (count or internal version), so results are identical to List.Contains.
    /// </summary>
    internal static class FastLookups
    {
        private sealed class Mirror<T>
        {
            public List<T> Source;
            public int Count;
            public int Version;
            public HashSet<T> Set;
        }

        private static class Cache<T>
        {
            public static readonly object Lock = new object();
            public static readonly List<Mirror<T>> Mirrors = new List<Mirror<T>>(4);
            // List<T>._version exists on Mono and .NET; if it does not, Count alone is used.
            public static readonly Func<List<T>, int> VersionOf = BuildVersionReader();

            private static Func<List<T>, int> BuildVersionReader()
            {
                FieldInfo field = typeof(List<T>).GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(int))
                    return null;
                try
                {
                    var p = System.Linq.Expressions.Expression.Parameter(typeof(List<T>), "list");
                    return System.Linq.Expressions.Expression.Lambda<Func<List<T>, int>>(
                        System.Linq.Expressions.Expression.Field(p, field), p).Compile();
                }
                catch
                {
                    return list => (int)field.GetValue(list);
                }
            }
        }

        public static long Lookups;
        public static long Rebuilds;

        public static bool ListContainsType(List<Type> list, Type item) => Contains(list, item);
        public static bool ListContainsInt(List<int> list, int item) => Contains(list, item);
        public static bool ListContainsString(List<string> list, string item) => Contains(list, item);

        public static bool Contains<T>(List<T> list, T item)
        {
            if (list == null)
                throw new NullReferenceException(); // same failure the original call would produce
            Lookups++;
            lock (Cache<T>.Lock)
            {
                Mirror<T> mirror = Find(list);
                int version = Cache<T>.VersionOf != null ? Cache<T>.VersionOf(list) : 0;
                if (mirror == null)
                {
                    mirror = new Mirror<T> { Source = list };
                    if (Cache<T>.Mirrors.Count >= 16)
                        Cache<T>.Mirrors.RemoveAt(0);
                    Cache<T>.Mirrors.Add(mirror);
                    Rebuild(mirror, version);
                }
                else if (mirror.Count != list.Count || mirror.Version != version)
                {
                    Rebuild(mirror, version);
                }
                return mirror.Set.Contains(item);
            }
        }

        private static Mirror<T> Find<T>(List<T> list)
        {
            List<Mirror<T>> mirrors = Cache<T>.Mirrors;
            for (int i = 0; i < mirrors.Count; i++)
                if (ReferenceEquals(mirrors[i].Source, list))
                    return mirrors[i];
            return null;
        }

        private static void Rebuild<T>(Mirror<T> mirror, int version)
        {
            Rebuilds++;
            // EqualityComparer<T>.Default is exactly what List<T>.Contains uses (ordinal for strings).
            mirror.Set = new HashSet<T>(mirror.Source, EqualityComparer<T>.Default);
            mirror.Count = mirror.Source.Count;
            mirror.Version = version;
        }
    }
}

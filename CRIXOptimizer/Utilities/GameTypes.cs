// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CRIXOptimizer.Utilities
{
    /// <summary>
    /// Name-based index of the game's types. CRIX never links against Assembly-CSharp.dll; every game type
    /// is looked up here once at startup. A missing type disables only the feature that needs it, so a game
    /// update that renames something cannot stop the plugin (or its other features) from loading.
    /// </summary>
    internal sealed class GameTypes
    {
        public static readonly string[] DefaultAssemblyNames = { "Assembly-CSharp", "Assembly-CSharp-firstpass" };

        private readonly Dictionary<string, Type> byFullName = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> bySimpleName = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> indexedAssemblies = new List<string>();

        public IReadOnlyList<string> IndexedAssemblies => indexedAssemblies;
        public int TypeCount => byFullName.Count;
        public bool IsEmpty => byFullName.Count == 0;

        public static GameTypes FromLoadedAssemblies(IEnumerable<string> assemblyNames = null)
        {
            var wanted = new HashSet<string>(assemblyNames ?? DefaultAssemblyNames, StringComparer.OrdinalIgnoreCase);
            var assemblies = new List<Assembly>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }
                if (wanted.Contains(name))
                    assemblies.Add(assembly);
            }
            return FromAssemblies(assemblies);
        }

        public static GameTypes FromAssemblies(IEnumerable<Assembly> assemblies)
        {
            var types = new GameTypes();
            foreach (Assembly assembly in assemblies)
                types.Index(assembly);
            return types;
        }

        private void Index(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            catch
            {
                return;
            }

            indexedAssemblies.Add(assembly.GetName().Name);
            foreach (Type type in types)
            {
                if (type == null || type.FullName == null)
                    continue;
                if (!byFullName.ContainsKey(type.FullName))
                    byFullName.Add(type.FullName, type);

                string simple = type.Name;
                if (bySimpleName.TryGetValue(simple, out Type existing))
                {
                    if (existing != type)
                        ambiguous.Add(simple);
                }
                else
                {
                    bySimpleName.Add(simple, type);
                }
            }
        }

        /// <summary>
        /// Finds the first candidate that matches. Candidates with a namespace are matched exactly; a bare name
        /// matches a unique simple type name in any namespace (game updates move types between namespaces).
        /// </summary>
        public Type Find(params string[] candidates)
        {
            foreach (string candidate in candidates)
                if (byFullName.TryGetValue(candidate, out Type type))
                    return type;

            foreach (string candidate in candidates)
            {
                int dot = candidate.LastIndexOf('.');
                string simple = dot >= 0 ? candidate.Substring(dot + 1) : candidate;
                if (!ambiguous.Contains(simple) && bySimpleName.TryGetValue(simple, out Type type))
                    return type;
            }
            return null;
        }
    }
}

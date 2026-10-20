// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CRIXOptimizer.Core
{
    public enum ModuleState
    {
        /// <summary>Running.</summary>
        Active,
        /// <summary>Turned off by configuration or the active profile.</summary>
        Disabled,
        /// <summary>Required game code or engine feature is not present (e.g. a game update renamed a method).</summary>
        Unavailable,
        /// <summary>Handled by another installed plugin (e.g. Potassium) to avoid duplicate patches.</summary>
        Deferred,
        /// <summary>Threw repeatedly and was shut off for this session.</summary>
        Faulted,
    }

    /// <summary>Status registry for modules and individual patches; drives the dashboard and system report.</summary>
    public sealed class StatusBoard
    {
        public sealed class Entry
        {
            public string Name;
            public string Group;
            public ModuleState State;
            public string Detail;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, Entry> byName = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public IReadOnlyList<Entry> Entries => entries;
        public int Version { get; private set; }

        public void Set(string group, string name, ModuleState state, string detail = null)
        {
            if (!byName.TryGetValue(name, out Entry entry))
            {
                entry = new Entry { Name = name, Group = group };
                byName.Add(name, entry);
                entries.Add(entry);
            }
            if (entry.State == state && entry.Detail == detail && entry.Group == group)
                return;
            entry.Group = group;
            entry.State = state;
            entry.Detail = detail;
            Version++;
        }

        public bool TryGet(string name, out Entry entry) => byName.TryGetValue(name, out entry);

        public int Count(ModuleState state)
        {
            int n = 0;
            foreach (Entry e in entries)
                if (e.State == state)
                    n++;
            return n;
        }
    }

    /// <summary>Reference-identity comparer (Unity objects override == and Equals; patches need identity).</summary>
    public sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

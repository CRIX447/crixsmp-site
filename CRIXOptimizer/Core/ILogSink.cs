// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Core
{
    /// <summary>Logging abstraction so core logic stays free of BepInEx/Unity and can be unit-tested.</summary>
    public interface ILogSink
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Debug(string message);
    }

    /// <summary>Discards everything. Used as a safe default before the plugin wires a real sink.</summary>
    public sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new NullLogSink();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Debug(string message) { }
    }

    /// <summary>Collects messages in memory (tests and system reports).</summary>
    public sealed class MemoryLogSink : ILogSink
    {
        public readonly List<string> Lines = new List<string>();
        public void Info(string message) => Lines.Add("INFO " + message);
        public void Warn(string message) => Lines.Add("WARN " + message);
        public void Error(string message) => Lines.Add("ERROR " + message);
        public void Debug(string message) => Lines.Add("DEBUG " + message);

        public bool Contains(string fragment)
        {
            foreach (string line in Lines)
                if (line.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }

    /// <summary>Emits a given key at most once; prevents log spam from repeated conditions.</summary>
    public sealed class OnceLogger
    {
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly ILogSink sink;

        public OnceLogger(ILogSink sink) { this.sink = sink ?? NullLogSink.Instance; }

        public void Warn(string key, string message)
        {
            if (seen.Add(key))
                sink.Warn(message);
        }

        public void Info(string key, string message)
        {
            if (seen.Add(key))
                sink.Info(message);
        }

        public void Reset(string key) => seen.Remove(key);
    }
}

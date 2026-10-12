// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Concurrent;
using BepInEx.Logging;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Utilities
{
    /// <summary>ILogSink backed by the plugin's BepInEx log source.</summary>
    internal sealed class BepInExLogSink : ILogSink
    {
        private readonly ManualLogSource source;

        public BepInExLogSink(ManualLogSource source) { this.source = source; }

        public bool Verbose { get; set; }

        public void Info(string message) => source.LogInfo(message);
        public void Warn(string message) => source.LogWarning(message);
        public void Error(string message) => source.LogError(message);

        public void Debug(string message)
        {
            if (Verbose)
                source.LogDebug(message);
        }
    }

    /// <summary>Hands results from background threads back to Unity's main thread (drained in Update).</summary>
    internal sealed class MainThreadQueue
    {
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private readonly ILogSink log;

        public MainThreadQueue(ILogSink log) { this.log = log; }

        public void Post(Action action)
        {
            if (action != null)
                queue.Enqueue(action);
        }

        public void Drain(int maxPerFrame = 8)
        {
            for (int i = 0; i < maxPerFrame && queue.TryDequeue(out Action action); i++)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Warn("Deferred task failed: " + e.Message);
                }
            }
        }
    }
}

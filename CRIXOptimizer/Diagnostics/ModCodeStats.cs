// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// What the opt-in timing of other mods' code measured (no Unity or Harmony dependency, unit tested): per method
    /// the calls, total time, longest call and every slow call (<see cref="SlowMs"/> or more), so a hitch can be named
    /// ("OculusReportMenu: Menu.Update, 74 ms") and a method that is slow on a timer stands out by its spacing.
    /// Main thread only (the timed methods are Unity callbacks and Harmony patches running on it); calls from other
    /// threads are not recorded.
    /// </summary>
    internal sealed class ModCodeStats
    {
        public const double SlowMs = 20, NoteMs = 5;
        private const int MaxSlowTimes = 512;

        internal sealed class Method
        {
            public string Mod = "", Name = "";
            public long Calls;
            public double TotalMs, MaxMs;
            public readonly List<double> SlowTimes = new List<double>();
            public readonly List<float> SlowMsList = new List<float>();
            public string Label => Mod + ": " + Name;
        }

        private readonly List<Method> methods = new List<Method>();
        // Calls of NoteMs or more in the most recent frames, for naming the method behind a hitch.
        private readonly int[] recentFrame = new int[64];
        private readonly int[] recentMethod = new int[64];
        private readonly float[] recentMs = new float[64];
        private int recentNext;

        public int Count => methods.Count;
        public IReadOnlyList<Method> Methods => methods;

        public int Register(string mod, string name)
        {
            methods.Add(new Method { Mod = mod ?? "?", Name = name ?? "?" });
            return methods.Count - 1;
        }

        /// <summary>One call of method id that took ms milliseconds, in frame number frame at time now (seconds).</summary>
        public void Record(int id, double ms, int frame, double now)
        {
            if (id < 0 || id >= methods.Count)
                return;
            Method m = methods[id];
            m.Calls++;
            m.TotalMs += ms;
            if (ms > m.MaxMs)
                m.MaxMs = ms;
            if (ms < NoteMs)
                return;
            recentFrame[recentNext] = frame;
            recentMethod[recentNext] = id;
            recentMs[recentNext] = (float)ms;
            recentNext = (recentNext + 1) % recentFrame.Length;
            if (ms >= SlowMs && m.SlowTimes.Count < MaxSlowTimes)
            {
                m.SlowTimes.Add(now);
                m.SlowMsList.Add((float)ms);
            }
        }

        /// <summary>The longest call recorded in the given frame (NoteMs or more), if any.</summary>
        public bool Slowest(int frame, out string label, out double ms)
        {
            label = "";
            ms = 0;
            int best = -1;
            for (int i = 0; i < recentFrame.Length; i++)
            {
                if (recentFrame[i] != frame || recentMs[i] <= 0)
                    continue;
                if (best < 0 || recentMs[i] > recentMs[best])
                    best = i;
            }
            if (best < 0)
                return false;
            label = methods[recentMethod[best]].Label;
            ms = recentMs[best];
            return true;
        }

        /// <summary>The method whose slow calls come back every <paramref name="period"/> seconds (at least minCount of
        /// them, spaced within the repeating-hitch tolerance for most gaps), or null.</summary>
        public Method OnTimer(double period, int minCount)
        {
            Method best = null;
            int bestNear = 0;
            double tol = Math.Max(0.15, period * 0.02);
            foreach (Method m in methods)
            {
                if (m.SlowTimes.Count < minCount)
                    continue;
                int near = 0;
                for (int i = 1; i < m.SlowTimes.Count; i++)
                {
                    double gap = m.SlowTimes[i] - m.SlowTimes[i - 1];
                    double k = Math.Round(gap / period);
                    if (k >= 1 && Math.Abs(gap - k * period) <= tol * k)
                        near++;
                }
                if (near * 10 >= (m.SlowTimes.Count - 1) * 7 && near > bestNear)
                {
                    best = m;
                    bestNear = near;
                }
            }
            return best;
        }

        /// <summary>Text lines for the notes: the methods with slow calls, most slow calls first (at most max).</summary>
        public List<string> Describe(int max)
        {
            var slow = methods.FindAll(m => m.SlowTimes.Count > 0);
            slow.Sort((a, b) => b.SlowTimes.Count != a.SlowTimes.Count ? b.SlowTimes.Count.CompareTo(a.SlowTimes.Count) : b.MaxMs.CompareTo(a.MaxMs));
            var lines = new List<string>();
            foreach (Method m in slow)
            {
                if (lines.Count >= max)
                    break;
                var ms = new List<float>(m.SlowMsList);
                ms.Sort();
                string spacing = "";
                if (m.SlowTimes.Count >= 4)
                {
                    var gaps = new List<double>();
                    for (int i = 1; i < m.SlowTimes.Count; i++)
                        gaps.Add(m.SlowTimes[i] - m.SlowTimes[i - 1]);
                    gaps.Sort();
                    double median = gaps[gaps.Count / 2];
                    int near = gaps.FindAll(g => Math.Abs(g - median) <= Math.Max(0.15, median * 0.02)).Count;
                    if (near * 10 >= gaps.Count * 7)
                        spacing = string.Format(CultureInfo.InvariantCulture, ", every {0:0.0} s", median);
                }
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1} slow calls ({2:0}+ ms), typically {3:0} ms, longest {4:0} ms{5}; {6} calls in all, {7:0.000} ms per call on average",
                    m.Label, m.SlowTimes.Count, SlowMs, ms[ms.Count / 2], m.MaxMs, spacing, m.Calls, m.Calls > 0 ? m.TotalMs / m.Calls : 0));
            }
            return lines;
        }
    }
}

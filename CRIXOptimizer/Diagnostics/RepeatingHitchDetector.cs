// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Finds big hitches that come back on a fixed timer (no Unity dependency, unit tested). A hitch every N seconds
    /// is a timer in the game, a mod, the VR runtime or a program on the PC, which is worth naming on its own: it
    /// recurs for the whole session, and on a 15 s timer it also kept the notes' startup phase from ever ending.
    ///
    /// The period comes from the differences between hitch times (a timer shows up as a peak at its period), and
    /// is checked by walking the timeline from each hitch, one period at a time: a slot counts when a hitch lies
    /// within <see cref="Tolerance"/> of it. Walking re-anchors on each match, so a timer that drifts by a frame per
    /// round (a coroutine waiting N seconds) still matches. A pattern needs <see cref="MinRepeats"/> matches over at
    /// least <see cref="MinSpanSeconds"/>, covering at least <see cref="MinCoverage"/> of the slots between its first and
    /// last match and clearly more than random hitches at the session's hitch rate would (a burst of loading hitches
    /// can line up with some period for a minute).
    /// </summary>
    internal sealed class RepeatingHitchDetector
    {
        public const double MinPeriod = 2, MaxPeriod = 120;
        public const int MinRepeats = 10;
        public const double MinSpanSeconds = 120;
        public const double MinCoverage = 0.7;
        /// <summary>A slot on the timer that has gone this many periods without a hitch ends the walk.</summary>
        private const int MaxMissesInRow = 3;
        private const int MaxHitches = 4000;
        private const double Bin = 0.1;

        internal sealed class Pattern
        {
            public double Period;
            public double First, Last;
            public int Matched, Slots;
            public float TypicalMs, MinMs, MaxMs;
            /// <summary>The frame phase most matched hitches spent most time in ("" = not measured), and how many.</summary>
            public string TopPhase = "";
            public int TopPhaseCount, PhaseMeasured;
            public double Coverage => Slots > 0 ? (double)Matched / Slots : 0;
        }

        private readonly List<double> times = new List<double>();
        private readonly List<float> durations = new List<float>();
        private readonly List<string> phases = new List<string>();
        private volatile Pattern current;

        /// <summary>The strongest repeating pattern found by the last <see cref="Update"/> or <see cref="Publish"/>, or null.</summary>
        public Pattern Current => current;

        public int Count => times.Count;

        /// <remarks>A timer hitch is stamped at the frame after it: its own length (about &#177;20 ms) and frame alignment (a frame)
        /// spread it by a few tens of milliseconds; a narrow window keeps random hitches from lining up.</remarks>
        public static double Tolerance(double period) => Math.Max(0.08, 0.005 * period);

        /// <summary>A big hitch at time t (seconds, increasing) and the frame phase it spent most time in ("" = unknown).</summary>
        public void Add(double t, float ms, string phase = "")
        {
            if (times.Count > 0 && t < times[times.Count - 1])
                return;
            if (times.Count >= MaxHitches)
            {
                times.RemoveRange(0, MaxHitches / 4);
                durations.RemoveRange(0, MaxHitches / 4);
                phases.RemoveRange(0, MaxHitches / 4);
            }
            times.Add(t);
            durations.Add(ms);
            phases.Add(phase ?? "");
        }

        /// <summary>True when a hitch at t falls on the current pattern's timer (call before <see cref="Add"/>).</summary>
        public bool Matches(double t)
        {
            Pattern p = Current;
            if (p == null)
                return false;
            double k = Math.Round((t - p.Last) / p.Period);
            if (k < 0 || k > MaxMissesInRow + 3)
                return false;
            return Math.Abs(t - (p.Last + k * p.Period)) <= Tolerance(p.Period);
        }

        /// <summary>Recomputes <see cref="Current"/> on this thread (tests; the runtime uses <see cref="Snapshot"/>).</summary>
        public void Update() => current = Snapshot()();

        /// <summary>Main thread: copies the hitches (microseconds) and returns the search, which can run on any thread
        /// (tens of milliseconds for a long session); hand its result to <see cref="Publish"/>.</summary>
        public Func<Pattern> Snapshot()
        {
            double[] t = times.ToArray();
            float[] d = durations.ToArray();
            string[] p = phases.ToArray();
            return () => Find(t, d, p);
        }

        public void Publish(Pattern pattern) => current = pattern;

        private static Pattern Find(double[] times, float[] durations, string[] phases)
        {
            int n = times.Length;
            if (n < MinRepeats)
                return null;
            var hist = new int[(int)(MaxPeriod / Bin) + 2];
            var histSum = new double[hist.Length];
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double d = times[j] - times[i];
                    if (d > MaxPeriod)
                        break;
                    if (d < MinPeriod * 0.9)
                        continue;
                    int b = (int)(d / Bin);
                    hist[b]++;
                    histSum[b] += d;
                }
            }

            // The strongest peaks (three bins wide), each tried at its period and at a half and a third of it, so
            // a timer whose every second round was missed still yields its real period.
            var candidates = new List<double>();
            var used = new bool[hist.Length];
            for (int pick = 0; pick < 5; pick++)
            {
                int best = -1, bestCount = 0;
                for (int b = 1; b < hist.Length - 1; b++)
                {
                    if (used[b])
                        continue;
                    int c = hist[b - 1] + hist[b] + hist[b + 1];
                    if (c > bestCount)
                    {
                        bestCount = c;
                        best = b;
                    }
                }
                if (best < 0 || bestCount < MinRepeats / 2)
                    break;
                double period = (histSum[best - 1] + histSum[best] + histSum[best + 1]) / bestCount;
                for (int b = Math.Max(0, best - 2); b <= Math.Min(hist.Length - 1, best + 2); b++)
                    used[b] = true;
                foreach (double div in new[] { 1.0, 2.0, 3.0 })
                {
                    if (period / div >= MinPeriod)
                        candidates.Add(period / div);
                }
            }

            // Chance that a slot holds a random hitch at the average hitch rate.
            double rate = (n - 1) / Math.Max(1.0, times[n - 1] - times[0]);
            Pattern bestPattern = null;
            foreach (double period in candidates)
            {
                Pattern p = Walk(times, durations, phases, period);
                if (p == null || p.Matched < MinRepeats || p.Last - p.First < MinSpanSeconds)
                    continue;
                double chance = 1 - Math.Exp(-rate * 2 * Tolerance(period));
                if (p.Coverage < Math.Max(MinCoverage, chance + 0.4))
                    continue;
                if (bestPattern == null || p.Matched > bestPattern.Matched ||
                    (p.Matched == bestPattern.Matched && p.Period < bestPattern.Period))
                    bestPattern = p;
            }
            return bestPattern;
        }

        /// <summary>The longest chain of hitches on a timer with roughly this period.</summary>
        private static Pattern Walk(double[] times, float[] durations, string[] phases, double period)
        {
            int n = times.Length;
            double tol = Tolerance(period);
            var inChain = new bool[n];
            List<int> best = null;
            int bestSlots = 0;
            var chain = new List<int>();
            for (int s = 0; s < n; s++)
            {
                if (inChain[s])
                    continue; // a chain from a hitch already in one is part of that chain
                chain.Clear();
                chain.Add(s);
                inChain[s] = true;
                int slots = 1, gap = 1, misses = 0;
                double anchor = times[s];
                double end = times[n - 1] + tol;
                while (true)
                {
                    double expected = anchor + gap * period;
                    if (expected > end)
                        break;
                    int k = Nearest(times, expected);
                    if (k > chain[chain.Count - 1] && Math.Abs(times[k] - expected) <= tol)
                    {
                        chain.Add(k);
                        inChain[k] = true;
                        slots += gap;
                        anchor = times[k];
                        gap = 1;
                        misses = 0;
                    }
                    else
                    {
                        if (++misses > MaxMissesInRow)
                            break;
                        gap++;
                    }
                }
                if (best == null || chain.Count > best.Count)
                {
                    best = new List<int>(chain);
                    bestSlots = slots;
                }
            }
            if (best == null || best.Count < 2)
                return null;

            // Period: the average spacing of the matched hitches (slot steps, not hitch count).
            double first = times[best[0]], last = times[best[best.Count - 1]];
            var p = new Pattern
            {
                Period = bestSlots > 1 ? (last - first) / (bestSlots - 1) : period,
                First = first,
                Last = last,
                Matched = best.Count,
                Slots = bestSlots,
            };
            var ms = new List<float>(best.Count);
            var phaseCounts = new Dictionary<string, int>();
            foreach (int i in best)
            {
                ms.Add(durations[i]);
                if (phases[i].Length == 0)
                    continue;
                p.PhaseMeasured++;
                phaseCounts.TryGetValue(phases[i], out int c);
                phaseCounts[phases[i]] = c + 1;
            }
            ms.Sort();
            p.MinMs = ms[0];
            p.MaxMs = ms[ms.Count - 1];
            p.TypicalMs = ms[ms.Count / 2];
            foreach (var kv in phaseCounts)
            {
                if (kv.Value > p.TopPhaseCount || (kv.Value == p.TopPhaseCount && string.CompareOrdinal(kv.Key, p.TopPhase) < 0))
                {
                    p.TopPhase = kv.Key;
                    p.TopPhaseCount = kv.Value;
                }
            }
            return p;
        }

        private static int Nearest(double[] times, double t)
        {
            int lo = 0, hi = times.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] < t)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            if (lo > 0 && Math.Abs(times[lo - 1] - t) < Math.Abs(times[lo] - t))
                return lo - 1;
            return lo;
        }
    }
}

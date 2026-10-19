// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Per-minute history of the session (no Unity dependency, unit tested): frame rate, 1 % low, hitches, memory,
    /// garbage collections, profile, recording/streaming state and adaptive level. The live window in the system report
    /// covers only the last seconds; this shows whether the frame rate falls over a long session (for example while
    /// streaming) and what changed along with it.
    /// </summary>
    internal sealed class SessionTrend
    {
        public sealed class Minute
        {
            public int Index;
            public double AvgFps;
            public double Low1Fps;
            public int Hitches;
            public int BigHitches;
            public int Frames;
            public long HeapBytes = -1;
            public long UnityBytes = -1;
            public long GfxBytes = -1;
            public int Gcs;
            public string Profile = "";
            public bool Capturing;
            public int AdaptiveLevel;
            public bool VrActive;
            public double RefreshHz;
            /// <summary>The map (zone) most of the minute was spent in, "" when unknown, and that share of the minute.</summary>
            public string Zone = "";
            public double ZoneShare;
        }

        /// <summary>What the runtime knows at the end of a minute.</summary>
        public struct Context
        {
            public long HeapBytes;
            public long UnityBytes;
            public long GfxBytes;
            public int GcCount;
            public string Profile;
            public bool Capturing;
            public int AdaptiveLevel;
            public bool VrActive;
            public double RefreshHz;
            public string Zone;
            public double ZoneShare;
        }

        public const int MaxMinutes = 180;
        /// <summary>A minute counts for one map when at least this share of it was spent there.</summary>
        public const double SameMapShare = 0.8;
        private const double MinuteSeconds = 60.0;

        private readonly List<float> frameMs = new List<float>(8192);
        private readonly List<float> sortBuffer = new List<float>(8192);
        private double minuteStart = -1;
        private int nextIndex;
        private int lastGcCount = -1;

        public readonly List<Minute> Minutes = new List<Minute>();

        /// <summary>Per frame (main thread). Frames over 5 s (pauses, loading with the window minimised) are ignored.</summary>
        public void AddFrame(float dtSeconds, double now)
        {
            if (minuteStart < 0)
                minuteStart = now;
            if (dtSeconds > 0f && dtSeconds < 5f)
                frameMs.Add(dtSeconds * 1000f);
        }

        public bool MinuteDue(double now) => minuteStart >= 0 && now - minuteStart >= MinuteSeconds;

        /// <summary>Closes the current minute and starts the next. Returns the closed minute (null if it had no frames).</summary>
        public Minute CloseMinute(double now, Context ctx)
        {
            double elapsed = minuteStart >= 0 ? now - minuteStart : 0;
            minuteStart = now;
            int gcs = lastGcCount >= 0 && ctx.GcCount >= lastGcCount ? ctx.GcCount - lastGcCount : 0;
            lastGcCount = ctx.GcCount;
            if (frameMs.Count == 0 || elapsed <= 0)
            {
                frameMs.Clear();
                return null;
            }
            double sum = 0;
            foreach (float ms in frameMs)
                sum += ms;
            double budget = ctx.RefreshHz > 1 ? 1000.0 / ctx.RefreshHz : sum / frameMs.Count;
            int hitches = 0, big = 0;
            foreach (float ms in frameMs)
            {
                if (ms > budget * 2)
                    hitches++;
                if (ms > 70)
                    big++;
            }
            sortBuffer.Clear();
            sortBuffer.AddRange(frameMs);
            sortBuffer.Sort();
            float p99 = sortBuffer[Math.Min(sortBuffer.Count - 1, (int)Math.Ceiling(sortBuffer.Count * 0.99) - 1)];
            var m = new Minute
            {
                Index = nextIndex++,
                Frames = frameMs.Count,
                AvgFps = frameMs.Count / (sum / 1000.0),
                Low1Fps = p99 > 0 ? 1000.0 / p99 : 0,
                Hitches = hitches,
                BigHitches = big,
                HeapBytes = ctx.HeapBytes,
                UnityBytes = ctx.UnityBytes,
                GfxBytes = ctx.GfxBytes,
                Gcs = gcs,
                Profile = ctx.Profile ?? "",
                Capturing = ctx.Capturing,
                AdaptiveLevel = ctx.AdaptiveLevel,
                VrActive = ctx.VrActive,
                RefreshHz = ctx.RefreshHz,
                Zone = ctx.Zone ?? "",
                ZoneShare = ctx.ZoneShare,
            };
            frameMs.Clear();
            Minutes.Add(m);
            if (Minutes.Count > MaxMinutes)
                Minutes.RemoveAt(0);
            return m;
        }

        /// <summary>
        /// Looks for a frame rate that fell over the session: the first three usable minutes (headset on, after a two
        /// minute warm-up) against the last three. When the map of each minute is known, only minutes spent mostly in
        /// one map count, and the last three are compared with the first three in the same map: a heavier map is not a
        /// drop over time. Returns a description with what changed along with it, or null.
        /// </summary>
        public string FindSlowDrop()
        {
            var usable = Minutes.FindAll(m => m.VrActive && m.Index >= 2 && m.Frames >= 600 && (m.Zone.Length == 0 || m.ZoneShare >= SameMapShare));
            if (usable.Count < 8)
                return null;
            List<Minute> first = usable.GetRange(0, 3), last = usable.GetRange(usable.Count - 3, 3);
            string mapNow = MainZone(last);
            if (mapNow.Length > 0 && MainZone(first) != mapNow)
            {
                // Compare with the first minutes in the map played now, if there are enough of them before the last three.
                var sameMap = usable.FindAll(m => m.Zone == mapNow && m.Index < last[0].Index);
                if (sameMap.Count >= 3)
                {
                    first = sameMap.GetRange(0, 3);
                }
                else
                {
                    double startFps = Median(first, m => m.AvgFps), endFps = Median(last, m => m.AvgFps);
                    if (startFps <= 0 || startFps - endFps < 8 || (startFps - endFps) / startFps < 0.10)
                        return null;
                    return string.Format(CultureInfo.InvariantCulture,
                        "The frame rate is lower now ({0:0} FPS) than at the start ({1:0} FPS), but you are in a different map ({2} then, {3} now): " +
                        "that is the map, not a drop over time. The notes' per-map summary compares the maps.",
                        endFps, startFps, MainZone(first), mapNow);
                }
            }
            double baseFps = Median(first, m => m.AvgFps), nowFps = Median(last, m => m.AvgFps);
            if (baseFps <= 0 || baseFps - nowFps < 8 || (baseFps - nowFps) / baseFps < 0.10)
                return null;
            int span = last[2].Index - first[0].Index + 1;
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "Frame rate fell from about {0:0} to {1:0} FPS over {2} minutes (minutes {3}-{4} against {5}-{6}{7}).",
                baseFps, nowFps, span, first[0].Index + 1, first[2].Index + 1, last[0].Index + 1, last[2].Index + 1,
                mapNow.Length > 0 ? ", both in " + mapNow : ""));
            var causes = new List<string>();

            double refreshBefore = Median(first, m => m.RefreshHz), refreshNow = Median(last, m => m.RefreshHz);
            if (refreshBefore > 1 && refreshNow > 1 && Math.Abs(refreshBefore - refreshNow) > 1)
                causes.Add(string.Format(CultureInfo.InvariantCulture, "the headset refresh rate changed from {0:0} to {1:0} Hz", refreshBefore, refreshNow));
            if (!first[0].Capturing && last.TrueForAll(m => m.Capturing))
            {
                Minute started = usable.Find(m => m.Capturing);
                causes.Add($"it fell after recording or streaming started (OBS, Streamlabs or Discord; minute {started.Index + 1})");
            }
            string profileBefore = first[0].Profile, profileNow = last[2].Profile;
            if (profileBefore.Length > 0 && profileNow.Length > 0 && profileBefore != profileNow)
                causes.Add("the profile changed from " + profileBefore + " to " + profileNow);

            double heapGrowth = Growth(first, last, m => m.HeapBytes);
            if (heapGrowth >= 200.0 * 1024 * 1024)
                causes.Add($"managed memory grew by {Mb(heapGrowth)} (garbage collections take longer as it grows; check recently added mods)");
            double unityGrowth = Growth(first, last, m => m.UnityBytes);
            if (unityGrowth >= 500.0 * 1024 * 1024)
                causes.Add($"Unity memory grew by {Mb(unityGrowth)} (unused assets such as other players' cosmetics and visited maps are not freed while timed unloads are skipped; see [Memory] DeferredAssetUnload)");
            double gfxGrowth = Growth(first, last, m => m.GfxBytes);
            if (gfxGrowth >= 300.0 * 1024 * 1024)
                causes.Add($"graphics memory grew by {Mb(gfxGrowth)} (an 8 GB card shared with the headset stream encoder and OBS can run out)");

            double hitchesBefore = Median(first, m => m.Hitches), hitchesNow = Median(last, m => m.Hitches);
            if (hitchesNow >= 10 && hitchesNow >= hitchesBefore * 2)
                causes.Add(string.Format(CultureInfo.InvariantCulture, "hitches went from about {0:0} to {1:0} per minute", hitchesBefore, hitchesNow));
            double gcBefore = Median(first, m => m.Gcs), gcNow = Median(last, m => m.Gcs);
            if (gcNow >= 4 && gcNow >= gcBefore * 2)
                causes.Add(string.Format(CultureInfo.InvariantCulture, "garbage collections went from about {0:0} to {1:0} per minute", gcBefore, gcNow));
            if (last[2].AdaptiveLevel < first[0].AdaptiveLevel)
                causes.Add($"adaptive quality already lowered settings (level {first[0].AdaptiveLevel} to {last[2].AdaptiveLevel})");

            if (causes.Count > 0)
                sb.Append(" Along with it: ").Append(string.Join("; ", causes.ToArray())).Append('.');
            else
                sb.Append(" Nothing CRIX can see changed with it (same profile, no memory growth). Common causes outside the game: GPU or CPU heat " +
                          "(check temperatures), the GPU video encoder shared by OBS, the headset stream (Quest Link / Virtual Desktop) and NVIDIA Instant Replay, " +
                          "or OBS itself (View > Stats: rendering lag, encoding lag).");
            return sb.ToString();
        }

        /// <summary>One line per minute for the system report (the last <paramref name="count"/> minutes).</summary>
        public IEnumerable<string> Describe(int count)
        {
            for (int i = Math.Max(0, Minutes.Count - count); i < Minutes.Count; i++)
            {
                Minute m = Minutes[i];
                yield return string.Format(CultureInfo.InvariantCulture,
                    "min {0,3}: {1,5:0.0} FPS, 1% low {2,5:0.0}, hitches {3,3} (big {4}), GCs {5}, heap {6}, Unity {7}{8}, adaptive {9}, {10}{11}{12}{13}",
                    m.Index + 1, m.AvgFps, m.Low1Fps, m.Hitches, m.BigHitches, m.Gcs, Mb(m.HeapBytes), Mb(m.UnityBytes),
                    m.GfxBytes >= 0 ? ", gfx " + Mb(m.GfxBytes) : "", m.AdaptiveLevel, m.Profile,
                    m.Capturing ? ", recording/streaming" : "", m.VrActive ? "" : ", headset not active",
                    m.Zone.Length == 0 ? "" : m.ZoneShare >= SameMapShare ? ", map " + m.Zone : ", mostly " + m.Zone);
            }
        }

        /// <summary>The map most of these minutes were spent in ("" when unknown).</summary>
        private static string MainZone(List<Minute> minutes)
        {
            var counts = new Dictionary<string, int>();
            string best = "";
            int bestCount = 0;
            foreach (Minute m in minutes)
            {
                if (m.Zone.Length == 0)
                    continue;
                counts.TryGetValue(m.Zone, out int c);
                counts[m.Zone] = ++c;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = m.Zone;
                }
            }
            return best;
        }

        /// <summary>
        /// Which map most of the last minute was spent in: the zone whose frame time grew most since the last call, and
        /// its share of the total. <paramref name="previous"/> holds each zone's total at the last call (updated here).
        /// </summary>
        public static (string zone, double share) MinuteZone(IEnumerable<KeyValuePair<string, double>> zoneTotals, Dictionary<string, double> previous)
        {
            string best = "";
            double bestGain = 0, sum = 0;
            foreach (KeyValuePair<string, double> z in zoneTotals)
            {
                previous.TryGetValue(z.Key, out double before);
                double gain = Math.Max(0, z.Value - before);
                previous[z.Key] = z.Value;
                sum += gain;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = z.Key;
                }
            }
            return sum > 0 ? (best, bestGain / sum) : ("", 0);
        }

        private static double Median(List<Minute> minutes, Func<Minute, double> value)
        {
            var values = minutes.ConvertAll(m => value(m));
            values.Sort();
            return values.Count == 0 ? 0 : values[values.Count / 2];
        }

        /// <summary>Median of the last minutes minus median of the first (only when both sides report the value).</summary>
        private static double Growth(List<Minute> first, List<Minute> last, Func<Minute, long> value)
        {
            if (first.Exists(m => value(m) < 0) || last.Exists(m => value(m) < 0))
                return 0;
            return Median(last, m => value(m)) - Median(first, m => value(m));
        }

        private static string Mb(double bytes) =>
            bytes < 0 ? "n/a" : (bytes / (1024.0 * 1024.0)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }
}

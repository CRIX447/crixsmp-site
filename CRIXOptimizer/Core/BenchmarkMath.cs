// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Globalization;
using System.Text;

namespace CRIXOptimizer.Core
{
    /// <summary>Exact statistics over a recorded benchmark capture (all values derived from measured frame times).</summary>
    public sealed class BenchmarkResult
    {
        public int Frames;
        public double DurationSeconds;
        public double BudgetMs;
        public double AverageFps;
        public double AverageFrameMs;
        public double MedianMs;
        public double P95Ms;
        public double P99Ms;
        public double P999Ms;
        public double MaxMs;
        public double StdDevMs;
        /// <summary>Frame rate at the 99th-percentile frame time.</summary>
        public double OnePercentLowFps;
        /// <summary>Average frame rate of the slowest 1% of frames (the other common "1% low" definition).</summary>
        public double OnePercentLowAverageFps;
        public double PointOnePercentLowFps;
        public double OverBudgetPercent;
        public int Hitches;

        public string ToKeyValueText()
        {
            var sb = new StringBuilder(512);
            Append(sb, "frames", Frames);
            Append(sb, "duration_s", DurationSeconds);
            Append(sb, "budget_ms", BudgetMs);
            Append(sb, "avg_fps", AverageFps);
            Append(sb, "avg_frame_ms", AverageFrameMs);
            Append(sb, "median_frame_ms", MedianMs);
            Append(sb, "p95_frame_ms", P95Ms);
            Append(sb, "p99_frame_ms", P99Ms);
            Append(sb, "p999_frame_ms", P999Ms);
            Append(sb, "max_frame_ms", MaxMs);
            Append(sb, "stddev_frame_ms", StdDevMs);
            Append(sb, "low1_fps_percentile", OnePercentLowFps);
            Append(sb, "low1_fps_average", OnePercentLowAverageFps);
            Append(sb, "low01_fps_percentile", PointOnePercentLowFps);
            Append(sb, "over_budget_percent", OverBudgetPercent);
            Append(sb, "hitches_over_2x_budget", Hitches);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string key, double value) =>
            sb.Append(key).Append('=').Append(value.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');

        private static void Append(StringBuilder sb, string key, int value) =>
            sb.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    public static class BenchmarkMath
    {
        /// <summary>
        /// Computes exact statistics. <paramref name="scratch"/> must hold at least <paramref name="count"/>
        /// elements; it is overwritten (sorted copy) so callers can reuse one buffer.
        /// </summary>
        public static BenchmarkResult Compute(float[] frameMs, int count, double budgetMs, float[] scratch = null)
        {
            if (frameMs == null)
                throw new ArgumentNullException(nameof(frameMs));
            count = Math.Min(count, frameMs.Length);
            var result = new BenchmarkResult { BudgetMs = budgetMs };

            // Ignore invalid samples instead of letting one bad value poison the averages.
            if (scratch == null || scratch.Length < count)
                scratch = new float[Math.Max(count, 1)];
            int n = 0;
            double sum = 0, sumSquares = 0;
            int overBudget = 0, hitches = 0;
            for (int i = 0; i < count; i++)
            {
                float v = frameMs[i];
                if (!(v > 0f) || float.IsInfinity(v))
                    continue;
                scratch[n++] = v;
                sum += v;
                sumSquares += (double)v * v;
                if (budgetMs > 0)
                {
                    if (v > budgetMs * 1.05)
                        overBudget++;
                    if (v > budgetMs * 2.0)
                        hitches++;
                }
            }

            result.Frames = n;
            if (n == 0)
                return result;

            Array.Sort(scratch, 0, n);
            result.DurationSeconds = sum / 1000.0;
            result.AverageFrameMs = sum / n;
            result.AverageFps = n * 1000.0 / sum;
            result.MedianMs = NearestRank(scratch, n, 50);
            result.P95Ms = NearestRank(scratch, n, 95);
            result.P99Ms = NearestRank(scratch, n, 99);
            result.P999Ms = NearestRank(scratch, n, 99.9);
            result.MaxMs = scratch[n - 1];
            double variance = n > 1 ? (sumSquares - sum * sum / n) / (n - 1) : 0;
            result.StdDevMs = variance > 0 ? Math.Sqrt(variance) : 0;
            result.OnePercentLowFps = 1000.0 / result.P99Ms;
            result.PointOnePercentLowFps = 1000.0 / result.P999Ms;

            int worst = Math.Max(1, n / 100);
            double worstSum = 0;
            for (int i = n - worst; i < n; i++)
                worstSum += scratch[i];
            result.OnePercentLowAverageFps = worst * 1000.0 / worstSum;

            result.OverBudgetPercent = budgetMs > 0 ? overBudget * 100.0 / n : 0;
            result.Hitches = hitches;
            return result;
        }

        /// <summary>Nearest-rank percentile on an ascending-sorted array.</summary>
        public static double NearestRank(float[] sortedAscending, int n, double percentile)
        {
            if (n <= 0)
                return 0;
            int rank = (int)Math.Ceiling(percentile / 100.0 * n);
            if (rank < 1)
                rank = 1;
            if (rank > n)
                rank = n;
            return sortedAscending[rank - 1];
        }
    }
}

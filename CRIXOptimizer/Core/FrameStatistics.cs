// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Core
{
    /// <summary>Point-in-time summary of the frame-time window. All values are derived from measured frame times.</summary>
    public struct FrameStatsSnapshot
    {
        public int FrameCount;
        public double WindowSeconds;
        public double AverageFps;
        public double AverageFrameMs;
        public double StdDevMs;
        public float MedianMs;
        public float P95Ms;
        public float P99Ms;
        public float P999Ms;
        public float MaxMs;
        public float BudgetMs;
        public double OverBudgetFraction;
        public int HitchCount;

        /// <summary>"1% low" defined as the frame rate at the 99th-percentile frame time.</summary>
        public double OnePercentLowFps => P99Ms > 0 ? 1000.0 / P99Ms : 0;
        public double PointOnePercentLowFps => P999Ms > 0 ? 1000.0 / P999Ms : 0;
        public bool IsEmpty => FrameCount == 0;
    }

    /// <summary>
    /// Allocation-free sliding window of frame times.
    /// Push is O(1); percentiles come from a 0.1 ms histogram that is maintained incrementally,
    /// so a full snapshot costs O(bins + frames) and is intended to run a few times per second, not per frame.
    /// </summary>
    public sealed class FrameStatistics
    {
        public const float BinWidthMs = 0.1f;
        public const int BinCount = 2500; // 0 - 250 ms; the last bin collects everything slower.

        private readonly float[] ring;
        private readonly int[] histogram = new int[BinCount];
        private int head;
        private int count;
        private double sum;
        private double sumSquares;
        private double windowMs;
        private int popsSinceRecompute;

        public FrameStatistics(int capacity = 8192, double windowSeconds = 10.0)
        {
            if (capacity < 16)
                capacity = 16;
            ring = new float[capacity];
            WindowSeconds = windowSeconds;
        }

        public int Capacity => ring.Length;
        public int Count => count;
        public double TotalMs => sum;

        /// <summary>Length of the sliding window. Shrinking it takes effect on the next push.</summary>
        public double WindowSeconds
        {
            get => windowMs / 1000.0;
            set => windowMs = Math.Max(0.25, Math.Min(600.0, double.IsNaN(value) ? 10.0 : value)) * 1000.0;
        }

        public void Push(float frameMs)
        {
            if (!(frameMs > 0f) || float.IsInfinity(frameMs))
                return;

            if (count == ring.Length)
                PopOldest();

            int index = head + count;
            if (index >= ring.Length)
                index -= ring.Length;
            ring[index] = frameMs;
            count++;
            sum += frameMs;
            sumSquares += (double)frameMs * frameMs;
            histogram[Bin(frameMs)]++;

            // Keep the newest frames covering at least the window: the oldest frame is dropped only when the
            // rest still cover the full window, so a filled window spans [window, window + one frame).
            while (count > 1 && sum - ring[head] >= windowMs)
                PopOldest();
        }

        public void Clear()
        {
            head = 0;
            count = 0;
            sum = 0;
            sumSquares = 0;
            popsSinceRecompute = 0;
            Array.Clear(histogram, 0, histogram.Length);
        }

        public double AverageFrameMs => count == 0 ? 0 : sum / count;
        public double AverageFps => sum <= 0 ? 0 : count * 1000.0 / sum;

        public double StdDevMs
        {
            get
            {
                if (count < 2)
                    return 0;
                double variance = (sumSquares - sum * sum / count) / (count - 1);
                return variance > 0 ? Math.Sqrt(variance) : 0;
            }
        }

        /// <summary>Frame time at percentile p (0-100), quantised to the 0.1 ms histogram bin's upper edge.</summary>
        public float PercentileMs(double p)
        {
            if (count == 0)
                return 0f;
            if (p <= 0)
                p = 0;
            if (p >= 100)
                return MaxMs;

            long rank = (long)Math.Ceiling(p / 100.0 * count);
            if (rank < 1)
                rank = 1;

            long cumulative = 0;
            for (int i = 0; i < BinCount; i++)
            {
                cumulative += histogram[i];
                if (cumulative >= rank)
                    return i == BinCount - 1 ? MaxMs : (i + 1) * BinWidthMs;
            }
            return MaxMs;
        }

        public float MaxMs
        {
            get
            {
                float max = 0f;
                for (int i = 0, index = head; i < count; i++)
                {
                    float value = ring[index];
                    if (value > max)
                        max = value;
                    if (++index == ring.Length)
                        index = 0;
                }
                return max;
            }
        }

        /// <summary>Exact number of frames in the window strictly slower than the threshold.</summary>
        public int CountAbove(float thresholdMs)
        {
            int above = 0;
            for (int i = 0, index = head; i < count; i++)
            {
                if (ring[index] > thresholdMs)
                    above++;
                if (++index == ring.Length)
                    index = 0;
            }
            return above;
        }

        /// <summary>Most recent frame time, or 0 if empty.</summary>
        public float LastMs
        {
            get
            {
                if (count == 0)
                    return 0f;
                int index = head + count - 1;
                if (index >= ring.Length)
                    index -= ring.Length;
                return ring[index];
            }
        }

        public FrameStatsSnapshot Snapshot(float budgetMs)
        {
            var snapshot = new FrameStatsSnapshot
            {
                FrameCount = count,
                WindowSeconds = sum / 1000.0,
                AverageFps = AverageFps,
                AverageFrameMs = AverageFrameMs,
                StdDevMs = StdDevMs,
                BudgetMs = budgetMs,
            };
            if (count == 0)
                return snapshot;

            snapshot.MedianMs = PercentileMs(50);
            snapshot.P95Ms = PercentileMs(95);
            snapshot.P99Ms = PercentileMs(99);
            snapshot.P999Ms = PercentileMs(99.9);
            snapshot.MaxMs = MaxMs;
            if (budgetMs > 0)
            {
                // A small tolerance avoids counting frames that land exactly on vsync as "over budget".
                snapshot.OverBudgetFraction = CountAbove(budgetMs * 1.05f) / (double)count;
                snapshot.HitchCount = CountAbove(budgetMs * 2f);
            }
            return snapshot;
        }

        private static int Bin(float frameMs)
        {
            int bin = (int)(frameMs / BinWidthMs);
            return bin < 0 ? 0 : (bin >= BinCount ? BinCount - 1 : bin);
        }

        private void PopOldest()
        {
            float value = ring[head];
            if (++head == ring.Length)
                head = 0;
            count--;
            histogram[Bin(value)]--;

            if (count == 0)
            {
                sum = 0;
                sumSquares = 0;
                popsSinceRecompute = 0;
                return;
            }

            sum -= value;
            sumSquares -= (double)value * value;

            // Periodically recompute the running sums to stop floating-point drift in long sessions.
            if (++popsSinceRecompute >= 65536)
                RecomputeSums();
        }

        private void RecomputeSums()
        {
            popsSinceRecompute = 0;
            double s = 0, s2 = 0;
            for (int i = 0, index = head; i < count; i++)
            {
                double v = ring[index];
                s += v;
                s2 += v * v;
                if (++index == ring.Length)
                    index = 0;
            }
            sum = s;
            sumSquares = s2;
        }
    }
}

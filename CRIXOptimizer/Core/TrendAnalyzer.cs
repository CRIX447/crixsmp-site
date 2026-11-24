// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Fixed-size least-squares trend over (time, value) samples. Used to detect sustained memory growth.
    /// No allocations after construction.
    /// </summary>
    public sealed class TrendAnalyzer
    {
        private readonly double[] times;
        private readonly double[] values;
        private int head;
        private int count;

        public TrendAnalyzer(int capacity)
        {
            if (capacity < 3)
                capacity = 3;
            times = new double[capacity];
            values = new double[capacity];
        }

        public int Count => count;
        public int Capacity => times.Length;
        public bool IsFull => count == times.Length;

        public void Add(double timeSeconds, double value)
        {
            int index = (head + count) % times.Length;
            if (count == times.Length)
            {
                index = head;
                head = (head + 1) % times.Length;
            }
            else
            {
                count++;
            }
            times[index] = timeSeconds;
            values[index] = value;
        }

        public void Clear()
        {
            head = 0;
            count = 0;
        }

        /// <summary>Slope in value units per second (least squares). 0 with fewer than 3 samples.</summary>
        public double SlopePerSecond
        {
            get
            {
                if (count < 3)
                    return 0;
                double meanT = 0, meanV = 0;
                for (int i = 0; i < count; i++)
                {
                    int idx = (head + i) % times.Length;
                    meanT += times[idx];
                    meanV += values[idx];
                }
                meanT /= count;
                meanV /= count;

                double num = 0, den = 0;
                for (int i = 0; i < count; i++)
                {
                    int idx = (head + i) % times.Length;
                    double dt = times[idx] - meanT;
                    num += dt * (values[idx] - meanV);
                    den += dt * dt;
                }
                return den > 1e-12 ? num / den : 0;
            }
        }

        /// <summary>Seconds covered by the stored samples.</summary>
        public double SpanSeconds
        {
            get
            {
                if (count < 2)
                    return 0;
                int last = (head + count - 1) % times.Length;
                return Math.Max(0, times[last] - times[head]);
            }
        }

        public double Latest => count == 0 ? 0 : values[(head + count - 1) % values.Length];
        public double Oldest => count == 0 ? 0 : values[head];
    }
}

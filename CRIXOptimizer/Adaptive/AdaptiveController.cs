// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Adaptive
{
    public sealed class AdaptiveParameters
    {
        /// <summary>Lowest (cheapest) level allowed, &lt;= 0.</summary>
        public int MinLevel = -6;
        /// <summary>Highest level allowed, &gt;= 0. Above 0 only render scale rises.</summary>
        public int MaxLevel = 0;
        /// <summary>0 = tolerant (reacts only to large drops), 1 = sensitive.</summary>
        public double Sensitivity = 0.5;
        /// <summary>Seconds the frame rate must stay below target before stepping down.</summary>
        public double DownSustainSeconds = 2;
        /// <summary>Seconds of headroom required before stepping up.</summary>
        public double UpSustainSeconds = 20;
        /// <summary>Minimum seconds between any two changes.</summary>
        public double CooldownSeconds = 8;
        /// <summary>Samples right after a change are ignored (resource reallocation causes a one-off hitch).</summary>
        public double SettleSeconds = 2;
        /// <summary>A step down within this many seconds of a step up counts as a failed probe.</summary>
        public double ProbeFailWindowSeconds = 30;
        /// <summary>Back-off after a failed probe; doubles per consecutive failure up to MaxBackoffSeconds.</summary>
        public double BaseBackoffSeconds = 60;
        public double MaxBackoffSeconds = 900;
        /// <summary>Fraction of the frame budget the GPU should stay under (0.1 = keep 10% headroom). Only used when GPU time is reported.</summary>
        public double GpuHeadroomFraction = 0.0;

        public double Tolerance => 0.15 - 0.13 * Clamp01(Sensitivity);

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }

    public struct AdaptiveSample
    {
        public double Now;
        /// <summary>Mean frame time over the last evaluation window (measured).</summary>
        public double FrameMs;
        /// <summary>Frame budget for the target frame rate.</summary>
        public double TargetMs;
        /// <summary>GPU time per frame reported by the XR runtime or FrameTimingManager; NaN when unavailable.</summary>
        public double GpuMs;
    }

    public enum AdaptiveReason
    {
        None,
        Disabled,
        Manual,
        Frozen,
        Settling,
        SteppedDown,
        SteppedUp,
        AtMinimum,
        AtMaximum,
        Cooldown,
        BackoffActive,
    }

    public struct AdaptiveDecision
    {
        public int Level;
        public bool Changed;
        public AdaptiveReason Reason;
    }

    /// <summary>
    /// Pure decision logic for adaptive quality. It only produces a level; applying it is someone else's job.
    ///
    /// In VR the frame rate is capped at the headset refresh rate, so "at target" says nothing about
    /// headroom. When GPU time is reported we use it directly; otherwise stepping up is a probe that is
    /// only tried after a long stable period, and a probe that fails is backed off exponentially. This
    /// prevents oscillating between two levels.
    /// </summary>
    public sealed class AdaptiveController
    {
        private double belowSince = -1;
        private double aboveSince = -1;
        private double lastChangeTime = double.NegativeInfinity;
        private double lastUpTime = double.NegativeInfinity;
        private double upBlockedUntil = double.NegativeInfinity;
        private int consecutiveFailedProbes;

        public AdaptiveController(AdaptiveParameters parameters = null)
        {
            Parameters = parameters ?? new AdaptiveParameters();
        }

        public AdaptiveParameters Parameters { get; set; }
        public int Level { get; private set; }
        public bool Frozen { get; set; }
        public int? ManualLevel { get; set; }
        public int ConsecutiveFailedProbes => consecutiveFailedProbes;
        public double UpBlockedUntil => upBlockedUntil;
        public AdaptiveReason LastReason { get; private set; }

        /// <summary>True when the last evaluation judged the frame CPU-bound (GPU had spare time but frames were late).</summary>
        public bool LastJudgedCpuBound { get; private set; }

        public void Reset()
        {
            Level = 0;
            belowSince = -1;
            aboveSince = -1;
            lastChangeTime = double.NegativeInfinity;
            lastUpTime = double.NegativeInfinity;
            upBlockedUntil = double.NegativeInfinity;
            consecutiveFailedProbes = 0;
            LastJudgedCpuBound = false;
        }

        public AdaptiveDecision Evaluate(AdaptiveSample sample)
        {
            AdaptiveParameters p = Parameters;
            int minLevel = Math.Min(0, p.MinLevel);
            int maxLevel = Math.Max(0, p.MaxLevel);

            if (ManualLevel.HasValue)
            {
                int manual = Math.Max(minLevel, Math.Min(maxLevel, ManualLevel.Value));
                return Decide(manual, manual != Level, AdaptiveReason.Manual, sample.Now);
            }

            // Bounds may have been tightened since the last evaluation.
            if (Level < minLevel)
                return Decide(minLevel, true, AdaptiveReason.AtMinimum, sample.Now);
            if (Level > maxLevel)
                return Decide(maxLevel, true, AdaptiveReason.AtMaximum, sample.Now);

            if (Frozen)
                return Decide(Level, false, AdaptiveReason.Frozen, sample.Now);

            if (sample.TargetMs <= 0 || sample.FrameMs <= 0)
                return Decide(Level, false, AdaptiveReason.None, sample.Now);

            if (sample.Now - lastChangeTime < p.SettleSeconds)
            {
                belowSince = -1;
                aboveSince = -1;
                return Decide(Level, false, AdaptiveReason.Settling, sample.Now);
            }

            double tolerance = p.Tolerance;
            bool gpuKnown = !double.IsNaN(sample.GpuMs) && sample.GpuMs > 0;
            double gpuTargetMs = sample.TargetMs * (1.0 - Math.Max(0, Math.Min(0.5, p.GpuHeadroomFraction)));

            bool framesLate = sample.FrameMs > sample.TargetMs * (1.0 + tolerance);
            bool gpuOverTarget = gpuKnown && p.GpuHeadroomFraction > 0 && sample.GpuMs > gpuTargetMs * (1.0 + tolerance);
            bool overloaded = framesLate || gpuOverTarget;

            bool atFrameTarget = sample.FrameMs <= sample.TargetMs * 1.03;
            bool hasHeadroom = gpuKnown
                ? atFrameTarget && sample.GpuMs < gpuTargetMs * 0.80
                : atFrameTarget;

            LastJudgedCpuBound = framesLate && gpuKnown && sample.GpuMs < sample.TargetMs * 0.75;

            if (overloaded)
            {
                aboveSince = -1;
                if (belowSince < 0)
                    belowSince = sample.Now;

                if (sample.Now - belowSince < p.DownSustainSeconds)
                    return Decide(Level, false, AdaptiveReason.None, sample.Now);
                if (Level <= minLevel)
                    return Decide(Level, false, AdaptiveReason.AtMinimum, sample.Now);
                if (sample.Now - lastChangeTime < p.CooldownSeconds)
                    return Decide(Level, false, AdaptiveReason.Cooldown, sample.Now);

                if (sample.Now - lastUpTime <= p.ProbeFailWindowSeconds)
                {
                    consecutiveFailedProbes++;
                    double backoff = Math.Min(p.MaxBackoffSeconds, p.BaseBackoffSeconds * Math.Pow(2, consecutiveFailedProbes - 1));
                    upBlockedUntil = sample.Now + backoff;
                }
                belowSince = -1;
                return Decide(Level - 1, true, AdaptiveReason.SteppedDown, sample.Now);
            }

            belowSince = -1;
            if (!hasHeadroom)
            {
                aboveSince = -1;
                return Decide(Level, false, AdaptiveReason.None, sample.Now);
            }

            // A step up that survived the fail window was a success: forgive one failure.
            if (lastUpTime > double.NegativeInfinity && sample.Now - lastUpTime > p.ProbeFailWindowSeconds && consecutiveFailedProbes > 0 && lastChangeTime == lastUpTime)
            {
                consecutiveFailedProbes--;
                lastUpTime = double.NegativeInfinity;
            }

            if (aboveSince < 0)
                aboveSince = sample.Now;
            if (sample.Now - aboveSince < p.UpSustainSeconds)
                return Decide(Level, false, AdaptiveReason.None, sample.Now);
            if (Level >= maxLevel)
                return Decide(Level, false, AdaptiveReason.AtMaximum, sample.Now);
            if (sample.Now < upBlockedUntil)
                return Decide(Level, false, AdaptiveReason.BackoffActive, sample.Now);
            if (sample.Now - lastChangeTime < p.CooldownSeconds)
                return Decide(Level, false, AdaptiveReason.Cooldown, sample.Now);

            aboveSince = -1;
            lastUpTime = sample.Now;
            return Decide(Level + 1, true, AdaptiveReason.SteppedUp, sample.Now);
        }

        private AdaptiveDecision Decide(int level, bool changed, AdaptiveReason reason, double now)
        {
            if (changed && level != Level)
            {
                Level = level;
                lastChangeTime = now;
            }
            else
            {
                changed = false;
            }
            LastReason = reason;
            return new AdaptiveDecision { Level = Level, Changed = changed, Reason = reason };
        }
    }
}

// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Core
{
    public enum DegradationEvent
    {
        None,
        Started,
        Recovered,
    }

    /// <summary>
    /// Detects sustained performance degradation: a smoothed frame rate that stays below
    /// target * (1 - threshold) for at least SustainSeconds. Uses hysteresis so a single dip or a
    /// single good second does not toggle the state, and a cooldown between repeated alerts.
    /// </summary>
    public sealed class DegradationDetector
    {
        public double ThresholdFraction = 0.10;
        public double SustainSeconds = 5;
        public double RecoverSeconds = 5;
        public double CooldownSeconds = 60;

        private double belowSince = -1;
        private double aboveSince = -1;
        private double lastAlertTime = double.NegativeInfinity;

        public bool IsDegraded { get; private set; }
        public double DegradedSince { get; private set; } = -1;
        public int AlertCount { get; private set; }
        public double LastFps { get; private set; }

        public DegradationEvent Update(double now, double smoothedFps, double targetFps)
        {
            LastFps = smoothedFps;
            if (targetFps <= 0 || smoothedFps <= 0)
                return DegradationEvent.None;

            bool below = smoothedFps < targetFps * (1.0 - ThresholdFraction);
            if (below)
            {
                aboveSince = -1;
                if (belowSince < 0)
                    belowSince = now;

                if (!IsDegraded && now - belowSince >= SustainSeconds)
                {
                    IsDegraded = true;
                    DegradedSince = belowSince;
                    if (now - lastAlertTime >= CooldownSeconds)
                    {
                        lastAlertTime = now;
                        AlertCount++;
                        return DegradationEvent.Started;
                    }
                }
                return DegradationEvent.None;
            }

            belowSince = -1;
            if (!IsDegraded)
                return DegradationEvent.None;

            if (aboveSince < 0)
                aboveSince = now;
            if (now - aboveSince >= RecoverSeconds)
            {
                IsDegraded = false;
                DegradedSince = -1;
                aboveSince = -1;
                return DegradationEvent.Recovered;
            }
            return DegradationEvent.None;
        }

        public void Reset()
        {
            belowSince = -1;
            aboveSince = -1;
            IsDegraded = false;
            DegradedSince = -1;
        }
    }
}

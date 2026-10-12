// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Adaptive
{
    public enum ProbeResult
    {
        Idle,
        Pending,
        Effective,
        Ineffective,
    }

    /// <summary>
    /// Checks whether lowering the render scale actually made frames faster. Without GPU timing from the VR
    /// runtime, a CPU bottleneck (busy lobbies, many cosmetics) looks exactly like a GPU bottleneck, and
    /// lowering resolution would only blur the image. After a resolution drop the probe waits out the settle
    /// time, averages the frame time for a few seconds and compares it with the frame time before the change.
    /// Less than <see cref="MinImprovement"/> improvement means "did not help": the caller should restore the
    /// resolution and treat the bottleneck as CPU-side for a while.
    /// </summary>
    public sealed class ResolutionProbe
    {
        public double MinImprovement = 0.03;
        public double SettleSeconds = 2.5;
        public double MeasureSeconds = 3;

        private double startTime = -1;
        private double beforeMs;
        private double sum;
        private int count;

        public bool IsPending => startTime >= 0;
        public double LastImprovement { get; private set; }

        public void Begin(double now, double frameMsBefore)
        {
            if (frameMsBefore <= 0)
                return;
            startTime = now;
            beforeMs = frameMsBefore;
            sum = 0;
            count = 0;
        }

        public void Cancel() => startTime = -1;

        public ProbeResult Sample(double now, double frameMs)
        {
            if (startTime < 0)
                return ProbeResult.Idle;
            double elapsed = now - startTime;
            if (elapsed < SettleSeconds || frameMs <= 0)
                return ProbeResult.Pending;
            sum += frameMs;
            count++;
            if (elapsed < SettleSeconds + MeasureSeconds)
                return ProbeResult.Pending;

            double after = sum / count;
            LastImprovement = (beforeMs - after) / beforeMs;
            startTime = -1;
            return LastImprovement >= MinImprovement ? ProbeResult.Effective : ProbeResult.Ineffective;
        }
    }
}

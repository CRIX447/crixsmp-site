// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Records a fixed-length capture of per-frame times and writes a CSV plus a key=value summary to
    /// BepInEx/CRIXOptimizer/benchmarks. Recording is a single array store per frame; statistics and file
    /// writing happen on a worker thread after the capture ends. Use tools/compare_benchmarks.py to compare runs.
    /// </summary>
    internal sealed class BenchmarkRecorder
    {
        public enum Phase { Idle, Warmup, Recording, Writing }

        public sealed class Context
        {
            public string Label;
            public string Profile;
            public bool Adaptive;
            public double BudgetMs;
            public string Environment;
            public string Settings;
        }

        private readonly ILogSink log;
        private readonly string directory;
        private float[] frames = new float[0];
        private float[] gpu = new float[0];
        private int count;
        private int gpuCount;
        private double startWall;
        private double phaseEnd;
        private double durationSeconds;
        private Context context;
        private int gcAtStart;
        private TimeSpan cpuAtStart;
        private long managedAtStart;
        private int droppedAtStart = -1;
        private readonly Stopwatch wall = Stopwatch.StartNew();

        public BenchmarkRecorder(ILogSink log, string directory)
        {
            this.log = log;
            this.directory = directory;
        }

        // Written by the worker thread when results are saved, so access is volatile.
        private volatile int state;
        public Phase State
        {
            get => (Phase)state;
            private set => state = (int)value;
        }
        private volatile string lastResultPath;
        private volatile string lastSummary;
        public string LastResultPath { get => lastResultPath; private set => lastResultPath = value; }
        public string LastSummary { get => lastSummary; private set => lastSummary = value; }
        public double SecondsRemaining => State == Phase.Idle || State == Phase.Writing ? 0 : Math.Max(0, phaseEnd - wall.Elapsed.TotalSeconds);
        public bool WantsGpuSamples => State == Phase.Recording;

        public void Start(double warmupSeconds, double duration, Context ctx)
        {
            if (State != Phase.Idle)
                return;
            context = ctx;
            durationSeconds = Math.Max(1, duration);
            int capacity = (int)Math.Min(2_000_000, durationSeconds * 400 + 1024);
            if (frames.Length < capacity)
            {
                frames = new float[capacity];
                gpu = new float[capacity];
            }
            count = 0;
            gpuCount = 0;
            State = Phase.Warmup;
            phaseEnd = wall.Elapsed.TotalSeconds + Math.Max(0, warmupSeconds);
            log.Info($"Benchmark: warming up for {warmupSeconds:0}s, then recording {durationSeconds:0}s (profile {ctx.Profile}{(ctx.Adaptive ? ", adaptive" : "")}).");
        }

        public void Cancel()
        {
            if (State == Phase.Warmup || State == Phase.Recording)
            {
                State = Phase.Idle;
                log.Info("Benchmark cancelled.");
            }
        }

        /// <summary>Per frame. droppedFrames is the XR runtime's counter or -1.</summary>
        public void Sample(float unscaledDeltaTime, float gpuMs, int droppedFrames)
        {
            if (State == Phase.Warmup)
            {
                if (wall.Elapsed.TotalSeconds < phaseEnd)
                    return;
                State = Phase.Recording;
                startWall = wall.Elapsed.TotalSeconds;
                phaseEnd = startWall + durationSeconds;
                gcAtStart = GC.CollectionCount(0);
                cpuAtStart = ProcessCpu();
                managedAtStart = GC.GetTotalMemory(false);
                droppedAtStart = droppedFrames;
                return;
            }
            if (State != Phase.Recording)
                return;

            float ms = unscaledDeltaTime * 1000f;
            if (count < frames.Length && ms > 0f)
                frames[count++] = ms;
            if (!float.IsNaN(gpuMs) && gpuMs > 0f && gpuCount < gpu.Length)
                gpu[gpuCount++] = gpuMs;

            if (wall.Elapsed.TotalSeconds >= phaseEnd || count >= frames.Length)
                Finish(droppedFrames);
        }

        private void Finish(int droppedFrames)
        {
            State = Phase.Writing;
            double elapsed = wall.Elapsed.TotalSeconds - startWall;
            int gcCount = GC.CollectionCount(0) - gcAtStart;
            TimeSpan cpu = ProcessCpu() - cpuAtStart;
            long managedEnd = GC.GetTotalMemory(false);
            int dropped = droppedAtStart >= 0 && droppedFrames >= 0 ? droppedFrames - droppedAtStart : -1;
            int cores = Environment.ProcessorCount;

            float[] frameCopy = new float[count];
            Array.Copy(frames, frameCopy, count);
            float[] gpuCopy = new float[gpuCount];
            Array.Copy(gpu, gpuCopy, gpuCount);
            Context ctx = context;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    BenchmarkResult result = BenchmarkMath.Compute(frameCopy, frameCopy.Length, ctx.BudgetMs);
                    var summary = new StringBuilder(2048);
                    summary.Append("# CRIX OPTIMIZER benchmark summary. All values measured in-game; see docs/BENCHMARKING.md.\n");
                    summary.Append("label=").Append(Sanitize(ctx.Label)).Append('\n');
                    summary.Append("profile=").Append(ctx.Profile).Append('\n');
                    summary.Append("adaptive=").Append(ctx.Adaptive ? "on" : "off").Append('\n');
                    summary.Append("timestamp_utc=").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');
                    summary.Append("wall_seconds=").Append(elapsed.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                    summary.Append(result.ToKeyValueText());
                    summary.Append("gc_collections=").Append(gcCount).Append('\n');
                    summary.Append("managed_heap_delta_mb=").Append(((managedEnd - managedAtStart) / (1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
                    double cpuPercentOfMachine = elapsed > 0 ? cpu.TotalSeconds / (elapsed * cores) * 100.0 : 0;
                    summary.Append("process_cpu_percent_of_all_cores=").Append(cpuPercentOfMachine.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
                    summary.Append("process_cpu_cores_busy=").Append((elapsed > 0 ? cpu.TotalSeconds / elapsed : 0).ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
                    if (gpuCopy.Length > 0)
                    {
                        double sum = 0;
                        foreach (float g in gpuCopy) sum += g;
                        summary.Append("gpu_frame_ms_avg=").Append((sum / gpuCopy.Length).ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                        summary.Append("gpu_samples=").Append(gpuCopy.Length).Append('\n');
                    }
                    else
                    {
                        summary.Append("gpu_frame_ms_avg=unavailable\n");
                    }
                    summary.Append("xr_dropped_frames=").Append(dropped >= 0 ? dropped.ToString(CultureInfo.InvariantCulture) : "unavailable").Append('\n');
                    summary.Append("gpu_utilization=unavailable (not measurable in-process)\n");
                    summary.Append("vram_usage=unavailable (not measurable in-process)\n");
                    summary.Append("\n[environment]\n").Append(ctx.Environment).Append('\n');
                    summary.Append("\n[settings]\n").Append(ctx.Settings).Append('\n');

                    Directory.CreateDirectory(directory);
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                    string name = $"{stamp}_{ctx.Profile}{(ctx.Adaptive ? "_adaptive" : "")}{(string.IsNullOrEmpty(ctx.Label) ? "" : "_" + Sanitize(ctx.Label))}";
                    string csvPath = Path.Combine(directory, name + ".csv");
                    string summaryPath = Path.Combine(directory, name + ".summary.txt");

                    var csv = new StringBuilder(frameCopy.Length * 10 + 64);
                    csv.Append("frame,frame_ms\n");
                    for (int i = 0; i < frameCopy.Length; i++)
                        csv.Append(i).Append(',').Append(frameCopy[i].ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
                    File.WriteAllText(csvPath, csv.ToString());
                    File.WriteAllText(summaryPath, summary.ToString());

                    LastResultPath = summaryPath;
                    LastSummary = $"avg {result.AverageFps:0.0} FPS, 1% low {result.OnePercentLowFps:0.0} FPS, {result.OverBudgetPercent:0.0}% frames over budget";
                    log.Info($"Benchmark finished: {LastSummary}. Written to {summaryPath}");
                }
                catch (Exception e)
                {
                    log.Error("Writing benchmark results failed: " + e.Message);
                }
                finally
                {
                    State = Phase.Idle;
                }
            });
        }

        private static TimeSpan ProcessCpu()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    return p.TotalProcessorTime;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.ToString();
        }
    }
}

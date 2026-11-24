#!/usr/bin/env python3
"""Compare CRIX OPTIMIZER benchmark captures.

Reads the *.summary.txt files written by the in-game benchmark recorder (BepInEx/CRIXOptimizer/benchmarks)
and prints a Markdown table, optionally relative to a baseline run. Only numbers measured in-game are shown;
nothing is estimated. Standard library only.

  python3 tools/compare_benchmarks.py BENCH_DIR [BENCH_DIR ...] [--baseline LABEL_OR_FILE] [--csv out.csv]
  python3 tools/compare_benchmarks.py --self-test
"""
import argparse
import csv
import glob
import os
import sys

COLUMNS = [
    ("avg_fps", "Avg FPS", True),
    ("low1_fps_percentile", "1% low (p99)", True),
    ("low1_fps_average", "1% low (avg)", True),
    ("low01_fps_percentile", "0.1% low", True),
    ("stddev_frame_ms", "σ frame ms", False),
    ("p99_frame_ms", "p99 ms", False),
    ("max_frame_ms", "max ms", False),
    ("over_budget_percent", "% over budget", False),
    ("hitches_over_2x_budget", "hitches", False),
    ("gc_collections", "GCs", False),
    ("process_cpu_percent_of_all_cores", "CPU % (all cores)", False),
    ("gpu_frame_ms_avg", "GPU ms (runtime)", False),
    ("xr_dropped_frames", "XR dropped", False),
]


def parse_summary(path):
    data = {"_file": os.path.basename(path)}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line.startswith("["):
                break  # environment/settings blocks follow; stop at the first section
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            data[key.strip()] = value.strip()
    return data


def run_name(d):
    name = d.get("profile", "?")
    if d.get("adaptive") == "on":
        name += "+adaptive"
    if d.get("label"):
        name += " (" + d["label"] + ")"
    return name


def as_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def collect(paths):
    files = []
    for p in paths:
        if os.path.isdir(p):
            files.extend(sorted(glob.glob(os.path.join(p, "*.summary.txt"))))
        else:
            files.append(p)
    return [parse_summary(f) for f in files]


def table(runs, baseline=None):
    header = ["Run", "Frames", "Duration s"] + [title for _, title, _ in COLUMNS]
    lines = ["| " + " | ".join(header) + " |", "|" + "---|" * len(header)]
    for r in runs:
        cells = [run_name(r), r.get("frames", "?"), r.get("duration_s", "?")]
        for key, _, higher_is_better in COLUMNS:
            raw = r.get(key, "n/a")
            value = as_float(raw)
            cell = raw if value is None else ("%.2f" % value).rstrip("0").rstrip(".")
            if baseline is not None and r is not baseline and value is not None:
                base = as_float(baseline.get(key))
                if base not in (None, 0):
                    delta = (value - base) / abs(base) * 100.0
                    better = delta > 0 if higher_is_better else delta < 0
                    if abs(delta) >= 0.05:
                        cell += " (%+.1f%%%s)" % (delta, "" if abs(delta) < 1 else (" ✓" if better else " ✗"))
            cells.append(cell)
        lines.append("| " + " | ".join(str(c) for c in cells) + " |")
    return "\n".join(lines)


def pick_baseline(runs, selector):
    if not selector:
        return None
    for r in runs:
        if selector in (r["_file"], r.get("label"), run_name(r), r.get("profile")):
            return r
    sys.exit("baseline '%s' not found among: %s" % (selector, ", ".join(run_name(r) for r in runs)))


def self_test():
    """Exercises parsing and formatting with SYNTHETIC numbers (not benchmark results)."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        for name, fps, low in (("Disabled", "80", "60"), ("Balanced", "88", "75")):
            with open(os.path.join(tmp, name + ".summary.txt"), "w", encoding="utf-8") as f:
                f.write("# synthetic\nlabel=synthetic\nprofile=%s\nadaptive=off\nframes=100\nduration_s=1\n"
                        "avg_fps=%s\nlow1_fps_percentile=%s\nstddev_frame_ms=1.5\ngpu_frame_ms_avg=unavailable\n"
                        "\n[environment]\navg_fps=999\n" % (name, fps, low))
        runs = collect([tmp])
        assert len(runs) == 2, runs
        assert runs[0]["avg_fps"] in ("80", "88"), "environment block must not override summary"
        base = pick_baseline(runs, "Disabled")
        out = table(runs, base)
        assert "+10.0% ✓" in out, out
        assert "unavailable" in out
    print("self-test passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="*", help="benchmark directories or .summary.txt files")
    parser.add_argument("--baseline", help="label, profile, run name or file name of the baseline run")
    parser.add_argument("--csv", help="also write the raw values to this CSV file")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.paths:
        parser.error("give at least one benchmark directory or summary file")
    runs = collect(args.paths)
    if not runs:
        sys.exit("no *.summary.txt files found")
    print(table(runs, pick_baseline(runs, args.baseline)))
    if args.csv:
        keys = ["_file", "profile", "adaptive", "label", "frames", "duration_s"] + [k for k, _, _ in COLUMNS]
        with open(args.csv, "w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=keys, extrasaction="ignore")
            writer.writeheader()
            writer.writerows(runs)


if __name__ == "__main__":
    main()

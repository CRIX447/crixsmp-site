#!/usr/bin/env bash
# Builds, tests, verifies and packages a CRIX OPTIMIZER release.
#
# Output (in release/):
#   CRIXOptimizer.dll                  the plugin
#   CRIXOptimizer-<ver>.zip            install/ (drop-in plugin folder) + source/ + docs + checksums
#   logs/build.log, logs/tests-*.log, logs/refcheck-*.log, logs/unity-api-check.log
#
# Optional environment: EXTRA_BEPINEX_DLL / EXTRA_BEPINEX_DIR (test and ref-check against a second BepInEx build),
# UNITY_CS_REFERENCE (a UnityCsReference checkout at the game's Unity version, see tools/check_unity_api.py).
#
# Usage: tools/package-release.sh [--skip-tests]
# Requires lib/ (tools/setup-references.sh) unless building against a local game install (RefMode=Game).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/CRIXOptimizer.csproj")"
OUT="$ROOT/release"
LOGS="$OUT/logs"
STAGE="$OUT/.stage/CRIXOptimizer-$VERSION"
SKIP_TESTS="${1:-}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

rm -rf "$OUT/.stage" "$LOGS"
mkdir -p "$LOGS" "$STAGE"

echo "== Build $VERSION"
dotnet build "$ROOT/CRIXOptimizer.csproj" -c Release -nologo -p:RefMode="${REFMODE:-Lib}" -p:ContinuousIntegrationBuild=true \
  2>&1 | tee "$LOGS/build.log" | grep -E "warning|error|Build succeeded|->" || true
grep -q "Build succeeded" "$LOGS/build.log" || { echo "build failed"; exit 1; }
DLL="$ROOT/bin/Release/CRIXOptimizer.dll"
[ -s "$DLL" ] || { echo "DLL missing or empty"; exit 1; }

if [ "$SKIP_TESTS" != "--skip-tests" ]; then
  echo "== Tests (BepInEx in lib/)"
  "$ROOT/Tests/run-tests.sh" >"$LOGS/tests-lib-bepinex.log" 2>&1 || { tail -30 "$LOGS/tests-lib-bepinex.log"; exit 1; }
  grep -E "^# (runtime|BepInEx|passed)" "$LOGS/tests-lib-bepinex.log"
  if [ -n "${EXTRA_BEPINEX_DLL:-}" ]; then
    echo "== Tests (BepInEx at $EXTRA_BEPINEX_DLL)"
    BEPINEX_DLL="$EXTRA_BEPINEX_DLL" "$ROOT/Tests/run-tests.sh" >"$LOGS/tests-extra-bepinex.log" 2>&1 || { tail -30 "$LOGS/tests-extra-bepinex.log"; exit 1; }
    grep -E "^# (runtime|BepInEx|passed)" "$LOGS/tests-extra-bepinex.log"
  fi
  echo "== Regenerate docs/PROFILES.md and docs/examples from code"
  "$ROOT/Tests/run-tests.sh" --dump-docs "$ROOT/docs" >"$LOGS/docdump.log" 2>&1
  python3 -I "$ROOT/tools/compare_benchmarks.py" --self-test >>"$LOGS/tests-lib-bepinex.log"
fi

echo "== Reference check"
dotnet build "$ROOT/tools/RefCheck/RefCheck.csproj" -c Release -nologo -v quiet >/dev/null
dotnet "$ROOT/tools/RefCheck/bin/Release/net8.0/RefCheck.dll" "$DLL" "$ROOT/lib/bepinex" "$ROOT/lib/unity" | tee "$LOGS/refcheck-lib.log" | tail -1
if [ -n "${EXTRA_BEPINEX_DIR:-}" ]; then
  dotnet "$ROOT/tools/RefCheck/bin/Release/net8.0/RefCheck.dll" "$DLL" "$EXTRA_BEPINEX_DIR" "$ROOT/lib/unity" | tee "$LOGS/refcheck-extra.log" | tail -1
fi

if [ -n "${UNITY_CS_REFERENCE:-}" ]; then
  echo "== Unity API check against $UNITY_CS_REFERENCE"
  python3 -I "$ROOT/tools/check_unity_api.py" "$DLL" "$UNITY_CS_REFERENCE" | tee "$LOGS/unity-api-check.log" | tail -1
fi

echo "== Stage"
PLUGIN_DIR="$STAGE/install/BepInEx/plugins/CRIXOptimizer"
mkdir -p "$PLUGIN_DIR"
cp "$DLL" "$PLUGIN_DIR/"
cp "$ROOT/LICENSE" "$ROOT/README.md" "$PLUGIN_DIR/"
for f in README.md INSTALLATION.md BUILDING.md TROUBLESHOOTING.md LICENSES.md CHANGELOG.md LICENSE; do
  cp "$ROOT/$f" "$STAGE/"
done
cp -r "$ROOT/docs" "$STAGE/docs"
mkdir -p "$STAGE/logs" && cp "$LOGS"/*.log "$STAGE/logs/"

# Complete corresponding source (GPL-3.0 section 6): everything needed to rebuild, nothing generated.
python3 -I - "$ROOT" "$STAGE/source" <<'PY'
import os, shutil, sys
root, dest = sys.argv[1], sys.argv[2]
skip_dirs = {"bin", "obj", "lib", "release", ".run", ".vs", ".idea", "__pycache__"}
for base, dirs, files in os.walk(root):
    dirs[:] = sorted(d for d in dirs if d not in skip_dirs)
    rel = os.path.relpath(base, root)
    for name in sorted(files):
        target = os.path.join(dest, rel, name)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copy2(os.path.join(base, name), target)
PY

(cd "$STAGE" && find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt)

echo "== Zip"
ZIP="$OUT/CRIXOptimizer-$VERSION.zip"
rm -f "$ZIP"
(cd "$OUT/.stage" && TZ=UTC find "CRIXOptimizer-$VERSION" -exec touch -t 202610120000 {} + && zip -q -X -r "$ZIP" "CRIXOptimizer-$VERSION")
cp "$DLL" "$OUT/CRIXOptimizer.dll"
rm -rf "$OUT/.stage"

echo "== Done"
ls -l "$OUT/CRIXOptimizer.dll" "$ZIP"
sha256sum "$OUT/CRIXOptimizer.dll" "$ZIP"

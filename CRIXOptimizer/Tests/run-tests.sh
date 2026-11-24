#!/usr/bin/env bash
# Builds the plugin and the test suite, then runs the tests on Mono (the runtime family Unity uses) with
# the HarmonyX/MonoMod builds that BepInEx 5 ships. Requires: .NET SDK 8+, mono, and lib/ populated by
# tools/setup-references.sh.
#
#   Tests/run-tests.sh                 run against lib/bepinex/BepInEx.dll (5.4.21 by default)
#   BEPINEX_DLL=/path/BepInEx.dll Tests/run-tests.sh   run against another BepInEx build
#   Tests/run-tests.sh <name-filter>   run a subset
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
RUN="$HERE/.run"
NUGET="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet build "$ROOT/CRIXOptimizer.csproj" -c Release -nologo -v quiet -p:RefMode=Lib
dotnet build "$HERE/CRIXOptimizer.Tests/CRIXOptimizer.Tests.csproj" -c Release -nologo -v quiet -p:RefMode=Lib
dotnet build "$HERE/Runner/Runner.csproj" -c Release -nologo -v quiet

rm -rf "$RUN" && mkdir -p "$RUN"
cp "$HERE/Runner/bin/Release/net472/Runner.exe" "$RUN/"
cp "$HERE/CRIXOptimizer.Tests/bin/Release/CRIXOptimizer.Tests.dll" "$RUN/"
cp "$ROOT/bin/Release/CRIXOptimizer.dll" "$RUN/"
cp "$HERE/FakeGame/bin/Release/netstandard2.1/Assembly-CSharp.dll" "$RUN/"
cp "$ROOT/lib/bepinex/0Harmony.dll" "$RUN/"
cp "${BEPINEX_DLL:-$ROOT/lib/bepinex/BepInEx.dll}" "$RUN/BepInEx.dll"
cp "$NUGET/monomod.runtimedetour/22.1.29.1/lib/net35/MonoMod.RuntimeDetour.dll" "$RUN/"
cp "$NUGET/monomod.utils/22.1.29.1/lib/net35/MonoMod.Utils.dll" "$RUN/"
cp "$NUGET/mono.cecil/0.10.4/lib/net35/"Mono.Cecil*.dll "$RUN/"
cp "$ROOT/lib/unity/"*.dll "$RUN/"

cd "$RUN"
mono --debug Runner.exe "$@"

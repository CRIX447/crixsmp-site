# Building CRIX OPTIMIZER

CRIX does **not** link against Gorilla Tag's game assembly (`Assembly-CSharp.dll`). Game types are resolved by name
at runtime, so building only needs three sets of compile-time references:

| Reference | Version used for releases | Where it comes from |
|---|---|---|
| BepInEx (`BepInEx.dll`) | 5.4.21 (oldest supported, so no newer-only API slips in) | your game's `BepInEx/core`, or built from the [BepInEx v5 source](https://github.com/BepInEx/BepInEx) tag `v5.4.21` |
| HarmonyX (`0Harmony.dll`) | 2.9.0 (the version BepInEx 5.4.21-5.4.23 ships) | your game's `BepInEx/core`, or NuGet package `HarmonyX` 2.9.0 (net35 build) |
| Unity engine modules (`UnityEngine.*Module.dll`) | Unity 2021.3 API surface | your game's `Gorilla Tag_Data/Managed`, or NuGet package `UnityEngine.Modules` 2021.3.33 |

The target framework is **.NET Standard 2.1**, the same as Potassium 1.0.0. Gorilla Tag PC itself runs **Unity
6000.1.9f1** (Unity 6) with URP 17. Compiling against the older 2021.3 API keeps CRIX to members that exist in both;
`tools/check_unity_api.py` (below) checks the result against Unity 6's published source.

## Option A: build against your installed game (Windows)

Requirements: .NET SDK 8 (or newer), Gorilla Tag with BepInEx 5 installed and started once.

```powershell
cd CRIXOptimizer
dotnet build -c Release
# custom install location:
dotnet build -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\Gorilla Tag"
# build and copy straight into BepInEx/plugins/CRIXOptimizer:
dotnet build -c Release -p:CopyToGame=true
```

`Directory.Build.props` picks `RefMode=Game` automatically when it finds
`<GamePath>\Gorilla Tag_Data\Managed\UnityEngine.CoreModule.dll` and `<GamePath>\BepInEx\core\BepInEx.dll`.

## Option B: build without the game (Linux/macOS/Windows, CI)

Requirements: .NET SDK 8+, `git`, `curl`, `unzip`, network access to `api.nuget.org` and `github.com`.

```bash
cd CRIXOptimizer
tools/setup-references.sh          # fills lib/ (BepInEx 5.4.21 built from source, HarmonyX 2.9.0, Unity 2021.3.33 modules)
dotnet build -c Release -p:RefMode=Lib
```

`lib/` holds compile-time references only. It is git-ignored and never packaged.
`tools/setup-references.sh v5.4.23.5` builds against another BepInEx tag.

Output: `bin/Release/CRIXOptimizer.dll` (a single file with no extra dependencies to ship).

## Tests

Requirements: everything from option B, plus **Mono** (`apt install mono-complete` on Debian/Ubuntu). Tests run on
Mono because Unity's scripting runtime is Mono-based, and they use the exact HarmonyX/MonoMod builds BepInEx ships.

```bash
Tests/run-tests.sh                                   # all tests, BepInEx from lib/
Tests/run-tests.sh Adaptive                          # name filter
BEPINEX_DLL=/path/to/other/BepInEx.dll Tests/run-tests.sh   # same suite against another BepInEx build
python3 tools/compare_benchmarks.py --self-test
```

What the suite covers and what it cannot cover is described in [docs/TEST_REPORT.md](docs/TEST_REPORT.md).

## Reference check

`tools/RefCheck` resolves every type and member the built DLL references against a folder of assemblies, which
proves API compatibility with another BepInEx/Unity build without running the game:

```bash
dotnet run --project tools/RefCheck -c Release -- bin/Release/CRIXOptimizer.dll lib/bepinex lib/unity
dotnet run --project tools/RefCheck -c Release -- bin/Release/CRIXOptimizer.dll "<Gorilla Tag>/BepInEx/core" "<Gorilla Tag>/Gorilla Tag_Data/Managed"
```

The second form checks against your actual game install, which is the best offline check that the plugin matches
your game's Unity version.

Without a game install, `tools/check_unity_api.py` compares every Unity member the DLL references with Unity's
published C# source at the game's version (names and [Obsolete] markers, not binary signatures):

```bash
git clone --depth 1 --filter=blob:none --sparse --branch 6000.1.9f1 \
    https://github.com/Unity-Technologies/UnityCsReference ucs && (cd ucs && git sparse-checkout set Runtime Modules)
python3 tools/check_unity_api.py bin/Release/CRIXOptimizer.dll ucs
UNITY_CS_REFERENCE=ucs tools/package-release.sh      # also runs it during packaging
```

## Release package

```bash
tools/package-release.sh            # build + tests + doc generation + reference check + zip
tools/package-release.sh --skip-tests
```

Creates `release/CRIXOptimizer.dll`, `release/CRIXOptimizer-<version>.zip` (install folder, full source,
documentation, logs, SHA-256 checksums) and `release/logs/`.

## Project layout

```
CRIXOptimizer/
  CRIXOptimizer.csproj, CRIXOptimizer.sln, Directory.Build.props
  Plugin.cs, ModInfo.cs        BepInEx entry point
  Configuration/               config binding, profiles, presets, validation
  Core/                        Unity-free logic: statistics, settings ledger, detectors (unit-tested)
  Optimization/                runtime (composition root, main loop, fault isolation), live flags
  CPU/                         game patches: throttles, staggering, entity scans, sanitizer, patch catalog
  GPU/                         engine graphics settings, render-pipeline detection, cosmetic culling
  Memory/                      GC / asset-unload policies, memory monitor
  Adaptive/                    adaptive controller and quality ladder
  Diagnostics/                 frame monitor, GPU timing, XR runtime info, benchmark recorder, system report
  Compatibility/               plugin inventory, Harmony overlap scan, environment (apps/hooks) scanner
  VirtualDesktop/ Streaming/ Discord/ ReShade/   detection and advice; ReShade guard; read-only OBS WebSocket
                               and Streamlabs status clients
  UI/                          dashboard overlay and text
  Utilities/                   name-based game type index, compiled accessors, patch set, helpers
  Tests/                       stand-in game assembly, test suite, Mono runner
  tools/                       setup-references.sh, package-release.sh, compare_benchmarks.py, RefCheck,
                               check_unity_api.py
  docs/                        guides, generated profile reference, build and test reports
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together.

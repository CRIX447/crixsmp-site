#!/usr/bin/env bash
# Populates CRIXOptimizer/lib with COMPILE-TIME reference assemblies so the plugin can be built
# without a Gorilla Tag install (RefMode=Lib). Nothing in lib/ is shipped or committed.
#
#   lib/unity/    UnityEngine.*Module.dll   from NuGet package UnityEngine.Modules 2021.3.33 (nuget.org)
#   lib/bepinex/  0Harmony.dll              from NuGet package HarmonyX 2.9.0 (nuget.org, MIT)
#   lib/bepinex/  BepInEx.dll               built from the BepInEx v5 source at tag $BEPINEX_TAG (MIT)
#
# Requirements: bash, curl, unzip, git, .NET SDK 8+.
# Usage: tools/setup-references.sh [bepinex-tag]   (default v5.4.21)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
LIB="$ROOT/lib"
WORK="${CRIX_REF_WORKDIR:-$ROOT/lib/.work}"
BEPINEX_TAG="${1:-v5.4.21}"
UNITY_MODULES_VERSION="2021.3.33"
HARMONYX_VERSION="2.9.0"

mkdir -p "$LIB/unity" "$LIB/bepinex" "$WORK"

fetch_nupkg() { # id version dest-dir
  local id="$1" ver="$2" dest="$3" lid
  lid="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  if [ ! -f "$dest/.done" ]; then
    mkdir -p "$dest"
    echo "Downloading $id $ver from nuget.org"
    curl -sSfL -o "$dest/pkg.nupkg" "https://api.nuget.org/v3-flatcontainer/$lid/$ver/$lid.$ver.nupkg"
    (cd "$dest" && unzip -q -o pkg.nupkg && touch .done)
  fi
}

# 1. Unity engine modules (reference only). The monolithic UnityEngine.dll is skipped on purpose.
fetch_nupkg UnityEngine.Modules "$UNITY_MODULES_VERSION" "$WORK/unity-modules"
find "$WORK/unity-modules/lib/net45" -name 'UnityEngine.*.dll' -exec cp {} "$LIB/unity/" \;

# 2. HarmonyX - the net35 build is the one BepInEx 5 ships in BepInEx/core.
fetch_nupkg HarmonyX "$HARMONYX_VERSION" "$WORK/harmonyx"
cp "$WORK/harmonyx/lib/net35/0Harmony.dll" "$LIB/bepinex/0Harmony.dll"

# 3. BepInEx.dll from source (nuget.bepinex.dev is not always reachable, the source always is).
SRC="$WORK/bepinex-$BEPINEX_TAG"
if [ ! -d "$SRC/.git" ]; then
  git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$BEPINEX_TAG" https://github.com/BepInEx/BepInEx "$SRC"
fi
BUILD="$WORK/bepinex-build-$BEPINEX_TAG"
rm -rf "$BUILD" && mkdir -p "$BUILD"
cp -r "$SRC/BepInEx/." "$BUILD/"
rm -f "$BUILD/BepInEx.csproj"
cat > "$BUILD/nuget.config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
</configuration>
XML
cat > "$BUILD/BepInExRef.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net35</TargetFramework>
    <AssemblyName>BepInEx</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>\$(NoWarn);CS0618;CS0612;CS1591;CS0672;CS0436</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net35" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="HarmonyX" Version="$HARMONYX_VERSION" />
    <PackageReference Include="Mono.Cecil" Version="0.10.4" />
    <PackageReference Include="MonoMod.RuntimeDetour" Version="22.1.29.1" />
    <PackageReference Include="MonoMod.Utils" Version="22.1.29.1" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="$LIB/unity/UnityEngine.CoreModule.dll" Private="false" />
    <Reference Include="$LIB/unity/UnityEngine.InputLegacyModule.dll" Private="false" />
    <Reference Include="$LIB/unity/UnityEngine.IMGUIModule.dll" Private="false" />
    <Reference Include="$LIB/unity/UnityEngine.JSONSerializeModule.dll" Private="false" />
  </ItemGroup>
</Project>
XML
echo "Building BepInEx.dll ($BEPINEX_TAG) as a compile-time reference"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet build "$BUILD/BepInExRef.csproj" -c Release -nologo -v quiet -o "$BUILD/out" >"$WORK/bepinex-build.log" 2>&1 \
  || { echo "BepInEx reference build failed; see $WORK/bepinex-build.log"; tail -20 "$WORK/bepinex-build.log"; exit 1; }
cp "$BUILD/out/BepInEx.dll" "$LIB/bepinex/BepInEx.dll"

echo "References ready in $LIB:"
ls -1 "$LIB/bepinex"
echo "unity: $(ls -1 "$LIB/unity" | wc -l) module assemblies (UnityEngine.Modules $UNITY_MODULES_VERSION)"

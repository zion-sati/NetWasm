#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 2 ]]; then
  echo "usage: eng/qualify-sdk-consumers.sh <package-directory> <version> [--host-tools-version VERSION] [--sdk-evaluation-output PATH --producer-manifest PATH --asset-receipt-output PATH] [--skip-test-consumers]" >&2
  exit 2
fi

source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
package_root="$(cd "$1" && pwd -P)"
version="$2"
shift 2
host_tools_version="$version"
skip_test_consumers=false
sdk_evaluation_output=""
producer_manifest=""
asset_receipt_output=""
while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --host-tools-version)
      [[ "$#" -ge 2 ]] || { echo "--host-tools-version requires a version" >&2; exit 2; }
      host_tools_version="$2"
      shift 2
      ;;
    --skip-test-consumers)
      skip_test_consumers=true
      shift
      ;;
    --sdk-evaluation-output)
      [[ "$#" -ge 2 ]] || { echo "--sdk-evaluation-output requires a path" >&2; exit 2; }
      sdk_evaluation_output="$2"
      shift 2
      ;;
    --producer-manifest)
      [[ "$#" -ge 2 ]] || { echo "--producer-manifest requires a path" >&2; exit 2; }
      producer_manifest="$2"
      shift 2
      ;;
    --asset-receipt-output)
      [[ "$#" -ge 2 ]] || { echo "--asset-receipt-output requires a path" >&2; exit 2; }
      asset_receipt_output="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done
if [[ -n "$sdk_evaluation_output" ]]; then
  mkdir -p "$(dirname "$sdk_evaluation_output")"
  sdk_evaluation_output="$(cd "$(dirname "$sdk_evaluation_output")" && pwd -P)/$(basename "$sdk_evaluation_output")"
fi
if [[ -n "$producer_manifest" || -n "$asset_receipt_output" ]]; then
  [[ -n "$producer_manifest" && -n "$asset_receipt_output" && -n "$sdk_evaluation_output" ]] || {
    echo "Asset receipt output requires SDK evaluation output and a producer manifest." >&2
    exit 2
  }
  producer_manifest="$(cd "$(dirname "$producer_manifest")" && pwd -P)/$(basename "$producer_manifest")"
  [[ -f "$producer_manifest" ]] || { echo "Producer manifest does not exist." >&2; exit 2; }
  mkdir -p "$(dirname "$asset_receipt_output")"
  asset_receipt_output="$(cd "$(dirname "$asset_receipt_output")" && pwd -P)/$(basename "$asset_receipt_output")"
fi
work_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/netwasm-runtime-host.XXXXXX")"
trap 'rm -rf "$work_root"' EXIT
consumer_root="$work_root/consumer with spaces"
app_root="$consumer_root/app"
app_project="$app_root/RuntimeHost.csproj"
package_cache_root="$consumer_root/packages"
mkdir -p "$package_cache_root"

run_log() {
  local log_path="$1"
  shift
  if ! "$@" > "$log_path" 2>&1; then
    echo "Qualification stage failed: $(basename "$log_path" .log)" >&2
    cat "$log_path" >&2
    return 1
  fi
}

assert_log_contains() {
  local log_path="$1"
  local expected="$2"
  if ! grep -Fq "$expected" "$log_path"; then
    cat "$log_path" >&2
    echo "Expected log to contain: $expected" >&2
    return 1
  fi
}

expect_failure_contains() {
  local log_path="$1"
  local expected="$2"
  shift 2
  if "$@" > "$log_path" 2>&1; then
    cat "$log_path" >&2
    echo "The expected failure did not occur: $expected" >&2
    return 1
  fi
  assert_log_contains "$log_path" "$expected"
}

assert_run_42() {
  local log_path="$1"
  shift
  run_log "$log_path" dotnet run "$@"
  local actual
  actual="$(tr -d '\r' < "$log_path" | tail -n 1)"
  if [[ "$actual" != 42 ]]; then
    echo "Qualification stage failed: $(basename "$log_path" .log)" >&2
    cat "$log_path" >&2
    echo "Expected application stdout to end with 42; received: $actual" >&2
    return 1
  fi
}

dotnet_package_root="$package_root"
dotnet_work_root="$work_root"
dotnet_package_cache_root="$package_cache_root"
if command -v cygpath >/dev/null 2>&1; then
  dotnet_package_root="$(cygpath -w "$package_root")"
  dotnet_work_root="$(cygpath -w "$work_root")"
  dotnet_package_cache_root="$(cygpath -w "$package_cache_root")"
fi

cat > "$work_root/NuGet.Config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$dotnet_package_root" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

export NUGET_PACKAGES="$dotnet_package_cache_root"
export DOTNET_CLI_HOME="$dotnet_work_root/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_UI_LANGUAGE=en-US

if [[ -n "${NETWASM_QUALIFY_DOTNET_ROOT:-}" ]]; then
  isolated_root="$(cd "$NETWASM_QUALIFY_DOTNET_ROOT" && pwd -P)"
  expected_dotnet="$isolated_root/dotnet"
  [[ -f "$expected_dotnet" ]] || expected_dotnet="$isolated_root/dotnet.exe"
  expected_dotnet="$(realpath "$expected_dotnet")"
  actual_dotnet="$(realpath "$(command -v dotnet)")"
  [[ "$actual_dotnet" == "$expected_dotnet" ]] || {
    echo "Qualification did not select the isolated dotnet host." >&2
    exit 1
  }
  [[ "$(dotnet --list-sdks | wc -l | tr -d ' ')" == 1 ]] || {
    echo "Qualification dotnet root contains more than one SDK." >&2
    exit 1
  }
  expected_major="${NETWASM_QUALIFY_SDK_VERSION%%.*}"
  if dotnet --list-runtimes | awk '{ print $2 }' | cut -d. -f1 | grep -Fvxq "$expected_major"; then
    echo "Qualification dotnet root contains another runtime major." >&2
    exit 1
  fi
fi

unset EMSDK EMSDK_ROOT EMSDK_NODE NETWASM_EMSDK_ROOT
unset NETWASM_NODE_PATH NETWASM_WASM_LD_PATH
unset NETWASM_WASM_OPT_PATH NETWASM_WASM_MERGE_PATH

if [[ -n "${NETWASM_QUALIFY_SDK_VERSION:-}" ]]; then
  cat > "$work_root/global.json" <<EOF
{
  "sdk": {
    "version": "$NETWASM_QUALIFY_SDK_VERSION",
    "rollForward": "disable",
    "allowPrerelease": true
  }
}
EOF
fi
cd "$work_root"
run_log "$work_root/template-install.log" \
  dotnet new install "$dotnet_package_root/NetWasm.Templates.$version.nupkg" \
    --nuget-source "$dotnet_package_root" --force
run_log "$work_root/app-create.log" \
  dotnet new netwasm-app -n RuntimeHost -o "$app_root"
if [[ -n "${NETWASM_QUALIFY_SDK_VERSION:-}" ]]; then
  if ! grep -Fq '"rollForward": "latestFeature"' "$app_root/global.json"; then
    echo "The template SDK selection has changed; minimum-SDK qualification needs review" >&2
    exit 1
  fi
  sed \
    -e "s/\"version\": \"[^\"]*\"/\"version\": \"$NETWASM_QUALIFY_SDK_VERSION\"/" \
    -e 's/"rollForward": "latestFeature"/"rollForward": "disable"/' \
    "$app_root/global.json" > "$app_root/global.json.tmp"
  mv "$app_root/global.json.tmp" "$app_root/global.json"
fi

# The .NET CLI selects global.json from its working directory, even when a
# project path points elsewhere. Keep every consumer command in the fixture.
cd "$app_root"
if [[ -n "${NETWASM_QUALIFY_SDK_VERSION:-}" ]]; then
  actual_sdk="$(dotnet --version)"
  if [[ "$actual_sdk" != "$NETWASM_QUALIFY_SDK_VERSION" ]]; then
    echo "Expected .NET SDK $NETWASM_QUALIFY_SDK_VERSION; selected $actual_sdk" >&2
    exit 1
  fi
  printf 'Qualifying minimum .NET SDK %s\n' "$actual_sdk"
fi

run_log "$work_root/app-restore-debug.log" \
  dotnet restore "$app_project" --configfile "$work_root/NuGet.Config" \
    --disable-build-servers --nologo
evaluated_netwasm_sdk_version="$(dotnet msbuild "$app_project" \
  -getProperty:NetWasmSdkPackageVersion -nologo | tr -d '\r')"
evaluated_netwasm_sdk_root="$(dotnet msbuild "$app_project" \
  -getProperty:NetWasmSdkPackageRoot -nologo | tr -d '\r')"
if [[ "$evaluated_netwasm_sdk_version" != "$version" || -z "$evaluated_netwasm_sdk_root" ]]; then
  echo "The evaluated NetWasm SDK package does not match the candidate." >&2
  exit 1
fi
if [[ -n "$sdk_evaluation_output" ]]; then
  python3 - "$sdk_evaluation_output" "$evaluated_netwasm_sdk_version" \
    "$evaluated_netwasm_sdk_root" <<'PY'
import json
import sys
from pathlib import Path

output = Path(sys.argv[1])
if output.exists():
    raise SystemExit(f"SDK evaluation evidence already exists: {output}")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({
    "packageVersion": sys.argv[2],
    "packageRoot": str(Path(sys.argv[3]).resolve()),
}, indent=2) + "\n", encoding="utf-8")
PY
fi
if [[ -n "$asset_receipt_output" ]]; then
  python3 "$source_root/eng/verify-managed-candidate-assets.py" \
    --assets "$app_root/obj/project.assets.json" \
    --sdk-evaluation "$sdk_evaluation_output" \
    --packages-root "$package_cache_root" \
    --producer-manifest "$producer_manifest" \
    --candidate-version "$version" \
    --released-baseline-version "$host_tools_version" \
    --output "$asset_receipt_output"
fi
if [[ -n "${NETWASM_QUALIFY_SDK_VERSION:-}" ]]; then
  evaluated_sdk="$(dotnet msbuild "$app_project" -getProperty:NETCoreSdkVersion -nologo | tr -d '\r')"
  if [[ "$evaluated_sdk" != "$NETWASM_QUALIFY_SDK_VERSION" ]]; then
    echo "Expected MSBuild SDK $NETWASM_QUALIFY_SDK_VERSION; evaluated $evaluated_sdk" >&2
    exit 1
  fi
  printf 'MSBuild evaluated .NET SDK %s\n' "$evaluated_sdk"
fi
run_log "$work_root/app-build-debug.log" \
  dotnet build "$app_project" -c Debug --no-restore \
    --disable-build-servers --nologo -v:normal
assert_log_contains "$work_root/app-build-debug.log" 'NetWasm host tools: Node=Package '
assert_log_contains "$work_root/app-build-debug.log" 'wasm-ld=Package '
assert_log_contains "$work_root/app-build-debug.log" 'Binaryen=native'
assert_run_42 "$work_root/app-run-debug.log" \
  --project "$app_project" -c Debug --no-build --no-restore \
  --disable-build-servers
host_rid="$(dotnet msbuild "$app_project" \
  -getProperty:NETCoreSdkRuntimeIdentifier -nologo | tr -d '\r')"
[[ -n "$host_rid" ]] || {
  echo "The SDK did not evaluate a development-host RID" >&2
  exit 1
}
host_package_root="$package_cache_root/netwasm.hosttools.$host_rid/$host_tools_version"
node_name=node
if [[ "$host_rid" == win-* ]]; then
  node_name=node.exe
fi
node_override="$consumer_root/override/$node_name"
mkdir -p "$(dirname "$node_override")"
cp -p "$host_package_root/tools/bin/$node_name" "$node_override"
dotnet_node_override="$node_override"
if command -v cygpath >/dev/null 2>&1; then
  dotnet_node_override="$(cygpath -w "$node_override")"
fi
export NETWASM_NODE_PATH="$dotnet_node_override"
run_log "$work_root/valid-node-override.log" \
  dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
    -v:normal -nologo
assert_log_contains "$work_root/valid-node-override.log" 'Node=Override '
unset NETWASM_NODE_PATH
shadow_path="$consumer_root/conflicting-path"
mkdir -p "$shadow_path"
for tool in node wasm-ld wasm-opt wasm-merge; do
  tool_name="$tool"
  if [[ "$host_rid" == win-* ]]; then
    tool_name="$tool.exe"
  fi
  printf '%s\n' 'intentionally invalid PATH tool' > "$shadow_path/$tool_name"
  chmod +x "$shadow_path/$tool_name"
done
run_log "$work_root/conflicting-path.log" \
  env PATH="$shadow_path:$PATH" dotnet msbuild "$app_project" \
    -target:NetWasmSdkResolveBuildEnvironment -v:normal -nologo
assert_log_contains "$work_root/conflicting-path.log" 'Node=Package '
assert_log_contains "$work_root/conflicting-path.log" 'wasm-ld=Package '
assert_log_contains "$work_root/conflicting-path.log" 'Binaryen=native'
expect_failure_contains "$work_root/missing-host-package.log" 'NWSDK042' \
  dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
    -p:NetWasmHostToolsPackageRoot="$consumer_root/absent-host-package" -nologo
export NETWASM_NODE_PATH="$consumer_root/absent-node"
expect_failure_contains "$work_root/invalid-node-override.log" 'NWSDK040' \
  dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
    -nologo
unset NETWASM_NODE_PATH
expect_failure_contains "$work_root/unsupported-host.log" 'NWSDK041' \
  dotnet msbuild "$app_project" -target:NetWasmSdkValidateHostToolsHost \
    -p:NETCoreSdkRuntimeIdentifier=freebsd-x64 -nologo
expect_failure_contains "$work_root/emulated-host.log" 'NWSDK044' \
  dotnet msbuild "$app_project" -target:NetWasmSdkValidateHostToolsHost \
    -p:NETCoreSdkRuntimeIdentifier=win-x64 \
    -p:NetWasmBuildHostOSArchitecture=Arm64 -nologo
run_log "$work_root/app-clean-debug.log" \
  dotnet clean "$app_project" -c Debug --disable-build-servers --nologo
assert_run_42 "$work_root/app-after-clean.log" \
  --project "$app_project" -c Debug --no-restore --disable-build-servers

for target in wasm32 wasm64; do
  target_arguments=(-p:NetWasmTarget="$target")
  if [[ "$target" = wasm64 ]]; then
    target_arguments+=(-p:NetWasmRawWasm=true)
  fi
  rm -rf "$app_root/obj" "$app_root/bin"
  run_log "$work_root/app-restore-$target.log" \
    dotnet restore "$app_project" --configfile "$work_root/NuGet.Config" \
      --disable-build-servers --nologo "${target_arguments[@]}"
  run_log "$work_root/app-build-$target.log" \
    dotnet build "$app_project" -c Release --no-restore \
      --disable-build-servers --nologo "${target_arguments[@]}"
  assert_run_42 "$work_root/app-run-$target.log" \
    --project "$app_project" -c Release --no-build --no-restore \
    --disable-build-servers "${target_arguments[@]}"
  if [[ "$target" = wasm32 ]]; then
    run_log "$work_root/app-publish-local.log" \
      dotnet publish "$app_project" -c Release --no-restore \
        --disable-build-servers --nologo \
        -p:NetWasmPublishTarget=portable -o "$consumer_root/publish/local"
    run_log "$work_root/app-publish-browser.log" \
      dotnet publish "$app_project" -c Release --no-restore \
        --disable-build-servers --nologo \
        -p:NetWasmPublishTarget=browser -o "$consumer_root/publish"
    [[ -f "$consumer_root/publish/local/deployment.json" ]] || {
      echo "Portable publish is missing deployment.json" >&2
      exit 1
    }
    [[ -f "$consumer_root/publish/browser/deployment.json" ]] || {
      echo "Browser publish is missing deployment.json" >&2
      exit 1
    }
    [[ -f "$consumer_root/publish/browser/netwasm.browser.mjs" ]] || {
      echo "Browser publish is missing its entry module" >&2
      exit 1
    }
    if find "$consumer_root/publish" -type f \( \
        -name node -o -name node.exe -o \
        -name wasm-ld -o -name wasm-ld.exe -o \
        -name wasm-opt -o -name wasm-opt.exe -o \
        -name wasm-merge -o -name wasm-merge.exe \) | grep -q .; then
      echo "Publish output contains a development-host executable" >&2
      exit 1
    fi
    run_log "$work_root/app-lock-restore.log" \
      dotnet restore "$app_project" --configfile "$work_root/NuGet.Config" \
        -p:RestorePackagesWithLockFile=true --disable-build-servers --nologo
    [[ -f "$app_root/packages.$host_rid.lock.json" ]] || {
      echo "Restore did not create the host-qualified lock file" >&2
      exit 1
    }
    run_log "$work_root/app-locked-restore.log" \
      dotnet restore "$app_project" --configfile "$work_root/NuGet.Config" \
        -p:RestorePackagesWithLockFile=true --locked-mode \
        --disable-build-servers --nologo
  fi
done

if [[ "${NETWASM_QUALIFY_SDK_VERSION:-}" == 11.* ]]; then
  cat > "$app_root/Program.cs" <<'EOF'
using System;

public sealed record Cat(int Value);
public sealed record Dog(int Value);
public union Pet(Cat, Dog);
public sealed class Holder<T>(T value)
{
    public T Value = value;
}

public static class Program
{
    public static int Main()
    {
        Pet[] values = [new Cat(20), new Dog(22)];
        var holder = new Holder<Pet>(values[1]);
        object boxed = holder.Value;
        var before = GC.CollectionCount(0);
        for (var index = 0; index < 4096; index++)
        {
            _ = new byte[1024];
        }
        GC.Collect();
        var after = GC.CollectionCount(0);
        var sum = values[0] switch { Cat cat => cat.Value, Dog dog => dog.Value }
            + ((Pet)boxed switch { Cat cat => cat.Value, Dog dog => dog.Value });
        if (after <= before || sum != 42 || holder.Value is not Dog { Value: 22 })
        {
            return 1;
        }
        Console.WriteLine(42);
        return 0;
    }
}
EOF
  for configuration in Debug Release; do
    for target in wasm32 wasm64; do
      target_arguments=(-p:NetWasmTarget="$target")
      if [[ "$target" = wasm64 ]]; then
        target_arguments+=(-p:NetWasmRawWasm=true)
      fi
      rm -rf "$app_root/obj" "$app_root/bin"
      run_log "$work_root/csharp15-gc-restore-$configuration-$target.log" \
        dotnet restore "$app_project" --configfile "$work_root/NuGet.Config" \
          --disable-build-servers --nologo "${target_arguments[@]}"
      run_log "$work_root/csharp15-gc-build-$configuration-$target.log" \
        dotnet build "$app_project" -c "$configuration" --no-restore \
          --disable-build-servers --nologo -p:LangVersion=15.0 \
          -p:NetWasmOptimization=None "${target_arguments[@]}"
      assert_run_42 "$work_root/csharp15-gc-run-$configuration-$target.log" \
        --project "$app_project" -c "$configuration" --no-build --no-restore \
        --disable-build-servers -p:LangVersion=15.0 \
        -p:NetWasmOptimization=None "${target_arguments[@]}"
    done
  done
fi

library_root="$consumer_root/library"
library_project="$library_root/RuntimeLibrary.csproj"
library_packages="$consumer_root/library-packages"
mkdir -p "$library_packages"
run_log "$work_root/library-create.log" \
  dotnet new netwasm-lib -n RuntimeLibrary -o "$library_root"
run_log "$work_root/library-restore.log" \
  dotnet restore "$library_project" --configfile "$work_root/NuGet.Config" \
    --disable-build-servers --nologo
if grep -Fqi 'NetWasm.HostTools.' "$library_root/obj/project.assets.json"; then
  echo "A plain library restored a development-host tools package" >&2
  exit 1
fi
run_log "$work_root/library-build.log" \
  dotnet build "$library_project" -c Release --no-restore \
    --disable-build-servers --nologo
run_log "$work_root/library-pack.log" \
  dotnet pack "$library_project" -c Release --no-restore \
    --disable-build-servers --nologo -o "$library_packages"
[[ -f "$library_packages/RuntimeLibrary.1.0.0.nupkg" ]] || {
  echo "Dual-target library package is missing" >&2
  exit 1
}

dotnet_library_packages="$library_packages"
if command -v cygpath >/dev/null 2>&1; then
  dotnet_library_packages="$(cygpath -w "$library_packages")"
fi
cat > "$work_root/NuGet.WithLibrary.Config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$dotnet_package_root" />
    <add key="library" value="$dotnet_library_packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

wasm_consumer_root="$consumer_root/transitive-wasm"
desktop_consumer_root="$consumer_root/transitive-desktop"
mkdir -p "$wasm_consumer_root" "$desktop_consumer_root"
cp "$app_root/global.json" "$wasm_consumer_root/global.json"
cat > "$wasm_consumer_root/TransitiveWasm.csproj" <<'EOF'
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netwasm0.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RuntimeLibrary" Version="1.0.0" />
  </ItemGroup>
</Project>
EOF
cat > "$desktop_consumer_root/TransitiveDesktop.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RuntimeLibrary" Version="1.0.0" />
  </ItemGroup>
</Project>
EOF
for root in "$wasm_consumer_root" "$desktop_consumer_root"; do
  cat > "$root/Program.cs" <<'EOF'
using System;
Console.WriteLine(RuntimeLibrary.Class1.GetMessage());
EOF
done
run_log "$work_root/transitive-wasm-restore.log" \
  dotnet restore "$wasm_consumer_root/TransitiveWasm.csproj" \
    --configfile "$work_root/NuGet.WithLibrary.Config" \
    --disable-build-servers --nologo
run_log "$work_root/transitive-desktop-restore.log" \
  dotnet restore "$desktop_consumer_root/TransitiveDesktop.csproj" \
    --configfile "$work_root/NuGet.WithLibrary.Config" \
    --disable-build-servers --nologo
if grep -Fqi 'NetWasm.HostTools.' \
    "$desktop_consumer_root/obj/project.assets.json"; then
  echo "A desktop consumer restored a development-host tools package" >&2
  exit 1
fi
assert_run_42 "$work_root/transitive-wasm-run.log" \
  --project "$wasm_consumer_root/TransitiveWasm.csproj" -c Debug \
  --no-restore --disable-build-servers
assert_run_42 "$work_root/transitive-desktop-run.log" \
  --project "$desktop_consumer_root/TransitiveDesktop.csproj" -c Debug \
  --no-restore --disable-build-servers

if [[ "$skip_test_consumers" == false ]]; then
test_root="$consumer_root/tunit"
mkdir -p "$test_root"
cp "$app_root/global.json" "$test_root/global.json"
cat > "$test_root/RuntimeHostTests.csproj" <<'EOF'
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <TargetFramework>netwasm0.1</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NetWasm.TUnit" Version="0.2.2" />
  </ItemGroup>
</Project>
EOF
cat > "$work_root/NuGet.WithTUnit.Config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$dotnet_package_root" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat > "$test_root/Tests.cs" <<'EOF'
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

public sealed class Tests
{
    [Test]
    public async Task AnswerIsFortyTwo()
    {
        await Assert.That(42).IsEqualTo(42);
    }
}
EOF
run_log "$work_root/tunit-restore.log" \
  dotnet restore "$test_root/RuntimeHostTests.csproj" \
    --configfile "$work_root/NuGet.WithTUnit.Config" \
    --disable-build-servers --nologo
run_log "$work_root/tunit-test.log" \
  dotnet test "$test_root/RuntimeHostTests.csproj" -c Debug --no-restore \
    --disable-build-servers --nologo
assert_log_contains "$work_root/tunit-test.log" 'Passed!'
run_log "$work_root/tunit-no-build.log" \
  dotnet test "$test_root/RuntimeHostTests.csproj" -c Debug \
    --no-build --no-restore --disable-build-servers --nologo
assert_log_contains "$work_root/tunit-no-build.log" 'Passed!'
cat > "$test_root/Tests.cs" <<'EOF'
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

public sealed class Tests
{
    [Test]
    public async Task AnswerIsFortyTwo()
    {
        await Assert.That(42).IsEqualTo(43);
    }
}
EOF
run_log "$work_root/tunit-failing-build.log" \
  dotnet build "$test_root/RuntimeHostTests.csproj" -c Debug --no-restore \
    --disable-build-servers --nologo
if dotnet test "$test_root/RuntimeHostTests.csproj" -c Debug \
    --no-build --no-restore --disable-build-servers --nologo \
    > "$work_root/tunit-failing-test.log" 2>&1; then
  echo "The failing TUnit canary unexpectedly passed" >&2
  exit 1
fi
assert_log_contains "$work_root/tunit-failing-test.log" 'Failed!'
assert_log_contains "$work_root/tunit-failing-test.log" 'AnswerIsFortyTwo'

adapter_project="$source_root/tests/NetWasm.Testing.VSTest.Tests.Adapter/NetWasm.Testing.VSTest.Tests.Adapter.csproj"
adapter_feed="$consumer_root/vstest-adapter-packages"
mkdir -p "$adapter_feed"
run_log "$work_root/vstest-adapter-restore.log" \
  dotnet restore "$adapter_project" \
    --configfile "$work_root/NuGet.WithTUnit.Config" \
    --disable-build-servers --nologo
run_log "$work_root/vstest-adapter-pack.log" \
  dotnet pack "$adapter_project" -c Release --no-restore \
    --disable-build-servers --nologo -o "$adapter_feed"
[[ -f "$adapter_feed/NetWasm.Testing.VSTest.Tests.Adapter.0.1.0-preview.2.nupkg" ]] || {
  echo "The qualification-only VSTest adapter package is missing" >&2
  exit 1
}
dotnet_adapter_feed="$adapter_feed"
if command -v cygpath >/dev/null 2>&1; then
  dotnet_adapter_feed="$(cygpath -w "$adapter_feed")"
fi
cat > "$work_root/NuGet.WithVSTest.Config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$dotnet_package_root" />
    <add key="qualification-adapter" value="$dotnet_adapter_feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF
vstest_root="$consumer_root/generic-vstest"
vstest_project="$vstest_root/GenericVSTest.csproj"
mkdir -p "$vstest_root"
cp "$app_root/global.json" "$vstest_root/global.json"
cat > "$vstest_project" <<EOF
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <TargetFramework>netwasm0.1</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NetWasm.Testing.VSTest" Version="$version" PrivateAssets="all" />
    <PackageReference Include="NetWasm.Testing.VSTest.Tests.Adapter" Version="0.1.0-preview.2" PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF
cat > "$vstest_root/Program.cs" <<'EOF'
public static class Program
{
    public static int Main() => 0;
}
EOF
run_log "$work_root/vstest-restore.log" \
  dotnet restore "$vstest_project" \
    --configfile "$work_root/NuGet.WithVSTest.Config" \
    --disable-build-servers --nologo
run_log "$work_root/vstest-test.log" \
  dotnet test "$vstest_project" -c Debug --no-restore \
    --disable-build-servers --nologo
assert_log_contains "$work_root/vstest-test.log" 'Passed!'
run_log "$work_root/vstest-no-build.log" \
  dotnet test "$vstest_project" -c Debug --no-build --no-restore \
    --disable-build-servers --nologo --list-tests
assert_log_contains "$work_root/vstest-no-build.log" 'NetWasm.GenericVSTest.Contract'
expect_failure_contains "$work_root/vstest-failing-test.log" 'Failed!' \
  env NETWASM_VSTEST_FAIL_CANARY=1 dotnet test "$vstest_project" -c Debug \
    --no-build --no-restore --disable-build-servers --nologo
assert_log_contains "$work_root/vstest-failing-test.log" 'NetWasm.GenericVSTest.Contract'
fi

corrupt_package_root="$consumer_root/corrupt-host-package"
cp -R "$host_package_root" "$corrupt_package_root"
wasm_opt_name=wasm-opt
if [[ "$host_rid" == win-* ]]; then
  wasm_opt_name=wasm-opt.exe
fi
printf '%s\n' 'invalid tool payload' > "$corrupt_package_root/tools/bin/$wasm_opt_name"
dotnet_corrupt_package_root="$corrupt_package_root"
if command -v cygpath >/dev/null 2>&1; then
  dotnet_corrupt_package_root="$(cygpath -w "$corrupt_package_root")"
fi
expect_failure_contains "$work_root/corrupt-host-package.log" 'NWSDK040' \
  dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
    -p:NetWasmHostToolsPackageRoot="$dotnet_corrupt_package_root" -nologo

if [[ "$host_rid" == linux-* ]]; then
  bundled_library="$host_package_root/tools/bin/libatomic.so.1"
  [[ -f "$bundled_library" ]] || {
    echo "Linux Node's bundled libatomic.so.1 is missing" >&2
    exit 1
  }
  library_probe_root="$consumer_root/libatomic-probe"
  cp -R "$host_package_root" "$library_probe_root"
  rm "$library_probe_root/tools/bin/libatomic.so.1"
  expect_failure_contains "$work_root/missing-libatomic.log" 'NWSDK040' \
    dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
      -p:NetWasmHostToolsPackageRoot="$library_probe_root" -nologo
  printf '%s\n' 'invalid libatomic payload' > "$library_probe_root/tools/bin/libatomic.so.1"
  expect_failure_contains "$work_root/corrupt-libatomic.log" 'NWSDK040' \
    dotnet msbuild "$app_project" -target:NetWasmSdkResolveBuildEnvironment \
      -p:NetWasmHostToolsPackageRoot="$library_probe_root" -nologo
fi

printf 'SDK consumer qualification PASS: %s %s\n' "$(uname -s)" "$(uname -m)"

#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
  echo "usage: eng/qualify-runtime-pack-host.sh <package-directory> <version>" >&2
  exit 2
fi

package_root="$(cd "$1" && pwd -P)"
version="$2"
work_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/netwasm-runtime-host.XXXXXX")"
trap 'rm -rf "$work_root"' EXIT

cat > "$work_root/NuGet.Config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$package_root" />
  </packageSources>
</configuration>
EOF

export NUGET_PACKAGES="$work_root/packages"
export DOTNET_CLI_HOME="$work_root/dotnet-home"
dotnet new install "$package_root/NetWasm.Templates.$version.nupkg" \
  --nuget-source "$package_root" --force >/dev/null
dotnet new netwasm-app -n RuntimeHost -o "$work_root/app" >/dev/null

for target in wasm32 wasm64; do
  target_arguments=(-p:NetWasmTarget="$target")
  if [[ "$target" = wasm64 ]]; then
    target_arguments+=(-p:NetWasmRawWasm=true)
  fi
  dotnet restore "$work_root/app/RuntimeHost.csproj" \
    --configfile "$work_root/NuGet.Config" \
    --disable-build-servers --nologo
  dotnet build "$work_root/app/RuntimeHost.csproj" \
    -c Release --no-restore --disable-build-servers --nologo \
    "${target_arguments[@]}"
done

printf 'Runtime-pack host qualification PASS: %s %s\n' "$(uname -s)" "$(uname -m)"

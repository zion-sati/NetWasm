#!/usr/bin/env bash
# Runs NetWasm.App's suites (tests/libraries) against a package feed that holds
# NetWasm.Sdk, NetWasm.App.Ref and NetWasm.App.Runtime at <version>, such as
# eng/build-packages.sh builds. Each TUnit project runs on
# desktop .NET 11, the oracle, and on netwasm0.1 (wasm32, SDK defaults) in Debug
# and Release; each desktop xUnit project runs on .NET 11. The suites are SDK
# consumers: they restore NetWasm.Sdk and NetWasm.App from the feed and the
# previous TUnit release from nuget.org. The committed HEAD is tested, in a
# disposable worktree.
set -euo pipefail

usage() {
  echo "usage: eng/test-netwasm-app.sh <package-directory> <version> [--lane desktop|netwasm] [--project NAME]... [--log-directory ABSOLUTE_NEW_DIRECTORY]" >&2
  exit 2
}

[[ "$#" -ge 2 ]] || usage
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
[[ -d "$1" ]] || { echo "Package directory does not exist: $1" >&2; exit 2; }
package_root="$(cd "$1" && pwd -P)"
version="$2"
shift 2
lanes=(desktop netwasm)
selected=()
log_directory=""
while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --lane)
      [[ "$#" -ge 2 && "$2" =~ ^(desktop|netwasm)$ ]] || usage
      lanes=("$2")
      shift 2
      ;;
    --project)
      [[ "$#" -ge 2 && -n "$2" ]] || usage
      selected+=("$2")
      shift 2
      ;;
    --log-directory)
      [[ "$#" -ge 2 && "$2" = /* ]] || { echo "--log-directory requires an absolute path" >&2; exit 2; }
      log_directory="$2"
      shift 2
      ;;
    *)
      usage
      ;;
  esac
done

for command in dotnet git python3; do
  command -v "${command}" >/dev/null 2>&1 || { echo "Missing required command: ${command}" >&2; exit 2; }
done
for package_id in NetWasm.Sdk NetWasm.App.Ref NetWasm.App.Runtime; do
  [[ -f "${package_root}/${package_id}.${version}.nupkg" ]] || {
    echo "The feed has no ${package_id} ${version}: ${package_root}" >&2
    exit 2
  }
done
if [[ -z "${log_directory}" ]]; then
  log_directory="${repository_root}/artifacts/netwasm-app-tests/$(date -u +%Y%m%dT%H%M%SZ)"
fi
[[ ! -e "${log_directory}" ]] || { echo "Refusing to reuse a log directory: ${log_directory}" >&2; exit 2; }
mkdir -p "${log_directory}"

test_root="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-app-tests.XXXXXX")"
test_root="$(cd "${test_root}" && pwd -P)"
source_root="${test_root}/source"
# NETWASM_APP_TESTS_KEEP=1 keeps the run's packages and each lane's build outputs for diagnosis; the source worktree
# is still removed.
cleanup() {
  if [[ -d "${source_root}" ]]; then
    git -C "${repository_root}" worktree remove --force "${source_root}" >/dev/null 2>&1 || true
  fi
  if [[ -n "${NETWASM_APP_TESTS_KEEP:-}" ]]; then
    echo "Kept: ${test_root}"
  else
    rm -rf "${test_root}"
  fi
}
trap cleanup EXIT

git -C "${repository_root}" rev-parse HEAD > "${log_directory}/source-head.txt"
git -C "${repository_root}" worktree add --quiet --detach "${source_root}" "$(< "${log_directory}/source-head.txt")"
python3 -B "${source_root}/eng/project-release-version.py" \
  --source-root "${source_root}" \
  --version "${version}" \
  --receipt "${log_directory}/release-version-projection.json" >/dev/null

xml_escape() {
  printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/"/\&quot;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
}
nuget_config="${test_root}/NuGet.Config"
printf '%s\n' \
  '<?xml version="1.0" encoding="utf-8"?>' \
  '<configuration>' \
  '  <packageSources>' \
  '    <clear />' \
  "    <add key=\"candidate\" value=\"$(xml_escape "${package_root}")\" />" \
  '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
  '  </packageSources>' \
  '</configuration>' > "${nuget_config}"
export NUGET_PACKAGES="${test_root}/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# The SDK resolver takes NetWasm.Sdk from the package cache, so restore it once first.
seed_project="${test_root}/seed/SeedSdk.csproj"
mkdir -p "$(dirname "${seed_project}")"
printf '%s\n' \
  '<Project Sdk="Microsoft.NET.Sdk">' \
  '  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>' \
  "  <ItemGroup><PackageReference Include=\"NetWasm.Sdk\" Version=\"[${version}]\" PrivateAssets=\"all\" /></ItemGroup>" \
  '</Project>' > "${seed_project}"
dotnet restore "${seed_project}" --configfile "${nuget_config}" --disable-build-servers --nologo \
  > "${log_directory}/seed.log" 2>&1 || { echo "Restoring NetWasm.Sdk ${version} failed; see ${log_directory}/seed.log" >&2; exit 1; }

tunit_projects=()
desktop_projects=()
while IFS= read -r project; do
  name="$(basename "${project}" .csproj)"
  if [[ "${#selected[@]}" -gt 0 ]] && ! printf '%s\n' "${selected[@]}" | grep -qxF "${name}"; then
    continue
  fi
  if grep -q 'Sdk="NetWasm.Sdk"' "${source_root}/${project}"; then
    tunit_projects+=("${project}")
  else
    desktop_projects+=("${project}")
  fi
done < <(cd "${source_root}" && ls tests/libraries/*/*.csproj | LC_ALL=C sort)
[[ "$(( ${#tunit_projects[@]} + ${#desktop_projects[@]} ))" -gt 0 ]] || { echo "No suite matched." >&2; exit 2; }

results=()
failed=0
# Runs one lane of one project, with its own artifacts so lanes never share outputs. The script keeps to bash 3.2,
# which macOS ships.
run_lane() {
  local project="$1" lane="$2"
  shift 2
  local name
  name="$(basename "${project}" .csproj)"
  local log="${log_directory}/${name}.${lane}.log"
  local artifacts="${test_root}/artifacts/${name}/${lane}"
  local status=0
  (
    cd "${source_root}"
    dotnet restore "${project}" --configfile "${nuget_config}" --disable-build-servers --nologo \
      -p:UseArtifactsOutput=true -p:ArtifactsPath="${artifacts}"
    "$@" -p:UseArtifactsOutput=true -p:ArtifactsPath="${artifacts}"
  ) > "${log}" 2>&1 || status=$?
  local result="PASS ${name} ${lane}"
  if [[ "${status}" -ne 0 ]]; then
    result="FAIL ${name} ${lane} (exit ${status}, ${log})"
    failed=1
  fi
  results+=("${result}")
  echo "${result}"
  [[ -n "${NETWASM_APP_TESTS_KEEP:-}" ]] || rm -rf "${artifacts}"
}

for project in ${tunit_projects[@]+"${tunit_projects[@]}"}; do
  for lane in "${lanes[@]}"; do
    if [[ "${lane}" == desktop ]]; then
      run_lane "${project}" desktop-net11.0 \
        dotnet run --project "${project}" --framework net11.0 --configuration Release \
          --no-restore --no-launch-profile --disable-build-servers
    else
      for configuration in Debug Release; do
        run_lane "${project}" "netwasm0.1-${configuration}" \
          dotnet test "${project}" --framework netwasm0.1 --configuration "${configuration}" \
            --no-restore --disable-build-servers --nologo
      done
    fi
  done
done
if printf '%s\n' "${lanes[@]}" | grep -qx desktop; then
  for project in ${desktop_projects[@]+"${desktop_projects[@]}"}; do
    run_lane "${project}" desktop-net11.0 \
      dotnet test "${project}" --configuration Release --no-restore --disable-build-servers --nologo
  done
fi

printf '%s\n' ${results[@]+"${results[@]}"} > "${log_directory}/summary.txt"
echo "Logs: ${log_directory}"
exit "${failed}"

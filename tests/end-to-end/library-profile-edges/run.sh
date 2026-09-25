#!/usr/bin/env bash
set -euo pipefail

verify_results() {
  # A zero launcher exit does not prove that async Main completed. Require the
  # complete ordered dispatcher result set, including no duplicate/missing row.
  awk '/^CASE [0-9]+ FAIL$/ { exit 1 }
       /^CASE [0-9]+ PASS$/ { if ($2 != seen++) exit 1 }
       END { if (seen != 15) exit 1 }' "$1"
}

snapshot_restore() {
  # NuGet may suffix the lock file with the SDK-selected runtime identifier.
  # Each isolated lane must produce exactly one lock file; never guess between
  # a stale ordinary lock and a new RID-specific restore.
  local candidate
  local lock_files=()
  for candidate in packages.lock.json packages.*.lock.json; do
    if [[ -f "$candidate" ]]; then lock_files+=("$candidate"); fi
  done
  if [[ "${#lock_files[@]}" -ne 1 ]]; then
    echo "Expected exactly one restore lock file; found ${#lock_files[@]}." >&2
    return 1
  fi
  shasum -a 256 "${lock_files[0]}" obj/project.assets.json > restore-sha256.txt
}

if [[ "${1:-}" == --verify-results ]]; then
  verify_results "$2"
  exit
fi

fixture="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -n "${NETWASM_LIBRARY_EDGE_ARTIFACT_DIR:-}" ]]; then
  artifacts="$NETWASM_LIBRARY_EDGE_ARTIFACT_DIR"
  [[ "$artifacts" == /* && ! -e "$artifacts" ]] || {
    echo 'Artifact directory must be absolute and new.' >&2
    exit 2
  }
  mkdir -p "$artifacts"
else
  artifacts="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-library-edges.XXXXXX")"
fi

# These are released-package compatibility witnesses, not a claim to execute
# this checkout's newly built compiler. Restore outside the repo with no sibling
# sources, local feeds, patched packages, or copied platform host.
cp -R "$fixture" "$artifacts/desktop"
cp -R "$fixture" "$artifacts/netwasm"
export NUGET_PACKAGES="$artifacts/packages"
shasum -a 256 "$fixture"/*.cs "$fixture"/*.csproj "$fixture"/*.props \
  "$fixture"/*.targets "$fixture/global.json" "$fixture/NuGet.Config" \
  > "$artifacts/source-sha256.txt"

for lane in desktop netwasm; do
  project=ProfileEdges.csproj
  if [[ "$lane" == desktop ]]; then project=ProfileEdges.Desktop.csproj; fi
  (
    cd "$artifacts/$lane"
    dotnet --version > dotnet-version.txt
    dotnet restore "$project" --use-lock-file --configfile NuGet.Config
    snapshot_restore
    dotnet run --project "$project" -c Release --no-restore
    shasum -a 256 -c restore-sha256.txt
  ) > "$artifacts/$lane.stdout" 2> "$artifacts/$lane.stderr"
  verify_results "$artifacts/$lane.stdout"
done

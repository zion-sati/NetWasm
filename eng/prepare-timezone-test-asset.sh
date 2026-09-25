#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  echo "Usage: $0 <output-directory>" >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$1"
output_dir="$(cd "$1" && pwd)"
timezone_source="${TZDIR:-/usr/share/zoneinfo}"
timezone_asset="$output_dir/timezones.nwtz"

dotnet run \
  --project "$repo_root/tools/NetWasm.TimeZones/NetWasm.TimeZones.csproj" \
  --configuration Release \
  --no-build \
  --no-restore \
  -- generate \
  --source "$timezone_source" \
  --zone UTC \
  --zone America/New_York \
  --zone Australia/Melbourne \
  --asset "$timezone_asset" \
  --brotli "$output_dir/timezones.nwtz.br" \
  --manifest "$output_dir/timezones.json" \
  --browser-loader "$output_dir/timezones.loader.mjs" \
  >"$output_dir/timezone-assets.txt"

printf '%s\n' "$timezone_asset"

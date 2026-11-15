#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  echo "Usage: $0 <output-directory>" >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$1"
output_dir="$(cd "$1" && pwd)"
# Use the pinned IANA release that NetWasm.Toolchain ships: CoreLib requires its
# data version, and a host's system zoneinfo can be a different release.
timezone_source="$repo_root/src/NetWasm.Toolchain/timezones/zoneinfo"
timezone_asset="$output_dir/timezones.nwtz"

dotnet run \
  --project "$repo_root/tools/NetWasm.TimeZones/NetWasm.TimeZones.csproj" \
  --configuration Release \
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

#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 4 ]]; then
  echo "usage: eng/verify-host-tools-repeatability.sh <rid> <version> <input-cache> <first-package>" >&2
  exit 2
fi

rid="$1"
version="$2"
input_cache="$3"
first_package="$4"
test -f "$first_package" || {
  echo "First host-tools package is missing: $first_package" >&2
  exit 1
}

repeat_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/netwasm-host-repeat.XXXXXX")"
trap 'rm -rf "$repeat_root"' EXIT
python3 eng/build-host-tools-package.py \
  --rid "$rid" --version "$version" \
  --output "$repeat_root/packages" --cache "$input_cache"
second_package="$repeat_root/packages/NetWasm.HostTools.$rid.$version.nupkg"
if ! cmp -s "$first_package" "$second_package"; then
  echo "Host-tools packaging is not byte-for-byte repeatable for $rid $version" >&2
  exit 1
fi
python3 - "$rid" "$version" "$first_package" <<'PY'
import hashlib
import sys
from pathlib import Path

rid, version, package = sys.argv[1:]
with Path(package).open("rb") as stream:
    digest = hashlib.file_digest(stream, "sha256").hexdigest()
print(f"Repeat-pack PASS: {rid} {version} sha256={digest}")
PY

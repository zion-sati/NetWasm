#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

tests=()
while IFS= read -r test_file; do
  tests+=("$test_file")
done < <(
  find eng compiler-qualification src tests \
    -type f -name '*.test.mjs' \
    ! -path '*/bin/*' \
    ! -path '*/obj/*' \
    -print | sort
)

if (( ${#tests[@]} == 0 )); then
  echo 'No JavaScript tests were found.' >&2
  exit 1
fi

node --test "${tests[@]}"

#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

"$repo_root/eng/verify-toolchain.sh" --qualification
dotnet test "$repo_root/NetWasm.slnx" --no-restore --nologo --maxcpucount:1

# Preserve the complete raw JavaScript interop boundary before exercising
# Component Model composition.
"$repo_root/tests/end-to-end/host-interop/javascript/run.sh"

"$repo_root/tests/end-to-end/component-model/run-component-diagnostics.sh"
"$repo_root/tests/end-to-end/component-model/run-command-diagnostics.sh"
"$repo_root/tests/end-to-end/component-model/run-jco-browser.sh"
"$repo_root/tests/end-to-end/component-model/run-string-component.sh"
"$repo_root/tests/end-to-end/component-model/run-resource-component.sh"
"$repo_root/tests/end-to-end/component-model/run-canonical-matrix.sh"
"$repo_root/tests/end-to-end/component-model/run-jco-wasi-browser.sh"
"$repo_root/tests/end-to-end/component-model/run-wasi-stream.sh"
"$repo_root/tests/end-to-end/component-model/run-async-js.sh"
"$repo_root/tests/end-to-end/component-model/run-mixed-boundary.sh"
"$repo_root/tests/end-to-end/component-model/run-trimming.sh"

printf 'NetWasm Component Model, WIT, WASI and JavaScript acceptance PASS\n'

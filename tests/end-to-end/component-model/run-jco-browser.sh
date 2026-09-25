#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/jco-browser"
component="$repo_root/tests/end-to-end/component-model/artifacts/scalar-component/component.first.wasm"
toolchain="$repo_root/eng/toolchain.json"
jco_version="$(node -p 'require(process.argv[1]).jco' "$toolchain")"

for tool in node npx; do
    command -v "$tool" >/dev/null || {
        echo "missing required tool: $tool" >&2
        exit 1
    }
done
[[ "$(npx jco --version)" = "$jco_version" ]]

"$repo_root/tests/end-to-end/component-model/run-scalar-component.sh"
mkdir -p "$artifact_root/first" "$artifact_root/second"
for repeat in first second; do
    npx jco transpile "$component" -o "$artifact_root/$repeat" \
        --name scalar --instantiation async --strict --quiet
done
for file in scalar.js scalar.d.ts scalar.core.wasm scalar.core2.wasm; do
    cmp "$artifact_root/first/$file" "$artifact_root/second/$file"
done

node "$repo_root/tests/end-to-end/component-model/run-jco-browser.mjs" "$artifact_root/first"
printf 'Component Model jco browser packaging PASS\n'

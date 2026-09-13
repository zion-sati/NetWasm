#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
docs_root="$repo_root/docs"

required_pages=(
    support-status.md
    diagnostics.md
    targets-and-outputs.md
    corelib-runtime.md
    cli-build-deployment.md
    sdk-quickstart.md
    timezone-assets.md
    gaps.md
    licensing.md
    maintenance.md
)

index="$docs_root/index.md"
[[ -f "$index" ]] || { echo "missing documentation index: $index" >&2; exit 1; }
for page in "${required_pages[@]}"; do
    rg -F -q "($page)" "$index" || {
        echo "documentation index '$index' does not link '$page'" >&2
        exit 1
    }
done

node_policy_documents=(
    "$repo_root/README.md"
    "$repo_root/QUICKSTART.md"
    "$docs_root/index.md"
    "$docs_root/sdk-quickstart.md"
    "$docs_root/cli-build-deployment.md"
    "$repo_root/src/NetWasm.Sdk/README.md"
    "$repo_root/src/NetWasm.Templates/README.md"
    "$repo_root/src/NetWasm.Toolchain/README.md"
)

for document in "${node_policy_documents[@]}"; do
    [[ -f "$document" ]] || continue
    rg -q 'Node(\.js)? (24\+|24 or newer)' "$document" || {
        echo "Node.js 24+ policy is missing from '$document'" >&2
        exit 1
    }
    if rg -i -q 'Node(\.js)? (20|21|22|23)(\+| or newer)' "$document"; then
        echo "unsupported Node.js minimum is present in '$document'" >&2
        exit 1
    fi
done

emsdk_node_documents=(
    "$docs_root/sdk-quickstart.md"
)

for document in "${emsdk_node_documents[@]}"; do
    [[ -f "$document" ]] || continue
    rg -F -q 'EMSDK_NODE' "$document" || {
        echo "activated Emscripten Node selection is missing from '$document'" >&2
        exit 1
    }
    if rg -q 'Node[^.\n]*(from|in) (the )?activated `PATH`' "$document"; then
        echo "stale PATH-only Node selection is present in '$document'" >&2
        exit 1
    fi
done

if [[ -f "$docs_root/sdk-quickstart.md" ]]; then
    rg -F -q 'Python 3' "$docs_root/sdk-quickstart.md" || {
        echo "emsdk Python 3 bootstrap prerequisite is missing from the SDK quickstart" >&2
        exit 1
    }
fi

if [[ -f "$repo_root/package.json" ]] &&
    ! rg -q '"node": ">=24\.0\.0"' "$repo_root/package.json"; then
    echo "package.json does not enforce the Node.js 24+ repository floor" >&2
    exit 1
fi

document="$docs_root/gaps.md"
rg -F -q '(support-status.md)' "$document" || {
    echo "gap reference '$document' does not link the support-status page" >&2
    exit 1
}

document="$docs_root/support-status.md"
for term in \
    'Complete / supported' \
    Conditional \
    'Partially qualified' \
    'In progress' \
    'Deferred' \
    'External toolchain blocker' \
    'Intentionally unsupported' \
    NW1010 cm64p2 'WASI 0.3' wasm32 wasm64 SIMD DWARF \
    'System.Net.Http' 'WASI-only HTTP' 'System.Xml' 'System.IO.Hashing' \
    'NetWasm.Ref' 'SDK/templates' 'developer-loop' TUnit \
    'stack-trace' Random Guid 'Guid.NewGuid' TimeZoneInfo open-generic \
    PackedSimd 'System.IO.File' FileStream Directory \
    'System.Net.Sockets' 'System.Diagnostics.Process' 'Task.Run'; do
    rg -F -q "$term" "$document" || {
        echo "support-status term '$term' is missing from '$document'" >&2
        exit 1
    }
done

for term in \
    'WASI 0.2.11 WIT closure' 'System.IO.File' FileStream Directory \
    'System.Net.Sockets' 'System.Diagnostics.Process' 'Task.Run'; do
    rg -F -q "$term" "$docs_root/LIMITATIONS.md" || {
        echo "WASI/platform limitation '$term' is missing from the limitations page" >&2
        exit 1
    }
done

[[ -f "$docs_root/verify-docs.sh" ]] || {
    echo "missing documentation verifier: $docs_root/verify-docs.sh" >&2
    exit 1
}

diagnostics_source="$repo_root/src/NetWasm.Compiler.Core/Diagnostics.cs"
[[ -f "$diagnostics_source" ]] || {
    echo "missing DiagnosticCode source: $diagnostics_source" >&2
    exit 1
}

while read -r name value; do
    if [[ "$name" == "GenericExpansion" ]]; then
        id="NWA2001"
    else
        printf -v id 'NW%04d' "$value"
    fi
    document="$docs_root/diagnostics.md"
    rg -F -q "\`$id\`" "$document" || {
        echo "DiagnosticCode.$name ($id) is missing from '$document'" >&2
        exit 1
    }
done < <(awk '/^[[:space:]]+[A-Za-z][A-Za-z0-9]+ = [0-9]+,?$/ {
    gsub(/,/, "", $0)
    print $1, $3
}' "$diagnostics_source")

document="$docs_root/diagnostics.md"
for id in NW1010 NWA2001; do
    rg -F -q "\`$id\`" "$document" || {
        echo "required diagnostic '$id' is missing from '$document'" >&2
        exit 1
    }
done

document="$docs_root/targets-and-outputs.md"
for term in wasm32 wasm64 cm32p2 cm64p2 'WASI 0.3' NW1010 silently; do
    rg -F -q "$term" "$document" || {
        echo "target contract term '$term' is missing from '$document'" >&2
        exit 1
    }
done

echo "NetWasm documentation parity and navigation checks passed."

#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
workspace_dir="$(cd "$script_dir/../.." && pwd)"
output_dir="$(mktemp -d)"
trap 'rm -rf "$output_dir"' EXIT

# The runtime is built by clang (emcc), and its portable range checks are written
# for clang's warnings: GCC's -Wtype-limits rejects checks that only a 64-bit host
# makes always true. The host contracts compile with clang wherever there is one.
if command -v clang >/dev/null 2>&1; then
  cc() { clang "$@"; }
fi

node --test "$script_dir/runtime-contract-imports.test.mjs"
node --test --experimental-test-coverage \
  --test-coverage-include='**/monotonic-clock-imports.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  "$script_dir/monotonic-clock-imports.test.mjs"
node --test "$script_dir/runtime-build-additional-source.test.mjs"
node --test --experimental-test-coverage \
  --test-coverage-include='**/output-stream-imports.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  "$script_dir/output-stream-imports.test.mjs"
node --test --experimental-test-coverage \
  --test-coverage-include='**/guest-environment-imports.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  "$script_dir/guest-environment-imports.test.mjs"
node --test --experimental-test-coverage \
  --test-coverage-include='**/read-only-asset-imports.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  "$script_dir/read-only-asset-imports.test.mjs"

cc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -I"$script_dir" \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/unmanaged_allocator_contract.c" \
  -o "$output_dir/unmanaged_allocator_contract"

"$output_dir/unmanaged_allocator_contract"

printf 'Unmanaged allocator contract PASS\n'

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/runtime_libc_initialization_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_libc_initializer.c" \
  -o "$output_dir/runtime_libc_initialization_contract"
"$output_dir/runtime_libc_initialization_contract"
printf 'Libc initialization contract PASS\n'

cc -std=c11 -Wall -Wextra -Werror -pedantic \
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c" \
  "$script_dir/netwasm_gc_core_contract.c" \
  -o "$output_dir/netwasm_gc_core_contract"
"$output_dir/netwasm_gc_core_contract"
echo "Independent NetWasmGC core contract PASS"

cc -std=c11 -Wall -Wextra -Werror -pedantic \
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c" \
  "$script_dir/netwasm_gc_available_run_contract.c" \
  -o "$output_dir/netwasm_gc_available_run_contract"
"$output_dir/netwasm_gc_available_run_contract"
echo "Independent NetWasmGC available-run contract PASS"

cc -std=c11 -Wall -Wextra -Werror -pedantic \
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c" \
  "$script_dir/netwasm_gc_recycling_contract.c" \
  -o "$output_dir/netwasm_gc_recycling_contract"
"$output_dir/netwasm_gc_recycling_contract"
echo "Independent NetWasmGC slot recycling contract PASS"

cc -std=c11 -Wall -Wextra -Werror -pedantic -DNETWASM_GC_STATISTICS \
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c" \
  "$script_dir/netwasm_gc_statistics_contract.c" \
  -o "$output_dir/netwasm_gc_statistics_contract"
"$output_dir/netwasm_gc_statistics_contract"
echo "Independent NetWasmGC diagnostic statistics contract PASS"

cc -std=c11 -Wall -Wextra -Werror -pedantic \
  "$script_dir/netwasm_gc_layout_contract.c" \
  -o "$output_dir/netwasm_gc_layout_contract"
"$output_dir/netwasm_gc_layout_contract"
echo "Independent NetWasmGC layout and endpoint contract PASS"

netwasm_gc_sources=(
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/lifetime.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/address_index.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/allocation.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/collection.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/ephemeron.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/finalization.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/identity_hash.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/lifecycle.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/metadata.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/pinning.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/reference_store.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/roots.c"
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/weak_reference.c"
)
cc -std=c11 -Wall -Wextra -Werror -pedantic \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" \
  "$script_dir/netwasm_gc_runtime_contract.c" \
  -o "$output_dir/netwasm_gc_runtime_contract"
"$output_dir/netwasm_gc_runtime_contract"
echo "Independent NetWasmGC runtime contract PASS"
cc -std=c11 -Wall -Wextra -Werror -pedantic \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/address_index.c" \
  "$script_dir/netwasm_gc_address_index_contract.c" \
  -o "$output_dir/netwasm_gc_address_index_contract"
"$output_dir/netwasm_gc_address_index_contract"
echo "NetWasmGC address index contract PASS"

# Compact's own core behind NetWasmGC's interface, with the shared adapters.
compact_gc_sources=(
  "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/compact_gc.c"
  "${netwasm_gc_sources[@]:1}"
)
for optimization in O0 Oz O3; do
  cc -std=c11 -Wall -Wextra -Werror -pedantic "-$optimization" -DNETWASM_GC_COMPACT \
    "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/compact_gc.c" \
    "$script_dir/netwasm_gc_core_contract.c" \
    -o "$output_dir/compact_gc_core_contract_$optimization"
  "$output_dir/compact_gc_core_contract_$optimization"
  cc -std=c11 -Wall -Wextra -Werror -pedantic "-$optimization" -DNETWASM_GC_COMPACT \
    "$workspace_dir/src/NetWasm.Runtime/collector/netwasm/compact_gc.c" \
    "$script_dir/compact_gc_contract.c" \
    -o "$output_dir/compact_gc_contract_$optimization"
  "$output_dir/compact_gc_contract_$optimization"
done
cc -std=c11 -Wall -Wextra -Werror -pedantic -DNETWASM_GC_COMPACT \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${compact_gc_sources[@]}" \
  "$script_dir/netwasm_gc_runtime_contract.c" \
  -o "$output_dir/compact_gc_runtime_contract"
"$output_dir/compact_gc_runtime_contract"
echo "Compact core and runtime contracts PASS"


frame_linker_flags=(-Wl,--gc-sections)
if [[ "$(uname -s)" = Darwin ]]; then
  frame_linker_flags=(-Wl,-dead_strip)
fi
cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  \
  "${frame_linker_flags[@]}" -I"$workspace_dir/src/NetWasm.Runtime" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" "$script_dir/nullable_array_readback_contract.c" \
  -o "$output_dir/nullable_array_readback_contract"
"$output_dir/nullable_array_readback_contract"
cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  -DNULLABLE_READBACK_TEST_HOOKS "${frame_linker_flags[@]}" \
  -I"$workspace_dir/src/NetWasm.Runtime" -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" "$script_dir/nullable_array_readback_contract.c" \
  -o "$output_dir/nullable_array_readback_hooks_contract"
"$output_dir/nullable_array_readback_hooks_contract"
echo "Nullable array readback and collector lifetime contract PASS"
cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  "${frame_linker_flags[@]}" -I"$workspace_dir/src/NetWasm.Runtime" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" "$script_dir/array_set_value_contract.c" \
  -o "$output_dir/array_set_value_contract"
"$output_dir/array_set_value_contract"
echo "Array boxed-value storage and collector lifetime contract PASS"

cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  "${frame_linker_flags[@]}" -I"$workspace_dir/src/NetWasm.Runtime" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" "$script_dir/object_clone_contract.c" \
  -o "$output_dir/object_clone_contract"
"$output_dir/object_clone_contract"
cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  -DCLONE_TEST_HOOKS "${frame_linker_flags[@]}" \
  -I"$workspace_dir/src/NetWasm.Runtime" -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "${netwasm_gc_sources[@]}" "$script_dir/object_clone_contract.c" \
  -o "$output_dir/object_clone_hooks_contract"
"$output_dir/object_clone_hooks_contract"
echo "Object shallow clone and collector lifetime contract PASS"

cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  "${frame_linker_flags[@]}" -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/frame_arena_contract.c" -o "$output_dir/frame_arena_contract"
"$output_dir/frame_arena_contract"
cc -std=c11 -O1 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  -DNETWASM_BUMP_COLLECTOR "${frame_linker_flags[@]}" \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/frame_arena_contract.c" -o "$output_dir/bump_frame_arena_contract"
"$output_dir/bump_frame_arena_contract"
echo "Frame arena ownership contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  "$script_dir/bump_arena_contract.c" \
  -o "$output_dir/bump_arena_contract"
"$output_dir/bump_arena_contract"
echo "Bump arena contract PASS"
cc -std=c11 -Wall -Wextra -Werror \
  "$script_dir/bump_default_capacity_contract.c" \
  -o "$output_dir/bump_default_capacity_contract"
"$output_dir/bump_default_capacity_contract"
echo "Bump default capacity contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/bump_weak_reference_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/bump_runtime.c" \
  -o "$output_dir/bump_weak_reference_contract"
"$output_dir/bump_weak_reference_contract"
echo "Bump weak-reference contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/weak_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/weak_handle_table.c" \
  -o "$output_dir/weak_handle_table_contract"
"$output_dir/weak_handle_table_contract"
echo "Weak-handle table contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/ephemeron_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/ephemeron_handle_table.c" \
  -o "$output_dir/ephemeron_handle_table_contract"
"$output_dir/ephemeron_handle_table_contract"
echo "Ephemeron-handle table contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/strong_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/strong_handle_table.c" \
  -o "$output_dir/strong_handle_table_contract"
"$output_dir/strong_handle_table_contract"
echo "Strong-handle table contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/command_diagnostics_contract.c" \
  -o "$output_dir/command_diagnostics_contract"
"$output_dir/command_diagnostics_contract"
echo "Command diagnostics contract PASS"


cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/gc_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/gc_handle_table.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/strong_handle_table.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/weak_handle_table.c" \
  -o "$output_dir/gc_handle_table_contract"
"$output_dir/gc_handle_table_contract"
echo "GC-handle table contract PASS"

cc -std=c11 -Wall -Wextra -Werror -DNETWASM_TARGET_WASM64 \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/object_data_address_resolver_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/object_data_address_resolver.c" \
  -o "$output_dir/object_data_address_resolver_contract"
"$output_dir/object_data_address_resolver_contract"
if sh -c '"$1" invalid >/dev/null 2>&1' sh \
    "$output_dir/object_data_address_resolver_contract" 2>/dev/null; then
  echo "Object-data address resolver accepted an invalid type id" >&2
  exit 1
fi
echo "Object-data address resolver contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/gc_metric_reader_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/gc_metric_reader.c" \
  -o "$output_dir/gc_metric_reader_contract"
"$output_dir/gc_metric_reader_contract"
"$output_dir/gc_metric_reader_contract" underflow
"$output_dir/gc_metric_reader_contract" pause
if sh -c '"$1" invalid >/dev/null 2>&1' sh \
    "$output_dir/gc_metric_reader_contract" 2>/dev/null; then
  echo "GC metric reader accepted an invalid metric" >&2
  exit 1
fi
if sh -c '"$1" invalid-support >/dev/null 2>&1' sh \
    "$output_dir/gc_metric_reader_contract" 2>/dev/null; then
  echo "GC metric reader accepted an invalid support query" >&2
  exit 1
fi
echo "GC metric reader contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/collector/identity_hash.c" \
  "$script_dir/collector_identity_hash_contract.c" \
  -o "$output_dir/collector_identity_hash_contract"
"$output_dir/collector_identity_hash_contract"

cc -std=c11 -Wall -Wextra -Werror \
    -I"$workspace_dir/src/NetWasm.Runtime" \
    "$workspace_dir/src/NetWasm.Runtime/collector/reference_store.c" \
    "$script_dir/collector_reference_store_contract.c" \
    -o "$output_dir/collector_reference_store_contract"
"$output_dir/collector_reference_store_contract"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/runtime_system_initialization_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_system_initializer.c" \
  -o "$output_dir/runtime-system-initialization-contract"
"$output_dir/runtime-system-initialization-contract"

cc -std=c11 -Wall -Wextra -Werror \
  -Dmalloc=runtime_test_environment_allocate \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  -c "$workspace_dir/src/NetWasm.Runtime/runtime_environment_reader.c" \
  -o "$output_dir/runtime_environment_reader.o"
cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/runtime_environment_reader_contract.c" \
  "$output_dir/runtime_environment_reader.o" \
  -o "$output_dir/runtime_environment_reader_contract"
"$output_dir/runtime_environment_reader_contract"
echo "Runtime Preview 2 environment reader contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_standard_output.c" \
  "$script_dir/runtime_standard_output_contract.c" \
  -o "$output_dir/runtime_standard_output_contract"
for output_case in normal validation dropped-first dropped-second dropped-partial invalid-result wrong-event; do
  "$output_dir/runtime_standard_output_contract" "$output_case"
done
echo "Runtime WASI 0.3 standard output contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_process_exit.c" \
  "$script_dir/runtime_process_exit_contract.c" \
  -o "$output_dir/runtime_process_exit_contract"
"$output_dir/runtime_process_exit_contract"
echo "Runtime Preview 2 exit contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_finalizer_safepoint.c" \
  "$script_dir/runtime_finalizer_safepoint_contract.c" \
  -o "$output_dir/runtime_finalizer_safepoint_contract"
"$output_dir/runtime_finalizer_safepoint_contract"
echo "Runtime finalizer safepoint contract PASS"

# Rename standard symbols in these in-memory facade tests so the executable's
# own libc, test diagnostics and coverage writers retain their host I/O.
cc -std=c11 -Wall -Wextra -Werror \
  -Dwritev=netwasm_contract_writev -Dclose=netwasm_contract_close \
  -Dlseek=netwasm_contract_lseek \
  -I"$script_dir/fake_stdio" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_stdio.c" \
  "$script_dir/runtime_stdio_contract.c" \
  -o "$output_dir/runtime_stdio_contract"
"$output_dir/runtime_stdio_contract"
echo "Runtime stdio buffering contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -Dwrite=netwasm_contract_write -Dwritev=netwasm_contract_writev \
  -Dclose=netwasm_contract_close -Dlseek=netwasm_contract_lseek \
  -D__lseek=netwasm_contract_internal_lseek \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_posix_io.c" \
  "$script_dir/runtime_posix_io_contract.c" \
  -o "$output_dir/runtime_posix_io_contract"
"$output_dir/runtime_posix_io_contract"

cc -std=c11 -Wall -Wextra -Werror \
  -D_Exit=netwasm_contract_exit \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_posix_exit.c" \
  "$script_dir/runtime_posix_exit_contract.c" \
  -o "$output_dir/runtime_posix_exit_contract"
"$output_dir/runtime_posix_exit_contract"
echo "Runtime POSIX facade contracts PASS"

#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
workspace_dir="$(cd "$script_dir/../.." && pwd)"
output_dir="$(mktemp -d)"
trap 'rm -rf "$output_dir"' EXIT

node --test "$script_dir/runtime-contract-imports.test.mjs"
node --test "$script_dir/yield-reactor-imports.test.mjs"
node --test --experimental-test-coverage \
  --test-coverage-include='**/timer-reactor-imports.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  "$script_dir/timer-reactor-imports.test.mjs"
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
  -I"$script_dir/fake_gc" \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/unmanaged_allocator_bdwgc_contract.c" \
  -o "$output_dir/unmanaged_allocator_bdwgc_contract"
"$output_dir/unmanaged_allocator_bdwgc_contract"
printf 'BDWGC unmanaged allocator startup contract PASS\n'

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/runtime_libc_initialization_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_libc_initializer.c" \
  -o "$output_dir/runtime_libc_initialization_contract"
"$output_dir/runtime_libc_initialization_contract"
printf 'Libc initialization contract PASS\n'

cc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -I"$script_dir/fake_gc" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "$script_dir/collector_weak_reference_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/boehm_weak_reference.c" \
  -o "$output_dir/collector_weak_reference_contract"

"$output_dir/collector_weak_reference_contract"
echo "Collector weak-reference contract PASS"

cc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -I"$script_dir/fake_gc" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "$script_dir/collector_lifecycle_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/boehm_lifecycle.c" \
  -o "$output_dir/collector_lifecycle_contract"

"$output_dir/collector_lifecycle_contract"
echo "Collector lifecycle contract PASS"

cc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -I"$script_dir/fake_gc" \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/collector_collection_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/boehm_collection.c" \
  -o "$output_dir/collector_collection_contract"

"$output_dir/collector_collection_contract"
echo "Collector collection contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$script_dir/fake_gc" \
  -I"$workspace_dir/src/NetWasm.Runtime/collector" \
  "$script_dir/collector_metadata_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/boehm_metadata.c" \
  -o "$output_dir/collector_metadata_contract"
"$output_dir/collector_metadata_contract"
echo "Collector metadata contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/weak_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/weak_handle_table.c" \
  -o "$output_dir/weak_handle_table_contract"
"$output_dir/weak_handle_table_contract"
echo "Weak-handle table contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$script_dir/strong_handle_table_contract.c" \
  "$workspace_dir/src/NetWasm.Runtime/collector/strong_handle_table.c" \
  -o "$output_dir/strong_handle_table_contract"
"$output_dir/strong_handle_table_contract"
echo "Strong-handle table contract PASS"

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
  -I"$script_dir/fake_gc" \
  "$workspace_dir/src/NetWasm.Runtime/collector/boehm_identity_hash.c" \
  "$script_dir/collector_identity_hash_contract.c" \
  -o "$output_dir/collector_identity_hash_contract"
"$output_dir/collector_identity_hash_contract"

cc -std=c11 -Wall -Wextra -Werror \
    -I"$workspace_dir/src/NetWasm.Runtime" \
    "$workspace_dir/src/NetWasm.Runtime/collector/boehm_reference_store.c" \
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
for output_case in normal validation failed-first failed-second failed-partial closed invalid-result invalid-error; do
  "$output_dir/runtime_standard_output_contract" "$output_case"
done
echo "Runtime Preview 2 output contract PASS"

cc -std=c11 -Wall -Wextra -Werror \
  -I"$workspace_dir/src/NetWasm.Runtime" \
  "$workspace_dir/src/NetWasm.Runtime/runtime_process_exit.c" \
  "$script_dir/runtime_process_exit_contract.c" \
  -o "$output_dir/runtime_process_exit_contract"
"$output_dir/runtime_process_exit_contract"
echo "Runtime Preview 2 exit contract PASS"

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

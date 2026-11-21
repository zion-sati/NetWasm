#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
source_package_root="$repo_root/src/NetWasm.Runtime.Pack"
package_root="$source_package_root"
if [[ "${1:-}" != "--allow-emscripten" ]]; then
  echo "runtime pack regeneration is maintainer-only; pass --allow-emscripten explicitly" >&2
  exit 2
fi
shift
# --only TARGET/COLLECTOR rebuilds one collector directory, such as
# wasm32/netwasm, inside an existing regenerated output package root and then
# rewrites its manifest. It is for measuring collector changes quickly.
only=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-package-root) package_root="$2"; shift 2 ;;
    --only) only="$2"; shift 2 ;;
    *) echo 'Unknown runtime pack regeneration option.' >&2; exit 2 ;;
  esac
done
if [[ -n "$only" ]]; then
  [[ "$package_root" != "$source_package_root" && -f "$package_root/runtime/runtime-pack.json" ]] || {
    echo '--only requires an existing regenerated output package root.' >&2; exit 2;
  }
elif [[ "$package_root" != "$source_package_root" ]]; then
  [[ "$package_root" = /* && ! -e "$package_root" ]] || {
    echo 'Output package root must be a new absolute directory.' >&2; exit 2;
  }
  mkdir -p "$package_root/runtime"
  cp "$source_package_root/runtime/runtime-policy.json" "$package_root/runtime/runtime-policy.json"
fi
runtime_root="$package_root/runtime"
policy="$runtime_root/runtime-policy.json"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-runtime-pack.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT

emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK_ROOT:-}}"
if [[ -n "$emsdk_root" ]]; then
  [[ -f "$emsdk_root/emsdk_env.sh" ]] || {
    echo "missing Emscripten SDK environment '$emsdk_root/emsdk_env.sh'" >&2
    exit 1
  }
  emscripten_cache="${EM_CACHE:-}"
  export EMSDK_QUIET=1
  source "$emsdk_root/emsdk_env.sh"
  if [[ -n "$emscripten_cache" ]]; then
    export EM_CACHE="$emscripten_cache"
  fi
fi
for tool in emcc emar node wasm-tools; do
  command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
wasm_ld="$EMSDK/upstream/bin/wasm-ld"
[[ -x "$wasm_ld" ]] || { echo "missing required Emscripten linker" >&2; exit 1; }
em_config="$EMSDK/upstream/emscripten/em-config"
[[ -x "$em_config" ]] || { echo "missing required Emscripten configuration reader" >&2; exit 1; }
emscripten_cache_root="$("$em_config" CACHE)"
[[ "$emscripten_cache_root" = /* ]] || { echo "Emscripten cache path must be absolute" >&2; exit 1; }

alignment="$(node -p 'require(process.argv[1]).alignment' "$policy")"
wasm_page_size="$(node -p 'require(process.argv[1]).wasmPageSize' "$policy")"
native_stack_size="$(node -p 'require(process.argv[1]).nativeStackSizeBytes' "$policy")"
toolchain="$repo_root/eng/toolchain.json"

wasm-tools component embed "$repo_root/src/NetWasm.Runtime/wit" \
  --only-custom --encoding utf8 --output "$temporary_root/runtime-component-type.bin"
bash "$repo_root/eng/prepare-emscripten-system-libraries.sh"
python3 "$repo_root/eng/stage-runtime-pack-license.py" \
  --emsdk-root "$EMSDK" \
  --toolchain "$toolchain" \
  --output "$runtime_root/LICENSE.txt"

grep -hEo '(export_name|NETWASM_EXPORT|NETWASM_FINALIZATION_EXPORT)\("[^"]+"\)' \
  "$repo_root"/src/NetWasm.Runtime/*.c \
  "$repo_root"/src/NetWasm.Runtime/collector/*.c \
  | sed -E 's/^(export_name|NETWASM_EXPORT|NETWASM_FINALIZATION_EXPORT)\("//;s/"\)$//' \
  | sort -u > "$runtime_root/runtime-exports.txt"

for target in wasm32 wasm64; do
  if [[ -n "$only" && "${only%%/*}" != "$target" ]]; then continue; fi
  target_root="$runtime_root/$target"
  if [[ -z "$only" ]]; then rm -rf "$target_root"; fi
  mkdir -p "$target_root"

  layout="$temporary_root/$target.layout.json"
  node - "$layout" "$target" <<'NODE'
const fs = require("node:fs");
const [layout, target] = process.argv.slice(2);
fs.writeFileSync(layout, `${JSON.stringify({ schemaVersion: 2, target, applicationStaticDataEnd: 0 }, null, 2)}\n`);
NODE

  printf '%s\n' emscripten_notify_memory_growth \
    > "$target_root/allowed-undefined-symbols.txt"

  if [[ "$target" = wasm32 ]]; then
    machine=-mwasm32
    system_source="$emscripten_cache_root/sysroot/lib/wasm32-emscripten/lto"
    exception_source="$emscripten_cache_root/sysroot/lib/wasm32-emscripten"
  else
    machine=-mwasm64
    system_source="$emscripten_cache_root/sysroot/lib/wasm64-emscripten/lto"
    exception_source="$emscripten_cache_root/sysroot/lib/wasm64-emscripten"
  fi

  system_libraries=()
  while IFS= read -r library; do
    system_libraries+=("$library")
  done < <(node -p \
    'require(process.argv[1]).targets[process.argv[2]].systemLibraries.join("\n")' \
    "$policy" "$target")
  for library in "${system_libraries[@]}"; do
    [[ "$library" == *.a ]] || {
      echo "Emscripten system library '$library' must be a static archive" >&2
      exit 1
    }
  done

  system_paths=()
  mkdir -p "$target_root/system-libraries"
  for library in "${system_libraries[@]}"; do
    [[ -f "$system_source/$library" ]] || {
      echo "missing pinned Emscripten system library '$library' for '$target'" >&2
      exit 1
    }
    packaged_library="$target_root/system-libraries/$library"
    cp "$system_source/$library" "$packaged_library"
    if [[ "$library" = libc_optz.a ]]; then
      # libc_optz.a supplies the runtime's size-optimized memory functions. Its log, log2 and pow are older, less
      # accurate versions than libc.a's, which Math and MathF call as Mono's WebAssembly runtime does, so they leave.
      for member in log_small.o log2_small.o pow_small.o; do
        emar t "$packaged_library" | grep -qx "$member" || {
          echo "libc_optz.a for '$target' no longer has '$member'; review which math it supplies" >&2
          exit 1
        }
      done
      emar d "$packaged_library" log_small.o log2_small.o pow_small.o
    fi
    system_paths+=("$packaged_library")
  done

  system_normalization_arguments=(
    --prefix "$repo_root"
    --prefix "$EMSDK"
    --prefix "$temporary_root"
    --prefix "$system_source"
  )
  if [[ -n "${HOME:-}" ]]; then
    system_normalization_arguments+=(--prefix "$HOME")
  fi
  for library in "${system_paths[@]}"; do
    system_normalization_arguments+=(--archive "$library")
  done
  # The closure a program with C++ exceptions links instead; its libraries beyond the ordinary closure are object
  # files from the pinned SDK's cache.
  exception_paths=()
  while IFS= read -r library; do
    [[ -n "$library" ]] || continue
    if [[ ! -f "$target_root/system-libraries/$library" ]]; then
      [[ -f "$exception_source/$library" ]] || {
        echo "missing pinned Emscripten C++ exception library '$library' for '$target'" >&2
        exit 1
      }
      cp "$exception_source/$library" "$target_root/system-libraries/$library"
      system_normalization_arguments+=(--archive "$target_root/system-libraries/$library")
    fi
    exception_paths+=("$target_root/system-libraries/$library")
  done < <(node -p \
    'require(process.argv[1]).targets[process.argv[2]].exceptionSystemLibraries.join("\n")' \
    "$policy" "$target")
  system_normalization_arguments+=(--prefix "$exception_source")
  node "$source_package_root/tools/normalize-archive-paths.mjs" \
    "${system_normalization_arguments[@]}"

  # Support for archives Emscripten built, both lazy so only a program whose native code needs a member links it:
  # Wasm setjmp/longjmp (with Wasm exception handling) for every output, and stand-ins for Emscripten's JavaScript
  # functions for components, linked after the project's archives so those can supply their own.
  emscripten_work="$temporary_root/$target-emscripten"
  rm -rf "$emscripten_work"
  mkdir -p "$emscripten_work"
  emscripten_target_args=()
  if [[ "$target" = wasm64 ]]; then emscripten_target_args=(-sMEMORY64=1 -sWASM_BIGINT=1); fi
  (
    cd "$repo_root"
    emcc src/NetWasm.Runtime/runtime_wasm_sjlj.c -c -Oz -fwasm-exceptions -sWASM_LEGACY_EXCEPTIONS=0 \
      "${emscripten_target_args[@]}" -o "$emscripten_work/wasm-sjlj.o"
    emcc src/NetWasm.Runtime/runtime_wasm_longjmp_tag.S -c -fwasm-exceptions -sWASM_LEGACY_EXCEPTIONS=0 \
      "${emscripten_target_args[@]}" -o "$emscripten_work/wasm-longjmp-tag.o"
    emcc src/NetWasm.Runtime/runtime_cxx_personality.c -c -Oz \
      "${emscripten_target_args[@]}" -o "$emscripten_work/cxx-personality.o"
    emcc src/NetWasm.Runtime/runtime_emscripten_stand_ins.c -c -Oz -ffunction-sections \
      "${emscripten_target_args[@]}" -o "$emscripten_work/emscripten-stand-ins.o"
  )
  rm -f "$target_root/libnetwasm-emscripten-support.a" "$target_root/libnetwasm-emscripten-stand-ins.a"
  emar rcs "$target_root/libnetwasm-emscripten-support.a" \
    "$emscripten_work/wasm-sjlj.o" "$emscripten_work/wasm-longjmp-tag.o" "$emscripten_work/cxx-personality.o"
  emar rcs "$target_root/libnetwasm-emscripten-stand-ins.a" "$emscripten_work/emscripten-stand-ins.o"
  node "$source_package_root/tools/normalize-archive-paths.mjs" \
    --prefix "$repo_root" --prefix "$EMSDK" --prefix "$temporary_root" --prefix "$HOME" \
    --archive "$target_root/libnetwasm-emscripten-support.a" \
    --archive "$target_root/libnetwasm-emscripten-stand-ins.a"
  rm -rf "$emscripten_work"

  maximum_memory="$(node -p \
    'require(process.argv[1]).targets[process.argv[2]].maximumMemorySizeBytes' \
    "$policy" "$target")"
  export_args=()
  while IFS= read -r export_name; do
    export_args+=("--export=$export_name")
  done < "$runtime_root/runtime-exports.txt"

  for collector_directory in compact netwasm netwasmgen bump; do
  if [[ -n "$only" && "${only#*/}" != "$collector_directory" ]]; then continue; fi
  for native_profile in size speed; do
  collector_root="$target_root/$collector_directory"
  if [[ "$native_profile" = speed ]]; then collector_root="$collector_root/speed"; fi
  mkdir -p "$collector_root"
  "$repo_root/eng/build-netwasm-runtime.sh" \
    --collector "$collector_directory" \
    --runtime-layout "$layout" \
    --target "$target" \
    --configuration release \
    --native-profile "$native_profile" \
    --relocatable \
    --output "$collector_root/libnetwasm-runtime.a" \
    --collector-output "$collector_root/libgc.a" \
    --wasi-output "$collector_root/libnetwasm-runtime-wasi.a" \
    --nowasi-output "$collector_root/libnetwasm-runtime-nowasi.a"
  node "$source_package_root/tools/normalize-archive-paths.mjs" \
    --prefix "$repo_root" --prefix "$EMSDK" \
    --prefix "$temporary_root" \
    --prefix "$HOME" \
    --archive "$collector_root/libnetwasm-runtime.a" \
    --archive "$collector_root/libgc.a" \
    --archive "$collector_root/libnetwasm-runtime-wasi.a" \
    --archive "$collector_root/libnetwasm-runtime-nowasi.a"

  footprint=""
  for base in "$alignment" 123456 67108864; do
    (( base % alignment == 0 )) || {
      echo "runtime footprint proof base '$base' is not aligned" >&2
      exit 1
    }
    initial_memory=$(( (base + 262144 + wasm_page_size - 1) / wasm_page_size * wasm_page_size ))
    proof="$temporary_root/$target-$collector_directory-$native_profile-$base.wasm"
    "$wasm_ld" "$machine" -Bstatic --strip-debug --table-base=1 \
      --whole-archive "$collector_root/libnetwasm-runtime.a" "$collector_root/libnetwasm-runtime-wasi.a" \
      --no-whole-archive \
      "$collector_root/libgc.a" \
      "${system_paths[@]}" \
      --allow-undefined-file="$target_root/allowed-undefined-symbols.txt" \
      --no-entry --gc-sections --no-stack-first \
      --global-base="$base" -z stack-size="$native_stack_size" \
      --initial-memory="$initial_memory" --max-memory="$maximum_memory" \
      --export-memory --export-table \
      --export=emscripten_stack_get_current --export=_emscripten_stack_restore \
      --export-if-defined=__start_em_asm --export-if-defined=__stop_em_asm \
      --export-if-defined=__start_em_lib_deps --export-if-defined=__stop_em_lib_deps \
      --export-if-defined=__start_em_js --export-if-defined=__stop_em_js \
      --export=__heap_base --export=__data_end \
      -mllvm -combiner-global-alias-analysis=false \
      -mllvm -enable-emscripten-sjlj -mllvm -disable-lsr \
      "${export_args[@]}" -o "$proof"
    wasm-tools validate "$proof" --features all
    wat="$temporary_root/$target-$collector_directory-$native_profile-$base.wat"
    wasm-tools print "$proof" > "$wat"
    heap_index="$(grep -F '(export "__heap_base"' "$wat" | sed -E 's/.*global ([0-9]+).*/\1/')"
    heap_base="$(grep -E '^  \(global ' "$wat" | sed -n "$((heap_index + 1))p" | sed -E 's/.*const ([0-9]+)\).*/\1/')"
    measured="$((heap_base - base))"
    if [[ -z "$footprint" ]]; then
      footprint="$measured"
    elif [[ "$footprint" != "$measured" ]]; then
      echo "runtime footprint is not invariant for '$target'" >&2
      exit 1
    fi

    node "$source_package_root/tools/validate-runtime-imports.mjs" \
      "$proof" "$target" "$collector_directory"
    node "$source_package_root/tools/validate-runtime-component-metadata.mjs" \
      "$proof" "$temporary_root/runtime-component-type.bin"
  done

  # Native code's C library imports no WASI Preview 1: each function whose musl version would import it is replaced
  # by the runtime. Link them all, live, and reject any Preview 1 import that remains.
  if [[ "$collector_directory" = compact && "$native_profile" = size ]]; then
    libc_proof="$temporary_root/$target-libc-closure.wasm"
    libc_exports=()
    for libc_function in clock_gettime clock_getres time gettimeofday clock timespec_get getentropy \
        read readv pread preadv pwrite pwritev fsync fcntl isatty dup2 fchdir fchmod fchmodat tmpfile \
        localtime_r mktime if_nametoindex if_indextoname if_nameindex getifaddrs res_send \
        close write writev lseek fopen opendir fclose fread fwrite fseek ftell fflush getenv; do
      libc_exports+=("--export=$libc_function")
    done
    "$wasm_ld" "$machine" -Bstatic --strip-debug --table-base=1 \
      --whole-archive "$collector_root/libnetwasm-runtime.a" "$collector_root/libnetwasm-runtime-wasi.a" \
      --no-whole-archive \
      "$collector_root/libgc.a" \
      "${system_paths[@]}" \
      "$target_root/libnetwasm-emscripten-support.a" \
      --allow-undefined-file="$target_root/allowed-undefined-symbols.txt" \
      --no-entry --gc-sections --no-stack-first \
      --global-base=1024 -z stack-size="$native_stack_size" \
      --initial-memory="$initial_memory" --max-memory="$maximum_memory" \
      --export-memory --export-table \
      -mllvm -combiner-global-alias-analysis=false \
      -mllvm -enable-emscripten-sjlj -mllvm -disable-lsr \
      "${export_args[@]}" "${libc_exports[@]}" -o "$libc_proof"
    if wasm-tools print "$libc_proof" | grep -F '(import "wasi_snapshot_preview1"' >&2; then
      echo "native code's C library imports WASI Preview 1 on '$target'" >&2
      exit 1
    fi
    node "$source_package_root/tools/validate-runtime-imports.mjs" \
      "$libc_proof" "$target" "$collector_directory"
    rm -f "$libc_proof"

    # The C++ exception closure links in place of the ordinary one, with the support archive's personality entry
    # for older Emscripten's landing pads, validates, and imports nothing beyond the runtime's own.
    cxx_proof="$temporary_root/$target-cxx-exceptions.wasm"
    "$wasm_ld" "$machine" -Bstatic --strip-debug --table-base=1 \
      --whole-archive "$collector_root/libnetwasm-runtime.a" "$collector_root/libnetwasm-runtime-wasi.a" \
      --no-whole-archive \
      "$collector_root/libgc.a" \
      "${exception_paths[@]}" "$target_root/libnetwasm-emscripten-support.a" \
      --allow-undefined-file="$target_root/allowed-undefined-symbols.txt" \
      --no-entry --gc-sections --no-stack-first \
      --global-base=1024 -z stack-size="$native_stack_size" \
      --initial-memory="$initial_memory" --max-memory="$maximum_memory" \
      --export-memory --export-table \
      -mllvm -combiner-global-alias-analysis=false \
      -mllvm -enable-emscripten-sjlj -mllvm -disable-lsr \
      "${export_args[@]}" --export=__cxa_throw --export=__cxa_rethrow --export=__cxa_begin_catch \
      --export=__cxa_end_catch --undefined=_Unwind_CallPersonality -o "$cxx_proof"
    wasm-tools validate "$cxx_proof" --features all
    if wasm-tools print "$cxx_proof" | grep -F '(import "wasi_snapshot_preview1"' >&2; then
      echo "the C++ exception runtime imports WASI Preview 1 on '$target'" >&2
      exit 1
    fi
    node "$source_package_root/tools/validate-runtime-imports.mjs" \
      "$cxx_proof" "$target" "$collector_directory"
    rm -f "$cxx_proof"
  fi

  node - "$collector_root/layout.json" "$target" "$footprint" <<'NODE'
const fs = require("node:fs");
const [path, target, footprint] = process.argv.slice(2);
fs.writeFileSync(path, `${JSON.stringify({
  schemaVersion: 1,
  target,
  runtimeFootprintBytes: Number(footprint),
}, null, 2)}\n`);
NODE
  done
  done
done

node "$source_package_root/tools/write-runtime-pack-manifest.mjs" \
  "$package_root" "$repo_root/eng/toolchain.json"

#!/usr/bin/env bash
# Builds libSystem.IO.Compression.Native.a, the native half of NetWasm.App's codecs: dotnet/runtime's pal_zlib shim over
# the zlib-ng it vendors (DeflateStream, ZLibStream, GZipStream and ZIP's Deflate entries) and the Brotli it vendors
# (BrotliStream, BrotliEncoder and BrotliDecoder call Brotli's own API), all taken from dotnet/runtime deae2e32 into
# src/native. zlib-ng and Brotli are configured as upstream's zlib-ng.cmake and brotli.cmake configure them. Upstream's
# WASM builds leave Brotli out; NetWasm includes it because Linux .NET has it. Final linking pulls only the members a
# program reaches, so a program that uses one codec family does not link the other.
#
# Usage: eng/build-compression-native.sh --target wasm32|wasm64 --output <archive>
# Needs the pinned Emscripten SDK (NETWASM_EMSDK_ROOT, as eng/build-netwasm-runtime.sh does), cmake and ninja.
# The archive holds ordinary objects built with Emscripten's default Wasm features, at -O2: one archive serves every
# optimization level, and the final link's wasm-opt applies the program's own.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target=""
output=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --target) target="$2"; shift 2 ;;
        --output) output="$2"; shift 2 ;;
        *) echo "unknown argument '$1'" >&2; exit 2 ;;
    esac
done
[[ "$target" = wasm32 || "$target" = wasm64 ]] || { echo "--target must be 'wasm32' or 'wasm64'" >&2; exit 2; }
[[ -n "$output" ]] || { echo "--output is required" >&2; exit 2; }

emsdk="${NETWASM_EMSDK_ROOT:-${EMSDK:-}}"
[[ -n "$emsdk" && -x "$emsdk/upstream/emscripten/emcc" ]] || {
    echo "missing Emscripten; set NETWASM_EMSDK_ROOT to the pinned emsdk installation" >&2
    exit 1
}
emscripten="$emsdk/upstream/emscripten"
for tool in cmake ninja; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done

zlib_source="$repo_root/src/native/external/zlib-ng"
brotli_source="$repo_root/src/native/external/brotli"
shim_source="$repo_root/src/native/libs/System.IO.Compression.Native"

work="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-compression.XXXXXX")"
trap 'rm -rf "$work"' EXIT

target_flags=()
if [[ "$target" = wasm64 ]]; then
    target_flags=(-sMEMORY64=1)
fi
# No DWARF, and no build paths in what remains (NetWasm does not publish native debug information).
common_flags=(-O2 -DNDEBUG -g0 "-ffile-prefix-map=$repo_root=." "-ffile-prefix-map=$work=." "${target_flags[@]}")

# Upstream's zlib-ng.cmake: zlib-compatible API with prefixed symbols, static only, no tests, and no aligned_alloc on
# WASI. zlib-ng has no WebAssembly-specific code, so its generic C paths are used.
"$emscripten/emcmake" cmake -S "$zlib_source" -B "$work/zlib-ng" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    "-DCMAKE_C_FLAGS=${common_flags[*]}" \
    "-DCMAKE_C_FLAGS_RELEASE=" \
    -DZLIB_COMPAT=ON \
    -DZLIB_ENABLE_TESTS=OFF \
    -DZLIBNG_ENABLE_TESTS=OFF \
    -DWITH_GTEST=OFF \
    -DZ_PREFIX=ON \
    -DWITH_RVV=OFF \
    -DWITH_ARMV6=OFF \
    -DHAVE_ALIGNED_ALLOC=FALSE \
    -DBUILD_SHARED_LIBS=OFF \
    -DSKIP_INSTALL_ALL=ON >"$work/configure.log" 2>&1 || { cat "$work/configure.log" >&2; exit 1; }
cmake --build "$work/zlib-ng" --target zlib >"$work/build.log" 2>&1 || { cat "$work/build.log" >&2; exit 1; }

zlib_archive="$(find "$work/zlib-ng" -maxdepth 1 -name 'libz*.a' | head -n 1)"
[[ -n "$zlib_archive" ]] || { echo "zlib-ng produced no static archive" >&2; exit 1; }

# Upstream's brotli.cmake: static libraries, no tests, and the shared-compilation definitions that give the API
# default visibility.
brotli_definitions="-DBROTLI_SHARED_COMPILATION -DBROTLIENC_SHARED_COMPILATION -DBROTLIDEC_SHARED_COMPILATION -DBROTLICOMMON_SHARED_COMPILATION"
"$emscripten/emcmake" cmake -S "$brotli_source" -B "$work/brotli" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    "-DCMAKE_C_FLAGS=${common_flags[*]} -Wno-implicit-fallthrough $brotli_definitions" \
    "-DCMAKE_C_FLAGS_RELEASE=" \
    -DBROTLI_DISABLE_TESTS=ON \
    -DBUILD_SHARED_LIBS=OFF >"$work/brotli-configure.log" 2>&1 || { cat "$work/brotli-configure.log" >&2; exit 1; }
cmake --build "$work/brotli" --target brotlicommon brotlienc brotlidec >"$work/brotli-build.log" 2>&1 || {
    cat "$work/brotli-build.log" >&2; exit 1; }

# pal_zlib.c is upstream's; the PAL headers it includes are NetWasm's reduced stand-ins (netwasm/).
"$emscripten/emcc" -c "$shim_source/pal_zlib.c" -o "$work/pal_zlib.o" \
    "${common_flags[@]}" -Wno-implicit-fallthrough \
    -I "$shim_source/netwasm" -I "$work/zlib-ng" -I "$zlib_source"

# One archive per logical library: the shim, then each library's members under a prefix, so no two names collide.
mkdir "$work/members"
cp "$work/pal_zlib.o" "$work/members/pal_zlib.o"
add_members() {
    local prefix="$1" archive="$2" extract="$work/extract-$1"
    [[ -f "$archive" ]] || { echo "missing static archive '$archive'" >&2; exit 1; }
    mkdir "$extract"
    (cd "$extract" && "$emscripten/emar" x "$archive")
    for member in "$extract"/*; do
        cp "$member" "$work/members/$prefix-$(basename "$member")"
    done
}
add_members zlib-ng "$zlib_archive"
for library in brotlicommon brotlienc brotlidec; do
    add_members "$library" "$work/brotli/lib$library.a"
done
mkdir -p "$(dirname "$output")"
rm -f "$output"
"$emscripten/emar" rcs "$output" $(cd "$work/members" && ls -1 | sort | sed "s#^#$work/members/#")

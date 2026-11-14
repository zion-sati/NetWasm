#!/usr/bin/env bash
# Builds NetWasm.App.Runtime's cryptography archive for one width: dotnet/runtime's System.Security.Cryptography.Native
# shim (src/native/libs, from .NET 11 GA) linked directly to OpenSSL's libcrypto, as upstream's static build is, with
# NetWasm's seed source over the platform's random source. OpenSSL is the release eng/toolchain.json pins, downloaded
# once into NETWASM_DEPENDENCY_CACHE and checked against its SHA-256. Every object goes into one archive, so a program
# links only the shim functions it calls and the OpenSSL code they reach.
#
# usage: build-crypto-native.sh --target wasm32|wasm64 --output <archive>
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
for tool in make perl curl python3; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done

pin() { python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['openssl'][sys.argv[2]])" "$repo_root/eng/toolchain.json" "$1"; }
version="$(pin version)"
url="$(pin url)"
sha256="$(pin sha256)"

cache="${NETWASM_DEPENDENCY_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/netwasm-dependencies}"
# Python's hashlib, since GNU sha256sum's --check is not on macOS (its BSD sha256sum rejects it).
matches_pin() { [[ "$(python3 -c 'import hashlib, sys; print(hashlib.sha256(open(sys.argv[1], "rb").read()).hexdigest())' "$1")" == "$sha256" ]]; }
tarball="$cache/openssl-$version.tar.gz"
mkdir -p "$cache"
if [[ ! -f "$tarball" ]] || ! matches_pin "$tarball"; then
    curl --fail --silent --show-error --location --output "$tarball.partial" "$url"
    matches_pin "$tarball.partial" || {
        rm -f "$tarball.partial"
        echo "OpenSSL $version does not match its pinned SHA-256" >&2
        exit 1
    }
    mv "$tarball.partial" "$tarball"
fi

shim_source="$repo_root/src/native/libs/System.Security.Cryptography.Native"
work="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-crypto.XXXXXX")"
trap 'rm -rf "$work"' EXIT

tar -xzf "$tarball" -C "$work"
openssl="$work/openssl-$version"

target_flags=()
openssl_target=linux-generic32
if [[ "$target" = wasm64 ]]; then
    target_flags=(-sMEMORY64=1)
    openssl_target=linux-generic64
fi
common_flags=(-O2 -DNDEBUG -g0 "-ffile-prefix-map=$repo_root=." "-ffile-prefix-map=$work=." "${target_flags[@]}")

# One thread, no engines, no dynamic loading, no sockets (TLS is the host's), and no seed source of OpenSSL's own:
# the shim installs NetWasm's (netwasm/pal_netwasm_seed.c). Providers are built in (no-module); the legacy one stays,
# as upstream loads it for DES, RC2 and old PKCS#12 files (CryptoNative_RegisterLegacyAlgorithms). The system trust
# store's default place is Debian's and Ubuntu's, /etc/ssl/certs and /etc/ssl/cert.pem, where a host preopens it;
# SSL_CERT_DIR and SSL_CERT_FILE choose another, as on Linux. OpenSSL's built-in default provider links every
# algorithm it has, so the algorithms no .NET API reaches are left out (a quarter of a cipher program's native code).
openssl_disabled=(no-argon2 no-aria no-bf no-blake2 no-camellia no-cast no-cmac no-cmp no-cms no-comp no-dgram no-idea
    no-md4 no-mdc2 no-ml-dsa no-ml-kem no-ocb no-rc4 no-rmd160 no-scrypt no-seed no-siphash no-siv no-slh-dsa no-sm2
    no-sm3 no-sm4 no-srp no-ts no-whirlpool)
(
    cd "$openssl"
    CC="$emscripten/emcc" AR="$emscripten/emar" RANLIB="$emscripten/emranlib" ./Configure "$openssl_target" \
        no-asm no-threads no-shared no-dso no-engine no-async no-sock no-afalgeng no-ui-console no-tests no-apps \
        no-docs no-module no-autoload-config no-secure-memory --with-rand-seed=none --openssldir=/etc/ssl \
        "${openssl_disabled[@]}" \
        "${common_flags[@]}" >"$work/configure.log" 2>&1 || { cat "$work/configure.log" >&2; exit 1; }
    # The generated headers first, in a make of their own: one make given both goals may compile sources before
    # the headers they include exist. getconf counts the cores where nproc is missing (macOS).
    { make build_generated && make -j"$(getconf _NPROCESSORS_ONLN)" libcrypto.a; } >"$work/build.log" 2>&1 ||
        { tail -50 "$work/build.log" >&2; exit 1; }
)

mkdir "$work/members"
shim_includes=(-I "$shim_source/netwasm" -I "$shim_source" -I "$repo_root/src/native/libs/Common" -I "$repo_root/src/native"
    -I "$openssl/include")
compile() {
    local source="$1" object="$2"
    "$emscripten/emcc" -c "$source" -o "$object" "${common_flags[@]}" -DTARGET_NETWASM -Wno-deprecated-declarations \
        "${shim_includes[@]}"
}
for source in "$shim_source"/*.c "$shim_source"/netwasm/*.c; do
    compile "$source" "$work/members/shim-$(basename "${source%.c}").o"
done

mkdir "$work/extract"
(cd "$work/extract" && "$emscripten/emar" x "$openssl/libcrypto.a")
for member in "$work/extract"/*; do
    cp "$member" "$work/members/libcrypto-$(basename "$member")"
done

mkdir -p "$(dirname "$output")"
rm -f "$output"
"$emscripten/emar" rcs "$output" $(cd "$work/members" && ls -1 | sort | sed "s#^#$work/members/#")

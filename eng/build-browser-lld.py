#!/usr/bin/env python3
"""Build the Wasm-only browser linker from the manifest's public LLVM commit.

Requires the exact Emscripten SDK and Node already installed. Build directories
are caller-owned caches; no product packages or host toolchains are repacked.
"""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import tarfile
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]


def run(args, log):
    print(' '.join(map(str, args)), flush=True)
    with log.open('w') as output:
        subprocess.run(list(map(str, args)), stdout=output, stderr=subprocess.STDOUT, check=True,
                       env={**os.environ, "EM_NODE_JS": shutil.which("node")})


def digest(path):
    return hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()


def source_digest(root):
    result = hashlib.sha256()
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            payload = 'symlink:' + os.readlink(path)
        elif path.is_file():
            payload = digest(path)
        else:
            continue
        result.update((path.relative_to(root).as_posix() + '\0' + payload + '\n').encode())
    return result.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cache', type=pathlib.Path)
    parser.add_argument('--emsdk', required=True, type=pathlib.Path)
    parser.add_argument('--parallel', type=int, default=4)
    parser.add_argument('--minimum-free-gib', type=int, default=100)
    args = parser.parse_args()
    cache = args.cache.resolve()
    cache.mkdir(parents=True, exist_ok=True)
    pins = json.loads((ROOT / 'eng/toolchain.json').read_text())
    emscripten = args.emsdk.resolve() / 'upstream/emscripten'
    emcc_version = subprocess.check_output([emscripten / 'emcc', '--version'], text=True)
    if f") {pins['emscripten']} " not in emcc_version:
        raise RuntimeError('Emscripten does not match eng/toolchain.json')
    node_version = subprocess.check_output(['node', '--version'], text=True).strip().removeprefix('v')
    if node_version != pins['node']:
        raise RuntimeError('Node does not match eng/toolchain.json')
    free = shutil.disk_usage(cache).free
    # Conservative peak allowance, including public source and incremental objects.
    if free - 60 * 1024 ** 3 < args.minimum_free_gib * 1024 ** 3:
        raise RuntimeError('Insufficient disk space for the 60 GiB build allowance')
    commit = pins['llvmLld']['commit']
    url = f'https://codeload.github.com/llvm/llvm-project/tar.gz/{commit}'
    archive = cache / 'llvm-source.tar.gz'
    source = cache / f'llvm-project-{commit}'
    source_receipt = cache / 'llvm-source.json'
    if not source_receipt.exists() or not source.exists():
        if not archive.exists():
            with urllib.request.urlopen(url) as response, archive.open('wb') as output:
                shutil.copyfileobj(response, output)
        with tarfile.open(archive) as tar:
            prefix = f'llvm-project-{commit}/'
            allowed = ('llvm/', 'lld/', 'cmake/', 'third-party/', 'libc/')
            members = [m for m in tar.getmembers()
                       if m.name.startswith(prefix) and m.name[len(prefix):].startswith(allowed)]
            tar.extractall(cache, members=members, filter='data')
        source_receipt.write_text(json.dumps({'commit': commit, 'url': url,
                                              'archiveSha256': digest(archive),
                                              'extractedSourceSha256': source_digest(source)}, indent=2) + '\n')
    recorded_source = json.loads(source_receipt.read_text())
    if recorded_source['commit'] != commit or recorded_source['url'] != url:
        raise RuntimeError('Cached LLVM source identity does not match the manifest')
    if source_digest(source) != recorded_source['extractedSourceSha256']:
        raise RuntimeError('Cached public LLVM source files changed')
    if archive.exists() and digest(archive) != recorded_source['archiveSha256']:
        raise RuntimeError('Cached public LLVM archive hash changed')
    native = cache / 'native'
    common = ['-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release', '-DLLVM_INCLUDE_TESTS=OFF',
              '-DLLVM_INCLUDE_BENCHMARKS=OFF', '-DLLVM_INCLUDE_EXAMPLES=OFF',
              '-DLLVM_ENABLE_ZLIB=OFF', '-DLLVM_ENABLE_ZSTD=OFF', '-DLLVM_ENABLE_LIBXML2=OFF']
    run(['cmake', '-S', source / 'llvm', '-B', native, *common, '-DLLVM_TARGETS_TO_BUILD=',
         f'-DLLVM_FORCE_VC_REVISION={commit}',
         '-DLLVM_FORCE_VC_REPOSITORY=https://github.com/llvm/llvm-project'],
        cache / 'native-configure.log')
    run(['cmake', '--build', native, '--target', 'llvm-tblgen', '--parallel', args.parallel],
        cache / 'native-build.log')
    build = cache / 'browser'
    adapter = ROOT / 'src/NetWasm.Toolchain/Browser/LLD'
    run([emscripten / 'emcmake', 'cmake', '-S', adapter, '-B', build, *common,
         f'-DNETWASM_LLVM_SOURCE={source}', f'-DLLVM_TABLEGEN={native / "bin/llvm-tblgen"}',
         '-DLLVM_HOST_TRIPLE=wasm32-unknown-emscripten',
         '-DLLVM_DEFAULT_TARGET_TRIPLE=wasm32-unknown-emscripten',
         f'-DLLVM_FORCE_VC_REVISION={commit}',
         '-DLLVM_FORCE_VC_REPOSITORY=https://github.com/llvm/llvm-project'], cache / 'browser-configure.log')
    run(['cmake', '--build', build, '--target', 'netwasm-browser-lld', '--parallel', args.parallel],
        cache / 'browser-build.log')
    assets = cache / 'assets'
    assets.mkdir(exist_ok=True)
    for filename in ('netwasm-browser-lld.mjs', 'netwasm-browser-lld.wasm'):
        shutil.copy2(build / filename, assets / filename)
    shutil.copy2(adapter / 'netwasm-lld.mjs', assets / 'netwasm-lld.mjs')
    shutil.copy2(source / 'llvm/LICENSE.TXT', assets / 'LLVM-LICENSE.txt')
    shutil.copy2(ROOT / 'LICENSES/LicenseRef-NetWasm-Community-1.0.txt', assets / 'NETWASM-LICENSE.txt')
    receipt = {'builderSha256': digest(pathlib.Path(__file__)),
               'toolchainManifestSha256': digest(ROOT / 'eng/toolchain.json'),
               'nativeTablegenSha256': digest(native / 'bin/llvm-tblgen'),
               'llvm': pins['llvmLld'], 'emscripten': pins['emscripten'], 'node': node_version,
               'source': json.loads(source_receipt.read_text()),
               'assets': {p.name: {'bytes': p.stat().st_size, 'sha256': digest(p)}
                          for p in sorted(assets.iterdir()) if p.is_file()},
               'adapterSources': {p.name: digest(p) for p in sorted(adapter.iterdir()) if p.is_file()}}
    (cache / 'build-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()

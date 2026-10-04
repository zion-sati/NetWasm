# Jco 1.28.1 NetWasm patch

This patch preserves exact component export names in Jco's generated JavaScript
bindings and exposes an internal predicate for genuine declared-result errors.
The predicate uses module-private identity rather than trusting error properties.
It is based on Jco tag `jco-v1.28.1` at commit
`e3ed4ca01e13a2f9ee6d0b44abec46c8da662791`.

The authoritative build recipe lives in `NetWasm.NativeTools` at commit
`10ebb838c6119514ff107ac65fa3966002f9de38`. It pins the upstream source,
patch, tool versions and release identity, applies deterministic Rust path
remapping, runs the patch's Rust tests, exercises the built module against a
declared-error component, and rejects build-machine paths before packaging.

To reproduce the checked-in replacement:

1. Check out the exact `NetWasm.NativeTools` builder commit recorded above.
2. Run its pinned builder:

   ```sh
   python3 eng/build-jco-bindgen.py \
     --output /tmp/js-component-bindgen-component.core.wasm \
     --receipt /tmp/jco-build-receipt.json \
     --cache /tmp/jco-build-cache
   ```

3. Run `eng/package-jco-bindgen-release.py` there to validate the receipt and
   deterministic archive.
4. Copy the validated module here and verify every SHA-256 digest in
   `patch-manifest.json`.

Keep changes to the Jco build procedure in `NetWasm.NativeTools`; this directory
retains the consumed module, reviewable source patch, license and pinned
provenance only. The path remapping is part of the artifact contract because
unmapped Rust panic locations expose the producing machine's build paths.

The staging script verifies the original upstream artifact, the source patch,
the upstream license and the rebuilt replacement before publishing a toolchain
asset generation.

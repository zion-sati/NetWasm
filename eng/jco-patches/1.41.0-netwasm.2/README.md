# Jco 1.41.0 NetWasm patch

This patch makes two changes to Jco's generated JavaScript. It exposes
`_util.isComponentError`, a predicate for genuine declared-result errors that
uses module-private identity rather than trusting error properties (proposed
upstream in bytecodealliance/jco#2425). It also completes a stream read with
the values the host already supplied: Jco's host injection drained them into
the read and then waited on an asynchronous source for more, so a read whose
buffered values were fewer than its size waited until the source produced
again, and a read cancelled meanwhile stranded them (reported upstream in
bytecodealliance/jco#2435). The patch is based on Jco
tag `jco-v1.41.0` at commit `04fe1e247df7c53a09ac047b30f24fa2386aa7c8`.

The authoritative build recipe lives in `NetWasm.NativeTools` at commit
`15413d3c7a696cc0c6fe2fa56a160a1fea7ba0c8`. It pins the upstream source,
patch, tool versions and release identity, applies deterministic Rust path
remapping, runs the bindgen crate's tests, exercises the built module against a
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

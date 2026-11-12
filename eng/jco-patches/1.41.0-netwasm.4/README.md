# Jco 1.41.0 NetWasm patch

This patch makes four changes to Jco's generated JavaScript. It exposes
`_util.isComponentError`, a predicate for genuine declared-result errors that
uses module-private identity rather than trusting error properties (proposed
upstream in bytecodealliance/jco#2425). It also completes a stream read with
the values the host already supplied: Jco's host injection drained them into
the read and then waited on an asynchronous source for more, so a read whose
buffered values were fewer than its size waited until the source produced
again, and a read cancelled meanwhile stranded them (reported upstream in
bytecodealliance/jco#2435). And it resumes a guest that yielded on the next macrotask
(`setImmediate`, else a `MessageChannel`) instead of `setTimeout`, whose minimum delay
(1 ms in Node.js, 4 ms in browsers once timers nest) a guest that yields after every
console write paid on every write. Last, a host read or write of a stream or future that the
other end has yet to complete waits for its event instead of polling with `setInterval`:
a guest resumed on the next macrotask runs before such a timer fires, so a guest that
wrote a line and then computed without waiting lost the line. The patch is based on Jco
tag `jco-v1.41.0` at commit `04fe1e247df7c53a09ac047b30f24fa2386aa7c8`.

The authoritative build recipe lives in `NetWasm.NativeTools` at commit
`4525b7400405d78eeb3f1ef951e8189095d82cb7`. It pins the upstream source,
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

# Browser Wasm runtime linker

`eng/build-browser-lld.py <ignored-cache> --emsdk <installed-sdk>` builds the
public LLVM commit, Emscripten and Node versions in `eng/toolchain.json`. It
builds native `llvm-tblgen`, then only the `lldWasm`/`lldCommon` dependency closure
and this C ABI. Other linker drivers are neither linked nor built. Threads are
disabled. The tool starts with 64 MiB linear memory and may grow to 1 GiB.

The resulting `assets/` directory contains the Emscripten ES module, Wasm binary
and `netwasm-lld.mjs` host. `build-receipt.json` records source and asset hashes.
These are build assets, not checked-in binaries or a release publication.

```js
import loadLld from './netwasm-browser-lld.mjs';
import { createBrowserLld } from './netwasm-lld.mjs';
const linker = createBrowserLld(loadLld);
const result = await linker.link({
  arguments: plannedArguments,
  files: { '/netwasm-link/runtime.a': archiveBytes },
  outputPath: '/netwasm-link/runtime.wasm',
});
```

Run in a dedicated worker. Arguments come from the existing NetWasm runtime
link plan; this module does not select archives, exports or memory layout.
Paths in the immutable byte map and output must live below `/netwasm-link/`.
Response files are ordinary byte-map inputs and use LLD's existing syntax.

The C result has failure and `canRunAgain` bits. The host discards the instance after every invocation. The pinned Wasm driver
retains LTO state after success even when `canRunAgain` is true, so reusing an
instance fails for the released bitcode archives. The worker itself may remain
alive. Failed links, traps and unsafe results also require fresh instances.
It releases argv allocations and removes tool files after each invocation.
The UI must terminate the worker to cancel synchronous linking; start a fresh
worker after termination. Tool-private MEMFS is not exposed to user programs.
The browser instance has no host filesystem and requires no shared memory or
cross-origin isolation. This tool boundary alone does not prove a complete C#
compile/run/component pipeline.

LLVM code uses Apache-2.0 WITH LLVM-exception; retain its license when distributing
built assets. NetWasm adapter source uses NetWasm Community License 1.0 under the existing
source-path license map.

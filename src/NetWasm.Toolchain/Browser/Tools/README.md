# Trusted browser tool hosts

These helpers host the pinned wasm-tools Preview 1 binary and Binaryen merge/opt
CLIs from `eng/toolchain.json`. They accept owned byte maps and CLI arguments;
NetWasm's existing planning policy selects commands and inputs. Worker scheduling,
UI messages, jco and guest Preview 2 imports stay with their existing owners.
The source uses NetWasm Community License 1.0 under the repository license map;
retain upstream tool/shim notices when distributing assets.

```js
import { createWasmToolsHost } from './wasm-tools-host.mjs';
import { createBinaryenHost } from './binaryen-host.mjs';
const wasmTools = createWasmToolsHost({ loadAsset: verifiedLoader,
  wasiShim: verifiedPreview1Shim });
const binaryen = createBinaryenHost({ loadAsset: verifiedLoader });
const parsed = await wasmTools.run({ args: ['parse', 'input.wat', '-o', 'core.wasm'],
  files: { 'input.wat': watBytes }, outputs: ['core.wasm'] });
const optimized = await binaryen.run('wasm-opt', {
  args: ['core.wasm', '-Oz', '-o', 'optimized.wasm'],
  files: parsed.files, outputs: ['optimized.wasm'] });
```

`loadAsset(name)` returns verified `Uint8Array` bytes. Its caller owns the trusted
manifest and verifies every asset before code execution. The caller also verifies
the entire imported shim graph before injecting its module exports. Default asset
names are `wasm-tools.wasm`, `wasm-merge.js`, `wasm-opt.js` and `path-browserify.js`.
The proven shim/path adapters are browser_wasi_shim 0.4.2 and path-browserify 1.0.1.
There are no package imports, network loaders or ambient filesystem providers in
these helpers.

Request `files` values must be `Uint8Array`; helpers snapshot them and arguments
before awaiting assets. Virtual names are flat ASCII filenames, excluding `.`
and `..`. Outputs are distinct filenames and cannot overwrite an input. Successful
results contain owned `Uint8Array` output copies, stdout/stderr, exit code and
observed linear-memory bytes. Nonzero CLI exits contain no output files. Host
validation, limit and unexpected wasm-tools errors reject the request. Binaryen
also reports caught CLI failures as a nonzero result.

Default bounds are 1 MiB aggregate input/output bytes, 64 KiB diagnostics and
argument bytes, 32 MiB per tool asset, 128 input/output filenames and 255 bytes per
name. Pass `limits` to change a bound. These checks bound accepted inputs and
returned data; they do not establish process peak or change the original trusted
tool binaries' memory maxima. Run synchronous tools in a dedicated worker. The
optional `onEnter(tool)` callback fires immediately before tool execution; the UI
can terminate that worker on timeout and create a fresh worker for recovery.

Each invocation creates a fresh tool instance. The wasm-tools host reuses only
its immutable compiled module and supplies empty environment/stdin plus a private
in-memory preopen at `.`. That Preview 1 shim is never supplied to user programs.
Its UTF-8 argv size import corrects shim 0.4.2's JS character-count calculation.
Descriptors and directory entries are released in `finally` after owned outputs
are copied.

Binaryen factories reuse verified bootstrap code, instantiate fresh tool Wasm
and expose its lexical MEMFS/callMain. An exact pinned-shape guard removes only
the 698-byte NODERAWFS installation block and replaces the `node:path` binding
with the injected browser path adapter. The embedded Wasm, CLI entry point and
command implementation remain intact. Requested MEMFS files are unlinked in
`finally`; the instance is then released. This pinned bootstrap adaptation uses
a function constructor, so its caller's content security policy must support
that mechanism or provide a separately maintained upstream build.

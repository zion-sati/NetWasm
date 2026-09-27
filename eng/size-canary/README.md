# Console42 final-size canary

From a clean, pinned source worktree with the repository toolchain installed:

```sh
NETWASM_EMSDK_ROOT=/absolute/path/to/emsdk \
NETWASM_NODE_MODULES_ROOT=/absolute/path/to/node_modules \
bash eng/measure-console42-size.sh /absolute/new/evidence-directory
```

The script compiles this folder's `Console.WriteLine(42)` source with Release
optimization, uses the current worktree's compiler, CoreLib, native runtime,
and command WIT, packages an executable WASI Preview 2 component with `-Oz`,
validates it, and checks that Wasmtime prints `42`. The final artifact is
`NetWasmApp.wasm`; the evidence directory includes its byte size and hash, the
source commit and input hashes, stage logs, and exit status. The directory
must be new, absolute, and outside the source worktree.

`NetWasmApp` is the canonical sample assembly identity used by the SDK and
Playground comparison. Assembly identity participates in deterministic
method/type ordering, so an otherwise identical project with another assembly
name can differ by a few encoded index bytes.

The final component must also match `expected-component-bytes.txt`. Change that
file only after reproducing and reviewing an intentional size change through
both this source canary and a clean SDK package consumer.

For each compiler fix, run this canary on clean pre-fix and post-fix worktrees
with the same toolchain and compare the final `NetWasmApp.wasm` byte sizes. Any
material growth needs a cause and owner review before merge. Raw
`application.wasm` size is diagnostic only; it is not the final size gate.

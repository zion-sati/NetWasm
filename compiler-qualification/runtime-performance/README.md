# Runtime performance comparison

`run.py` compares the code that two NetWasm package sets generate. It publishes
each workload through the SDK for every selected optimization preset, runs the
components under Wasmtime in interleaved samples (baseline, candidate, then
candidate, baseline), and records median time, the candidate/baseline ratio and
raw and Brotli-11 component sizes in `summary.json`.

Every workload prints a checksum. The runner also runs the same program on
desktop .NET 11 (the SDK pinned by `eng/csharp15-toolchain.json`) and requires
the candidate to match it in every scenario, so a faster wrong answer fails. The
baseline may differ only in scenarios passed to `--semantic-changes`.

```sh
python3 compiler-qualification/runtime-performance/run.py \
  --baseline <feed> <version> --candidate <feed> <version> \
  --output <new-directory> --samples 15 --semantic-changes conversions
```

## Workloads

- `transaction`: normalizes account strings, deduplicates through
  `HashSet<int>`, and accumulates `decimal` balances in a
  `Dictionary<string, decimal>`. The `transaction` scenario times aggregation
  into preallocated, warmed containers, with zero allocation and zero
  collections inside the timed region. `transaction-full` also times creating
  the transactions, normalizing every account and growing the containers from
  empty, so allocation and collection are part of its timing.
- `gc`: `object-graph` builds a retained graph of small objects, and `ui-churn`
  replaces a slice of a retained array every frame.
- `net11`: one scenario per .NET 11 code generation item: saturating
  floating-point conversions to small integers, constant string and UTF-8
  comparisons after inlining, a sparse constant set, `i + 1 < length` loop
  bounds, index-from-end access, an empty-span guard, implied branches, and
  generic virtual, default interface and `Activator.CreateInstance<T>()` calls,
  each in its own scenario.
- `corelib`: one scenario per CoreLib hot path. `short-copies` concatenates
  short strings and copies spans of every length from 0 to 16 chars;
  `integer-formatting` formats integers of every digit count to strings and
  into spans.
- `compute`: computation with no library work and no allocation. `nbody` is
  the Computer Language Benchmarks Game n-body simulation of the Sun and the
  four gas giants in double precision. `sort` quicksorts 100,000 integers in
  place, refilled from seed data every round. `crc32` computes a table-driven
  CRC-32 over a 1 MiB buffer. The program fails if a timed region allocates, and
  each checksum is exact, the bits of the final energy for `nbody`.

`gc_statistics.py` reports NetWasmGC's allocator statistics for each
scenario's timed region: allocations by path, run refills, bitmap search,
zeroed bytes, marking and sweep work, and a partition of the heap into arenas,
block metadata, headers, payload, padding, free slots and unused arena space.
It needs a runtime regenerated with `NETWASM_GC_STATISTICS` defined, which
reports on stderr whenever the allocated-bytes counter is read; never time that
build.

`profile.py` attributes a scenario's time to functions with Wasmtime's guest
profiler over several runs. It profiles the timed product component and names
its functions from a twin built with `NETWASM_KEEP_NAMES`, which keeps names
through every Binaryen step, runtime included; the twin must strip to the
product's exact code. Shares cover the whole process, not just the timed region.
The guest profiler samples only where Wasmtime checks its epoch, at function
entries and loop headers, so it charges straight-line code to the next call or
loop: functions with loops and small callees look more expensive than they are.
Confirm a hot function with a native profiler before acting on its share, for
example `samply record` over `wasmtime run --profile=perfmap`, naming
`wasm[0]::function[N]` from the twin's function N minus its imported functions.

`iterate.py` rebuilds the compiler (and, with `--corelib`, CoreLib) from a
source tree, overlays it on a released package set and times one workload
against that base. Both sides use NetWasmGC unless `--collector` names another.
Its timings guide iteration; report results from `run.py`.

The first two workloads are regression guards. Each `net11`
scenario must get faster with the optimization it exercises; `conversions`
measures a semantic change, so its cost is reported and explained instead.

Every component uses the runtime pack's default collector (Compact) unless
`--baseline-collector` or `--candidate-collector` names one of `Compact`,
`NetWasm` or `Bump`; `summary.json` records the selection. Comparing one
feed with two collectors isolates collector cost on an identical compiler.

`--baseline-property` and `--candidate-property` pass `NAME=VALUE` MSBuild
properties to one side's publishes, and repeat. One feed with an optimization
switched off on one side, such as
`--baseline-property NetWasmManagedArrayStoreCheckElimination=Disabled`,
measures that optimization alone on an identical compiler.

`--baseline-runtime` and `--candidate-runtime` replace a side's restored native
runtime with the `runtime` directory that
`src/NetWasm.Runtime.Pack/tools/regenerate-runtime-pack.sh --output-package-root`
writes, so collector or runtime changes are measured without building
packages. `--only TARGET/COLLECTOR`, such as `wasm32/netwasm`, rebuilds one
collector inside an existing regenerated root in under a minute. Regenerated
archives embed the checkout path, but an unchanged source tree publishes
components byte-identical to those built from the packaged runtime.

## Collector comparison

`collectors.py` compares the collectors on one package set and produces the
figures in `docs/runtime-garbage-collection.md`. It publishes every scenario
once per collector with the same preset (`O3` by default), requires each to
match desktop .NET, and times fresh `wasmtime run` processes after warmups,
rotating the collector order each sample. It records each cell's median,
minimum and maximum, the workload's own allocation and collection counters,
component sizes, and peak aggregate linear-memory capacity.

```sh
python3 compiler-qualification/runtime-performance/collectors.py \
  --feed <feed> <version> --output <new-directory>
```

`--desktop jit aot` adds desktop .NET 11 to the same rotation, run with the
default tiered JIT and published with NativeAOT, for scale. Each run is a fresh
process timing the same region the components time, so the JIT row includes
whatever tiering happens inside that region. Desktop runs native code for the
host CPU, so its rows have no linear memory or component size.

`--mono-aot <dotnet-root> <wasi-sdk>` adds Mono, compiled ahead of time for
`wasi-wasm` with Wasm SIMD and run under the same Wasmtime, with the same
checksum, memory and size measurements. The SDK root needs the
`wasi-experimental` workload and the wasi-sdk release that workload expects
(33.0 for .NET 11 RC3, 25.0 for .NET 10); the project targets the SDK's own
framework. wasi-sdk's clang runs any `wasm-opt` it finds on `PATH`, so the
harness builds Mono with a minimal `PATH`. Wasm samples compile serially (`-C parallel-compilation=n`) so
a sample's startup does not load every core, and `summary.json` keeps each
row's per-sample times.

Peak capacity comes from `memory-host`, a small Wasmtime host pinned to the
same Wasmtime release as the test hosts. It runs the WASI command like
`wasmtime run` and follows every linear memory's creation and growth through a
resource limiter. Linear memory never shrinks, so the total is the
whole-invocation peak: not live managed bytes and not host RSS. Each cell runs
twice and must report the same capacity. `cargo test` in `memory-host` checks
the accounting against Wasmtime's own memory sizes. The harness builds the host
unless `--memory-host` names a built one; the host is not used for timing.

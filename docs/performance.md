# Performance compared with Mono AOT and desktop .NET

NetWasm runs our allocation and garbage-collection benchmarks faster than
Mono AOT, compiled from the same C# source and run by the same Wasmtime. With
NetWasmGC it also takes less time than desktop .NET 11's JIT and desktop
NativeAOT on all three workloads. Without allocation, NetWasm takes less time
than Mono AOT on every workload.

## Results

Median time of 15 runs in milliseconds, with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full transaction | **26.7** (26.4–29.2) | 40.8 (40.4–44.7) | 56.7 (56.2–57.9) | 50.5 (49.9–53.6) | 33.2 (32.1–34.8) |
| Retained object graph | **4.0** (3.9–4.1) | 12.3 (12.2–12.9) | 14.2 (14.1–14.8) | 10.1 (9.6–10.9) | 7.9 (7.4–8.3) |
| UI churn | **5.4** (5.2–5.7) | 20.5 (20.3–20.9) | 26.8 (26.5–28.5) | 9.0 (8.5–9.6) | 7.0 (6.7–7.7) |

NetWasm with NetWasmGC takes 53%, 72% and 80% less time than Mono AOT on the
three workloads and 47%, 60% and 40% less than the desktop JIT. It also takes
less time than desktop NativeAOT on all three: 20% less on the full transaction,
50% on the object graph and 23% on UI churn. Compact, the size-first default, takes less time than Mono AOT on all
three workloads.

| Wasm build | Program size (Brotli 11) | Peak linear memory: transaction, graph, churn |
| --- | ---: | ---: |
| NetWasm, NetWasmGC | 99,247–190,018 B (24,469–45,122 B) | 20.9, 15.7 and 7.3 MiB |
| NetWasm, Compact | 89,391–177,429 B (21,797–42,091 B) | 17.4, 11.8 and 1.7 MiB |
| Mono AOT | 7,775,551–7,857,739 B (1,584,313–1,593,054 B) | 50, 50 and 50 MiB |

Mono reserves 50 MiB of linear memory when it starts, so its peak on every
workload is that reservation rather than its live data. Desktop .NET runs
native code, so it has no linear memory or Wasm program size.

## Without allocation

The three workloads above allocate heavily, so they largely measure memory
management. Four more take allocation out of the timed region: each program
fails if that region allocates a byte or triggers a collection.

- **Preallocated transaction** (`transaction`, `preallocated 100000 101`) runs
  the full transaction's aggregation over containers created and warmed before
  timing: library work without allocation.
- **n-body** (`nbody`, 1,000,000 steps) is the Computer Language Benchmarks
  Game simulation of the Sun and the four gas giants in double precision.
- **Quicksort** (`sort`, 10 rounds) sorts 100,000 integers in place, refilled
  from seed data every round.
- **CRC-32** (`crc32`, 16 passes) computes a table-driven CRC-32 over 1 MiB.

The last three use no library code. Median time of 15 runs in milliseconds,
with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Preallocated transaction | **13.2** (13.0–14.6) | 13.4 (13.2–15.3) | 22.6 (22.3–23.9) | 11.3 (11.1–11.4) | 7.7 (7.6–7.8) |
| n-body | **52.9** (52.5–54.3) | 52.8 (52.6–53.7) | 54.0 (53.2–54.2) | 63.4 (63.2–65.2) | 63.0 (62.9–64.9) |
| Quicksort | **53.9** (53.5–56.8) | 54.3 (53.8–56.0) | 60.1 (59.6–62.2) | 49.2 (48.6–52.3) | 48.5 (48.1–50.7) |
| CRC-32 | **47.6** (47.4–50.4) | 47.6 (47.4–48.9) | 48.3 (47.3–50.5) | 47.7 (47.0–49.9) | 47.2 (46.9–49.2) |

Without allocation, NetWasm takes 41% less time than Mono AOT on the
transaction's library work and 10% less on the quicksort, and 2% less on n-body
and CRC-32. Desktop .NET runs the preallocated transaction
and the quicksort faster than either Wasm build, and every build is within 2.4%
on CRC-32. So much of NetWasm's lead over desktop .NET on the allocating
workloads comes from allocation and collection.

On n-body, desktop .NET 11 is slower than both Wasm builds. Desktop .NET 10's
JIT runs the same program in about 53.5 ms on this machine, measured separately,
so that difference reflects .NET 11 rather than Wasm outrunning native code.

## Micro-benchmarks

dotnet/performance's runtime micro-benchmarks, with its `System.Numerics`
vector, `System.Runtime.Intrinsics` and `System.Memory` benchmarks, and
NetWasm's own workloads, compiled from the same source for each runtime: 2,213
cases that all three run. NetWasm's workloads are a search for the largest prime
below 10,000,000 by trial division, with a remainder test and with a single
division, and the timed regions of the three allocation workloads above, called
repeatedly in one process. Geometric mean of NetWasm's time over the other
runtime's, lower is faster:

| Family | Cases | NetWasm / Mono AOT | NetWasm / desktop .NET 11 JIT |
| --- | ---: | ---: | ---: |
| NetWasm workloads | 5 | 0.45 | 0.68 |
| Runtime | 384 | 0.39 | 0.70 |
| Math | 96 | 0.80 | 1.31 |
| `System.Memory` spans | 370 | 0.41 | 1.59 |
| `System.Numerics` vectors | 770 | 0.27 | 2.22 |
| `System.Runtime.Intrinsics` | 588 | 0.15 | 1.46 |
| All | 2,213 | 0.28 | 1.50 |

Five cases never use what they compute, and Mono AOT's LLVM removes their work
and times an empty loop. They are left out of the NetWasm / Mono AOT means;
counting them, the overall ratio is 0.29.

[Micro-benchmark results](micro-benchmarks.md) lists every case. The three
runtimes ran interleaved: the cases went in chunks of 25, each chunk ran on
every runtime one after another with the order rotating from chunk to chunk and
round to round, and each runtime kept its fastest of two rounds. NetWasm ran at
`O3` with NetWasmGC and wasm-opt repeated until the module stops shrinking
(`NetWasmOptimizerConverge=true`), as a release build in CI would, Mono AOT as configured below, and the desktop JIT with
tiering off, so it runs optimized code from the start. The benchmarks are
dotnet/performance at commit `ceb046992aa203f881d7c8a29d50a105805ecb42`, with
BenchmarkDotNet replaced by a plain timing loop that every runtime shares.
`compiler-qualification/micro-benchmarks/` holds the harness, and its
[README](../compiler-qualification/micro-benchmarks/README.md) gives the command
that reproduces the table.

## What was measured

The workloads are in `compiler-qualification/runtime-performance/workloads/`:

- **Full transaction** (`full 100000 101`) creates 100,000 transactions,
  normalizes account strings, deduplicates through `HashSet<int>` and sums
  `decimal` balances in a `Dictionary<string, decimal>`. Its containers grow
  from empty, so allocation and collection are part of the time.
- **Retained object graph** (`object-graph 100000`) builds a graph of 100,000
  small objects that stays alive.
- **UI churn** (`ui-churn 500`) keeps an array alive and replaces a slice of it
  every frame for 500 frames, allocating many short-lived objects.

Each program times its own work with `Stopwatch`, so process start, Wasm
compilation and runtime start-up are not included. Every run is a fresh
process. After 3 warmup runs, 15 runs of each build are interleaved, rotating
their order every round, and the median is reported. Every run's checksum must
equal the result desktop .NET 11 computes. Peak linear memory is measured by a
small Wasmtime host that follows every memory's growth.

## Versions and settings

- **NetWasm:** 0.13.0, `NetWasmOptimization=O3` and `NetWasmOptimizerConverge=true`,
  with `NetWasmGarbageCollector` set to `NetWasm` or `Compact`. The figures are the lower median of two
  15-run cohorts.
- **Mono AOT:** .NET 11 (SDK 11.0.100) with the
  `wasi-experimental` workload (Mono runtime pack 11.0.0) and
  wasi-sdk 33.0, building for `wasi-wasm` in Release with `RunAOTCompilation`,
  `WasmSingleFileBundle`, `InvariantGlobalization` and Wasm SIMD
  (`WasmEnableSIMD`, with `-msimd128` for clang, which the workload's targets
  leave out). Mono compiles the program and CoreLib ahead of time and keeps its
  interpreter for methods it cannot compile.
- **Desktop .NET:** .NET 11 (SDK 11.0.100), with the default
  tiered JIT and with NativeAOT.
- **Comparison figures:** every build ran in the same rotation as NetWasm's
  first cohort.
- **Engine:** Wasmtime 50.0.1 for every Wasm build, compiling each module on one
  thread (`-C parallel-compilation=n`).
- **Host:** Apple M1 Max, macOS 26.6.2, otherwise idle.

## Reproduce

The harness needs Python 3, the .NET SDKs pinned in `eng/csharp15-toolchain.json`,
Wasmtime 50.0.1 on `PATH`, Node.js for the Brotli sizes, and Rust to build the
memory host. For the Mono row, install the .NET 11 SDK 11.0.100 with the
`wasi-experimental` workload and wasi-sdk 33.0. An empty `--feed` directory
restores the released NetWasm packages from NuGet.org:

```sh
python3 compiler-qualification/runtime-performance/collectors.py \
  --feed <empty-directory> 0.13.0 --output <new-directory> \
  --collectors NetWasm Compact \
  --mono-aot <dotnet-root> <wasi-sdk-root> \
  --desktop jit aot
```

For the workloads without allocation, add
`--scenarios transaction nbody sort crc32`. It prints the table and writes
every run's time to `summary.json`.
`compiler-qualification/runtime-performance/README.md` describes the options.
See [Choosing a garbage collector](runtime-garbage-collection.md) for the
collector comparison on its own.

## Name functions in a profile

A published module keeps no function names, so a profiler shows its functions
only by index, such as Wasmtime's `wasm[0]::function[N]`. To name them, publish
a diagnostic twin of the same project and read the names from it. Never time
the twin.

- Set `NETWASM_KEEP_NAMES=1` for the twin's publish. The compiler's module
  merge then keeps every input's function names, runtime functions included,
  for components and plain modules alike.
- Binaryen drops the names whenever it strips debug information, so also set
  `NETWASM_WASM_OPT_PATH` to
  `compiler-qualification/runtime-performance/named-wasm-opt.py`, with
  `NETWASM_REAL_WASM_OPT` naming the restored `wasm-opt` and
  `NETWASM_NAMED_WASM_DIR` a directory for its output. With
  `NETWASM_KEEP_NAMES` set, every `wasm-opt` run keeps the names, and the copy
  of each stripped output is written there as `<name>.named.wasm`.

The twin's code must strip to the product's exact code, so its functions are
numbered as the product's and an index from a profile of the product names a
twin function. `profile.py` builds both, checks that, and attributes a
scenario's time to named functions with Wasmtime's guest profiler:

```sh
python3 compiler-qualification/runtime-performance/profile.py \
  --feed <feed> <version> --collector NetWasm \
  --scenarios object-graph --output <new-directory>
```

For instruction-level detail, record the product under `perf` with
`wasmtime run --profile=jitdump`, run `perf inject --jit`, and name the hot
functions from the twin. `compiler-qualification/runtime-performance/README.md`
describes the index mapping.

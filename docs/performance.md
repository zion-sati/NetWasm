# Performance compared with Mono AOT and desktop .NET

NetWasm runs our allocation and garbage-collection benchmarks faster than
Mono AOT, compiled from the same C# source and run by the same Wasmtime. With
NetWasmGC it also takes less time than desktop .NET 11's JIT on all three
workloads, and less than desktop NativeAOT on the full transaction and the
retained object graph. Without allocation, NetWasm takes less time than Mono AOT
or matches it on every workload.

## Results

Median time of 15 runs in milliseconds, with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full transaction | **28.2** (28.1–28.5) | 41.8 (41.6–42.6) | 56.6 (56.5–57.2) | 49.8 (49.2–50.7) | 32.4 (32.2–33.2) |
| Retained object graph | **4.8** (4.8–5.0) | 12.5 (12.4–12.7) | 14.4 (14.3–14.7) | 9.8 (9.7–11.7) | 7.6 (7.5–8.2) |
| UI churn | **7.7** (7.7–7.8) | 21.0 (20.9–21.3) | 27.0 (26.7–27.9) | 8.7 (8.6–9.1) | 6.7 (6.6–6.8) |

NetWasm with NetWasmGC takes 50%, 66% and 71% less time than Mono AOT on the
three workloads and 43%, 51% and 11% less than the desktop JIT. It also takes 13%
less than desktop NativeAOT on the full transaction and 36% less on the object
graph. Compact, the size-first default, takes less time than Mono AOT on all
three workloads.

| Wasm build | Program size (Brotli 11) | Peak linear memory: transaction, graph, churn |
| --- | ---: | ---: |
| NetWasm, NetWasmGC | 87,833–200,645 B (21,331–43,990 B) | 20.9, 15.7 and 7.3 MiB |
| NetWasm, Compact | 83,261–195,846 B (19,602–42,272 B) | 17.4, 11.8 and 1.7 MiB |
| Mono AOT | 7,761,543–7,848,447 B (1,579,198–1,593,933 B) | 50, 50 and 50 MiB |

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
| Preallocated transaction | **13.3** (13.2–13.4) | 13.5 (13.4–14.7) | 22.5 (22.3–23.8) | 11.2 (11.0–11.9) | 7.6 (7.6–8.0) |
| n-body | **52.9** (52.5–54.6) | 52.7 (52.5–54.8) | 53.3 (53.2–55.1) | 63.6 (63.4–65.4) | 62.9 (62.8–65.6) |
| Quicksort | **54.0** (53.8–55.2) | 53.3 (53.1–54.2) | 57.9 (57.7–59.2) | 48.7 (48.5–49.4) | 48.2 (48.0–50.1) |
| CRC-32 | **47.4** (47.3–47.8) | 47.4 (47.3–48.6) | 47.4 (47.4–47.8) | 46.9 (46.9–47.3) | 46.9 (46.8–47.8) |

Without allocation, NetWasm takes 41% less time than Mono AOT on the
transaction's library work and 7% less on the quicksort, and matches it on
n-body and CRC-32. Desktop .NET runs the preallocated transaction
and the quicksort faster than either Wasm build, and every build is within 1.2%
on CRC-32. So much of NetWasm's lead over desktop .NET on the allocating
workloads comes from allocation and collection.

On n-body, desktop .NET 11 RC3 is slower than both Wasm builds. Desktop .NET 10's
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
| NetWasm workloads | 5 | 0.49 | 0.73 |
| Runtime | 384 | 0.40 | 0.72 |
| Math | 96 | 0.80 | 1.32 |
| `System.Memory` spans | 370 | 0.43 | 1.64 |
| `System.Numerics` vectors | 770 | 0.28 | 2.28 |
| `System.Runtime.Intrinsics` | 588 | 0.16 | 1.49 |
| All | 2,213 | 0.29 | 1.54 |

Five cases never use what they compute, and Mono AOT's LLVM removes their work
and times an empty loop. They are left out of the NetWasm / Mono AOT means;
counting them, the overall ratio is 0.30.

[Micro-benchmark results](micro-benchmarks.md) lists every case. The three
runtimes ran interleaved: the cases went in chunks of 50, each chunk ran on
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

- **NetWasm:** 0.11.2, `NetWasmOptimization=O3` and `NetWasmOptimizerConverge=true`,
  with `NetWasmGarbageCollector` set to `NetWasm` or `Compact`. The figures are the lower median of two
  15-run cohorts.
- **Mono AOT:** .NET 11 RC3 (SDK 11.0.100-rc.3.26522.148) with the
  `wasi-experimental` workload (Mono runtime pack 11.0.0-rc.3.26522.148) and
  wasi-sdk 33.0, building for `wasi-wasm` in Release with `RunAOTCompilation`,
  `WasmSingleFileBundle`, `InvariantGlobalization` and Wasm SIMD
  (`WasmEnableSIMD`, with `-msimd128` for clang, which the workload's targets
  leave out). Mono compiles the program and CoreLib ahead of time and keeps its
  interpreter for methods it cannot compile.
- **Desktop .NET:** .NET 11 RC3 (SDK 11.0.100-rc.3.26522.148), with the default
  tiered JIT and with NativeAOT.
- **Comparison figures:** every build ran in the same rotation as NetWasm's
  first cohort.
- **Engine:** Wasmtime 49.0.3 for every Wasm build, compiling each module on one
  thread (`-C parallel-compilation=n`).
- **Host:** Apple M1 Max, macOS 26.6.2, otherwise idle.

## Reproduce

The harness needs Python 3, the .NET SDKs pinned in `eng/csharp15-toolchain.json`,
Wasmtime 49.0.3 on `PATH`, Node.js for the Brotli sizes, and Rust to build the
memory host. For the Mono row, install a .NET 11 RC3 SDK with the
`wasi-experimental` workload and wasi-sdk 33.0. An empty `--feed` directory
restores the released NetWasm packages from NuGet.org:

```sh
python3 compiler-qualification/runtime-performance/collectors.py \
  --feed <empty-directory> 0.11.2 --output <new-directory> \
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

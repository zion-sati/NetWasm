# Micro-benchmarks

`run.py` times dotnet/performance's runtime micro-benchmarks, with its
`System.Numerics` vector, `System.Runtime.Intrinsics` and `System.Memory`
benchmarks, and NetWasm's own workloads, on three runtimes:

- desktop .NET 11's JIT, without tiering (`DOTNET_TieredCompilation=0`);
- Mono AOT for `wasi-wasm` with Wasm SIMD, under Wasmtime;
- NetWasm at `O3` with NetWasmGC, wasm-opt repeating its passes until the module
  stops shrinking (`NetWasmOptimizerConverge=true`), under Wasmtime.

It writes every case's time to `results.tsv` and the per-family table to
`table.md`, in the form of the [micro-benchmark results](../../docs/micro-benchmarks.md).

## How it measures

The benchmarks use BenchmarkDotNet's attributes, and NetWasm and Mono AOT have
no reflection to drive BenchmarkDotNet. `generator/` reads the benchmark sources
with Roslyn and writes `Benchmarks.g.cs`, a table of every case as plain C#: the
construction, each `[Params]` value, `[GlobalSetup]`, `[IterationSetup]` and
each `[Arguments]` set. `shim/` supplies the attributes as types without
behaviour, and two of dotnet/performance's helper files. Every runtime compiles
the same sources and table, with the .NET version symbols the benchmarks test
defined for all three.

`runner/` times a case with a warm-up call, then a batch of calls doubled until
it lasts 20 ms, and reports the fastest of five batches as nanoseconds per
call. Each timed body is a function of its own, so one case's code does not
change how the engine compiles another's. A result passes through
`Runner.Consume`, so the work that produced it cannot be dropped.

The cases all three runtimes list are timed in chunks of 50. Each chunk runs on
the three runtimes one after another, the order rotating from chunk to chunk
and round to round, and each runtime keeps its fastest time for each case over
two rounds. `timing.log` records each chunk's time and the machine's load.

`exclusions.txt` lists the benchmark sources no runtime builds, with the
reason. Five cases never use what they compute, and Mono AOT's LLVM removes
their work: `mono-removed.txt` lists them, and `table.py` marks them with a
dagger and leaves them out of the NetWasm / Mono AOT means.

`workloads/` holds NetWasm's own workloads: issue #166's prime search, and the
timed regions of the [runtime-performance](../runtime-performance/README.md)
allocation workloads.

## Reproduce

The harness needs Python 3, Git, the .NET SDKs pinned in
`eng/csharp15-toolchain.json` and Wasmtime 50.0.1 on `PATH`. For Mono AOT,
install the .NET 11 SDK 11.0.100 with the `wasi-experimental` workload, and wasi-sdk
33.0. Check out dotnet/performance at the commit `run.py` pins:

```sh
git clone https://github.com/dotnet/performance <performance>
git -C <performance> checkout ceb046992aa203f881d7c8a29d50a105805ecb42
```

An empty `--feed` directory restores the released NetWasm packages from
NuGet.org:

```sh
python3 compiler-qualification/micro-benchmarks/run.py \
  --performance <performance> \
  --feed <empty-directory> 0.13.0 --libraries 0.11.0 \
  --mono-aot <dotnet-root> <wasi-sdk-root> \
  --output <new-directory>
```

`--libraries` names the `NetWasm.System.Linq` and
`NetWasm.System.Text.RegularExpressions` version when it differs from
NetWasm's. A full run builds the three runners, then times 2,213 cases for
about two hours; run it on an otherwise idle machine. `--filter` times only the
cases whose name contains its text, and `--limit` the first cases only.
`table.py` writes the table again from a `results.tsv`.

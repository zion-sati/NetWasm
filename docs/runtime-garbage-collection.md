# Choosing a garbage collector

**Compact is the default garbage collector**.
You do not need to configure a collector to build an application.

## Collector choices

Compact is the size-first, precise, non-moving mark-and-sweep collector.
It traces compiler-published roots and reference layouts without conservative
scanning. Choose it when small, self-contained output matters most. The name
describes implementation size, not heap compaction: live objects do not move.

NetWasmGC is the performance-oriented, precise, non-moving collector, selected
with the value `NetWasm`. It trades additional implementation size and memory
for faster allocation and collection paths. It is non-generational and collects
synchronously. It uses the same compiler-published roots and managed lifetime
contracts as Compact. Benchmark your application rather than assuming one
collector is universally faster.

The Boehm (BDWGC) backend has been removed. A project that still sets
`NetWasmGarbageCollector` to `Boehm` fails to build; remove the property to use
Compact, or select `NetWasm` for throughput.

Bump is a production non-collecting allocator for workloads with bounded
lifetimes and memory consumption, such as one-shot tools and plugins whose
entire Wasm instance is discarded after a job. Allocation advances through
lazily acquired chunks; managed objects are never reclaimed. `GC.Collect()`
does not recover their memory. Projects requiring finalization, weak handles
or ephemeron handles are rejected at build time. Internal runtime bookkeeping
does not expand this supported public lifetime surface.

The tracing collectors support NetWasm's managed lifetime profile, including
finalization, resurrection, weak references, dependent handles and pinning.
Collector selection does not enable threading, reflection or other APIs outside
the supported NetWasm profile.

## Measured performance

These figures come from complete managed workloads, not isolated
allocator microbenchmarks. The figures below are the lower median from two
cohorts on an Apple M1 Max under Wasmtime 49.0.3. Each cohort ran three warmups
and then fifteen fresh-process samples per collector, rotating the collector
order on every sample. Every cell used the same managed program and the `O3`
compiler profile, and every result matched the same program on desktop .NET;
lower is better.

| Collector | Full transaction | Retained object graph | UI-style object churn |
| --- | ---: | ---: | ---: |
| Bump | 31.35 ms | 4.17 ms | 9.40 ms |
| NetWasmGC | 34.77 ms | 4.65 ms | 7.77 ms |
| Compact | 49.33 ms | 12.43 ms | 20.89 ms |

The full transaction creates 100,000 transactions, normalizes their account
strings and aggregates them into growing containers, with allocation and
collection inside the timed region. Bump is not an equivalent lifetime choice:
it never collects or reclaims managed objects. Nor is it always fastest. On UI
churn, where Bump grows to 20 MiB while NetWasmGC reuses 7.3 MiB, NetWasmGC
takes less time.

The same artifacts reached this aggregate Wasm linear memory capacity:

| Collector | Full transaction | Retained object graph | UI-style object churn |
| --- | ---: | ---: | ---: |
| Bump | 20.375 MiB | 12.375 MiB | 20 MiB |
| NetWasmGC | 20.9375 MiB | 15.6875 MiB | 7.3125 MiB |
| Compact | 17.4375 MiB | 11.875 MiB | 1.6875 MiB |

This is whole-invocation linear-memory capacity, not live managed bytes, host
RSS or a promised ceiling. Each figure repeated exactly across runs. It makes
the tradeoffs visible: Bump keeps growing because it never reclaims, and Compact
trades throughput for the smallest tracing-collector artifact.

The corresponding complete Console `42` components, published with the SDK at
`Oz`, are 34,886 bytes for Bump (11,506 with Brotli quality 11), 38,919 for
Compact (13,213) and 41,879 for NetWasmGC (14,533). See
[Size and methodology](size-and-methodology.md#collector-selection-and-compression)
for the exact size boundary. Timing and file size measure
different things; neither table is a universal collector ranking.

## Selecting a collector

Set the property on the executable application, worker or test project:

```xml
<PropertyGroup>
  <NetWasmGarbageCollector>NetWasm</NetWasmGarbageCollector>
</PropertyGroup>
```

Or select it for a build/run:

```sh
dotnet run -c Release -p:NetWasmGarbageCollector=NetWasm
```

The canonical, case-sensitive values are `Compact`, `NetWasm` and `Bump`.
Leaving the property unset selects Compact. All three are packaged for wasm32
and wasm64; only the selected backend is linked into an application.
Class libraries remain collector-neutral.

Selection is a build-time choice, not a runtime switch or a `global.json`
setting. Rebuild when changing it. A caller-supplied runtime-file override is
not supported. To test a locally built runtime, use matching packages from a
local NuGet feed.

## Collection policy

Compact and NetWasmGC collect synchronously at allocation boundaries, using
adaptive allocation-pressure budgets. Compact's minimum budget is 1 MiB;
NetWasmGC's is 4 MiB. These are pacing floors, not permanently reserved heap
sizes, exact memory-use limits or promises of pause-free allocation. Budgets
adapt to retained memory after collection. Most applications should leave
collection to the runtime rather than repeatedly calling `GC.Collect()`.

Bump starts with a 256 KiB allocation chunk, acquired lazily. Subsequent normal
chunks grow geometrically up to 4 MiB; individual large requests can require
larger backing allocations. Memory grows until the instance is discarded or
the Wasm memory limit is reached. This makes Bump useful as both a bounded-
lifetime production option and a no-collection diagnostic control.

## Prebuilt runtimes and application builds

Runtime packs contain prebuilt Release native archives. Application builds
select and link them, including link-time optimization; they do not recompile
the collector's C source whenever application code changes. Both Debug and
Release C# builds use these prebuilt archives.

`O1`, `O2` and `O3` select speed-oriented native archives. `None`, `O0`, `Os`
and `Oz` select the size-oriented archives. Collector choice and optimization
mode are separate settings.

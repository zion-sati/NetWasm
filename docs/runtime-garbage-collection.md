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

NetWasmGC with generations is selected with the value `NetWasmGen`. It is
NetWasmGC with one addition: an object that has survived a collection is old,
and most collections are minor ones, which look only at the objects allocated
since the last collection and at the old objects a reference has been stored
into since. A program built for it records every store of a reference into
memory, which costs a little at each such store and about one byte per 256
bytes of heap. Choose it for programs that keep a sizeable set of objects
alive and allocate short-lived ones on top without rewriting the kept set
much: request handling against in-memory state, building and discarding
trees, per-frame garbage. Stay on `NetWasm` for short runs, for programs that
mostly build data they keep, and for code that rewrites its kept data
heavily. It is opt-in: measure your application before choosing it.

The Boehm (BDWGC) backend has been removed. A project that still sets
`NetWasmGarbageCollector` to `Boehm` fails to build; remove the property to use
Compact, or select `NetWasm` for throughput.

Bump is a production non-collecting allocator for workloads with bounded
lifetimes and memory consumption, such as one-shot tools and plugins whose
entire Wasm instance is discarded after a job. Allocation advances through
lazily acquired chunks; managed objects are never reclaimed. `GC.Collect()`
does not recover their memory. Since no object ever becomes unreachable to
Bump, finalizers never run, weak references never clear and a
`ConditionalWeakTable` keeps every value. .NET runs a finalizer only after a
collection finds its object unreachable, and none when a process exits, so
programs and libraries that use them build and run unchanged.

The tracing collectors support NetWasm's managed lifetime profile, including
finalization, resurrection, weak references, dependent handles and pinning.
Collector selection does not enable threading, reflection or other APIs outside
the supported NetWasm profile.

## Measured performance

These figures come from complete managed workloads, not isolated
allocator microbenchmarks. The figures below are the lower median from two
cohorts on an Apple M1 Max under Wasmtime 50.0.1. Each cohort ran three warmups
and then fifteen fresh-process samples per collector, rotating the collector
order on every sample. Every cell used the same managed program and the `O3`
compiler profile, and every result matched the same program on desktop .NET;
lower is better.

| Collector | Full transaction | Retained object graph | UI-style object churn |
| --- | ---: | ---: | ---: |
| Bump | 24.11 ms | 3.83 ms | 8.34 ms |
| NetWasmGC | 26.84 ms | 3.95 ms | 5.34 ms |
| Compact | 40.85 ms | 12.03 ms | 20.12 ms |

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
| Compact | 17.4375 MiB | 11.8125 MiB | 1.6875 MiB |

This is whole-invocation linear-memory capacity, not live managed bytes, host
RSS or a promised ceiling. Each figure repeated exactly across runs. It makes
the tradeoffs visible: Bump keeps growing because it never reclaims, and Compact
trades throughput for the smallest tracing-collector artifact.

The corresponding complete Console `42` components, published with the SDK at
`Oz`, are 23,810 bytes for Bump (9,870 with Brotli quality 11), 28,383 for
Compact (11,992) and 32,159 for NetWasmGC (13,478). See
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

The canonical, case-sensitive values are `Compact`, `NetWasm`, `NetWasmGen`
and `Bump`. Leaving the property unset selects Compact. All four are packaged
for wasm32 and wasm64; only the selected backend is linked into an
application. Class libraries remain collector-neutral.

Selection is a build-time choice, not a runtime switch or a `global.json`
setting. Rebuild when changing it: the collector is part of the compile as
well as the link, since a program for `NetWasmGen` records its stores of
references and a program for any other collector does not. `nwc` takes the
same name as `-gc:<collector>`, and the link refuses a `NetWasmGen` runtime
for a program that was compiled for another collector. A caller-supplied runtime-file override is
not supported. To test a locally built runtime, use matching packages from a
local NuGet feed.

## Collection policy

Compact and NetWasmGC collect synchronously at allocation boundaries, using
adaptive allocation-pressure budgets. Compact's minimum budget is 1 MiB;
NetWasmGC's is 4 MiB. These are pacing floors, not permanently reserved heap
sizes, exact memory-use limits or promises of pause-free allocation. Budgets
adapt to retained memory after collection: the next collection is due when the
heap reaches twice what survived plus the minimum budget. Most applications
should leave collection to the runtime rather than repeatedly calling
`GC.Collect()`.

NetWasmGC adapts in three more ways:

- When at least 60% of the heap survives a collection, the program is building
  data it keeps, and collecting again soon would trace all of it once more to
  free little. The heap may then grow to three times what survived.
- The blocks that hold small objects stay with the collector, so refilling them
  takes no memory the program has not used before. A collection is never due
  before small objects occupy as much as they have occupied before. Large
  objects do not count: their memory is released when they die.
- When no memory is left for the heap to grow before a collection is due, the
  allocation collects once and tries again before it throws
  `OutOfMemoryException`.

NetWasmGen follows the same rule for its collections of every object, so its
heap is bounded as NetWasmGC's is. Between them it runs a minor collection
each time the heap has grown by the minimum budget. A minor collection that
finds most of what it looks at alive would cost as much as a collection of
every object and free little, so it is given up; minor collections then wait
for the first collection of every object after four budgets of allocation,
then sixteen, then sixty-four, and start over once one runs to its end.
`GC.Collect()` collects every object. `GC.Collect(0)` runs a minor
collection, which frees young objects only and always runs to its end; after a
minor collection was given up, it collects every object. Under every other
collector `GC.Collect(0)` collects every object, as `GC.Collect()` does. An
idle host's collection is a minor one while those pay, and a quiet host's
collects every object.

A host that knows when it is idle can give that time to the collector. The
runtime exports `collect_when_idle`: called between calls into the module, it
collects when the heap has grown, since the last collection, by at least a
quarter of the minimum budget and half of what survived, so the collection the
program is heading for need not happen during its next call. It returns 1 when
it collected and 0 otherwise. A `JsModule` does this on its own: once the
module has run, its host asks for the next idle period and calls it then. In a
page that is the browser's `requestIdleCallback`; where there is none (Safari,
a worker, Node.js) it is a short timer, which runs once the event loop has
nothing queued ahead of it and, in Node.js, does not keep the process alive.

A program that drops what it held without allocating, as when it clears a
table, leaves garbage that no growth of the heap announces, so an idle
collection would not find it worth collecting. For that the runtime exports
`collect_when_quiet`, which collects whatever the heap has grown by. It
returns 0 when it did not collect, and otherwise 1 plus the share of the heap
the collection freed, in 255ths. A `JsModule` host calls it once the module
has run and then has not run for a whole quiet period, one second to begin
with: one collection follows each stretch of activity, and none follows a
stretch an idle collection already cleaned up after. A collection that freed
less than an eighth of the heap makes the next quiet period twice as long, up
to a minute, and one that paid starts over; a period is also never shorter
than twenty times what the last collection took. The host owns the timer and
the clock, and a call into the module costs it two flags: the runtime has no
clock of its own.

A component's own event loop does the same for an asynchronous program, an
`async Main` or asynchronous exports, so a host such as Wasmtime needs to do
nothing: once the program has run and the loop waits for its events, the
loop sets a timer of a fifth of a second for the idle collection and the
quiet period's timer, both on the program's monotonic clock, and the same
rules apply. Neither timer keeps the loop waiting: when the program's task
completes they are cancelled. A synchronous `Main` has no event loop and
asks for neither.

Other hosts collect only as allocation asks them to, unless their embedder
calls the exports: a core module's embedder calls `collect_when_idle` when it
is idle and `collect_when_quiet` once it has been quiet. Bump never collects.

The finalizers of objects a collection finds unreachable run when the call
that collected returns, or for an asynchronous program when its event loop's
turn ends, and at once in `GC.WaitForPendingFinalizers()`. Those an idle
collection queues run when the next call into the module returns.

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

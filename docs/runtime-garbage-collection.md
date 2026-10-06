# Choosing a garbage collector

Starting with NetWasm 0.7.0, **Compact is the default garbage collector**.
You do not need to configure a collector to build an application.

## Compact or Boehm?

Compact is NetWasm's size-first, precise, non-moving mark-and-sweep collector.
It traces the roots and reference layouts published by the compiler, without
conservative scanning. Choose it for the default small, self-contained output.
It supports the managed lifetime features in NetWasm's profile, including
finalization, resurrection, weak references, dependent handles and pinning.
"Compact" describes the implementation's size, not heap compaction: live objects
do not move.

Boehm is the alternative BDWGC backend. Its allocation paths can be faster for
allocation-heavy workloads, particularly many small objects, but it typically
adds more code to the final module. NetWasm supplies exact roots and layouts to
Boehm too; Boehm still has conservative pressure paths. Benchmark your actual
application before choosing it for performance. Neither backend is universally
faster.

Both collectors manage memory automatically. They do not make desktop .NET
threading or reflection available. Collector selection does not change the
documented NetWasm platform boundaries or enable unsupported APIs.

## Selecting a collector

To use Boehm, add this to the executable project's `.csproj`:

```xml
<PropertyGroup>
  <NetWasmGarbageCollector>Boehm</NetWasmGarbageCollector>
</PropertyGroup>
```

Or select it for a build/run:

```sh
dotnet run -c Release -p:NetWasmGarbageCollector=Boehm
```

The canonical, case-sensitive values are `Compact` and `Boehm`. Leaving the
property unset selects Compact. Both backends ship in the runtime pack for
wasm32 and wasm64, but only the selected backend is linked into an application.

Selection belongs to the executable application, worker or test root. Class
libraries are collector-neutral. This is a build-time choice, not a runtime
switch or a `global.json` setting. Rebuild the application when changing it.
An explicit choice with a caller-supplied prebuilt runtime is rejected when
matching collector provenance is unavailable.

## Collection policy

Compact collects synchronously at allocation boundaries when allocation pressure
reaches an adaptive threshold. The threshold has a 64 KiB minimum and adapts to
live memory after collection. This is not a timer, an exact memory-use ceiling,
or a promise of pause-free allocation. Most applications should leave collection
to the runtime rather than calling `GC.Collect()` repeatedly.

## Prebuilt runtimes and application builds

Published runtime packs contain prebuilt, size-optimized Release native archives.
Ordinary application builds select and link those archives, including link-time
optimization, rather than recompiling the collector's C source. Both Debug and
Release C# application builds use these prebuilt native archives.

## Boehm diagnostic-enabled native runtimes

`GC_DONT_GC` is a maintainer diagnostic for a Boehm native runtime built with
`NETWASM_GC_DIAGNOSTICS`. The native runtime build script defines this only for
its `--configuration debug` mode. It is not enabled in published runtime packs:
**`dotnet build -c Debug` does not enable the hook or rebuild the collector.**

With that diagnostic-enabled native runtime, the module's WASI environment can
provide `GC_DONT_GC` before runtime initialization to disable collection while
retaining the allocator. Presence alone enables it, even with an empty value or
`0`. The host must forward the variable through WASI Preview 2.

This does not apply to Compact. Native Release builds preprocess the hook out
before packaging. It cannot be switched after initialization, and explicit
collection requests do not override it. Without collection, managed memory is
not reclaimed.

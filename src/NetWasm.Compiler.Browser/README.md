# NetWasm.Compiler.Browser

`NetWasm.Compiler.Browser` provides the virtual-input compiler boundary used by
browser and WebAssembly hosts. Callers supply managed assemblies, references,
WIT documents and source metadata as owned byte arrays; the adapter returns
diagnostics and compiler artifacts without reading arbitrary host paths.

The package carries the closed NetWasm compiler assembly dependency set needed
by that adapter. Hosts remain responsible for supplying Roslyn and for enforcing
their own memory, time, source-size and capability limits.

Create one `BrowserCompilerSession` per retained compiler worker and reuse it for
sequential calls. The static `BrowserCompiler.Compile` entry point remains
available for one-shot compilation.

An `O0` compilation can reuse each method's compiled code from the session's last
compilation: set `CompilerOptions.CompiledUnits` and give
`CompilerOptions.IntermediateOutputPath` a name for the units (the session keeps
them in memory under that name, never on a file system). A reused method is one
whose inputs and every fact its code was built from still hold. To keep units
beyond the session, store `ExportCompiledUnits(name)` (for example in IndexedDB)
and pass it to `ImportCompiledUnits(name, units)` in a later session; units from
another compiler version are not reused. `CompiledUnitSummary(name)` reports what
the last compilation reused.

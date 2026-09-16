# NetWasm.Compiler.Browser

`NetWasm.Compiler.Browser` provides the virtual-input compiler boundary used by
browser and WebAssembly hosts. Callers supply managed assemblies, references,
WIT documents and source metadata as owned byte arrays; the adapter returns
diagnostics and compiler artifacts without reading arbitrary host paths.

The package carries the closed NetWasm compiler assembly dependency set needed
by that adapter. Hosts remain responsible for supplying Roslyn and for enforcing
their own memory, time, source-size and capability limits.

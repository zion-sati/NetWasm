# NetWasm.Compiler.Browser

`NetWasm.Compiler.Browser` provides the virtual-input compiler boundary used by
browser and WebAssembly hosts. Callers supply managed assemblies, references,
WIT documents and source metadata as owned byte arrays; the adapter returns
diagnostics and compiler artifacts without reading arbitrary host paths.

The package carries the closed NetWasm compiler assembly dependency set needed
by that adapter. Hosts remain responsible for supplying Roslyn and for enforcing
their own memory, time, source-size and capability limits.

Create one `BrowserCompilerSession` per retained compiler worker and reuse it for
sequential calls. Set `CompilerOptions.EnableFrontendCache` for development builds
to reuse completed dependency analysis and method-local lowering in that session.
The static `BrowserCompiler.Compile` entry point remains available for one-shot
compilation and does not retain frontend artifacts between calls.

# NetWasm documentation

NetWasm compiles C# to small, fast WebAssembly with the ordinary `dotnet`
commands: console programs for Node and WASI hosts, JavaScript modules for web
pages and workers, and WebAssembly components.

## Guides

| Guide | What it covers |
| --- | --- |
| [Getting started](getting-started.md) | Install, create a console program and a web page, run and publish them |
| [SDK quickstart](sdk-quickstart.md) | Each step in depth: requirements, output kinds, publishing, libraries, upgrading |
| [What a project can build](sdk-quickstart.md#choose-the-output-kind) | Programs, JavaScript modules, Web Workers, HTTP services, components and core modules |
| [JavaScript interop](javascript-interop.md) | `[JSImport]` and `[JSExport]`, the generated `open()` module, hosts, the types that cross, callbacks, tasks and cancellation |
| [DOM and Canvas from C#](dom-and-canvas.md) | Read and change the page, handle events, draw every animation frame |
| [C# Web Workers](web-workers.md) | Run C# off the page's thread and call it through a generated client |
| [HTTP services](sdk-quickstart.md#write-an-http-service) | Serve HTTP from a `wasi:http` component with `HttpRequestMessage` and `HttpResponseMessage` |
| [Files, network and other host capabilities](sdk-quickstart.md#host-capabilities) | What a program can reach and how a host grants it |
| [Time zones](timezone-assets.md) | Ship time zone data and set the program's zone |
| [Packages and libraries](netstandard-libraries.md) | Which NuGet packages work, what fails and why, and writing libraries that work everywhere |
| [Popular NuGet packages](package-compatibility.md) | How the most-downloaded packages run, and replacements for the ones that do not |
| [Native C libraries](static-native-interop.md) | Link C code with `[LibraryImport]` and call back with `[UnmanagedCallersOnly]` |
| [WIT and components](wit-bindings.md) | Generate C# from WIT and build components for any WASI 0.3 host |
| [Garbage collectors](runtime-garbage-collection.md) | Choose between the size-first default and the faster collectors |
| [Troubleshooting](troubleshooting.md) | Common build and run-time problems and their fixes |

## Reference

| Page | What it covers |
| --- | --- |
| [Configuration](configuration.md) | Every MSBuild property and item, with its default |
| [Optimization](optimization.md) | The optimization levels and every individual optimization |
| [Compiler diagnostics](diagnostics.md) | Every compiler diagnostic, exceptions, stack traces and failure reports |
| [Support status](support-status.md) | What is supported, partly supported and not supported |
| [Differences from desktop .NET](spec-deviations.md) | Deliberate profile differences and results the specification permits |
| [Targets and output formats](targets-and-outputs.md) | `wasm32` and `wasm64`, core modules and components, WASI versions |
| [CoreLib and runtime](corelib-runtime.md) | The framework's boundary and what it leaves out |
| [Size measurements](size-and-methodology.md) | How small programs are and how to reproduce the numbers |
| [Performance](performance.md) and [micro-benchmarks](micro-benchmarks.md) | Speed and memory against Mono AOT and desktop .NET |
| [Changelog](changelog.md) | Every release and its notes |
| [Roadmap](roadmap.md) | What is planned before 1.0 |
| [Licensing](licensing.md) | What is MIT and what the NetWasm Community License covers |
| [All pages](index.md) | The full index, including design and maintenance notes |

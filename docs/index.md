# NetWasm documentation

NetWasm is an experimental closed-world CIL-to-WebAssembly toolchain and a
smaller .NET platform with an explicit CoreLib, runtime, and host boundary.
There is no production-support or compatibility commitment.

## Start here

- [Size measurements and methodology](size-and-methodology.md) — reproduce the
  91,294-byte C# component and read the Rust comparison and JSON scenario limits.
- [Support status and roadmap](support-status.md)
- [Quickstart](../QUICKSTART.md) — create and run an application with tools
  restored through NuGet.
- [CLI, build, and deployment](cli-build-deployment.md)
- [Targets and output formats](targets-and-outputs.md)
- [Compiler diagnostics](diagnostics.md)
- [CoreLib and runtime compatibility](corelib-runtime.md)
- [NetWasm SDK quickstart](sdk-quickstart.md)
- [Timezone assets](timezone-assets.md)
- [Known gaps and boundaries](gaps.md)
- [Licensing boundary](licensing.md)
- [Documentation maintenance](maintenance.md)
- [Architecture](ARCHITECTURE.md)
- [Current limitations](LIMITATIONS.md)

The compiler, linker, optimizer, compiler/build tooling, debugger, IDE/browser
tools, and other developer tooling are covered by the NetWasm Community
License 1.0. `NetWasm.CoreLib`, `NetWasm.Runtime`, templates, generated
support code, and adjacent framework ports are MIT, subject to preserved
upstream notices. See the [license map](../LICENSE-MAP.md) and the [licensing
guide](licensing.md).

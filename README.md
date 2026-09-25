# C# at native Wasm size.

Write C#. Deploy like C++. Target WASI.

A clean Release build of `Console.WriteLine(42)` produces a **91,294-byte
final Wasm artifact—runtime and precise garbage collection included.**

[![Publication](https://img.shields.io/github/actions/workflow/status/zion-sati/NetWasm/release.yml?label=publish&event=release)](https://github.com/zion-sati/NetWasm/actions/workflows/release.yml)
[![License: Community + MIT](https://img.shields.io/badge/license-Community%20%2B%20MIT-blue)](docs/licensing.md)

NetWasm compiles C# into a standalone WebAssembly Component, without carrying
the desktop .NET runtime. Keep the language, generics, exceptions and managed
memory. Deploy the compiled program to a compatible WASI host.

[Try it in the browser](https://playground.netwasm.com/) ·
[Measured size and reproduction steps](docs/size-and-methodology.md) ·
[Quickstart](docs/sdk-quickstart.md) ·
[Supported APIs and limitations](docs/support-status.md)

## Try it

Install [.NET SDK 10.0.300 or newer](https://dotnet.microsoft.com/download/dotnet/10.0),
then follow the [host and restore notes](docs/sdk-quickstart.md#1-install-net).
Projects using C# 15 select the .NET 11 SDK; projects using earlier language
versions can stay on .NET 10, and neither path requires both SDKs.
The SDK restores its pinned Node, LLD and Binaryen tools for your development
host through NuGet. You do not need to install Emscripten, Git or Python to
build an ordinary app or test project.

Install, create, run and publish:

```sh
dotnet new install NetWasm.Templates
dotnet new netwasm-app -n HelloNetWasm
cd HelloNetWasm
dotnet restore
dotnet run
dotnet publish -c Release -o publish/local
```

Run the published component directly with a compatible WASI Preview 2 host.
For example, with [Wasmtime](https://wasmtime.dev/):

```sh
wasmtime run publish/local/HelloNetWasm.wasm
```

```text
42
```

The component is self-contained with respect to .NET: no CLR, Mono,
`dotnet.js`, application DLLs, or NetWasm installation is required at runtime.
A WebAssembly host is still required. **Build with .NET; run without .NET.**

For Windows host support, browser publishing and dual-target libraries, follow the
[complete SDK quickstart](docs/sdk-quickstart.md).

## More than Hello World

The 89.6 KB program is the baseline, not the boundary. NetWasm already covers a practical .NET development loop:

- C# 15 when the project selects the .NET 11 SDK, while the default template remains on .NET 10;
- `dotnet build`, `dotnet run`, `dotnet publish` and `dotnet test`;
- [TUnit-NetWasm](https://github.com/zion-sati/TUnit-NetWasm) tests through the generic VSTest bridge and ordinary `dotnet test`;
- independently packaged ports for LINQ, HTTP, JSON, XML, Regex, Hashing and reflection-free dependency injection;
- dual-target library projects for `netwasm0.1` and desktop `net10.0`.

The supported surface is deliberately smaller than desktop .NET, but it is already broad enough to build, test and package non-trivial programs.

## .NET designed around Wasm

I started NetWasm with a simple challenge: **“Kotlin has it—why can’t we?!”**

“It” is [Kotlin Multiplatform (KMP)'s common API surface](https://kotlinlang.org/docs/multiplatform/multiplatform-discover-project.html):
shared code uses portable APIs instead of the whole JVM/Java library surface,
with [Wasm among its targets](https://kotlinlang.org/docs/wasm-overview.html).
Think of a smaller platform contract, in the spirit of .NET Standard—not a
desktop runtime squeezed into every deployment.

C# does not require the desktop .NET runtime's entire compatibility surface.
NetWasm starts with a different boundary: a smaller CoreLib, closed-world
compilation, precise garbage collection and explicit WASI interfaces.

Roslyn produces CIL. NetWasm compiles the reachable program directly to Wasm,
specializes its generics, and links only the runtime support it needs. It does
not ship a CLR, Mono, `dotnet.js`, or application assemblies. The linked Wasm
component is the program.

This is a new .NET platform, not an attempt to make every desktop assumption
work inside a Wasm sandbox. That tradeoff is what makes the small artifact
possible.

## Pay for what you use

- **Reachable code and imports only.** An unused package reference is not a
  reason to retain its implementation or request its host capabilities.
- **No runtime type names.** Runtime type-name lookup and broad reflection are
  unsupported, so namespace-qualified type-name metadata never enters the Wasm.
  Application strings, JSON property names, and names required by public
  interfaces are ordinary reachable data.
- **JSON has tiers, not one fixed runtime tax.** `JsonDocument` read-only DOM
  parsing, source-generated serialization and typed deserialization retain different
  closures. See the [scenario measurements](docs/size-and-methodology.md#json-is-not-one-fixed-cost).
- **Timezone data stays out of the Wasm.** UTC needs no timezone database;
  local-time support uses an explicitly selected deployment sidecar.
- **Debugging is a build choice.** Debug enables managed stack traces;
  Release omits their instrumentation and symbol sidecar unless requested.

The 91,294-byte figure is the uncompressed final component, not a compressed
download or the size of a complete JavaScript-host deployment. A WASI host is
still required. [The measurement notes](docs/size-and-methodology.md) make that
boundary explicit.

## Libraries should travel upward

A library written for the smaller NetWasm profile should also be useful on
desktop .NET—not trapped in a Wasm-only ecosystem.

```sh
dotnet new netwasm-lib -n MyLibrary
```

The template targets both `netwasm0.1` and `net10.0`, producing NuGet
library assets for each platform. Portability is explicit multi-targeting,
not a promise that an arbitrary desktop package will work in reverse.

The [ported libraries](https://github.com/zion-sati/NetWasm.Libraries) are
independent packages, including LINQ, HTTP,
JSON, XML, Regex, Hashing and reflection-free dependency injection.
[TUnit-NetWasm](https://github.com/zion-sati/TUnit-NetWasm) uses the same public
SDK and generic VSTest bridge for `dotnet test` integration.

## Packages

[![NuGet: NetWasm.Compiler.Browser](https://img.shields.io/badge/NuGet-NetWasm.Compiler.Browser-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Compiler.Browser)
[![NuGet: NetWasm.Compiler.Tasks](https://img.shields.io/badge/NuGet-NetWasm.Compiler.Tasks-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Compiler.Tasks)
[![NuGet: NetWasm.Hosting](https://img.shields.io/badge/NuGet-NetWasm.Hosting-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Hosting)
[![NuGet: NetWasm.Hosting.Build](https://img.shields.io/badge/NuGet-NetWasm.Hosting.Build-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Hosting.Build)
[![NuGet: NetWasm.Ref](https://img.shields.io/badge/NuGet-NetWasm.Ref-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Ref)
[![NuGet: NetWasm.Runtime.Pack](https://img.shields.io/badge/NuGet-NetWasm.Runtime.Pack-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Runtime.Pack)
[![NuGet: NetWasm.Runtime.Wasm32](https://img.shields.io/badge/NuGet-NetWasm.Runtime.Wasm32-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Runtime.Wasm32)
[![NuGet: NetWasm.Runtime.Wasm64](https://img.shields.io/badge/NuGet-NetWasm.Runtime.Wasm64-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Runtime.Wasm64)
[![NuGet: NetWasm.Sdk](https://img.shields.io/badge/NuGet-NetWasm.Sdk-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Sdk)
[![NuGet: NetWasm.Templates](https://img.shields.io/badge/NuGet-NetWasm.Templates-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Templates)
[![NuGet: NetWasm.Testing.VSTest](https://img.shields.io/badge/NuGet-NetWasm.Testing.VSTest-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Testing.VSTest)
[![NuGet: NetWasm.Toolchain](https://img.shields.io/badge/NuGet-NetWasm.Toolchain-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Toolchain)
[![NuGet: NetWasm.Wit.Bindings.Tool](https://img.shields.io/badge/NuGet-NetWasm.Wit.Bindings.Tool-004880?logo=nuget)](https://www.nuget.org/packages/NetWasm.Wit.Bindings.Tool)

[Ported-library packages](https://github.com/zion-sati/NetWasm.Libraries#packages) ·
[TUnit packages](https://github.com/zion-sati/TUnit-NetWasm#netwasm-packages)

## The direction

Reflection-free .NET. WASI as the new POSIX.

The aim is a portable C# ecosystem built around ahead-of-time compilation,
explicit interfaces and source generation rather than runtime discovery.
WASI supplies a language-neutral platform boundary; C# should participate
without carrying a desktop runtime everywhere it goes.

That is the direction, not a claim that today's profile implements all of
.NET or all of WASI.

## Current boundary

NetWasm is experimental and intended for evaluation and early integration.
Source and ABI compatibility can change; this is not a production-support
commitment.

Development hosts: macOS ARM64, Linux ARM64/x64 and Windows ARM64/x64. The normal
portable output is a wasm32 WASI Preview 2 Component. Raw wasm32/wasm64 and
JavaScript-host integration are advanced alternatives; wasm64 Components
remain blocked by upstream tooling.

Runtime reflection, `dynamic`, runtime assembly loading and managed threading
are outside the profile. General `System.IO.File`/`Directory`/`FileStream`,
raw sockets and subprocess APIs are not implemented. Existing NuGet packages
work only when their reachable code and API dependencies fit the supported
profile—not merely because they target .NET.

Read the [support inventory](docs/support-status.md) before porting an app.
The [documentation index](docs/index.md) covers hosting, diagnostics, runtime
contracts and deployment.

## License

CoreLib, runtime libraries, templates and generated support code are MIT,
subject to preserved upstream notices. The compiler and developer tooling use
the [NetWasm Community License 1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt),
not MIT.

The Community License is free for qualifying individuals, education,
open-source work, evaluation, and organizations with fewer than 250 employees
and less than USD 10,000,000 in annual revenue. Matching
[GitHub Sponsors tiers](https://github.com/sponsors/zion-sati) grant limited
internal commercial use; OEM and Commercial Offering use require a separate
agreement. Generated applications may use their authors' chosen licenses.
There is no sign-in, activation, telemetry or technical enforcement.

See the [licensing guide](docs/licensing.md) for the exact boundaries.
Commercial licensing: <zionsatidev@gmail.com>.

## Bring a workload

Start with `Console.WriteLine(42)`, then try the smallest real workload where
deployment size, sandboxing, or portability matters. If something is missing,
open an [issue](https://github.com/zion-sati/NetWasm/issues)—the platform is early
enough to be shaped by concrete use cases.

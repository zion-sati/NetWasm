# C# at native Wasm size.

Write C#. Deploy like C++. Target WASI.

A clean Release build of `Console.WriteLine(42)` produces an **88,344-byte
final Wasm artifact—runtime and precise garbage collection included.**

NetWasm compiles C# into a standalone WebAssembly Component, without carrying
the desktop .NET runtime. Keep the language, generics, exceptions and managed
memory. Deploy the compiled program to a compatible WASI host.

[Measured size and reproduction steps](docs/size-and-methodology.md) ·
[Quickstart](docs/sdk-quickstart.md) ·
[Supported APIs and limitations](docs/support-status.md)

## Try it

Install [.NET SDK 10.0.300 or newer](https://dotnet.microsoft.com/download/dotnet/10.0),
Git, Python 3 and Emscripten SDK 6.0.7 using the
[prerequisite instructions](docs/sdk-quickstart.md#1-install-the-prerequisites).
Activated Emscripten supplies Node.js 24+ and LLD 24+; you do not need separate
LLVM, Node.js or Wasm-tool installations.

Then install, create, run and publish with ordinary `dotnet` commands:

```sh
dotnet new install "NetWasm.Templates@*-*"
dotnet new netwasm-app -n HelloNetWasm
cd HelloNetWasm
dotnet run
dotnet publish -c Release -o publish/local
```

The app prints `42`. Packages come from NuGet.org; no source checkout or
special package feed is required. `@*-*` includes experimental releases; the
generated project pins its SDK version. Its entry point is ordinary C# `Main`.

For Windows setup, browser publishing and dual-target libraries, follow the
[complete SDK quickstart](docs/sdk-quickstart.md).

## .NET designed around Wasm

C# does not require the desktop .NET runtime's entire compatibility surface.
NetWasm starts with a different boundary: a smaller CoreLib, closed-world
compilation, precise garbage collection and explicit WASI interfaces.

Roslyn produces CIL. NetWasm compiles the reachable program directly to Wasm,
specializes its generics, and links only the runtime support it needs. It does
not ship a CLR, Mono or `dotnet.js` alongside your application assemblies.

This is a new .NET platform, not an attempt to make every desktop assumption
work inside a Wasm sandbox. That tradeoff is what makes the small artifact
possible.

## Pay for what you use

- **Reachable code and imports only.** An unused package reference is not a
  reason to retain its implementation or request its host capabilities.
- **No reflection type-name catalogue.** Type identity and dispatch do not
  require a runtime-discoverable collection of type names. Explicit application
  strings and names required by public interfaces are a separate matter.
- **JSON has tiers, not one fixed runtime tax.** `JsonDocument` parsing,
  source-generated serialization and typed deserialization retain different
  closures. See the [scenario measurements](docs/size-and-methodology.md#json-is-not-one-fixed-cost).
- **Timezone data stays out of the Wasm.** UTC needs no timezone database;
  local-time support uses an explicitly selected deployment sidecar.
- **Debugging is a build choice.** Debug enables managed stack traces;
  Release omits their instrumentation and symbol sidecar unless requested.

The 88,344-byte figure is the uncompressed final component, not a compressed
download or the size of a complete JavaScript-host deployment. A WASI host is
still required. [The measurement notes](docs/size-and-methodology.md) make that
boundary explicit.

## Libraries should travel upward

A library written for the smaller NetWasm profile should also be useful on
desktop .NET—not trapped in a Wasm-only ecosystem.

```sh
dotnet new netwasm-lib -n MyLibrary
```

The template targets both `netwasm0.1` and `net10.0`, producing ordinary NuGet
library assets for each platform. Portability is explicit multi-targeting,
not a promise that an arbitrary desktop package will work in reverse.

The [ported libraries](https://github.com/zion-sati/NetWasm.Libraries) are
independent packages and ordinary NetWasm consumers, including LINQ, HTTP,
JSON, XML, Regex, Hashing and reflection-free dependency injection.
[TUnit-NetWasm](https://github.com/zion-sati/TUnit-NetWasm) uses the same public
SDK and generic VSTest bridge for ordinary `dotnet test` integration.

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

Development hosts: macOS ARM64, Linux ARM64/x64 and Windows x64. The normal
portable output is a wasm32 WASI Preview 2 Component. Raw wasm32/wasm64 and
JavaScript-host integration are advanced alternatives; wasm64 Components
remain blocked by upstream tooling. Windows ARM64 is unsupported.

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

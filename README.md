# NetWasm

NetWasm is an experimental closed-world .NET-to-WebAssembly toolchain. It
compiles Roslyn-produced CIL directly to WebAssembly, links reachable code with
the NetWasm runtime, and packages the current public flow as a WebAssembly
Component using WIT and WASI Preview 2. Raw core WebAssembly with an explicit
JavaScript host boundary remains an advanced alternative for host-specific
integration.

It is a smaller .NET platform for AOT-style programs, not a browser packaging
of the desktop .NET runtime. Applications compile against `NetWasm.CoreLib`.
A desktop-targeted package is compatible only where its CIL and API use fit the
current NetWasm profile.

## Status

The preview SDK, templates, runtime, hosting, and testing packages use the
normal NuGet.org workflow. The project remains experimental and has no
production-support or compatibility commitment; expect source-level and ABI
changes.

## Build from source

The pinned toolchain versions are in `global.json` and `eng/toolchain.json`.
From a clone on a supported development machine, install and activate
Emscripten 6.0.7 as shown in the [quickstart](QUICKSTART.md); it supplies the
supported Node.js 24+ and LLD 24+ tools. Then run:

```sh
dotnet restore NetWasm.slnx
npm ci
eng/verify-toolchain.sh
dotnet build NetWasm.slnx -c Release --no-restore
```

To build the complete 12-package core distribution from that clean checkout,
run `eng/build-packages.sh`. It materializes the audited platform-neutral
Toolchain assets from the checked-in locks and pinned upstream archive, then
writes packages under `artifacts/packages`. It does not publish them.

The package-consumer flow is documented in [QUICKSTART.md](QUICKSTART.md).
The [support status and roadmap](docs/support-status.md) separates the current
supported profile from external blockers, deferred work, and intentional
boundaries. The [documentation index](docs/index.md) links the diagnostic,
target/output, CoreLib/runtime, and CLI/build/deployment references.

## How it is structured

```text
Roslyn-produced CIL
        |
        v
NetWasm compiler and whole-program analysis
        |
        +--> core Wasm + generated runtime layout
        |             |
        |             v
        |     Component packaging + WIT/WASI Preview 2
        |             |
        |             v
        |      portable component host boundary
        |
        `--> raw core Wasm + interop manifest + JavaScript host adapter
```

The source layout is described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
Read [docs/LIMITATIONS.md](docs/LIMITATIONS.md) before porting an application.
The [CoreLib/runtime reference](docs/corelib-runtime.md) describes the
compatibility boundary and intentionally unsupported surfaces, including the
absence of general managed file/directory APIs, raw sockets, subprocesses and
managed threading in the first preview.

## Documentation

- [Manual quickstart](QUICKSTART.md)
- [Documentation index](docs/index.md)
- [Support status and roadmap](docs/support-status.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Current limitations](docs/LIMITATIONS.md)
- [Compiler diagnostics](docs/diagnostics.md)
- [Targets and output formats](docs/targets-and-outputs.md)
- [CoreLib and runtime compatibility](docs/corelib-runtime.md)
- [CLI, build, and deployment](docs/cli-build-deployment.md)
- [Known gaps and boundaries](docs/gaps.md)
- [Licensing boundary](docs/licensing.md)
- [Documentation maintenance](docs/maintenance.md)
- [Contributing](CONTRIBUTING.md)
- [Security](SECURITY.md)
- [Support](SUPPORT.md)
- [License policy](LICENSE.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

The compiler, linker, optimizer, build tools, debugger, IDE/browser tools, and
other developer tooling are covered by the [NetWasm Community License
1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt). `NetWasm.CoreLib`, runtime
libraries, templates, and generated support code are
MIT. Ported framework libraries live in their separate public repository. Some
vendored inputs retain their own upstream terms. See
[LICENSE.md](LICENSE.md), [LICENSE-MAP.md](LICENSE-MAP.md), and
[third-party notices](THIRD-PARTY-NOTICES.md).

The Community License is free for qualifying individuals, education,
open-source work, contributions, evaluation, and organizations with fewer
than 250 employees and less than USD 10,000,000 in annual revenue. An active
matching GitHub Sponsors tier at
<https://github.com/sponsors/zion-sati> grants limited internal commercial use
for its Developer cap. No sign-in, activation, telemetry, or technical
enforcement is required.

Bundling, embedding, redistribution, resale, sublicensing, hosted/cloud/API
access, and other OEM or Commercial Offering use require a separate written
agreement. Generated applications may be licensed by their authors under
terms of their choice. Commercial licensing contact: <zionsatidev@gmail.com>.

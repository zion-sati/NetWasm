# Troubleshooting

Problems people run into first, by what you see. For a compiler diagnostic
code (`NW` followed by four digits), see [compiler diagnostics](diagnostics.md).

## The build stops

**`NWSDK086`: the .NET SDK is too old.** NetWasm needs SDK 10.0.303 or newer.
Linux distributions package only the 10.0.1xx SDK; install Microsoft's
instead, as [install .NET](sdk-quickstart.md#1-install-net) describes.

**`NWSDK041` on Linux.** The development tools need glibc. Musl-based
distributions such as Alpine are not supported build hosts.

**`NWSDK105`: `netwasm0.1` is deprecated.** Change the project's
`TargetFramework` to `net10.0-netwasm`. Packages built only for `netwasm0.1`
need a `net10.0-netwasm` or `net8.0` and later build to be used from it.

**`NW1009` or `NW1010` in a project with `[JSImport]`.** The program imports a
JavaScript module that the selected output cannot carry. An `Exe` that imports
your own JavaScript modules needs `NetWasmRawWasm=true`; see
[choose the output kind](sdk-quickstart.md#choose-the-output-kind).

**`NWSDK018` from `dotnet run`.** A library cannot be run. Run the application
that references it.

**`NWSDK052` from `dotnet run`.** The project publishes no `index.html`, as a
worker project whose page is in another project does. Run the project with the
page.

## The program fails at run time

**`PlatformNotSupportedException`.** The API is outside what NetWasm
provides, such as threads and subprocesses. [Support status](support-status.md)
lists what is available, and
[spec deviations](spec-deviations.md) lists deliberate differences from
desktop .NET.

**`MissingMethodException`, `MissingFieldException` or `TypeLoadException`,
with warning `NW1016` at build time.** A library calls a member or type
NetWasm's framework does not have, often because of a version mismatch. The
method that names it throws when called, as desktop .NET does, and the rest of
the library works. Set
`NetWasmMissingApiAsError=true` to make the warning an error. See
[.NET Standard libraries](netstandard-libraries.md) for what to do about it.

**Error messages read like resource keys** instead of sentences. NetWasm sets
`UseSystemResourceKeys` to `true`, as Blazor WebAssembly does, to keep messages out of the
download. Set it to `false` and libraries report their full messages.

**A type's name reads `type#` and a number.** The project sets
`NetWasmTypeNames=false`, which leaves names out. Remove it to get names back;
a program that never reads a name pays nothing for them either way.

**Local time is UTC.** NetWasm never takes the time zone from the host: set
the `TZ` environment variable and ship the zone's data, as
[time zone assets](timezone-assets.md) describes.

**A file or directory is not found.** A program sees only the directories its
host mounts. `dotnet run` mounts the working directory and a temporary
directory; a published run mounts nothing unless you pass `--dir`. See
[host capabilities](sdk-quickstart.md#host-capabilities).

**"cannot block a synchronous task before returning".** A component's
synchronous export waited for something, such as a file, a network call or a
task, which the Component Model does not allow. Declare the WIT function
`async` so its C# implementation returns a `Task` and can wait.

**HTTPS or `SslStream` fails in a host other than NetWasm's own.** TLS needs a
host that provides `wasi:tls`, as NetWasm's launcher does and Wasmtime does
with `-Stls`. For a host without it, set `NetWasmTlsSupport=false`, and
`SslStream` throws `PlatformNotSupportedException` instead.

## In the browser

**The page stops responding while C# runs.** A synchronous `[JSExport]` method
runs on the page's thread. Return `Task` from an export that takes a while, or
run the module in a [Web Worker](web-workers.md) with `--host worker`.

**A `[JSImport]` module is missing in the browser.** Each JavaScript module the
C# code imports needs a `NetWasmJavaScriptModule` item that names its file; see
[choose the output kind](sdk-quickstart.md#choose-the-output-kind).

## Size and speed

**The `.wasm` file is larger than expected.** Publish in Release, which
optimizes for size at `Oz`. Debug builds keep stack traces and skip
optimization. Only code a program can reach is compiled in, so one call into
a large API can bring much of it along; [size measurements](size-and-methodology.md)
shows what to expect and how to measure.

**The program is slower than expected.** Publish with
`-p:NetWasmOptimization=O3`, and see the [optimization reference](optimization.md)
and [choosing a garbage collector](runtime-garbage-collection.md).

## Reporting a problem

If a program behaves differently on NetWasm than on desktop .NET and none of
the above explains it, please [open an issue](https://github.com/zion-sati/NetWasm/issues)
with the smallest program that shows it, the NetWasm version, and the
`NetWasmOptimization` level.

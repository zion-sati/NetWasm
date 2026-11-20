# Files, network and other host capabilities

A NetWasm program reaches the outside world only through what its host grants.
`File`, `Socket`, `HttpClient`, `Environment` and the clocks are ordinary .NET
APIs, implemented over WASI 0.3. The host decides which directories the
program sees and whether it has a network. This page shows what each host
grants by default and how to grant more.

A program imports only the capabilities its reachable code uses. A program that
never opens a socket has no socket import, so a host does not have to supply
one.

## What each host grants

| Capability | `dotnet run` and `dotnet test` | `node netwasm.host.mjs` (a published program) | `wasmtime run` | Browser page or worker |
| --- | --- | --- | --- | --- |
| Arguments, environment, standard streams, exit code | Yes | Yes; the environment is inherited | Yes, with `--env` for variables | `open()` and `run()` options |
| Clocks and random numbers | Yes | Yes | Yes | Yes |
| Files | The working directory and a private `/tmp` | Only directories named with `--dir` | Only directories named with `--dir` | An in-memory file system with `/tmp`, plus what the page mounts |
| `HttpClient` | Yes | Yes | With `-Shttp -Sinherit-network` | Yes, through `fetch` |
| Sockets and DNS | Yes | Yes | With `-Sinherit-network -Sallow-ip-name-lookup` | No |
| TLS (`SslStream`, `wss://`) | Yes | Yes | With `-Stls` | No |

## Files

File access is capability-based. `File`, `FileStream`, `Directory` and the rest
of `System.IO` work in the directories the host mounts into the program's
sandbox (WASI calls them preopens), and nothing else on the machine is
reachable.

`dotnet run` and `dotnet test` mount two directories, so code behaves as it does
on desktop .NET:

- the working directory (the project directory unless `RunWorkingDirectory`
  says otherwise), read-write, at its own absolute path; it is also the
  current directory, so relative paths resolve as usual;
- a private temporary directory at `/tmp`, created for the run and deleted
  after it, where `Path.GetTempPath()` and `Path.GetTempFileName()` point.

Set `NetWasmGrantWorkingDirectory` or `NetWasmGrantTemporaryDirectory` to
`false` to leave either out. Mount another directory with a `NetWasmPreopen`
item:

```xml
<ItemGroup>
  <NetWasmPreopen Include="$(MSBuildProjectDirectory)/data"
                  GuestPath="/data"
                  Access="readOnly" />
</ItemGroup>
```

`Include` is the directory on the machine, `GuestPath` the absolute path the
program sees, and `Access` is `readOnly` or `readWrite`. Guest paths use forward
slashes and must be unique in the project.

A published program mounts nothing unless you name it. The launcher takes
Wasmtime's `--dir HOST[::GUEST]` form, before the program's arguments:

```bash
node publish/local/netwasm.host.mjs --dir . --dir ./data::/data first second
wasmtime run --dir . --dir ./data::/data publish/local/Hello.wasm first second
```

A directory without a guest path appears at its own absolute path.

In a browser, a program that uses files gets an in-memory file system with an
empty `/tmp`. The page module exports `mount`, `unmount` and `sync`, which put a
file or directory the user picked at a path the program sees; see
[read and write files the user picked](javascript-interop.md#read-and-write-files-the-user-picked).

## Network

`HttpClient` works in every host. In Node.js the program carries the HTTP client
it needs, so nothing else is installed. In a browser each request goes through
`fetch`, so the browser's CORS rules decide which responses the program can
read, and `SocketsHttpHandler` reports `IsSupported` as false.

Sockets (`Socket`, `TcpClient`, `TcpListener`, `UdpClient`), `Dns` and TLS
(`SslStream`, `wss://` WebSockets, `SmtpClient` with SSL) need a host with
`wasi:sockets` and `wasi:tls`: NetWasm's Node.js launcher, or Wasmtime with the
flags in the table. A browser has neither. Wasmtime's `wasi:tls` trusts only its
bundled public root certificates.

A program that can reach TLS imports `wasi:tls`, even when a run uses none. For
a host without it, set `NetWasmTlsSupport` to `false`: the program then imports
no `wasi:tls`, and `SslStream` throws `PlatformNotSupportedException`.

The [support status](support-status.md#wasi-backed-net-surface) lists what each
area supports, including what WASI 0.3 itself leaves out, such as TLS server
mode and HTTP/3.

## Environment variables

A published program inherits the environment of the process that starts it.
Variables the program should always see go in the project:

```xml
<ItemGroup>
  <NetWasmEnvironmentVariable Include="TZ" Value="Australia/Melbourne" />
</ItemGroup>
```

A page's `open()` takes an `environment` option that adds to or replaces them.

## What no host provides

There are no processes (`System.Diagnostics.Process`), no managed threads or
thread pool, and no ambient machine or user information. `Task`, `ValueTask`,
timers and cancellation run on one thread, and synchronous I/O calls work by
waiting on WASI's asynchronous operations. See
[support status](support-status.md) for the full list.

## A host of your own

Every system service goes through the program's platform, `WasiPlatform` by
default in a component. A component imports exactly the WASI interfaces its
reachable code uses, and a host must supply each of them: one that lacks an
import refuses to instantiate the component and names the interface, as
Wasmtime does for `wasi:http` without `-Shttp`. Components run in any host that
implements the WASI 0.3 interfaces they import, such as Wasmtime, Spin and
wasmCloud.

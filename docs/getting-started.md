# Getting started

NetWasm compiles a C# project to WebAssembly with the ordinary `dotnet`
commands. This page takes a console program from nothing to a published
deployment, then shows the same for a web page. The
[SDK quickstart](sdk-quickstart.md) covers each step in more depth.

## Prerequisites

The [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0), 10.0.303 or
newer (.NET 11 works too), on Linux x64 or ARM64, macOS ARM64, or Windows x64
or ARM64. Nothing else: the first `dotnet restore` downloads the compiler,
Node, the linker and Binaryen as a NuGet package for your machine. See
[install .NET](sdk-quickstart.md#1-install-net) for distributions whose own
SDK package is too old.

Install the project templates once:

```bash
dotnet new install NetWasm.Templates
```

## A console program

```bash
mkdir Hello
cd Hello
dotnet new netwasm-app
dotnet run
```

The template prints `42`. Edit `Program.cs` as you would for desktop .NET and
run again; a Debug build recompiles only the methods you changed.

The project targets `net10.0-netwasm`, so NuGet packages restore as they would
for `net10.0`. Most of the base class library works, with files, sockets and
`HttpClient` going through what the host grants; there are no threads and no
subprocesses. See [support status](support-status.md) for the full picture.

## Publish it

```bash
dotnet publish -c Release -o publish/local
node publish/local/netwasm.host.mjs
```

`publish/local` holds the program's `.wasm` file and a launcher. Copy it
anywhere with Node.js and run it, or run the `.wasm` file in any WASI 0.3 host,
such as Wasmtime:

```bash
wasmtime run publish/local/Hello.wasm
```

A published run can reach only the directories you mount:
`node publish/local/netwasm.host.mjs --dir ./data::/data` gives the program the
`data` folder at `/data`. See [publishing and deployment](deployment.md).

Release builds optimize for size at `Oz`. For speed, publish with
`-p:NetWasmOptimization=O3`; see the [optimization reference](optimization.md).

## A web page

A page calls C# methods through a JavaScript module the build generates:

```bash
dotnet new netwasm-app -n Greeter --host page
cd Greeter
dotnet run
```

`dotnet run` serves the page on `http://localhost:5080/`. The C# side marks
the methods JavaScript may call with `[JSExport]`, and declares the JavaScript
functions it calls with `[JSImport]`:

```csharp
using System.Runtime.InteropServices.JavaScript;

public static partial class PageExports
{
    [JSImport("show", "app.host")]
    private static extern void Show(string message);

    [JSExport("greet")]
    public static string Greet(string name)
    {
        var message = $"Hello, {name}, from C#.";
        Show(message);
        return message;
    }
}
```

The page opens the module and calls the export; `app.host.mjs` implements
`show`:

```js
import { open } from "./browser/Greeter.mjs";

const app = await open();
app.greet("browser");
await app.close();
```

`dotnet publish -c Release` writes the site to
`bin/Release/net10.0-netwasm/publish`, ready for any static web server.
[JavaScript interop](javascript-interop.md) explains exports, imports,
callbacks and the types that cross, [DOM and Canvas](dom-and-canvas.md) shows
how C# reads and draws on the page, and `--host worker` runs the module in a
[Web Worker](web-workers.md) instead.

## Next steps

- [What a project can build](project-kinds.md):
  programs, JavaScript modules, Web Workers, HTTP services and WIT components.
- [Configuration reference](configuration.md): every setting and its default.
- [Packages and libraries](netstandard-libraries.md): which NuGet packages work
  and what to do when one does not.
- [Troubleshooting](troubleshooting.md): the problems people run into first.

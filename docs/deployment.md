# Publishing and deployment

`dotnet publish` writes everything a deployment needs to one directory. This
page shows what to publish for each host, how to serve a browser deployment,
and how to keep it small.

## Publish

| Deployment | Command | Deploy |
| --- | --- | --- |
| A program, for Node.js | `dotnet publish -c Release -o publish/local` | The whole `publish/local` directory; run `node netwasm.host.mjs` |
| A program, for a WASI 0.3 host such as Wasmtime | The same | `publish/local/<AssemblyName>.wasm` alone |
| A component, such as an [HTTP service](http-services.md) | `dotnet publish -c Release -o publish` | `publish/<AssemblyName>.wasm` alone |
| A web page or worker | `dotnet publish -c Release -o publish` | The whole publish directory, as a static site |
| A program with `Main` in a web page | `dotnet publish -c Release -p:NetWasmPublishTarget=browser -o publish` | The whole publish directory |

A `JsModule` or `WitWebWorker` project publishes for the browser without the
extra property. Release builds optimize at `Oz` and minify the JavaScript host;
the host contains only the code its deployment uses.

## Node.js

The local deployment holds the program's `.wasm` file, the `netwasm.host.mjs`
launcher and the JavaScript the launcher needs. A program that uses
`HttpClient` carries the Node.js HTTP client under `node_modules`, so nothing is
installed on the target machine. Copy the directory anywhere with Node.js 24.15
or later and run it:

```bash
node publish/local/netwasm.host.mjs --dir ./data::/data first second
```

The launcher passes the arguments to `Main`, inherits the environment, grants
the network, clocks and random numbers, mounts only the directories named with
`--dir`, and exits with the program's exit code. See
[host capabilities](host-capabilities.md).

A `JsModule` for the `node` host publishes `<AssemblyName>.node.mjs`, which a
Node.js server or serverless function imports like any other module; see
[JavaScript interop](javascript-interop.md#choose-a-host).

## A browser deployment

The publish directory is a static site:

| File | What it is |
| --- | --- |
| `index.html` and your other content | Your page, from the project's content items |
| `browser/<AssemblyName>.mjs` and `.d.mts` | The module your page imports (`open()` or `run()`), and its TypeScript declarations |
| `browser/<AssemblyName>.wasm` | The program |
| `browser/<AssemblyName>.wasm.type-symbols`, `.wasm.method-symbols` | Type names and stack-trace names, when the program uses them ([program sidecars](diagnostics.md#program-sidecars)) |
| `browser/deployment.json`, `interop.json`, `runtime-layout.json` | The deployment manifest and what it describes |
| `browser/netwasm.*.mjs`, `browser/<AssemblyName>.raw-adapter.mjs` | NetWasm's JavaScript host, split into chunks, and the program's adapter to it |
| `browser/workers/<Name>/` | Each worker's own files |

Deploy the whole directory. Some host chunks load only when a page needs them,
such as the JavaScript SHA-256 used on a page served over plain HTTP, so a
directory missing one works until that moment.

During development, `dotnet run` serves the published site on localhost; see
[run a browser project](sdk-quickstart.md#run-a-browser-project).

## Serve the published files

Copy the publish directory to any static host. It needs no server code and no
`Cross-Origin-Opener-Policy` or `Cross-Origin-Embedder-Policy` headers, and it
works over plain HTTP as well as HTTPS, for example a development build opened
from a phone on the local network. Startup checks each file it loads against its
SHA-256 digest, with `crypto.subtle` in a secure context and in JavaScript
elsewhere. Opening `index.html` from a `file://` URL does not work: browsers
block module scripts and `fetch` there.

The server must send `.mjs` files with a JavaScript MIME type such as
`text/javascript`, because browsers refuse to run a module script served as
anything else. Most static servers already do. Python's `http.server` on
Windows sends `text/plain` when the Windows registry lists `.mjs` as
`text/plain`, since registry entries override Python's own mapping. Set the
type when starting it from the publish directory (`py` on Windows):

```sh
python -c "import http.server, mimetypes; mimetypes.add_type('text/javascript', '.mjs'); http.server.test(http.server.SimpleHTTPRequestHandler, port=8000)"
```

or change the `Content Type` value under `HKEY_CLASSES_ROOT\.mjs` to
`text/javascript`.

Under a Content Security Policy, see [security](security.md).

## Caching

The `netwasm.runtime.*.mjs` files are named after their content, so a server can
let browsers cache them for as long as it likes. The other files keep their
names from one build to the next. Serve them so browsers revalidate (for
example `Cache-Control: no-cache` with an `ETag`), because startup checks each
file against the digests of the deployment it belongs to: a page that loads a
new `deployment.json` and an old cached `.wasm` file stops with an integrity
error instead of running mismatched code.

## Compression

WebAssembly and JavaScript compress well. The SDK writes no precompressed
files, so turn on Brotli or gzip in the server or CDN for `.wasm`, `.mjs`,
`.json` and the sidecar files. A small command-line program's component is
less than half its size after Brotli; see
[size measurements](size-and-methodology.md#collector-selection-and-compression).

## Keep it small

A Release build already leaves out what a program does not reach: unreachable
code, unused imports and every runtime feature it does not use. The remaining
choices trade size for speed or features:

| Choice | Effect |
| --- | --- |
| `NetWasmOptimization` | `Oz`, the Release default, is the smallest; `O3` is faster and larger. See [optimization](optimization.md). |
| `NetWasmGarbageCollector` | `Compact`, the default, is the smallest; `NetWasm` and `NetWasmGen` are faster and larger. See [garbage collectors](runtime-garbage-collection.md). |
| `NetWasmTypeNames=false` | Leaves out the type-names sidecar, and the code that loads it, from a program that reads type names; names then read `type#<id>`. |
| `NetWasmManagedStackTrace` | Off in Release: no stack-trace instrumentation and no method-symbols sidecar. |
| `UseSystemResourceKeys` | True by default: libraries report resource keys instead of carrying their full messages. |
| Time zone data | Only shipped when you add it; see [time zones](timezone-assets.md). |
| `NetWasmTlsSupport=false` | Leaves the `wasi:tls` import out of a program that can reach TLS but never uses it. |

[Size measurements](size-and-methodology.md) shows how small programs are and
how to reproduce the numbers, and how JSON and other features add to a
program's size.

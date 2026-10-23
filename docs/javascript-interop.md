# JavaScript interop

NetWasm provides a closed-world JavaScript boundary through
`System.Runtime.InteropServices.JavaScript`. Use `[JSImport]` for managed-to-
JavaScript calls and `[JSExport]` for JavaScript-to-managed calls. The compiler
generates the reachable adapters and an exact interop manifest; there is no
reflection-based discovery or global module lookup.

The quickest complete example is the JavaScript module template:

```sh
dotnet new install NetWasm.Templates
dotnet new netwasm-app -n Greeter --host page
cd Greeter
dotnet publish -c Release
```

Serve `bin/Release/netwasm0.1/publish` over HTTP and open `index.html`. The
template contains a managed export, a managed import, its JavaScript module that
writes to the page, and a page script that opens the module, calls the export,
and closes it. Use `--host worker` to run the same kind of module in a Web Worker
behind a generated Promise-based client; see [C# Web Workers](web-workers.md)
for the worker contract.

## Call C# from a page

A `JsModule` project publishes C# exports as a JavaScript module instead of a
program with `Main`:

```xml
<PropertyGroup>
  <OutputType>JsModule</OutputType>
  <NetWasmJavaScriptHosts>page</NetWasmJavaScriptHosts>
</PropertyGroup>
```

Publish writes `browser/<AssemblyName>.mjs` and its `.d.mts` declarations,
which type every export and name its parameters after the C# parameters. A
string, array or `JSObject` is typed with `| null` unless C# declares it
non-nullable under `<Nullable>enable</Nullable>`. A page opens the module, calls
its exports and closes it:

```js
import { open } from "./browser/Calculator.mjs";

const app = await open({ stdout: line => console.log(line) });
app.Add(2, 3);                  // 5, synchronously on the page
await app.FetchTitle(url);      // a Task<string> export returns a Promise
app.failure.then(error => console.error(error));
await app.close();
```

`open()` returns a frozen object with one method per export, plus `close()` and
`failure`. Each call to `open()` starts an independent instance. `failure`
settles when the instance traps or fails, or after `close()`; calls after
either throw `NetWasmHostError`. `close()` releases every handle the instance
holds.

Every option is optional:

| Option | Effect |
| --- | --- |
| `stdout`, `stderr` | Receive guest output one line at a time. `stderr` also receives a report of each managed exception that leaves an export. Default: the console. |
| `environment` | Adds or replaces environment variables over the project's `NetWasmEnvironmentVariable` items. |
| `modules` | Supplies import modules at runtime, keyed by module name, beside the bundled `NetWasmJavaScriptModule` items. A module may not be both. |
| `onNotification` | Receives notifications an import module sends, after the import returns. |
| `platform` | Test hooks: `now()` returns epoch milliseconds for the wall clock, and `random(bytes)` fills random bytes. |

A synchronous export runs on the page's thread and blocks it until it returns.
Return `Task` or `ValueTask` from an export for long work, or host the module
in a worker.

### Choose a host

`NetWasmJavaScriptHosts` selects where the module runs:

| Host | Calls | Use it when |
| --- | --- | --- |
| `page` (default) | Synchronous exports return values; `Task` exports return Promises. Import modules may use the DOM. | C# drives page UI, or calls are short. |
| `worker` | Every export returns a Promise. Import modules run in the worker and reach the page through notifications. | Work is long enough to block the page. |
| `page;worker` | Publishes both. | The page needs some calls of each kind. |

The worker host publishes `browser/<AssemblyName>.worker-client.mjs`; see
[C# Web Workers](web-workers.md). A `JSObject` stays in the realm that created
it, so the worker host rejects exports that pass one (NWSDK081).

## Declare imports and exports

Every reached import must be a bodyless static method and must name its module
explicitly:

```csharp
using System.Runtime.InteropServices.JavaScript;

public static partial class WorkerExports
{
    [JSImport("report", "example.notifications")]
    private static extern void Report(int completed, int total);

    [JSExport("run")]
    public static int Run(int total)
    {
        Report(total, total);
        return total;
    }
}
```

Every export must be a static method with a managed body. Export names must be
unique; omitting the name uses the method name. NetWasm does not discover
instance methods, assemblies, or attributes at runtime.

Supply each application-owned module explicitly:

```xml
<ItemGroup>
  <NetWasmJavaScriptModule Include="notifications.mjs"
                           Module="example.notifications" />
</ItemGroup>
```

```js
export function createWorkerImports(notify) {
  return Object.freeze({
    report(completed, total) {
      notify("progress", [completed, total]);
    },
  });
}
```

For an ordinary browser application rather than a worker, reachable
`[JSImport]` declarations require `<NetWasmRawWasm>true</NetWasmRawWasm>`.
The same `NetWasmJavaScriptModule` item supplies page-owned modules; in
that boundary the module runs in the page realm and may access the DOM. Follow
the [entry-contract walkthrough](sdk-quickstart.md#choose-the-entry-contract-and-output-kind)
for the raw browser publish settings.

A page starts the published application with the generated `run()` helper.
Browser publish writes `browser/<AssemblyName>.mjs` and its `.d.mts`
declarations next to `deployment.json`. The helper pins that deployment's
build fingerprint and manifest digest, and uses the project's arguments,
environment and grants (`NetWasmArgument`, `NetWasmEnvironmentVariable`,
`NetWasmClockGrant`, `NetWasmNetworkPolicy` and `NetWasmRandomness`):

```js
import { run } from "./browser/App.mjs";

const result = await run({
  stdout: line => document.querySelector("#output").append(line, "\n"),
});
if (result.primaryFailure !== null || result.exitCode !== 0) {
  throw new Error(result.primaryFailure?.detail ?? result.primaryFailure?.message);
}
```

Every option is optional. `arguments` replaces the arguments after the program
name, as `Main` receives them. `environment` adds or replaces variables.
`stdout` and `stderr` receive output one line at a time and default to the
console. `signal` is an `AbortSignal` that cancels the run.

A command application ends when `Main` returns, and its exports and callbacks
end with it. A page that needs C# to keep answering JavaScript, for example
from event handlers, should publish `<OutputType>JsModule</OutputType>` and
call its exports instead of keeping `Main` alive.

To build the request yourself, call `executeNetWasm`. Its request must
identify the browser deployment, not the separate local deployment. For a
project that copies `index.html` and `app.mjs` to the publish directory, this
`app.mjs` builds the request from the published manifest:

```js
import { executeNetWasm } from "./browser/netwasm.browser.mjs";

const bytes = await (await fetch("./browser/deployment.json")).arrayBuffer();
const manifest = JSON.parse(new TextDecoder().decode(bytes));
const digest = await crypto.subtle.digest("SHA-256", bytes);
const sha256 = Array.from(new Uint8Array(digest),
  byte => byte.toString(16).padStart(2, "0")).join("");

const request = {
  schemaVersion: 1,
  buildFingerprint: manifest.buildFingerprint,
  deploymentManifestSha256: sha256,
  arguments: [],
  environment: [],
  grants: {
    environment: [], preopens: [], network: "denyAll",
    clocks: [], randomness: true,
  },
  applicationImports: [],
};
const sink = Object.freeze({
  write(bytes) { console.log(new TextDecoder().decode(bytes)); },
});
const result = await executeNetWasm({
  request, stdout: sink, stderr: sink, signal: null,
});
if (result.primaryFailure !== null || result.exitCode !== 0) {
  throw new Error(JSON.stringify(result));
}
```

A failed result names a stable `code` and `message` in `primaryFailure`. When
the host caught an error, the failure also carries `detail`: that error's name
and message on one line, at most 1,024 characters. For example, a deployment
that reads the wall clock fails with
`detail: "TypeError: platform capability 'wallClock' was not granted"` when the
request grants no clocks. Branch on `code` and show `detail` to people. The
local launcher replaces host paths in `detail` with `<host-path>`.

The example grants randomness, which the NetWasm runtime uses to seed string
hashing, and no network, clocks, environment variables or filesystem mounts.
The deployment's `requiredImportModules` lists every platform capability it
can use; a request that leaves one of them ungranted fails with a `detail` that
names it. Enable only the capabilities your application requires. JavaScript
modules supplied through `NetWasmJavaScriptModule` are already bundled by the
SDK; they do not need entries in `applicationImports`. Run the page over
localhost or HTTPS so `crypto.subtle` is available.

## Supported signatures

| Boundary | Supported managed signature |
| --- | --- |
| Synchronous `JSImport` | Primitive scalars, `string`, number arrays, `JSObject`, and one supported delegate parameter; primitive scalars, `string`, number arrays, `JSObject`, `JSSubscription`, or `void` result |
| Asynchronous `JSImport` | Ordinary `[JSImport]` returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`; `T` is a primitive scalar, `string`, a number array, or `JSObject` |
| Synchronous `JSExport` | Primitive scalar, `string`, number array, or `JSObject` parameters; the same result types or `void` |
| Asynchronous `JSExport` | Primitive scalar, `string`, number array, or `JSObject` parameters and a `Task`/`ValueTask` result whose value is `void` or a primitive scalar |
| Import callback | One delegate parameter and a `JSSubscription` result; callback parameters are primitive scalars plus at most one `string` or number array, and the callback result is a primitive scalar or `void` |

Primitive scalars are `bool`, signed and unsigned integer types, `char`,
`nint`/`nuint`, `float`, and `double`. Number arrays are one-dimensional
arrays of `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`float`, or `double`. `JSObject` and
`JSSubscription` own host handles and must be disposed. Disposing a
subscription also releases the managed callback root.

At the JavaScript boundary, 64-bit integers use `bigint`; 32-bit and smaller
integers and floating-point values use `number`. `nint`/`nuint` follow the
guest width. A `char` uses a one-code-unit JavaScript string, `bool` uses a
Boolean. Each number array uses the typed array with the same element type:

| C# | JavaScript |
| --- | --- |
| `byte[]` | `Uint8Array` |
| `sbyte[]` | `Int8Array` |
| `short[]` / `ushort[]` | `Int16Array` / `Uint16Array` |
| `int[]` / `uint[]` | `Int32Array` / `Uint32Array` |
| `long[]` / `ulong[]` | `BigInt64Array` / `BigUint64Array` |
| `float[]` / `double[]` | `Float32Array` / `Float64Array` |

A typed array of a different element type is rejected, and `null` maps to a
`null` array. Strings and arrays are copied through the boundary in both
directions, including the value a Promise settles with; arbitrary managed
references are not passed to JavaScript. A `JSObject` that an imported Promise
settles with is owned by the managed task result and must be disposed like any
other `JSObject`.

An export that takes a `JSObject` receives its own handle to the caller's
object; dispose it when the export no longer needs it. An export that returns
a `JSObject` gives the caller the original JavaScript object and keeps its
managed handle. A JavaScript object cannot be posted between a page and a
worker, so only the page host publishes exports that use `JSObject`. Selecting
the worker host for such an export fails the build with NWSDK081.

Use ordinary `[JSImport]` for JavaScript Promise results. The
`JSImportPromise` attribute is the compiler's callback-lowered form and is not
the application-facing Promise API.

## Async and failure behavior

Imported Promises complete the returned managed task on the browser event loop.
Exported `Task` and `ValueTask` methods become JavaScript Promises. A managed
fault rejects the Promise; worker clients expose the managed message through
`error.message` and, when available, structured metadata under
`error.managed`.

In the page realm, JavaScript that an import runs may call an export again
before the import returns, for example from an event the import dispatches.
Such calls can nest up to 32 levels. A deeper call throws `NetWasmHostError`,
which the managed caller observes as a `JSException`, and the session remains
usable.

Worker export calls are serialized by the generated worker boundary.
`dispose()` drains accepted calls, while `terminate()` rejects pending calls
and closes immediately. A worker module cannot access the page DOM because it
runs in the worker realm; send a notification or return a value to page code.

## Serve the published files

The publish directory is a static site: copy it to any static host. It needs
no server code and no `Cross-Origin-Opener-Policy` or
`Cross-Origin-Embedder-Policy` headers. Serve it over HTTPS or from
`localhost`, because startup verifies files with `crypto.subtle`, which
browsers provide only in secure contexts. Opening `index.html` from a
`file://` URL does not work: browsers block module scripts and `fetch` there.

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

Under a strict Content Security Policy, allow module loading from the site,
from `blob:` URLs (startup imports each verified module from a `blob:` URL),
and WebAssembly compilation:

```text
Content-Security-Policy: script-src 'self' blob: 'wasm-unsafe-eval'
```

## Profile boundary

JavaScript interop is deliberately source-declared and pay-as-you-use. Only
reachable code and the codecs, callbacks, and module providers required by the
declared boundary are retained. Declaring an export makes that method a root.

The current boundary does not provide runtime reflection marshalling, arbitrary
object graphs, automatic POCO serialization, dynamic overload resolution,
instance export discovery, shared managed objects between realms, or browser
main-thread emulation. Use generated JSON contracts or WIT when the boundary is
better represented as data rather than the supported direct ABI.

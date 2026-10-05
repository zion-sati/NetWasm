# JavaScript interop

NetWasm provides a closed-world JavaScript boundary through
`System.Runtime.InteropServices.JavaScript`. Use `[JSImport]` for managed-to-
JavaScript calls and `[JSExport]` for JavaScript-to-managed calls. The compiler
generates the reachable adapters and an exact interop manifest; there is no
reflection-based discovery or global module lookup.

The quickest complete example is the released JSExport Web Worker template:

```sh
dotnet new install NetWasm.Templates@0.6.0
dotnet new netwasm-app -n ReportWorker --worker jsexport
cd ReportWorker
dotnet publish -c Release
```

Serve `bin/Release/netwasm0.1/publish` over HTTP and open `index.html`. The
template contains a managed export, a managed import, its JavaScript module, a
generated Promise-based page client, notifications, and orderly worker
disposal. See [C# Web Workers](web-workers.md) for the complete worker contract.

The commands and generated browser deployment above were package-only qualified
with the published 0.6.0 SDK and templates on .NET SDK 10.0.401, then executed
in Chromium. Both the JSExport and WIT worker templates completed their managed
calls and returned `Completed: 5`.

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
  <NetWasmWorkerJavaScriptModule Include="notifications.mjs"
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
The same `NetWasmWorkerJavaScriptModule` item supplies page-owned modules; in
that boundary the module runs in the page realm and may access the DOM. Follow
the [entry-contract walkthrough](sdk-quickstart.md#choose-the-entry-contract-and-output-kind)
for the raw browser publish settings.

A page starts the published application through `executeNetWasm`. Its request
must identify the browser deployment, not the separate local deployment. For a
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
    clocks: [], randomness: false,
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

The example grants no network, clocks, randomness, environment variables, or
filesystem mounts. Enable only the capabilities your application requires.
JavaScript modules supplied through `NetWasmWorkerJavaScriptModule` are already
bundled by the SDK; they do not need entries in `applicationImports`.
Run the page over localhost or HTTPS so `crypto.subtle` is available.
This startup code was executed in Chromium with a released 0.6.0 raw application
whose `Host.Report(42)` import updated the page's `#result` element.

## Supported signatures

| Boundary | Supported managed signature |
| --- | --- |
| Synchronous `JSImport` | Primitive scalars, `string`, `byte[]`, `JSObject`, and one supported delegate parameter; primitive scalars, `string`, `byte[]`, `JSObject`, `JSSubscription`, or `void` result |
| Asynchronous `JSImport` | Ordinary `[JSImport]` returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`; `T` is a primitive scalar |
| Synchronous `JSExport` | Primitive scalar, `string`, or `byte[]` parameters; the same result types or `void` |
| Asynchronous `JSExport` | Primitive scalar parameters and a `Task`/`ValueTask` result whose value is `void` or a primitive scalar |
| Import callback | One delegate parameter and a `JSSubscription` result; callback parameters are primitive scalars plus at most one `string` or `byte[]`, and the callback result is a primitive scalar or `void` |

Primitive scalars are `bool`, signed and unsigned integer types, `char`,
`nint`/`nuint`, `float`, and `double`. `JSObject` and
`JSSubscription` own host handles and must be disposed. Disposing a
subscription also releases the managed callback root.

At the JavaScript boundary, 64-bit integers use `bigint`; 32-bit and smaller
integers and floating-point values use `number`. `nint`/`nuint` follow the
guest width. A `char` uses a one-code-unit JavaScript string, `bool` uses a
Boolean, and `byte[]` uses a `Uint8Array`. Strings and byte arrays are copied
through the boundary; arbitrary managed references are not passed to JavaScript.

Use ordinary `[JSImport]` for JavaScript Promise results. The
`JSImportPromise` attribute is the compiler's callback-lowered form and is not
the application-facing Promise API.

## Async and failure behavior

Imported Promises complete the returned managed task on the browser event loop.
Exported `Task` and `ValueTask` methods become JavaScript Promises. A managed
fault rejects the Promise; worker clients expose the managed message through
`error.message` and, when available, structured metadata under
`error.managed`.

Worker export calls are serialized by the generated worker boundary.
`dispose()` drains accepted calls, while `terminate()` rejects pending calls
and closes immediately. A worker module cannot access the page DOM because it
runs in the worker realm; send a notification or return a value to page code.

## Profile boundary

JavaScript interop is deliberately source-declared and pay-as-you-use. Only
reachable code and the codecs, callbacks, and module providers required by the
declared boundary are retained. Declaring an export makes that method a root.

The current boundary does not provide runtime reflection marshalling, arbitrary
object graphs, automatic POCO serialization, dynamic overload resolution,
instance export discovery, shared managed objects between realms, or browser
main-thread emulation. Use generated JSON contracts or WIT when the boundary is
better represented as data rather than the supported direct ABI.

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
| `page;worker` | Publishes both, sharing one runtime. | The page needs some calls of each kind. |

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

An import may also be declared `static partial`, as on .NET's browser runtime,
so such declarations compile unchanged; the SDK supplies the implementing
declaration. A partial import needs an accessibility modifier, such as
`internal`, or the build fails with NWJS001:

```csharp
[JSImport("report", "example.notifications")]
internal static partial void Report(int completed, int total);
```

Every export must be a static method with a managed body. Export names must be
unique; omitting the name uses the method name. NetWasm does not discover
instance methods, assemblies, or attributes at runtime.

An export can be declared in any assembly the application references, so a
library can ship the callbacks its JavaScript host calls. All exports share the
module's namespace. A name declared twice, in the application or in any library,
fails the build with an error that names both declarations and their assemblies,
so give a library's exports distinctive names, for example with a library
prefix.

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

A page or worker includes only the declared modules its reachable imports use.
A module for an optional feature costs nothing in a build that leaves the
feature out.

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
| Synchronous `JSImport` | Primitive scalars, dates, their nullables, `string`, `object`, `Exception`, number arrays, reference arrays, spans of numbers, `JSObject`, and one supported delegate parameter; the same values except spans, `JSSubscription`, or `void` result |
| Asynchronous `JSImport` | Ordinary `[JSImport]` returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`; `T` is any synchronous result value |
| Synchronous `JSExport` | Primitive scalar, date, nullable, `string`, `object`, `Exception`, number array, reference array, or `JSObject` parameters; the same result types or `void` |
| Asynchronous `JSExport` | A synchronous export's parameters and a `Task`/`ValueTask` result whose value is `void` or a synchronous export's result type |
| Import callback | One delegate parameter and a `JSSubscription` result; the callback takes and returns what a synchronous `JSExport` does |

Primitive scalars are `bool`, signed and unsigned integer types, `char`,
`nint`/`nuint`, `float`, and `double`. Number arrays are one-dimensional
arrays of `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`float`, or `double`. Dates are `DateTime` and `DateTimeOffset`. A nullable
is `T?` of either, such as `int?` or `DateTime?`. Reference arrays are
`string[]`, `JSObject[]` and `object[]`. `JSObject` and
`JSSubscription` own host handles and must be disposed. Disposing a
subscription also releases the managed callback root.

A synchronous `JSImport` whose parameters and result are all primitive scalars
(or `void`) compiles to a direct WebAssembly call into the JavaScript function,
with nothing copied through memory. Use such imports for calls on a hot path.
Anything the function throws, including a result outside the managed type's
range, reaches the caller as a `JSException`. To call a JavaScript function
with plain WebAssembly values instead, see
[Call host functions directly](static-native-interop.md#call-host-functions-directly).

At the JavaScript boundary, 64-bit integers use `bigint`; 32-bit and smaller
integers and floating-point values use `number`. `nint`/`nuint` follow the
guest width. A `char` uses a one-code-unit JavaScript string, `bool` uses a
Boolean. A `DateTime` or `DateTimeOffset` uses a `Date`, as on .NET's
browser runtime: it crosses as whole Unix milliseconds, so ticks below a
millisecond are dropped. A `DateTime` is read as UTC whatever its `Kind`, and
one from JavaScript has `Kind` `Utc`. A `Date` outside the years 1 to 9999
throws `ArgumentOutOfRangeException`. A nullable uses the value's JavaScript
type or `null`, typed `T | null`. Where C# expects a plain scalar or date,
JavaScript's `null` or `undefined` gives its default (`0`, `false`, or
`DateTime.MinValue`), as on .NET's browser runtime. A delegate cannot bind
directly to an import that returns a date or a nullable; bind a lambda that
calls it.

An `object` is any JavaScript value, as on .NET's browser runtime. From
JavaScript, a number arrives as `double`, a `bigint` as `long`, a Boolean as
`bool`, a `Date` as `DateTime`, an `Error` as an exception, an array as
`object[]`, a `Uint8Array`, `Int32Array` or `Float64Array` as the matching
array, and any other object or function as a `JSObject`. To JavaScript, a
number, Boolean, string, `char`, date, `JSObject`, exception, number array or
`object[]` crosses as its JavaScript value; a `long` must fit a JavaScript
number exactly, within 2^53 - 1, or the conversion throws
`OverflowException`. A value of any other type crosses as an opaque object
that JavaScript can only pass back, and it arrives as the same .NET object.

`[JSMarshalAs<T>]` with .NET's `JSType` markers is optional. Without one, a
parameter or result crosses as described here. With one, the marker must name
the JavaScript type the value takes, such as `JSType.Promise<JSType.Number>`
for a `Task<int>` or `JSType.Function<JSType.Number, JSType.String>` for a
`Func<int, string>`, or the build fails naming the marker it takes. Only a
`long` has a choice: `JSType.BigInt`, the default, crosses it as a `bigint`,
and `JSType.Number` as a `number`, which must be an integer within 2^53 - 1
both ways. A `void` result takes `JSType.Void`, `JSType.Discard` or
`JSType.DiscardNoWait`.

An `Exception` is an `Error`. An exception that came from JavaScript, a
`JSException`, gives back the original `Error`; any other exception crosses as
an `Error` named `ManagedError` with the exception's message, and it arrives
back as the same exception. A JavaScript value that is not an `Error` arrives
as a `JSException` whose message is the value as a string. `string[]`,
`JSObject[]` and `object[]` are JavaScript arrays, whose elements must be
strings, objects, or any values, or `null`. Each number array
uses the typed array with the same element type:

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

### Choose a copy or a view

A number array and a span of numbers both reach JavaScript as the same typed
array, but they make a different trade, so choose per parameter:

| C# parameter | JavaScript receives | Cost | Safety |
| --- | --- | --- | --- |
| `T[]` | A copy of the array | One copy per call | Safe: JavaScript owns its copy, can keep it, and its writes never reach .NET |
| `Span<T>` / `ReadOnlySpan<T>` | A view of the span's memory | No copy | Unchecked: valid only during the call, and JavaScript can read and write the application's memory through it |

Use an array unless the copy matters, for example for large buffers on every
frame. A view gives JavaScript direct access to the application's linear
memory: if it keeps the view after the call, or writes through the view of a
`ReadOnlySpan<T>`, it can corrupt the application's data. The damage stays
inside the application; the view cannot reach the page or the browser.

### Pass a buffer without a copy

A synchronous `JSImport` can take a `Span<T>` or `ReadOnlySpan<T>` of any
number-array element type. JavaScript receives the same typed array as for
`T[]`, as a view of the span's memory, so nothing is copied:

```csharp
[JSImport("upload", "app.gl")]
private static extern void Upload(ReadOnlySpan<byte> rgba, int width, int height);
```

```js
export function upload(rgba, width, height) {
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgba);
}
```

Writes to the view of a `Span<T>` reach the managed buffer. Never write to the
view of a `ReadOnlySpan<T>`: a typed array cannot be made read-only, and the
memory may hold data that C# treats as immutable. A span over an array,
`stackalloc` memory or `NativeMemory` works the same way. The view is
valid only until the function returns, so copy it with `slice()` to keep the
data. Calling back into .NET can grow memory and detach the view, so read it
before such a call. A span whose start is not a multiple of its element size
cannot be viewed, and the call throws `JSException`. Asynchronous `JSImport`
and `JSExport` do not take spans; use a number array instead.

An export that takes a `JSObject` receives its own handle to the caller's
object; dispose it when the export no longer needs it. An export that returns
a `JSObject` gives the caller the original JavaScript object and keeps its
managed handle. A JavaScript object cannot be posted between a page and a
worker, so only the page host publishes exports that use `JSObject` or
`JSObject[]`. Selecting the worker host for such an export fails the build
with NWSDK081. An `object`, exception or `object[]` crosses into a worker
when its JavaScript value can be structured-cloned; a call whose value cannot
be fails.

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

Calls in both directions run concurrently: while an asynchronous export awaits,
page code can call it or another export again, in the page realm and in a
worker alike. `dispose()` drains accepted worker calls, while `terminate()`
rejects pending calls and closes immediately. A worker module cannot access the
page DOM because it runs in the worker realm; send a notification or return a
value to page code.

### Cancel a call

Cancellation has two halves, and each works the same way in both directions.

**The caller stops waiting.** JavaScript passes `{ signal }` as a trailing
option to any asynchronous export call, on the page or in a worker, and C#
calls `WaitAsync` on the task an asynchronous import returns. Either rejects
at once, with the signal's reason or an `OperationCanceledException`. The call
itself keeps running, and its eventual result is discarded.

```js
const controller = new AbortController();
const report = exports.buildReport(input, { signal: controller.signal });
controller.abort();       // report rejects with an AbortError
```

```csharp
[JSImport("fetchText", "app.net")]
private static extern Task<string> Fetch(string url);

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var page = await Fetch(url).WaitAsync(timeout.Token);
```

**The callee stops working.** Ask it with an ordinary call in the other
direction. Page code calls an export that cancels a `CancellationTokenSource`,
and the running export returns how far it got. Because calls run
concurrently, this works in a worker too. C# calls an import that aborts the
`AbortController` its JavaScript module holds. In the page realm an
`AbortSignal` can also be passed as a `JSObject`.

```csharp
private static CancellationTokenSource? _job;

[JSExport("run")]
public static async Task<int> Run(int steps)
{
    _job = new CancellationTokenSource();
    var done = 0;
    while (done < steps && !_job.IsCancellationRequested)
    {
        await Task.Delay(100);
        done++;
    }
    return done;
}

[JSExport("cancel")]
public static void Cancel() => _job?.Cancel();
```

The runtime adds nothing beyond this: no cancellation type crosses the
boundary, as in .NET's own browser interop.

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

The `netwasm.runtime.*.mjs` files are named after their content, so a server
can let browsers cache them for as long as it likes.

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

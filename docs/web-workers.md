# C# Web Workers

NetWasm 0.6.0 can publish C# as a browser Web Worker with a generated
JavaScript client. The worker has its own WebAssembly instance and managed heap,
so page code communicates with it asynchronously through the generated boundary.
It does not create a managed thread or share managed objects with the page.

## Create a worker

Install the templates, then choose one of the two worker boundaries:

```bash
dotnet new install NetWasm.Templates
dotnet new netwasm-app -n ReportWorker --worker wit
# or: dotnet new netwasm-app -n ReportWorker --worker jsexport
cd ReportWorker
dotnet publish -c Release
```

Serve `bin/Release/netwasm0.1/publish` with a static HTTP server and open
`index.html`. Both templates include a working page, progress notifications,
a generated worker client, and orderly disposal.

| Worker kind | Select it when | Output boundary |
| --- | --- | --- |
| `--worker wit` | The application contract should be language-neutral and explicitly described by WIT. | A WebAssembly Component with generated WIT bindings. |
| `--worker jsexport` | JavaScript should call managed methods declared with `[JSExport]`, and managed code may use application-supplied `[JSImport]` modules. | Raw WebAssembly with the NetWasm JavaScript interop ABI. |

Worker projects select their boundary with `OutputType` and do not use the
ordinary `command` or `async-command` entry contracts:

```xml
<!-- WIT worker -->
<OutputType>WitWebWorker</OutputType>

<!-- JSExport worker -->
<OutputType>JsWebWorker</OutputType>
```

## Call the generated client

Publishing creates `browser/<ProjectName>.worker-client.mjs`. Import its
`createWorker` function from page JavaScript. This example uses the JSExport
template; WIT export names are qualified as shown in the WIT section below:

```js
import { createWorker } from "./browser/ReportWorker.worker-client.mjs";

const worker = await createWorker({
  onNotification({ operation, arguments: values }) {
    console.log(operation, ...values);
  },
});

try {
  const result = await worker.run(5);
  console.log(result);
} finally {
  await worker.dispose();
}
```

`createWorker()` resolves after the worker is ready. Export calls return
Promises. `dispose()` lets accepted calls finish before closing the worker;
`terminate()` stops it immediately and rejects pending calls. New calls are
rejected after either operation.

The generated `*.worker-client.d.mts` file lists the available method names and
their TypeScript signatures. The template's `app.mjs` calls the matching
boundary.

When an exported `Task` or `ValueTask` faults, the Promise rejects with the
managed message in `error.message`. `error.managed` carries the available
`typeId`, `typeName`, `message`, and `stackTrace`. Debug builds include
source file and line information when portable PDB data is available.

## WIT workers

A WIT worker declares both the complete worker world and the application-only
export world:

```xml
<PropertyGroup>
  <OutputType>WitWebWorker</OutputType>
  <TargetFramework>netwasm0.1</TargetFramework>
  <NetWasmWitPath>$(MSBuildProjectDirectory)/wit</NetWasmWitPath>
  <NetWasmWorld>example:reports@1.0.0/worker</NetWasmWorld>
  <NetWasmApplicationWorld>example:reports@1.0.0/application</NetWasmApplicationWorld>
</PropertyGroup>
```

The SDK generates the page client from that contract. Export names retain their
WIT qualification, so bracket notation is useful when calling them:

```js
const run = worker["example:reports/work@1.0.0/run"];
const result = await run(5);
```

Use `NetWasmApplicationImport` for an application-owned JavaScript module that
implements a WIT import. The module is copied into the deployment and bundled
into the worker host:

```xml
<ItemGroup>
  <NetWasmApplicationImport Include="notifications.mjs"
                            Module="example:reports/notifications@1.0.0"
                            ArtifactPath="notifications.mjs" />
</ItemGroup>
```

## JSExport workers

A JSExport worker exposes bounded managed entry points directly to JavaScript:

```csharp
using System.Runtime.InteropServices.JavaScript;

public static partial class WorkerExports
{
    [JSExport("run")]
    public static int Run(int total) => total;
}
```

The generated client exposes that method as `worker.run(total)`. Reachable
`[JSImport]` calls must name an application module supplied by the project:

```xml
<ItemGroup>
  <NetWasmWorkerJavaScriptModule Include="notifications.mjs"
                                 Module="example.notifications" />
</ItemGroup>
```

The JavaScript module runs inside the worker. It cannot access the page DOM;
use notifications or exported calls to ask page code to update the UI.
For notifications, export `createWorkerImports(notify)` and return the named
import functions; see the [JavaScript interop example](javascript-interop.md#declare-imports-and-exports).

## Execution boundary

Each worker owns its managed heap and event loop. Managed references do not
cross the worker boundary, and the worker does not share a managed heap with the
page or another worker. Calls and notifications use the generated interop
contract. Only reachable managed code, imports, exports, and host providers are
retained in the worker deployment.

A worker project is published for a browser caller and cannot be launched with
`dotnet run`. Use the generated page client from an HTTP-served deployment.

# NetWasm.Hosting

`NetWasm.Hosting` supplies the public, width-neutral execution boundary for
validated NetWasm deployment artifacts. The package contains:

- the .NET descriptor, request, result, capability, and deployment contracts;
- the managed `INetWasmArtifactClient` for desktop tools and test adapters;
- the umbrella `tools/netwasm/hosting/index.mjs` ESM entry point;
- browser-safe `tools/netwasm/hosting/browser.mjs` and Node-local
  `tools/netwasm/hosting/local.mjs` entry points for selected bundling;
- the package-owned `tools/netwasm/hosting/launcher.mjs` local launcher;
- the local and browser execution modules; and
- the versioned JSON schemas under `tools/netwasm/hosting/schemas`.

The package does not contain a managed guest runtime, test-framework protocol,
Node executable, native tool, or the JCO/Preview 2 toolchain closure. Build-time
tool assets belong to `NetWasm.Toolchain`; the SDK supplies validated absolute
host-tool and package paths through the execution descriptor.

The standard local launcher appends command-line arguments, inherits its process
environment, and lets explicit request values override inherited values. The
SDK's normal application request supplies clocks, randomness and HTTP network
policy; whole-program imports still decide which providers are retained.
Filesystem preopens and custom-host restrictions remain explicit. Libraries and
transitive packages do not select a host, environment or capability policy.

Desktop tools construct `NetWasmArtifactClient` and pass the target path that
MSBuild supplied together with explicit invocation arguments and distinct
writable stdout/stderr streams. The client replaces only the target path's
final extension with `.netwasm.execution.json` and `.netwasm.request.json`,
validates that both channels identify the same build, and launches only the
absolute host executable and launcher recorded by that descriptor. It performs
no Node, package, SDK or test-framework discovery. The returned
`NetWasmExecutionResult` remains distinct from application stdout and stderr.

Browser publish starts from `browser.mjs`; local publish starts from
`local.mjs` or the process launcher. The SDK bundles and minifies the selected
entry graph independently, so an unused local transport cannot enter a browser
deployment and an unused browser transport cannot enter a local deployment.

The public execution call is always:

```js
const result = await executeNetWasm({ request, stdout, stderr, signal });
```

This JavaScript facade does not replace the managed entry point: the compiled
guest still enters through its selected `Main`. The request is the resolved
invocation and host-policy snapshot; deployment and package identities come
from the SDK-generated, hash-bound descriptor and manifest.

For a local process, invoke the packaged launcher with three distinct absolute,
canonical channels:

```sh
node /absolute/package/tools/netwasm/hosting/launcher.mjs \
  --descriptor /absolute/app.netwasm.execution.json \
  --request /absolute/request.json \
  --result /absolute/result.json
```

Use `--request -` to read the request JSON from stdin. Application stdout and
stderr remain the process streams; the launcher writes exactly one LF-terminated
structured result to a new result file and never overwrites an existing file.
A process exit code of zero means that structured transport completed—the guest
exit code or host failure is carried by the result. Exit code one is reserved
for failure before the result channel can complete.

The SDK adds `--replace-result` for its bounded `$(TargetDir)` result channel so
repeated `dotnet run --no-build` invocations replace only that prior result.
Direct launcher callers omit the flag and retain create-new-only behavior.

Browser consumers import `browser.mjs`. The SDK-generated bootstrap supplies
the manifest URL, Preview 2 filesystem/shim factories and browser fetch, digest,
module-compilation and module-URL mechanisms. Advanced direct composition uses
`createBrowserNetWasmBootstrap({ manifestUrl, preview2, web })`; ordinary
applications receive that composition from SDK publish output rather than
hand-writing it.

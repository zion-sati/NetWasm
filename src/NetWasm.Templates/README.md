# NetWasm.Templates

This package contains the `dotnet new` templates for a
NetWasm application, a dual-target library and an HTTP service. The templates use the
`NetWasm.Sdk` project SDK and the `netwasm0.1` profile.

Application templates do not copy reusable JavaScript host files. The SDK
generates the application-specific descriptors and bootstrap and stages the
selected `NetWasm.Hosting` modules during build and publish.

Create a JavaScript module whose `[JSExport]` methods run on the page or in a
Web Worker:

```bash
dotnet new netwasm-app --host page
dotnet new netwasm-app --host worker
```

The page variant opens the module, calls an export, and closes it; its import
module writes to the DOM. The worker variant calls the generated Promise-based
client, handles an application-defined progress message, and disposes the
worker.

Create a worker whose contract is described by WIT instead:

```bash
dotnet new netwasm-app --worker wit
```

Create an HTTP service, a WASI 0.3 component whose
`Handle(HttpRequestMessage)` answers each request, which `wasmtime serve`,
Spin and wasmCloud run as published:

```bash
dotnet new netwasm-serverless
```

The package follows NetWasm's documented support profile. The application
template restores the native host tools it needs through NuGet; the library
template does not select a host package. See the repository's
[package-consumer quickstart](../../docs/sdk-quickstart.md).

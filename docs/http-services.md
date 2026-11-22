# HTTP services

An HTTP service is a WASI 0.3 component that answers HTTP requests. You write
one method that takes an `HttpRequestMessage` and returns an
`HttpResponseMessage`; NetWasm generates the `wasi:http` handler around it. The
published `.wasm` file runs unchanged in any `wasi:http` 0.3 host, such as
Wasmtime, Spin and wasmCloud.

## Create one

```bash
dotnet new netwasm-serverless -n Greeter
cd Greeter
dotnet publish -c Release -o publish
wasmtime serve -Sp3 -Scli --addr 127.0.0.1:8080 publish/Greeter.wasm
```

Then `curl http://127.0.0.1:8080/hello`. The template's README shows the same
steps for its project.

## The project

```xml
<Project Sdk="NetWasm.Sdk/<version>">
  <PropertyGroup>
    <OutputType>Component</OutputType>
    <TargetFramework>net10.0-netwasm</TargetFramework>
    <NetWasmWorld>service</NetWasmWorld>
  </PropertyGroup>
</Project>
```

The `service` world is `wasi:cli`'s imports plus `wasi:http/service`.
`System.Net.Http` is part of NetWasm's framework, so the project needs no
package for it.

## The handler

```csharp
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Greeter;

public static partial class ServiceExports
{
    public static async partial Task<HttpResponseMessage> Handle(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"Hello from C#: {request.Method} {request.RequestUri!.PathAndQuery} ({body.Length} bytes)\n"),
        };
    }
}
```

`ServiceExports` goes in the project's root namespace; set
`NetWasmHttpServiceNamespace` to use another. For each request the generated
handler builds an `HttpRequestMessage` with the method, URI, headers and body,
calls `Handle`, and streams the response back with its status, headers and
content. An exception that leaves `Handle` answers with `wasi:http`'s internal
error, which hosts send as status 500.

While it handles a request, the service can call other services with
`HttpClient` and use the other APIs its host grants; see
[host capabilities](host-capabilities.md).

## Run and host it

`dotnet run` does not serve a service: it prints how to publish and host it.
`dotnet publish` writes `<AssemblyName>.wasm` to the publish directory.

With Wasmtime:

```bash
wasmtime serve -Sp3 -Scli --addr 127.0.0.1:8080 publish/Greeter.wasm
```

With Spin, save this as `spin.toml` and run `spin up`:

```toml
spin_manifest_version = 2

[application]
name = "greeter"
version = "0.1.0"

[[trigger.http]]
route = "/..."
component = "service"

[component.service]
source = "publish/Greeter.wasm"
# List the origins HttpClient calls, for example:
# allowed_outbound_hosts = ["https://example.com"]
```

With wasmCloud, save this as `.wash/config.yaml` and run `wash dev`:

```yaml
build:
  command: dotnet publish -c Release -o publish
  component_path: publish/Greeter.wasm
dev:
  address: 127.0.0.1:8080
```

Spin and wasmCloud offer no `wasi:tls`, so a service that reaches `SslStream`
directly needs `<NetWasmTlsSupport>false</NetWasmTlsSupport>` there; `HttpClient`
requests go through the host's `wasi:http` and are not affected.

[NetWasm.Hosts](https://github.com/zion-sati/NetWasm.Hosts) runs the same
service on more hosts, each checked end to end against the released packages:
Cloudflare Workers, Deno, Netlify Edge Functions and Vercel Functions through
its NuGet package, with which `dotnet publish` also writes what each runs;
Akamai Functions as a Spin application; Kubernetes through SpinKube; Google Cloud Run, Azure Container
Apps and AWS Fargate through `wasmtime serve` in a container image; and AWS
Lambda, Azure Functions and Google Cloud Run functions through a `JsModule` in
a Node.js function.
[Running on cloud providers](cloud-providers.md) has the steps for each.

Node.js cannot serve WASI 0.3 HTTP components yet: jco and its preview3-shim do
not serve them correctly.

To call C# from a Node.js server or a serverless function instead, publish a
`JsModule` for the `node` host; see
[JavaScript interop](javascript-interop.md#choose-a-host).

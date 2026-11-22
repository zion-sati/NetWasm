# Running on cloud providers

A NetWasm HTTP service is a standard `wasi:http` component, the project the
`netwasm-serverless` template makes (see [HTTP services](http-services.md)).
Hosts that run WebAssembly components run it as it is; for the others, the
component is wrapped in what the provider runs: a Worker, a container or a
function. Each route below is checked end to end against NetWasm's released
packages in [NetWasm.Hosts](https://github.com/zion-sati/NetWasm.Hosts), whose
guides have the complete files.

| Provider | What runs | How |
| --- | --- | --- |
| Cloudflare Workers | The component inside a Worker | The `NetWasm.Hosts` package: `dotnet publish` also writes the Worker |
| Deno, on your own servers | The component behind `deno serve` | The `NetWasm.Hosts` package: `dotnet publish` also writes the Deno entry |
| Kubernetes, through SpinKube | The component, on the Spin containerd shim | `spin registry push`, then a `SpinApp` |
| Google Cloud Run, Azure Container Apps, AWS Fargate | The component, served by Wasmtime in a container | A small container image |
| AWS Lambda | C# called from a Node.js function | A `JsModule` project for the `node` host |
| Your own servers | The component | Wasmtime, Spin or wasmCloud, as in [HTTP services](http-services.md) |

Start from the template's service:

```sh
dotnet new install NetWasm.Templates
dotnet new netwasm-serverless -n Hello
cd Hello
```

## Cloudflare Workers

Add the `NetWasm.Hosts` package, whose version is the NetWasm version the
project builds with, and name the host:

```sh
dotnet add package NetWasm.Hosts --version <the project's NetWasm.Sdk version>
```

```xml
<PropertyGroup>
  <NetWasmServiceHosts>cloudflare</NetWasmServiceHosts>
</PropertyGroup>
```

`dotnet publish -c Release -o publish` then also writes `publish/cloudflare`:
the Worker's entry (`worker.mjs`), its configuration (`wrangler.json`), the
component transpiled to JavaScript and the host modules it imports.

```sh
cd publish/cloudflare
npx wrangler dev        # http://localhost:8787
npx wrangler deploy
```

Each request reaches the component's handler and its response streams back.
`HttpClient` requests go out through the Worker's `fetch`, and the Worker's
variables and secrets reach the component's environment beside its
`NetWasmEnvironmentVariable` items. The Worker's name and compatibility date
are MSBuild properties; see the package's
[README](https://github.com/zion-sati/NetWasm.Hosts/blob/main/src/NetWasm.Hosts/README.md).

## Deno

The same package publishes for Deno: name both hosts, or only `deno`.

```xml
<PropertyGroup>
  <NetWasmServiceHosts>cloudflare;deno</NetWasmServiceHosts>
</PropertyGroup>
```

`dotnet publish -c Release -o publish` then also writes `publish/deno`, whose
`main.mjs` compiles the component once at startup and serves it:

```sh
cd publish/deno
deno serve --allow-read --allow-net --allow-env main.mjs     # http://localhost:8000
```

The component's environment is its `NetWasmEnvironmentVariable` items, each
replaced by the process's variable of the same name when it has one; no other
variable of the process reaches it.

## Kubernetes, through SpinKube

[SpinKube](https://www.spinkube.dev) runs Spin applications on Kubernetes, and
the Spin containerd shim on its nodes runs the component as Spin does. Put a
Spin manifest beside the published component:

```toml
spin_manifest_version = 2

[application]
name = "hello"
version = "0.1.0"

[[trigger.http]]
route = "/..."
component = "service"

[component.service]
source = "Hello.wasm"
# List the origins HttpClient calls, for example:
# allowed_outbound_hosts = ["https://example.com"]
```

Push it to a registry the cluster pulls from and deploy it as a `SpinApp`:

```sh
dotnet publish -c Release -o publish
cp spin.toml publish/ && cd publish
spin registry push <registry>/hello:v1
```

```yaml
apiVersion: core.spinkube.dev/v1alpha1
kind: SpinApp
metadata:
  name: hello
spec:
  image: "<registry>/hello:v1"
  executor: containerd-shim-spin
  replicas: 1
```

`kubectl apply -f spinapp.yaml` starts it behind a service named `hello` on
port 80. SpinKube's [quickstart](https://www.spinkube.dev/docs/install/quickstart/)
installs the operator and the shim on a cluster; NetWasm.Hosts'
[guide](https://github.com/zion-sati/NetWasm.Hosts/blob/main/docs/spinkube.md)
has the versions it is checked with.

## Google Cloud Run, Azure Container Apps and AWS Fargate

These run containers, so the component runs in one with Wasmtime:

```dockerfile
# A NetWasm HTTP service in a container: Wasmtime serves the published component on $PORT (8080 unless the platform
# sets it, as Cloud Run does). Build it from the service project's directory after dotnet publish -c Release -o publish,
# with --build-arg SERVICE=<the project's assembly name>.
FROM debian:bookworm-slim@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc AS wasmtime
ARG WASMTIME=50.0.1
ARG WASMTIME_SHA256_X86_64=685466455dd42a14774a86c36c8f2e45cbb9bd6d92a850139315df60a75498a5
ARG WASMTIME_SHA256_AARCH64=d737e077416d78fbfb0a008229ec27d6a0fd5a4924874b8c38b293c850cf4094
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl xz-utils \
 && arch=$(uname -m) \
 && case "$arch" in x86_64) sha=$WASMTIME_SHA256_X86_64 ;; aarch64) sha=$WASMTIME_SHA256_AARCH64 ;; *) exit 1 ;; esac \
 && curl -fsSL -o /tmp/wasmtime.tar.xz "https://github.com/bytecodealliance/wasmtime/releases/download/v$WASMTIME/wasmtime-v$WASMTIME-$arch-linux.tar.xz" \
 && echo "$sha  /tmp/wasmtime.tar.xz" | sha256sum -c - \
 && tar -xJf /tmp/wasmtime.tar.xz -C /usr/local/bin --strip-components=1 "wasmtime-v$WASMTIME-$arch-linux/wasmtime"

FROM debian:bookworm-slim@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc
ARG SERVICE=Hello
COPY --from=wasmtime /usr/local/bin/wasmtime /usr/local/bin/wasmtime
COPY publish/${SERVICE}.wasm /srv/service.wasm
ENV PORT=8080
EXPOSE 8080
USER nobody
CMD ["sh", "-c", "exec wasmtime serve -C cache=n -Sp3 -Scli --addr 0.0.0.0:$PORT /srv/service.wasm"]
```

```sh
dotnet publish -c Release -o publish
docker build --build-arg SERVICE=Hello -t hello .
docker run --rm -p 8080:8080 hello     # curl http://localhost:8080/hello
```

Push the image to the provider's registry and create a service from it: a Cloud
Run service, a container app, or a Fargate task behind a load balancer. The
container listens on `$PORT`, which Cloud Run sets; on Container Apps and
Fargate, point the target port at 8080. The image checks the Wasmtime download
against its SHA-256 and runs as `nobody`; NetWasm.Hosts'
[guide](https://github.com/zion-sati/NetWasm.Hosts/blob/main/docs/containers.md)
keeps this Dockerfile current.

## AWS Lambda

Lambda runs Node.js functions, so C# reaches it as a `JsModule` for the `node`
host, whose exports a Node.js handler calls:

```xml
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <OutputType>JsModule</OutputType>
    <TargetFramework>net10.0-netwasm</TargetFramework>
    <NetWasmJavaScriptHosts>node</NetWasmJavaScriptHosts>
  </PropertyGroup>
</Project>
```

```csharp
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static partial class Exports
{
    [JSExport("handle")]
    public static Task<string> Handle(string method, string path, string body) =>
        Task.FromResult($"Hello from C# on Lambda: {method} {path} ({body.Length} bytes)\n");
}
```

```js
// index.mjs
import { open } from "./Functions.node.mjs";

// One instance per execution environment: opened while Lambda initializes the function, reused by every request.
const app = await open();

// A function URL or API Gateway HTTP API request (payload format 2.0).
export const handler = async event => {
  const body = event.body === undefined ? ""
    : event.isBase64Encoded ? Buffer.from(event.body, "base64").toString("utf8")
    : event.body;
  return {
    statusCode: 200,
    headers: { "content-type": "text/plain; charset=utf-8" },
    body: await app.handle(event.requestContext.http.method, event.rawPath, body),
  };
};
```

```sh
dotnet publish -c Release -o function
cp index.mjs function/ && cd function && zip -r ../function.zip . && cd ..
aws lambda create-function --function-name hello --runtime nodejs24.x --handler index.handler \
  --zip-file fileb://function.zip --role <an execution role's ARN>
```

The handler above takes a function URL's or an API Gateway HTTP API's request.
`HttpClient` in C# makes its requests with Node.js's `fetch`. NetWasm.Hosts'
[guide](https://github.com/zion-sati/NetWasm.Hosts/blob/main/docs/aws-lambda.md)
runs the same function locally in AWS's Lambda image first.

## What every host shares

- Each request gets a new instance of the component where NetWasm.Hosts runs
  it, released once the response has been sent; Wasmtime, Spin and SpinKube
  manage instances themselves.
- Workers, Deno, Spin, SpinKube and wasmCloud offer no `wasi:tls`, so a service that
  reaches `SslStream` directly needs
  `<NetWasmTlsSupport>false</NetWasmTlsSupport>` there; `HttpClient` requests go
  through the host's `wasi:http` and are not affected.
- A Worker has no file system, so the component sees no preopened directories.

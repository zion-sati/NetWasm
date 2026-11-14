# NetWasm.System.Net.Http

`NetWasm.System.Net.Http` supplies the `System.Net.Http` assembly for the
`netwasm0.1` profile: upstream .NET's `HttpClient`, `HttpRequestMessage`,
`HttpResponseMessage` and content types over WASI 0.3's `wasi:http`.

The package carries the `NetWasm,Version=v0.1` asset and preserves the public
`System.Net.Http` assembly identity. It does not add a JavaScript HTTP fallback
or replace desktop .NET's built-in `System.Net.Http` reference.

## Outgoing requests

`HttpClient` sends requests through the host's `wasi:http` client. Wasmtime,
Spin, wasmCloud and NetWasm's Node.js and browser hosts provide it; a command
run with `wasmtime run` needs `-Shttp`.

## HTTP services

A project with `<OutputType>Component</OutputType>` and
`<NetWasmWorld>service</NetWasmWorld>` that references this package is a
`wasi:http` service. Implement one method in the project's root namespace (or
in `NetWasmHttpServiceNamespace`):

```csharp
public static partial class ServiceExports
{
    public static async partial Task<HttpResponseMessage> Handle(HttpRequestMessage request)
    {
        var body = await request.Content!.ReadAsStringAsync();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{request.Method} {request.RequestUri!.PathAndQuery} [{body}]"),
        };
    }
}
```

The package generates the `wasi:http` handler that turns each request into an
`HttpRequestMessage`, calls `Handle`, and streams the `HttpResponseMessage`
back with its status, headers and content. An exception from `Handle` answers
with `wasi:http`'s internal error, which hosts send as a 500. `Handle` can use
`HttpClient` to call other services.

`dotnet publish` writes one component that `wasmtime serve -Sp3 -Scli`, Spin
and wasmCloud run unchanged. Node.js cannot serve WASI 0.3 HTTP components
yet.

# NetWasmService

A C# HTTP service: a WASI 0.3 component whose `ServiceExports.Handle` answers
each `wasi:http` request. Publish it:

```bash
dotnet publish -c Release -o publish
```

`publish/NetWasmService.wasm` runs unchanged in any `wasi:http` 0.3 host.
`dotnet run` does not serve it.

## Wasmtime

```bash
wasmtime serve -Sp3 -Scli --addr 127.0.0.1:8080 publish/NetWasmService.wasm
curl http://127.0.0.1:8080/hello
```

## Spin

Save this as `spin.toml`, then run `spin up`:

```toml
spin_manifest_version = 2

[application]
name = "netwasm-service"
version = "0.1.0"

[[trigger.http]]
route = "/..."
component = "service"

[component.service]
source = "publish/NetWasmService.wasm"
# List the origins HttpClient calls, for example:
# allowed_outbound_hosts = ["https://example.com"]
```

## wasmCloud

Save this as `.wash/config.yaml`, then run `wash dev`:

```yaml
build:
  command: dotnet publish -c Release -o publish
  component_path: publish/NetWasmService.wasm
dev:
  address: 127.0.0.1:8080
```

Node.js cannot serve WASI 0.3 HTTP components yet.
